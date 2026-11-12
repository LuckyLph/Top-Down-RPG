using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using VContainer.Unity;

/// <summary>
/// Drives the <see cref="OptionsMenuView"/>: a Character section first, when the view names a catalog of more than one
/// look or colours a look carries: a Look row whose button shows the look of the local settings and moves to the next
/// one, its reset going back to the first (Docs/AsepriteCharacters.md, Phase 2), then a row per part slot of more than
/// one option of the first look that wears parts (Docs/PlayerCharacter.md, Phases 6 and 7; not the weapon's, which
/// shows the equipped weapon's gear), its button showing the option the
/// local settings pick and moving to the next, its reset going back to the first, then a row per colour choice whose
/// dyes a look's sheets or parts carry (Phase 5), its button showing the swatch the local settings pick and moving to the
/// next one of its list, its reset going back to the choice's own; then a Display section with the UI size row
/// (Docs/DesignSystem.md, Phase 3), its button showing the size the display shows and moving to the next one that fits,
/// its reset going back to Normal, the Reduce flashing and Reduce motion toggle rows (Phase 6), each button reading
/// On or Off and turning it the other way, each reset turning it off, and the allies' damage numbers row
/// (Docs/UiRevamp.md, Phase 3), its button showing Shown, Smaller or Hidden and moving to the next, its reset going back
/// to Shown, the Chat timestamps toggle row (Phase 12), and the Chat window row, its button reading Default or Moved
/// and, like its reset, putting a moved chat window back in its place; then a row per rebindable action of
/// <see cref="InputBindings"/> under its section, rebinding a row when its binding is selected (listening starts on the
/// next tick, so the click itself is never captured), naming the action that moved on a swap, resetting one row or all
/// of them after a confirmation; and it closes on Back. While open it is a layer on the <see cref="MenuStack"/>; Back
/// closes the confirmation, or else the screen, and does nothing while a rebind is pending or listening (the rebind
/// itself takes Escape as cancel). Opening it selects the first row.
/// </summary>
public sealed class OptionsMenuController : IStartable, ITickable, IDisposable, IMenuLayer
{
    public const string ListeningPromptFormat = "Press a key or mouse button for {0}.\nEsc to cancel.";
    public const string MovedNoticeFormat = "{0} moved to {1}.";
    public const string ResetAllNotice = "All controls are back to their defaults.";
    public const string CharacterSection = "Character";
    public const string LookLabel = "Look";
    public const string DisplaySection = "Display";
    public const string UiSizeLabel = "UI size";
    public static readonly string[] UiSizeNames = { "Normal", "Large", "Larger" };
    public const string ReduceFlashingLabel = "Reduce flashing";
    public const string ReduceMotionLabel = "Reduce motion";
    public const string OnName = "On";
    public const string OffName = "Off";
    public const string AllyPopupsLabel = "Allies' damage numbers";
    public static readonly string[] AllyPopupsNames = { "Shown", "Smaller", "Hidden" };
    public const string ChatTimestampsLabel = "Chat timestamps";
    public const string ChatWindowLabel = "Chat window";
    public const string ChatWindowDefault = "Default";
    public const string ChatWindowMoved = "Moved";

    private readonly OptionsMenuView view;
    private readonly InputBindings bindings;
    private readonly MenuStack menus;
    private readonly UISelection selection;
    private readonly UserSettings settings;
    private readonly UiScale uiScale;
    private readonly List<KeyBindingRowView> rows = new();
    private readonly List<KeyBindingRowView> colourRows = new();
    private readonly List<int> colourChoices = new();
    private readonly List<KeyBindingRowView> partRows = new();
    private readonly List<CharacterPartSlot> partSlots = new();
    private CharacterParts parts;
    private KeyBindingRowView lookRow;
    private KeyBindingRowView uiSizeRow;
    private KeyBindingRowView reduceFlashingRow;
    private KeyBindingRowView reduceMotionRow;
    private KeyBindingRowView allyPopupsRow;
    private KeyBindingRowView chatTimestampsRow;
    private KeyBindingRowView chatWindowRow;
    private Action onClosed;
    private int pendingRebind = -1;
    private int listeningRow = -1;

