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

    public GameSaveData(string areaSceneGuid, string spawnId)
    {
        version = CurrentVersion;
        this.areaSceneGuid = areaSceneGuid;
        this.spawnId = spawnId;
    }

    private GameSaveData()
    {
    }

    public int Version => version;
    public string AreaSceneGuid => areaSceneGuid;
    public string SpawnId => spawnId;

    public string ToJson()
    {
        return JsonUtility.ToJson(this, true);
    }

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
