using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reads and writes the save files (<see cref="GameSaveData"/>), one per session, in an <see cref="ISaveFolder"/>
/// (Docs/KnownGaps.md, Phase 5): a session's own as <c>session-&lt;id&gt;.json</c>, a joined client's copy of its own
/// place in a host's session as <c>joined-&lt;host session id&gt;.json</c>. <see cref="List"/> gives every readable
/// save, newest first by its <see cref="GameSaveData.SavedAt"/>, for the Saved games screen (Docs/UiRevamp.md, Phase
/// 10); the latest (<see cref="TryLoadLatest"/>) is the first of them, a session or a copy, since a copy plays on its own
/// whether its host is gone or not. <see cref="Delete"/> takes a save out of the list, kept as one copy. A file that
/// cannot be parsed, or has a format this build does not read, is ignored with a warning (once a run) and
/// kept as a copy beside it before anything writes over it. The single save file of earlier builds is imported once as
/// a session, its terrain edits (which named layers by index) dropped with a warning, and renamed; one this build does
/// not import is ignored with a warning and left alone.
/// </summary>
public sealed class GameSave
{
    public const string SessionPrefix = "session-";
    public const string CopyPrefix = "joined-";

    private readonly ISaveFolder folder;
    private readonly IWallClock wallClock;
    private readonly HashSet<string> warned = new();
    private readonly HashSet<string> checkedNames = new();
    private bool legacyChecked;

    /// <summary>
    /// Creates a save that reads and writes the files of <paramref name="saveFolder"/>, stamping each write with
    /// <paramref name="clock"/>'s time.
    /// </summary>
    public GameSave(ISaveFolder saveFolder, IWallClock clock)
    {
        folder = saveFolder;
        wallClock = clock;
    }

    /// <summary>
    /// Whether a save Continue would load is stored (<see cref="TryLoadLatest"/>).
    /// </summary>
    public bool HasSave => TryLoadLatest(out _);

    /// <summary>
    /// Whether the last <see cref="Write"/> failed, so the in-game menu asks before leaving a game it could not save
    /// (Docs/UiRevamp.md, Phase 6). False before any write.
    /// </summary>
    public bool LastWriteFailed { get; private set; }

    /// <summary>
    /// The name of the file <paramref name="data"/> is written to: its host's copy for a joined client's copy, else its
    /// session's.
    /// </summary>
    public static string FileNameOf(GameSaveData data)
    {
        return data.IsCopy ? CopyNameOf(data.Host) : SessionPrefix + data.Id + FileSaveFolder.Extension;
    }

    /// <summary>
    /// The name of a joined client's copy of its place in the host's session <paramref name="hostSessionId"/>.
    /// </summary>
    public static string CopyNameOf(string hostSessionId)
    {
        return CopyPrefix + hostSessionId + FileSaveFolder.Extension;
    }

    /// <summary>
    /// Every readable save, the sessions and the joined clients' copies, newest first by when each was saved (a tie
    /// going first to the name that sorts last). Imports the earlier builds' save file first, once. A file that cannot be
    /// read is left out, with its warning once a run.
    /// </summary>
    public IReadOnlyList<SavedGame> List()
    {
        ImportLegacyOnce();
        List<SavedGame> saves = new();
        foreach (string name in folder.Names())
        {
            if (TryRead(name, out GameSaveData data))
            {
                saves.Add(new SavedGame(name, data));
            }
        }

        saves.Sort(NewestFirst);
        return saves;
    }

    /// <summary>
    /// The save Continue loads: the first of <see cref="List"/>, the readable save saved last, a session or a joined
    /// client's copy. False when there is none.
    /// </summary>
    public bool TryLoadLatest(out GameSaveData data)
    {
        IReadOnlyList<SavedGame> saves = List();
        data = saves.Count > 0 ? saves[0].Data : null;
        return data != null;
    }

    /// <summary>
    /// The save in the file <paramref name="name"/>, as <see cref="List"/> names it; false when it does not exist or
    /// cannot be read.
    /// </summary>
    public bool TryLoad(string name, out GameSaveData data)
    {
        ImportLegacyOnce();
        data = null;
        return !string.IsNullOrEmpty(name) && TryRead(name, out data);
    }

    /// <summary>
    /// Deletes the save file <paramref name="name"/>, keeping one copy of it beside it (<see cref="ISaveFolder.Delete"/>);
    /// false when there is no such file or the disk refuses.
    /// </summary>
    public bool Delete(string name)
    {
        if (string.IsNullOrEmpty(name) || !folder.Delete(name))
        {
            return false;
        }

        checkedNames.Remove(name);
        return true;
    }

    /// <summary>
    /// This machine's copy of its place in the host's session <paramref name="hostSessionId"/>; false when it has none
    /// or it cannot be read.
    /// </summary>
    public bool TryLoadCopy(string hostSessionId, out GameSaveData copy)
    {
        copy = null;
        return !string.IsNullOrEmpty(hostSessionId) && TryRead(CopyNameOf(hostSessionId), out copy) && copy.IsCopy;
    }

    /// <summary>
    /// Writes <paramref name="data"/>, stamped with the time, to its file (<see cref="FileNameOf"/>); returns false when
    /// it names no session or host, or the store fails. Before the first write over a stored file this build cannot
    /// read, a copy of it is kept (<see cref="ISaveStore.KeepCopy"/>), with a warning naming it; when the copy cannot be
    /// made, nothing is written. Each write records whether it failed (<see cref="LastWriteFailed"/>).
    /// </summary>
    public bool Write(GameSaveData data)
    {
        bool written = WriteFile(data);
        LastWriteFailed = !written;
        return written;
    }

