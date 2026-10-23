using System;
using UnityEngine;

/// <summary>
/// The local machine's preferences as written to disk: a format version, the key binding overrides, this install's
/// player id and the name of the look its player is drawn with. A file without a player id or a look (written before
/// there was one) reads with an empty one.
/// </summary>
[Serializable]
public sealed class UserSettingsData
{
    public const int CurrentVersion = 1;

    [SerializeField] private int version;
    [SerializeField] private string inputBindingOverrides;
    [SerializeField] private string playerId;
    [SerializeField] private string characterLook;

    /// <summary>
    /// Creates settings data in the current format holding <paramref name="inputBindingOverrides"/>,
    /// <paramref name="playerId"/> and <paramref name="characterLook"/>.
    /// </summary>
    public UserSettingsData(string inputBindingOverrides, string playerId, string characterLook)
    {
        version = CurrentVersion;
        this.inputBindingOverrides = inputBindingOverrides;
        this.playerId = playerId;
        this.characterLook = characterLook;
    }

    /// <summary>
    /// Creates empty data (version 0) for <see cref="TryParse"/> to fill from JSON.
    /// </summary>
    private UserSettingsData()
    {
    }

    public int Version => version;
    public string InputBindingOverrides => inputBindingOverrides ?? string.Empty;
    public string PlayerId => playerId ?? string.Empty;
    public string CharacterLook => characterLook ?? string.Empty;

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
