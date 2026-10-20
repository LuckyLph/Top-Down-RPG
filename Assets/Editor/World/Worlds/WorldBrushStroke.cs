using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// A brush stroke on a chunk of a seamless world, split by chunk (Docs/WorldStreaming.md: Authoring). Every chunk's
/// grid sits at its world's origin, so a stroke's cells are world cells whichever chunk's tilemap was the paint target,
/// and each cell goes to the chunk that owns it, onto that chunk's tilemap of the same kind as the target: the layer
/// drawing the target's ground material, added where missing (also to each open neighbour whose skirt the painted
/// cells reach), the walls of the same rule tile (a copy of the target's walls where missing), or the raised tilemap
/// (set up where missing). A tilemap of any other kind is written only inside its own chunk. Cells of a chunk whose
/// scene is not open, or outside the world's chunks, are skipped with a warning. Every write, added layer and added
/// tilemap joins the current undo group, so a stroke across scenes is one undo step. A flood fill spreads across the
/// open chunks' cells and stops at the border of a chunk that is not open.
/// </summary>
public static class WorldBrushStroke
{
    private const string UndoName = "Paint across chunks";

    /// <summary>
    /// What a paint target is to its chunk.
    /// </summary>
    public enum Kind
    {
        Ground,
        Walls,
        Raised,
        Other
    }

    /// <summary>
    /// A paint target on a chunk's grid: the chunk, the kind of tilemap, the tilemap itself, the ground material its
    /// layer draws and its dual-grid module, when it has them.
    /// </summary>
    public readonly struct Target
    {
        /// <summary>
        /// Describes the target <paramref name="tilemap"/> of kind <paramref name="kind"/> on <paramref name="chunk"/>.
        /// </summary>
        public Target(WorldChunk chunk, Kind kind, Tilemap tilemap, TerrainMaterial material, DualGridTilemapModule module)
        {
            Chunk = chunk;
            TargetKind = kind;
            Tilemap = tilemap;
            Material = material;
            Module = module;
        }

        public WorldChunk Chunk { get; }
        public Kind TargetKind { get; }
        public Tilemap Tilemap { get; }
        public TerrainMaterial Material { get; }
        public DualGridTilemapModule Module { get; }
        public WorldDefinition World => Chunk.World;

        /// <summary>
        /// Whether a stroke writes the module's data tile into every cell (a dual-grid tilemap) rather than the brush's
        /// tiles.
        /// </summary>
        public bool WritesEveryCell => Module != null;
    }

    /// <summary>
    /// The chunk target of <paramref name="brushTarget"/>: false for anything but a tilemap under the grid of a chunk
    /// that names its world, so painting a standalone area is left to the brush as it was.
    /// </summary>
    public static bool TryResolve(GameObject brushTarget, out Target target)
    {
        target = default;
        if (brushTarget == null || !brushTarget.TryGetComponent(out Tilemap tilemap))
        {
            return false;
        }

        WorldChunk chunk = tilemap.GetComponentInParent<WorldChunk>(true);
        if (chunk == null || chunk.World == null)
        {
            return false;
        }

        brushTarget.TryGetComponent(out DualGridTilemapModule module);
        TerrainBlendRenderer renderer = chunk.GetComponent<TerrainBlendRenderer>();
        if (renderer != null && renderer.RaisedTilemap == tilemap)
        {
            target = new Target(chunk, Kind.Raised, tilemap, null, null);
        }
        else if (renderer != null && TerrainBlendRenderer.FindLayerDrawing(tilemap, out int layer) == renderer && renderer.Layers[layer].Material != null)
        {
            target = new Target(chunk, Kind.Ground, tilemap, renderer.Layers[layer].Material, module);
        }
        else if (module != null)
        {
            target = new Target(chunk, Kind.Walls, tilemap, null, module);
        }
        else
        {
            target = new Target(chunk, Kind.Other, tilemap, null, null);
        }

        return true;
    }

