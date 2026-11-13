using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

/// <summary>
/// Tests for <see cref="OptionsMenuController"/> on the real options menu prefab and project input actions: the rail of
/// categories and its navigation (Docs/UiRevamp.md, Phase 8), the Accessibility, Interface and Controls rows, the
/// focused row's description, Reset shown only on changed rows, the category's reset with its confirmation, where the
/// screen opens, Back and close handling, interactive rebinding and per-row reset.
/// </summary>
public class OptionsMenuControllerTests : InputTestFixture
{
    private const string ProjectActionsPath = "Assets/InputSystem_Actions.inputactions";
    private const string RebindableActionsPath = "Assets/Data/Input/RebindableActions.asset";
    private const string OptionsMenuPath = "Assets/Prefabs/UI/OptionsMenu.prefab";
    private const int AttackMoveRow = 1;
    private const int StopRow = 2;
    private const int Ability1Row = 3;

    private InputActionAsset actions;
    private InputBindings bindings;
    private OptionsMenuView view;
    private FakeMenuInput menuInput;
    private MenuStack menus;
    private OptionsMenuController controller;
    private UserSettings settings;
    private UiScaleSettings scaleSettings;
    private UiScale uiScale;
    private GameObject eventObject;
    private EventSystem events;
    private Keyboard keyboard;
    private int closedCount;

    /// <summary>
    /// Adds a test keyboard, binds a copy of the project input actions with the project's rebindable list and
    /// in-memory settings, instantiates the options menu prefab and starts the controller on a fresh menu stack, its
    /// selection on a new event system.
    /// </summary>
    public override void Setup()
    {
        base.Setup();
        keyboard = InputSystem.AddDevice<Keyboard>();
        actions = InputActionAsset.FromJson(AssetDatabase.LoadAssetAtPath<InputActionAsset>(ProjectActionsPath).ToJson());
        bindings = new InputBindings(actions, AssetDatabase.LoadAssetAtPath<RebindableActionList>(RebindableActionsPath), new UserSettings(new MemorySaveStore()));
        bindings.Initialize();
        view = Object.Instantiate(AssetDatabase.LoadAssetAtPath<OptionsMenuView>(OptionsMenuPath));
        menuInput = new FakeMenuInput();
        menus = new MenuStack(menuInput);
        settings = new UserSettings(new MemorySaveStore());
        scaleSettings = UiScaleSettings.Create();
        uiScale = new UiScale(scaleSettings, settings);
        uiScale.Apply(1920, 1080, 0);
        eventObject = new GameObject("EventSystem");
        events = eventObject.AddComponent<EventSystem>();
        controller = new OptionsMenuController(view, bindings, menus, new UISelection(events), settings, uiScale);
        controller.Start();
        closedCount = 0;
    }

    /// <summary>
    /// Disposes the controller and bindings, then destroys the menu instance, the event system and the copied input
    /// actions.
    /// </summary>
    public override void TearDown()
    {
        controller.Dispose();
        bindings.Dispose();
        Object.DestroyImmediate(view.gameObject);
        Object.DestroyImmediate(eventObject);
        Object.DestroyImmediate(actions);
        Object.DestroyImmediate(scaleSettings);
        base.TearDown();
    }

    /// <summary>
    /// Starting builds the rail's three items, Accessibility, Interface and Controls, then the Accessibility rows (UI
    /// size, Reduce flashing, Reduce motion), the Interface rows (Allies' damage numbers, Chat timestamps, Chat window)
    /// and ten binding rows under four section headers, labelled with each action and its current binding, the chat key
    /// last; it shows the first category's rows only and leaves the screen hidden.
    /// </summary>
    [Test]
    public void Start_BuildsTheRailAndTheRows_AndShowsTheFirstCategory()
    {
        Assert.That(view.IsShown, Is.False);
        Assert.That(view.Rail.childCount, Is.EqualTo(3));
        for (int i = 0; i < 3; i++)
        {
            RailItemView item = view.Rail.GetChild(i).GetComponent<RailItemView>();
            Assert.That(item, Is.SameAs(controller.GetRailItem((OptionsMenuController.Category)i)));
            Assert.That(item.Label.text, Is.EqualTo(OptionsMenuController.CategoryNames[i]));
        }

        Assert.That(OptionsMenuController.CategoryNames, Is.EqualTo(new[] { "Accessibility", "Interface", "Controls" }));
        Assert.That(controller.RowCount, Is.EqualTo(10));
        Assert.That(view.RowsContainer.childCount, Is.EqualTo(20), "Three Accessibility rows, three Interface rows, four section headers and ten binding rows.");
        KeyBindingRowView[] preferences = { controller.UiSizeRow, controller.ReduceFlashingRow, controller.ReduceMotionRow, controller.AllyPopupsRow, controller.ChatTimestampsRow, controller.ChatWindowRow };
        for (int i = 0; i < preferences.Length; i++)
        {
            Assert.That(view.RowsContainer.GetChild(i).GetComponent<KeyBindingRowView>(), Is.SameAs(preferences[i]));
        }

        Assert.That(view.RowsContainer.GetChild(6).GetComponent<TMP_Text>().text, Is.EqualTo("Movement"));
        Assert.That(view.RowsContainer.GetChild(18).GetComponent<TMP_Text>().text, Is.EqualTo("Other"));
        Assert.That(controller.GetRow(9).Label.text, Is.EqualTo("Chat"));
        Assert.That(controller.GetRow(9).BindingText.text, Is.EqualTo(bindings.GetLabel(bindings.GetAction(9))));
        Assert.That(controller.GetRow(0).Label.text, Is.EqualTo("Move / attack"));
        Assert.That(controller.GetRow(0).BindingText.text, Is.EqualTo("Right mouse button"));
        Assert.That(controller.GetRow(AttackMoveRow).Label.text, Is.EqualTo("Attack move"));
        Assert.That(controller.GetRow(AttackMoveRow).BindingText.text, Is.EqualTo("A"));
        Assert.That(controller.GetRow(StopRow).BindingText.text, Is.EqualTo("S"));
        Assert.That(controller.GetRow(8).Label.text, Is.EqualTo("Weapon ability 2"));

        Assert.That(controller.CurrentCategory, Is.EqualTo(OptionsMenuController.Category.Accessibility));
        Assert.That(controller.UiSizeRow.gameObject.activeSelf, Is.True);
        Assert.That(controller.AllyPopupsRow.gameObject.activeSelf, Is.False, "The Interface rows wait for their category.");
        Assert.That(controller.GetRow(0).gameObject.activeSelf, Is.False, "The Controls rows wait for theirs.");
        Assert.That(view.RowsContainer.GetChild(6).gameObject.activeSelf, Is.False, "So do their headers.");
    }

