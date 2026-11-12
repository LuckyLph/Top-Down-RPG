using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The local machine's preferences as written to disk: a format version, the key binding overrides, this install's
/// player id, the name of the look its player is drawn with, the swatch it picked for each of its colour choices
/// (Docs/PlayerCharacter.md, Phase 5), the option it picked for each of its part slots (Phase 6), by name, the UI
/// size step (Docs/DesignSystem.md, Phase 3), whether the player reduces flashing and motion (Phase 6), how allies'
/// popups show (Docs/UiRevamp.md, Phase 3), whether the chat shows timestamps and where its window was moved
/// (Phase 12), and the hosts joined last, newest first (Phase 9). A file without a player id, a look, colours, parts,
/// a UI size step, the reduce preferences, the allies' popups, the chat's or recent hosts (written before there was
/// one) reads with an empty one, the step at Normal (0), the preferences off, allies' popups shown, no timestamps, the
/// chat window in its default place and no recent host.
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
    [SerializeField] private int uiSizeStep;
    [SerializeField] private bool reduceFlashing;
    [SerializeField] private bool reduceMotion;
    [SerializeField] private int allyPopups;
    [SerializeField] private bool chatTimestamps;
    [SerializeField] private ChatWindowPlace chatWindow;
    [SerializeField] private List<string> recentHosts = new();

    /// <summary>
    /// Creates settings data in the current format holding <paramref name="inputBindingOverrides"/>,
    /// <paramref name="playerId"/>, <paramref name="characterLook"/>, <paramref name="colours"/>,
    /// <paramref name="parts"/>, <paramref name="sizeStep"/>, <paramref name="lessFlashing"/>,
    /// <paramref name="lessMotion"/>, <paramref name="allies"/>, <paramref name="timestamps"/>, <paramref name="window"/>
    /// and <paramref name="hosts"/>.
    /// </summary>
    public UserSettingsData(string inputBindingOverrides, string playerId, string characterLook, IEnumerable<UserColourPick> colours = null, IEnumerable<UserPartPick> parts = null, int sizeStep = 0, bool lessFlashing = false, bool lessMotion = false, AllyPopups allies = AllyPopups.Shown, bool timestamps = false, ChatWindowPlace window = default, IEnumerable<string> hosts = null)
    {
        version = CurrentVersion;
        this.inputBindingOverrides = inputBindingOverrides;
        this.playerId = playerId;
        this.characterLook = characterLook;
        characterColours = colours != null ? new List<UserColourPick>(colours) : new List<UserColourPick>();
        characterParts = parts != null ? new List<UserPartPick>(parts) : new List<UserPartPick>();
        uiSizeStep = sizeStep;
        reduceFlashing = lessFlashing;
        reduceMotion = lessMotion;
        allyPopups = (int)allies;
        chatTimestamps = timestamps;
        chatWindow = window;
        recentHosts = hosts != null ? new List<string>(hosts) : new List<string>();
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
    public int UiSizeStep => Mathf.Max(0, uiSizeStep);
    public bool ReduceFlashing => reduceFlashing;
    public bool ReduceMotion => reduceMotion;

    /// <summary>
    /// How allies' popups show; a value this build does not know (written by a newer one) reads as shown.
    /// </summary>
    public AllyPopups AllyPopups => allyPopups > (int)AllyPopups.Shown && allyPopups <= (int)AllyPopups.Hidden ? (AllyPopups)allyPopups : AllyPopups.Shown;

    /// <summary>
    /// Whether the chat shows each line's time.
    /// </summary>
    public bool ChatTimestamps => chatTimestamps;

    /// <summary>
    /// Where the chat window was moved; one never moved, or with a corner this build does not know, reads as the
    /// default.
    /// </summary>
    public ChatWindowPlace ChatWindow => chatWindow.IsMoved ? chatWindow : default;

    /// <summary>
    /// The hosts joined last, newest first, as written.
    /// </summary>
    public IReadOnlyList<string> RecentHosts => recentHosts ?? new List<string>();

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