    /// <summary>
    /// The enabled chunks of <paramref name="world"/> in the open scenes by their coordinates, with
    /// <paramref name="including"/> among them whatever its state.
    /// </summary>
    public static Dictionary<Vector2Int, WorldChunk> OpenChunks(WorldDefinition world, WorldChunk including = null)
    {
        Dictionary<Vector2Int, WorldChunk> open = new();
        foreach (WorldChunk chunk in Object.FindObjectsByType<WorldChunk>(FindObjectsInactive.Exclude))
        {
            if (chunk.isActiveAndEnabled && chunk.World == world && !open.ContainsKey(chunk.Coords) && !EditorUtility.IsPersistent(chunk))
            {
                open.Add(chunk.Coords, chunk);
            }
        }

        if (including != null)
        {
            open[including.Coords] = including;
        }

        return open;
    }

    /// <summary>
    /// Writes <paramref name="changes"/> (world cells and their tiles, null erasing) as one stroke on
    /// <paramref name="target"/>'s world, each cell into the chunk that owns it. Returns how many cells it skipped,
    /// after a warning naming why.
    /// </summary>
    public static int Apply(Target target, IReadOnlyList<TileChangeData> changes)
    {
        if (changes == null || changes.Count == 0 || target.Chunk == null)
        {
            return 0;
        }

        Dictionary<Vector2Int, WorldChunk> open = OpenChunks(target.World, target.Chunk);
        Dictionary<Vector2Int, List<TileChangeData>> parts = SplitByChunk(changes);
        int skipped = 0;
        List<string> reasons = new();
        foreach (KeyValuePair<Vector2Int, List<TileChangeData>> part in parts)
        {
            string reason = SkipReason(target, open, part.Key);
            if (reason == null && !WritePart(target, open[part.Key], part.Value, open))
            {
                reason = $"chunk {part.Key} could not take the {target.TargetKind} cells (see the error above)";
            }

            if (reason != null)
            {
                skipped += part.Value.Count;
                reasons.Add($"{part.Value.Count} cells of {reason}");
            }
        }

        if (reasons.Count > 0)
        {
            string hint = reasons.Exists(reason => reason.EndsWith("is not open")) ? " Open those chunks with the World Editor to paint them." : string.Empty;
            Debug.LogWarning($"Terrain Brush on {target.Tilemap.name} of {target.Chunk.gameObject.scene.name}: skipped {string.Join("; ", reasons)}.{hint}", target.Chunk);
        }

        return skipped;
    }

    /// <summary>
    /// Flood fills <paramref name="fill"/> from <paramref name="start"/> across the cells of <paramref name="target"/>'s
    /// kind holding the same tile as the start cell, 4-connected, over the open chunks of its world (only its own chunk
    /// for a tilemap of another kind), and writes them as one stroke (<see cref="Apply"/>). A chunk with no tilemap of
    /// the kind counts as empty. Returns how many cells it filled; warns when the fill reached the border of a chunk of
    /// the world that is not open.
    /// </summary>
    public static int FloodFill(Target target, Vector3Int start, TileBase fill)
    {
        if (target.Chunk == null || fill == null)
        {
            return 0;
        }

        Dictionary<Vector2Int, WorldChunk> open = OpenChunks(target.World, target.Chunk);
        Dictionary<Vector2Int, Tilemap> sources = new();
        HashSet<Vector2Int> closedBorders = new();
        Vector2Int first = new(start.x, start.y);
        if (!TryReadFillCell(target, open, sources, first, start.z, out TileBase from) || from == fill)
        {
            return 0;
        }

        List<TileChangeData> changes = new();
        HashSet<Vector2Int> visited = new() { first };
        Queue<Vector2Int> frontier = new();
        frontier.Enqueue(first);
        Vector2Int[] steps = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        while (frontier.Count > 0)
        {
            Vector2Int cell = frontier.Dequeue();
            changes.Add(new TileChangeData { position = new Vector3Int(cell.x, cell.y, start.z), tile = fill, color = Color.white, transform = Matrix4x4.identity });
            foreach (Vector2Int step in steps)
            {
                Vector2Int next = cell + step;
                if (!visited.Add(next))
                {
                    continue;
                }

                if (!TryReadFillCell(target, open, sources, next, start.z, out TileBase tile))
                {
                    Vector2Int chunk = ChunkCoords.ChunkOf(next);
                    if (target.World.HasChunk(chunk) && !open.ContainsKey(chunk))
                    {
                        closedBorders.Add(chunk);
                    }

                    continue;
                }

                if (tile == from)
                {
                    frontier.Enqueue(next);
                }
            }
        }

        if (closedBorders.Count > 0)
        {
            Debug.LogWarning($"Terrain Brush: the flood fill on {target.Tilemap.name} stopped at the border of {string.Join(", ", closedBorders)}, whose scenes are not open.", target.Chunk);
        }

        Apply(target, changes);
        return changes.Count;
    }

