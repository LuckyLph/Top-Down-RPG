using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// <c>Tools/TopDownRPG/World/World Editor</c> (Docs/WorldStreaming.md: Authoring): creates a world at the first free
/// origin of the global layout with its chunks, lists it in <see cref="GameScenes"/>, opens a chunk with its eight
/// neighbours (or alone) and closes them again, gives an open chunk walls or raised ground for the Terrain Brush to
/// paint, opens the root scene, edits the world's entries and waystones, and runs
/// the whole-world steps: syncing the layer lists, baking the coast maps of the chunks whose sea changed (or all of
/// them), repairing persistent ids and validating the world. The steps live in <see cref="WorldAuthoring"/>,
/// <see cref="WorldLayout"/>, <see cref="PersistentIds"/> and <see cref="WorldValidation"/>; the window only draws
/// them. Its only standing work is a flag set when a tile of an open chunk of the picked world changes, which warns
/// that the coast maps may need baking.
/// </summary>
public class WorldEditorWindow : EditorWindow
{
    private const string MenuPath = ProjectScenes.MenuRoot + "World/World Editor";
    private const float ChunkButtonSize = 34f;

    private readonly HashSet<string> chunkPaths = new();
    private List<WorldDefinition> worlds = new();
    private WorldDefinition world;
    private SerializedObject worldData;
    private Vector2Int selected;
    private string newName = "NewWorld";
    private Vector2Int newSize = new(2, 2);
    private Vector2Int newOrigin;
    private bool autoOrigin = true;
    private bool createChunks = true;
    private bool listWorld = true;
    private bool groundChanged;
    private List<Vector2Int> staleCoasts;
    private List<string> messages = new();
    private Vector2 scroll;
    private Vector2 messageScroll;

    /// <summary>
    /// Opens the window.
    /// </summary>
    [MenuItem(MenuPath, priority = 40)]
    public static void Open()
    {
        GetWindow<WorldEditorWindow>("World Editor");
    }

    /// <summary>
    /// Reads the worlds and follows tile changes.
    /// </summary>
    private void OnEnable()
    {
        RefreshWorlds();
        Tilemap.tilemapTileChanged += HandleTileChanged;
        EditorSceneManager.sceneOpened += HandleSceneOpened;
        EditorSceneManager.sceneClosed += HandleSceneClosed;
    }

    /// <summary>
    /// Stops following tile changes.
    /// </summary>
    private void OnDisable()
    {
        Tilemap.tilemapTileChanged -= HandleTileChanged;
        EditorSceneManager.sceneOpened -= HandleSceneOpened;
        EditorSceneManager.sceneClosed -= HandleSceneClosed;
    }

    /// <summary>
    /// Draws the world picker, the new world section, the picked world's chunks, entries and steps, and the last
    /// report.
    /// </summary>
    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawPicker();
        EditorGUILayout.Space();
        DrawNewWorld();
        if (world != null)
        {
            EditorGUILayout.Space();
            DrawWorld();
            EditorGUILayout.Space();
            DrawChunks();
            EditorGUILayout.Space();
            DrawEntries();
            EditorGUILayout.Space();
            DrawSteps();
        }

