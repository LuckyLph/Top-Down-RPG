using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Outside Play Mode, joins the navigation pages of the open chunks of each world, as the world's
/// <see cref="WorldNavigation"/> joins them in play, so <see cref="MovementBlockerGizmo"/> reads a neighbouring chunk's
/// page past a border instead of outlining it, and a cliff crossing a border shows only where the game has one. A chunk
/// takes part when its <see cref="WorldChunk"/> and <see cref="NavigationGrid2D"/> are enabled in its world's saved scene
/// for those coordinates; chunks built in memory (tests) stand alone as before. The links follow scenes opening and
/// closing and the hierarchy changing, once per editor update at most; a chunk's page leaves before its scene closes,
/// and every page leaves before Play Mode and before an assembly reload. Up to <see cref="NavigationPagePool.DefaultSlots"/>
/// chunks are joined; any more stand alone.
/// </summary>
[InitializeOnLoad]
public static class EditModeWorldNavigation
{
    private static readonly Dictionary<WorldDefinition, WorldNavigation> navigations = new();
    private static readonly Dictionary<NavigationGrid2D, KeyValuePair<WorldDefinition, Vector2Int>> joined = new();
    private static readonly List<NavigationGrid2D> leaving = new();
    private static NavigationPagePool pool = new();
    private static bool scheduled;

    /// <summary>
    /// Follows the scenes, the hierarchy, Play Mode and assembly reloads, and joins the chunks already open.
    /// </summary>
    static EditModeWorldNavigation()
    {
        EditorSceneManager.sceneOpened += (scene, mode) => Schedule();
        EditorSceneManager.sceneClosing += HandleSceneClosing;
        EditorApplication.hierarchyChanged += Schedule;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        AssemblyReloadEvents.beforeAssemblyReload += LeaveAll;
        Schedule();
    }

    /// <summary>
    /// Whether <paramref name="grid"/>'s page is joined to its world's other open chunks.
    /// </summary>
    public static bool IsJoined(NavigationGrid2D grid)
    {
        return grid != null && joined.ContainsKey(grid);
    }

    /// <summary>
    /// The navigation joining <paramref name="world"/>'s open chunks, or null while none of them has joined.
    /// </summary>
    public static WorldNavigation NavigationOf(WorldDefinition world)
    {
        return world != null && navigations.TryGetValue(world, out WorldNavigation navigation) ? navigation : null;
    }

    /// <summary>
    /// Brings the joined pages in line with the open chunks now: pages whose chunk went, was disabled or moved leave,
    /// and the chunks open in their world's scenes join.
    /// </summary>
    public static void Sync()
    {
        scheduled = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        leaving.Clear();
        foreach (KeyValuePair<NavigationGrid2D, KeyValuePair<WorldDefinition, Vector2Int>> entry in joined)
        {
            if (!TryGetChunk(entry.Key, out WorldDefinition world, out Vector2Int coords) || world != entry.Value.Key || coords != entry.Value.Value)
            {
                leaving.Add(entry.Key);
            }
        }

        foreach (NavigationGrid2D grid in leaving)
        {
            Leave(grid);
        }

        foreach (WorldChunk chunk in Object.FindObjectsByType<WorldChunk>(FindObjectsInactive.Exclude))
        {
            if (!chunk.TryGetComponent(out NavigationGrid2D grid) || joined.ContainsKey(grid) || !TryGetChunk(grid, out WorldDefinition world, out Vector2Int coords))
            {
                continue;
            }

            if (!navigations.TryGetValue(world, out WorldNavigation navigation))
            {
                navigations[world] = navigation = new WorldNavigation(pool, world);
            }

            if (!navigation.HasChunk(coords) && pool.UsedSlots < pool.SlotCount && navigation.AddChunk(grid, coords))
            {
                joined.Add(grid, new KeyValuePair<WorldDefinition, Vector2Int>(world, coords));
            }
        }
    }

    /// <summary>
    /// Takes every page out of the joined navigation, so each chunk builds its own page again when its gizmo needs it.
    /// </summary>
    public static void LeaveAll()
    {
        leaving.Clear();
        leaving.AddRange(joined.Keys);
        foreach (NavigationGrid2D grid in leaving)
        {
            Leave(grid);
        }

        navigations.Clear();
        pool = new NavigationPagePool();
    }

    /// <summary>
    /// Syncs once at the next editor update, however many changes come before it.
    /// </summary>
    private static void Schedule()
    {
        if (!scheduled)
        {
            scheduled = true;
            EditorApplication.delayCall += Sync;
        }
    }

    /// <summary>
    /// The world and coordinates of the chunk whose grid is <paramref name="grid"/>, when it is enabled in the scene its
    /// world lists for those coordinates.
    /// </summary>
    private static bool TryGetChunk(NavigationGrid2D grid, out WorldDefinition world, out Vector2Int coords)
    {
        world = null;
        coords = default;
        if (grid == null || !grid.isActiveAndEnabled || !grid.TryGetComponent(out WorldChunk chunk) || !chunk.isActiveAndEnabled || chunk.World == null)
        {
            return false;
        }

        world = chunk.World;
        coords = chunk.Coords;
        return world.TryGetChunkScene(coords, out SceneDefinition scene) && scene.IsValid && scene.ScenePath == grid.gameObject.scene.path;
    }

    /// <summary>
    /// Takes <paramref name="grid"/>'s page out of its world's navigation and forgets it.
    /// </summary>
    private static void Leave(NavigationGrid2D grid)
    {
        if (joined.Remove(grid, out KeyValuePair<WorldDefinition, Vector2Int> chunk) && navigations.TryGetValue(chunk.Key, out WorldNavigation navigation))
        {
            navigation.RemoveChunk(chunk.Value);
        }
    }

    /// <summary>
    /// Takes the pages of a closing scene's chunks out before it unloads.
    /// </summary>
    private static void HandleSceneClosing(Scene scene, bool removingScene)
    {
        leaving.Clear();
        foreach (NavigationGrid2D grid in joined.Keys)
        {
            if (ReferenceEquals(grid, null) || grid == null || grid.gameObject.scene == scene)
            {
                leaving.Add(grid);
            }
        }

        foreach (NavigationGrid2D grid in leaving)
        {
            Leave(grid);
        }

        Schedule();
    }

    /// <summary>
    /// Takes every page out before Play Mode, and joins the open chunks again after it.
    /// </summary>
    private static void HandlePlayModeStateChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingEditMode)
        {
            LeaveAll();
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            Schedule();
        }
    }
}