    /// <summary>
    /// The layer of <paramref name="chunk"/>'s terrain renderer drawing <paramref name="material"/>, or null.
    /// </summary>
    public static Tilemap LayerOf(WorldChunk chunk, TerrainMaterial material)
    {
        if (chunk == null || !chunk.TryGetComponent(out TerrainBlendRenderer renderer))
        {
            return null;
        }

        for (int i = 0; i < renderer.Layers.Count; i++)
        {
            TerrainBlendRenderer.Layer layer = renderer.Layers[i];
            if (layer != null && layer.Material == material && layer.Tilemap != null)
            {
                return layer.Tilemap;
            }
        }

        return null;
    }

    /// <summary>
    /// The wall module under <paramref name="chunk"/>'s grid drawing the same rule tile as <paramref name="like"/>, or
    /// null.
    /// </summary>
    public static DualGridTilemapModule WallsLike(WorldChunk chunk, DualGridTilemapModule like)
    {
        foreach (DualGridTilemapModule module in chunk.GetComponentsInChildren<DualGridTilemapModule>(true))
        {
            if (module.RenderTile == like.RenderTile)
            {
                return module;
            }
        }

        return null;
    }

    /// <summary>
    /// Why the cells of the chunk at <paramref name="coords"/> cannot be painted, or null when they can.
    /// </summary>
    private static string SkipReason(Target target, Dictionary<Vector2Int, WorldChunk> open, Vector2Int coords)
    {
        if (!target.World.HasChunk(coords))
        {
            return $"chunk {coords}, which the world {target.World.name} does not have";
        }

        if (!open.ContainsKey(coords))
        {
            return $"chunk {coords} ({WorldAuthoring.ChunkSceneName(target.World, coords)}), whose scene is not open";
        }

        if (target.TargetKind == Kind.Other && open[coords] != target.Chunk)
        {
            return $"chunk {coords}: only ground layers, walls and raised ground paint across chunk borders";
        }

        return null;
    }

