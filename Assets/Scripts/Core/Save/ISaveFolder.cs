using System.Collections.Generic;

/// <summary>
/// The folder of save files, one per session (Docs/KnownGaps.md, Phase 5), and the single save file earlier builds kept
/// beside it, so save logic is tested without the disk.
/// </summary>
public interface ISaveFolder
{
    /// <summary>
    /// The single save file of earlier builds, which <see cref="GameSave"/> imports once as a session.
    /// </summary>
    ISaveStore Legacy { get; }

    /// <summary>
    /// The names of the save files in the folder (<c>session-&lt;id&gt;.json</c>, <c>joined-&lt;host&gt;.json</c>),
    /// without the copies kept beside them or files half written.
    /// </summary>
    IReadOnlyList<string> Names();

    /// <summary>
    /// The store of the save file named <paramref name="name"/> in the folder, whether it exists yet or not.
    /// </summary>
    ISaveStore Open(string name);

    /// <summary>
    /// Puts the legacy save file out of the way once it was imported, under a name of its own beside it, so it is never
    /// imported again; <paramref name="moved"/> names it. Returns false when the disk refuses.
    /// </summary>
    bool RetireLegacy(out string moved);
}