    /// <summary>
    /// The rail: the current category's item is raised with its pip and the others flat; its items loop up and down, and
    /// Right goes to the first row. Moving onto an item shows its category's rows and the footer's reset becomes the
    /// category's; Submit on an item enters its first row; Left from any row returns to the current item, and Down from
    /// the last row reaches the footer.
    /// </summary>
    [Test]
    public void TheRail_ShowsACategoryWhenMovedOnto_AndRightOrSubmitEntersItsFirstRow()
    {
        RailItemView accessibility = controller.GetRailItem(OptionsMenuController.Category.Accessibility);
        RailItemView interfaceItem = controller.GetRailItem(OptionsMenuController.Category.Interface);
        RailItemView controls = controller.GetRailItem(OptionsMenuController.Category.Controls);
        controller.Open(null);
        Assert.That(events.currentSelectedGameObject, Is.SameAs(accessibility.gameObject), "The main menu opens on the first category's item.");
        Assert.That(accessibility.IsCurrent, Is.True);
        Assert.That(accessibility.Pip.enabled, Is.True);
        Assert.That(interfaceItem.IsCurrent, Is.False);
        Assert.That(interfaceItem.Pip.enabled, Is.False, "Shape and the pip mark the current category, not colour alone.");
        Assert.That(accessibility.Button.navigation.selectOnDown, Is.SameAs(interfaceItem.Button));
        Assert.That(controls.Button.navigation.selectOnDown, Is.SameAs(accessibility.Button), "The rail loops.");
        Assert.That(accessibility.Button.navigation.selectOnUp, Is.SameAs(controls.Button));
        Assert.That(accessibility.Button.navigation.selectOnRight, Is.SameAs(controller.UiSizeRow.BindingButton));

        events.SetSelectedGameObject(interfaceItem.gameObject);
        controller.Tick();
        Assert.That(controller.CurrentCategory, Is.EqualTo(OptionsMenuController.Category.Interface));
        Assert.That(interfaceItem.IsCurrent, Is.True);
        Assert.That(accessibility.IsCurrent, Is.False);
        Assert.That(controller.AllyPopupsRow.gameObject.activeSelf, Is.True);
        Assert.That(controller.UiSizeRow.gameObject.activeSelf, Is.False);
        Assert.That(interfaceItem.Button.navigation.selectOnRight, Is.SameAs(controller.AllyPopupsRow.BindingButton));
        Assert.That(view.CategoryResetText.text, Is.EqualTo("Reset interface"));

        controls.Button.onClick.Invoke();
        Assert.That(controller.CurrentCategory, Is.EqualTo(OptionsMenuController.Category.Controls));
        Assert.That(events.currentSelectedGameObject, Is.SameAs(controller.GetRow(0).BindingButton.gameObject), "Submit enters the first row.");
        Assert.That(controller.GetRow(Ability1Row).BindingButton.navigation.selectOnLeft, Is.SameAs(controls.Button), "Left from any row returns to the rail.");
        Assert.That(controller.GetRow(StopRow).BindingButton.navigation.selectOnDown, Is.SameAs(controller.GetRow(Ability1Row).BindingButton), "Down skips the section header.");
        Assert.That(controller.GetRow(9).BindingButton.navigation.selectOnDown, Is.SameAs(view.BackButton), "Down from the last row reaches the footer.");
        Assert.That(view.BackButton.navigation.selectOnUp, Is.SameAs(controller.GetRow(9).BindingButton));
        Assert.That(view.BackButton.navigation.selectOnLeft, Is.SameAs(controls.Button));
        Assert.That(view.CategoryResetText.text, Is.EqualTo("Reset controls"));
    }

