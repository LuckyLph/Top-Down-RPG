using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// What a chunk holds in the terms of its written hash (Docs/WorldGeneration.md: Architecture: Writing): a bit per
/// ground material of the terrain style's layer order for each of its cells, its raised ground, the spawn points under
/// its generated root (prefab, cell and id, in order), the props under its generated props' root (prefab and
/// position to the pixel, in order) and the region skies its grid's <see cref="ChunkSky"/> holds (Docs/Lighting.md,
/// Phase 8). It is read either from a composition (what the writer writes there) or from a
/// chunk's grid (what its scene holds now), and both give the same <see cref="Hash"/> for a chunk just written. Empty
/// layers, the walls and every object outside the generated roots count for nothing, so the layers a chunk's
/// <see cref="AreaTerrain"/> keeps, a layer sync adds or a prop placed by hand under the scene's own Props root change
/// no hash, while paint on any ground layer, empty or not, on the raised ground, or a change to a generated spawn point
/// or prop does.
/// </summary>
internal sealed class ChunkContent
{
    /// <summary>
    /// A generated spawn point as the hash reads it.
    /// </summary>
    private readonly struct SpawnPoint
    {
        /// <summary>
        /// A spawn point of the prefab named <paramref name="prefab"/> at <paramref name="cell"/> with <paramref name="id"/>.
        /// </summary>
        public SpawnPoint(string prefab, Vector2Int cell, string id)
        {
            Prefab = prefab;
            Cell = cell;
            Id = id;
        }

        public string Prefab { get; }
        public Vector2Int Cell { get; }
        public string Id { get; }
    }

    /// <summary>
    /// A generated prop as the hash reads it: its prefab's name and its root's place on the grid, in pixels.
    /// </summary>
    private readonly struct PropPlace
    {
        /// <summary>
        /// A prop of the prefab named <paramref name="prefab"/> whose root stands <paramref name="pixels"/> from the
        /// grid's origin.
        /// </summary>
        public PropPlace(string prefab, Vector2Int pixels)
        {
            Prefab = prefab;
            Pixels = pixels;
        }

        public string Prefab { get; }
        public Vector2Int Pixels { get; }
    }

    private const ulong Offset = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    private readonly IReadOnlyList<TerrainMaterial> layerOrder;
    private readonly RectInt bounds;
    private readonly uint[] masks = new uint[ChunkCoords.Size * ChunkCoords.Size];
    private readonly byte[] raised = new byte[ChunkCoords.Size * ChunkCoords.Size];
    private readonly List<SpawnPoint> spawnPoints = new();
    private readonly List<PropPlace> props = new();
    private readonly List<string> skies = new();
    private byte[] skyWeights = System.Array.Empty<byte>();
    private readonly List<string> unlisted = new();

    /// <summary>
    /// An empty chunk at <paramref name="coords"/> over the materials of <paramref name="order"/>.
    /// </summary>
    private ChunkContent(Vector2Int coords, IReadOnlyList<TerrainMaterial> order)
    {
        layerOrder = order;
        bounds = ChunkCoords.Bounds(coords);
    }

    /// <summary>
    /// What the chunk at <paramref name="coords"/> of <paramref name="composition"/> holds: its cells' materials and
    /// raised ground, and its mobs and props in the composition's order, each prop where the writer stands its root
    /// (<see cref="RootOf"/>).
    /// </summary>
    public static ChunkContent Of(WorldComposition composition, Vector2Int coords)
    {
        ChunkContent content = new(coords, composition.LayerOrder);
        RectInt bounds = content.bounds;
        int width = composition.Size.x;
        for (int y = 0; y < bounds.height; y++)
        {
            int from = (bounds.yMin + y) * width + bounds.xMin;
            System.Array.Copy(composition.materialBits, from, content.masks, y * bounds.width, bounds.width);
            System.Array.Copy(composition.raised, from, content.raised, y * bounds.width, bounds.width);
        }

        foreach (WorldComposition.Mob mob in composition.Mobs)
        {
            if (bounds.Contains(mob.Cell))
            {
                content.spawnPoints.Add(new SpawnPoint(mob.Prefab != null ? mob.Prefab.name : string.Empty, mob.Cell, mob.Id));
            }
        }

        foreach (WorldComposition.Prop prop in composition.Props)
        {
            if (prop.Chunk == coords)
            {
                content.props.Add(new PropPlace(prop.Prefab != null ? prop.Prefab.name : string.Empty, Pixels(RootOf(prop))));
            }
        }

        WorldComposition.Chunk chunk = composition.ChunkAt(coords);
        if (chunk != null)
        {
            content.ReadSkies(chunk.Skies, chunk.SkyWeights);
        }

        return content;
    }

