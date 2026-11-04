using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The World Editor's Place markers step (Docs/KnownGaps.md, Phase 3): what shows a world's waystones and exits in
/// game, written from the world's data into its chunk scenes, under each chunk grid's <see cref="RootName"/>. Each
/// waystone entry gets a waystone (a blocking standing stone naming its entry, <see cref="WaystoneMarker"/>) on the
/// first free cell of <see cref="WaystoneOffsets"/> from the entry's cell, two cells north first, so a player arriving
/// there stands in front of it; each exit gets its <see cref="WorldExit.Marker"/>: a doorway standing on its bottom row
/// and a doormat lying across its cells, both walked through, or a blocking signpost pointing out of the world (west in
/// the world's western half, east in its eastern) on the first free cell of <see cref="SignpostRows"/> above, then
/// below, its middle column, one row clear of it first. A cell is free when the navigation walks it for the default
/// movement profile (ground, no wall, cliff or obstacle but the markers' own), it lies on no exit, no mob spawn point and
/// no entry's row of arrival slots (the entry's cell and one either side), and no other marker takes it. Markers are reused by name and only changed where they differ, so a second run changes no file; a marker
/// whose entry or exit moved follows it, into another chunk too, and one whose entry or exit went goes. Every chunk is
/// written, locked ones too, since markers are world data, but nothing outside the root is touched. Not an undo step:
/// it spans closed scenes, and saves the ones it changed.
/// </summary>
public static class WorldMarkers
{
    public const string RootName = "Markers";
    public const string MarkersFolder = PropWriter.PrefabsFolder + "/Markers";
    public const string WaystonePath = MarkersFolder + "/Waystone_0.prefab";
    public const string DoorwayPath = MarkersFolder + "/Doorway_0.prefab";
    public const string MovementProfilePath = "Assets/Settings/AI/TerrainMovement_Default.asset";

    /// <summary>
    /// Where a waystone's stone may stand from its entry's cell, in order: two cells north, then one, then beside those,
    /// then further round.
    /// </summary>
    public static readonly Vector2Int[] WaystoneOffsets =
    {
        new(0, 2), new(0, 1), new(-1, 2), new(1, 2), new(-1, 1), new(1, 1), new(0, 3), new(-1, 3), new(1, 3),
        new(-2, 1), new(2, 1), new(-2, 2), new(2, 2), new(0, -2), new(-1, -2), new(1, -2)
    };

    /// <summary>
    /// Where a signpost may stand from an exit's middle column, in rows over its top row (positive) or under its bottom
    /// row (negative), in order: one row clear above it first, beside the road it ends.
    /// </summary>
    public static readonly int[] SignpostRows = { 2, 1, 3, -2, -1, -3 };

    private const float ObstacleInset = 0.05f;

    /// <summary>
    /// One marker a chunk should hold: its prefab, where its root stands (world units), its name in the root, the entry
    /// it names (a waystone's) and its cell.
    /// </summary>
    public readonly struct Placement
    {
        public Placement(GameObject prefab, Vector3 position, string name, string entry, Vector2Int cell)
        {
            Prefab = prefab;
            Position = position;
            Name = name;
            Entry = entry;
            Cell = cell;
        }

        public GameObject Prefab { get; }
        public Vector3 Position { get; }
        public string Name { get; }
        public string Entry { get; }
        public Vector2Int Cell { get; }
    }

    /// <summary>
    /// Places the markers of <paramref name="world"/>'s entries and exits in its chunk scenes, opening those that are not
    /// open and closing them again, saving the ones it changed. Returns a line per chunk it changed and per marker it
    /// could not place.
    /// </summary>
    public static List<string> Place(WorldDefinition world)
    {
        List<string> report = new();
        List<Scene> opened = new();
        try
        {
            Dictionary<Vector2Int, Grid> grids = OpenGrids(world, opened);
            Dictionary<Vector2Int, List<Placement>> plan = Plan(world, grids, report);
            foreach (KeyValuePair<Vector2Int, Grid> chunk in grids)
            {
                plan.TryGetValue(chunk.Key, out List<Placement> placements);
                if (!WriteChunk(chunk.Value, placements))
                {
                    continue;
                }

                Scene scene = chunk.Value.gameObject.scene;
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                report.Add($"{scene.name}: {Describe(placements)}.");
            }
        }
        finally
        {
            WorldAuthoring.CloseScenes(opened, false);
        }

        return report;
    }

