using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

public class GameMenuControllerTests
{
    private const string ProjectActionsPath = "Assets/InputSystem_Actions.inputactions";
    private const string RebindableActionsPath = "Assets/Data/Input/RebindableActions.asset";
    private const string GameMenuCanvasPath = "Assets/Prefabs/UI/GameMenuCanvas.prefab";

    private InputActionAsset actions;
    private PlayerInputService playerInput;
    private GameplayInputGate inputGate;
    private TimeFreeze timeFreeze;
    private InputBindings bindings;
    private GameObject canvas;
    private GameMenuView menuView;
    private OptionsMenuView optionsView;
    private FakeMenuInput menuInput;
    private FakeSessionStatus session;
    private OptionsMenuController options;
    private GameMenuController controller;

    [SetUp]
    public void SetUp()
    {
        actions = InputActionAsset.FromJson(AssetDatabase.LoadAssetAtPath<InputActionAsset>(ProjectActionsPath).ToJson());
        playerInput = new PlayerInputService(actions, null);
        GameFlow gameFlow = new(null, null, null, null);
        inputGate = new GameplayInputGate(gameFlow, playerInput);
        timeFreeze = new TimeFreeze();
        bindings = new InputBindings(actions, AssetDatabase.LoadAssetAtPath<RebindableActionList>(RebindableActionsPath), new UserSettings(new MemorySaveStore()));
        bindings.Initialize();
        canvas = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(GameMenuCanvasPath));
        menuView = canvas.GetComponentInChildren<GameMenuView>(true);
        optionsView = canvas.GetComponentInChildren<OptionsMenuView>(true);
        menuInput = new FakeMenuInput();
        session = new FakeSessionStatus();
        options = new OptionsMenuController(optionsView, bindings, menuInput);
        options.Start();
        controller = new GameMenuController(menuView, options, menuInput, inputGate, timeFreeze, gameFlow, session);
        controller.Start();
    }

    [TearDown]
    public void TearDown()
    {
        controller.Dispose();
        options.Dispose();
        bindings.Dispose();
        playerInput.Dispose();
        timeFreeze.Dispose();
        Object.DestroyImmediate(canvas);
        Object.DestroyImmediate(actions);
        Time.timeScale = 1f;
    }

    [Test]
    public void ThePrefab_HasTheMenuAndTheOptionsScreenHidden()
    {
        Assert.That(menuView, Is.Not.Null);
        Assert.That(optionsView, Is.Not.Null);
        Assert.That(menuView.IsShown, Is.False);
        Assert.That(optionsView.IsShown, Is.False);
        Assert.That(canvas.GetComponent<Canvas>().sortingOrder, Is.GreaterThan(5000), "Above the damage popups.");
    }

    [Test]
    public void TheMenuKey_OpensIt_BlocksGameplayInput_AndPausesSoloPlay()
    {
        TickWith(menu: true);

        Assert.That(controller.IsOpen, Is.True);
        Assert.That(menuView.IsShown, Is.True);
        Assert.That(inputGate.IsBlocked, Is.True);
        Assert.That(controller.IsPaused, Is.True);
        Assert.That(Time.timeScale, Is.Zero);
    }

    [Test]
    public void Online_ItBlocksGameplayInput_ButPausesNothing()
    {
        session.IsOnline = true;
        session.IsHost = true;

        TickWith(menu: true);

        Assert.That(controller.IsOpen, Is.True);
        Assert.That(inputGate.IsBlocked, Is.True);
        Assert.That(controller.IsPaused, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
    }

    [Test]
    public void BackOrTheMenuKey_ClosesItOnALaterTick_AndGivesEverythingBack()
    {
        TickWith(menu: true, back: true);
        Assert.That(controller.IsOpen, Is.True, "Escape is both keys; the press that opened it does not also close it.");

        TickWith();
        TickWith(back: true);
        Assert.That(controller.IsOpen, Is.False);
        Assert.That(menuView.IsShown, Is.False);
        Assert.That(inputGate.IsBlocked, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(1f));

        TickWith(menu: true);
        TickWith();
        TickWith(menu: true);
        Assert.That(controller.IsOpen, Is.False, "The menu key (gamepad Start) toggles it.");
    }

    [Test]
    public void Resume_ClosesIt()
    {
        TickWith(menu: true);

        menuView.ResumeButton.onClick.Invoke();

        Assert.That(controller.IsOpen, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
    }

    [Test]
    public void Options_TakeTheMenusPlace_AndBackFromThemReturnsToTheMenuOnly()
    {
        TickWith(menu: true);
        TickWith();

        menuView.OptionsButton.onClick.Invoke();
        Assert.That(menuView.IsShown, Is.False);
        Assert.That(options.IsOpen, Is.True);
        Assert.That(Time.timeScale, Is.Zero, "Still paused while in the options.");

        TickWith();
        TickWith(back: true);
        Assert.That(options.IsOpen, Is.False);
        Assert.That(menuView.IsShown, Is.True);
        Assert.That(controller.IsOpen, Is.True, "The Back that closed the options does not also close the menu.");

        TickWith(back: true);
        Assert.That(controller.IsOpen, Is.False);
    }

    [Test]
    public void Closing_WithTheOptionsOpen_ClosesThemToo()
    {
        TickWith(menu: true);
        menuView.OptionsButton.onClick.Invoke();

        controller.Close();

        Assert.That(options.IsOpen, Is.False);
        Assert.That(menuView.IsShown, Is.False);
        Assert.That(inputGate.IsBlocked, Is.False);
    }

    [Test]
    public void AHost_IsAskedBeforeLeavingOrQuitting_AndBackOnlyClosesTheQuestion()
    {
        session.IsOnline = true;
        session.IsHost = true;
        TickWith(menu: true);
        TickWith();

        menuView.LeaveButton.onClick.Invoke();
        Assert.That(menuView.IsConfirmShown, Is.True);
        Assert.That(menuView.ConfirmText.text, Is.EqualTo(GameMenuController.LeaveQuestion));
        Assert.That(controller.IsLeaving, Is.False);

        TickWith(back: true);
        Assert.That(menuView.IsConfirmShown, Is.False);
        Assert.That(controller.IsOpen, Is.True);

        menuView.QuitButton.onClick.Invoke();
        Assert.That(menuView.ConfirmText.text, Is.EqualTo(GameMenuController.QuitQuestion));

        menuView.ConfirmNoButton.onClick.Invoke();
        Assert.That(menuView.IsConfirmShown, Is.False);
        Assert.That(controller.IsOpen, Is.True);
    }

    [Test]
    public void Dispose_WhileOpen_GivesBackGameplayInputAndTime()
    {
        TickWith(menu: true);

        controller.Dispose();

        Assert.That(inputGate.IsBlocked, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
    }

    private void TickWith(bool menu = false, bool back = false)
    {
        menuInput.MenuPressedThisFrame = menu;
        menuInput.BackPressedThisFrame = back;
        options.Tick();
        controller.Tick();
        menuInput.MenuPressedThisFrame = false;
        menuInput.BackPressedThisFrame = false;
    }
}