    /// <summary>
    /// Creates a closed options controller that edits <paramref name="bindings"/>, and the look, colours, parts and UI
    /// size step kept in <paramref name="settings"/>, through <paramref name="view"/>; the UI size row offers the steps
    /// <paramref name="scale"/> says fit.
    /// </summary>
    public OptionsMenuController(OptionsMenuView view, InputBindings bindings, MenuStack menus, UISelection selection, UserSettings settings, UiScale scale)
    {
        this.view = view;
        this.bindings = bindings;
        this.menus = menus;
        this.selection = selection;
        this.settings = settings;
        uiScale = scale;
    }

    public bool IsOpen => view.IsShown;
    public bool IsListening => listeningRow >= 0;
    internal int RowCount => rows.Count;
    internal KeyBindingRowView LookRow => lookRow;
    internal KeyBindingRowView UiSizeRow => uiSizeRow;
    internal KeyBindingRowView ReduceFlashingRow => reduceFlashingRow;
    internal KeyBindingRowView ReduceMotionRow => reduceMotionRow;
    internal KeyBindingRowView AllyPopupsRow => allyPopupsRow;
    internal KeyBindingRowView ChatTimestampsRow => chatTimestampsRow;
    internal KeyBindingRowView ChatWindowRow => chatWindowRow;
    internal int ColourRowCount => colourRows.Count;
    internal int PartRowCount => partRows.Count;

    /// <summary>
    /// The part row at <paramref name="index"/>, in slot order.
    /// </summary>
    internal KeyBindingRowView GetPartRow(int index)
    {
        return partRows[index];
    }

    /// <summary>
    /// The colour row at <paramref name="index"/>, in the order its choices have in the colours.
    /// </summary>
    internal KeyBindingRowView GetColourRow(int index)
    {
        return colourRows[index];
    }

    /// <summary>
    /// The row built for the rebindable action at <paramref name="index"/>, in <see cref="InputBindings"/> order.
    /// </summary>
    internal KeyBindingRowView GetRow(int index)
    {
        return rows[index];
    }

    /// <summary>
    /// Hides the screen, builds the Character section and the Display section, then a section header for each new
    /// section and a labelled row per rebindable action, wires the buttons, and keeps the rows' labels in sync with
    /// <see cref="InputBindings.BindingsChanged"/>, <see cref="UserSettings.CharacterLookChanged"/>,
    /// <see cref="UserSettings.CharacterColoursChanged"/>, <see cref="UserSettings.UiSizeStepChanged"/>,
    /// <see cref="UiScale.Changed"/>, <see cref="UserSettings.ReduceFlashingChanged"/>,
    /// <see cref="UserSettings.ReduceMotionChanged"/>, <see cref="UserSettings.AllyPopupsChanged"/>,
    /// <see cref="UserSettings.ChatTimestampsChanged"/> and <see cref="UserSettings.ChatWindowChanged"/>.
    /// </summary>
    public void Start()
    {
        view.SetShown(false);
        view.HideListening();
        view.SetConfirmShown(false);
        BuildCharacterSection();
        BuildDisplaySection();

        string section = null;
        for (int i = 0; i < bindings.Count; i++)
        {
            InputAction action = bindings.GetAction(i);
            string actionSection = bindings.GetSection(action);
            if (actionSection != section)
            {
                view.AddSectionHeader(actionSection);
                section = actionSection;
            }

            KeyBindingRowView row = view.AddRow();
            row.SetLabel(bindings.GetDisplayName(action));
            int index = i;
            row.BindingButton.onClick.AddListener(() => RequestRebind(index));
            row.ResetButton.onClick.AddListener(() => ResetRow(index));
            rows.Add(row);
        }

        view.ResetAllButton.onClick.AddListener(AskResetAll);
        view.BackButton.onClick.AddListener(Close);
        view.ConfirmYesButton.onClick.AddListener(ConfirmResetAll);
        view.ConfirmNoButton.onClick.AddListener(CancelResetAll);
        bindings.BindingsChanged += RefreshRows;
        RefreshRows();
    }

