using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ownership checks of seamless worlds (Docs/WorldStreaming.md: Ownership): everything placed in a chunk lies inside
/// its 64 x 64 cells. A mob spawn point, a <see cref="PersistentId"/> or a walk-through prop (a child of the scene's
/// Props root, placed by hand, or of its grid's <see cref="WorldGenerationWriter.GeneratedPropsName"/>, written by world
/// generation) stands on one of its cells, and a blocking prop's footprint covers only its cells, by the rule
/// <see cref="PropGridSnapping.BlockedCells"/> shares with the navigation. Every mob spawn point has a
/// <see cref="PersistentId"/>, every id is set, and no two objects of a world share one. A world's chunk scenes name it
/// and their coordinates, and its origin keeps the layout (<see cref="WorldLayout"/>). Every <see cref="WorldExit"/>
/// covers only its chunk's cells and leads to a world <see cref="GameScenes"/> lists, at an entry that world has. Each
/// problem is a line naming the object; the World Editor shows them and <see cref="WorldBuildValidator"/> refuses a
/// build with any.
/// </summary>
public static class WorldValidation
{
    /// <summary>
    /// The ownership problems of the objects in <paramref name="chunk"/>'s scene, its exits checked against
    /// <paramref name="listedWorlds"/> (the project's <see cref="GameScenes"/> list when null).
    /// </summary>
    public static List<string> ValidateChunk(WorldChunk chunk, IReadOnlyList<WorldDefinition> listedWorlds = null)
    {
        List<string> problems = new();
        Scene scene = chunk.gameObject.scene;
        Grid grid = chunk.GetComponent<Grid>();
        RectInt cells = chunk.Cells;
        foreach (MobSpawnPoint point in SceneQuery.FindAll<MobSpawnPoint>(scene))
        {
            CheckInside(problems, point, grid, cells, "mob spawn point");
            if (!point.TryGetComponent(out PersistentId _))
            {
                problems.Add($"{PersistentIds.PathOf(point)}: a mob spawn point without a {nameof(PersistentId)}; run Repair ids in the World Editor.");
            }
        }

        foreach (PersistentId id in SceneQuery.FindAll<PersistentId>(scene))
        {
            if (!id.HasId)
            {
                problems.Add($"{PersistentIds.PathOf(id)}: its {nameof(PersistentId)} has no id; run Repair ids in the World Editor.");
            }

            if (!id.TryGetComponent(out MobSpawnPoint _) && !id.TryGetComponent(out WorldExit _))
            {
                CheckInside(problems, id, grid, cells, "object with a persistent id");
            }
        }

        foreach (WorldExit exit in SceneQuery.FindAll<WorldExit>(scene))
        {
            CheckExit(problems, exit, grid, cells, listedWorlds ?? ListedWorlds());
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name != PropPlacementWindow.PropsRootName)
            {
                continue;
            }

            foreach (Transform prop in root.transform)
            {
                CheckProp(problems, prop.gameObject, grid, cells);
            }
        }

        Transform generated = grid != null ? grid.transform.Find(WorldGenerationWriter.GeneratedPropsName) : null;
        if (generated != null)
        {
            foreach (Transform prop in generated)
            {
                CheckProp(problems, prop.gameObject, grid, cells);
            }
        }