    /// <summary>
    /// Writes <paramref name="data"/> as <see cref="Write"/> describes; false when nothing was written.
    /// </summary>
    private bool WriteFile(GameSaveData data)
    {
        ImportLegacyOnce();
        if (data == null || (data.IsCopy ? string.IsNullOrEmpty(data.Host) : string.IsNullOrEmpty(data.Id)))
        {
            Debug.LogError("A save naming no session or host cannot be written.");
            return false;
        }

        string name = FileNameOf(data);
        ISaveStore store = folder.Open(name);
        if (!checkedNames.Contains(name) && !KeepUnreadable(name, store))
        {
            return false;
        }

        checkedNames.Add(name);
        data.Stamp(wallClock.UtcNow);
        return store.Write(data.ToJson());
    }

    /// <summary>
    /// Reads and parses the file <paramref name="name"/>; false, with a warning once a run, when it cannot be parsed or
    /// has a format version this build does not read, and false quietly when it does not exist.
    /// </summary>
    private bool TryRead(string name, out GameSaveData data)
    {
        data = null;
        if (!folder.Open(name).TryRead(out string json))
        {
            return false;
        }

        if (!GameSaveData.TryParse(json, out GameSaveData parsed))
        {
            WarnOnce(name, $"The save file '{name}' could not be read as a save; ignoring it.");
            return false;
        }

        if (!GameSaveData.IsReadable(parsed.Version))
        {
            WarnOnce(name, $"The save file '{name}' has format version {parsed.Version}, but this build reads version {GameSaveData.CurrentVersion}; ignoring it.");
            return false;
        }

        data = parsed;
        return true;
    }

    /// <summary>
    /// Keeps a copy of what <paramref name="store"/> holds unless it is a save this build reads (or nothing is stored).
    /// False, with an error, when the copy could not be made.
    /// </summary>
    private bool KeepUnreadable(string name, ISaveStore store)
    {
        if (!store.TryRead(out string json) || (GameSaveData.TryParse(json, out GameSaveData parsed) && GameSaveData.IsReadable(parsed.Version)))
        {
            return true;
        }

        if (!store.KeepCopy(out string copy))
        {
            Debug.LogError($"The save file '{name}', which this build cannot read, could not be kept; nothing is saved over it.");
            return false;
        }

        if (copy != null)
        {
            Debug.LogWarning($"The save file '{name}', which this build cannot read, was kept as '{copy}' before saving over it.");
        }

        return true;
    }

    /// <summary>
    /// Once a run: imports the earlier builds' single save file, when it is a format this build imports, as a session
    /// with an id of its own, its terrain edits dropped with a warning, then renames it; warns about and leaves alone one
    /// it does not import.
    /// </summary>
    private void ImportLegacyOnce()
    {
        if (legacyChecked)
        {
            return;
        }

        legacyChecked = true;
        if (!folder.Legacy.TryRead(out string json))
        {
            return;
        }

        if (!GameSaveData.TryParse(json, out GameSaveData legacy) || !GameSaveData.IsImportable(legacy.Version))
        {
            string version = legacy != null ? $"format version {legacy.Version}" : "no format this build reads";
            Debug.LogWarning($"The save file of an earlier build has {version}; this build imports versions {GameSaveData.OldestImportableVersion} to {GameSaveData.CurrentVersion - 1}, so it is ignored and left as it is.");
            return;
        }

        GameSaveData imported = Import(legacy, Guid.NewGuid().ToString("N"), out int droppedEdits);
        if (!Write(imported))
        {
            return;
        }

        folder.RetireLegacy(out string moved);
        string edits = droppedEdits > 0 ? $"; its {droppedEdits} terrain edits named layers by index and were dropped" : string.Empty;
        Debug.LogWarning($"Imported the save file of an earlier build as the session {imported.Id}{edits}. The old file was kept as '{moved}'.");
    }

    /// <summary>
    /// <paramref name="legacy"/>, a save of an earlier build's format, as a session <paramref name="sessionId"/> in the
    /// current format: its time, players and worlds' mob deaths, without its terrain edits, whose count is
    /// <paramref name="droppedEdits"/>.
    /// </summary>
    internal static GameSaveData Import(GameSaveData legacy, string sessionId, out int droppedEdits)
    {
        droppedEdits = 0;
        List<GameSaveData.World> worlds = new();
        foreach (GameSaveData.World world in legacy.Worlds)
        {
            if (world == null)
            {
                continue;
            }

            List<GameSaveData.Chunk> chunks = new();
            foreach (GameSaveData.Chunk chunk in world.Chunks)
            {
                if (chunk == null)
                {
                    continue;
                }

                droppedEdits += chunk.TerrainEdits.Count;
                if (chunk.DeadMobs.Count > 0)
                {
                    chunks.Add(new GameSaveData.Chunk(chunk.Coords, chunk.DeadMobs, Array.Empty<GameSaveData.CellEdit>()));
                }
            }

            worlds.Add(new GameSaveData.World(world.WorldId, chunks));
        }

        GameSaveData imported = new(legacy.Time, legacy.Players, worlds);
        imported.SetSession(sessionId);
        return imported;
    }

    /// <summary>
    /// Orders <paramref name="a"/> before <paramref name="b"/> when it was saved later, or at the same time under a name
    /// that sorts later.
    /// </summary>
    private static int NewestFirst(SavedGame a, SavedGame b)
    {
        int bySavedAt = b.SavedAtUtc().CompareTo(a.SavedAtUtc());
        return bySavedAt != 0 ? bySavedAt : string.CompareOrdinal(b.Name, a.Name);
    }

    /// <summary>
    /// Logs <paramref name="message"/> as a warning the first time a run meets <paramref name="name"/>.
    /// </summary>
    private void WarnOnce(string name, string message)
    {
        if (warned.Add(name))
        {
            Debug.LogWarning(message);
        }
    }
}
