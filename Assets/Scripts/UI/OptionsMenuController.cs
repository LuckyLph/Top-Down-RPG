using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using VContainer.Unity;

/// <summary>
/// Drives the <see cref="OptionsMenuView"/> (Docs/KeyBindings.md, Options screen; Docs/UiRevamp.md, Phase 8): a rail of
/// three categories beside a pane of the current one's rows.
/// <list type="bullet">
/// <item><b>Accessibility:</b> the UI size row (Docs/DesignSystem.md, Phase 3), its button showing the size the display
/// shows and moving to the next one that fits, its reset going back to Normal; the Reduce flashing and Reduce motion
/// toggle rows (Phase 6), each button reading On or Off and turning it the other way, each reset turning it off.</item>
/// <item><b>Interface:</b> the allies' damage numbers row (Docs/UiRevamp.md, Phase 3), its button showing Shown, Smaller
/// or Hidden and moving to the next, its reset going back to Shown; the Chat timestamps toggle row and the Chat window
/// row (Phase 12), its button reading Default or Moved and, like its reset, putting a moved chat window back.</item>
/// <item><b>Controls:</b> a row per rebindable action of <see cref="InputBindings"/> under its section, rebinding a row
/// when its binding is selected (listening starts on the next tick, so the click itself is never captured), naming the
/// action that moved on a swap, its reset putting its default back.</item>
/// </list>
/// Moving onto a category shows its rows; Right or Submit enters its first row, and Left from any row returns to it. The
/// focused row's description shows at the pane's foot, and a category's while its item is focused. A row's Reset shows
/// only while it differs from its default, and is out of navigation while hidden; the footer's reset, shown while
/// anything in the category differs, puts the whole category back after a confirmation. Opening from the main menu shows
/// the first category, in game the one used last this session (Controls the first time), and the Controls page's Change
/// keys the Controls' first row. While open it is a layer on the <see cref="MenuStack"/>; Back closes the confirmation,
/// or else the screen, and does nothing while a rebind is pending or listening (the rebind itself takes Escape as
/// cancel). Online in game it is a full screen with the vitals strip (<see cref="IVitalsScreen"/>). The look, parts and
/// colours are the Character screen's (<see cref="CharacterScreenController"/>, Docs/UiRevamp.md, Phase 7).
/// </summary>
public sealed class OptionsMenuController : IStartable, ITickable, IDisposable, IMenuLayer, IVitalsScreen
{
    /// <summary>The options screen's categories, in the rail's order.</summary>
    public enum Category
    {
        Accessibility,
        Interface,
        Controls
    }

    /// <summary>
    /// Where the screen opens: on the first category (the main menu), on the category used last this session, Controls
    /// the first time (the in-game menu), or on the Controls' first row (the in-game menu's Change keys).
    /// </summary>
    public enum Opening
    {
        First,
        Last,
        Controls
    }

    public const string ListeningPromptFormat = "Press a key or mouse button for {0}.\nEsc to cancel.";
    public const string MovedNoticeFormat = "{0} moved to {1}.";
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
    public const string UiSizeDescription = "Draws the menus and the HUD larger.";
    public const string UiSizeNoLargerSize = "Larger sizes need a larger window.";
    public const string ReduceFlashingDescription = "Hits flash at a third of their strength, and torches and fires stop flickering.";
    public const string ReduceMotionDescription = "The camera doesn't shake when you're hit, and damage numbers fade in place instead of rising.";
    public const string AllyPopupsDescription = "How the numbers of hits and heals on other players show: like yours, smaller, or not at all.";
    public const string ChatTimestampsDescription = "Starts each chat line with the time it arrived.";
    public const string ChatWindowDescription = "Puts the chat window back in its place and size. Move it by its header while the chat is open.";
    public static readonly string[] CategoryNames = { "Accessibility", "Interface", "Controls" };
    public static readonly string[] CategoryDescriptions =
    {
        "How large the UI is drawn, and less flashing and motion.",
        "How allies' damage numbers show, and the chat.",
        "The key or mouse button for every control.",
    };
    public static readonly string[] ResetLabels = { "Reset accessibility", "Reset interface", "Reset controls" };
    public static readonly string[] ResetQuestions =
    {
        "Reset all accessibility options to their defaults?",
        "Reset all interface options to their defaults?",
        "Reset all controls to their defaults?",
    };
    public static readonly string[] ResetNotices =
    {
        "All accessibility options are back to their defaults.",
        "All interface options are back to their defaults.",
        "All controls are back to their defaults.",
    };