    /// <summary>
    /// The chunks of <paramref name="world"/> whose markers differ from what Place markers would write, each a line,
    /// opening the chunk scenes that are not open and closing them again unchanged; empty when every chunk is in step.
    /// </summary>
    public static List<string> Stale(WorldDefinition world)
    {
        List<string> stale = new();
        List<Scene> opened = new();
        try
        {
            Dictionary<Vector2Int, Grid> grids = OpenGrids(world, opened);
            Dictionary<Vector2Int, List<Placement>> plan = Plan(world, grids, new List<string>());
            foreach (KeyValuePair<Vector2Int, Grid> chunk in grids)
            {
                plan.TryGetValue(chunk.Key, out List<Placement> placements);
                if (Differs(chunk.Value, placements))
                {
                    stale.Add($"{chunk.Value.gameObject.scene.name}: its waystone and exit markers are out of date; run Place markers in the World Editor.");
                }
            }
        }
        finally
        {
            WorldAuthoring.CloseScenes(opened, false);
        }

        return stale;
    }

    /// <summary>
    /// The markers each chunk of <paramref name="world"/> should hold, by its coordinates, from its entries and the exits
    /// of <paramref name="grids"/>' scenes, judging free cells on those grids' navigation (built for the check when not
    /// built, and unbuilt after). A marker with no free cell is left out, with a line in <paramref name="report"/>.
    /// </summary>
    public static Dictionary<Vector2Int, List<Placement>> Plan(WorldDefinition world, IReadOnlyDictionary<Vector2Int, Grid> grids, List<string> report)
    {
        Dictionary<Vector2Int, List<Placement>> plan = new();
        TerrainMovementProfile2D profile = AssetDatabase.LoadAssetAtPath<TerrainMovementProfile2D>(MovementProfilePath);
        GameObject waystone = AssetDatabase.LoadAssetAtPath<GameObject>(WaystonePath);
        List<NavigationGrid2D> built = new();
        List<(WorldExit Exit, RectInt Cells)> exits = new();
        HashSet<Vector2Int> taken = new();
        HashSet<Scene> scenes = new();
        foreach (KeyValuePair<Vector2Int, Grid> chunk in grids)
        {
            Scene scene = chunk.Value.gameObject.scene;
            if (!scenes.Add(scene))
            {
                continue;
            }

            foreach (WorldExit exit in SceneQuery.FindAll<WorldExit>(scene))
            {
                RectInt cells = exit.CellsIn(world);
                exits.Add((exit, cells));
                foreach (Vector2Int cell in cells.allPositionsWithin)
                {
                    taken.Add(cell);
                }
            }

            foreach (MobSpawnPoint point in SceneQuery.FindAll<MobSpawnPoint>(scene))
            {
                taken.Add(ChunkCoords.CellOf(world.ToLocal(point.transform.position)));
            }
        }

        foreach (WorldDefinition.Entry entry in world.Entries)
        {
            if (entry == null)
            {
                continue;
            }

            Vector2Int cell = ChunkCoords.CellOf(entry.Position);
            for (int dx = -1; dx <= 1; dx++)
            {
                taken.Add(cell + new Vector2Int(dx, 0));
            }
        }

        try
        {
            foreach (WorldDefinition.Entry entry in world.Entries)
            {
                if (entry == null || !entry.Waystone)
                {
                    continue;
                }

                Vector2Int origin = ChunkCoords.CellOf(entry.Position);
                if (waystone == null || !TryFindCell(origin, WaystoneOffsets, grids, profile, taken, built, out Vector2Int cell))
                {
                    report.Add($"{world.name}: the waystone '{entry.Name}' at {origin} has no free cell near it; it has no marker.");
                    continue;
                }

                Add(plan, grids, new Placement(waystone, Root(world, grids, cell, waystone), $"Waystone_{entry.Name}", entry.Name, cell));
            }

            foreach ((WorldExit exit, RectInt cells) in exits)
            {
                if (!TryPlanExit(world, exit, cells, grids, profile, taken, built, out Placement placement))
                {
                    if (exit.Marker != ExitMarker.None)
                    {
                        report.Add($"{world.name}: the exit {PersistentIds.PathOf(exit)} has no free cell for its {exit.Marker}; it has no marker.");
                    }

                    continue;
                }

                Add(plan, grids, placement);
            }
        }
        finally
        {
            foreach (NavigationGrid2D navigation in built)
            {
                navigation.Unbuild();
            }
        }

        return plan;
    }

