using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VContainer.Unity;

/// <summary>
/// Drives the <see cref="CharacterScreenView"/> (Docs/UiRevamp.md, Phase 7), shared by the main menu and the in-game
/// menu as the options screen is. Its rows come from <see cref="CharacterGroups"/>: the Look row, the Body row and a row
/// per part slot, each with its colours as chips; a row shows for the look picked, a chip while the option worn carries
/// its colour, and every change shows at once and is kept in the local settings at once.
/// <list type="bullet">
/// <item><b>Rows:</b> a row's button moves to the next look or option; Reset puts back the row's look, or its part and
/// every colour of it; Reset all asks first, then puts back the look, every part and every colour.</item>
/// <item><b>Chips:</b> pressing one opens the swatch picker on its swatches, the one shown selected. Moving through the
/// picker shows each swatch on the preview and names it; pressing one keeps it and closes the picker; Back, or leaving
/// the picker, closes it and puts the preview's colours back.</item>
/// <item><b>Preview:</b> Turn left and Turn right turn the stage's character; the pose's button moves to the next pose
/// and reads it.</item>
/// <item><b>Foot line:</b> names the focused chip ("Tunic: Blue") or turn button.</item>
/// <item><b>Focus:</b> opening selects the first row's first control. Within a row Left and Right move along it, Left
/// from its first control to the pose's button; Up and Down move between rows to the same kind of control, Down from
/// the last to Reset all; Right from Turn right returns to the first row.</item>
/// </list>
/// While open it is a layer on the <see cref="MenuStack"/>: Back closes the picker, else the confirmation, else the
/// screen. Online in game it is a full screen with the vitals strip (<see cref="IVitalsScreen"/>).
/// </summary>
public sealed class CharacterScreenController : IStartable, ITickable, IDisposable, IMenuLayer, IVitalsScreen
{
    public const string TurnLeftName = "Turn left";
    public const string TurnRightName = "Turn right";
    public const string ChipFormat = "{0}: {1}";
    public static readonly string[] PoseNames = { "Standing", "Running", "Attacking" };

    private readonly CharacterScreenView view;
    private readonly ICharacterPreview preview;
    private readonly MenuStack menus;
    private readonly UISelection selection;
    private readonly UserSettings settings;
    private readonly List<CharacterGroup> groups = new();
    private readonly List<RowChipsView> rows = new();
    private readonly List<List<int>> shownChoices = new();
    private readonly List<RowChipsView> visibleRows = new();
    private readonly List<Selectable> rowControls = new();
    private CharacterLookCatalog looks;
    private CharacterColours colours;
    private CharacterParts parts;
    private Action onClosed;
    private GameObject focused;
    private int pickerRow = -1;
    private int pickerChip = -1;
    private int pickerChoice = -1;
    private bool started;

    /// <summary>
    /// Creates a closed screen over <paramref name="view"/>, showing <paramref name="preview"/> and editing the
    /// character kept in <paramref name="settings"/>.
    /// </summary>
    public CharacterScreenController(CharacterScreenView view, ICharacterPreview preview, MenuStack menus, UISelection selection, UserSettings settings)
    {
        this.view = view;
        this.preview = preview;
        this.menus = menus;
        this.selection = selection;
        this.settings = settings;
    }

    public bool IsOpen { get; private set; }
    public bool IsPickerOpen => pickerChoice >= 0;
    public VitalsStripView Vitals => view.Vitals;
    internal int RowCount => rows.Count;
    internal CharacterGroup GetGroup(int index) => groups[index];
    internal RowChipsView GetRow(int index) => rows[index];

    /// <summary>
    /// The colour choices row <paramref name="index"/>'s chips show now, in order.
    /// </summary>
    internal IReadOnlyList<int> ShownChoices(int index) => shownChoices[index];

