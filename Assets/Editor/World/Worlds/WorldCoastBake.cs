using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Bakes each chunk's coast map from the whole world's ground (Docs/WorldStreaming.md: Terrain), so a chunk renderer's
/// waves read the same distances to the coast as one renderer over the whole world would. The waves read a distance
/// field reaching 32 cells from the coast, more than any skirt, and building it costs about 22 µs per cell, so it is
/// baked in the editor rather than built at runtime. A chunk's map covers its control map
/// (<see cref="TerrainBlendRenderer.ChunkControlArea"/>) and is worked out by <see cref="TerrainCoastMap"/> over a
/// window of the world's cells <see cref="Reach"/> cells around the chunk, clipped to the world's ground and its
/// margin as a whole renderer's control map would be, so the cells inside the chunk get exactly what that renderer's
/// map gives them. A chunk that shows no material with waves bakes nothing.
/// </summary>
public static class WorldCoastBake
{
    public const int Reach = 40;

    /// <summary>
    /// A world's ground as one grid of cells: for each cell, a bit per material filled there.
    /// </summary>
    public sealed class WorldCells
    {
        private readonly List<TerrainMaterial> materials = new();
        private readonly Dictionary<TerrainMaterial, int> indices = new();
        private readonly RectInt area;
        private readonly ulong[] bits;
        private bool any;
        private Vector2Int min;
        private Vector2Int max;

        /// <summary>
        /// An empty grid over the world-local cells <paramref name="cells"/>.
        /// </summary>
        public WorldCells(RectInt cells)
        {
            area = cells;
            bits = new ulong[cells.width * cells.height];
        }

        public RectInt Area => area;
        public int MaterialCount => materials.Count;

        /// <summary>
        /// The filled cells' bounds, grown by the control map's margin, as a renderer over the whole world would
        /// cover them; zero size while nothing is filled.
        /// </summary>
        public RectInt Covered => any
            ? new RectInt(min.x - TerrainControlMap.Margin, min.y - TerrainControlMap.Margin, max.x - min.x + 1 + 2 * TerrainControlMap.Margin, max.y - min.y + 1 + 2 * TerrainControlMap.Margin)
            : new RectInt(0, 0, 0, 0);

        /// <summary>
        /// Adds every filled cell of <paramref name="tilemap"/> inside <paramref name="cells"/> (and the grid) as
        /// <paramref name="material"/>.
        /// </summary>
        public void Add(Tilemap tilemap, TerrainMaterial material, RectInt cells)
        {
            if (tilemap == null || material == null)
            {
                return;
            }

            if (!indices.TryGetValue(material, out int index))
            {
                if (materials.Count >= 64)
                {
                    Debug.LogError($"The coast bake counts at most 64 materials; {material.name} is left out.", material);
                    return;
                }

                index = materials.Count;
                materials.Add(material);
                indices.Add(material, index);
            }

            BoundsInt tilemapBounds = tilemap.cellBounds;
            RectInt read = Intersect(Intersect(new RectInt(tilemapBounds.xMin, tilemapBounds.yMin, tilemapBounds.size.x, tilemapBounds.size.y), cells), area);
            if (read.width <= 0 || read.height <= 0)
            {
                return;
            }

            TileBase[] tiles = tilemap.GetTilesBlock(new BoundsInt(read.xMin, read.yMin, 0, read.width, read.height, 1));
            ulong bit = 1ul << index;
            for (int i = 0; i < tiles.Length; i++)
            {
                if (tiles[i] == null)
                {
                    continue;
                }

                Vector2Int cell = new(read.xMin + i % read.width, read.yMin + i / read.width);
                bits[(cell.y - area.yMin) * area.width + cell.x - area.xMin] |= bit;
                min = any ? Vector2Int.Min(min, cell) : cell;
                max = any ? Vector2Int.Max(max, cell) : cell;
                any = true;
            }
        }

        /// <summary>
        /// The material bits of <paramref name="cell"/>; 0 outside the grid.
        /// </summary>
        public ulong BitsAt(Vector2Int cell)
        {
            return area.Contains(cell) ? bits[(cell.y - area.yMin) * area.width + cell.x - area.xMin] : 0ul;
        }

        /// <summary>
        /// The material of bit <paramref name="index"/>.
        /// </summary>
        public TerrainMaterial MaterialOf(int index)
        {
            return materials[index];
        }

