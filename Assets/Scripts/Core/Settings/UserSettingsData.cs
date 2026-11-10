using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The local machine's preferences as written to disk: a format version, the key binding overrides, this install's
/// player id, the name of the look its player is drawn with, the swatch it picked for each of its colour choices
/// (Docs/PlayerCharacter.md, Phase 5) and the option it picked for each of its part slots (Phase 6), by name. A file
/// without a player id, a look, colours or parts (written before there was one) reads with an empty one.
/// </summary>
[Serializable]
public sealed class UserSettingsData
{
    public const int CurrentVersion = 1;

    [SerializeField] private int version;
    [SerializeField] private string inputBindingOverrides;
    [SerializeField] private string playerId;
    [SerializeField] private string characterLook;
    [SerializeField] private List<UserColourPick> characterColours = new();
    [SerializeField] private List<UserPartPick> characterParts = new();

    /// <summary>
    /// Creates settings data in the current format holding <paramref name="inputBindingOverrides"/>,
    /// <paramref name="playerId"/>, <paramref name="characterLook"/>, <paramref name="colours"/> and
    /// <paramref name="parts"/>.
    /// </summary>
    public UserSettingsData(string inputBindingOverrides, string playerId, string characterLook, IEnumerable<UserColourPick> colours = null, IEnumerable<UserPartPick> parts = null)
    {
        version = CurrentVersion;
        this.inputBindingOverrides = inputBindingOverrides;
        this.playerId = playerId;
        this.characterLook = characterLook;
        characterColours = colours != null ? new List<UserColourPick>(colours) : new List<UserColourPick>();
        characterParts = parts != null ? new List<UserPartPick>(parts) : new List<UserPartPick>();
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
    public IReadOnlyList<UserColourPick> CharacterColours => characterColours ?? new List<UserColourPick>();
    public IReadOnlyList<UserPartPick> CharacterParts => characterParts ?? new List<UserPartPick>();

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

/// <summary>
/// The swatch picked for one colour choice of the local player's character, both by name.
/// </summary>
[Serializable]
public struct UserColourPick
{
    [SerializeField] private string choice;
    [SerializeField] private string swatch;

    /// <summary>
    /// Creates the pick of <paramref name="swatchName"/> for <paramref name="choiceName"/>.
    /// </summary>
    public UserColourPick(string choiceName, string swatchName)
    {
        choice = choiceName;
        swatch = swatchName;
    }

    public string Choice => choice ?? string.Empty;
    public string Swatch => swatch ?? string.Empty;
}

/// <summary>
/// The option picked for one part slot of the local player's character, both by name.
/// </summary>
[Serializable]
public struct UserPartPick
{
    [SerializeField] private string slot;
    [SerializeField] private string part;

    /// <summary>
    /// Creates the pick of <paramref name="partName"/> for <paramref name="slotName"/>.
    /// </summary>
    public UserPartPick(string slotName, string partName)
    {
        slot = slotName;
        part = partName;
    }

    public string Slot => slot ?? string.Empty;
    public string Part => part ?? string.Empty;
}