    /// <summary>
    /// The pane's foot describes the focus: a category's item its category, a preference's row the controller's line, a
    /// binding's row the line from the rebindable list (every action has one), the UI size row adding that a larger size
    /// needs a larger window where none fits; the footer shows none.
    /// </summary>
    [Test]
    public void TheDescription_FollowsTheFocusedRow()
    {
        controller.Open(null);
        Assert.That(view.Description.text, Is.EqualTo(OptionsMenuController.CategoryDescriptions[0]));

        Select(controller.ReduceFlashingRow.BindingButton);
        Assert.That(view.Description.text, Is.EqualTo(OptionsMenuController.ReduceFlashingDescription));
        Select(controller.UiSizeRow.BindingButton);
        Assert.That(view.Description.text, Is.EqualTo(OptionsMenuController.UiSizeDescription), "1080p fits Large.");
        uiScale.Apply(1280, 720, 0);
        Assert.That(view.Description.text, Is.EqualTo(OptionsMenuController.UiSizeDescription + "\n" + OptionsMenuController.UiSizeNoLargerSize), "720p fits no larger size, and the line says why.");

        Select(controller.GetRailItem(OptionsMenuController.Category.Interface).Button);
        Assert.That(view.Description.text, Is.EqualTo(OptionsMenuController.CategoryDescriptions[1]));
        Select(controller.ChatWindowRow.BindingButton);
        Assert.That(view.Description.text, Is.EqualTo(OptionsMenuController.ChatWindowDescription));

        Select(controller.GetRailItem(OptionsMenuController.Category.Controls).Button);
        for (int i = 0; i < controller.RowCount; i++)
        {
            string line = bindings.GetDescription(bindings.GetAction(i));
            Assert.That(line, Is.Not.Empty, $"{bindings.GetDisplayName(bindings.GetAction(i))} has a description.");
            Select(controller.GetRow(i).BindingButton);
            Assert.That(view.Description.text, Is.EqualTo(line));
        }

        Assert.That(bindings.GetDescription(bindings.GetAction(AttackMoveRow)), Is.EqualTo("Walk to the pointer and attack the first enemy on the way."));
        Select(view.BackButton);
        Assert.That(view.Description.text, Is.Empty);
        foreach (string line in new[] { OptionsMenuController.UiSizeDescription, OptionsMenuController.ReduceFlashingDescription, OptionsMenuController.ReduceMotionDescription, OptionsMenuController.AllyPopupsDescription, OptionsMenuController.ChatTimestampsDescription, OptionsMenuController.ChatWindowDescription })
        {
            Assert.That(line, Is.Not.Empty);
        }
    }

    /// <summary>
    /// A row's Reset shows only while the row differs from its default and is out of navigation while hidden; the
    /// footer's reset shows while anything in the category differs. Pressing a Reset hides it and selects its row's
    /// button.
    /// </summary>
    [Test]
    public void Reset_ShowsOnlyOnChangedRows_AndLeavesNavigationWhileHidden()
    {
        controller.Open(null);
        Assert.That(controller.ReduceMotionRow.IsResetShown, Is.False);
        Assert.That(controller.ReduceMotionRow.BindingButton.navigation.selectOnRight, Is.Null, "A hidden Reset is out of navigation.");
        Assert.That(view.IsCategoryResetShown, Is.False, "Nothing in the category differs.");

        settings.SetReduceMotion(true);
        Assert.That(controller.ReduceMotionRow.IsResetShown, Is.True);
        Assert.That(controller.ReduceFlashingRow.IsResetShown, Is.False);
        Assert.That(controller.ReduceMotionRow.BindingButton.navigation.selectOnRight, Is.SameAs(controller.ReduceMotionRow.ResetButton));
        Assert.That(view.IsCategoryResetShown, Is.True);
        Assert.That(view.CategoryResetText.text, Is.EqualTo("Reset accessibility"));
        Assert.That(view.BackButton.navigation.selectOnLeft, Is.SameAs(view.CategoryResetButton));

        controller.GetRailItem(OptionsMenuController.Category.Controls).Button.onClick.Invoke();
        Assert.That(view.IsCategoryResetShown, Is.False, "Accessibility's change is not the Controls'.");
        bindings.TryAssign(actions.FindAction("Stop", true), "<Keyboard>/f", out _);
        Assert.That(controller.GetRow(StopRow).IsResetShown, Is.True);
        Assert.That(controller.GetRow(AttackMoveRow).IsResetShown, Is.False);
        Assert.That(controller.GetRow(StopRow).ResetButton.navigation.selectOnUp, Is.SameAs(controller.GetRow(AttackMoveRow).BindingButton), "Up from a Reset to the button of a row whose Reset hides.");
        Assert.That(controller.GetRow(AttackMoveRow).BindingButton.navigation.selectOnRight, Is.Null);
        Assert.That(view.IsCategoryResetShown, Is.True);

        events.SetSelectedGameObject(controller.GetRow(StopRow).ResetButton.gameObject);
        controller.GetRow(StopRow).ResetButton.onClick.Invoke();
        Assert.That(controller.GetRow(StopRow).BindingText.text, Is.EqualTo("S"));
        Assert.That(controller.GetRow(StopRow).IsResetShown, Is.False);
        Assert.That(events.currentSelectedGameObject, Is.SameAs(controller.GetRow(StopRow).BindingButton.gameObject), "The selection leaves the Reset that hid.");
        Assert.That(view.IsCategoryResetShown, Is.False);
    }

