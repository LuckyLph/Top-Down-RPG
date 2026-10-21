using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// <c>Tools/TopDownRPG/World/World Generator</c> (Docs/WorldGeneration.md: Behaviour: Generating): a thin window over a
/// <see cref="WorldGeneratorSession"/>, as the World Editor is over <see cref="WorldAuthoring"/>. It shows a
/// <see cref="WorldGenerator"/> asset's composition (<see cref="WorldGeneratorPreview"/>, zoomed to fit or by whole
/// pixels per cell, the selected chunks framed and the cell under the pointer described), its warnings with those that
/// stop a write set apart, a grid of its chunks, north up, each with its state, layer count and record flags, where
/// chunks are selected and locked or allowed wide with undo, the three write buttons, each asking first with the chunks
/// it writes and the warnings, the last write's report, and the generator's inspector. Writes and composing run after
/// the GUI pass. Its only standing work is following changes to the generator, its world and its region generators
/// (inspector edits, undo, asset changes, the window gaining focus), which recompose the world a moment after the last
/// one. Disabled in play mode.
/// </summary>
public sealed class WorldGeneratorWindow : EditorWindow
{
    private const string MenuPath = ProjectScenes.MenuRoot + "World/World Generator";
    private const float ChunkButtonSize = 30f;
    private const float PreviewShare = 0.45f;
    private const float MinPreviewHeight = 120f;
    private const float ListHeight = 160f;
    private const float SelectionFrame = 2f;
    private const int SelectionListed = 8;

    private static readonly int[] Zooms = { 0, 1, 2, 3, 4, 6, 8 };
    private static readonly string[] ZoomNames = { "Fit", "1 px a cell", "2 px", "3 px", "4 px", "6 px", "8 px" };
    private static readonly Color ChangedColour = new(1f, 0.78f, 0.35f);
    private static readonly Color LockedColour = new(0.55f, 0.68f, 1f);
    private static readonly Color MissingColour = new(0.45f, 0.45f, 0.45f);
    private static readonly Color SelectionColour = new(1f, 1f, 1f, 0.95f);

    [SerializeField] private WorldGenerator generator;
    [SerializeField] private List<Vector2Int> selection = new();
    [SerializeField] private List<string> report = new();
    [SerializeField] private int zoom;
    [SerializeField] private bool showWarnings = true;
    [SerializeField] private bool showSettings = true;

    private WorldGeneratorSession session;
    private Editor generatorEditor;
    private Texture2D previewTexture;
    private int drawnVersion = -1;
    private bool following;
    private bool hovering;
    private Vector2Int hovered;
    private GUIStyle chunkStyle;
    private Vector2 scroll;
    private Vector2 previewScroll;
    private Vector2 warningScroll;
    private Vector2 reportScroll;

    /// <summary>
    /// Opens the window.
    /// </summary>
    [MenuItem(MenuPath, priority = 45)]
    public static void Open()
    {
        GetWindow<WorldGeneratorWindow>("World Generator");
    }

    /// <summary>
    /// Picks the first generator asset when none is set, starts following changes and has the world composed.
    /// </summary>
    private void OnEnable()
    {
        wantsMouseMove = true;
        if (generator == null)
        {
            string[] guids = AssetDatabase.FindAssets($"t:{nameof(WorldGenerator)}");
            generator = guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<WorldGenerator>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
        }

        Undo.undoRedoPerformed += HandleChange;
        ObjectChangeEvents.changesPublished += HandleObjectChanges;
        EditorApplication.playModeStateChanged += HandlePlayModeChanged;
        EditorSceneManager.sceneDirtied += HandleScene;
        EditorSceneManager.sceneSaved += HandleScene;
        EditorSceneManager.sceneClosed += HandleScene;
        Pick(generator, true);
    }

    /// <summary>
    /// Stops following changes and destroys the inspector and the preview texture.
    /// </summary>
    private void OnDisable()
    {
        Undo.undoRedoPerformed -= HandleChange;
        ObjectChangeEvents.changesPublished -= HandleObjectChanges;
        EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
        EditorSceneManager.sceneDirtied -= HandleScene;
        EditorSceneManager.sceneSaved -= HandleScene;
        EditorSceneManager.sceneClosed -= HandleScene;
        StopFollowing();
        if (generatorEditor != null)
        {
            DestroyImmediate(generatorEditor);
        }

        if (previewTexture != null)
        {
            DestroyImmediate(previewTexture);
        }
    }

