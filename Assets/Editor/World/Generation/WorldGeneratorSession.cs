using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// What the World Generator window works on (Docs/WorldGeneration.md: Behaviour: Generating), kept apart from its GUI
/// so tests and tools drive every step with no dialog: one generator's composition, composed again only when what it is
/// composed from changes (the generator's settings, of its chunk records only the wide allowances and wilds seeds, and
/// not its written entries; its world's size and chunks; its region generators' settings), <see cref="RecomposeDelay"/>
/// seconds after the last
/// change; each chunk's state, from its record and the composed hash alone, never opening a scene; the
/// <see cref="WritePlan"/> of what each write button would write; the write, after a confirmation the caller passes in,
/// then the whole-world steps, reported a line per chunk and per step; and the records' locks and wide allowances, set
/// with undo.
/// </summary>
internal sealed class WorldGeneratorSession
{
    public const double RecomposeDelay = 0.4;

    private const int ChunksInQuestion = 40;
    private const int WarningsInQuestion = 8;

    private static readonly Regex Records = new("\"chunks\":\\[[^\\]]*\\]");
    private static readonly Regex Entries = new("\"writtenEntries\":\\[[^\\]]*\\]");

    /// <summary>
    /// A chunk's state in the chunk grid: unchanged (its composed hash is its record's), changed (it is not, or the
    /// chunk was never written), locked (its record keeps the writer away) or missing (its world has no scene for it,
    /// so a write creates it).
    /// </summary>
    public enum ChunkState
    {
        Unchanged,
        Changed,
        Locked,
        Missing
    }

    /// <summary>
    /// What a write button writes: the changed and missing chunks; the selected ones, forced; or every chunk, forced.
    /// Locked chunks never.
    /// </summary>
    public enum Scope
    {
        Changed,
        Selected,
        Every
    }

    /// <summary>
    /// The answer to a write's question.
    /// </summary>
    public enum Answer
    {
        Cancel,
        Write,
        WriteAnyway
    }

    /// <summary>
    /// What one write button would do now: the composition it writes, the chunks it covers and among them those it would
    /// write, in row order (and those it creates), how many it leaves out as unchanged or locked, the composition's
    /// warnings and those that stop it unless written anyway, and the problems that stop it whatever the answer.
    /// </summary>
    public sealed class WritePlan
    {
        /// <summary>
        /// An empty plan of <paramref name="scope"/> over <paramref name="composition"/>, composed from
        /// <paramref name="settings"/>.
        /// </summary>
        internal WritePlan(Scope scope, WorldComposition composition, string settings)
        {
            Scope = scope;
            Composition = composition;
            Settings = settings;
        }

        public Scope Scope { get; }

        /// <summary>
        /// Whether its chunks are written whatever their records say (<see cref="WorldGenerationWriter.Write"/>'s force):
        /// for the selected chunks and every chunk.
        /// </summary>
        public bool Force => Scope != Scope.Changed;

        public WorldComposition Composition { get; }

        /// <summary>
        /// Every chunk the button covers, in row order, those it leaves out included, which the writer is given so it
        /// reports each one: every chunk, or the selected ones inside the world.
        /// </summary>
        public List<Vector2Int> Targets { get; } = new();

        public List<Vector2Int> Chunks { get; } = new();
        public List<Vector2Int> Created { get; } = new();
        public int Unchanged { get; internal set; }
        public int Locked { get; internal set; }
        public List<string> Problems { get; } = new();
        public IReadOnlyList<string> Warnings => Composition.Warnings;
        public IReadOnlyList<string> BlockingWarnings => Composition.BlockingWarnings;
        internal string Settings { get; }