    /// <summary>
    /// The footer's reset puts back only its own category, after a confirmation naming it: Accessibility's UI size,
    /// flashing and motion, then Interface's allies' damage numbers and chat, each saying so, with Back selected once
    /// nothing is left to reset; the bindings stay as they were.
    /// </summary>
    [Test]
    public void TheCategoryReset_PutsBackOnlyItsCategory()
    {
        settings.SetUiSizeStep(1);
        settings.SetReduceFlashing(true);
        settings.SetReduceMotion(true);
        settings.SetAllyPopups(AllyPopups.Hidden);
        settings.SetChatTimestamps(true);
        settings.SetChatWindow(new ChatWindowPlace(ScreenCorner.BottomLeft, new Vector2Int(8, 80), new Vector2Int(200, 100)));
        bindings.TryAssign(actions.FindAction("Stop", true), "<Keyboard>/f", out _);
        controller.Open(null);

        view.CategoryResetButton.onClick.Invoke();
        Assert.That(view.IsConfirmShown, Is.True);
        Assert.That(view.ConfirmText.text, Is.EqualTo(OptionsMenuController.ResetQuestions[0]));
        view.ConfirmYesButton.onClick.Invoke();
        Assert.That(settings.UiSizeStep, Is.Zero);
        Assert.That(settings.ReduceFlashing, Is.False);
        Assert.That(settings.ReduceMotion, Is.False);
        Assert.That(settings.AllyPopups, Is.EqualTo(AllyPopups.Hidden), "Interface keeps its options.");
        Assert.That(controller.GetRow(StopRow).BindingText.text, Is.EqualTo("F"), "The controls keep theirs.");
        Assert.That(view.NoticeText.text, Is.EqualTo(OptionsMenuController.ResetNotices[0]));
        Assert.That(view.IsCategoryResetShown, Is.False);
        Assert.That(events.currentSelectedGameObject, Is.SameAs(view.BackButton.gameObject));

        controller.GetRailItem(OptionsMenuController.Category.Interface).Button.onClick.Invoke();
        view.CategoryResetButton.onClick.Invoke();
        Assert.That(view.ConfirmText.text, Is.EqualTo(OptionsMenuController.ResetQuestions[1]));
        view.ConfirmYesButton.onClick.Invoke();
        Assert.That(settings.AllyPopups, Is.EqualTo(AllyPopups.Shown));
        Assert.That(settings.ChatTimestamps, Is.False);
        Assert.That(settings.ChatWindow.IsMoved, Is.False);
        Assert.That(controller.GetRow(StopRow).BindingText.text, Is.EqualTo("F"));
        Assert.That(view.NoticeText.text, Is.EqualTo(OptionsMenuController.ResetNotices[1]));
    }

    /// <summary>
    /// In game the screen opens on the category used last this session, Controls the first time, with its item
    /// selected; from the main menu it opens on the first category, which then counts as the last.
    /// </summary>
    [Test]
    public void Reopening_InGame_ShowsTheCategoryUsedLast()
    {
        controller.Open(null, OptionsMenuController.Opening.Last);
        Assert.That(controller.CurrentCategory, Is.EqualTo(OptionsMenuController.Category.Controls), "Controls the first time.");
        Assert.That(events.currentSelectedGameObject, Is.SameAs(controller.GetRailItem(OptionsMenuController.Category.Controls).gameObject));

        Select(controller.GetRailItem(OptionsMenuController.Category.Interface).Button);
        controller.Close();
        controller.Open(null, OptionsMenuController.Opening.Last);
        Assert.That(controller.CurrentCategory, Is.EqualTo(OptionsMenuController.Category.Interface));
        Assert.That(controller.AllyPopupsRow.gameObject.activeSelf, Is.True);
        Assert.That(events.currentSelectedGameObject, Is.SameAs(controller.GetRailItem(OptionsMenuController.Category.Interface).gameObject));
        controller.Close();

        controller.Open(null);
        Assert.That(controller.CurrentCategory, Is.EqualTo(OptionsMenuController.Category.Accessibility), "The main menu opens on the first category.");
        controller.Close();
        controller.Open(null, OptionsMenuController.Opening.Last);
        Assert.That(controller.CurrentCategory, Is.EqualTo(OptionsMenuController.Category.Accessibility));
    }