    /// <summary>
    /// Writes <paramref name="part"/>, the cells of one chunk, into <paramref name="chunk"/>'s tilemap of the target's
    /// kind, creating it when the part paints and it is missing. False when it could not be created.
    /// </summary>
    private static bool WritePart(Target target, WorldChunk chunk, List<TileChangeData> part, Dictionary<Vector2Int, WorldChunk> open)
    {
        bool paints = part.Exists(change => change.tile != null);
        Grid grid = chunk.GetComponent<Grid>();
        Tilemap destination;
        DualGridTilemapModule module = null;
        switch (target.TargetKind)
        {
            case Kind.Ground:
                destination = LayerOf(chunk, target.Material);
                if (destination == null)
                {
                    if (!paints)
                    {
                        return true;
                    }

                    destination = GroundLayerMigration.AddLayer(grid, target.Material, false);
                    if (destination == null)
                    {
                        return false;
                    }

                    chunk.RefreshLinks();
                }

                destination.TryGetComponent(out module);
                break;
            case Kind.Walls:
                module = WallsLike(chunk, target.Module);
                if (module == null)
                {
                    if (!paints)
                    {
                        return true;
                    }

                    module = CopyWalls(target.Module, chunk);
                }

                destination = module.DataTilemap;
                break;
            case Kind.Raised:
                TerrainBlendRenderer renderer = chunk.GetComponent<TerrainBlendRenderer>();
                destination = renderer != null ? renderer.RaisedTilemap : null;
                if (destination == null)
                {
                    if (!paints)
                    {
                        return true;
                    }

                    if (renderer == null)
                    {
                        Debug.LogError($"{chunk.gameObject.scene.name} has no terrain renderer to draw raised ground.", chunk);
                        return false;
                    }

                    destination = AreaGroundSetup.SetUpRaised(grid, new List<string>());
                    chunk.RefreshLinks();
                }

                break;
            default:
                destination = target.Tilemap;
                break;
        }

        RecordUndo(chunk, destination, module, target.TargetKind == Kind.Walls);
        Write(destination, module, part);
        if (!Application.isPlaying)
        {
            EditorSceneManager.MarkSceneDirty(chunk.gameObject.scene);
        }

        if (target.TargetKind == Kind.Ground && paints)
        {
            CoverInNeighbours(open, chunk.Coords, part, target.Material);
        }

        return true;
    }

    /// <summary>
    /// Records <paramref name="destination"/>, its render tilemap and, for walls, the render tilemaps of the open
    /// neighbours' walls, which redraw their border from its cells, in the current undo group.
    /// </summary>
    private static void RecordUndo(WorldChunk chunk, Tilemap destination, DualGridTilemapModule module, bool walls)
    {
        List<Object> recorded = new() { destination };
        if (module != null && module.RenderTilemap != null)
        {
            recorded.Add(module.RenderTilemap);
        }

        if (walls && module != null)
        {
            for (int i = 0; i < chunk.Neighbours.Count; i++)
            {
                DualGridTilemapModule other = chunk.Neighbours[i] != null ? WallsLike(chunk.Neighbours[i], module) : null;
                if (other != null && other.RenderTilemap != null)
                {
                    recorded.Add(other.RenderTilemap);
                }
            }
        }

        Undo.RegisterCompleteObjectUndo(recorded.ToArray(), UndoName);
    }

    /// <summary>
    /// Writes <paramref name="part"/> into <paramref name="destination"/>: through <paramref name="module"/> on a
    /// dual-grid tilemap (its data tile, or empty), else each cell's tile, colour and transform in one batch.
    /// </summary>
    private static void Write(Tilemap destination, DualGridTilemapModule module, List<TileChangeData> part)
    {
        if (module == null)
        {
            destination.SetTiles(part.ToArray(), false);
            return;
        }

        List<Vector3Int> filled = new();
        List<Vector3Int> emptied = new();
        foreach (TileChangeData change in part)
        {
            (change.tile != null ? filled : emptied).Add(change.position);
        }

        if (filled.Count > 0)
        {
            module.SetDataTiles(filled);
        }

        if (emptied.Count > 0)
        {
            module.ClearDataTiles(emptied);
        }
    }

    /// <summary>
    /// Gives <paramref name="chunk"/> walls like <paramref name="source"/>: an empty copy of its tilemap, module and
    /// render tilemap under the chunk's grid, blocking the chunk's navigation, recorded for undo.
    /// </summary>
    private static DualGridTilemapModule CopyWalls(DualGridTilemapModule source, WorldChunk chunk)
    {
        GameObject copy = Object.Instantiate(source.gameObject, chunk.transform, false);
        copy.name = source.gameObject.name;
        Undo.RegisterCreatedObjectUndo(copy, $"Add {copy.name}");
        DualGridTilemapModule module = copy.GetComponent<DualGridTilemapModule>();
        module.DataTilemap.ClearAllTiles();
        module.RenderTilemap.ClearAllTiles();
        Grid grid = chunk.GetComponent<Grid>();
        if (grid.TryGetComponent(out NavigationGrid2D _))
        {
            WorldAuthoring.SetUpNavigation(grid);
        }

        chunk.RefreshLinks();
        return module;
    }