        return problems;
    }

    /// <summary>
    /// Whether a blocking prop with <paramref name="footprint"/> rooted at <paramref name="rootPosition"/> blocks only
    /// cells of <paramref name="cells"/> on <paramref name="grid"/>, with the cells it blocks.
    /// </summary>
    public static bool FootprintInside(GridLayout grid, Vector2 rootPosition, Rect footprint, RectInt cells, out BoundsInt blocked)
    {
        blocked = PropGridSnapping.BlockedCells(grid, rootPosition, footprint);
        return blocked.xMin >= cells.xMin && blocked.yMin >= cells.yMin && blocked.xMax <= cells.xMax && blocked.yMax <= cells.yMax;
    }

    /// <summary>
    /// The duplicate ids among <paramref name="ids"/>, a line per shared id naming every object holding it.
    /// </summary>
    public static List<string> ValidateIds(IEnumerable<PersistentId> ids)
    {
        List<string> problems = new();
        foreach (KeyValuePair<string, List<PersistentId>> duplicate in PersistentIds.Duplicates(ids))
        {
            List<string> holders = new();
            foreach (PersistentId id in duplicate.Value)
            {
                holders.Add(PersistentIds.PathOf(id));
            }

            problems.Add($"{nameof(PersistentId)} {duplicate.Key} is shared by {string.Join(", ", holders)}; run Repair ids in the World Editor.");
        }

        return problems;
    }

    /// <summary>
    /// Every problem of <paramref name="world"/>: its origin against <paramref name="allWorlds"/>, each chunk scene's
    /// marker and ownership, and duplicate ids across its chunks. Opens the chunk scenes that are not open beside the
    /// others and closes them again, unchanged.
    /// </summary>
    public static List<string> ValidateWorld(WorldDefinition world, IReadOnlyList<WorldDefinition> allWorlds)
    {
        List<string> problems = new();
        string layout = WorldLayout.Problem(world.Origin, world.SizeInChunks, allWorlds, world);
        if (layout != null)
        {
            problems.Add($"World {world.name} (origin {world.Origin}): {layout}.");
        }

        List<Scene> opened = new();
        try
        {
            List<PersistentId> ids = new();
            foreach (KeyValuePair<Vector2Int, Scene> chunk in WorldAuthoring.OpenChunkScenes(world, opened))
            {
                WorldChunk marker = SceneQuery.FindFirst<WorldChunk>(chunk.Value);
                if (marker == null || marker.World != world || marker.Coords != chunk.Key)
                {
                    problems.Add($"{chunk.Value.name}: its grid has no {nameof(WorldChunk)} naming {world.name} and chunk {chunk.Key}.");
                    continue;
                }

                problems.AddRange(ValidateChunk(marker));
                ids.AddRange(SceneQuery.FindAll<PersistentId>(chunk.Value));
            }

            problems.AddRange(ValidateIds(ids));
        }
        finally
        {
            foreach (Scene scene in opened)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        return problems;
    }

    /// <summary>
    /// The problems of every world in the project (<see cref="WorldLayout.AllWorlds"/>).
    /// </summary>
    public static List<string> ValidateAll()
    {
        List<WorldDefinition> worlds = WorldLayout.AllWorlds();
        List<string> problems = new();
        foreach (WorldDefinition world in worlds)
        {
            problems.AddRange(ValidateWorld(world, worlds));
        }

        return problems;
    }

    /// <summary>
    /// Adds a problem for each way <paramref name="exit"/> is wrong: a cell outside <paramref name="cells"/>, no target
    /// world, a target missing from <paramref name="listedWorlds"/>, or a target entry the world does not have (or no
    /// default entry for an empty name).
    /// </summary>
    private static void CheckExit(List<string> problems, WorldExit exit, GridLayout grid, RectInt cells, IReadOnlyList<WorldDefinition> listedWorlds)
    {
        Vector3Int first = grid.WorldToCell(exit.transform.position);
        RectInt covered = new(new Vector2Int(first.x, first.y), exit.SizeInCells);
        if (covered.xMin < cells.xMin || covered.yMin < cells.yMin || covered.xMax > cells.xMax || covered.yMax > cells.yMax)
        {
            problems.Add($"{PersistentIds.PathOf(exit)}: a world exit covering cells ({covered.xMin}, {covered.yMin}) to ({covered.xMax - 1}, {covered.yMax - 1}), outside its chunk's cells {Describe(cells)}.");
        }

        WorldDefinition target = exit.TargetWorld;
        if (target == null)
        {
            problems.Add($"{PersistentIds.PathOf(exit)}: a world exit leading to no world.");
            return;
        }

        bool listed = false;
        for (int i = 0; i < listedWorlds.Count && !listed; i++)
        {
            listed = listedWorlds[i] == target;
        }

        if (!listed)
        {
            problems.Add($"{PersistentIds.PathOf(exit)}: a world exit leading to {target.name}, which Game Scenes does not list.");
        }

        if (string.IsNullOrEmpty(exit.TargetEntry))
        {
            if (target.DefaultEntry == null)
            {
                problems.Add($"{PersistentIds.PathOf(exit)}: a world exit leading to {target.name}, which has no entry.");
            }

            return;
        }

        foreach (WorldDefinition.Entry entry in target.Entries)
        {
            if (entry != null && entry.Name == exit.TargetEntry)
            {
                return;
            }
        }

        problems.Add($"{PersistentIds.PathOf(exit)}: a world exit leading to entry '{exit.TargetEntry}' of {target.name}, which has no entry of that name.");
    }

    /// <summary>
    /// The worlds the project's <see cref="GameScenes"/> lists; none without one.
    /// </summary>
    private static IReadOnlyList<WorldDefinition> ListedWorlds()
    {
        GameScenes gameScenes = ProjectScenes.LoadGameScenes();
        return gameScenes != null ? gameScenes.Worlds : new WorldDefinition[0];
    }

    /// <summary>
    /// Adds a problem when <paramref name="component"/>'s position is on no cell of <paramref name="cells"/>.
    /// </summary>
    private static void CheckInside(List<string> problems, Component component, GridLayout grid, RectInt cells, string what)
    {
        Vector3Int cell = grid.WorldToCell(component.transform.position);
        if (!cells.Contains(new Vector2Int(cell.x, cell.y)))
        {
            problems.Add($"{PersistentIds.PathOf(component)}: a {what} on cell ({cell.x}, {cell.y}), outside its chunk's cells {Describe(cells)}.");
        }
    }

    /// <summary>
    /// Adds a problem when the prop <paramref name="prop"/> stands outside <paramref name="cells"/>, or for a blocking
    /// prop, when its footprint blocks a cell outside them.
    /// </summary>
    private static void CheckProp(List<string> problems, GameObject prop, GridLayout grid, RectInt cells)
    {
        if (!PropGridSnapping.TryGetFootprint(prop, out Rect footprint))
        {
            CheckInside(problems, prop.transform, grid, cells, "prop");
            return;
        }

        if (!FootprintInside(grid, prop.transform.position, footprint, cells, out BoundsInt blocked))
        {
            problems.Add($"{PersistentIds.PathOf(prop.transform)}: a blocking prop whose footprint covers cells ({blocked.xMin}, {blocked.yMin}) to ({blocked.xMax - 1}, {blocked.yMax - 1}), crossing its chunk's border {Describe(cells)}.");
        }
    }

    /// <summary>
    /// <paramref name="cells"/> as "(xMin, yMin) to (xMax - 1, yMax - 1)", its first and last cells.
    /// </summary>
    private static string Describe(RectInt cells)
    {
        return $"({cells.xMin}, {cells.yMin}) to ({cells.xMax - 1}, {cells.yMax - 1})";
    }
}
