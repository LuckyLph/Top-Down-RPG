using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer;

/// <summary>
/// The local machine's preferences, read once when created and written on every change: the key binding overrides, the
/// name of the look the local player is drawn with (Docs/AsepriteCharacters.md, Phase 2), the swatch picked for each of
/// its colour choices (Docs/PlayerCharacter.md, Phase 5), the option picked for each of its part slots (Phase 6), the
/// UI size step (Docs/DesignSystem.md, Phase 3), whether the player reduces flashing and motion (Phase 6), how
/// allies' popups show (Docs/UiRevamp.md, Phase 3), whether the chat shows timestamps and where its window was moved
/// (Phase 12), the hosts joined last (Phase 9), whether the first run is over (Phase 11), and this install's player id
/// (Docs/WorldStreaming.md: Player identity), a GUID made the first time it is asked for and kept in the file. Unlike
/// the game save they are written on every machine, clients included, and never replicated. The store is the
/// <see cref="ISaveStore"/> registered under the <see cref="FileName"/> key. A file written by a newer build is never
/// overwritten: its settings are not used, and changes this session are kept in memory only.
/// </summary>
public sealed class UserSettings
{
    public const string FileName = "settings.json";
    public const int MaxRecentHosts = 5;

    private readonly ISaveStore store;
    private string inputBindingOverrides = string.Empty;
    private string playerId = string.Empty;
    private string characterLook = string.Empty;
    private readonly List<UserColourPick> characterColours = new();
    private readonly List<UserPartPick> characterParts = new();
    private int uiSizeStep;
    private bool reduceFlashing;
    private bool reduceMotion;
    private AllyPopups allyPopups;
    private bool chatTimestamps;
    private ChatWindowPlace chatWindow;
    private readonly List<string> recentHosts = new();
    private bool firstRunDone;
    private bool isReadOnly;

    /// <summary>
    /// Creates the settings over the keyed settings <paramref name="store"/> and loads them right away.
    /// </summary>
    public UserSettings([Key(FileName)] ISaveStore store)
    {
        this.store = store;
        Load();
    }

    /// <summary>
    /// Raised when <see cref="SetCharacterLook"/> changes the look.
    /// </summary>
    public event Action CharacterLookChanged;

    /// <summary>
    /// Raised when <see cref="SetCharacterColour"/> changes a colour choice.
    /// </summary>
    public event Action CharacterColoursChanged;

    /// <summary>
    /// Raised when <see cref="SetCharacterPart"/> changes a part slot's pick.
    /// </summary>
    public event Action CharacterPartsChanged;

    /// <summary>
    /// Raised when <see cref="SetUiSizeStep"/> changes the UI size step.
    /// </summary>
    public event Action UiSizeStepChanged;

    /// <summary>
    /// Raised when <see cref="SetReduceFlashing"/> changes whether flashing is reduced.
    /// </summary>
    public event Action ReduceFlashingChanged;

    /// <summary>
    /// Raised when <see cref="SetReduceMotion"/> changes whether motion is reduced.
    /// </summary>
    public event Action ReduceMotionChanged;

    /// <summary>
    /// Raised when <see cref="SetAllyPopups"/> changes how allies' popups show.
    /// </summary>
    public event Action AllyPopupsChanged;

    /// <summary>
    /// Raised when <see cref="SetChatTimestamps"/> changes whether the chat shows timestamps.
    /// </summary>
    public event Action ChatTimestampsChanged;

    /// <summary>
    /// Raised when <see cref="SetChatWindow"/> moves or resizes the chat window, or puts it back.
    /// </summary>
    public event Action ChatWindowChanged;

    /// <summary>The key binding overrides as written by the Input System, or an empty string for the defaults.</summary>
    public string InputBindingOverrides => inputBindingOverrides;

    /// <summary>The name of the look the local player is drawn with, or an empty string for the player's own.</summary>
    public string CharacterLook => characterLook;