        /// <summary>
        /// The question asked before writing <paramref name="worldName"/>: the chunks it writes (the first
        /// <see cref="ChunksInQuestion"/>), those it creates, what forcing means, the steps that follow, and the
        /// warnings, those that stop it set apart.
        /// </summary>
        public string Question(string worldName)
        {
            StringBuilder text = new();
            text.Append($"Write {Chunks.Count} chunks of {worldName}");
            text.Append(Created.Count > 0 ? $", creating {Created.Count} of them" : string.Empty);
            text.AppendLine($": {List(Chunks, ChunksInQuestion)}.");
            if (Unchanged > 0 || Locked > 0)
            {
                text.AppendLine($"Left out: {Unchanged} unchanged, {Locked} locked.");
            }

            text.AppendLine(Force
                ? "Forced: each is written whatever its record says, so any hand work in it is replaced. Lock a chunk to keep it."
                : "A chunk changed by hand since its last write is refused and named.");
            text.AppendLine("Then: Sync layer lists, Bake changed coasts, Repair ids, Validate. Writing is no undo step; git is the way back.");
            if (BlockingWarnings.Count > 0)
            {
                text.AppendLine();
                text.AppendLine($"{BlockingWarnings.Count} warnings stop a write unless written anyway (layer lists, reachability):");
                AppendLines(text, BlockingWarnings);
            }

            int others = Warnings.Count - BlockingWarnings.Count;
            if (others > 0)
            {
                text.AppendLine();
                text.AppendLine($"{others} other warnings:");
                List<string> rest = new();
                foreach (string warning in Warnings)
                {
                    if (!Contains(BlockingWarnings, warning))
                    {
                        rest.Add(warning);
                    }
                }

                AppendLines(text, rest);
            }

            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// Appends the first <see cref="WarningsInQuestion"/> of <paramref name="lines"/>, one per line, and how many
        /// more there are.
        /// </summary>
        private static void AppendLines(StringBuilder text, IReadOnlyList<string> lines)
        {
            for (int i = 0; i < lines.Count && i < WarningsInQuestion; i++)
            {
                text.AppendLine($"- {lines[i]}");
            }

            if (lines.Count > WarningsInQuestion)
            {
                text.AppendLine($"- and {lines.Count - WarningsInQuestion} more, listed in the window.");
            }
        }

        /// <summary>
        /// The first <paramref name="most"/> of <paramref name="chunks"/> and how many more there are.
        /// </summary>
        private static string List(List<Vector2Int> chunks, int most)
        {
            List<string> listed = new();
            for (int i = 0; i < chunks.Count && i < most; i++)
            {
                listed.Add($"({chunks[i].x}, {chunks[i].y})");
            }

            return string.Join(" ", listed) + (chunks.Count > most ? $" and {chunks.Count - most} more" : string.Empty);
        }

        /// <summary>
        /// Whether <paramref name="lines"/> holds <paramref name="line"/>.
        /// </summary>
        private static bool Contains(IReadOnlyList<string> lines, string line)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] == line)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private ChunkState[] states = Array.Empty<ChunkState>();
    private string composedSettings;
    private string drawnLocks;
    private double recomposeAt = -1.0;

    /// <summary>
    /// A session over <paramref name="generator"/>, not composed yet.
    /// </summary>
    public WorldGeneratorSession(WorldGenerator generator)
    {
        Generator = generator;
    }

    public WorldGenerator Generator { get; }

    /// <summary>
    /// The last composition, or null before the first.
    /// </summary>
    public WorldComposition Composition { get; private set; }

    /// <summary>
    /// How long the last composition took, in milliseconds.
    /// </summary>
    public double ComposeMilliseconds { get; private set; }

    /// <summary>
    /// Changes whenever the preview would draw differently: a new composition, or a chunk locked or unlocked.
    /// </summary>
    public int PreviewVersion { get; private set; }

    public bool IsRecomposeScheduled => recomposeAt >= 0.0;

    /// <summary>
    /// Whether what the composition is composed from changed since it was composed (or it never was).
    /// </summary>
    public bool IsStale => Composition == null || SettingsKey() != composedSettings;

    /// <summary>
    /// Composes the world now, cancelling a recompose that was waiting, and reads the chunk states again.
    /// </summary>
    public void Recompose()
    {
        recomposeAt = -1.0;
        composedSettings = SettingsKey();
        Stopwatch watch = Stopwatch.StartNew();
        Composition = Generator.Compose();
        ComposeMilliseconds = watch.Elapsed.TotalMilliseconds;
        drawnLocks = null;
        PreviewVersion++;
        Refresh();
    }