        EditorGUILayout.EndScrollView();
        DrawMessages();
    }

    /// <summary>
    /// The world picker and a button to read the worlds again.
    /// </summary>
    private void DrawPicker()
    {
        EditorGUILayout.BeginHorizontal();
        string[] names = worlds.Select(w => w.name).ToArray();
        int index = EditorGUILayout.Popup("World", worlds.IndexOf(world), names);
        if (index >= 0 && index < worlds.Count && worlds[index] != world)
        {
            Pick(worlds[index]);
        }

        if (GUILayout.Button("Refresh", GUILayout.Width(70f)))
        {
            RefreshWorlds();
        }

        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// The name, size and origin of a new world, with what keeps it from being created, and the button creating it.
    /// </summary>
    private void DrawNewWorld()
    {
        EditorGUILayout.LabelField("New world", EditorStyles.boldLabel);
        newName = EditorGUILayout.TextField("Name", newName);
        newSize = EditorGUILayout.Vector2IntField("Size in chunks", newSize);
        newSize = new Vector2Int(Mathf.Clamp(newSize.x, 1, WorldDefinition.MaxChunksPerSide), Mathf.Clamp(newSize.y, 1, WorldDefinition.MaxChunksPerSide));
        autoOrigin = EditorGUILayout.Toggle("First free origin", autoOrigin);
        string problem = WorldAuthoring.NameProblem(newName);
        if (autoOrigin)
        {
            if (WorldLayout.TryFindFreeOrigin(newSize, worlds, out Vector2Int free))
            {
                newOrigin = free;
            }
            else
            {
                problem ??= "The layout has no free slot for a world this size.";
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Vector2IntField("Origin", newOrigin);
            }
        }
        else
        {
            newOrigin = EditorGUILayout.Vector2IntField("Origin", newOrigin);
        }

        string layout = WorldLayout.Problem(newOrigin, newSize, worlds);
        problem ??= layout != null ? $"At {newOrigin}, {layout}." : null;
        createChunks = EditorGUILayout.Toggle("Create every chunk", createChunks);
        listWorld = EditorGUILayout.Toggle("List in Game Scenes", listWorld);
        if (problem != null)
        {
            EditorGUILayout.HelpBox(problem, MessageType.Warning);
        }

        using (new EditorGUI.DisabledScope(problem != null))
        {
            if (GUILayout.Button("Create world"))
            {
                Run(CreateWorld);
            }
        }
    }

    /// <summary>
    /// The picked world's origin, size, layout check and listing.
    /// </summary>
    private void DrawWorld()
    {
        EditorGUILayout.LabelField(world.name, EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Origin", $"{world.Origin}   size {world.SizeInChunks} chunks");
        string layout = WorldLayout.Problem(world.Origin, world.SizeInChunks, worlds, world);
        if (layout != null)
        {
            EditorGUILayout.HelpBox($"The origin breaks the layout: {layout}.", MessageType.Error);
        }

        GameScenes gameScenes = ProjectScenes.LoadGameScenes();
        if (gameScenes != null && !gameScenes.Worlds.Contains(world))
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.HelpBox("Not listed in Game Scenes, so the game does not know it.", MessageType.Info);
            if (GUILayout.Button("List it", GUILayout.Width(70f)))
            {
                WorldAuthoring.ListInGameScenes(world);
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Open root scene"))
        {
            Run(() => WorldAuthoring.OpenRoot(world));
        }

        if (GUILayout.Button("Close every chunk") && SaveBeforeClosing(null, true))
        {
            Run(() => WorldAuthoring.CloseChunks(world, null, true));
        }

        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// The chunk grid, north up: each chunk's button shows whether it has a scene and whether it is open; then what
    /// can be done with the selected chunk.
    /// </summary>
    private void DrawChunks()
    {
        EditorGUILayout.LabelField("Chunks", EditorStyles.boldLabel);
        HashSet<Vector2Int> open = new(WorldAuthoring.OpenChunkCoords(world));
        Color normal = GUI.backgroundColor;
        for (int y = world.SizeInChunks.y - 1; y >= 0; y--)
        {
            EditorGUILayout.BeginHorizontal();
            for (int x = 0; x < world.SizeInChunks.x; x++)
            {
                Vector2Int coords = new(x, y);
                bool exists = world.HasChunk(coords);
                GUI.backgroundColor = coords == selected ? new Color(0.5f, 0.8f, 1f) : open.Contains(coords) ? new Color(0.6f, 1f, 0.6f) : exists ? normal : new Color(0.6f, 0.6f, 0.6f);
                string label = exists ? $"{x},{y}" : "+";
                if (GUILayout.Button(new GUIContent(label, exists ? (open.Contains(coords) ? "Open" : "Closed") : "No chunk yet"), GUILayout.Width(ChunkButtonSize), GUILayout.Height(ChunkButtonSize)))
                {
                    selected = coords;
                }
            }

            GUI.backgroundColor = normal;
            EditorGUILayout.EndHorizontal();
        }

        GUI.backgroundColor = normal;
        EditorGUILayout.LabelField("Selected", $"chunk {selected}: {(world.HasChunk(selected) ? (open.Contains(selected) ? "open" : "closed") : "no scene")}");
        EditorGUILayout.BeginHorizontal();
        if (!world.HasChunk(selected))
        {
            if (GUILayout.Button("Create chunk"))
            {
                Run(() => WorldAuthoring.CreateChunk(world, selected));
            }
        }
        else
        {
            if (GUILayout.Button("Open with neighbours"))
            {
                Run(() => WorldAuthoring.OpenChunk(world, selected, true));
            }

            if (GUILayout.Button("Open alone"))
            {
                Run(() => WorldAuthoring.OpenChunk(world, selected, false));
            }

            HashSet<Vector2Int> keep = new() { selected };
            if (GUILayout.Button("Close the others") && SaveBeforeClosing(keep, false))
            {
                Run(() => WorldAuthoring.CloseChunks(world, keep));
            }
        }

        EditorGUILayout.EndHorizontal();
        Grid grid = open.Contains(selected) ? WorldAuthoring.OpenChunkGrid(world, selected) : null;
        if (grid == null)
        {
            return;
        }

        TerrainBlendRenderer renderer = grid.GetComponent<TerrainBlendRenderer>();
        bool hasWalls = grid.transform.Find(WorldAuthoring.WallsName) != null;
        bool hasRaised = renderer == null || renderer.RaisedTilemap != null;
        if (hasWalls && hasRaised)
        {
            return;
        }

        EditorGUILayout.BeginHorizontal();
        if (!hasWalls && GUILayout.Button("Add walls"))
        {
            Run(() => WorldAuthoring.AddWalls(grid));
        }

        if (!hasRaised && GUILayout.Button("Add raised ground"))
        {
            Run(() => WorldAuthoring.AddRaised(grid));
        }

        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// The world's entries, the first its default, each with its waystone flag, with a warning for an entry on no
    /// chunk.
    /// </summary>
    private void DrawEntries()
    {
        if (worldData == null || worldData.targetObject != world)
        {
            worldData = new SerializedObject(world);
        }

        worldData.Update();
        EditorGUILayout.PropertyField(worldData.FindProperty("entries"), new GUIContent("Entries (the first is the default)"), true);
        worldData.ApplyModifiedProperties();
        foreach (WorldDefinition.Entry entry in world.Entries)
        {
            if (entry != null && !world.HasChunk(ChunkCoords.ChunkOf(ChunkCoords.CellOf(entry.Position))))
            {
                EditorGUILayout.HelpBox($"Entry '{entry.Name}' at {entry.Position} is on no chunk of the world.", MessageType.Warning);
            }
        }
    }

    /// <summary>
    /// The whole-world steps: layer lists, coast maps (with a warning once the ground of an open chunk changed or a
    /// check found stale maps), ids and validation.
    /// </summary>
    private void DrawSteps()
    {
        EditorGUILayout.LabelField("Whole world", EditorStyles.boldLabel);
        if (GUILayout.Button("Sync layer lists"))
        {
            Run(() => Report("Sync layer lists", WorldAuthoring.SyncLayerLists(world)));
        }

        if (staleCoasts != null && staleCoasts.Count > 0)
        {
            EditorGUILayout.HelpBox($"The coast maps of {string.Join(", ", staleCoasts)} are out of date.", MessageType.Warning);
        }
        else if (groundChanged)
        {
            EditorGUILayout.HelpBox("The ground of an open chunk changed: check or bake the coast maps.", MessageType.Info);
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Check coasts"))
        {
            Run(CheckCoasts);
        }

        if (GUILayout.Button("Bake changed coasts"))
        {
            Run(() => BakeCoasts(true));
        }

        if (GUILayout.Button("Bake every coast"))
        {
            Run(() => BakeCoasts(false));
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Repair ids"))
        {
            Run(() => Report("Repair ids", PersistentIds.Repair(world)));
        }

        if (GUILayout.Button("Validate"))
        {
            Run(Validate);
        }

        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// The last step's report.
    /// </summary>
    private void DrawMessages()
    {
        if (messages.Count == 0)
        {
            return;
        }

        messageScroll = EditorGUILayout.BeginScrollView(messageScroll, GUILayout.MaxHeight(140f));
        foreach (string line in messages)
        {
            EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
        }

        EditorGUILayout.EndScrollView();
    }

    /// <summary>
    /// Creates the new world, its chunks when asked, and lists it when asked, then picks it.
    /// </summary>
    private void CreateWorld()
    {
        List<string> lines = new();
        try
        {
            WorldDefinition created = WorldAuthoring.CreateWorld(newName, newOrigin, newSize);
            lines.Add($"Created {created.name} at {created.Origin}.");
            if (createChunks)
            {
                for (int y = 0; y < newSize.y; y++)
                {
                    for (int x = 0; x < newSize.x; x++)
                    {
                        lines.Add($"Created {WorldAuthoring.CreateChunk(created, new Vector2Int(x, y))}.");
                    }
                }
            }

            if (listWorld && WorldAuthoring.ListInGameScenes(created))
            {
                lines.Add("Listed it in Game Scenes.");
            }

            RefreshWorlds();
            Pick(created);
        }
        catch (System.Exception exception)
        {
            lines.Add(exception.Message);
            Debug.LogException(exception);
        }

        Report("Create world", lines);
    }

    /// <summary>
    /// Lists the chunks whose coast map is out of date.
    /// </summary>
    private void CheckCoasts()
    {
        staleCoasts = WorldAuthoring.StaleCoastMaps(world);
        groundChanged = false;
        Report("Check coasts", new List<string> { staleCoasts.Count == 0 ? "Every coast map is up to date." : $"Out of date: {string.Join(", ", staleCoasts)}." });
    }

    /// <summary>
    /// Validates the picked world, reporting each problem and logging it as an error.
    /// </summary>
    private void Validate()
    {
        List<string> problems = WorldValidation.ValidateWorld(world, worlds);
        Report("Validate", problems.Count == 0 ? new List<string> { $"{world.name} is valid." } : problems);
        foreach (string problem in problems)
        {
            Debug.LogError(problem, world);
        }
    }

    /// <summary>
    /// Bakes the coast maps of the chunks whose sea changed, or of every chunk.
    /// </summary>
    private void BakeCoasts(bool onlyChanged)
    {
        Report(onlyChanged ? "Bake changed coasts" : "Bake every coast", WorldAuthoring.BakeCoastMaps(world, onlyChanged));
        staleCoasts = null;
        groundChanged = false;
    }

    /// <summary>
    /// Asks to save the open scenes of the picked world's chunks that would close (all but <paramref name="keep"/>,
    /// and the root when <paramref name="root"/>); false when the user cancels.
    /// </summary>
    private bool SaveBeforeClosing(ICollection<Vector2Int> keep, bool root)
    {
        return EditorSceneManager.SaveModifiedScenesIfUserWantsTo(WorldAuthoring.OpenScenesExcept(world, keep, root).ToArray());
    }

    /// <summary>
    /// Runs <paramref name="step"/> after the window's GUI pass, since steps open and close scenes, reporting an
    /// exception instead of throwing it, and repaints the window.
    /// </summary>
    private void Run(System.Action step)
    {
        EditorApplication.delayCall += () =>
        {
            if (this == null)
            {
                return;
            }

            try
            {
                step();
            }
            catch (System.Exception exception)
            {
                Report("Error", new List<string> { exception.Message });
                Debug.LogException(exception);
            }

            Repaint();
        };
    }

    /// <summary>
    /// Shows <paramref name="lines"/> under <paramref name="title"/> as the last report.
    /// </summary>
    private void Report(string title, List<string> lines)
    {
        messages = new List<string> { $"{title}:" };
        messages.AddRange(lines.Count > 0 ? lines : new List<string> { "Nothing to do." });
        Repaint();
    }

    /// <summary>
    /// Reads every world in the project again, keeping the picked one.
    /// </summary>
    private void RefreshWorlds()
    {
        worlds = WorldLayout.AllWorlds();
        Pick(world != null && worlds.Contains(world) ? world : worlds.FirstOrDefault());
    }

    /// <summary>
    /// Picks <paramref name="picked"/>, remembering its chunk scenes' paths for the tile change check.
    /// </summary>
    private void Pick(WorldDefinition picked)
    {
        world = picked;
        worldData = null;
        staleCoasts = null;
        groundChanged = false;
        selected = Vector2Int.zero;
        chunkPaths.Clear();
        if (world == null)
        {
            return;
        }

        foreach (WorldDefinition.Chunk chunk in world.Chunks)
        {
            if (chunk != null && chunk.Scene != null && chunk.Scene.IsValid)
            {
                chunkPaths.Add(chunk.Scene.ScenePath);
            }
        }
    }

    /// <summary>
    /// Notes that the ground of an open chunk of the picked world changed.
    /// </summary>
    private void HandleTileChanged(Tilemap tilemap, Tilemap.SyncTile[] tiles)
    {
        if (!groundChanged && tilemap != null && chunkPaths.Contains(tilemap.gameObject.scene.path))
        {
            groundChanged = true;
            Repaint();
        }
    }

    /// <summary>
    /// Repaints when a scene opens, so the chunk grid shows it.
    /// </summary>
    private void HandleSceneOpened(Scene scene, OpenSceneMode mode)
    {
        Repaint();
    }

    /// <summary>
    /// Repaints when a scene closes, so the chunk grid shows it.
    /// </summary>
    private void HandleSceneClosed(Scene scene)
    {
        Repaint();
    }
}
