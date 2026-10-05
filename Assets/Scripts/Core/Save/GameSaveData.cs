using System;
using UnityEngine;

/// <summary>The saved game as written to disk: a format version, the area the party was last in and the spawn point it entered by.</summary>
[Serializable]
public sealed class GameSaveData
{
    public const int CurrentVersion = 1;

    [SerializeField] private int version;
    [SerializeField] private string areaSceneGuid;
    [SerializeField] private string spawnId;

    /// <summary>
    /// Creates save data in the current format for <paramref name="areaSceneGuid"/> and <paramref name="spawnId"/>.
    /// </summary>
    public GameSaveData(string areaSceneGuid, string spawnId)
    {
        version = CurrentVersion;
        this.areaSceneGuid = areaSceneGuid;
        this.spawnId = spawnId;
    }

    /// <summary>
    /// Creates empty data (version 0) for <see cref="TryParse"/> to fill from JSON.
    /// </summary>
    private GameSaveData()
    {
    }

    public int Version => version;
    public string AreaSceneGuid => areaSceneGuid;
    public string SpawnId => spawnId;

    /// <summary>
    /// The data as pretty-printed JSON.
    /// </summary>
    public string ToJson()
    {
        return JsonUtility.ToJson(this, true);
    }

    /// <summary>
    /// Parses <paramref name="json"/>; false only when it is not valid JSON. Missing fields keep their defaults (a
    /// missing version reads as 0), so callers still check <see cref="Version"/>.
    /// </summary>
    public static bool TryParse(string json, out GameSaveData data)
    {
        data = new GameSaveData();
        try
        {
            JsonUtility.FromJsonOverwrite(json, data);
            return true;
        }
        catch (ArgumentException)
        {
            data = null;
            return false;
        }
    }
}