    /// <summary>
    /// The in-game menu's Change keys opens the screen on the Controls with the first binding selected and described.
    /// </summary>
    [Test]
    public void ChangeKeys_LandsOnTheControls_AtTheFirstBinding()
    {
        controller.Open(null, OptionsMenuController.Opening.Controls);

        Assert.That(controller.CurrentCategory, Is.EqualTo(OptionsMenuController.Category.Controls));
        Assert.That(controller.GetRailItem(OptionsMenuController.Category.Controls).IsCurrent, Is.True);
        Assert.That(events.currentSelectedGameObject, Is.SameAs(controller.GetRow(0).BindingButton.gameObject));
        Assert.That(view.Description.text, Is.EqualTo(bindings.GetDescription(bindings.GetAction(0))));
        Assert.That(controller.GetRow(0).gameObject.activeSelf, Is.True);
        Assert.That(controller.UiSizeRow.gameObject.activeSelf, Is.False);
    }

    /// <summary>
    /// The UI size row shows the size the display shows: Normal by default; its button keeps the next size that fits
    /// (at 1080p Large, then Normal again), its reset goes back to Normal, a step that no longer fits shows the largest
    /// that does, and on a display that fits none the button keeps Normal.
    /// </summary>
    [Test]
    public void TheUiSizeRow_CyclesTheSizesThatFit()
    {
        KeyBindingRowView row = controller.UiSizeRow;
        Assert.That(row.Label.text, Is.EqualTo(OptionsMenuController.UiSizeLabel));
        Assert.That(row.BindingText.text, Is.EqualTo("Normal"));

        controller.Open(null);
        row.BindingButton.onClick.Invoke();
        Assert.That(settings.UiSizeStep, Is.EqualTo(1));
        Assert.That(row.BindingText.text, Is.EqualTo("Large"));
        Assert.That(row.IsResetShown, Is.True);
        row.BindingButton.onClick.Invoke();
        Assert.That(settings.UiSizeStep, Is.Zero, "1080p fits Large only, so Normal comes after it.");
        Assert.That(row.BindingText.text, Is.EqualTo("Normal"));

        settings.SetUiSizeStep(2);
        uiScale.Apply(3840, 2160, settings.UiSizeStep);
        Assert.That(row.BindingText.text, Is.EqualTo("Larger"));
        uiScale.Apply(1920, 1080, settings.UiSizeStep);
        Assert.That(row.BindingText.text, Is.EqualTo("Large"), "Larger no longer fits.");
        row.ResetButton.onClick.Invoke();
        Assert.That(settings.UiSizeStep, Is.Zero);
        Assert.That(row.BindingText.text, Is.EqualTo("Normal"));

        uiScale.Apply(1280, 720, settings.UiSizeStep);
        row.BindingButton.onClick.Invoke();
        Assert.That(settings.UiSizeStep, Is.Zero, "720p fits no larger size.");
        Assert.That(row.BindingText.text, Is.EqualTo("Normal"));
    }

    /// <summary>
    /// The Reduce flashing and Reduce motion rows (Docs/DesignSystem.md, Phase 6) are toggle rows: each reads Off with
    /// its box unmarked by default, its button turns its preference on (reading On, its box marked) and off again, its
    /// reset turns it off, each changes only its own preference, and a row follows a change made elsewhere. Neither
    /// changes while a confirmation shows.
    /// </summary>
    [Test]
    public void TheToggleRows_ShowAndTurnTheirPreferences()
    {
        KeyBindingRowView flashing = controller.ReduceFlashingRow;
        KeyBindingRowView motion = controller.ReduceMotionRow;
        Assert.That(flashing.Label.text, Is.EqualTo(OptionsMenuController.ReduceFlashingLabel));
        Assert.That(motion.Label.text, Is.EqualTo(OptionsMenuController.ReduceMotionLabel));
        foreach (KeyBindingRowView row in new[] { flashing, motion })
        {
            Assert.That(row.ToggleMark, Is.Not.Null, "A toggle row has a mark.");
            Assert.That(row.BindingText.text, Is.EqualTo(OptionsMenuController.OffName));
            Assert.That(row.ToggleMark.enabled, Is.False);
        }

        controller.Open(null);
        flashing.BindingButton.onClick.Invoke();
        Assert.That(settings.ReduceFlashing, Is.True);
        Assert.That(settings.ReduceMotion, Is.False);
        Assert.That(flashing.BindingText.text, Is.EqualTo(OptionsMenuController.OnName));
        Assert.That(flashing.ToggleMark.enabled, Is.True);
        Assert.That(motion.BindingText.text, Is.EqualTo(OptionsMenuController.OffName));
        flashing.BindingButton.onClick.Invoke();
        Assert.That(settings.ReduceFlashing, Is.False);
        Assert.That(flashing.BindingText.text, Is.EqualTo(OptionsMenuController.OffName));
        Assert.That(flashing.ToggleMark.enabled, Is.False);

        motion.BindingButton.onClick.Invoke();
        Assert.That(settings.ReduceMotion, Is.True);
        Assert.That(motion.ToggleMark.enabled, Is.True);
        motion.ResetButton.onClick.Invoke();
        Assert.That(settings.ReduceMotion, Is.False);
        Assert.That(motion.BindingText.text, Is.EqualTo(OptionsMenuController.OffName));

        settings.SetReduceFlashing(true);
        settings.SetReduceMotion(true);
        Assert.That(flashing.BindingText.text, Is.EqualTo(OptionsMenuController.OnName), "A row follows a change made elsewhere.");
        Assert.That(motion.ToggleMark.enabled, Is.True);

        view.CategoryResetButton.onClick.Invoke();
        flashing.BindingButton.onClick.Invoke();
        motion.ResetButton.onClick.Invoke();
        Assert.That(settings.ReduceFlashing, Is.True, "Nothing changes while a confirmation shows.");
        Assert.That(settings.ReduceMotion, Is.True);
    }