    /// <summary>
    /// Makes the instances under <paramref name="grid"/>'s <see cref="RootName"/> the chunk's
    /// <paramref name="placements"/>, in order: an instance of the same name and prefab is reused, moved where it
    /// differs, others are made in the grid's scene, the rest destroyed; the root is made when missing and destroyed
    /// when there is nothing to place. Returns whether anything changed.
    /// </summary>
    public static bool WriteChunk(Grid grid, IReadOnlyList<Placement> placements)
    {
        Transform root = grid.transform.Find(RootName);
        if (placements == null || placements.Count == 0)
        {
            if (root == null)
            {
                return false;
            }

            Object.DestroyImmediate(root.gameObject);
            return true;
        }

        bool changed = false;
        if (root == null)
        {
            root = new GameObject(RootName).transform;
            root.SetParent(grid.transform, false);
            changed = true;
        }

        Dictionary<string, GameObject> existing = new();
        List<GameObject> leftovers = new();
        foreach (Transform child in root)
        {
            if (!existing.TryAdd(child.name, child.gameObject))
            {
                leftovers.Add(child.gameObject);
            }
        }

        HashSet<GameObject> kept = new();
        for (int i = 0; i < placements.Count; i++)
        {
            Placement placement = placements[i];
            if (!existing.TryGetValue(placement.Name, out GameObject instance) || PrefabUtility.GetCorrespondingObjectFromSource(instance) != placement.Prefab)
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(placement.Prefab, grid.gameObject.scene);
                instance.name = placement.Name;
                instance.transform.SetParent(root, false);
                changed = true;
            }

            kept.Add(instance);
            if (instance.transform.position != placement.Position)
            {
                instance.transform.position = placement.Position;
                changed = true;
            }

            if (instance.transform.GetSiblingIndex() != i)
            {
                instance.transform.SetSiblingIndex(i);
                changed = true;
            }

            changed |= SetEntry(instance, placement.Entry);
        }

        foreach (Transform child in root)
        {
            if (!kept.Contains(child.gameObject))
            {
                leftovers.Add(child.gameObject);
            }
        }

