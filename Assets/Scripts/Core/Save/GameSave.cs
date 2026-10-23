using UnityEngine;

/// <summary>
/// Reads and writes the save file (<see cref="GameSaveData"/>) through an <see cref="ISaveStore"/>. A file that cannot
/// be parsed, or has another format version (a version 1 area save included), is ignored with a warning, and kept as a
/// copy beside it before the first write replaces it, so a save from a newer build or a damaged one is never lost.
/// </summary>
public sealed class GameSave
{
    private readonly ISaveStore store;
    private bool storedIsCurrent;

    /// <summary>
    /// Creates a save that reads and writes through <paramref name="store"/>.
    /// </summary>
    public GameSave(ISaveStore store)
    {
        this.store = store;
    }

    /// <summary>
    /// Whether a save in the current format is stored.
    /// </summary>
    public bool HasSave => TryLoad(out _);

    /// <summary>
    /// Reads the save. Returns false when there is none, and warns and returns false when it cannot be parsed or has
    /// another format version.
    /// </summary>
    public bool TryLoad(out GameSaveData data)
    {
        data = null;
        if (!store.TryRead(out string json))
        {
            return false;
        }

        if (!GameSaveData.TryParse(json, out GameSaveData parsed))
        {
            Debug.LogWarning("The save file could not be read as a save; ignoring it.");
            return false;
        }

        if (parsed.Version != GameSaveData.CurrentVersion)
        {
            Debug.LogWarning($"The save file has format version {parsed.Version}, but this build reads version {GameSaveData.CurrentVersion}; ignoring it.");
            return false;
        }

        data = parsed;
        storedIsCurrent = true;
        return true;
    }

    /// <summary>
    /// Writes <paramref name="data"/> as the save; returns false when the store fails. Before the first write over a
    /// stored save this build cannot read, a copy of it is kept (<see cref="ISaveStore.KeepCopy"/>), with a warning
    /// naming it; when the copy cannot be made, nothing is written.
    /// </summary>
    public bool Write(GameSaveData data)
    {
        if (!storedIsCurrent && !KeepUnreadable())
        {
            return false;
        }

        storedIsCurrent = store.Write(data.ToJson());
        return storedIsCurrent;
    }

    /// <summary>
    /// Keeps a copy of what is stored unless it is a save this build reads (or nothing is stored). False, with an error,
    /// when the copy could not be made.
    /// </summary>
    private bool KeepUnreadable()
    {
        if (store.TryRead(out string json) && GameSaveData.TryParse(json, out GameSaveData parsed) && parsed.Version == GameSaveData.CurrentVersion)
        {
            return true;
        }

        if (!store.KeepCopy(out string copy))
        {
            Debug.LogError("The save file this build cannot read could not be kept; nothing is saved over it.");
            return false;
        }

        if (copy != null)
        {
            Debug.LogWarning($"The save file this build cannot read was kept as '{copy}' before saving over it.");
        }

        return true;
    }
}