    /// <summary>
    /// Stops following binding changes, leaves the menu stack and cancels a rebind that is still listening.
    /// </summary>
    public void Dispose()
    {
        bindings.BindingsChanged -= RefreshRows;
        if (lookRow != null)
        {
            settings.CharacterLookChanged -= RefreshLook;
        }

        if (colourRows.Count > 0)
        {
            settings.CharacterColoursChanged -= RefreshColours;
        }

        if (partRows.Count > 0)
        {
            settings.CharacterPartsChanged -= RefreshParts;
        }

        if (uiSizeRow != null)
        {
            settings.UiSizeStepChanged -= RefreshUiSize;
            uiScale.Changed -= RefreshUiSize;
        }

        if (reduceFlashingRow != null)
        {
            settings.ReduceFlashingChanged -= RefreshReduceFlashing;
        }

        if (reduceMotionRow != null)
        {
            settings.ReduceMotionChanged -= RefreshReduceMotion;
        }

        if (allyPopupsRow != null)
        {
            settings.AllyPopupsChanged -= RefreshAllyPopups;
        }

        if (chatTimestampsRow != null)
        {
            settings.ChatTimestampsChanged -= RefreshChatTimestamps;
        }

        if (chatWindowRow != null)
        {
            settings.ChatWindowChanged -= RefreshChatWindow;
        }

        menus.Remove(this);
        if (IsListening)
        {
            bindings.CancelRebind();
        }
    }

    /// <summary>
    /// Shows the screen with its first row selected, or, <paramref name="atControls"/>, its first key binding row (the
    /// in-game menu's Change keys, Docs/UiRevamp.md, Phase 6). <paramref name="closed"/> runs once it closes.
    /// </summary>
    public void Open(Action closed, bool atControls = false)
    {
        onClosed = closed;
        pendingRebind = -1;
        view.SetNotice(string.Empty);
        view.SetConfirmShown(false);
        view.HideListening();
        RefreshRows();
        view.SetShown(true);
        menus.Push(this);
        KeyBindingRowView first = atControls && rows.Count > 0 ? rows[0] : FirstRow();
        selection.Select(first != null ? first.BindingButton : view.BackButton);
    }

    /// <summary>
    /// The first row of the screen: the Look row, a part row, a colour row, the UI size row, the Reduce flashing row or
    /// a binding row, in that order; null without rows.
    /// </summary>
    private KeyBindingRowView FirstRow()
    {
        if (lookRow != null)
        {
            return lookRow;
        }

        if (partRows.Count > 0)
        {
            return partRows[0];
        }

        if (colourRows.Count > 0)
        {
            return colourRows[0];
        }

        if (uiSizeRow != null)
        {
            return uiSizeRow;
        }

        if (reduceFlashingRow != null)
        {
            return reduceFlashingRow;
        }

        return rows.Count > 0 ? rows[0] : null;
    }

    /// <summary>
    /// Hides the screen, cancelling a rebind that is listening, and runs the callback given to <see cref="Open"/>.
    /// </summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        pendingRebind = -1;
        if (IsListening)
        {
            bindings.CancelRebind();
        }

