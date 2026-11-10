using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Tests for <see cref="OptionsMenuController"/> on the real options menu prefab and project input actions: the
/// Character section's Look row over the project's looks and its colour rows over the project's colours, the binding
/// rows, Back and close handling, interactive
/// rebinding, per-row reset and reset all with confirmation.
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
    /// Starting builds the Character section's Look row, a part row for each of the adventurer's eight part slots its
    /// player picks (not the weapon's, which shows the equipped weapon's gear) and a colour row for each of the fifteen
    /// colour choices its sheets or parts carry, then the Display section's UI size row (Docs/DesignSystem.md, Phase 3)
    /// and its Reduce flashing and Reduce motion toggle rows (Phase 6), then nine binding rows under three section
    /// headers, labelled with each action and its current binding, and leaves the screen hidden.
    /// </summary>
    [Test]
    public void Start_BuildsARowPerActionUnderItsSection_AndStaysHidden()
    {
        Assert.That(view.IsShown, Is.False);
        Assert.That(controller.RowCount, Is.EqualTo(9));
        Assert.That(controller.PartRowCount, Is.EqualTo(8));
        Assert.That(controller.ColourRowCount, Is.EqualTo(15));
        Assert.That(view.RowsContainer.childCount, Is.EqualTo(41), "The Character header, its Look row, eight part rows and fifteen colour rows, the Display header, its UI size row and two toggle rows, three section headers and nine rows.");
        Assert.That(view.RowsContainer.GetChild(0).GetComponent<TMPro.TMP_Text>().text, Is.EqualTo(OptionsMenuController.CharacterSection));
        Assert.That(view.RowsContainer.GetChild(1).GetComponent<KeyBindingRowView>(), Is.SameAs(controller.LookRow));
        Assert.That(view.RowsContainer.GetChild(2).GetComponent<KeyBindingRowView>(), Is.SameAs(controller.GetPartRow(0)));
        Assert.That(view.RowsContainer.GetChild(10).GetComponent<KeyBindingRowView>(), Is.SameAs(controller.GetColourRow(0)));
        Assert.That(view.RowsContainer.GetChild(25).GetComponent<TMPro.TMP_Text>().text, Is.EqualTo(OptionsMenuController.DisplaySection));
        Assert.That(view.RowsContainer.GetChild(26).GetComponent<KeyBindingRowView>(), Is.SameAs(controller.UiSizeRow));
        Assert.That(view.RowsContainer.GetChild(27).GetComponent<KeyBindingRowView>(), Is.SameAs(controller.ReduceFlashingRow));
        Assert.That(view.RowsContainer.GetChild(28).GetComponent<KeyBindingRowView>(), Is.SameAs(controller.ReduceMotionRow));
        Assert.That(view.RowsContainer.GetChild(29).GetComponent<TMPro.TMP_Text>().text, Is.EqualTo("Movement"));
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
    /// The part rows, the adventurer's slots in order (Docs/PlayerCharacter.md, Phases 6 and 7), show the option the local
    /// settings pick, the first by default; a button keeps the slot's next option, after the last the first, kept as no
    /// pick; a reset goes back to the first; and a row follows a change made elsewhere, an unknown option showing the
    /// first.
    /// </summary>
    [Test]
    public void ThePartRows_ShowAndCycleTheSettingsParts()
    {
        string[] labels = new string[controller.PartRowCount];
        for (int i = 0; i < labels.Length; i++)
        {
            labels[i] = controller.GetPartRow(i).Label.text;
        }

        Assert.That(labels, Is.EqualTo(new[] { "Hair", "Headwear", "Top", "Cape", "Legs", "Boots", "Hands", "Bag" }));
        KeyBindingRowView hair = controller.GetPartRow(0);
        KeyBindingRowView legs = controller.GetPartRow(4);
        KeyBindingRowView bag = controller.GetPartRow(7);
        Assert.That(bag.BindingText.text, Is.EqualTo("None"));
        Assert.That(hair.BindingText.text, Is.EqualTo("Tousled"));

        controller.Open(null);
        hair.BindingButton.onClick.Invoke();
        Assert.That(settings.CharacterPart("Hair"), Is.EqualTo("Crop"));
        Assert.That(hair.BindingText.text, Is.EqualTo("Crop"));
        legs.BindingButton.onClick.Invoke();
        Assert.That(settings.CharacterPart("Legs"), Is.EqualTo("Shorts"));
        legs.BindingButton.onClick.Invoke();
        Assert.That(settings.CharacterPart("Legs"), Is.Empty, "After the last option, the first.");
        Assert.That(legs.BindingText.text, Is.EqualTo("Trousers"));
        bag.BindingButton.onClick.Invoke();
        Assert.That(settings.CharacterPart("Bag"), Is.EqualTo("Satchel"));

        hair.ResetButton.onClick.Invoke();
        Assert.That(settings.CharacterPart("Hair"), Is.Empty);
        settings.SetCharacterPart("Hair", "Spiky");
        Assert.That(hair.BindingText.text, Is.EqualTo("Spiky"));
        settings.SetCharacterPart("Hair", "Mohawk");
        Assert.That(hair.BindingText.text, Is.EqualTo("Tousled"));
    }

    /// <summary>
    /// The colour rows, the choices the adventurer's sheets carry in the colours' order, show the swatch the local
    /// settings pick, each choice's own by default; a button keeps the next swatch of its choice's list in the settings,
    /// after the last the first, the choice's own kept as no pick; a reset goes back to the choice's own; and a row
    /// follows a change made elsewhere, an unknown swatch showing the choice's own.
    /// </summary>
    [Test]
    public void TheColourRows_ShowAndCycleTheSettingsSwatches()
    {
        string[] labels = new string[controller.ColourRowCount];
        for (int i = 0; i < labels.Length; i++)
        {
            labels[i] = controller.GetColourRow(i).Label.text;
        }

        Assert.That(labels, Is.EqualTo(new[] { "Skin", "Hair", "Eyes", "Tunic", "Shirt", "Trousers", "Boots", "Belt", "Buckle", "Gloves", "Strap", "Pouch", "Headwear", "Cape", "Lining" }));
        KeyBindingRowView tunic = controller.GetColourRow(3);
        KeyBindingRowView shirt = controller.GetColourRow(4);
        Assert.That(tunic.BindingText.text, Is.EqualTo("Blue"));
        Assert.That(shirt.BindingText.text, Is.EqualTo("Beige"));

        controller.Open(null);
        tunic.BindingButton.onClick.Invoke();
        Assert.That(settings.CharacterColour("Tunic"), Is.EqualTo("Red"));
        Assert.That(tunic.BindingText.text, Is.EqualTo("Red"));
        shirt.BindingButton.onClick.Invoke();
        Assert.That(settings.CharacterColour("Shirt"), Is.EqualTo("Linen"), "The cloth after the shirt's own beige.");

        CharacterColours colours = view.Colours;
        int cloths = colours.Swatches(colours.IndexOf("Tunic")).Count;
        for (int i = 1; i < cloths; i++)
        {
            tunic.BindingButton.onClick.Invoke();
        }

        Assert.That(settings.CharacterColour("Tunic"), Is.Empty, "After the last cloth, the first, the tunic's own.");
        Assert.That(tunic.BindingText.text, Is.EqualTo("Blue"));

        shirt.ResetButton.onClick.Invoke();
        Assert.That(settings.CharacterColour("Shirt"), Is.Empty);
        Assert.That(shirt.BindingText.text, Is.EqualTo("Beige"));

        settings.SetCharacterColour("Tunic", "Teal");
        Assert.That(tunic.BindingText.text, Is.EqualTo("Teal"));
        settings.SetCharacterColour("Tunic", "Plaid");
        Assert.That(tunic.BindingText.text, Is.EqualTo("Blue"));
    }

    /// <summary>
    /// The Look row shows the look the local settings name (the first, the adventurer, by default); its button keeps the
    /// project's next look in the settings, after the last the first again, and its reset the first; the row follows a
    /// change made elsewhere.
    /// </summary>
    [Test]
    public void TheLookRow_ShowsAndCyclesTheSettingsLook()
    {
        KeyBindingRowView row = controller.LookRow;
        Assert.That(row.Label.text, Is.EqualTo(OptionsMenuController.LookLabel));
        Assert.That(row.BindingText.text, Is.EqualTo("Adventurer"));

        controller.Open(null);
        row.BindingButton.onClick.Invoke();
        Assert.That(settings.CharacterLook, Is.EqualTo("Bandit"));
        Assert.That(row.BindingText.text, Is.EqualTo("Bandit"));
        row.BindingButton.onClick.Invoke();
        Assert.That(settings.CharacterLook, Is.EqualTo("Adventurer"), "After the last look, the first.");

        settings.SetCharacterLook("Bandit");
        Assert.That(row.BindingText.text, Is.EqualTo("Bandit"));
        row.ResetButton.onClick.Invoke();
        Assert.That(settings.CharacterLook, Is.EqualTo("Adventurer"));
        Assert.That(row.BindingText.text, Is.EqualTo("Adventurer"));
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