    /// <summary>
    /// Has the world composed <see cref="RecomposeDelay"/> seconds after <paramref name="now"/> (the editor's time since
    /// startup), pushing back a recompose already waiting.
    /// </summary>
    public void ScheduleRecompose(double now)
    {
        recomposeAt = now + RecomposeDelay;
    }

    /// <summary>
    /// Follows a change that may concern the generator: when what the composition is composed from changed, schedules a
    /// recompose (<see cref="ScheduleRecompose"/>), else cancels one waiting; either way reads the chunk states again.
    /// Returns whether a recompose is waiting.
    /// </summary>
    public bool NoteChange(double now)
    {
        if (SettingsKey() != composedSettings)
        {
            ScheduleRecompose(now);
        }
        else
        {
            recomposeAt = -1.0;
        }

        Refresh();
        return IsRecomposeScheduled;
    }

    /// <summary>
    /// Composes the world when a recompose is waiting and <paramref name="now"/> (the editor's time since startup) has
    /// reached it. Returns whether it composed. The window calls it on each editor update while one waits; tests call it
    /// directly.
    /// </summary>
    public bool FollowRecompose(double now)
    {
        if (!IsRecomposeScheduled || now < recomposeAt)
        {
            return false;
        }

        Recompose();
        return true;
    }

    /// <summary>
    /// The state of the chunk at <paramref name="coords"/> as last read (<see cref="ChunkState"/>); missing outside the
    /// composed world.
    /// </summary>
    public ChunkState StateOf(Vector2Int coords)
    {
        Vector2Int size = Composition != null ? Composition.SizeInChunks : Vector2Int.zero;
        if (coords.x < 0 || coords.y < 0 || coords.x >= size.x || coords.y >= size.y)
        {
            return ChunkState.Missing;
        }

        return states[coords.y * size.x + coords.x];
    }

    /// <summary>
    /// How many chunks of the composed world are in <paramref name="state"/>.
    /// </summary>
    public int CountOf(ChunkState state)
    {
        int count = 0;
        foreach (ChunkState chunk in states)
        {
            count += chunk == state ? 1 : 0;
        }

        return count;
    }

    /// <summary>
    /// What the button of <paramref name="scope"/> would write now, composing first when the composition is stale or a
    /// recompose is waiting: every changed or missing chunk; or the chunks of <paramref name="selection"/>; or every
    /// chunk; never a locked one, in row order. Problems stop it: play mode, no world, a composition that failed, a world
    /// that is no asset, chunk scenes of the world open with unsaved changes (the writer and the whole-world steps save
    /// the chunk scenes they change, unsaved work included), and chunks to create while an untitled scene has unsaved
    /// changes (creating one would replace it).
    /// </summary>
    public WritePlan Plan(Scope scope, IEnumerable<Vector2Int> selection)
    {
        if (IsStale || IsRecomposeScheduled)
        {
            Recompose();
        }

        WritePlan plan = new(scope, Composition, composedSettings);
        WorldDefinition world = Generator.World;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            plan.Problems.Add("World generation writes outside play mode only.");
        }

        if (world == null || Composition.Style == null)
        {
            plan.Problems.Add(world == null ? "The world generator has no world." : Composition.Warnings[0]);
            return plan;
        }

        if (!EditorUtility.IsPersistent(world))
        {
            plan.Problems.Add($"{world.name} is not saved as an asset, so it has no chunk scenes to write.");
        }

        List<string> unsaved = OpenUnsavedChunks();
        if (unsaved.Count > 0)
        {
            plan.Problems.Add($"Chunk scenes of {world.name} are open with unsaved changes: {string.Join(", ", unsaved)}. Save or close them first: the writer and the whole-world steps save the chunk scenes they change, unsaved work included.");
        }