    /// <summary>
    /// Checks whether anything the composition is made from changed while another window had the focus.
    /// </summary>
    private void OnFocus()
    {
        HandleChange();
    }

    /// <summary>
    /// Draws the picker, the preview, and in a scroll view the warnings, the chunks, the write buttons, the report and
    /// the generator's settings; everything disabled in play mode.
    /// </summary>
    private void OnGUI()
    {
        bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
        if (playing)
        {
            EditorGUILayout.HelpBox("Disabled in play mode: world generation writes chunk scenes, and a change made during play is lost when it stops.", MessageType.Info);
        }

        using (new EditorGUI.DisabledScope(playing))
        {
            DrawPicker();
            if (session == null)
            {
                return;
            }

            if (session.Composition == null)
            {
                EditorGUILayout.LabelField("Composing...", EditorStyles.miniLabel);
                return;
            }

            bool composed = DrawPreview();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawWarnings();
            if (composed)
            {
                EditorGUILayout.Space();
                DrawChunks();
                EditorGUILayout.Space();
                DrawWrite();
            }

            DrawReport();
            EditorGUILayout.Space();
            DrawSettings();
            EditorGUILayout.EndScrollView();
        }
    }

    /// <summary>
    /// The generator picker, Recompose, and the generator's world.
    /// </summary>
    private void DrawPicker()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            WorldGenerator picked = (WorldGenerator)EditorGUILayout.ObjectField("Generator", generator, typeof(WorldGenerator), false);
            if (EditorGUI.EndChangeCheck())
            {
                Defer(() => Pick(picked, false));
            }

