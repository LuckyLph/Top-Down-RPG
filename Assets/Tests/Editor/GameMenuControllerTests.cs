using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Tests for the in-game menu (<see cref="GameMenuController"/>) on the real canvas prefab: opening and closing,
/// input blocking, pausing solo play only, the options screen and the host's leave and quit confirmation.
/// </summary>
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
    private MenuStack menus;
    private OptionsMenuController options;
    private GameMenuController controller;

    /// <summary>
    /// Copies the project input actions, builds the input, binding and time services on in-memory settings,
    /// instantiates the menu canvas prefab and starts the options and game menu controllers with fake menu input.
    /// </summary>
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
        menus = new MenuStack(menuInput);
        options = new OptionsMenuController(optionsView, bindings, menus, new UISelection(null));
        options.Start();
        controller = new GameMenuController(menuView, options, menus, inputGate, timeFreeze, gameFlow, session, new UISelection(null));
        controller.Start();
    }

    /// <summary>
    /// Disposes the controllers and services, destroys the canvas and the copied actions, and resets the time scale.
    /// </summary>
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

    /// <summary>
    /// The canvas prefab holds both the menu and options views, both start hidden, and it sorts above the damage
    /// popups.
    /// </summary>
    [Test]
    public void ThePrefab_HasTheMenuAndTheOptionsScreenHidden()
    {
        Assert.That(menuView, Is.Not.Null);
        Assert.That(optionsView, Is.Not.Null);
        Assert.That(menuView.IsShown, Is.False);
        Assert.That(optionsView.IsShown, Is.False);
        Assert.That(canvas.GetComponent<Canvas>().sortingOrder, Is.GreaterThan(5000), "Above the damage popups.");
    }

    /// <summary>
    /// Offline, the menu key shows the menu, blocks gameplay input and pauses time.
    /// </summary>
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

    /// <summary>
    /// In an online session the open menu blocks gameplay input but leaves time running.
    /// </summary>
    [Test]
    public void Online_ItBlocksGameplayInput_ButPausesNothing()
    {
        session.IsActive = true;
        session.IsHost = true;

        TickWith(menu: true);

        Assert.That(controller.IsOpen, Is.True);
        Assert.That(inputGate.IsBlocked, Is.True);
        Assert.That(controller.IsPaused, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
    }

    /// <summary>
    /// The press that opens the menu does not also close it; a later Back closes it and restores input, time and
    /// the menu stack, and the menu key toggles it.
    /// </summary>
    [Test]
    public void BackOrTheMenuKey_ClosesIt_AndGivesEverythingBack()
    {
        TickWith(menu: true, back: true);
        Assert.That(controller.IsOpen, Is.True, "Escape is both keys; the press that opened it does not also close it.");
        Assert.That(menus.Top, Is.SameAs(controller));

        TickWith(back: true);
        Assert.That(controller.IsOpen, Is.False);
        Assert.That(menuView.IsShown, Is.False);
        Assert.That(inputGate.IsBlocked, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
        Assert.That(menus.Count, Is.Zero);

        TickWith(menu: true);
        TickWith(menu: true);
        Assert.That(controller.IsOpen, Is.False, "The menu key (gamepad Start) toggles it.");
    }

    /// <summary>
    /// The Resume button closes the menu and unpauses time.
    /// </summary>
    [Test]
    public void Resume_ClosesIt()
    {
        TickWith(menu: true);

        menuView.ResumeButton.onClick.Invoke();

        Assert.That(controller.IsOpen, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
    }

    /// <summary>
    /// The Options button hides the menu and pushes the options screen while staying paused; Back from the options
    /// returns to the menu without closing it, and a second Back closes the menu.
    /// </summary>
    [Test]
    public void Options_TakeTheMenusPlace_AndBackFromThemReturnsToTheMenuOnly()
    {
        TickWith(menu: true);
        TickWith();

        menuView.OptionsButton.onClick.Invoke();
        Assert.That(menuView.IsShown, Is.False);
        Assert.That(options.IsOpen, Is.True);
        Assert.That(menus.Top, Is.SameAs(options));
        Assert.That(Time.timeScale, Is.Zero, "Still paused while in the options.");

        TickWith(back: true);
        Assert.That(options.IsOpen, Is.False);
        Assert.That(menuView.IsShown, Is.True);
        Assert.That(controller.IsOpen, Is.True, "The Back that closed the options does not also close the menu.");
        Assert.That(menus.Top, Is.SameAs(controller));

        TickWith(back: true);
        Assert.That(controller.IsOpen, Is.False);
    }

    /// <summary>
    /// Closing the menu while the options are open closes both, unblocks input and empties the menu stack.
    /// </summary>
    [Test]
    public void Closing_WithTheOptionsOpen_ClosesThemToo()
    {
        TickWith(menu: true);
        menuView.OptionsButton.onClick.Invoke();

        controller.Close();

        Assert.That(options.IsOpen, Is.False);
        Assert.That(menuView.IsShown, Is.False);
        Assert.That(inputGate.IsBlocked, Is.False);
        Assert.That(menus.Count, Is.Zero);
    }

    /// <summary>
    /// An online host gets a confirmation question before leaving or quitting, and Back or No dismisses only the
    /// question, leaving the menu open.
    /// </summary>
    [Test]
    public void AHost_IsAskedBeforeLeavingOrQuitting_AndBackOnlyClosesTheQuestion()
    {
        session.IsActive = true;
        session.IsHost = true;
        TickWith(menu: true);

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

    /// <summary>
    /// Disposing the controller while open restores input and time and empties the menu stack, and afterwards the
    /// menu key no longer opens it.
    /// </summary>
    [Test]
    public void Dispose_WhileOpen_GivesBackGameplayInputAndTime()
    {
        TickWith(menu: true);

        controller.Dispose();

        Assert.That(inputGate.IsBlocked, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
        Assert.That(menus.Count, Is.Zero);

        TickWith(menu: true);
        Assert.That(controller.IsOpen, Is.False, "A disposed menu no longer answers the menu key.");
    }

    /// <summary>
    /// Simulates one frame: sets the menu and back presses, ticks the menu stack and the options controller, then
    /// releases both presses.
    /// </summary>
    private void TickWith(bool menu = false, bool back = false)
    {
        menuInput.MenuPressedThisFrame = menu;
        menuInput.BackPressedThisFrame = back;
        menus.Tick();
        options.Tick();
        menuInput.MenuPressedThisFrame = false;
        menuInput.BackPressedThisFrame = false;
    }
}