    private const string UiSizeDescriptionNoLargerSize = UiSizeDescription + "\n" + UiSizeNoLargerSize;

    /// <summary>A row of the screen: its view, its category, and the action it binds or the preference it shows.</summary>
    private sealed class OptionRow
    {
        public OptionRow(KeyBindingRowView view, Category category, InputAction action, string description, Func<bool> changed)
        {
            View = view;
            Category = category;
            Action = action;
            Description = description;
            Changed = changed;
        }

        public KeyBindingRowView View { get; }
        public Category Category { get; }
        public InputAction Action { get; }
        public string Description { get; }
        public Func<bool> Changed { get; }
    }

    private readonly OptionsMenuView view;
    private readonly InputBindings bindings;
    private readonly MenuStack menus;
    private readonly UISelection selection;
    private readonly UserSettings settings;
    private readonly UiScale uiScale;
    private readonly RailItemView[] railItems = new RailItemView[3];
    private readonly List<OptionRow> options = new();
    private readonly List<KeyBindingRowView> rows = new();
    private readonly List<GameObject> headers = new();
    private readonly List<Category> headerCategories = new();
    private readonly List<KeyBindingRowView> shownRows = new();
    private KeyBindingRowView uiSizeRow;
    private KeyBindingRowView reduceFlashingRow;
    private KeyBindingRowView reduceMotionRow;
    private KeyBindingRowView allyPopupsRow;
    private KeyBindingRowView chatTimestampsRow;
    private KeyBindingRowView chatWindowRow;
    private Category current;
    private Category last = Category.Controls;
    private GameObject focused;
    private Action onClosed;
    private int pendingRebind = -1;
    private int listeningRow = -1;
    private bool started;

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
    public Category CurrentCategory => current;
    public VitalsStripView Vitals => view.Vitals;
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
    /// The rail's item for <paramref name="category"/>.
    /// </summary>
    internal RailItemView GetRailItem(Category category)
    {
        return railItems[(int)category];
    }

    /// <summary>
    /// Hides the screen, builds the rail, the Accessibility and Interface rows, then a section header for each new
    /// section and a labelled row per rebindable action, wires the buttons, shows the first category, and keeps the rows
    /// in sync with the bindings, the preferences and the UI sizes that fit.
    /// </summary>
    public void Start()
    {
        view.SetShown(false);
        view.HideListening();
        view.SetConfirmShown(false);
        view.SetWarning(null);
        view.SetDescription(string.Empty);
        for (int i = 0; i < railItems.Length; i++)
        {
            RailItemView item = view.AddRailItem();
            item.SetLabel(CategoryNames[i]);
            Category category = (Category)i;
            item.Button.onClick.AddListener(() => EnterCategory(category));
            railItems[i] = item;
        }

        BuildAccessibility();
        BuildInterface();
        BuildControls();
        view.CategoryResetButton.onClick.AddListener(AskReset);
        view.BackButton.onClick.AddListener(Close);
        view.ConfirmYesButton.onClick.AddListener(ConfirmReset);
        view.ConfirmNoButton.onClick.AddListener(CancelReset);
        bindings.BindingsChanged += Refresh;
        settings.UiSizeStepChanged += Refresh;
        uiScale.Changed += Refresh;
        settings.ReduceFlashingChanged += Refresh;
        settings.ReduceMotionChanged += Refresh;
        settings.AllyPopupsChanged += Refresh;
        settings.ChatTimestampsChanged += Refresh;
        settings.ChatWindowChanged += Refresh;
        started = true;
        ShowCategory(Category.Accessibility);
        Refresh();
    }