    /// <summary>
    /// The Chat timestamps row (Docs/UiRevamp.md, Phase 12) reads Off and turns them on and off, its reset off; the Chat
    /// window row reads Default, Moved once the window was moved, and its button, like its reset, puts the window back;
    /// both follow changes made elsewhere, and nothing changes while a confirmation shows.
    /// </summary>
    [Test]
    public void TheChatRows_TurnTimestamps_AndPutTheWindowBack()
    {
        KeyBindingRowView stamps = controller.ChatTimestampsRow;
        KeyBindingRowView window = controller.ChatWindowRow;
        Assert.That(stamps.Label.text, Is.EqualTo(OptionsMenuController.ChatTimestampsLabel));
        Assert.That(stamps.BindingText.text, Is.EqualTo(OptionsMenuController.OffName));
        Assert.That(window.Label.text, Is.EqualTo(OptionsMenuController.ChatWindowLabel));
        Assert.That(window.BindingText.text, Is.EqualTo(OptionsMenuController.ChatWindowDefault));

        controller.Open(null, OptionsMenuController.Opening.Last);
        stamps.BindingButton.onClick.Invoke();
        Assert.That(settings.ChatTimestamps, Is.True);
        Assert.That(stamps.BindingText.text, Is.EqualTo(OptionsMenuController.OnName));
        stamps.ResetButton.onClick.Invoke();
        Assert.That(settings.ChatTimestamps, Is.False);

        ChatWindowPlace moved = new(ScreenCorner.BottomLeft, new Vector2Int(8, 80), new Vector2Int(200, 100));
        settings.SetChatWindow(moved);
        Assert.That(window.BindingText.text, Is.EqualTo(OptionsMenuController.ChatWindowMoved), "The row follows a change made elsewhere.");
        window.BindingButton.onClick.Invoke();
        Assert.That(settings.ChatWindow.IsMoved, Is.False);
        Assert.That(window.BindingText.text, Is.EqualTo(OptionsMenuController.ChatWindowDefault));
        settings.SetChatWindow(moved);
        window.ResetButton.onClick.Invoke();
        Assert.That(settings.ChatWindow.IsMoved, Is.False);

        settings.SetChatWindow(moved);
        view.CategoryResetButton.onClick.Invoke();
        window.BindingButton.onClick.Invoke();
        stamps.BindingButton.onClick.Invoke();
        Assert.That(settings.ChatWindow.IsMoved, Is.True, "Nothing changes while a confirmation shows.");
        Assert.That(settings.ChatTimestamps, Is.False);
    }

    /// <summary>
    /// The allies' damage numbers row (Docs/UiRevamp.md, Phase 3) reads Shown by default; its button keeps the next way
    /// (Smaller, Hidden, then Shown again), its reset goes back to Shown, it follows a change made elsewhere, and nothing
    /// changes while a confirmation shows.
    /// </summary>
    [Test]
    public void TheAlliesDamageNumbersRow_CyclesShownSmallerAndHidden()
    {
        KeyBindingRowView row = controller.AllyPopupsRow;
        Assert.That(row.Label.text, Is.EqualTo(OptionsMenuController.AllyPopupsLabel));
        Assert.That(row.BindingText.text, Is.EqualTo("Shown"));

        controller.Open(null);
        row.BindingButton.onClick.Invoke();
        Assert.That(settings.AllyPopups, Is.EqualTo(AllyPopups.Smaller));
        Assert.That(row.BindingText.text, Is.EqualTo("Smaller"));
        row.BindingButton.onClick.Invoke();
        Assert.That(settings.AllyPopups, Is.EqualTo(AllyPopups.Hidden));
        Assert.That(row.BindingText.text, Is.EqualTo("Hidden"));
        row.BindingButton.onClick.Invoke();
        Assert.That(settings.AllyPopups, Is.EqualTo(AllyPopups.Shown));

        settings.SetAllyPopups(AllyPopups.Hidden);
        Assert.That(row.BindingText.text, Is.EqualTo("Hidden"), "The row follows a change made elsewhere.");
        row.ResetButton.onClick.Invoke();
        Assert.That(settings.AllyPopups, Is.EqualTo(AllyPopups.Shown));

        settings.SetAllyPopups(AllyPopups.Hidden);
        view.CategoryResetButton.onClick.Invoke();
        row.BindingButton.onClick.Invoke();
        Assert.That(settings.AllyPopups, Is.EqualTo(AllyPopups.Hidden), "Nothing changes while a confirmation shows.");
    }

    /// <summary>
    /// Back closes the open screen, pops it off the menu stack and runs the close callback exactly once, even
    /// when pressed again.
    /// </summary>
    [Test]
    public void Back_ClosesTheScreen_TakesItOffTheMenuStack_AndRunsTheCallbackOnce()
    {
        controller.Open(() => closedCount++);
        Assert.That(menus.Top, Is.SameAs(controller));

        TickWith(back: true);
        Assert.That(view.IsShown, Is.False);
        Assert.That(menus.Count, Is.Zero);
        Assert.That(closedCount, Is.EqualTo(1));

        TickWith(back: true);
        Assert.That(closedCount, Is.EqualTo(1));
    }