    /// <summary>
    /// The name of the swatch picked for the colour choice named <paramref name="choice"/>, or an empty string for the
    /// choice's own.
    /// </summary>
    public string CharacterColour(string choice)
    {
        foreach (UserColourPick pick in characterColours)
        {
            if (pick.Choice == choice)
            {
                return pick.Swatch;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// This install's player id, which a joining client sends the host. The first read makes a new GUID and writes the
    /// file; when the file is read-only (from a newer build) the id lasts for this session only.
    /// </summary>
    public string PlayerId
    {
        get
        {
            if (string.IsNullOrEmpty(playerId))
            {
                playerId = Guid.NewGuid().ToString("N");
                Write();
            }

            return playerId;
        }
    }

    /// <summary>Whether the file on disk came from a newer build, so changes are not written to it.</summary>
    public bool IsReadOnly => isReadOnly;

    /// <summary>The UI size step above Normal (0) the player asked for; <see cref="UiScale"/> applies what fits.</summary>
    public int UiSizeStep => uiSizeStep;

    /// <summary>
    /// Keeps <paramref name="step"/> (at least 0) as the UI size step, writes the file unless it came from a newer
    /// build, and raises <see cref="UiSizeStepChanged"/> when it changed. Returns whether it was written.
    /// </summary>
    public bool SetUiSizeStep(int step)
    {
        int clamped = Mathf.Max(0, step);
        if (clamped == uiSizeStep)
        {
            return false;
        }

        uiSizeStep = clamped;
        bool written = Write();
        UiSizeStepChanged?.Invoke();
        return written;
    }

    /// <summary>
    /// Whether the player reduces flashing: hits flash at a part of their strength and lights stop flickering on this
    /// machine (Docs/DesignSystem.md, Phase 6). Off by default.
    /// </summary>
    public bool ReduceFlashing => reduceFlashing;

    /// <summary>
    /// Keeps whether the player reduces flashing, writes the file unless it came from a newer build, and raises
    /// <see cref="ReduceFlashingChanged"/> when it changed. Returns whether it was written.
    /// </summary>
    public bool SetReduceFlashing(bool reduce)
    {
        if (reduce == reduceFlashing)
        {
            return false;
        }

        reduceFlashing = reduce;
        bool written = Write();
        ReduceFlashingChanged?.Invoke();
        return written;
    }

    /// <summary>
    /// Whether the player reduces motion: the camera does not kick on hits and popups fade in place on this machine
    /// (Docs/DesignSystem.md, Phase 6). Off by default.
    /// </summary>
    public bool ReduceMotion => reduceMotion;

    /// <summary>
    /// Keeps whether the player reduces motion, writes the file unless it came from a newer build, and raises
    /// <see cref="ReduceMotionChanged"/> when it changed. Returns whether it was written.
    /// </summary>
    public bool SetReduceMotion(bool reduce)
    {
        if (reduce == reduceMotion)
        {
            return false;
        }

        reduceMotion = reduce;
        bool written = Write();
        ReduceMotionChanged?.Invoke();
        return written;
    }

    /// <summary>
    /// How this machine shows the popups of hits, ticks and heals that involve another player but not the local one
    /// (Docs/UiRevamp.md, Phase 3). Shown by default.
    /// </summary>
    public AllyPopups AllyPopups => allyPopups;

    /// <summary>
    /// Keeps how allies' popups show, writes the file unless it came from a newer build, and raises
    /// <see cref="AllyPopupsChanged"/> when it changed. Returns whether it was written.
    /// </summary>
    public bool SetAllyPopups(AllyPopups popups)
    {
        if (popups == allyPopups)
        {
            return false;
        }

        allyPopups = popups;
        bool written = Write();
        AllyPopupsChanged?.Invoke();
        return written;
    }

    /// <summary>
    /// Whether the chat shows the time each line arrived on this machine (Docs/UiRevamp.md, Phase 12). Off by default.
    /// </summary>
    public bool ChatTimestamps => chatTimestamps;

    /// <summary>
    /// Keeps whether the chat shows timestamps, writes the file unless it came from a newer build, and raises
    /// <see cref="ChatTimestampsChanged"/> when it changed. Returns whether it was written.
    /// </summary>
    public bool SetChatTimestamps(bool shown)
    {
        if (shown == chatTimestamps)
        {
            return false;
        }

        chatTimestamps = shown;
        bool written = Write();
        ChatTimestampsChanged?.Invoke();
        return written;
    }

    /// <summary>
    /// Where the player moved the chat window and how large they made it (Docs/UiRevamp.md, Phase 12); the default
    /// value while it stands in its default place.
    /// </summary>
    public ChatWindowPlace ChatWindow => chatWindow;

    /// <summary>
    /// Keeps <paramref name="place"/> as the chat window's place (one not moved puts it back in its default place),
    /// writes the file unless it came from a newer build, and raises <see cref="ChatWindowChanged"/> when it changed.
    /// Returns whether it was written.
    /// </summary>
    public bool SetChatWindow(ChatWindowPlace place)
    {
        ChatWindowPlace kept = place.IsMoved ? place : default;
        if (kept.Equals(chatWindow))
        {
            return false;
        }

        chatWindow = kept;
        bool written = Write();
        ChatWindowChanged?.Invoke();
        return written;
    }

    /// <summary>
    /// Whether the first run is over (Docs/UiRevamp.md, Phase 11): the Welcome was answered, or the file came from a
    /// build before the flag and already held a choice the player made.
    /// </summary>
    public bool FirstRunDone => firstRunDone;

    /// <summary>
    /// Marks the first run over and writes the file, unless it already was or the file came from a newer build. Returns
    /// whether it was written.
    /// </summary>
    public bool SetFirstRunDone()
    {
        if (firstRunDone)
        {
            return false;
        }

        firstRunDone = true;
        return Write();
    }

    /// <summary>
    /// The hosts this machine joined last, newest first, at most <see cref="MaxRecentHosts"/> (Docs/UiRevamp.md,
    /// Phase 9): each as Play together shows it, the host alone on the default port, else <c>host:port</c>.
    /// </summary>
    public IReadOnlyList<string> RecentHosts => recentHosts;

    /// <summary>
    /// Puts <paramref name="host"/> first among the recent hosts, taking it from where it stood (whatever its case) and
    /// dropping the oldest past <see cref="MaxRecentHosts"/>, and writes the file unless it came from a newer build.
    /// Returns whether it was written; nothing changes for an empty host.
    /// </summary>
    public bool AddRecentHost(string host)
    {
        string kept = host?.Trim() ?? string.Empty;
        if (kept.Length == 0)
        {
            return false;
        }

        recentHosts.RemoveAll(existing => string.Equals(existing, kept, StringComparison.OrdinalIgnoreCase));
        recentHosts.Insert(0, kept);
        if (recentHosts.Count > MaxRecentHosts)
        {
            recentHosts.RemoveRange(MaxRecentHosts, recentHosts.Count - MaxRecentHosts);
        }

        return Write();
    }

    /// <summary>
    /// Keeps <paramref name="overrides"/> and writes the file, unless it came from a newer build. Returns whether it was written.
    /// </summary>
    public bool SetInputBindingOverrides(string overrides)
    {
        inputBindingOverrides = overrides ?? string.Empty;
        return Write();
    }

    /// <summary>
    /// Keeps <paramref name="lookName"/> as the look the local player is drawn with, writes the file unless it came from
    /// a newer build, and raises <see cref="CharacterLookChanged"/> when it changed. Returns whether it was written.
    /// </summary>
    public bool SetCharacterLook(string lookName)
    {
        string look = lookName ?? string.Empty;
        if (look == characterLook)
        {
            return false;
        }

        characterLook = look;
        bool written = Write();
        CharacterLookChanged?.Invoke();
        return written;
    }

    /// <summary>
    /// The name of the option picked for the part slot named <paramref name="slot"/>, or an empty string for the slot's
    /// first, the look's own.
    /// </summary>
    public string CharacterPart(string slot)
    {
        foreach (UserPartPick pick in characterParts)
        {
            if (pick.Slot == slot)
            {
                return pick.Part;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Keeps <paramref name="partName"/> as the option picked for the part slot named <paramref name="slot"/> (an empty
    /// name goes back to the slot's first, forgetting the pick), writes the file unless it came from a newer build, and
    /// raises <see cref="CharacterPartsChanged"/> when it changed. Returns whether it was written.
    /// </summary>
    public bool SetCharacterPart(string slot, string partName)
    {
        string part = partName ?? string.Empty;
        if (string.IsNullOrEmpty(slot) || part == CharacterPart(slot))
        {
            return false;
        }

        characterParts.RemoveAll(pick => pick.Slot == slot);
        if (part.Length > 0)
        {
            characterParts.Add(new UserPartPick(slot, part));
        }

        bool written = Write();
        CharacterPartsChanged?.Invoke();
        return written;
    }

    /// <summary>
    /// Keeps <paramref name="swatchName"/> as the swatch picked for the colour choice named <paramref name="choice"/>
    /// (an empty name goes back to the choice's own, forgetting the pick), writes the file unless it came from a newer
    /// build, and raises <see cref="CharacterColoursChanged"/> when it changed. Returns whether it was written.
    /// </summary>
    public bool SetCharacterColour(string choice, string swatchName)
    {
        string swatch = swatchName ?? string.Empty;
        if (string.IsNullOrEmpty(choice) || swatch == CharacterColour(choice))
        {
            return false;
        }

        characterColours.RemoveAll(pick => pick.Choice == choice);
        if (swatch.Length > 0)
        {
            characterColours.Add(new UserColourPick(choice, swatch));
        }

        bool written = Write();
        CharacterColoursChanged?.Invoke();
        return written;
    }

    /// <summary>
    /// Forgets the look, every part and every colour picked, so the character is the catalog's first look in its own
    /// parts and colours (the Character screen's Reset all, Docs/UiRevamp.md, Phase 7), in one write; raises
    /// <see cref="CharacterLookChanged"/>, <see cref="CharacterPartsChanged"/> and <see cref="CharacterColoursChanged"/>
    /// for those that changed. Returns whether it was written.
    /// </summary>
    public bool ResetCharacter()
    {
        bool look = characterLook.Length > 0;
        bool parts = characterParts.Count > 0;
        bool colours = characterColours.Count > 0;
        if (!look && !parts && !colours)
        {
            return false;
        }

        characterLook = string.Empty;
        characterParts.Clear();
        characterColours.Clear();
        bool written = Write();
        if (look)
        {
            CharacterLookChanged?.Invoke();
        }

        if (parts)
        {
            CharacterPartsChanged?.Invoke();
        }

        if (colours)
        {
            CharacterColoursChanged?.Invoke();
        }

        return written;
    }

    /// <summary>
    /// Puts the accessibility options back on their defaults (the options screen's Reset accessibility,
    /// Docs/UiRevamp.md, Phase 8): the UI size at Normal, flashing and motion not reduced, in one write; raises
    /// <see cref="UiSizeStepChanged"/>, <see cref="ReduceFlashingChanged"/> and <see cref="ReduceMotionChanged"/> for those
    /// that changed. Returns whether it was written.
    /// </summary>
    public bool ResetAccessibility()
    {
        bool size = uiSizeStep != 0;
        bool flashing = reduceFlashing;
        bool motion = reduceMotion;
        if (!size && !flashing && !motion)
        {
            return false;
        }

        uiSizeStep = 0;
        reduceFlashing = false;
        reduceMotion = false;
        bool written = Write();
        if (size)
        {
            UiSizeStepChanged?.Invoke();
        }

        if (flashing)
        {
            ReduceFlashingChanged?.Invoke();
        }

        if (motion)
        {
            ReduceMotionChanged?.Invoke();
        }

        return written;
    }

    /// <summary>
    /// Puts the interface options back on their defaults (the options screen's Reset interface, Docs/UiRevamp.md,
    /// Phase 8): allies' popups shown, no chat timestamps, the chat window in its place, in one write; raises
    /// <see cref="AllyPopupsChanged"/>, <see cref="ChatTimestampsChanged"/> and <see cref="ChatWindowChanged"/> for those
    /// that changed. Returns whether it was written.
    /// </summary>
    public bool ResetInterface()
    {
        bool popups = allyPopups != AllyPopups.Shown;
        bool stamps = chatTimestamps;
        bool window = chatWindow.IsMoved;
        if (!popups && !stamps && !window)
        {
            return false;
        }

        allyPopups = AllyPopups.Shown;
        chatTimestamps = false;
        chatWindow = default;
        bool written = Write();
        if (popups)
        {
            AllyPopupsChanged?.Invoke();
        }

        if (stamps)
        {
            ChatTimestampsChanged?.Invoke();
        }

        if (window)
        {
            ChatWindowChanged?.Invoke();
        }

        return written;
    }

    /// <summary>
    /// Writes every setting to the file, unless it came from a newer build. Returns whether it was written.
    /// </summary>
    private bool Write()
    {
        return !isReadOnly && store.Write(new UserSettingsData(inputBindingOverrides, playerId, characterLook, characterColours, characterParts, uiSizeStep, reduceFlashing, reduceMotion, allyPopups, chatTimestamps, chatWindow, recentHosts, firstRunDone).ToJson());
    }

    /// <summary>
    /// Reads the stored settings, keeping the defaults (with a warning) when the file cannot be parsed or has another
    /// format version; a file from a newer build also makes these settings read-only.
    /// </summary>
    private void Load()
    {
        if (!store.TryRead(out string json))
        {
            return;
        }

        if (!UserSettingsData.TryParse(json, out UserSettingsData data))
        {
            Debug.LogWarning("The settings file could not be read; using the default settings.");
            return;
        }

        if (data.Version > UserSettingsData.CurrentVersion)
        {
            isReadOnly = true;
            Debug.LogWarning($"The settings file was written by a newer build (format version {data.Version}, this build reads {UserSettingsData.CurrentVersion}); using the default settings and leaving the file as it is.");
            return;
        }

        if (data.Version != UserSettingsData.CurrentVersion)
        {
            Debug.LogWarning($"The settings file has format version {data.Version}, but this build reads version {UserSettingsData.CurrentVersion}; using the default settings.");
            return;
        }

        inputBindingOverrides = data.InputBindingOverrides;
        playerId = data.PlayerId;
        characterLook = data.CharacterLook;
        uiSizeStep = data.UiSizeStep;
        reduceFlashing = data.ReduceFlashing;
        reduceMotion = data.ReduceMotion;
        allyPopups = data.AllyPopups;
        chatTimestamps = data.ChatTimestamps;
        chatWindow = data.ChatWindow;
        firstRunDone = data.FirstRunDone || data.HoldsChoices;
        foreach (UserColourPick pick in data.CharacterColours)
        {
            if (pick.Choice.Length > 0 && pick.Swatch.Length > 0 && CharacterColour(pick.Choice).Length == 0)
            {
                characterColours.Add(pick);
            }
        }

        foreach (UserPartPick pick in data.CharacterParts)
        {
            if (pick.Slot.Length > 0 && pick.Part.Length > 0 && CharacterPart(pick.Slot).Length == 0)
            {
                characterParts.Add(pick);
            }
        }

        foreach (string host in data.RecentHosts)
        {
            string kept = host?.Trim() ?? string.Empty;
            if (kept.Length > 0 && recentHosts.Count < MaxRecentHosts && !recentHosts.Exists(existing => string.Equals(existing, kept, StringComparison.OrdinalIgnoreCase)))
            {
                recentHosts.Add(kept);
            }
        }
    }
}