    /// <summary>
    /// Hides the screen, builds a row per group of the view's looks and colours, wires every control and follows the
    /// local settings' look, parts and colours.
    /// </summary>
    public void Start()
    {
        looks = view.Looks;
        colours = view.Colours;
        parts = CharacterGroups.PartsOf(looks);
        view.SetShown(false);
        view.SetConfirmShown(false);
        view.Picker.Hide();
        view.SetWarning(null);
        view.SetFocusLine(string.Empty);
        foreach (CharacterGroup group in CharacterGroups.Build(looks, colours))
        {
            RowChipsView row = view.AddRow();
            row.SetLabel(group.Label);
            int index = rows.Count;
            row.ValueButton.onClick.AddListener(() => NextValue(index));
            row.ResetButton.onClick.AddListener(() => ResetRow(index));
            for (int chip = 0; chip < row.ChipCount; chip++)
            {
                int chipIndex = chip;
                row.GetChip(chip).Button.onClick.AddListener(() => OpenPicker(index, chipIndex));
            }

            groups.Add(group);
            rows.Add(row);
            shownChoices.Add(new List<int>());
        }

        view.TurnLeftButton.onClick.AddListener(() => Turn(-1));
        view.TurnRightButton.onClick.AddListener(() => Turn(1));
        view.PoseButton.onClick.AddListener(NextPose);
        view.ResetAllButton.onClick.AddListener(AskResetAll);
        view.BackButton.onClick.AddListener(Close);
        view.ConfirmYesButton.onClick.AddListener(ConfirmResetAll);
        view.ConfirmNoButton.onClick.AddListener(CancelResetAll);
        view.Picker.Focused += PreviewSwatch;
        view.Picker.Picked += PickSwatch;
        settings.CharacterLookChanged += Refresh;
        settings.CharacterPartsChanged += Refresh;
        settings.CharacterColoursChanged += Refresh;
        started = true;
        Refresh();
    }

    /// <summary>
    /// Stops following the local settings, leaves the menu stack and stops the preview.
    /// </summary>
    public void Dispose()
    {
        if (started)
        {
            settings.CharacterLookChanged -= Refresh;
            settings.CharacterPartsChanged -= Refresh;
            settings.CharacterColoursChanged -= Refresh;
            view.Picker.Focused -= PreviewSwatch;
            view.Picker.Picked -= PickSwatch;
        }

        menus.Remove(this);
        if (IsOpen)
        {
            preview.Hide();
        }
    }

    /// <summary>
    /// Shows the screen with its first row's first control selected and the preview standing, facing south, in the gear
    /// and with the attacks of <paramref name="weapon"/> (none for the starting weapon's). <paramref name="closed"/> runs
    /// once it closes.
    /// </summary>
    public void Open(Action closed, PlayerWeapon weapon = null)
    {
        if (IsOpen)
        {
            return;
        }

        IsOpen = true;
        onClosed = closed;
        preview.Show(weapon);
        view.SetPreview(preview.Texture);
        view.SetPose(PoseNames[(int)preview.Pose]);
        view.SetConfirmShown(false);
        view.SetWarning(null);
        view.SetShown(true);
        Refresh();
        menus.Push(this);
        focused = null;
        Selectable first = visibleRows.Count > 0 ? FirstControl(visibleRows[0]) : view.BackButton;
        selection.Select(first);
        ShowFocus(first != null ? first.gameObject : null);
    }

    /// <summary>
    /// Hides the screen, its picker and its confirmation, stops the preview and runs the callback given to
    /// <see cref="Open"/>.
    /// </summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        if (IsPickerOpen)
        {
            ClosePicker(false);
        }

