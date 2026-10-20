using UnityEngine;

/// <summary>
/// Reads and writes the save file (<see cref="GameSaveData"/>) through an <see cref="ISaveStore"/>. A file that cannot
/// be parsed, or has another format version (a version 1 area save included), is ignored with a warning.
/// </summary>
public sealed class GameSave
{
    private readonly ISaveStore store;

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
        return true;
    }

    /// <summary>
    /// Writes <paramref name="data"/> as the save; returns false when the store fails.
    /// </summary>
    public bool Write(GameSaveData data)
    {
        return store.Write(data.ToJson());
    }
}
