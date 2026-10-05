using UnityEngine;

/// <summary>Saves and loads where the game continues from. Only areas listed in <see cref="GameScenes"/> are saved or restored.</summary>
public sealed class GameSave
{
    private readonly ISaveStore store;
    private readonly GameScenes gameScenes;

    /// <summary>
    /// Creates a save that reads and writes through <paramref name="store"/> and resolves areas against
    /// <paramref name="gameScenes"/>.
    /// </summary>
    public GameSave(ISaveStore store, GameScenes gameScenes)
    {
        this.store = store;
        this.gameScenes = gameScenes;
    }

    public bool HasSave => TryLoadArea(out _, out _);

    /// <summary>
    /// Reads the saved area and spawn id. Returns false when there is no save, and warns and returns false when it
    /// cannot be parsed, has another format version, or names an area this build does not list.
    /// </summary>
    public bool TryLoadArea(out SceneDefinition area, out string spawnId)
    {
        area = null;
        spawnId = null;
        if (!store.TryRead(out string json))
        {
            return false;
        }

        if (!GameSaveData.TryParse(json, out GameSaveData data))
        {
            Debug.LogWarning("The save file could not be read as a save; ignoring it.");
            return false;
        }

        if (data.Version != GameSaveData.CurrentVersion)
        {
            Debug.LogWarning($"The save file has format version {data.Version}, but this build reads version {GameSaveData.CurrentVersion}; ignoring it.");
            return false;
        }

        area = gameScenes.FindAreaByGuid(data.AreaSceneGuid);
        if (area == null)
        {
            Debug.LogWarning($"The saved area '{data.AreaSceneGuid}' is not one of this build's areas; ignoring the save.");
            return false;
        }

        spawnId = data.SpawnId;
        return true;
    }

    /// <summary>
    /// The saved area and spawn id, or the starting area and spawn id from <see cref="GameScenes"/> when there is no
    /// usable save.
    /// </summary>
    public void GetSavedOrStartingArea(out SceneDefinition area, out string spawnId)
    {
        if (!TryLoadArea(out area, out spawnId))
        {
            area = gameScenes.StartingArea;
            spawnId = gameScenes.StartingSpawnId;
        }
    }

    /// <summary>
    /// Writes <paramref name="area"/> (by scene GUID) and <paramref name="spawnId"/> as the save. Returns false,
    /// writing nothing, for an area <see cref="GameScenes"/> does not list (such as a transient one), or when the
    /// store fails.
    /// </summary>
    public bool SaveArea(SceneDefinition area, string spawnId)
    {
        if (area == null || gameScenes.FindAreaByGuid(area.SceneGuid) == null)
        {
            return false;
        }

        return store.Write(new GameSaveData(area.SceneGuid, spawnId).ToJson());
    }
}