    /// <summary>
    /// Adds <paramref name="material"/>'s layer to each open neighbour of the chunk at <paramref name="coords"/> whose
    /// skirt reaches a cell <paramref name="part"/> fills and that does not draw it yet.
    /// </summary>
    private static void CoverInNeighbours(Dictionary<Vector2Int, WorldChunk> open, Vector2Int coords, List<TileChangeData> part, TerrainMaterial material)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                Vector2Int at = coords + new Vector2Int(dx, dy);
                if (at == coords || !open.TryGetValue(at, out WorldChunk neighbour) || LayerOf(neighbour, material) != null)
                {
                    continue;
                }

                RectInt bounds = ChunkCoords.Bounds(at);
                RectInt reach = new(bounds.xMin - WorldChunk.SkirtCells, bounds.yMin - WorldChunk.SkirtCells, bounds.width + 2 * WorldChunk.SkirtCells, bounds.height + 2 * WorldChunk.SkirtCells);
                if (part.Exists(change => change.tile != null && reach.Contains(new Vector2Int(change.position.x, change.position.y)))
                    && GroundLayerMigration.AddLayer(neighbour.GetComponent<Grid>(), material, false) != null)
                {
                    neighbour.RefreshLinks();
                }
            }
        }
    }

    /// <summary>
    /// The tile of the target's kind at <paramref name="cell"/> in the chunk owning it, null when that chunk has no
    /// such tilemap; false when the cell lies outside the fill's reach: outside the world's chunks, in a chunk whose
    /// scene is not open, or for a tilemap of another kind outside its own chunk.
    /// </summary>
    private static bool TryReadFillCell(Target target, Dictionary<Vector2Int, WorldChunk> open, Dictionary<Vector2Int, Tilemap> sources, Vector2Int cell, int z, out TileBase tile)
    {
        tile = null;
        Vector2Int coords = ChunkCoords.ChunkOf(cell);
        if (!open.TryGetValue(coords, out WorldChunk chunk) || !target.World.HasChunk(coords) || (target.TargetKind == Kind.Other && chunk != target.Chunk))
        {
            return false;
        }

        if (!sources.TryGetValue(coords, out Tilemap source))
        {
            source = SourceOf(target, chunk);
            sources.Add(coords, source);
        }

        tile = source != null ? source.GetTile(new Vector3Int(cell.x, cell.y, z)) : null;
        return true;
    }

    /// <summary>
    /// <paramref name="chunk"/>'s tilemap of the target's kind, or null when it has none.
    /// </summary>
    private static Tilemap SourceOf(Target target, WorldChunk chunk)
    {
        switch (target.TargetKind)
        {
            case Kind.Ground:
                return LayerOf(chunk, target.Material);
            case Kind.Walls:
                DualGridTilemapModule walls = WallsLike(chunk, target.Module);
                return walls != null ? walls.DataTilemap : null;
            case Kind.Raised:
                TerrainBlendRenderer renderer = chunk.GetComponent<TerrainBlendRenderer>();
                return renderer != null ? renderer.RaisedTilemap : null;
            default:
                return target.Tilemap;
        }
    }

    /// <summary>
    /// <paramref name="changes"/> grouped by the chunk owning their cell.
    /// </summary>
    private static Dictionary<Vector2Int, List<TileChangeData>> SplitByChunk(IReadOnlyList<TileChangeData> changes)
    {
        Dictionary<Vector2Int, List<TileChangeData>> parts = new();
        for (int i = 0; i < changes.Count; i++)
        {
            Vector2Int chunk = ChunkCoords.ChunkOf(new Vector2Int(changes[i].position.x, changes[i].position.y));
            if (!parts.TryGetValue(chunk, out List<TileChangeData> part))
            {
                parts[chunk] = part = new List<TileChangeData>();
            }

            part.Add(changes[i]);
        }

        return parts;
    }
}