    /// <summary>
    /// Where the writer stands <paramref name="prop"/>'s root, in cells from the grid's origin: a blocking prop's
    /// footprint centred on its cells and standing on the bottom edge of the lowest
    /// (<see cref="PropGridSnapping.RootOnCells"/> on cells of one unit), a walk-through prop at its point in its cell,
    /// to the pixel.
    /// </summary>
    public static Vector2 RootOf(WorldComposition.Prop prop)
    {
        if (prop.Blocks && PropGridSnapping.TryGetFootprint(prop.Prefab, out Rect footprint))
        {
            return new Vector2(prop.Cell.x + prop.Covered.x * 0.5f - footprint.center.x, prop.Cell.y - footprint.yMin);
        }

        return PropGridSnapping.SnapToPixels(prop.Cell + prop.Offset);
    }

    /// <summary>
    /// What the chunk grid <paramref name="grid"/>, the chunk at <paramref name="coords"/>, holds now over the materials
    /// of <paramref name="order"/>: the cells of its terrain renderer's ground layers and raised tilemap inside its
    /// bounds, the spawn points under its generated root (<see cref="WorldGenerationWriter.GeneratedMobSpawnsName"/>)
    /// and the props under its generated props' root (<see cref="WorldGenerationWriter.GeneratedPropsName"/>), each in
    /// hierarchy order. A painted layer of a material the order does not list is named, so it changes the hash.
    /// </summary>
    public static ChunkContent Read(Grid grid, Vector2Int coords, IReadOnlyList<TerrainMaterial> order)
    {
        ChunkContent content = new(coords, order);
        RectInt bounds = content.bounds;
        BoundsInt block = new(bounds.xMin, bounds.yMin, 0, bounds.width, bounds.height, 1);
        if (grid.TryGetComponent(out TerrainBlendRenderer renderer))
        {
            foreach (TerrainBlendRenderer.Layer layer in renderer.Layers)
            {
                if (layer != null && layer.Tilemap != null && layer.Material != null)
                {
                    content.ReadLayer(layer.Tilemap.GetTilesBlock(block), layer.Material);
                }
            }

            if (renderer.RaisedTilemap != null)
            {
                TileBase[] tiles = renderer.RaisedTilemap.GetTilesBlock(block);
                for (int i = 0; i < tiles.Length; i++)
                {
                    content.raised[i] = RaisedGround.KindOf(tiles[i]);
                }
            }
        }

        Transform root = grid.transform.Find(WorldGenerationWriter.GeneratedMobSpawnsName);
        if (root != null)
        {
            foreach (MobSpawnPoint point in root.GetComponentsInChildren<MobSpawnPoint>(true))
            {
                Vector3Int cell = grid.WorldToCell(point.transform.position);
                string id = point.TryGetComponent(out PersistentId persistentId) ? persistentId.Id : string.Empty;
                content.spawnPoints.Add(new SpawnPoint(point.MobPrefab != null ? point.MobPrefab.name : string.Empty, new Vector2Int(cell.x, cell.y), id ?? string.Empty));
            }
        }

        Transform propsRoot = grid.transform.Find(WorldGenerationWriter.GeneratedPropsName);
        if (propsRoot != null)
        {
            Vector3 origin = grid.CellToWorld(Vector3Int.zero);
            foreach (Transform prop in propsRoot)
            {
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(prop.gameObject);
                Vector2 local = new((prop.position.x - origin.x) / grid.cellSize.x, (prop.position.y - origin.y) / grid.cellSize.y);
                content.props.Add(new PropPlace(source != null ? source.name : prop.name, Pixels(local)));
            }
        }

        if (grid.TryGetComponent(out ChunkSky sky))
        {
            content.ReadSkies(sky.Skies, sky.Weights);
        }

        return content;
    }

