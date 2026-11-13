using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Tests for the in-game menu (<see cref="GameMenuController"/>, Docs/UiRevamp.md, Phase 6) on the real canvas prefab:
/// opening and closing, input blocking, pausing and dimming solo play only, the title and buttons per session kind and
/// their loop, the Party and Controls pages in the menu's place, the options screen, the Save game button, and the
/// confirmations, asked only when leaving loses something.
/// </summary>
public class GameMenuControllerTests
{
    private const string ProjectActionsPath = "Assets/InputSystem_Actions.inputactions";
    private const string RebindableActionsPath = "Assets/Data/Input/RebindableActions.asset";
    private const string GameMenuCanvasPath = "Assets/Prefabs/UI/GameMenuCanvas.prefab";

    private readonly List<Object> created = new();
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
    private CharacterScreenView characterView;
    private FakeCharacterPreview preview;
    private CharacterScreenController character;
    private UiScaleSettings scaleSettings;
    private PlayerRegistry players;
    private LocalPlayerTracker localPlayer;
    private ManualWallClock wallClock;
    private HudSettings hud;
    private FakeGameSaving saving;
    private FakeGameExit exit;
    private PartyPagePresenter party;
    private ControlsPagePresenter controls;
    private GameMenuController controller;

    /// <summary>
    /// Copies the project input actions, builds the input, binding and time services on in-memory settings,
    /// instantiates the menu canvas prefab and starts the options, page and game menu controllers with fake menu input,
    /// saving and exit.
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
        UserSettings settings = new(new MemorySaveStore());
        scaleSettings = UiScaleSettings.Create();
        options = new OptionsMenuController(optionsView, bindings, menus, new UISelection(null), settings, new UiScale(scaleSettings, settings));
        options.Start();
        characterView = canvas.GetComponentInChildren<CharacterScreenView>(true);
        preview = new FakeCharacterPreview();
        character = new CharacterScreenController(characterView, preview, menus, new UISelection(null), settings);
        character.Start();
        players = new PlayerRegistry();
        localPlayer = new LocalPlayerTracker();
        wallClock = new ManualWallClock();
        hud = HudSettings.Create(messageSeconds: 1.2f);
        saving = new FakeGameSaving();
        exit = new FakeGameExit();
        party = new PartyPagePresenter(menuView.PartyPage, players, localPlayer, new PlayerWorlds(players), new ManualClock(), session, new HostAddresses(new FakeNetworkInterfaces(), 7777), new FakeClipboard(), wallClock, hud);
        party.Start();
        controls = new ControlsPagePresenter(menuView.ControlsPage, bindings);
        controls.Start();
        controller = new GameMenuController(menuView, options, character, party, menus, inputGate, timeFreeze, gameFlow, session, new UISelection(null), saving, exit, players, localPlayer, wallClock, hud);
        controller.Start();
    }

    /// <summary>
    /// Disposes the controllers and services, destroys the canvas, the players and the copied actions, and resets the
    /// time scale.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        controller.Dispose();
        controls.Dispose();
        party.Dispose();
        character.Dispose();
        options.Dispose();
        bindings.Dispose();
        playerInput.Dispose();
        timeFreeze.Dispose();
        Object.DestroyImmediate(canvas);
        Object.DestroyImmediate(actions);
        Object.DestroyImmediate(scaleSettings);
        Object.DestroyImmediate(hud);
        foreach (Object instance in created)
        {
            if (instance != null)
            {
                Object.DestroyImmediate(instance);
            }
        }

        created.Clear();
        Time.timeScale = 1f;
    }

    /// <summary>
    /// The canvas prefab holds the menu, its two pages and the options screen, all hidden, and it sorts above the damage
    /// popups.
    /// </summary>
    [Test]
    public void ThePrefab_HasTheMenuItsPagesAndTheOptionsScreenHidden()
    {
        Assert.That(menuView, Is.Not.Null);
        Assert.That(menuView.PartyPage, Is.Not.Null);
        Assert.That(menuView.ControlsPage, Is.Not.Null);
        Assert.That(optionsView, Is.Not.Null);
        Assert.That(menuView.IsShown, Is.False);
        Assert.That(optionsView.IsShown, Is.False);
        Assert.That(menuView.PartyPage.gameObject.activeSelf, Is.False);
        Assert.That(menuView.ControlsPage.gameObject.activeSelf, Is.False);
        Assert.That(canvas.GetComponent<Canvas>().sortingOrder, Is.GreaterThan(5000), "Above the damage popups.");
    }

    /// <summary>
    /// Offline, the menu key shows the menu titled "Paused" over the scrim, blocks gameplay input and pauses time; there
    /// is no Party button, and Save game can be pressed.
    /// </summary>
    [Test]
    public void Offline_ItIsTitledPaused_DimsAndPausesTheGame_AndOffersSaveButNoParty()
    {
        TickWith(menu: true);

        Assert.That(controller.IsOpen, Is.True);
        Assert.That(menuView.IsShown, Is.True);
        Assert.That(menuView.Title.text, Is.EqualTo(GameMenuController.PausedTitle));
        Assert.That(menuView.IsScrimShown, Is.True, "The scrim marks a paused game.");
        Assert.That(inputGate.IsBlocked, Is.True);
        Assert.That(controller.IsPaused, Is.True);
        Assert.That(Time.timeScale, Is.Zero);
        Assert.That(menuView.PartyButton.gameObject.activeSelf, Is.False, "Nobody to list offline.");
        Assert.That(menuView.SaveButton.gameObject.activeSelf, Is.True);
        Assert.That(menuView.SaveButton.interactable, Is.True);
        Assert.That(menuView.SaveText.text, Is.EqualTo(GameMenuController.SaveLabel));
    }

    /// <summary>
    /// Hosting, the menu is titled "Menu" with no scrim, blocks gameplay input but leaves time running, and shows both
    /// the Party and the Save game buttons.
    /// </summary>
    [Test]
    public void Hosting_ItIsTitledMenu_NeitherDimsNorPauses_AndShowsPartyAndSave()
    {
        session.IsActive = true;
        session.IsHost = true;

        TickWith(menu: true);

        Assert.That(controller.IsOpen, Is.True);
        Assert.That(menuView.Title.text, Is.EqualTo(GameMenuController.MenuTitle));
        Assert.That(menuView.IsScrimShown, Is.False, "Online the game stays in view.");
        Assert.That(inputGate.IsBlocked, Is.True);
        Assert.That(controller.IsPaused, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
        Assert.That(menuView.PartyButton.gameObject.activeSelf, Is.True);
        Assert.That(menuView.SaveButton.gameObject.activeSelf, Is.True);
    }

    /// <summary>
    /// A joined client's menu shows the Party button but no Save game button: the host saves the game.
    /// </summary>
    [Test]
    public void Joined_ItShowsPartyButNoSaveButton()
    {
        session.IsActive = true;

        TickWith(menu: true);

        Assert.That(menuView.Title.text, Is.EqualTo(GameMenuController.MenuTitle));
        Assert.That(menuView.PartyButton.gameObject.activeSelf, Is.True);
        Assert.That(menuView.SaveButton.gameObject.activeSelf, Is.False);
    }

    /// <summary>
    /// The shown buttons loop: up from Resume reaches Quit to desktop and down from it Resume, the hidden buttons
    /// skipped.
    /// </summary>
    [Test]
    public void TheButtons_Loop_SkippingTheHiddenOnes()
    {
        TickWith(menu: true);

        Assert.That(menuView.ResumeButton.navigation.mode, Is.EqualTo(Navigation.Mode.Explicit));
        Assert.That(menuView.ResumeButton.navigation.selectOnUp, Is.EqualTo(menuView.QuitButton));
        Assert.That(menuView.QuitButton.navigation.selectOnDown, Is.EqualTo(menuView.ResumeButton));
        Assert.That(menuView.ResumeButton.navigation.selectOnDown, Is.EqualTo(menuView.CharacterButton), "Character comes after Resume.");
        Assert.That(menuView.CharacterButton.navigation.selectOnDown, Is.EqualTo(menuView.ControlsButton), "Party is hidden offline.");
        controller.Close();

        session.IsActive = true;
        TickWith(menu: true);
        Assert.That(menuView.CharacterButton.navigation.selectOnDown, Is.EqualTo(menuView.PartyButton));
        Assert.That(menuView.OptionsButton.navigation.selectOnDown, Is.EqualTo(menuView.LeaveButton), "Save game is hidden joined.");
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
    /// The Party button shows the Party page in the menu's place; Back returns to the menu's buttons without closing
    /// it, and a second Back closes it.
    /// </summary>
    [Test]
    public void Party_ShowsItsPageInTheMenusPlace_AndBackReturnsToTheButtons()
    {
        session.IsActive = true;
        TickWith(menu: true);
        TickWith();

        menuView.PartyButton.onClick.Invoke();
        Assert.That(menuView.ShownPage, Is.EqualTo(GameMenuView.Page.Party));
        Assert.That(menuView.PartyPage.gameObject.activeSelf, Is.True);
        Assert.That(menuView.MenuBox.gameObject.activeSelf, Is.False);
        Assert.That(party.IsOpen, Is.True);

        TickWith(back: true);
        Assert.That(menuView.ShownPage, Is.EqualTo(GameMenuView.Page.Menu));
        Assert.That(menuView.MenuBox.gameObject.activeSelf, Is.True);
        Assert.That(party.IsOpen, Is.False);
        Assert.That(controller.IsOpen, Is.True, "The Back that left the page does not also close the menu.");

        menuView.PartyButton.onClick.Invoke();
        menuView.PartyPage.BackButton.onClick.Invoke();
        Assert.That(menuView.ShownPage, Is.EqualTo(GameMenuView.Page.Menu), "The page's Back button returns too.");

        TickWith(back: true);
        Assert.That(controller.IsOpen, Is.False);
    }

    /// <summary>
    /// The Controls page shows the keys bound now, and follows a rebind; its Chat row shows online only.
    /// </summary>
    [Test]
    public void Controls_ShowTheBoundKeys_FollowARebind_AndShowChatOnlineOnly()
    {
        TickWith(menu: true);

        menuView.ControlsButton.onClick.Invoke();
        ControlsPageView page = menuView.ControlsPage;
        Assert.That(menuView.ShownPage, Is.EqualTo(GameMenuView.Page.Controls));
        Assert.That(page.MoveKey.text, Is.EqualTo("RMB"));
        Assert.That(page.AttackMoveKey.text, Is.EqualTo(bindings.GetActionKeyLabel(PlayerInputService.AttackMoveActionName)).And.Not.Empty);
        Assert.That(page.StopKey.text, Is.EqualTo(bindings.GetActionKeyLabel(PlayerInputService.StopActionName)).And.Not.Empty);
        Assert.That(page.AbilityKey(0).text, Is.EqualTo(bindings.GetAbilityKeyLabel(0)).And.Not.Empty);
        Assert.That(page.AbilityKey(5).text, Is.EqualTo(bindings.GetAbilityKeyLabel(5)).And.Not.Empty);
        Assert.That(page.IsChatShown, Is.False, "No chat offline.");

        InputAction ability = actions.FindAction($"{PlayerInputService.PlayerMapName}/{PlayerInputService.AbilityActionName(0)}");
        Assert.That(bindings.TryAssign(ability, "<Mouse>/middleButton", out _), Is.True);
        Assert.That(page.AbilityKey(0).text, Is.EqualTo("MMB"));

        page.BackButton.onClick.Invoke();
        controller.Close();
        session.IsActive = true;
        TickWith(menu: true);
        menuView.ControlsButton.onClick.Invoke();
        Assert.That(page.IsChatShown, Is.True);
        Assert.That(page.ChatKey.text, Is.EqualTo(bindings.GetActionKeyLabel(PlayerInputService.ChatActionName)).And.Not.Empty, "The keyboard layout's name for the key.");
    }

    /// <summary>
    /// Options, and the Controls page's Change keys, open the options screen in the menu's place while staying paused,
    /// Options on the category used last (Controls the first time) and Change keys on the Controls (Docs/UiRevamp.md,
    /// Phase 8); Back from the options returns to where they were opened from without closing the menu.
    /// </summary>
    [Test]
    public void OptionsAndChangeKeys_OpenTheOptions_AndBackFromThemReturnsWhereTheyWereOpened()
    {
        TickWith(menu: true);
        TickWith();

        menuView.OptionsButton.onClick.Invoke();
        Assert.That(menuView.IsShown, Is.False);
        Assert.That(options.IsOpen, Is.True);
        Assert.That(menus.Top, Is.SameAs(options));
        Assert.That(Time.timeScale, Is.Zero, "Still paused while in the options.");
        Assert.That(options.CurrentCategory, Is.EqualTo(OptionsMenuController.Category.Controls), "In game the options open on the category used last, Controls the first time.");

        TickWith(back: true);
        Assert.That(options.IsOpen, Is.False);
        Assert.That(menuView.IsShown, Is.True);
        Assert.That(menuView.ShownPage, Is.EqualTo(GameMenuView.Page.Menu));
        Assert.That(controller.IsOpen, Is.True, "The Back that closed the options does not also close the menu.");

        menuView.ControlsButton.onClick.Invoke();
        menuView.ControlsPage.ChangeKeysButton.onClick.Invoke();
        Assert.That(options.IsOpen, Is.True);
        Assert.That(options.CurrentCategory, Is.EqualTo(OptionsMenuController.Category.Controls), "Change keys lands on the Controls.");
        TickWith(back: true);
        Assert.That(menuView.IsShown, Is.True);
        Assert.That(menuView.ShownPage, Is.EqualTo(GameMenuView.Page.Controls), "Back from the options returns to the Controls page.");
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
    /// Save game writes the game and says "Saved" on the button for the message time, then its label comes back.
    /// </summary>
    [Test]
    public void SaveGame_WritesTheGame_AndSaysSoOnTheButtonForItsTime()
    {
        TickWith(menu: true);

        menuView.SaveButton.onClick.Invoke();
        Assert.That(saving.Saves, Is.EqualTo(1));
        Assert.That(menuView.SaveText.text, Is.EqualTo(GameMenuController.SavedLabel));

        wallClock.Advance(1.1);
        controller.Tick();
        Assert.That(menuView.SaveText.text, Is.EqualTo(GameMenuController.SavedLabel), "Real time counts, the game being paused.");

        wallClock.Advance(0.2);
        controller.Tick();
        Assert.That(menuView.SaveText.text, Is.EqualTo(GameMenuController.SaveLabel));
    }

    /// <summary>
    /// A save that fails says "Couldn't save" on the button.
    /// </summary>
    [Test]
    public void SaveGame_ThatFails_SaysItCouldNot()
    {
        saving.Succeeds = false;
        TickWith(menu: true);

        menuView.SaveButton.onClick.Invoke();

        Assert.That(menuView.SaveText.text, Is.EqualTo(GameMenuController.SaveFailedLabel));
    }

    /// <summary>
    /// Where this machine does not write the game (a throwaway session), Save game cannot be pressed and saves nothing.
    /// </summary>
    [Test]
    public void SaveGame_CannotBePressed_WhereTheGameIsNotWritten()
    {
        saving.CanSave = false;
        TickWith(menu: true);

        Assert.That(menuView.SaveButton.interactable, Is.False);
        menuView.SaveButton.onClick.Invoke();
        Assert.That(saving.Saves, Is.Zero);
    }

    /// <summary>
    /// Offline, alone as the host, or joined, with the last save written, Leave game and Quit to desktop go ahead at
    /// once.
    /// </summary>
    [Test]
    public void LeavingOrQuitting_WithNothingToLose_GoesAheadAtOnce()
    {
        TickWith(menu: true);
        menuView.QuitButton.onClick.Invoke();
        Assert.That(menuView.IsConfirmShown, Is.False);
        Assert.That(exit.Quits, Is.EqualTo(1), "Offline.");

        controller.Close();
        session.IsActive = true;
        session.IsHost = true;
        AddPlayer("Host", isLocal: true);
        TickWith(menu: true);
        menuView.QuitButton.onClick.Invoke();
        Assert.That(menuView.IsConfirmShown, Is.False);
        Assert.That(exit.Quits, Is.EqualTo(2), "A host alone.");

        controller.Close();
        session.IsHost = false;
        AddPlayer("Other");
        TickWith(menu: true);
        menuView.LeaveButton.onClick.Invoke();
        Assert.That(menuView.IsConfirmShown, Is.False);
        Assert.That(exit.Leaves, Is.EqualTo(1), "Joined: leaving loses nobody else's game.");
        Assert.That(controller.IsLeaving, Is.True);
    }

    /// <summary>
    /// A host with others connected is asked first, the question naming how many and saying the game is saved;
    /// Back or Cancel closes only the question, and confirming leaves.
    /// </summary>
    [Test]
    public void AHostWithOthers_IsAskedFirst_NamingHowMany()
    {
        session.IsActive = true;
        session.IsHost = true;
        AddPlayer("Host", isLocal: true);
        AddPlayer("Second");
        AddPlayer("Third");
        TickWith(menu: true);

        menuView.LeaveButton.onClick.Invoke();
        Assert.That(menuView.IsConfirmShown, Is.True);
        Assert.That(menuView.ConfirmText.text, Is.EqualTo("End the game for 2 other players?\nThe game is saved."));
        Assert.That(exit.Leaves, Is.Zero);

        TickWith(back: true);
        Assert.That(menuView.IsConfirmShown, Is.False);
        Assert.That(controller.IsOpen, Is.True);

        menuView.QuitButton.onClick.Invoke();
        Assert.That(menuView.ConfirmText.text, Does.StartWith("End the game for 2 other players?"));
        menuView.ConfirmNoButton.onClick.Invoke();
        Assert.That(menuView.IsConfirmShown, Is.False);
        Assert.That(exit.Quits, Is.Zero);

        menuView.LeaveButton.onClick.Invoke();
        menuView.ConfirmYesButton.onClick.Invoke();
        Assert.That(exit.Leaves, Is.EqualTo(1));
        Assert.That(controller.IsLeaving, Is.True);
    }

    /// <summary>
    /// The confirmation's words for each case: one other player, many, the game unsaved, and leaving or quitting after
    /// a failed save, offline or joined; none when nothing is lost.
    /// </summary>
    [Test]
    public void TheQuestion_SaysWhatIsLost()
    {
        Assert.That(GameMenuController.Question(true, false, 1, false, false), Is.EqualTo("End the game for 1 other player?\nThe game is saved."));
        Assert.That(GameMenuController.Question(true, false, 3, true, true), Is.EqualTo("End the game for 3 other players?\nThe game couldn't be saved."));
        Assert.That(GameMenuController.Question(false, false, 0, true, false), Is.EqualTo("The game couldn't be saved.\nLeave anyway?"));
        Assert.That(GameMenuController.Question(true, false, 0, true, true), Is.EqualTo("The game couldn't be saved.\nQuit anyway?"));
        Assert.That(GameMenuController.Question(false, true, 2, true, false), Is.EqualTo("Your place couldn't be saved.\nLeave anyway?"));
        Assert.That(GameMenuController.Question(false, false, 0, false, true), Is.Null);
        Assert.That(GameMenuController.Question(true, false, 0, false, false), Is.Null, "A host alone.");
        Assert.That(GameMenuController.Question(false, true, 3, false, false), Is.Null, "Joined.");
    }

    /// <summary>
    /// After a failed save, leaving offline asks first.
    /// </summary>
    [Test]
    public void AfterAFailedSave_LeavingAsksFirst()
    {
        saving.LastWriteFailed = true;
        TickWith(menu: true);

        menuView.LeaveButton.onClick.Invoke();

        Assert.That(menuView.IsConfirmShown, Is.True);
        Assert.That(menuView.ConfirmText.text, Is.EqualTo("The game couldn't be saved.\nLeave anyway?"));
        Assert.That(exit.Leaves, Is.Zero);
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

    /// <summary>
    /// Registers a player named <paramref name="name"/>, as the local one when <paramref name="isLocal"/>.
    /// </summary>
    private void AddPlayer(string name, bool isLocal = false)
    {
        GameObject unit = new(name);
        created.Add(unit);
        PlayerController unitController = unit.AddComponent<PlayerController>();
        Health health = unit.AddComponent<Health>();
        LocalPlayer asLocal = isLocal ? new LocalPlayer(unitController) : null;
        players.Add(asLocal != null ? asLocal.Handle : new PlayerHandle(unit.transform, health));
        if (asLocal != null)
        {
            localPlayer.Assign(asLocal);
        }
    }
}
