using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reads and writes the save files (<see cref="GameSaveData"/>), one per session, in an <see cref="ISaveFolder"/>
/// (Docs/KnownGaps.md, Phase 5): a session's own as <c>session-&lt;id&gt;.json</c>, a joined client's copy of its own
/// place in a host's session as <c>joined-&lt;host session id&gt;.json</c>. The latest save
/// (<see cref="TryLoadLatest"/>) is the readable one saved last by its <see cref="GameSaveData.SavedAt"/>, among the
/// sessions and the copies whose host is gone; a copy whose host may still be there is used only while joined to it. A
/// file that cannot be parsed, or has a format this build does not read, is ignored with a warning (once a run) and
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
    private bool hostGoneNotice;

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
    /// The save Continue loads: of the readable files, the sessions and the copies whose host is gone, the one saved
    /// last (a tie going to the name that sorts last). Imports the earlier builds' save file first, once. False when
    /// there is none.
    /// </summary>
    public bool TryLoadLatest(out GameSaveData data)
    {
        ImportLegacyOnce();
        data = null;
        string latestName = null;
        DateTime latest = DateTime.MinValue;
        foreach (string name in folder.Names())
        {
            if (!TryRead(name, out GameSaveData candidate) || (candidate.IsCopy && !candidate.HostGone))
            {
                continue;
            }

            DateTime savedAt = candidate.SavedAtUtc();
            if (data == null || savedAt > latest || (savedAt == latest && string.CompareOrdinal(name, latestName) > 0))
            {
                data = candidate;
                latest = savedAt;
                latestName = name;
            }
        }

        return data != null;
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
    /// made, nothing is written. Writing a copy whose host is gone leaves a notice for the menu
    /// (<see cref="TakeHostGoneNotice"/>).
    /// </summary>
    public bool Write(GameSaveData data)
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
        if (!store.Write(data.ToJson()))
        {
            return false;
        }

        if (data.IsCopy && data.HostGone)
        {
            hostGoneNotice = true;
        }

        return true;
    }

    /// <summary>
    /// Whether a joined client's copy was written with its host gone since the last call, clearing it: the menu then
    /// says that Continue carries on alone.
    /// </summary>
    public bool TakeHostGoneNotice()
    {
        bool notice = hostGoneNotice;
        hostGoneNotice = false;
        return notice;
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
