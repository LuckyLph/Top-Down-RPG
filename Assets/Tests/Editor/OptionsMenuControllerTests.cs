using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Tests for <see cref="OptionsMenuController"/> on the real options menu prefab and project input actions: the Display
/// section's rows, the binding rows, Back and close handling, interactive rebinding, per-row reset and reset all with
/// confirmation.
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
    private Keyboard keyboard;
    private int closedCount;

    /// <summary>
    /// Adds a test keyboard, binds a copy of the project input actions with the project's rebindable list and
    /// in-memory settings, instantiates the options menu prefab and starts the controller on a fresh menu stack.
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
        controller = new OptionsMenuController(view, bindings, menus, new UISelection(null), settings, uiScale);
        controller.Start();
        closedCount = 0;
    }

    /// <summary>
    /// Disposes the controller and bindings, then destroys the menu instance and the copied input actions.
    /// </summary>
    public override void TearDown()
    {
        controller.Dispose();
        bindings.Dispose();
        Object.DestroyImmediate(view.gameObject);
        Object.DestroyImmediate(actions);
        Object.DestroyImmediate(scaleSettings);
        base.TearDown();
    }

    /// <summary>
    /// Starting builds the Display section's UI size row (Docs/DesignSystem.md, Phase 3) and its Reduce flashing and
    /// Reduce motion toggle rows (Phase 6), the allies' damage numbers row (Docs/UiRevamp.md, Phase 3) and the Chat
    /// timestamps and Chat window rows (Phase 12), then ten binding rows under four section headers, labelled with each
    /// action and its current binding, the chat key last, and leaves the screen hidden. The look, parts and colours are
    /// the Character screen's (Phase 7).
    /// </summary>
    [Test]
    public void Start_BuildsARowPerActionUnderItsSection_AndStaysHidden()
    {
        Assert.That(view.IsShown, Is.False);
        Assert.That(controller.RowCount, Is.EqualTo(10));
        Assert.That(view.RowsContainer.childCount, Is.EqualTo(21), "The Display header, its UI size row, two toggle rows, the allies' damage numbers row and the two chat rows, four section headers and ten rows.");
        Assert.That(view.RowsContainer.GetChild(0).GetComponent<TMPro.TMP_Text>().text, Is.EqualTo(OptionsMenuController.DisplaySection));
        Assert.That(view.RowsContainer.GetChild(1).GetComponent<KeyBindingRowView>(), Is.SameAs(controller.UiSizeRow));
        Assert.That(view.RowsContainer.GetChild(2).GetComponent<KeyBindingRowView>(), Is.SameAs(controller.ReduceFlashingRow));
        Assert.That(view.RowsContainer.GetChild(3).GetComponent<KeyBindingRowView>(), Is.SameAs(controller.ReduceMotionRow));
        Assert.That(view.RowsContainer.GetChild(4).GetComponent<KeyBindingRowView>(), Is.SameAs(controller.AllyPopupsRow));
        Assert.That(view.RowsContainer.GetChild(5).GetComponent<KeyBindingRowView>(), Is.SameAs(controller.ChatTimestampsRow));
        Assert.That(view.RowsContainer.GetChild(6).GetComponent<KeyBindingRowView>(), Is.SameAs(controller.ChatWindowRow));
        Assert.That(view.RowsContainer.GetChild(7).GetComponent<TMPro.TMP_Text>().text, Is.EqualTo("Movement"));
        Assert.That(view.RowsContainer.GetChild(19).GetComponent<TMPro.TMP_Text>().text, Is.EqualTo("Other"));
        Assert.That(controller.GetRow(9).Label.text, Is.EqualTo("Chat"));
        Assert.That(controller.GetRow(9).BindingText.text, Is.EqualTo("Enter"));
        Assert.That(controller.GetRow(0).Label.text, Is.EqualTo("Move / attack"));
        Assert.That(controller.GetRow(0).BindingText.text, Is.EqualTo("Right mouse button"));
        Assert.That(controller.GetRow(AttackMoveRow).Label.text, Is.EqualTo("Attack move"));
        Assert.That(controller.GetRow(AttackMoveRow).BindingText.text, Is.EqualTo("A"));
        Assert.That(controller.GetRow(StopRow).BindingText.text, Is.EqualTo("S"));
        Assert.That(controller.GetRow(8).Label.text, Is.EqualTo("Weapon ability 2"));
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

        view.ResetAllButton.onClick.Invoke();
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

        controller.Open(null);
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
        view.ResetAllButton.onClick.Invoke();
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

        view.ResetAllButton.onClick.Invoke();
        row.BindingButton.onClick.Invoke();
        Assert.That(settings.AllyPopups, Is.EqualTo(AllyPopups.Shown), "Nothing changes while a confirmation shows.");
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
    /// Reset all asks for confirmation first; Back dismisses only the question, and confirming restores every
    /// default and shows the reset-all notice.
    /// </summary>
    [Test]
    public void ResetAll_AsksFirst_AndBackOnlyClosesTheQuestion()
    {
        OpenAndSettle();
        bindings.TryAssign(actions.FindAction("Stop", true), "<Keyboard>/f", out _);

        view.ResetAllButton.onClick.Invoke();
        Assert.That(view.IsConfirmShown, Is.True);
        Assert.That(controller.GetRow(StopRow).BindingText.text, Is.EqualTo("F"));

        TickWith(back: true);
        Assert.That(view.IsConfirmShown, Is.False);
        Assert.That(view.IsShown, Is.True);

        view.ResetAllButton.onClick.Invoke();
        view.ConfirmYesButton.onClick.Invoke();

        Assert.That(view.IsConfirmShown, Is.False);
        Assert.That(controller.GetRow(StopRow).BindingText.text, Is.EqualTo("S"));
        Assert.That(view.NoticeText.text, Is.EqualTo(OptionsMenuController.ResetAllNotice));
    }

    /// <summary>
    /// Answering no to the reset-all question closes it and keeps the custom bindings.
    /// </summary>
    [Test]
    public void TheConfirmationsCancel_KeepsTheBindings()
    {
        OpenAndSettle();
        bindings.TryAssign(actions.FindAction("Stop", true), "<Keyboard>/f", out _);

        view.ResetAllButton.onClick.Invoke();
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
        view.ResetAllButton.onClick.Invoke();
        view.ConfirmYesButton.onClick.Invoke();
        controller.Close();

        controller.Open(null);

        Assert.That(view.NoticeText.text, Is.Empty);
    }

    /// <summary>
    /// Opens the screen with a close callback that counts calls, then ticks once so it is ready for input.
    /// </summary>
    private void OpenAndSettle()
    {
        controller.Open(() => closedCount++);
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