    /// <summary>
    /// A 64-bit FNV-1a hash, in hex: for each material of the layer order with cells in the chunk, its name and the
    /// local index of each such cell in row order; then each raised cell's index and kind; then each spawn point's
    /// prefab name, cell and id; then, when it has any, each prop's prefab name and place in pixels, so a chunk without
    /// props hashes as it did before props were written; then, when it has any, each region sky's name and its weights
    /// (Docs/Lighting.md, Phase 8), so a chunk without skies hashes as it did before skies were written; then the names
    /// of painted materials the order does not list.
    /// </summary>
    public string Hash()
    {
        uint own = 0u;
        foreach (uint mask in masks)
        {
            own |= mask;
        }

        ulong hash = Offset;
        for (int i = 0; i < layerOrder.Count; i++)
        {
            uint bit = 1u << i;
            if ((own & bit) == 0)
            {
                continue;
            }

            hash = Mix(hash, layerOrder[i].name);
            for (int cell = 0; cell < masks.Length; cell++)
            {
                if ((masks[cell] & bit) != 0)
                {
                    hash = Mix(hash, cell);
                }
            }
        }

        hash = Mix(hash, "raised");
        for (int cell = 0; cell < raised.Length; cell++)
        {
            if (raised[cell] != RaisedGround.Level)
            {
                hash = Mix(Mix(hash, cell), raised[cell]);
            }
        }

        hash = Mix(hash, "mobs");
        foreach (SpawnPoint point in spawnPoints)
        {
            hash = Mix(Mix(Mix(Mix(hash, point.Prefab), point.Cell.x), point.Cell.y), point.Id);
        }

        if (props.Count > 0)
        {
            hash = Mix(hash, "props");
            foreach (PropPlace prop in props)
            {
                hash = Mix(Mix(Mix(hash, prop.Prefab), prop.Pixels.x), prop.Pixels.y);
            }
        }

        if (skies.Count > 0)
        {
            hash = Mix(hash, "skies");
            foreach (string sky in skies)
            {
                hash = Mix(hash, sky);
            }

            foreach (byte weight in skyWeights)
            {
                hash = (hash ^ weight) * Prime;
            }
        }

        foreach (string material in unlisted)
        {
            hash = Mix(Mix(hash, "unlisted"), material);
        }

        return hash.ToString("x16");
    }

    /// <summary>
    /// The hash of a chunk holding nothing: what a chunk the writer has not written yet must hold to be written.
    /// </summary>
    public static string EmptyHash(IReadOnlyList<TerrainMaterial> order)
    {
        return new ChunkContent(Vector2Int.zero, order).Hash();
    }

    /// <summary>
    /// Sets <paramref name="material"/>'s bit on every cell of <paramref name="tiles"/> (the chunk's block, row order)
    /// holding a tile, or names the material when the order does not list it and it holds any.
    /// </summary>
    private void ReadLayer(TileBase[] tiles, TerrainMaterial material)
    {
        int index = -1;
        for (int i = 0; i < layerOrder.Count && index < 0; i++)
        {
            index = layerOrder[i] == material ? i : -1;
        }

        for (int cell = 0; cell < tiles.Length; cell++)
        {
            if (tiles[cell] == null)
            {
                continue;
            }

            if (index < 0)
            {
                unlisted.Add(material.name);
                return;
            }

            masks[cell] |= 1u << index;
        }
    }

    /// <summary>
    /// Records the names of <paramref name="regionSkies"/> and a copy of their <paramref name="weights"/>.
    /// </summary>
    private void ReadSkies(IReadOnlyList<RegionSky> regionSkies, IReadOnlyList<byte> weights)
    {
        foreach (RegionSky sky in regionSkies)
        {
            skies.Add(sky != null ? sky.name : string.Empty);
        }

        skyWeights = new byte[weights.Count];
        for (int i = 0; i < weights.Count; i++)
        {
            skyWeights[i] = weights[i];
        }
    }

    /// <summary>
    /// <paramref name="cells"/>, a place in cells, in whole pixels.
    /// </summary>
    private static Vector2Int Pixels(Vector2 cells)
    {
        return new Vector2Int(Mathf.RoundToInt(cells.x * PropGridSnapping.PixelsPerUnit), Mathf.RoundToInt(cells.y * PropGridSnapping.PixelsPerUnit));
    }

    /// <summary>
    /// <paramref name="hash"/> with <paramref name="text"/>'s characters mixed in.
    /// </summary>
    private static ulong Mix(ulong hash, string text)
    {
        foreach (char character in text)
        {
            hash = (hash ^ character) * Prime;
        }

        return (hash ^ 0xff) * Prime;
    }

    /// <summary>
    /// <paramref name="hash"/> with <paramref name="value"/>'s four bytes mixed in.
    /// </summary>
    private static ulong Mix(ulong hash, int value)
    {
        unchecked
        {
            for (int shift = 0; shift < 32; shift += 8)
            {
                hash = (hash ^ (byte)(value >> shift)) * Prime;
            }
        }

        return hash;
    }
}
