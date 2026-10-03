using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public class OptionsMenuControllerTests : InputTestFixture
{
    private const string ProjectActionsPath = "Assets/InputSystem_Actions.inputactions";
    private const string RebindableActionsPath = "Assets/Data/Input/RebindableActions.asset";
    private const string OptionsMenuPath = "Assets/Prefabs/UI/OptionsMenu.prefab";
    private const int StopRow = 1;
    private const int Ability1Row = 2;

    private InputActionAsset actions;
    private InputBindings bindings;
    private OptionsMenuView view;
    private FakeMenuInput menuInput;
    private OptionsMenuController controller;
    private Keyboard keyboard;
    private int closedCount;

    public override void Setup()
    {
        base.Setup();
        keyboard = InputSystem.AddDevice<Keyboard>();
        actions = InputActionAsset.FromJson(AssetDatabase.LoadAssetAtPath<InputActionAsset>(ProjectActionsPath).ToJson());
        bindings = new InputBindings(actions, AssetDatabase.LoadAssetAtPath<RebindableActionList>(RebindableActionsPath), new UserSettings(new MemorySaveStore()));
        bindings.Initialize();
        view = Object.Instantiate(AssetDatabase.LoadAssetAtPath<OptionsMenuView>(OptionsMenuPath));
        menuInput = new FakeMenuInput();
        controller = new OptionsMenuController(view, bindings, menuInput);
        controller.Start();
        closedCount = 0;
    }

    public override void TearDown()
    {
        controller.Dispose();
        bindings.Dispose();
        Object.DestroyImmediate(view.gameObject);
        Object.DestroyImmediate(actions);
        base.TearDown();
    }

    [Test]
    public void Start_BuildsARowPerActionUnderItsSection_AndStaysHidden()
    {
        Assert.That(view.IsShown, Is.False);
        Assert.That(controller.RowCount, Is.EqualTo(8));
        Assert.That(view.RowsContainer.childCount, Is.EqualTo(11), "Three section headers and eight rows.");
        Assert.That(view.RowsContainer.GetChild(0).GetComponent<TMPro.TMP_Text>().text, Is.EqualTo("Movement"));
        Assert.That(controller.GetRow(0).Label.text, Is.EqualTo("Move / attack"));
        Assert.That(controller.GetRow(0).BindingText.text, Is.EqualTo("Right mouse button"));
        Assert.That(controller.GetRow(StopRow).BindingText.text, Is.EqualTo("X"));
        Assert.That(controller.GetRow(7).Label.text, Is.EqualTo("Weapon ability 2"));
    }

    [Test]
    public void Back_ClosesTheScreenOnALaterTick_AndRunsTheCallbackOnce()
    {
        controller.Open(() => closedCount++);
        menuInput.BackPressedThisFrame = true;

        controller.Tick();
        Assert.That(view.IsShown, Is.True, "The press that opened the screen is not also Back.");

        controller.Tick();
        Assert.That(view.IsShown, Is.False);
        Assert.That(closedCount, Is.EqualTo(1));

        controller.Tick();
        Assert.That(closedCount, Is.EqualTo(1));
    }

    [Test]
    public void TheBackButton_ClosesTheScreen()
    {
        controller.Open(() => closedCount++);

        view.BackButton.onClick.Invoke();

        Assert.That(view.IsShown, Is.False);
        Assert.That(closedCount, Is.EqualTo(1));
    }

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

    [Test]
    public void BindingATakenKey_NamesTheActionThatMoved()
    {
        OpenAndSettle();
        controller.GetRow(StopRow).BindingButton.onClick.Invoke();
        controller.Tick();

        PressAndSettle(keyboard.qKey);

        Assert.That(controller.GetRow(StopRow).BindingText.text, Is.EqualTo("Q"));
        Assert.That(controller.GetRow(Ability1Row).BindingText.text, Is.EqualTo("X"));
        Assert.That(view.NoticeText.text, Is.EqualTo("Class ability 1 moved to X."));
    }

    [Test]
    public void Escape_CancelsListening_WithoutAlsoClosingTheScreen()
    {
        OpenAndSettle();
        controller.GetRow(StopRow).BindingButton.onClick.Invoke();
        controller.Tick();

        PressAndSettle(keyboard.escapeKey);
        menuInput.BackPressedThisFrame = true;
        controller.Tick();

        Assert.That(controller.IsListening, Is.False);
        Assert.That(view.IsShown, Is.True);
        Assert.That(controller.GetRow(StopRow).BindingText.text, Is.EqualTo("X"));

        controller.Tick();
        Assert.That(view.IsShown, Is.False, "A later Back closes it.");
    }

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

    [Test]
    public void ResettingARow_PutsItsDefaultBack_AndRowsFollowEveryChange()
    {
        OpenAndSettle();
        bindings.TryAssign(actions.FindAction("Ability1", true), "<Keyboard>/x", out _);
        Assert.That(controller.GetRow(StopRow).BindingText.text, Is.EqualTo("Q"));

        controller.GetRow(Ability1Row).ResetButton.onClick.Invoke();

        Assert.That(controller.GetRow(Ability1Row).BindingText.text, Is.EqualTo("Q"));
        Assert.That(controller.GetRow(StopRow).BindingText.text, Is.EqualTo("X"));
        Assert.That(view.NoticeText.text, Is.EqualTo("Stop moved to X."));
    }

    [Test]
    public void ResetAll_AsksFirst_AndBackOnlyClosesTheQuestion()
    {
        OpenAndSettle();
        bindings.TryAssign(actions.FindAction("Stop", true), "<Keyboard>/f", out _);

        view.ResetAllButton.onClick.Invoke();
        Assert.That(view.IsConfirmShown, Is.True);
        Assert.That(controller.GetRow(StopRow).BindingText.text, Is.EqualTo("F"));

        menuInput.BackPressedThisFrame = true;
        controller.Tick();
        Assert.That(view.IsConfirmShown, Is.False);
        Assert.That(view.IsShown, Is.True);
        menuInput.BackPressedThisFrame = false;

        view.ResetAllButton.onClick.Invoke();
        view.ConfirmYesButton.onClick.Invoke();

        Assert.That(view.IsConfirmShown, Is.False);
        Assert.That(controller.GetRow(StopRow).BindingText.text, Is.EqualTo("X"));
        Assert.That(view.NoticeText.text, Is.EqualTo(OptionsMenuController.ResetAllNotice));
    }

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

    private void OpenAndSettle()
    {
        controller.Open(() => closedCount++);
        controller.Tick();
    }

    private void PressAndSettle(ButtonControl button)
    {
        Press(button);
        currentTime += 0.2;
        InputSystem.Update();
        Release(button);
    }
}