    /// <summary>
    /// Clicking the on-screen back button closes the screen and runs the close callback.
    /// </summary>
    [Test]
    public void TheBackButton_ClosesTheScreen()
    {
        controller.Open(() => closedCount++);

        view.BackButton.onClick.Invoke();

        Assert.That(view.IsShown, Is.False);
        Assert.That(closedCount, Is.EqualTo(1));
    }

    /// <summary>
    /// Clicking a binding only starts listening on the next tick, so the click itself is never captured, and the
    /// next key pressed becomes the binding.
    /// </summary>
    [Test]
    public void SelectingABinding_ListensFromTheNextTick_AndAKeyBindsIt()
    {
        OpenAndSettle();

        controller.GetRow(StopRow).BindingButton.onClick.Invoke();
        Assert.That(controller.IsListening, Is.False, "The click that selected the binding is never captured.");

        controller.Tick();
        Assert.That(controller.IsListening, Is.True);
        Assert.That(view.IsListeningShown, Is.True);
        Assert.That(view.ListeningText.text, Does.Contain("Stop"));

        PressAndSettle(keyboard.fKey);

        Assert.That(controller.IsListening, Is.False);
        Assert.That(view.IsListeningShown, Is.False);
        Assert.That(controller.GetRow(StopRow).BindingText.text, Is.EqualTo("F"));
        Assert.That(controller.GetRow(StopRow).IsResetShown, Is.True, "The row now differs from its default.");
        Assert.That(view.NoticeText.text, Is.Empty);
    }

    /// <summary>
    /// Binding a key another action already uses swaps the two keys, and the notice names the action that moved
    /// and its new key.
    /// </summary>
    [Test]
    public void BindingATakenKey_NamesTheActionThatMoved()
    {
        OpenAndSettle();
        controller.GetRow(StopRow).BindingButton.onClick.Invoke();
        controller.Tick();

        PressAndSettle(keyboard.qKey);

        Assert.That(controller.GetRow(StopRow).BindingText.text, Is.EqualTo("Q"));
        Assert.That(controller.GetRow(Ability1Row).BindingText.text, Is.EqualTo("S"));
        Assert.That(view.NoticeText.text, Is.EqualTo("Class ability 1 moved to S."));
    }

    /// <summary>
    /// Escape while listening cancels the rebind and keeps the old binding without closing the screen; a later
    /// Back does close it.
    /// </summary>
    [Test]
    public void Escape_CancelsListening_WithoutAlsoClosingTheScreen()
    {
        OpenAndSettle();
        controller.GetRow(StopRow).BindingButton.onClick.Invoke();
        controller.Tick();

        PressAndSettle(keyboard.escapeKey);

        Assert.That(controller.IsListening, Is.False);
        Assert.That(view.IsShown, Is.True);
        Assert.That(controller.GetRow(StopRow).BindingText.text, Is.EqualTo("S"));

        TickWith(back: true);
        Assert.That(view.IsShown, Is.False, "A later Back closes it.");
    }

    /// <summary>
    /// Back is ignored both while a rebind is waiting to start and while it is listening.
    /// </summary>
    [Test]
    public void Back_WhileARebindIsPendingOrListening_DoesNothing()
    {
        OpenAndSettle();
        controller.GetRow(StopRow).BindingButton.onClick.Invoke();

        menus.Top.HandleBack();
        Assert.That(view.IsShown, Is.True, "Pending: listening starts on the next tick.");

        controller.Tick();
        menus.Top.HandleBack();
        Assert.That(controller.IsListening, Is.True);
        Assert.That(view.IsShown, Is.True);
    }

    /// <summary>
    /// Closing the screen while listening cancels the running rebind, hides the listening panel and runs the
    /// close callback.
    /// </summary>
    [Test]
    public void Closing_WhileListening_CancelsTheRebind()
    {
        OpenAndSettle();
        controller.GetRow(StopRow).BindingButton.onClick.Invoke();
        controller.Tick();

        controller.Close();

        Assert.That(controller.IsListening, Is.False);
        Assert.That(bindings.IsRebinding, Is.False);
        Assert.That(view.IsListeningShown, Is.False);
        Assert.That(closedCount, Is.EqualTo(1));
    }

    /// <summary>
    /// Rows follow bindings changed outside the screen, and a row's reset button restores that action's default
    /// key, moving the action that held it and naming it in the notice.
    /// </summary>
    [Test]
    public void ResettingARow_PutsItsDefaultBack_AndRowsFollowEveryChange()
    {
        OpenAndSettle();
        bindings.TryAssign(actions.FindAction("Ability1", true), "<Keyboard>/s", out _);
        Assert.That(controller.GetRow(StopRow).BindingText.text, Is.EqualTo("Q"));

        controller.GetRow(Ability1Row).ResetButton.onClick.Invoke();

        Assert.That(controller.GetRow(Ability1Row).BindingText.text, Is.EqualTo("Q"));
        Assert.That(controller.GetRow(StopRow).BindingText.text, Is.EqualTo("S"));
        Assert.That(view.NoticeText.text, Is.EqualTo("Stop moved to S."));
    }