        view.SetConfirmShown(false);
        view.HideListening();
        view.SetShown(false);
        menus.Remove(this);
        Action closed = onClosed;
        onClosed = null;
        closed?.Invoke();
    }

    /// <summary>
    /// Starts listening for the rebind requested on an earlier frame, so the click that asked for it is not
    /// captured as the new binding.
    /// </summary>
    public void Tick()
    {
        if (IsOpen && pendingRebind >= 0)
        {
            int row = pendingRebind;
            pendingRebind = -1;
            BeginListening(row);
        }
    }

    /// <summary>
    /// Closes the Reset all confirmation, or else the screen. Ignored while a rebind is pending or listening.
    /// </summary>
    public void HandleBack()
    {
        if (IsListening || pendingRebind >= 0)
        {
            return;
        }

        if (view.IsConfirmShown)
        {
            CancelResetAll();
        }
        else
        {
            Close();
        }
    }

    /// <summary>
    /// Binding button of row <paramref name="index"/>: queues its rebind for the next tick. Ignored while closed,
    /// listening or confirming.
    /// </summary>
    private void RequestRebind(int index)
    {
        if (IsOpen && !IsListening && !view.IsConfirmShown)
        {
            pendingRebind = index;
        }
    }

    /// <summary>
    /// Shows the listening prompt for row <paramref name="index"/>, clears the selection and starts the
    /// interactive rebind; if it cannot start, hides the prompt and reselects the row's binding button.
    /// </summary>
    private void BeginListening(int index)
    {
        InputAction action = bindings.GetAction(index);
        listeningRow = index;
        view.SetNotice(string.Empty);
        view.ShowListening(string.Format(ListeningPromptFormat, bindings.GetDisplayName(action)));
        selection.Select(null);
        if (!bindings.StartRebind(action, HandleRebindFinished))
        {
            listeningRow = -1;
            view.HideListening();
            selection.Select(rows[index].BindingButton);
        }
    }

    /// <summary>
    /// Ends listening: hides the prompt, names the action that moved when the new input was assigned, and
    /// reselects the row's binding button if the screen is still open.
    /// </summary>
    private void HandleRebindFinished(RebindOutcome outcome, InputAction swappedWith)
    {
        int row = listeningRow;
        listeningRow = -1;
        view.HideListening();
        if (outcome == RebindOutcome.Assigned)
        {
            ShowMoved(swappedWith);
        }

        if (IsOpen && row >= 0)
        {
            selection.Select(rows[row].BindingButton);
        }
    }

    /// <summary>
    /// Reset button of row <paramref name="index"/>: puts that action back on its default input and names the
    /// action that moved, if any. Ignored while listening or confirming.
    /// </summary>
    private void ResetRow(int index)
    {
        if (IsListening || view.IsConfirmShown)
        {
            return;
        }

        bindings.Reset(bindings.GetAction(index), out InputAction swappedWith);
        ShowMoved(swappedWith);
    }

    /// <summary>
    /// Reset all button: shows the confirmation with Cancel selected. Ignored while listening.
    /// </summary>
    private void AskResetAll()
    {
        if (IsListening)
        {
            return;
        }

        view.SetConfirmShown(true);
        selection.Select(view.ConfirmNoButton);
    }

    /// <summary>
    /// Confirmation accepted: puts every action back on its default, hides the confirmation and says so.
    /// </summary>
    private void ConfirmResetAll()
    {
        bindings.ResetAll();
        view.SetConfirmShown(false);
        view.SetNotice(ResetAllNotice);
        selection.Select(view.ResetAllButton);
    }

    /// <summary>
    /// Confirmation declined: hides it and reselects Reset all.
    /// </summary>
    private void CancelResetAll()
    {
        view.SetConfirmShown(false);
        selection.Select(view.ResetAllButton);
    }

    /// <summary>
    /// Shows which action <paramref name="swappedWith"/> moved to which input, or clears the notice when nothing
    /// was swapped.
    /// </summary>
    private void ShowMoved(InputAction swappedWith)
    {
        view.SetNotice(swappedWith != null
            ? string.Format(MovedNoticeFormat, bindings.GetDisplayName(swappedWith), bindings.GetLabel(swappedWith))
            : string.Empty);
    }

    /// <summary>
    /// Builds the Character section: its Look row when the view names a catalog of more than one look, and a row per
    /// colour choice whose dyes a look of the catalog carries; nothing without either.
    /// </summary>
    private void BuildCharacterSection()
    {
        CharacterLookCatalog looks = view.Looks;
        CharacterColours colours = view.Colours;
        if (looks == null || settings == null)
        {
            return;
        }

        int carried = 0;
        for (int i = 0; i < looks.Count; i++)
        {
            CharacterLook look = looks.Get(i);
            carried |= look != null && look.AnimationSet != null ? look.AnimationSet.Dyes : 0;
            carried |= look != null && look.Parts != null ? look.Parts.Dyes : 0;
            if (parts == null && look != null && look.Parts != null)
            {
                parts = look.Parts;
            }
        }

        if (parts != null)
        {
            foreach (CharacterPartSlot slot in parts.Slots)
            {
                if (slot != null && !slot.FollowsWeapon && slot.Options.Count > 1)
                {
                    partSlots.Add(slot);
                }
            }
        }

        for (int i = 0; colours != null && i < colours.Count && i < CharacterColourPicks.MaxChoices; i++)
        {
            if (colours.Colours(i, carried) && colours.Swatches(i).Count > 0)
            {
                colourChoices.Add(i);
            }
        }

        if (looks.Count < 2 && colourChoices.Count == 0 && partSlots.Count == 0)
        {
            return;
        }

        view.AddSectionHeader(CharacterSection);
        if (looks.Count >= 2)
        {
            BuildLookRow();
        }

        foreach (CharacterPartSlot slot in partSlots)
        {
            KeyBindingRowView row = view.AddRow();
            row.SetLabel(slot.Slot.ToString());
            row.BindingButton.onClick.AddListener(() => NextPart(slot));
            row.ResetButton.onClick.AddListener(() => ResetPart(slot));
            partRows.Add(row);
        }

        if (partRows.Count > 0)
        {
            settings.CharacterPartsChanged += RefreshParts;
            RefreshParts();
        }

        foreach (int choice in colourChoices)
        {
            KeyBindingRowView row = view.AddSwatchRow();
            row.SetLabel(colours.Get(choice).Name);
            row.BindingButton.onClick.AddListener(() => NextColour(choice));
            row.ResetButton.onClick.AddListener(() => ResetColour(choice));
            colourRows.Add(row);
        }

        if (colourRows.Count > 0)
        {
            settings.CharacterColoursChanged += RefreshColours;
            RefreshColours();
        }
    }

    /// <summary>
    /// Builds the Display section: its UI size row, when there is a UI zoom to follow, then the Reduce flashing and
    /// Reduce motion toggle rows, the allies' damage numbers row, the Chat timestamps toggle row and the Chat window row.
    /// </summary>
    private void BuildDisplaySection()
    {
        if (settings == null)
        {
            return;
        }

        view.AddSectionHeader(DisplaySection);
        if (uiScale != null)
        {
            uiSizeRow = view.AddRow();
            uiSizeRow.SetLabel(UiSizeLabel);
            uiSizeRow.BindingButton.onClick.AddListener(NextUiSize);
            uiSizeRow.ResetButton.onClick.AddListener(ResetUiSize);
            settings.UiSizeStepChanged += RefreshUiSize;
            uiScale.Changed += RefreshUiSize;
            RefreshUiSize();
        }

        reduceFlashingRow = AddToggleRow(ReduceFlashingLabel, () => SetToggle(settings.SetReduceFlashing, !settings.ReduceFlashing), () => SetToggle(settings.SetReduceFlashing, false));
        reduceMotionRow = AddToggleRow(ReduceMotionLabel, () => SetToggle(settings.SetReduceMotion, !settings.ReduceMotion), () => SetToggle(settings.SetReduceMotion, false));
        settings.ReduceFlashingChanged += RefreshReduceFlashing;
        settings.ReduceMotionChanged += RefreshReduceMotion;
        RefreshReduceFlashing();
        RefreshReduceMotion();
        allyPopupsRow = view.AddRow();
        allyPopupsRow.SetLabel(AllyPopupsLabel);
        allyPopupsRow.BindingButton.onClick.AddListener(NextAllyPopups);
        allyPopupsRow.ResetButton.onClick.AddListener(ResetAllyPopups);
        settings.AllyPopupsChanged += RefreshAllyPopups;
        RefreshAllyPopups();
        chatTimestampsRow = AddToggleRow(ChatTimestampsLabel, () => SetToggle(settings.SetChatTimestamps, !settings.ChatTimestamps), () => SetToggle(settings.SetChatTimestamps, false));
        settings.ChatTimestampsChanged += RefreshChatTimestamps;
        RefreshChatTimestamps();
        chatWindowRow = view.AddRow();
        chatWindowRow.SetLabel(ChatWindowLabel);
        chatWindowRow.BindingButton.onClick.AddListener(ResetChatWindow);
        chatWindowRow.ResetButton.onClick.AddListener(ResetChatWindow);
        settings.ChatWindowChanged += RefreshChatWindow;
        RefreshChatWindow();
    }

    /// <summary>
    /// Shows whether the chat shows timestamps on the Chat timestamps row.
    /// </summary>
    private void RefreshChatTimestamps()
    {
        ShowToggle(chatTimestampsRow, settings.ChatTimestamps);
    }

    /// <summary>
    /// Chat window button and reset button: puts a moved chat window back in its place. Ignored while listening or
    /// confirming.
    /// </summary>
    private void ResetChatWindow()
    {
        if (!IsListening && !view.IsConfirmShown)
        {
            settings.SetChatWindow(default);
        }
    }

    /// <summary>
    /// Shows whether the chat window was moved on its row's button.
    /// </summary>
    private void RefreshChatWindow()
    {
        chatWindowRow.SetBinding(settings.ChatWindow.IsMoved ? ChatWindowMoved : ChatWindowDefault);
    }

    /// <summary>
    /// Allies' damage numbers button: keeps the next way they show, after Hidden Shown. Ignored while listening or
    /// confirming.
    /// </summary>
    private void NextAllyPopups()
    {
        if (!IsListening && !view.IsConfirmShown)
        {
            settings.SetAllyPopups((AllyPopups)(((int)settings.AllyPopups + 1) % AllyPopupsNames.Length));
        }
    }

    /// <summary>
    /// Allies' damage numbers reset button: back to Shown. Ignored while listening or confirming.
    /// </summary>
    private void ResetAllyPopups()
    {
        if (!IsListening && !view.IsConfirmShown)
        {
            settings.SetAllyPopups(AllyPopups.Shown);
        }
    }

    /// <summary>
    /// Shows how allies' damage numbers show on their row's button.
    /// </summary>
    private void RefreshAllyPopups()
    {
        allyPopupsRow.SetBinding(AllyPopupsNames[(int)settings.AllyPopups]);
    }

    /// <summary>
    /// Adds a toggle row labelled <paramref name="label"/> whose button runs <paramref name="toggle"/> and whose reset
    /// runs <paramref name="reset"/>.
    /// </summary>
    private KeyBindingRowView AddToggleRow(string label, UnityAction toggle, UnityAction reset)
    {
        KeyBindingRowView row = view.AddToggleRow();
        row.SetLabel(label);
        row.BindingButton.onClick.AddListener(toggle);
        row.ResetButton.onClick.AddListener(reset);
        return row;
    }

    /// <summary>
    /// Keeps <paramref name="on"/> through <paramref name="set"/>, a toggle row's setter. Ignored while listening or
    /// confirming.
    /// </summary>
    private void SetToggle(Func<bool, bool> set, bool on)
    {
        if (!IsListening && !view.IsConfirmShown)
        {
            set(on);
        }
    }

    /// <summary>
    /// Shows whether flashing is reduced on the Reduce flashing row.
    /// </summary>
    private void RefreshReduceFlashing()
    {
        ShowToggle(reduceFlashingRow, settings.ReduceFlashing);
    }

    /// <summary>
    /// Shows whether motion is reduced on the Reduce motion row.
    /// </summary>
    private void RefreshReduceMotion()
    {
        ShowToggle(reduceMotionRow, settings.ReduceMotion);
    }

    /// <summary>
    /// Shows <paramref name="on"/> on <paramref name="row"/>: its button reads On or Off and its box is marked while on.
    /// </summary>
    private static void ShowToggle(KeyBindingRowView row, bool on)
    {
        row.SetBinding(on ? OnName : OffName);
        row.SetToggle(on);
    }

    /// <summary>
    /// The UI size step the display shows: the one the local settings ask for, lowered to the steps that fit.
    /// </summary>
    private int UiSizeStep => Mathf.Clamp(settings.UiSizeStep, 0, Mathf.Min(uiScale.StepsThatFit, UiSizeNames.Length - 1));

    /// <summary>
    /// UI size button: keeps the next size that fits, after the largest Normal. Ignored while listening or confirming.
    /// </summary>
    private void NextUiSize()
    {
        if (!IsListening && !view.IsConfirmShown)
        {
            int sizes = Mathf.Min(uiScale.StepsThatFit, UiSizeNames.Length - 1) + 1;
            settings.SetUiSizeStep((UiSizeStep + 1) % sizes);
        }
    }

    /// <summary>
    /// UI size reset button: back to Normal. Ignored while listening or confirming.
    /// </summary>
    private void ResetUiSize()
    {
        if (!IsListening && !view.IsConfirmShown)
        {
            settings.SetUiSizeStep(0);
        }
    }

    /// <summary>
    /// Shows the UI size the display shows on the UI size row's button.
    /// </summary>
    private void RefreshUiSize()
    {
        uiSizeRow.SetBinding(UiSizeNames[UiSizeStep]);
    }

    /// <summary>
    /// Builds the Character section's Look row.
    /// </summary>
    private void BuildLookRow()
    {
        lookRow = view.AddRow();
        lookRow.SetLabel(LookLabel);
        lookRow.BindingButton.onClick.AddListener(NextLook);
        lookRow.ResetButton.onClick.AddListener(ResetLook);
        settings.CharacterLookChanged += RefreshLook;
        RefreshLook();
    }

    /// <summary>
    /// The catalog index of the look the local settings name, the first for an empty or unknown name.
    /// </summary>
    private int LookIndex => Mathf.Max(0, view.Looks.IndexOf(settings.CharacterLook));

    /// <summary>
    /// Look button: keeps the catalog's next look, after the last the first. Ignored while listening or confirming.
    /// </summary>
    private void NextLook()
    {
        if (!IsListening && !view.IsConfirmShown)
        {
            settings.SetCharacterLook(view.Looks.Get((LookIndex + 1) % view.Looks.Count).Name);
        }
    }

    /// <summary>
    /// Look reset button: keeps the catalog's first look. Ignored while listening or confirming.
    /// </summary>
    private void ResetLook()
    {
        if (!IsListening && !view.IsConfirmShown)
        {
            settings.SetCharacterLook(view.Looks.Get(0).Name);
        }
    }

    /// <summary>
    /// Shows the look the local settings name on the Look row's button.
    /// </summary>
    private void RefreshLook()
    {
        lookRow.SetBinding(view.Looks.Get(LookIndex).Name);
    }

    /// <summary>
    /// The index in its list of the swatch the local settings pick for colour choice <paramref name="choice"/>, its own
    /// for an empty or unknown name.
    /// </summary>
    private int ColourIndex(int choice)
    {
        CharacterColours colours = view.Colours;
        int picked = colours.IndexOfSwatch(choice, settings.CharacterColour(colours.Get(choice).Name));
        return picked >= 0 ? picked : Mathf.Max(0, colours.OwnIndex(choice));
    }

    /// <summary>
    /// A colour row's button: keeps the next swatch of its choice's list, after the last the first (the choice's own
    /// kept as no pick). Ignored while listening or confirming.
    /// </summary>
    private void NextColour(int choice)
    {
        if (IsListening || view.IsConfirmShown)
        {
            return;
        }

        CharacterColours colours = view.Colours;
        int next = (ColourIndex(choice) + 1) % colours.Swatches(choice).Count;
        settings.SetCharacterColour(colours.Get(choice).Name, next == colours.OwnIndex(choice) ? string.Empty : colours.Swatches(choice)[next].Name);
    }

    /// <summary>
    /// A colour row's reset button: back to the choice's own swatch. Ignored while listening or confirming.
    /// </summary>
    private void ResetColour(int choice)
    {
        if (!IsListening && !view.IsConfirmShown)
        {
            settings.SetCharacterColour(view.Colours.Get(choice).Name, string.Empty);
        }
    }

    /// <summary>
    /// Shows the swatch the local settings pick for each colour row's choice on its button.
    /// </summary>
    private void RefreshColours()
    {
        CharacterColours colours = view.Colours;
        for (int i = 0; i < colourRows.Count; i++)
        {
            int choice = colourChoices[i];
            CharacterSwatch swatch = colours.Swatches(choice)[ColourIndex(choice)];
            colourRows[i].SetBinding(swatch.Name);
            colourRows[i].SetSwatch(swatch.Ramps.Count > 0 ? swatch.Ramps[0].Middle : Color.clear);
        }
    }

    /// <summary>
    /// The index of the option the local settings pick for <paramref name="slot"/>, the first for an empty or unknown
    /// name.
    /// </summary>
    private int PartIndex(CharacterPartSlot slot)
    {
        return Mathf.Max(0, slot.IndexOf(settings.CharacterPart(slot.Slot.ToString())));
    }

    /// <summary>
    /// A part row's button: keeps the slot's next option, after the last the first (kept as no pick). Ignored while
    /// listening or confirming.
    /// </summary>
    private void NextPart(CharacterPartSlot slot)
    {
        if (IsListening || view.IsConfirmShown)
        {
            return;
        }

        int next = (PartIndex(slot) + 1) % slot.Options.Count;
        settings.SetCharacterPart(slot.Slot.ToString(), next == 0 ? string.Empty : slot.Options[next].Name);
    }

    /// <summary>
    /// A part row's reset button: back to the slot's first option. Ignored while listening or confirming.
    /// </summary>
    private void ResetPart(CharacterPartSlot slot)
    {
        if (!IsListening && !view.IsConfirmShown)
        {
            settings.SetCharacterPart(slot.Slot.ToString(), string.Empty);
        }
    }

    /// <summary>
    /// Shows the option the local settings pick for each part row's slot on its button.
    /// </summary>
    private void RefreshParts()
    {
        for (int i = 0; i < partRows.Count; i++)
        {
            partRows[i].SetBinding(partSlots[i].Options[PartIndex(partSlots[i])].Name);
        }
    }

    /// <summary>
    /// Updates every row's binding button with its action's current binding label.
    /// </summary>
    private void RefreshRows()
    {
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].SetBinding(bindings.GetLabel(bindings.GetAction(i)));
        }
    }
}
