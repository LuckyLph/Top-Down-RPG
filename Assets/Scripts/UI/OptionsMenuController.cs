using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using VContainer.Unity;

/// <summary>
/// Drives the <see cref="OptionsMenuView"/>: a Display section with the UI size row
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
/// itself takes Escape as cancel). Opening it selects the first row. The look, parts and colours are the Character
/// screen's (<see cref="CharacterScreenController"/>, Docs/UiRevamp.md, Phase 7).
/// </summary>
public sealed class OptionsMenuController : IStartable, ITickable, IDisposable, IMenuLayer
{
    public const string ListeningPromptFormat = "Press a key or mouse button for {0}.\nEsc to cancel.";
    public const string MovedNoticeFormat = "{0} moved to {1}.";
    public const string ResetAllNotice = "All controls are back to their defaults.";
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
    /// Creates a closed options controller that edits <paramref name="bindings"/>, and the preferences kept in
    /// <paramref name="settings"/>, through <paramref name="view"/>; the UI size row offers the steps
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
    internal KeyBindingRowView UiSizeRow => uiSizeRow;
    internal KeyBindingRowView ReduceFlashingRow => reduceFlashingRow;
    internal KeyBindingRowView ReduceMotionRow => reduceMotionRow;
    internal KeyBindingRowView AllyPopupsRow => allyPopupsRow;
    internal KeyBindingRowView ChatTimestampsRow => chatTimestampsRow;
    internal KeyBindingRowView ChatWindowRow => chatWindowRow;

    /// <summary>
    /// The row built for the rebindable action at <paramref name="index"/>, in <see cref="InputBindings"/> order.
    /// </summary>
    internal KeyBindingRowView GetRow(int index)
    {
        return rows[index];
    }

    /// <summary>
    /// Hides the screen, builds the Display section, then a section header for each new section and a labelled row per
    /// rebindable action, wires the buttons, and keeps the rows' labels in sync with
    /// <see cref="InputBindings.BindingsChanged"/>, <see cref="UserSettings.UiSizeStepChanged"/>,
    /// <see cref="UiScale.Changed"/>, <see cref="UserSettings.ReduceFlashingChanged"/>,
    /// <see cref="UserSettings.ReduceMotionChanged"/>, <see cref="UserSettings.AllyPopupsChanged"/>,
    /// <see cref="UserSettings.ChatTimestampsChanged"/> and <see cref="UserSettings.ChatWindowChanged"/>.
    /// </summary>
    public void Start()
    {
        view.SetShown(false);
        view.HideListening();
        view.SetConfirmShown(false);
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
    /// The first row of the screen: the UI size row, the Reduce flashing row or a binding row, in that order; null
    /// without rows.
    /// </summary>
    private KeyBindingRowView FirstRow()
    {
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