    /// <summary>
    /// Reset controls asks first, naming the controls; Back dismisses only the question, and confirming restores every
    /// default, says so, and selects Back once nothing is left to reset.
    /// </summary>
    [Test]
    public void ResetControls_AsksFirst_AndBackOnlyClosesTheQuestion()
    {
        OpenAndSettle();
        bindings.TryAssign(actions.FindAction("Stop", true), "<Keyboard>/f", out _);

        view.CategoryResetButton.onClick.Invoke();
        Assert.That(view.IsConfirmShown, Is.True);
        Assert.That(view.ConfirmText.text, Is.EqualTo(OptionsMenuController.ResetQuestions[(int)OptionsMenuController.Category.Controls]));
        Assert.That(controller.GetRow(StopRow).BindingText.text, Is.EqualTo("F"));

        TickWith(back: true);
        Assert.That(view.IsConfirmShown, Is.False);
        Assert.That(view.IsShown, Is.True);
        Assert.That(events.currentSelectedGameObject, Is.SameAs(view.CategoryResetButton.gameObject));

        view.CategoryResetButton.onClick.Invoke();
        view.ConfirmYesButton.onClick.Invoke();

        Assert.That(view.IsConfirmShown, Is.False);
        Assert.That(controller.GetRow(StopRow).BindingText.text, Is.EqualTo("S"));
        Assert.That(view.NoticeText.text, Is.EqualTo(OptionsMenuController.ResetNotices[(int)OptionsMenuController.Category.Controls]));
        Assert.That(view.IsCategoryResetShown, Is.False);
        Assert.That(events.currentSelectedGameObject, Is.SameAs(view.BackButton.gameObject));
    }

    /// <summary>
    /// Answering no to the reset question closes it and keeps the custom bindings.
    /// </summary>
    [Test]
    public void TheConfirmationsCancel_KeepsTheBindings()
    {
        OpenAndSettle();
        bindings.TryAssign(actions.FindAction("Stop", true), "<Keyboard>/f", out _);

        view.CategoryResetButton.onClick.Invoke();
        view.ConfirmNoButton.onClick.Invoke();

        Assert.That(view.IsConfirmShown, Is.False);
        Assert.That(controller.GetRow(StopRow).BindingText.text, Is.EqualTo("F"));
    }

    /// <summary>
    /// Closing and reopening the screen clears the notice left from the previous visit.
    /// </summary>
    [Test]
    public void Reopening_ClearsTheLastNotice()
    {
        OpenAndSettle();
        bindings.TryAssign(actions.FindAction("Stop", true), "<Keyboard>/f", out _);
        view.CategoryResetButton.onClick.Invoke();
        view.ConfirmYesButton.onClick.Invoke();
        controller.Close();

        controller.Open(null);

        Assert.That(view.NoticeText.text, Is.Empty);
    }

    /// <summary>
    /// The warning (the vitals strip's "You're taking damage.") shows over the notice, which hides while it shows and
    /// comes back once it clears.
    /// </summary>
    [Test]
    public void TheWarning_ShowsOverTheNotice()
    {
        OpenAndSettle();
        view.SetNotice("Stop moved to S.");

        controller.SetWarning(VitalsStripPresenter.DamageWarning);
        Assert.That(view.WarningText.text, Is.EqualTo(VitalsStripPresenter.DamageWarning));
        Assert.That(view.NoticeText.enabled, Is.False);
        Assert.That(controller.Vitals, Is.SameAs(view.Vitals));

        controller.SetWarning(null);
        Assert.That(view.WarningText.text, Is.Empty);
        Assert.That(view.NoticeText.enabled, Is.True);
    }

    /// <summary>
    /// Opens the screen on the Controls with a close callback that counts calls, then ticks once so it is ready for
    /// input.
    /// </summary>
    private void OpenAndSettle()
    {
        controller.Open(() => closedCount++, OptionsMenuController.Opening.Controls);
        controller.Tick();
    }

    /// <summary>
    /// Selects <paramref name="control"/> and ticks the controller, as moving the selection onto it would.
    /// </summary>
    private void Select(Selectable control)
    {
        events.SetSelectedGameObject(control.gameObject);
        controller.Tick();
    }

    /// <summary>
    /// Ticks the menu stack and the controller once, with Back pressed for that frame only when
    /// <paramref name="back"/> is true.
    /// </summary>
    private void TickWith(bool back)
    {
        menuInput.BackPressedThisFrame = back;
        menus.Tick();
        controller.Tick();
        menuInput.BackPressedThisFrame = false;
    }

    /// <summary>
    /// Presses <paramref name="button"/>, advances input time by 0.2 seconds and runs an input update so a
    /// listening rebind completes, then releases it.
    /// </summary>
    private void PressAndSettle(ButtonControl button)
    {
        Press(button);
        currentTime += 0.2;
        InputSystem.Update();
        Release(button);
    }
}