        HashSet<Vector2Int> picked = scope == Scope.Selected ? new HashSet<Vector2Int>(selection ?? Array.Empty<Vector2Int>()) : null;
        foreach (WorldComposition.Chunk chunk in Composition.Chunks)
        {
            if (picked != null && !picked.Contains(chunk.Coords))
            {
                continue;
            }

            plan.Targets.Add(chunk.Coords);
            ChunkState state = StateOf(chunk.Coords);
            if (state == ChunkState.Locked)
            {
                plan.Locked++;
            }
            else if (scope == Scope.Changed && state == ChunkState.Unchanged)
            {
                plan.Unchanged++;
            }
            else
            {
                plan.Chunks.Add(chunk.Coords);
                if (state == ChunkState.Missing)
                {
                    plan.Created.Add(chunk.Coords);
                }
            }
        }

        if (plan.Created.Count > 0 && HasUnsavedUntitledScene())
        {
            plan.Problems.Add($"{plan.Created.Count} chunks are to be created and an untitled scene has unsaved changes, which creating a chunk would replace; save or close it first.");
        }

        return plan;
    }

    /// <summary>
    /// Writes <paramref name="plan"/>'s chunks after asking <paramref name="confirm"/> (the window's dialog, or a fixed
    /// answer from a test or a tool), then runs the whole-world steps when any chunk was written
    /// (<see cref="WorldGenerationWriter.Write"/>, <see cref="WorldGenerationWriter.RunWorldSteps"/>). Nothing is asked
    /// or written when the plan has problems or no chunk, or when the settings changed since it was made; a plan with
    /// warnings that stop a write is written only when the answer is to write anyway. Returns the report: what was done,
    /// each chunk's line, then each step's line with its time and the step's own lines.
    /// </summary>
    public List<string> Write(WritePlan plan, Func<WritePlan, Answer> confirm)
    {
        List<string> report = new();
        if (plan.Problems.Count > 0)
        {
            report.Add("Nothing written:");
            report.AddRange(plan.Problems);
            return report;
        }

        if (plan.Chunks.Count == 0)
        {
            report.Add($"Nothing to write: {plan.Unchanged} chunks unchanged, {plan.Locked} locked.");
            return report;
        }

        if (plan.Settings != SettingsKey())
        {
            report.Add("Nothing written: the settings changed since the chunks to write were listed; list them again.");
            return report;
        }

        Answer answer = confirm(plan);
        bool anyway = answer == Answer.WriteAnyway;
        if (answer == Answer.Cancel || (plan.BlockingWarnings.Count > 0 && !anyway))
        {
            report.Add("Cancelled; nothing written.");
            return report;
        }

        WorldDefinition world = Generator.World;
        Stopwatch watch = Stopwatch.StartNew();
        List<WorldGenerationWriter.ChunkWrite> results = WorldGenerationWriter.Write(Generator, plan.Composition, plan.Targets, plan.Force, anyway);
        double writing = watch.Elapsed.TotalSeconds;
        Refresh();
        if (results.Count == 0)
        {
            report.Add("Nothing written: the writer refused; see the console.");
            return report;
        }

        int[] outcomes = new int[Enum.GetValues(typeof(WorldGenerationWriter.Outcome)).Length];
        foreach (WorldGenerationWriter.ChunkWrite result in results)
        {
            outcomes[(int)result.Outcome]++;
        }

        int written = outcomes[(int)WorldGenerationWriter.Outcome.Written];
        int created = outcomes[(int)WorldGenerationWriter.Outcome.Created];
        report.Add($"Wrote {written + created} of {plan.Chunks.Count} chunks of {world.name} in {writing:0.0} s: {created} created, {outcomes[(int)WorldGenerationWriter.Outcome.Unchanged]} unchanged, {outcomes[(int)WorldGenerationWriter.Outcome.Locked]} locked, {outcomes[(int)WorldGenerationWriter.Outcome.ChangedByHand]} changed by hand and refused, {outcomes[(int)WorldGenerationWriter.Outcome.Failed]} failed.");
        foreach (WorldGenerationWriter.ChunkWrite result in results)
        {
            report.Add(result.Line);
        }

        if (written + created == 0)
        {
            report.Add("No chunk was written, so the whole-world steps did not run.");
            return report;
        }

        watch.Restart();
        List<string> steps = WorldGenerationWriter.RunWorldSteps(world);
        report.Add($"Whole-world steps in {watch.Elapsed.TotalSeconds:0.0} s:");
        report.AddRange(steps);
        Refresh();
        return report;
    }

    /// <summary>
    /// Locks or unlocks the chunks at <paramref name="chunks"/> in their records, made in row order where missing, as one
    /// undo step.
    /// </summary>
    public void SetLocked(IEnumerable<Vector2Int> chunks, bool locked)
    {
        SetFlag(chunks, "locked", locked, locked ? "Lock chunks" : "Unlock chunks");
    }

    /// <summary>
    /// Allows or stops the chunks at <paramref name="chunks"/> drawing past 16 layers in the wide variant, in their
    /// records, made in row order where missing, as one undo step; the composition follows after
    /// <see cref="RecomposeDelay"/>.
    /// </summary>
    public void SetAllowWide(IEnumerable<Vector2Int> chunks, bool allowWide)
    {
        SetFlag(chunks, "allowWide", allowWide, allowWide ? "Allow chunks wide" : "Keep chunks narrow");
    }

    /// <summary>
    /// The names of the generator's world's chunk scenes that are open with unsaved changes.
    /// </summary>
    public List<string> OpenUnsavedChunks()
    {
        List<string> names = new();
        WorldDefinition world = Generator.World;
        if (world == null)
        {
            return names;
        }

        foreach (Scene scene in WorldAuthoring.OpenScenesExcept(world, null, false))
        {
            if (scene.isDirty)
            {
                names.Add(scene.name);
            }
        }

        return names;
    }

    /// <summary>
    /// What the composition is composed from, as text: the generator's serialized settings without its chunk records and
    /// written entries (the writer and the lock toggles change those), then of the records only what the composer reads,
    /// the wide allowances and wilds seeds that are set; its world's id, size and chunks; and each of its region
    /// generators' settings once.
    /// </summary>
    internal string SettingsKey()
    {
        string json = EditorJsonUtility.ToJson(Generator);
        json = Records.Replace(json, "\"chunks\":[]");
        json = Entries.Replace(json, "\"writtenEntries\":[]");
        StringBuilder key = new(json);
        foreach (ChunkRecord record in Generator.Chunks)
        {
            if (record != null && (record.AllowWide || record.WildsSeed != 0))
            {
                key.Append($"|{record.Coords.x},{record.Coords.y}:{record.AllowWide},{record.WildsSeed}");
            }
        }

        WorldDefinition world = Generator.World;
        if (world != null)
        {
            key.Append('|').Append(world.WorldId).Append('|').Append(world.name).Append('|').Append(world.SizeInChunks);
            for (int y = 0; y < world.SizeInChunks.y; y++)
            {
                for (int x = 0; x < world.SizeInChunks.x; x++)
                {
                    key.Append(world.HasChunk(new Vector2Int(x, y)) ? '1' : '0');
                }
            }
        }

        List<AreaGenerator> seen = new();
        foreach (RegionPlacement placement in Generator.Regions)
        {
            if (placement != null && placement.Generator != null && !seen.Contains(placement.Generator))
            {
                seen.Add(placement.Generator);
                key.Append('|').Append(EditorJsonUtility.ToJson(placement.Generator));
            }
        }

        return key.ToString();
    }

    /// <summary>
    /// Reads every chunk's state again (<see cref="ChunkState"/>) and moves <see cref="PreviewVersion"/> on when a lock
    /// changed.
    /// </summary>
    private void Refresh()
    {
        if (Composition == null)
        {
            states = Array.Empty<ChunkState>();
            return;
        }

        states = new ChunkState[Composition.Chunks.Count];
        StringBuilder locks = new(states.Length);
        for (int i = 0; i < states.Length; i++)
        {
            states[i] = ReadState(Composition.Chunks[i]);
            ChunkRecord record = Generator.RecordOf(Composition.Chunks[i].Coords);
            locks.Append(record != null && record.Locked ? '1' : '0');
        }

        string read = locks.ToString();
        if (read != drawnLocks)
        {
            drawnLocks = read;
            PreviewVersion++;
        }
    }

    /// <summary>
    /// The state of <paramref name="chunk"/>: locked by its record; else missing when the world has no scene for it;
    /// else unchanged when its record holds its composed hash, changed when not.
    /// </summary>
    private ChunkState ReadState(WorldComposition.Chunk chunk)
    {
        ChunkRecord record = Generator.RecordOf(chunk.Coords);
        if (record != null && record.Locked)
        {
            return ChunkState.Locked;
        }

        WorldDefinition world = Generator.World;
        if (world == null || !world.HasChunk(chunk.Coords))
        {
            return ChunkState.Missing;
        }

        return record != null && !string.IsNullOrEmpty(record.WrittenHash) && record.WrittenHash == chunk.ContentHash ? ChunkState.Unchanged : ChunkState.Changed;
    }

    /// <summary>
    /// Sets the bool <paramref name="property"/> of the records of <paramref name="chunks"/> inside the world to
    /// <paramref name="value"/>, through the generator's serialized object, as one undo step named
    /// <paramref name="undoName"/>, then follows the change (<see cref="NoteChange"/>).
    /// </summary>
    private void SetFlag(IEnumerable<Vector2Int> chunks, string property, bool value, string undoName)
    {
        WorldDefinition world = Generator.World;
        SerializedObject data = new(Generator);
        SerializedProperty records = data.FindProperty("chunks");
        foreach (Vector2Int coords in chunks)
        {
            if (world == null || coords.x < 0 || coords.y < 0 || coords.x >= world.SizeInChunks.x || coords.y >= world.SizeInChunks.y)
            {
                continue;
            }

            SerializedProperty record = RecordProperty(records, coords) ?? InsertRecord(records, coords);
            record.FindPropertyRelative(property).boolValue = value;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(undoName);
        data.ApplyModifiedProperties();
        NoteChange(EditorApplication.timeSinceStartup);
    }

    /// <summary>
    /// The element of <paramref name="records"/> for the chunk at <paramref name="coords"/>, or null.
    /// </summary>
    private static SerializedProperty RecordProperty(SerializedProperty records, Vector2Int coords)
    {
        for (int i = 0; i < records.arraySize; i++)
        {
            SerializedProperty record = records.GetArrayElementAtIndex(i);
            if (record.FindPropertyRelative("coords").vector2IntValue == coords)
            {
                return record;
            }
        }

        return null;
    }

    /// <summary>
    /// Inserts an empty record for the chunk at <paramref name="coords"/> into <paramref name="records"/> before the
    /// first record of a later chunk in row order, and returns it.
    /// </summary>
    private static SerializedProperty InsertRecord(SerializedProperty records, Vector2Int coords)
    {
        int at = records.arraySize;
        for (int i = 0; i < records.arraySize; i++)
        {
            Vector2Int other = records.GetArrayElementAtIndex(i).FindPropertyRelative("coords").vector2IntValue;
            if (other.y > coords.y || (other.y == coords.y && other.x > coords.x))
            {
                at = i;
                break;
            }
        }

        records.InsertArrayElementAtIndex(at);
        SerializedProperty record = records.GetArrayElementAtIndex(at);
        record.FindPropertyRelative("coords").vector2IntValue = coords;
        record.FindPropertyRelative("writtenHash").stringValue = string.Empty;
        record.FindPropertyRelative("locked").boolValue = false;
        record.FindPropertyRelative("allowWide").boolValue = false;
        record.FindPropertyRelative("wildsSeed").intValue = 0;
        return record;
    }

    /// <summary>
    /// Whether an untitled scene with unsaved changes is open.
    /// </summary>
    private static bool HasUnsavedUntitledScene()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (string.IsNullOrEmpty(scene.path) && scene.isDirty)
            {
                return true;
            }
        }

        return false;
    }
}