    /// <summary>
    /// Stops following the bindings and preferences, leaves the menu stack and cancels a rebind that is still listening.
    /// </summary>
    public void Dispose()
    {
        if (started)
        {
            bindings.BindingsChanged -= Refresh;
            settings.UiSizeStepChanged -= Refresh;
            uiScale.Changed -= Refresh;
            settings.ReduceFlashingChanged -= Refresh;
            settings.ReduceMotionChanged -= Refresh;
            settings.AllyPopupsChanged -= Refresh;
            settings.ChatTimestampsChanged -= Refresh;
            settings.ChatWindowChanged -= Refresh;
        }

        menus.Remove(this);
        if (IsListening)
        {
            bindings.CancelRebind();
        }
    }

    /// <summary>
    /// Shows the screen as <paramref name="opening"/> asks: the first category or the one used last with its rail item
    /// selected, or the Controls with their first row selected. <paramref name="closed"/> runs once it closes.
    /// </summary>
    public void Open(Action closed, Opening opening = Opening.First)
    {
        onClosed = closed;
        pendingRebind = -1;
        view.SetNotice(string.Empty);
        view.SetConfirmShown(false);
        view.HideListening();
        view.SetShown(true);
        Category category = opening == Opening.First ? Category.Accessibility : opening == Opening.Controls ? Category.Controls : last;
        ShowCategory(category);
        Refresh();
        menus.Push(this);
        Selectable first = opening == Opening.Controls ? FirstRowControl() : railItems[(int)category].Button;
        selection.Select(first);
        ShowFocus(first.gameObject);
    }

    /// <summary>
    /// Hides the screen, cancelling a rebind that is listening, keeps its category as the one used last, and runs the
    /// callback given to <see cref="Open"/>.
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