            using (new EditorGUI.DisabledScope(session == null))
            {
                if (GUILayout.Button(new GUIContent("Recompose", "Composes the world again now, as after a change."), GUILayout.Width(80f)))
                {
                    session.ScheduleRecompose(EditorApplication.timeSinceStartup - WorldGeneratorSession.RecomposeDelay);
                    Follow();
                }
            }
        }

        if (generator == null)
        {
            EditorGUILayout.HelpBox("Pick a world generator asset, or create one with Create > TopDownRPG > World > World Generator under Assets/Settings/Generation.", MessageType.Info);
            return;
        }

        WorldDefinition world = generator.World;
        if (world == null)
        {
            EditorGUILayout.HelpBox("The generator has no world: create one in the World Editor and set it under World.", MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField("World", $"{world.name}: {world.SizeInChunks.x} x {world.SizeInChunks.y} chunks at {world.Origin}");
    }

    /// <summary>
    /// The preview, its zoom and a line on the cell under the pointer and on the composition; false, after showing why,
    /// when the composition failed.
    /// </summary>
    private bool DrawPreview()
    {
        WorldComposition composition = session.Composition;
        if (composition.Style == null)
        {
            EditorGUILayout.HelpBox(composition.Warnings.Count > 0 ? composition.Warnings[0] : "The world could not be composed.", MessageType.Error);
            return false;
        }

        if (previewTexture == null || drawnVersion != session.PreviewVersion)
        {
            previewTexture = WorldGeneratorPreview.Draw(composition, generator, previewTexture);
            drawnVersion = session.PreviewVersion;
        }

        zoom = Mathf.Clamp(zoom, 0, Zooms.Length - 1);
        int zoomed = EditorGUILayout.Popup("Zoom", zoom, ZoomNames);
        if (zoomed != zoom)
        {
            Defer(() => zoom = zoomed);
        }
        float maxHeight = Mathf.Max(MinPreviewHeight, position.height * PreviewShare);
        if (Zooms[zoom] == 0)
        {
            float width = position.width - 12f;
            float scale = Mathf.Min(width / previewTexture.width, maxHeight / previewTexture.height);
            Rect line = GUILayoutUtility.GetRect(width, previewTexture.height * scale);
            Rect area = new(line.x + (line.width - previewTexture.width * scale) / 2f, line.y, previewTexture.width * scale, previewTexture.height * scale);
            DrawPreviewArea(area, scale);
        }
        else
        {
            float scale = Zooms[zoom];
            previewScroll = EditorGUILayout.BeginScrollView(previewScroll, GUILayout.Height(Mathf.Min(maxHeight, previewTexture.height * scale + 16f)));
            Rect area = GUILayoutUtility.GetRect(previewTexture.width * scale, previewTexture.height * scale, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
            DrawPreviewArea(area, scale);
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.LabelField(hovering ? DescribeCell(composition, hovered) : "Click a chunk to select it, ctrl or shift click to add it.", EditorStyles.miniLabel);
        EditorGUILayout.LabelField(Summary(composition), EditorStyles.wordWrappedMiniLabel);
        return true;
    }

    /// <summary>
    /// Draws the preview texture into <paramref name="area"/> at <paramref name="scale"/> screen pixels a cell, frames
    /// the selected chunks, follows the pointer over it and selects the chunk clicked.
    /// </summary>
    private void DrawPreviewArea(Rect area, float scale)
    {
        GUI.DrawTexture(area, previewTexture, ScaleMode.StretchToFill);
        float side = ChunkCoords.Size * scale;
        foreach (Vector2Int coords in selection)
        {
            Rect frame = new(area.x + coords.x * side, area.yMax - (coords.y + 1) * side, side, side);
            EditorGUI.DrawRect(new Rect(frame.x, frame.y, frame.width, SelectionFrame), SelectionColour);
            EditorGUI.DrawRect(new Rect(frame.x, frame.yMax - SelectionFrame, frame.width, SelectionFrame), SelectionColour);
            EditorGUI.DrawRect(new Rect(frame.x, frame.y, SelectionFrame, frame.height), SelectionColour);
            EditorGUI.DrawRect(new Rect(frame.xMax - SelectionFrame, frame.y, SelectionFrame, frame.height), SelectionColour);
        }

        Event current = Event.current;
        bool inside = area.Contains(current.mousePosition);
        Vector2Int cell = new(Mathf.FloorToInt((current.mousePosition.x - area.x) / scale), Mathf.FloorToInt((area.yMax - current.mousePosition.y) / scale));
        if (current.type == EventType.MouseMove || current.type == EventType.MouseDrag)
        {
            if (inside != hovering || (inside && cell != hovered))
            {
                hovering = inside;
                hovered = cell;
                Repaint();
            }
        }
        else if (current.type == EventType.MouseDown && current.button == 0 && inside)
        {
            Select(ChunkCoords.ChunkOf(cell), current.control || current.command || current.shift);
            current.Use();
        }
    }

    /// <summary>
    /// The warnings, those that stop a write first and set apart.
    /// </summary>
    private void DrawWarnings()
    {
        WorldComposition composition = session.Composition;
        if (composition.Warnings.Count == 0)
        {
            EditorGUILayout.LabelField("No warnings.", EditorStyles.miniLabel);
            return;
        }

        int blocking = composition.BlockingWarnings.Count;
        showWarnings = EditorGUILayout.Foldout(showWarnings, $"Warnings: {composition.Warnings.Count}, {blocking} of them stopping a write", true);
        if (!showWarnings)
        {
            return;
        }

        warningScroll = EditorGUILayout.BeginScrollView(warningScroll, GUILayout.MaxHeight(ListHeight));
        if (blocking > 0)
        {
            EditorGUILayout.HelpBox($"Stop a write unless written anyway (layer lists, reachability):\n- {string.Join("\n- ", composition.BlockingWarnings)}", MessageType.Error);
        }

        List<string> others = composition.Warnings.FindAll(warning => !composition.BlockingWarnings.Contains(warning));
        if (others.Count > 0)
        {
            EditorGUILayout.HelpBox($"- {string.Join("\n- ", others)}", MessageType.Warning);
        }

        EditorGUILayout.EndScrollView();
    }

    /// <summary>
    /// The chunk grid, north up, each chunk's button tinted by its state and showing its coordinates, its layer count
    /// (red past 16 unless allowed wide), L when locked, + when missing and W when allowed wide; then the selection with
    /// its lock and wide allowance and buttons selecting chunks.
    /// </summary>
    private void DrawChunks()
    {
        WorldComposition composition = session.Composition;
        chunkStyle ??= new GUIStyle(GUI.skin.button) { fontSize = 9, richText = true, padding = new RectOffset(1, 1, 1, 1) };
        EditorGUILayout.LabelField("Chunks", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"{session.CountOf(WorldGeneratorSession.ChunkState.Unchanged)} unchanged, {session.CountOf(WorldGeneratorSession.ChunkState.Changed)} changed (amber), {session.CountOf(WorldGeneratorSession.ChunkState.Locked)} locked (blue, L), {session.CountOf(WorldGeneratorSession.ChunkState.Missing)} missing (grey, +); W: allowed wide.", EditorStyles.wordWrappedMiniLabel);
        Color normal = GUI.backgroundColor;
        bool additive = Event.current.control || Event.current.command || Event.current.shift;
        for (int y = composition.SizeInChunks.y - 1; y >= 0; y--)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int x = 0; x < composition.SizeInChunks.x; x++)
                {
                    Vector2Int coords = new(x, y);
                    WorldGeneratorSession.ChunkState state = session.StateOf(coords);
                    WorldComposition.Chunk chunk = composition.ChunkAt(coords);
                    GUI.backgroundColor = state switch
                    {
                        WorldGeneratorSession.ChunkState.Changed => ChangedColour,
                        WorldGeneratorSession.ChunkState.Locked => LockedColour,
                        WorldGeneratorSession.ChunkState.Missing => MissingColour,
                        _ => normal
                    };
                    bool selected = selection.Contains(coords);
                    if (GUILayout.Toggle(selected, ChunkLabel(chunk, state), chunkStyle, GUILayout.Width(ChunkButtonSize), GUILayout.Height(ChunkButtonSize)) != selected)
                    {
                        Select(coords, additive);
                    }
                }
            }
        }

        GUI.backgroundColor = normal;
        DrawSelection(composition);
    }

    /// <summary>
    /// The selected chunks, with Locked and Allowed wide applying to all of them (mixed when they differ), the one
    /// chunk's materials when only one is selected, and buttons selecting the changed chunks, every chunk or none.
    /// </summary>
    private void DrawSelection(WorldComposition composition)
    {
        if (selection.Count == 0)
        {
            EditorGUILayout.LabelField("Select chunks in the grid or the preview.", EditorStyles.miniLabel);
        }
        else
        {
            List<string> listed = new();
            for (int i = 0; i < selection.Count && i < SelectionListed; i++)
            {
                listed.Add($"({selection[i].x}, {selection[i].y})");
            }

            EditorGUILayout.LabelField("Selected", $"{string.Join(" ", listed)}{(selection.Count > SelectionListed ? $" and {selection.Count - SelectionListed} more" : string.Empty)}");
            if (selection.Count == 1 && composition.ChunkAt(selection[0]) != null)
            {
                WorldComposition.Chunk chunk = composition.ChunkAt(selection[0]);
                RectInt bounds = ChunkCoords.Bounds(chunk.Coords);
                int mobs = composition.Mobs.FindAll(mob => bounds.Contains(mob.Cell)).Count;
                EditorGUILayout.LabelField($"{session.StateOf(chunk.Coords)}; {chunk.Materials.Count} layers: {string.Join(", ", chunk.Materials.ConvertAll(material => material.name))}; {mobs} mobs.", EditorStyles.wordWrappedMiniLabel);
            }

            DrawFlag("Locked", "The writer never writes the chunk, so hand work in it stays.", record => record.Locked, value => session.SetLocked(selection, value));
            DrawFlag("Allowed wide", "The chunk may list more than 16 layers, drawn by the renderer's slower wide variant.", record => record.AllowWide, value => session.SetAllowWide(selection, value));
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Select changed"))
            {
                List<Vector2Int> changed = new();
                foreach (WorldComposition.Chunk chunk in composition.Chunks)
                {
                    WorldGeneratorSession.ChunkState state = session.StateOf(chunk.Coords);
                    if (state == WorldGeneratorSession.ChunkState.Changed || state == WorldGeneratorSession.ChunkState.Missing)
                    {
                        changed.Add(chunk.Coords);
                    }
                }

                Defer(() => selection = changed);
            }

            if (GUILayout.Button("Select every chunk"))
            {
                List<Vector2Int> every = composition.Chunks.ConvertAll(chunk => chunk.Coords);
                Defer(() => selection = every);
            }

            if (GUILayout.Button("Clear selection"))
            {
                Defer(() => selection.Clear());
            }
        }
    }

    /// <summary>
    /// A toggle of the selected chunks' record flag read by <paramref name="read"/>, mixed when they differ, which calls
    /// <paramref name="set"/> when changed.
    /// </summary>
    private void DrawFlag(string label, string tooltip, Func<ChunkRecord, bool> read, Action<bool> set)
    {
        int on = 0;
        foreach (Vector2Int coords in selection)
        {
            ChunkRecord record = generator.RecordOf(coords);
            on += record != null && read(record) ? 1 : 0;
        }

        EditorGUI.showMixedValue = on > 0 && on < selection.Count;
        EditorGUI.BeginChangeCheck();
        bool value = EditorGUILayout.Toggle(new GUIContent(label, tooltip), on == selection.Count);
        EditorGUI.showMixedValue = false;
        if (EditorGUI.EndChangeCheck())
        {
            set(value);
            Follow();
        }
    }

    /// <summary>
    /// Write changed chunks, Write selected chunks and Write every chunk, each with the number of chunks it writes and
    /// run once the GUI pass is over, with a note when chunk scenes of the world are open with unsaved changes.
    /// </summary>
    private void DrawWrite()
    {
        EditorGUILayout.LabelField("Write", EditorStyles.boldLabel);
        List<string> unsaved = session.OpenUnsavedChunks();
        if (unsaved.Count > 0)
        {
            EditorGUILayout.HelpBox($"Open with unsaved changes: {string.Join(", ", unsaved)}. Save or close them before writing.", MessageType.Warning);
        }

        int locked = session.CountOf(WorldGeneratorSession.ChunkState.Locked);
        int changed = session.CountOf(WorldGeneratorSession.ChunkState.Changed) + session.CountOf(WorldGeneratorSession.ChunkState.Missing);
        int selected = selection.FindAll(coords => session.Composition.ChunkAt(coords) != null && session.StateOf(coords) != WorldGeneratorSession.ChunkState.Locked).Count;
        int every = session.Composition.Chunks.Count - locked;
        using (new EditorGUILayout.HorizontalScope())
        {
            WriteButton($"Write changed chunks ({changed})", "Writes every chunk that changed since its last write or is missing; a chunk changed by hand is refused. Then the whole-world steps.", changed, WorldGeneratorSession.Scope.Changed);
            WriteButton($"Write selected chunks ({selected})", "Writes the selected chunks but the locked ones, forced: whatever their records say, hand work in them replaced. Then the whole-world steps.", selected, WorldGeneratorSession.Scope.Selected);
            WriteButton($"Write every chunk ({every})", "Writes every chunk but the locked ones, forced, unchanged ones too. Then the whole-world steps.", every, WorldGeneratorSession.Scope.Every);
        }
    }

    /// <summary>
    /// One write button, disabled when it would write nothing, running the write of <paramref name="scope"/> after the
    /// GUI pass.
    /// </summary>
    private void WriteButton(string label, string tooltip, int count, WorldGeneratorSession.Scope scope)
    {
        using (new EditorGUI.DisabledScope(count == 0))
        {
            if (GUILayout.Button(new GUIContent(label, tooltip)))
            {
                EditorApplication.delayCall += () => RunWrite(scope);
            }
        }
    }

    /// <summary>
    /// The last write's report.
    /// </summary>
    private void DrawReport()
    {
        if (report.Count == 0)
        {
            return;
        }

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Last write", EditorStyles.boldLabel);
            if (GUILayout.Button("Clear", GUILayout.Width(60f)))
            {
                Defer(() => report.Clear());
            }
        }

        reportScroll = EditorGUILayout.BeginScrollView(reportScroll, GUILayout.MaxHeight(ListHeight));
        foreach (string line in report)
        {
            EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
        }

        EditorGUILayout.EndScrollView();
    }

    /// <summary>
    /// The generator's inspector, whose changes recompose the world a moment later.
    /// </summary>
    private void DrawSettings()
    {
        showSettings = EditorGUILayout.Foldout(showSettings, "Settings", true);
        if (!showSettings)
        {
            return;
        }

        Editor.CreateCachedEditor(generator, null, ref generatorEditor);
        EditorGUI.BeginChangeCheck();
        generatorEditor.OnInspectorGUI();
        if (EditorGUI.EndChangeCheck())
        {
            HandleChange();
        }
    }

    /// <summary>
    /// Plans the write of <paramref name="scope"/>, asks (<see cref="Ask"/>) and writes, keeping the report.
    /// </summary>
    private void RunWrite(WorldGeneratorSession.Scope scope)
    {
        if (this == null || session == null)
        {
            return;
        }

        try
        {
            WorldGeneratorSession.WritePlan plan = session.Plan(scope, selection);
            report = session.Write(plan, Ask);
        }
        catch (Exception exception)
        {
            report = new List<string> { $"Error: {exception.Message}" };
            Debug.LogException(exception);
        }

        Repaint();
    }

    /// <summary>
    /// Asks whether to write <paramref name="plan"/>, offering Write anyway when warnings stop it.
    /// </summary>
    private WorldGeneratorSession.Answer Ask(WorldGeneratorSession.WritePlan plan)
    {
        string question = plan.Question(generator.World.name);
        if (plan.BlockingWarnings.Count > 0)
        {
            return EditorUtility.DisplayDialog("Write chunks despite the warnings?", question, "Write anyway", "Cancel") ? WorldGeneratorSession.Answer.WriteAnyway : WorldGeneratorSession.Answer.Cancel;
        }

        return EditorUtility.DisplayDialog("Write chunks", question, "Write", "Cancel") ? WorldGeneratorSession.Answer.Write : WorldGeneratorSession.Answer.Cancel;
    }

    /// <summary>
    /// After the GUI pass, selects only the chunk at <paramref name="coords"/>, or with <paramref name="additive"/> adds
    /// it to the selection or takes it out.
    /// </summary>
    private void Select(Vector2Int coords, bool additive)
    {
        if (session.Composition.ChunkAt(coords) == null)
        {
            return;
        }

        Defer(() =>
        {
            if (!additive)
            {
                selection = new List<Vector2Int> { coords };
            }
            else if (!selection.Remove(coords))
            {
                selection.Add(coords);
            }
        });
    }

    /// <summary>
    /// Runs <paramref name="change"/> once the GUI pass is over and redraws, so a pass never draws a different set of
    /// controls than it laid out.
    /// </summary>
    private void Defer(Action change)
    {
        EditorApplication.delayCall += () =>
        {
            if (this != null)
            {
                change();
                Repaint();
            }
        };
    }

    /// <summary>
    /// Works on <paramref name="picked"/> from now on, with a new session composed at the next editor update, keeping
    /// the selection when <paramref name="keepSelection"/>.
    /// </summary>
    private void Pick(WorldGenerator picked, bool keepSelection)
    {
        generator = picked;
        session = picked != null ? new WorldGeneratorSession(picked) : null;
        drawnVersion = -1;
        hovering = false;
        if (!keepSelection)
        {
            selection.Clear();
            report.Clear();
        }

        if (session != null)
        {
            session.ScheduleRecompose(EditorApplication.timeSinceStartup - WorldGeneratorSession.RecomposeDelay);
            Follow();
        }

        Repaint();
    }

    /// <summary>
    /// Follows a change that may concern the generator: the session recomposes a moment later when what it composes
    /// from changed, and the window redraws.
    /// </summary>
    private void HandleChange()
    {
        if (generator == null && session != null)
        {
            Pick(null, false);
            return;
        }

        if (session != null && session.Composition != null)
        {
            session.NoteChange(EditorApplication.timeSinceStartup);
            Follow();
        }

        Repaint();
    }

    /// <summary>
    /// Follows changes to the properties of the generator, its world or its region generators.
    /// </summary>
    private void HandleObjectChanges(ref ObjectChangeEventStream stream)
    {
        if (session == null)
        {
            return;
        }

        for (int i = 0; i < stream.length; i++)
        {
            if (stream.GetEventType(i) != ObjectChangeKind.ChangeAssetObjectProperties)
            {
                continue;
            }

            stream.GetChangeAssetObjectPropertiesEvent(i, out ChangeAssetObjectPropertiesEventArgs change);
            if (Concerns(EditorUtility.EntityIdToObject(change.entityId)))
            {
                HandleChange();
                return;
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="changed"/> is the generator, its world or one of its region generators.
    /// </summary>
    private bool Concerns(UnityEngine.Object changed)
    {
        if (changed == null || generator == null)
        {
            return false;
        }

        if (changed == generator || changed == generator.World)
        {
            return true;
        }

        foreach (RegionPlacement placement in generator.Regions)
        {
            if (placement != null && placement.Generator == changed)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Catches up with the changes made during play once it stops.
    /// </summary>
    private void HandlePlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredEditMode)
        {
            HandleChange();
        }
        else
        {
            Repaint();
        }
    }

    /// <summary>
    /// Redraws when a scene is changed, saved or closed, so the note on unsaved chunk scenes follows.
    /// </summary>
    private void HandleScene(Scene scene)
    {
        Repaint();
    }

    /// <summary>
    /// Runs <see cref="HandleUpdate"/> on each editor update while the session waits to recompose, outside play mode.
    /// </summary>
    private void Follow()
    {
        if (!following && session != null && session.IsRecomposeScheduled && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.update += HandleUpdate;
            following = true;
        }
    }

    /// <summary>
    /// Stops running <see cref="HandleUpdate"/>.
    /// </summary>
    private void StopFollowing()
    {
        if (following)
        {
            EditorApplication.update -= HandleUpdate;
            following = false;
        }
    }

    /// <summary>
    /// Recomposes once the session's time has come, then stops following updates.
    /// </summary>
    private void HandleUpdate()
    {
        if (session == null || !session.IsRecomposeScheduled || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            StopFollowing();
            return;
        }

        if (session.FollowRecompose(EditorApplication.timeSinceStartup))
        {
            StopFollowing();
            Repaint();
        }
    }

    /// <summary>
    /// A chunk button's label (coordinates, then its layer count, red past 16 unless allowed wide, with L when locked,
    /// + when missing and W when allowed wide) and its tooltip.
    /// </summary>
    private static GUIContent ChunkLabel(WorldComposition.Chunk chunk, WorldGeneratorSession.ChunkState state)
    {
        int count = chunk.Materials.Count;
        string layers = count > TerrainControlMap.NarrowLayers && !chunk.AllowWide ? $"<color=#ff4a4a>{count}</color>" : count.ToString();
        string marks = (state == WorldGeneratorSession.ChunkState.Locked ? "L" : state == WorldGeneratorSession.ChunkState.Missing ? "+" : string.Empty) + (chunk.AllowWide ? "W" : string.Empty);
        string tooltip = $"Chunk ({chunk.Coords.x}, {chunk.Coords.y}): {state}, {count} layers{(chunk.AllowWide ? ", allowed wide" : string.Empty)}.";
        return new GUIContent($"{chunk.Coords.x},{chunk.Coords.y}\n{layers}{marks}", tooltip);
    }

    /// <summary>
    /// What lies on <paramref name="cell"/>: its chunk, owner, top material and raised ground.
    /// </summary>
    private static string DescribeCell(WorldComposition composition, Vector2Int cell)
    {
        if (!composition.Contains(cell))
        {
            return string.Empty;
        }

        int owner = composition.RegionAt(cell);
        string region = owner >= 0 ? composition.Regions[owner].Key : owner == WorldComposition.Wilds ? "the wilds" : "the sea";
        TerrainMaterial top = composition.TopAt(cell);
        byte raised = composition.RaisedAt(cell);
        string height = raised == RaisedGround.Raised ? ", raised" : raised == RaisedGround.Ramp ? ", ramp" : string.Empty;
        Vector2Int chunk = ChunkCoords.ChunkOf(cell);
        return $"Cell ({cell.x}, {cell.y}) in chunk ({chunk.x}, {chunk.y}): {region}, {(top != null ? top.name : "no ground")}{height}.";
    }

    /// <summary>
    /// The composition's counts, its chunks' layer range and how long each step took.
    /// </summary>
    private string Summary(WorldComposition composition)
    {
        int wilds = composition.Mobs.FindAll(mob => composition.RegionAt(mob.Cell) == WorldComposition.Wilds).Count;
        int fewest = int.MaxValue;
        int most = 0;
        foreach (WorldComposition.Chunk chunk in composition.Chunks)
        {
            fewest = Mathf.Min(fewest, chunk.Materials.Count);
            most = Mathf.Max(most, chunk.Materials.Count);
        }

        List<string> steps = new();
        foreach (WorldComposition.StepTime step in composition.StepTimes)
        {
            steps.Add($"{step.Step.ToLowerInvariant()} {step.Milliseconds:0}");
        }

        return $"{composition.Regions.Count} regions, {composition.Roads.Count} roads, {composition.Mobs.Count} mobs ({wilds} in the wilds), {composition.Entries.Count} entries; chunks list {fewest} to {most} layers. Composed in {session.ComposeMilliseconds:0} ms: {string.Join(", ", steps)}.";
    }
}