        /// <summary>
        /// The materials filled in <paramref name="cells"/>.
        /// </summary>
        public HashSet<TerrainMaterial> MaterialsIn(RectInt cells)
        {
            ulong seen = 0ul;
            RectInt read = Intersect(cells, area);
            for (int y = read.yMin; y < read.yMax; y++)
            {
                for (int x = read.xMin; x < read.xMax; x++)
                {
                    seen |= bits[(y - area.yMin) * area.width + x - area.xMin];
                }
            }

            HashSet<TerrainMaterial> found = new();
            for (int i = 0; i < materials.Count; i++)
            {
                if ((seen & (1ul << i)) != 0)
                {
                    found.Add(materials[i]);
                }
            }

            return found;
        }
    }

    /// <summary>
    /// The baked coast map of the chunk owning <paramref name="chunkCells"/>, whose renderer draws
    /// <paramref name="chunkMaterials"/> in <paramref name="style"/>'s order: the encoded distances (as
    /// <see cref="TerrainCoastMap"/> encodes them) over its control map, <paramref name="width"/> x
    /// <paramref name="height"/> texels, row by row from the bottom; null when neither its layers nor the ground within
    /// its skirt show a material with waves.
    /// </summary>
    public static ushort[] BakeChunk(WorldCells world, TerrainStyle style, IEnumerable<TerrainMaterial> chunkMaterials, RectInt chunkCells, out int width, out int height)
    {
        RectInt control = TerrainBlendRenderer.ChunkControlArea(chunkCells, WorldChunk.SkirtCells);
        width = control.width * TerrainCoastMap.TexelsPerCell;
        height = control.height * TerrainCoastMap.TexelsPerCell;
        HashSet<TerrainMaterial> shown = world.MaterialsIn(Grow(chunkCells, WorldChunk.SkirtCells));
        if (chunkMaterials != null)
        {
            shown.UnionWith(chunkMaterials);
        }

        TerrainMaterial waves = null;
        int wavesOrder = int.MaxValue;
        foreach (TerrainMaterial material in shown)
        {
            int order = style.LayerOrderIndexOf(material);
            if (material != null && material.HasWaves && order < wavesOrder)
            {
                waves = material;
                wavesOrder = order;
            }
        }

        RectInt window = Intersect(Grow(chunkCells, Reach), world.Covered);
        if (waves == null || window.width <= 0 || window.height <= 0)
        {
            return null;
        }

        ulong counted = 0ul;
        for (int i = 0; i < world.MaterialCount; i++)
        {
            TerrainMaterial material = world.MaterialOf(i);
            if (style.LayerOrderIndexOf(material) >= wavesOrder && (material == waves || !material.Bridge))
            {
                counted |= 1ul << i;
            }
        }

        bool[] sea = new bool[window.width * window.height];
        for (int y = 0; y < window.height; y++)
        {
            for (int x = 0; x < window.width; x++)
            {
                ulong bits = world.BitsAt(new Vector2Int(window.xMin + x, window.yMin + y));
                sea[y * window.width + x] = bits == 0ul || (bits & counted) != 0ul;
            }
        }

        TerrainCoastMap map = new("Coast bake");
        try
        {
            map.BuildFromCells(sea, window.width, window.height);
            ushort[] encoded = new ushort[width * height];
            int offsetX = (control.xMin - window.xMin) * TerrainCoastMap.TexelsPerCell;
            int offsetY = (control.yMin - window.yMin) * TerrainCoastMap.TexelsPerCell;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    encoded[y * width + x] = map.EncodedAt(offsetX + x, offsetY + y);
                }
            }

            return encoded;
        }
        finally
        {
            map.Dispose();
        }
    }

    /// <summary>
    /// A bilinear, clamped, linear R16 texture of <paramref name="encoded"/>, as the renderer builds its own coast map;
    /// the caller destroys it.
    /// </summary>
    public static Texture2D ToTexture(ushort[] encoded, int width, int height, string textureName)
    {
        Texture2D texture = new(width, height, TextureFormat.R16, false, true)
        {
            name = textureName,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixelData(encoded, 0);
        texture.Apply(false, false);
        return texture;
    }

    /// <summary>
    /// <paramref name="cells"/> grown by <paramref name="by"/> cells on every side.
    /// </summary>
    private static RectInt Grow(RectInt cells, int by)
    {
        return new RectInt(cells.xMin - by, cells.yMin - by, cells.width + 2 * by, cells.height + 2 * by);
    }

    /// <summary>
    /// The cells <paramref name="a"/> and <paramref name="b"/> share; zero size when none.
    /// </summary>
    private static RectInt Intersect(RectInt a, RectInt b)
    {
        int xMin = Mathf.Max(a.xMin, b.xMin);
        int yMin = Mathf.Max(a.yMin, b.yMin);
        int xMax = Mathf.Min(a.xMax, b.xMax);
        int yMax = Mathf.Min(a.yMax, b.yMax);
        return new RectInt(xMin, yMin, Mathf.Max(0, xMax - xMin), Mathf.Max(0, yMax - yMin));
    }
}