        last = current;
        view.SetConfirmShown(false);
        view.HideListening();
        view.SetShown(false);
        menus.Remove(this);
        Action closed = onClosed;
        onClosed = null;
        closed?.Invoke();
    }

    /// <summary>
    /// Starts listening for the rebind requested on an earlier frame, so the click that asked for it is not captured as
    /// the new binding, and follows the selection: moving onto a category's item shows it, and the description follows
    /// the focused row.
    /// </summary>
    public void Tick()
    {
        if (!IsOpen)
        {
            return;
        }

        if (pendingRebind >= 0)
        {
            int row = pendingRebind;
            pendingRebind = -1;
            BeginListening(row);
        }

        GameObject selected = selection.Current;
        if (selected != focused)
        {
            ShowFocus(selected);
        }
    }

    /// <summary>
    /// Closes the confirmation, or else the screen. Ignored while a rebind is pending or listening.
    /// </summary>
    public void HandleBack()
    {
        if (IsListening || pendingRebind >= 0)
        {
            return;
        }

        if (view.IsConfirmShown)
        {
            CancelReset();
        }
        else
        {
            Close();
        }
    }

    /// <summary>
    /// Shows <paramref name="text"/> as the notice line's warning over the notice, or clears it for null.
    /// </summary>
    public void SetWarning(string text)
    {
        view.SetWarning(text);
    }

    /// <summary>
    /// Whether nothing is being rebound or confirmed, so a row's button or reset may act.
    /// </summary>
    private bool CanChange => !IsListening && !view.IsConfirmShown;

    /// <summary>
    /// The UI size step the display shows: the one the local settings ask for, lowered to the steps that fit.
    /// </summary>
    private int UiSizeStep => Mathf.Clamp(settings.UiSizeStep, 0, Mathf.Min(uiScale.StepsThatFit, UiSizeNames.Length - 1));

    /// <summary>
    /// Builds the Accessibility rows: UI size, Reduce flashing and Reduce motion.
    /// </summary>
    private void BuildAccessibility()
    {
        uiSizeRow = view.AddRow();
        uiSizeRow.SetLabel(UiSizeLabel);
        uiSizeRow.BindingButton.onClick.AddListener(NextUiSize);
        uiSizeRow.ResetButton.onClick.AddListener(() => Change(() => settings.SetUiSizeStep(0)));
        AddOption(uiSizeRow, Category.Accessibility, UiSizeDescription, () => UiSizeStep != 0);
        reduceFlashingRow = AddToggleRow(ReduceFlashingLabel, () => settings.SetReduceFlashing(!settings.ReduceFlashing), () => settings.SetReduceFlashing(false));
        AddOption(reduceFlashingRow, Category.Accessibility, ReduceFlashingDescription, () => settings.ReduceFlashing);
        reduceMotionRow = AddToggleRow(ReduceMotionLabel, () => settings.SetReduceMotion(!settings.ReduceMotion), () => settings.SetReduceMotion(false));
        AddOption(reduceMotionRow, Category.Accessibility, ReduceMotionDescription, () => settings.ReduceMotion);
    }

    /// <summary>
    /// Builds the Interface rows: Allies' damage numbers, Chat timestamps and Chat window.
    /// </summary>
    private void BuildInterface()
    {
        allyPopupsRow = view.AddRow();
        allyPopupsRow.SetLabel(AllyPopupsLabel);
        allyPopupsRow.BindingButton.onClick.AddListener(() => Change(() => settings.SetAllyPopups((AllyPopups)(((int)settings.AllyPopups + 1) % AllyPopupsNames.Length))));
        allyPopupsRow.ResetButton.onClick.AddListener(() => Change(() => settings.SetAllyPopups(AllyPopups.Shown)));
        AddOption(allyPopupsRow, Category.Interface, AllyPopupsDescription, () => settings.AllyPopups != AllyPopups.Shown);
        chatTimestampsRow = AddToggleRow(ChatTimestampsLabel, () => settings.SetChatTimestamps(!settings.ChatTimestamps), () => settings.SetChatTimestamps(false));
        AddOption(chatTimestampsRow, Category.Interface, ChatTimestampsDescription, () => settings.ChatTimestamps);
        chatWindowRow = view.AddRow();
        chatWindowRow.SetLabel(ChatWindowLabel);
        chatWindowRow.BindingButton.onClick.AddListener(() => Change(() => settings.SetChatWindow(default)));
        chatWindowRow.ResetButton.onClick.AddListener(() => Change(() => settings.SetChatWindow(default)));
        AddOption(chatWindowRow, Category.Interface, ChatWindowDescription, () => settings.ChatWindow.IsMoved);
    }

    /// <summary>
    /// Builds the Controls rows: a section header for each new section, then a labelled row per rebindable action.
    /// </summary>
    private void BuildControls()
    {
        string section = null;
        for (int i = 0; i < bindings.Count; i++)
        {
            InputAction action = bindings.GetAction(i);
            string actionSection = bindings.GetSection(action);
            if (actionSection != section)
            {
                headers.Add(view.AddSectionHeader(actionSection).gameObject);
                headerCategories.Add(Category.Controls);
                section = actionSection;
            }

            KeyBindingRowView row = view.AddRow();
            row.SetLabel(bindings.GetDisplayName(action));
            int index = i;
            row.BindingButton.onClick.AddListener(() => RequestRebind(index));
            row.ResetButton.onClick.AddListener(() => ResetRow(index));
            rows.Add(row);
            options.Add(new OptionRow(row, Category.Controls, action, null, null));
        }
    }

    /// <summary>
    /// Keeps <paramref name="row"/>, a preference's, in <paramref name="category"/>, described by
    /// <paramref name="description"/> and differing from its default while <paramref name="changed"/>.
    /// </summary>
    private void AddOption(KeyBindingRowView row, Category category, string description, Func<bool> changed)
    {
        options.Add(new OptionRow(row, category, null, description, changed));
    }

    /// <summary>
    /// Adds a toggle row labelled <paramref name="label"/> whose button runs <paramref name="toggle"/> and whose reset
    /// runs <paramref name="reset"/>, each ignored while listening or confirming.
    /// </summary>
    private KeyBindingRowView AddToggleRow(string label, Func<bool> toggle, Func<bool> reset)
    {
        KeyBindingRowView row = view.AddToggleRow();
        row.SetLabel(label);
        row.BindingButton.onClick.AddListener(() => Change(toggle));
        row.ResetButton.onClick.AddListener(() => Change(reset));
        return row;
    }

    /// <summary>
    /// Runs <paramref name="change"/>, a preference's setter, unless a rebind is listening or a confirmation shows.
    /// </summary>
    private void Change(Func<bool> change)
    {
        if (CanChange)
        {
            change();
        }
    }

    /// <summary>
    /// UI size button: keeps the next size that fits, after the largest Normal. Ignored while listening or confirming.
    /// </summary>
    private void NextUiSize()
    {
        if (CanChange)
        {
            int sizes = Mathf.Min(uiScale.StepsThatFit, UiSizeNames.Length - 1) + 1;
            settings.SetUiSizeStep((UiSizeStep + 1) % sizes);
        }
    }

    /// <summary>
    /// A rail item's press (a click or Submit): shows its category and enters its first row. Ignored while listening or
    /// confirming.
    /// </summary>
    private void EnterCategory(Category category)
    {
        if (!CanChange)
        {
            return;
        }

        if (category != current)
        {
            ShowCategory(category);
        }

        Selectable first = FirstRowControl();
        selection.Select(first);
        ShowFocus(first.gameObject);
    }

    /// <summary>
    /// Makes <paramref name="category"/> the current one: its rail item raised, its rows and headers shown and the
    /// others hidden, the footer's reset its own, the rows from the top.
    /// </summary>
    private void ShowCategory(Category category)
    {
        current = category;
        for (int i = 0; i < railItems.Length; i++)
        {
            railItems[i].SetCurrent(i == (int)category);
        }

        foreach (OptionRow option in options)
        {
            SetActive(option.View.gameObject, option.Category == category);
        }

        for (int i = 0; i < headers.Count; i++)
        {
            SetActive(headers[i], headerCategories[i] == category);
        }

        ShowResets();
        if (IsOpen)
        {
            view.ScrollToTop();
        }
    }

    /// <summary>
    /// Shows every row's value: the UI size the display shows, each toggle's state, the allies' popups, whether the
    /// chat window moved and each action's binding; then each Reset and the footer's.
    /// </summary>
    private void Refresh()
    {
        uiSizeRow.SetBinding(UiSizeNames[UiSizeStep]);
        ShowToggle(reduceFlashingRow, settings.ReduceFlashing);
        ShowToggle(reduceMotionRow, settings.ReduceMotion);
        allyPopupsRow.SetBinding(AllyPopupsNames[(int)settings.AllyPopups]);
        ShowToggle(chatTimestampsRow, settings.ChatTimestamps);
        chatWindowRow.SetBinding(settings.ChatWindow.IsMoved ? ChatWindowMoved : ChatWindowDefault);
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].SetBinding(bindings.GetLabel(bindings.GetAction(i)));
        }

        ShowResets();
        if (IsOpen)
        {
            Describe(selection.Current);
        }
    }

    /// <summary>
    /// Shows each row's Reset while the row differs from its default, and the footer's reset while any row of the
    /// current category does; rewires the navigation and moves the selection off a Reset that just hid.
    /// </summary>
    private void ShowResets()
    {
        bool any = false;
        foreach (OptionRow option in options)
        {
            bool changed = option.Action != null ? !bindings.IsDefault(option.Action) : option.Changed();
            option.View.SetResetShown(changed);
            any |= changed && option.Category == current;
        }

        view.SetCategoryReset(ResetLabels[(int)current], any);
        Navigate();
        KeepSelectionShown();
    }

    /// <summary>
    /// When the selection is a Reset that just hid, selects its row's button; when it is the footer's reset, Back.
    /// </summary>
    private void KeepSelectionShown()
    {
        GameObject selected = selection.Current;
        if (!IsOpen || selected == null || selected.activeSelf)
        {
            return;
        }

        foreach (OptionRow option in options)
        {
            if (option.View.ResetButton.gameObject == selected)
            {
                selection.Select(option.View.BindingButton);
                return;
            }
        }

        if (selected == view.CategoryResetButton.gameObject)
        {
            selection.Select(view.BackButton);
        }
    }

    /// <summary>
    /// The first row's button in the current category, or Back when it has none.
    /// </summary>
    private Selectable FirstRowControl()
    {
        foreach (OptionRow option in options)
        {
            if (option.Category == current)
            {
                return option.View.BindingButton;
            }
        }

        return view.BackButton;
    }

    /// <summary>
    /// Wires the explicit navigation: the rail's items loop up and down and go right to the first row; within the pane a
    /// row's button goes left to the current item and right to its Reset when shown, Up and Down move to the same kind
    /// of control in the row above or below (a hidden Reset's row giving its button), and Down from the last row reaches
    /// the footer, whose buttons go left to the rail and up to the last row.
    /// </summary>
    private void Navigate()
    {
        shownRows.Clear();
        foreach (OptionRow option in options)
        {
            if (option.Category == current)
            {
                shownRows.Add(option.View);
            }
        }

        Selectable item = railItems[(int)current].Button;
        Selectable footer = view.IsCategoryResetShown ? view.CategoryResetButton : view.BackButton;
        Selectable firstRow = shownRows.Count > 0 ? shownRows[0].BindingButton : footer;
        Selectable lastRow = shownRows.Count > 0 ? shownRows[shownRows.Count - 1].BindingButton : item;
        for (int i = 0; i < railItems.Length; i++)
        {
            SetNavigation(railItems[i].Button, null, firstRow, railItems[(i + railItems.Length - 1) % railItems.Length].Button, railItems[(i + 1) % railItems.Length].Button);
        }

        for (int r = 0; r < shownRows.Count; r++)
        {
            KeyBindingRowView row = shownRows[r];
            KeyBindingRowView above = r > 0 ? shownRows[r - 1] : null;
            KeyBindingRowView below = r < shownRows.Count - 1 ? shownRows[r + 1] : null;
            SetNavigation(row.BindingButton, item, row.IsResetShown ? row.ResetButton : null, above != null ? above.BindingButton : null, below != null ? below.BindingButton : footer);
            if (row.IsResetShown)
            {
                SetNavigation(row.ResetButton, row.BindingButton, null, above != null ? ResetOrButton(above) : null, below != null ? ResetOrButton(below) : footer);
            }
        }

        SetNavigation(view.CategoryResetButton, item, view.BackButton, lastRow, null);
        SetNavigation(view.BackButton, view.IsCategoryResetShown ? view.CategoryResetButton : item, null, lastRow, null);
    }

    /// <summary>
    /// <paramref name="row"/>'s Reset when shown, else its button.
    /// </summary>
    private static Selectable ResetOrButton(KeyBindingRowView row)
    {
        return row.IsResetShown ? row.ResetButton : row.BindingButton;
    }

    /// <summary>
    /// Follows <paramref name="selected"/>, the selection just moved to: a category's item shows its category, and the
    /// description follows (<see cref="Describe"/>).
    /// </summary>
    private void ShowFocus(GameObject selected)
    {
        focused = selected;
        if (selected == null)
        {
            return;
        }

        for (int i = 0; i < railItems.Length; i++)
        {
            if (railItems[i].gameObject == selected && (Category)i != current)
            {
                ShowCategory((Category)i);
            }
        }

        Describe(selected);
    }

    /// <summary>
    /// Describes <paramref name="selected"/> at the pane's foot: a category's item its category, a row's button or Reset
    /// the row, anything else nothing; nothing selected (a rebind listening) keeps the last.
    /// </summary>
    private void Describe(GameObject selected)
    {
        if (selected == null)
        {
            return;
        }

        for (int i = 0; i < railItems.Length; i++)
        {
            if (railItems[i].gameObject == selected)
            {
                view.SetDescription(CategoryDescriptions[i]);
                return;
            }
        }

        foreach (OptionRow option in options)
        {
            if (option.View.BindingButton.gameObject == selected || option.View.ResetButton.gameObject == selected)
            {
                view.SetDescription(DescriptionOf(option));
                return;
            }
        }

        view.SetDescription(string.Empty);
    }

    /// <summary>
    /// <paramref name="option"/>'s description: its action's, or its preference's; the UI size row's says too that a
    /// larger size needs a larger window where none fits.
    /// </summary>
    private string DescriptionOf(OptionRow option)
    {
        if (option.Action != null)
        {
            return bindings.GetDescription(option.Action);
        }

        return option.View == uiSizeRow && uiScale.StepsThatFit < 1 ? UiSizeDescriptionNoLargerSize : option.Description;
    }

    /// <summary>
    /// Binding button of row <paramref name="index"/>: queues its rebind for the next tick. Ignored while closed,
    /// listening or confirming.
    /// </summary>
    private void RequestRebind(int index)
    {
        if (IsOpen && CanChange)
        {
            pendingRebind = index;
        }
    }

    /// <summary>
    /// Shows the listening prompt for row <paramref name="index"/>, clears the selection and starts the interactive
    /// rebind; if it cannot start, hides the prompt and reselects the row's binding button.
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
    /// Ends listening: hides the prompt, names the action that moved when the new input was assigned, and reselects the
    /// row's binding button if the screen is still open.
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
    /// Reset button of row <paramref name="index"/>: puts that action back on its default input and names the action
    /// that moved, if any. Ignored while listening or confirming.
    /// </summary>
    private void ResetRow(int index)
    {
        if (!CanChange)
        {
            return;
        }

        bindings.Reset(bindings.GetAction(index), out InputAction swappedWith);
        ShowMoved(swappedWith);
    }

    /// <summary>
    /// The footer's reset: asks whether to put the current category back, with Cancel selected. Ignored while listening.
    /// </summary>
    private void AskReset()
    {
        if (IsListening)
        {
            return;
        }

        view.SetConfirmQuestion(ResetQuestions[(int)current]);
        view.SetConfirmShown(true);
        selection.Select(view.ConfirmNoButton);
    }

    /// <summary>
    /// Confirmation accepted: puts every row of the current category back on its default in one write, hides the
    /// confirmation and says so.
    /// </summary>
    private void ConfirmReset()
    {
        view.SetConfirmShown(false);
        switch (current)
        {
            case Category.Accessibility:
                settings.ResetAccessibility();
                break;
            case Category.Interface:
                settings.ResetInterface();
                break;
            default:
                bindings.ResetAll();
                break;
        }

        view.SetNotice(ResetNotices[(int)current]);
        selection.Select(view.IsCategoryResetShown ? view.CategoryResetButton : view.BackButton);
    }

    /// <summary>
    /// Confirmation declined: hides it and reselects the footer's reset.
    /// </summary>
    private void CancelReset()
    {
        view.SetConfirmShown(false);
        selection.Select(view.IsCategoryResetShown ? view.CategoryResetButton : view.BackButton);
    }

    /// <summary>
    /// Shows which action <paramref name="swappedWith"/> moved to which input, or clears the notice when nothing was
    /// swapped.
    /// </summary>
    private void ShowMoved(InputAction swappedWith)
    {
        view.SetNotice(swappedWith != null
            ? string.Format(MovedNoticeFormat, bindings.GetDisplayName(swappedWith), bindings.GetLabel(swappedWith))
            : string.Empty);
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
    /// Shows or hides <paramref name="target"/>, touching it only when that changes.
    /// </summary>
    private static void SetActive(GameObject target, bool active)
    {
        if (target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }

    /// <summary>
    /// Sets <paramref name="control"/>'s explicit navigation.
    /// </summary>
    private static void SetNavigation(Selectable control, Selectable left, Selectable right, Selectable up, Selectable down)
    {
        control.navigation = new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnLeft = left,
            selectOnRight = right,
            selectOnUp = up,
            selectOnDown = down
        };
    }
}
