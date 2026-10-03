using System;
using UnityEngine;

/// <summary>The local machine's preferences as written to disk: a format version and the key binding overrides.</summary>
[Serializable]
public sealed class UserSettingsData
{
    public const int CurrentVersion = 1;

    [SerializeField] private int version;
    [SerializeField] private string inputBindingOverrides;

    public UserSettingsData(string inputBindingOverrides)
    {
        version = CurrentVersion;
        this.inputBindingOverrides = inputBindingOverrides;
    }

    private UserSettingsData()
    {
    }

    public int Version => version;
    public string InputBindingOverrides => inputBindingOverrides ?? string.Empty;

    public string ToJson()
    {
        return JsonUtility.ToJson(this, true);
    }

    public static bool TryParse(string json, out UserSettingsData data)
    {
        data = new UserSettingsData();
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
