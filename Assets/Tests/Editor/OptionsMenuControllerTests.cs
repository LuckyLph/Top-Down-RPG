using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Tests for <see cref="OptionsMenuController"/> on the real options menu prefab and project input actions: the
/// Character section's Look row over the project's looks, the binding rows, Back and close handling, interactive
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
        controller = new OptionsMenuController(view, bindings, menus, new UISelection(null), settings);
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
        base.TearDown();
    }

    /// <summary>
    /// Starting builds the Character section's Look row, then nine binding rows under three section headers, labelled
    /// with each action and its current binding, and leaves the screen hidden.
    /// </summary>
    [Test]
    public void Start_BuildsARowPerActionUnderItsSection_AndStaysHidden()
    {
        Assert.That(view.IsShown, Is.False);
        Assert.That(controller.RowCount, Is.EqualTo(9));
        Assert.That(view.RowsContainer.childCount, Is.EqualTo(14), "The Character header and its Look row, three section headers and nine rows.");
        Assert.That(view.RowsContainer.GetChild(0).GetComponent<TMPro.TMP_Text>().text, Is.EqualTo(OptionsMenuController.CharacterSection));
        Assert.That(view.RowsContainer.GetChild(1).GetComponent<KeyBindingRowView>(), Is.SameAs(controller.LookRow));
        Assert.That(view.RowsContainer.GetChild(2).GetComponent<TMPro.TMP_Text>().text, Is.EqualTo("Movement"));
        Assert.That(controller.GetRow(0).Label.text, Is.EqualTo("Move / attack"));
        Assert.That(controller.GetRow(0).BindingText.text, Is.EqualTo("Right mouse button"));
        Assert.That(controller.GetRow(AttackMoveRow).Label.text, Is.EqualTo("Attack move"));
        Assert.That(controller.GetRow(AttackMoveRow).BindingText.text, Is.EqualTo("A"));
        Assert.That(controller.GetRow(StopRow).BindingText.text, Is.EqualTo("S"));
        Assert.That(controller.GetRow(8).Label.text, Is.EqualTo("Weapon ability 2"));
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