        foreach (GameObject leftover in leftovers)
        {
            if (leftover != null)
            {
                Object.DestroyImmediate(leftover);
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Whether <paramref name="grid"/>'s markers differ from <paramref name="placements"/>: a missing, extra, moved or
    /// renamed one, or a stone naming another entry.
    /// </summary>
    public static bool Differs(Grid grid, IReadOnlyList<Placement> placements)
    {
        Transform root = grid.transform.Find(RootName);
        int count = placements != null ? placements.Count : 0;
        if (root == null)
        {
            return count > 0;
        }

        if (root.childCount != count)
        {
            return true;
        }

        for (int i = 0; i < count; i++)
        {
            Transform child = root.GetChild(i);
            Placement placement = placements[i];
            if (child.name != placement.Name || child.position != placement.Position || PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) != placement.Prefab
                || (child.TryGetComponent(out WaystoneMarker marker) && marker.EntryName != placement.Entry))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The prefab of the marker <paramref name="kind"/> for an exit covering <paramref name="cells"/> of
    /// <paramref name="world"/>: a signpost pointing west or east by which half of the world its middle lies in, a
    /// doorway, or a doormat across two cells, along two cells or of one cell by the exit's shape; null for none.
    /// </summary>
    public static GameObject PrefabFor(ExitMarker kind, RectInt cells, WorldDefinition world)
    {
        switch (kind)
        {
            case ExitMarker.Signpost:
                int direction = cells.center.x < world.LocalBounds.center.x ? MarkerRecipe.SignpostWest : MarkerRecipe.SignpostEast;
                return AssetDatabase.LoadAssetAtPath<GameObject>($"{MarkersFolder}/{PropWriter.SliceName("Signpost", direction)}.prefab");
            case ExitMarker.Door:
                return AssetDatabase.LoadAssetAtPath<GameObject>(DoorwayPath);
            case ExitMarker.Doormat:
                int shape = cells.width >= 2 && cells.width >= cells.height ? MarkerRecipe.DoormatAcross : cells.height >= 2 ? MarkerRecipe.DoormatAlong : MarkerRecipe.DoormatSingle;
                return AssetDatabase.LoadAssetAtPath<GameObject>($"{MarkersFolder}/{PropWriter.SliceName("Doormat", shape)}.prefab");
            default:
                return null;
        }
    }

    /// <summary>
    /// The marker of <paramref name="exit"/>, covering <paramref name="cells"/>: a doorway on the middle of its bottom
    /// row, a doormat on its middle, or a signpost on the first free cell beside it; false for none, or no free cell.
    /// </summary>
    private static bool TryPlanExit(WorldDefinition world, WorldExit exit, RectInt cells, IReadOnlyDictionary<Vector2Int, Grid> grids, TerrainMovementProfile2D profile,
        HashSet<Vector2Int> taken, List<NavigationGrid2D> built, out Placement placement)
    {
        placement = default;
        GameObject prefab = PrefabFor(exit.Marker, cells, world);
        if (prefab == null)
        {
            return false;
        }

        string name = $"{exit.Marker}_{exit.Id}";
        if (exit.Marker == ExitMarker.Signpost)
        {
            Vector2Int column = new(cells.x + (cells.width - 1) / 2, 0);
            List<Vector2Int> candidates = new();
            foreach (int row in SignpostRows)
            {
                candidates.Add(new Vector2Int(column.x, row > 0 ? cells.yMax - 1 + row : cells.yMin + row));
            }

            if (!TryFindCell(Vector2Int.zero, candidates.ToArray(), grids, profile, taken, built, out Vector2Int cell))
            {
                return false;
            }

            placement = new Placement(prefab, Root(world, grids, cell, prefab), name, null, cell);
            return true;
        }

        Vector2 local = exit.Marker == ExitMarker.Door ? new Vector2(cells.x + cells.width * 0.5f, cells.y + 0.5f) : cells.center;
        placement = new Placement(prefab, PropGridSnapping.SnapToPixels(world.ToWorld(local)), name, null, cells.position);
        return true;
    }

    /// <summary>
    /// The first of <paramref name="origin"/> plus each of <paramref name="offsets"/> that lies on an open chunk and is
    /// free (<see cref="IsFree"/>), taking it; false when none is.
    /// </summary>
    private static bool TryFindCell(Vector2Int origin, Vector2Int[] offsets, IReadOnlyDictionary<Vector2Int, Grid> grids, TerrainMovementProfile2D profile,
        HashSet<Vector2Int> taken, List<NavigationGrid2D> built, out Vector2Int found)
    {
        foreach (Vector2Int offset in offsets)
        {
            Vector2Int cell = origin + offset;
            if (taken.Contains(cell) || !grids.TryGetValue(ChunkCoords.ChunkOf(cell), out Grid grid) || !IsFree(grid, cell, profile, built))
            {
                continue;
            }

            taken.Add(cell);
            found = cell;
            return true;
        }

        found = default;
        return false;
    }

    /// <summary>
    /// Whether the navigation of <paramref name="grid"/> walks <paramref name="cell"/> for <paramref name="profile"/>,
    /// counting a cell blocked only by markers' colliders as walked. Builds the navigation first when it is not built,
    /// adding it to <paramref name="built"/>.
    /// </summary>
    private static bool IsFree(Grid grid, Vector2Int cell, TerrainMovementProfile2D profile, List<NavigationGrid2D> built)
    {
        NavigationGrid2D navigation = grid.GetComponent<NavigationGrid2D>();
        if (navigation == null)
        {
            return false;
        }

        if (!navigation.IsBuilt)
        {
            navigation.BuildGrid();
            built.Add(navigation);
        }

        Vector3Int at = new(cell.x, cell.y, 0);
        NavigationGrid2D.Blocker blocker = navigation.GetBlocker(at, profile);
        return blocker == NavigationGrid2D.Blocker.None || (blocker == NavigationGrid2D.Blocker.Obstacle && OnlyMarkersOn(grid, at, navigation.ObstacleLayers));
    }

    /// <summary>
    /// Whether every obstacle collider over <paramref name="cell"/> of <paramref name="grid"/> is a marker's.
    /// </summary>
    private static bool OnlyMarkersOn(Grid grid, Vector3Int cell, LayerMask layers)
    {
        Vector2 min = (Vector2)grid.CellToWorld(cell) + Vector2.one * ObstacleInset;
        Vector2 max = (Vector2)grid.CellToWorld(cell + new Vector3Int(1, 1, 0)) - Vector2.one * ObstacleInset;
        ContactFilter2D filter = new() { useTriggers = false };
        filter.SetLayerMask(layers);
        List<Collider2D> hits = new();
        Physics2D.SyncTransforms();
        Physics2D.OverlapArea(min, max, filter, hits);
        foreach (Collider2D hit in hits)
        {
            Transform parent = hit.transform.parent;
            if (parent == null || parent.name != RootName)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Where a blocking marker's root stands so its footprint covers <paramref name="cell"/> of the grid owning it, as
    /// the props' snapping puts it; the cell's middle for a marker without a footprint.
    /// </summary>
    private static Vector3 Root(WorldDefinition world, IReadOnlyDictionary<Vector2Int, Grid> grids, Vector2Int cell, GameObject prefab)
    {
        Grid grid = grids[ChunkCoords.ChunkOf(cell)];
        return PropGridSnapping.TryGetFootprint(prefab, out Rect footprint)
            ? PropGridSnapping.RootOnCells(grid, new Vector3Int(cell.x, cell.y, 0), footprint)
            : PropGridSnapping.SnapToPixels(world.ToWorld(cell + Vector2.one * 0.5f));
    }

    /// <summary>
    /// Adds <paramref name="placement"/> to the chunk owning its cell.
    /// </summary>
    private static void Add(Dictionary<Vector2Int, List<Placement>> plan, IReadOnlyDictionary<Vector2Int, Grid> grids, Placement placement)
    {
        Vector2Int chunk = ChunkCoords.ChunkOf(placement.Cell);
        if (!grids.ContainsKey(chunk))
        {
            return;
        }

        if (!plan.TryGetValue(chunk, out List<Placement> list))
        {
            list = new List<Placement>();
            plan[chunk] = list;
        }

        list.Add(placement);
    }

    /// <summary>
    /// Points <paramref name="instance"/>'s <see cref="WaystoneMarker"/>, when it has one, at <paramref name="entry"/>,
    /// through its serialized object so the instance keeps it as an override; returns whether it changed.
    /// </summary>
    private static bool SetEntry(GameObject instance, string entry)
    {
        if (!instance.TryGetComponent(out WaystoneMarker marker) || marker.EntryName == (entry ?? string.Empty))
        {
            return false;
        }

        SerializedObject data = new(marker);
        data.FindProperty("entryName").stringValue = entry ?? string.Empty;
        data.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    /// <summary>
    /// The grids of <paramref name="world"/>'s chunks by their coordinates, opening the scenes that are not open.
    /// </summary>
    private static Dictionary<Vector2Int, Grid> OpenGrids(WorldDefinition world, List<Scene> opened)
    {
        Dictionary<Vector2Int, Grid> grids = new();
        foreach (KeyValuePair<Vector2Int, Scene> chunk in WorldAuthoring.OpenChunkScenes(world, opened))
        {
            Grid grid = SceneQuery.FindFirst<Grid>(chunk.Value);
            if (grid != null)
            {
                grids[chunk.Key] = grid;
            }
        }

        return grids;
    }

    /// <summary>
    /// A line naming each marker of <paramref name="placements"/>, or saying there are none.
    /// </summary>
    private static string Describe(IReadOnlyList<Placement> placements)
    {
        if (placements == null || placements.Count == 0)
        {
            return "no markers";
        }

        List<string> names = new();
        foreach (Placement placement in placements)
        {
            names.Add($"{placement.Name} at {placement.Cell}");
        }

        return string.Join(", ", names);
    }
}