        view.SetConfirmShown(false);
        preview.Hide();
        view.SetShown(false);
        menus.Remove(this);
        Action closed = onClosed;
        onClosed = null;
        closed?.Invoke();
    }

    /// <summary>
    /// Shows <paramref name="text"/> as the foot line's warning over the focused control's name, or clears it for null.
    /// </summary>
    public void SetWarning(string text)
    {
        view.SetWarning(text);
    }

    /// <summary>
    /// Closes the picker, else the confirmation, else the screen.
    /// </summary>
    public void HandleBack()
    {
        if (IsPickerOpen)
        {
            ClosePicker(false);
        }
        else if (view.IsConfirmShown)
        {
            CancelResetAll();
        }
        else
        {
            Close();
        }
    }

    /// <summary>
    /// Names the focused control on the foot line when the selection moves, and closes the picker, putting the
    /// preview's colours back, once the selection leaves it.
    /// </summary>
    public void Tick()
    {
        if (!IsOpen)
        {
            return;
        }

        GameObject current = selection.Current;
        if (IsPickerOpen && !InPicker(current))
        {
            ClosePicker(false, false);
        }

        if (current != focused)
        {
            ShowFocus(current);
        }
    }

    /// <summary>
    /// The look picked: the catalog's index of the look the local settings name, the first for an empty or unknown name.
    /// </summary>
    private int LookIndex => Mathf.Max(0, looks.IndexOf(settings.CharacterLook));

    /// <summary>
    /// The parts the local settings pick: for each slot the option they name, the first for an empty or unknown one.
    /// </summary>
    private CharacterPartPicks LocalParts()
    {
        CharacterPartPicks picks = default;
        if (parts == null)
        {
            return picks;
        }

        foreach (CharacterPartSlot slot in parts.Slots)
        {
            if (slot != null && !slot.FollowsWeapon)
            {
                picks = picks.With(slot.Slot, Mathf.Max(0, slot.IndexOf(settings.CharacterPart(slot.Slot.ToString()))));
            }
        }

        return picks;
    }

    /// <summary>
    /// The index in its list of the swatch the local settings pick for colour choice <paramref name="choice"/>, its own
    /// for an empty or unknown name.
    /// </summary>
    private int ColourIndex(int choice)
    {
        int picked = colours.IndexOfSwatch(choice, settings.CharacterColour(colours.Get(choice).Name));
        return picked >= 0 ? picked : Mathf.Max(0, colours.OwnIndex(choice));
    }

    /// <summary>
    /// The swatch the local settings pick for <paramref name="choice"/>.
    /// </summary>
    private CharacterSwatch SwatchOf(int choice)
    {
        IReadOnlyList<CharacterSwatch> swatches = colours.Swatches(choice);
        int index = ColourIndex(choice);
        return index < swatches.Count ? swatches[index] : null;
    }

    /// <summary>
    /// Shows each row for the look picked with its value and the chips of the colours the option worn carries, wires
    /// their navigation, and moves a selection the change hid to its row's first control.
    /// </summary>
    private void Refresh()
    {
        if (!started)
        {
            return;
        }

        CharacterLook look = looks.Get(LookIndex);
        CharacterPartPicks worn = LocalParts();
        visibleRows.Clear();
        for (int i = 0; i < rows.Count; i++)
        {
            CharacterGroup group = groups[i];
            RowChipsView row = rows[i];
            bool shown = CharacterGroups.Shows(group, look, parts);
            if (row.gameObject.activeSelf != shown)
            {
                row.gameObject.SetActive(shown);
            }

            List<int> choices = shownChoices[i];
            choices.Clear();
            if (!shown)
            {
                continue;
            }

            visibleRows.Add(row);
            row.SetValue(ValueOf(group, look, worn));
            foreach (int choice in group.Choices)
            {
                if (choices.Count < row.ChipCount && CharacterGroups.ShowsChoice(group, choice, colours, look, parts, worn))
                {
                    choices.Add(choice);
                }
            }

            for (int chip = 0; chip < row.ChipCount; chip++)
            {
                CharacterSwatch swatch = chip < choices.Count ? SwatchOf(choices[chip]) : null;
                row.SetChip(chip, chip < choices.Count, swatch != null && swatch.Ramps.Count > 0 ? (Color)swatch.Ramps[0].Middle : Color.clear);
            }
        }

        Navigate();
        if (IsOpen && !IsPickerOpen && !view.IsConfirmShown)
        {
            KeepSelectionShown();
            ShowFocus(selection.Current);
        }
    }

    /// <summary>
    /// The text on <paramref name="group"/>'s button: the look's name, the option worn, or null for the Body row.
    /// </summary>
    private string ValueOf(CharacterGroup group, CharacterLook look, CharacterPartPicks worn)
    {
        switch (group.Kind)
        {
            case CharacterGroupKind.Look:
                return look != null ? look.Name : string.Empty;
            case CharacterGroupKind.Part:
                return group.Slot.Options[Mathf.Max(0, parts.Shown(worn, group.Slot.Slot))].Name;
            default:
                return null;
        }
    }

    /// <summary>
    /// A row's button: the next look, or the slot's next option (after the last the first, kept as no pick).
    /// </summary>
    private void NextValue(int index)
    {
        if (!CanEdit)
        {
            return;
        }

        CharacterGroup group = groups[index];
        if (group.Kind == CharacterGroupKind.Look)
        {
            settings.SetCharacterLook(looks.Get((LookIndex + 1) % looks.Count).Name);
        }
        else if (group.Kind == CharacterGroupKind.Part)
        {
            CharacterPartSlot slot = group.Slot;
            int next = (Mathf.Max(0, slot.IndexOf(settings.CharacterPart(slot.Slot.ToString()))) + 1) % slot.Options.Count;
            settings.SetCharacterPart(slot.Slot.ToString(), next == 0 ? string.Empty : slot.Options[next].Name);
        }
    }

    /// <summary>
    /// A row's Reset: the catalog's first look, or the slot's first option and every colour of the row (those hidden
    /// too), each choice's own.
    /// </summary>
    private void ResetRow(int index)
    {
        if (!CanEdit)
        {
            return;
        }

        CharacterGroup group = groups[index];
        if (group.Kind == CharacterGroupKind.Look)
        {
            settings.SetCharacterLook(looks.Get(0).Name);
            return;
        }

        if (group.Kind == CharacterGroupKind.Part)
        {
            settings.SetCharacterPart(group.Slot.Slot.ToString(), string.Empty);
        }

        foreach (int choice in group.Choices)
        {
            settings.SetCharacterColour(colours.Get(choice).Name, string.Empty);
        }
    }

    /// <summary>
    /// Whether a row's controls answer: open, with neither the picker nor the confirmation showing.
    /// </summary>
    private bool CanEdit => IsOpen && !IsPickerOpen && !view.IsConfirmShown;

    /// <summary>
    /// Turns the preview <paramref name="steps"/> directions.
    /// </summary>
    private void Turn(int steps)
    {
        if (CanEdit)
        {
            preview.Turn(steps);
        }
    }

    /// <summary>
    /// Shows the preview's next pose and reads it on its button.
    /// </summary>
    private void NextPose()
    {
        if (CanEdit)
        {
            preview.NextPose();
            view.SetPose(PoseNames[(int)preview.Pose]);
        }
    }

    /// <summary>
    /// A chip: opens the swatch picker on its choice's swatches, under the chip, with the swatch shown selected.
    /// </summary>
    private void OpenPicker(int index, int chip)
    {
        if (!CanEdit || chip >= shownChoices[index].Count)
        {
            return;
        }

        pickerRow = index;
        pickerChip = chip;
        pickerChoice = shownChoices[index][chip];
        SwatchChipView anchor = rows[index].GetChip(chip);
        view.Picker.Show(colours.Get(pickerChoice).Name, colours.Swatches(pickerChoice), (RectTransform)anchor.transform, view.ContentBottomY);
        int current = Mathf.Min(ColourIndex(pickerChoice), view.Picker.ShownCount - 1);
        view.Picker.SetName(colours.Swatches(pickerChoice)[current].Name);
        selection.Select(view.Picker.GetChip(current).Button);
    }

    /// <summary>
    /// A picker chip selected: shows its swatch on the preview and names it.
    /// </summary>
    private void PreviewSwatch(int index)
    {
        if (!IsPickerOpen)
        {
            return;
        }

        string swatch = colours.Swatches(pickerChoice)[index].Name;
        view.Picker.SetName(swatch);
        preview.PreviewColour(pickerChoice, swatch);
    }

    /// <summary>
    /// A picker chip pressed: keeps its swatch (the choice's own kept as no pick) and closes the picker.
    /// </summary>
    private void PickSwatch(int index)
    {
        if (!IsPickerOpen)
        {
            return;
        }

        int choice = pickerChoice;
        string name = colours.Swatches(choice)[index].Name;
        ClosePicker(true);
        settings.SetCharacterColour(colours.Get(choice).Name, index == colours.OwnIndex(choice) ? string.Empty : name);
    }

    /// <summary>
    /// Hides the picker; unless a swatch was <paramref name="picked"/>, puts the preview's colours back. With
    /// <paramref name="reselect"/>, selects the chip that opened it again.
    /// </summary>
    private void ClosePicker(bool picked, bool reselect = true)
    {
        int row = pickerRow;
        int chip = pickerChip;
        pickerRow = -1;
        pickerChip = -1;
        pickerChoice = -1;
        view.Picker.Hide();
        if (!picked)
        {
            preview.ShowPickedColours();
        }

        if (reselect && row >= 0 && rows[row].GetChip(chip).gameObject.activeInHierarchy)
        {
            selection.Select(rows[row].GetChip(chip).Button);
        }
    }

    /// <summary>
    /// Whether <paramref name="selected"/> is one of the picker's chips.
    /// </summary>
    private bool InPicker(GameObject selected)
    {
        return selected != null && selected.transform.IsChildOf(view.Picker.transform);
    }

    /// <summary>
    /// Reset all: shows the confirmation with Cancel selected.
    /// </summary>
    private void AskResetAll()
    {
        if (!CanEdit)
        {
            return;
        }

        view.SetConfirmShown(true);
        selection.Select(view.ConfirmNoButton);
    }

    /// <summary>
    /// The confirmation accepted: puts back the look, every part and every colour, and reselects Reset all.
    /// </summary>
    private void ConfirmResetAll()
    {
        view.SetConfirmShown(false);
        settings.ResetCharacter();
        selection.Select(view.ResetAllButton);
    }

    /// <summary>
    /// The confirmation declined: hides it and reselects Reset all.
    /// </summary>
    private void CancelResetAll()
    {
        view.SetConfirmShown(false);
        selection.Select(view.ResetAllButton);
    }

    /// <summary>
    /// Names <paramref name="selected"/> on the foot line: a chip by its choice and swatch, a turn button by its action,
    /// anything else by nothing.
    /// </summary>
    private void ShowFocus(GameObject selected)
    {
        focused = selected;
        view.SetFocusLine(FocusText(selected));
    }

    /// <summary>
    /// The foot line's text for <paramref name="selected"/>.
    /// </summary>
    private string FocusText(GameObject selected)
    {
        if (selected == null)
        {
            return string.Empty;
        }

        if (selected == view.TurnLeftButton.gameObject)
        {
            return TurnLeftName;
        }

        if (selected == view.TurnRightButton.gameObject)
        {
            return TurnRightName;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            List<int> choices = shownChoices[i];
            for (int chip = 0; chip < choices.Count; chip++)
            {
                if (rows[i].GetChip(chip).gameObject == selected)
                {
                    CharacterSwatch swatch = SwatchOf(choices[chip]);
                    return string.Format(ChipFormat, colours.Get(choices[chip]).Name, swatch != null ? swatch.Name : string.Empty);
                }
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// When the selection is a control a change just hid (a chip of a part taken off), selects its row's first control.
    /// </summary>
    private void KeepSelectionShown()
    {
        GameObject current = selection.Current;
        if (current == null || current.activeInHierarchy)
        {
            return;
        }

        foreach (RowChipsView row in rows)
        {
            if (current.transform.IsChildOf(row.transform))
            {
                selection.Select(row.gameObject.activeInHierarchy ? FirstControl(row) : FirstControl(visibleRows[0]));
                return;
            }
        }
    }

    /// <summary>
    /// The first control of <paramref name="row"/>: its button, else its first chip, else its Reset.
    /// </summary>
    private static Selectable FirstControl(RowChipsView row)
    {
        if (row.IsValueShown)
        {
            return row.ValueButton;
        }

        return row.ChipCount > 0 && row.GetChip(0).gameObject.activeSelf ? row.GetChip(0).Button : row.ResetButton;
    }

    /// <summary>
    /// Wires the explicit navigation of the rows, the preview's controls and the footer for the rows and chips shown.
    /// </summary>
    private void Navigate()
    {
        Selectable firstRow = visibleRows.Count > 0 ? FirstControl(visibleRows[0]) : view.ResetAllButton;
        Selectable lastRow = visibleRows.Count > 0 ? FirstControl(visibleRows[visibleRows.Count - 1]) : view.ResetAllButton;
        for (int r = 0; r < visibleRows.Count; r++)
        {
            RowChipsView row = visibleRows[r];
            RowChipsView above = r > 0 ? visibleRows[r - 1] : null;
            RowChipsView below = r < visibleRows.Count - 1 ? visibleRows[r + 1] : null;
            ControlsOf(row, rowControls);
            for (int c = 0; c < rowControls.Count; c++)
            {
                Selectable control = rowControls[c];
                int kind = KindOf(row, control);
                control.navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = c > 0 ? rowControls[c - 1] : view.PoseButton,
                    selectOnRight = c < rowControls.Count - 1 ? rowControls[c + 1] : null,
                    selectOnUp = above != null ? ControlOfKind(above, kind) : null,
                    selectOnDown = below != null ? ControlOfKind(below, kind) : view.ResetAllButton
                };
            }
        }

        SetNavigation(view.TurnLeftButton, null, view.PoseButton, null, view.ResetAllButton);
        SetNavigation(view.PoseButton, view.TurnLeftButton, view.TurnRightButton, null, view.ResetAllButton);
        SetNavigation(view.TurnRightButton, view.PoseButton, firstRow, null, view.ResetAllButton);
        SetNavigation(view.ResetAllButton, view.TurnLeftButton, view.BackButton, lastRow, null);
        SetNavigation(view.BackButton, view.ResetAllButton, null, lastRow, null);
    }

    /// <summary>
    /// Fills <paramref name="controls"/> with <paramref name="row"/>'s shown controls, left to right.
    /// </summary>
    private static void ControlsOf(RowChipsView row, List<Selectable> controls)
    {
        controls.Clear();
        if (row.IsValueShown)
        {
            controls.Add(row.ValueButton);
        }

        for (int chip = 0; chip < row.ChipCount; chip++)
        {
            if (row.GetChip(chip).gameObject.activeSelf)
            {
                controls.Add(row.GetChip(chip).Button);
            }
        }

        controls.Add(row.ResetButton);
    }

    /// <summary>
    /// The kind of <paramref name="control"/> in <paramref name="row"/>: -1 its button, -2 its Reset, else its chip's
    /// index.
    /// </summary>
    private static int KindOf(RowChipsView row, Selectable control)
    {
        if (control == row.ValueButton)
        {
            return -1;
        }

        if (control == row.ResetButton)
        {
            return -2;
        }

        for (int chip = 0; chip < row.ChipCount; chip++)
        {
            if (row.GetChip(chip).Button == control)
            {
                return chip;
            }
        }

        return -1;
    }

    /// <summary>
    /// <paramref name="row"/>'s control of <paramref name="kind"/>: its Reset for Reset; its button for the button, else
    /// its first chip; the chip of that index or its last shown, else its button; else its first control.
    /// </summary>
    private static Selectable ControlOfKind(RowChipsView row, int kind)
    {
        if (kind == -2)
        {
            return row.ResetButton;
        }

        if (kind >= 0)
        {
            for (int chip = Mathf.Min(kind, row.ChipCount - 1); chip >= 0; chip--)
            {
                if (row.GetChip(chip).gameObject.activeSelf)
                {
                    return row.GetChip(chip).Button;
                }
            }
        }

        return FirstControl(row);
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
