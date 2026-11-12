using System;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// The in-game menu (Docs/UiRevamp.md, Phase 6): <see cref="MenuStack.MenuRequested"/> (the Menu key with no menu open)
/// opens it as a layer on the stack. Nothing opens or reacts during a transition or once leaving.
/// <list type="bullet">
/// <item><b>Open:</b> titled "Paused" offline, where it holds the <see cref="TimeFreeze"/> and dims the game, and "Menu"
/// online, where nothing pauses and nothing dims; it blocks gameplay input through <see cref="GameplayInputGate"/> and
/// lets go of both when it closes or is disposed with the Gameplay scope.</item>
/// <item><b>Buttons,</b> looping: Resume, Character, Party (online), Controls, Options, Save game (offline and hosting),
/// Leave game and Quit to desktop. Party and Controls show their pages in the menu's place, and Back returns from them;
/// Character opens the Character screen in its place (Docs/UiRevamp.md, Phase 7), with the local player's weapon; Options,
/// and the Controls page's Change keys, open the options screen in its place.</item>
/// <item><b>Save game</b> writes the game through <see cref="IGameSaving"/>, its label reading "Saved" or
/// "Couldn't save" for <see cref="HudSettings.MessageSeconds"/> of real time; it cannot be pressed where this machine
/// does not write the game (a throwaway session).</item>
/// <item><b>Leaving or quitting</b> asks only when it loses something: as the host with others connected ("End the game
/// for 2 other players?" and whether the game is saved), or after this machine's last save failed. Otherwise it goes
/// ahead at once through <see cref="IGameExit"/>.</item>
/// </list>
/// Back or the Menu key closes the confirmation, else returns from a page, else closes the menu.
/// </summary>
public sealed class GameMenuController : IStartable, ITickable, IDisposable, IMenuLayer
{
    public const string PausedTitle = "Paused";
    public const string MenuTitle = "Menu";
    public const string SaveLabel = "Save game";
    public const string SavedLabel = "Saved";
    public const string SaveFailedLabel = "Couldn't save";
    public const string LeaveLabel = "Leave";
    public const string QuitLabel = "Quit";
    public const string EndGameForOne = "End the game for 1 other player?";
    public const string EndGameFormat = "End the game for {0} other players?";
    public const string GameSaved = "The game is saved.";
    public const string GameNotSaved = "The game couldn't be saved.";
    public const string PlaceNotSaved = "Your place couldn't be saved.";
    public const string LeaveAnyway = "Leave anyway?";
    public const string QuitAnyway = "Quit anyway?";

    /// <summary>
    /// The action waiting on the confirmation, or <see cref="PendingAction.None"/>.
    /// </summary>
    private enum PendingAction
    {
        None,
        Leave,
        Quit,
    }

    private readonly GameMenuView view;
    private readonly OptionsMenuController options;
    private readonly CharacterScreenController character;
    private readonly PartyPagePresenter party;
    private readonly MenuStack menus;
    private readonly GameplayInputGate inputGate;
    private readonly TimeFreeze timeFreeze;
    private readonly GameFlow gameFlow;
    private readonly ISessionStatus session;
    private readonly UISelection selection;
    private readonly IGameSaving saving;
    private readonly IGameExit exit;
    private readonly IPlayerRegistry players;
    private readonly LocalPlayerTracker localPlayer;
    private readonly IWallClock wallClock;
    private readonly HudSettings settings;
    private PendingAction pending;
    private bool isOpen;
    private bool isPaused;
    private bool isLeaving;
    private bool saveAnswered;
    private DateTime saveAnswerUntil;

    /// <summary>
    /// Creates a closed menu controller over <paramref name="view"/>, opening <paramref name="options"/>,
    /// <paramref name="character"/> and the Party page's <paramref name="party"/> in its place, asking <paramref name="session"/> whether to pause and which buttons
    /// show, saving through <paramref name="saving"/>, leaving through <paramref name="exit"/>, and counting the other
    /// players of <paramref name="players"/> for the host's confirmation.
    /// </summary>
    public GameMenuController(
        GameMenuView view,
        OptionsMenuController options,
        CharacterScreenController character,
        PartyPagePresenter party,
        MenuStack menus,
        GameplayInputGate inputGate,
        TimeFreeze timeFreeze,
        GameFlow gameFlow,
        ISessionStatus session,
        UISelection selection,
        IGameSaving saving,
        IGameExit exit,
        IPlayerRegistry players,
        LocalPlayerTracker localPlayer,
        IWallClock wallClock,
        HudSettings settings)
    {
        this.view = view;
        this.options = options;
        this.character = character;
        this.party = party;
        this.menus = menus;
        this.inputGate = inputGate;
        this.timeFreeze = timeFreeze;
        this.gameFlow = gameFlow;
        this.session = session;
        this.selection = selection;
        this.saving = saving;
        this.exit = exit;
        this.players = players;
        this.localPlayer = localPlayer;
        this.wallClock = wallClock;
        this.settings = settings;
    }

    public bool IsOpen => isOpen;
    public bool IsPaused => isPaused;
    public bool IsLeaving => isLeaving;

    /// <summary>
    /// The confirmation leaving (or, <paramref name="quitting"/>, quitting) asks, or null when it goes ahead at once:
    /// the host with <paramref name="others"/> other players connected is told how many and whether the game is saved;
    /// anyone whose last save failed is asked whether to go anyway (a joined client about its place).
    /// </summary>
    public static string Question(bool hosting, bool joined, int others, bool saveFailed, bool quitting)
    {
        if (hosting && others > 0)
        {
            string ending = others == 1 ? EndGameForOne : string.Format(EndGameFormat, others);
            return ending + "\n" + (saveFailed ? GameNotSaved : GameSaved);
        }

        if (saveFailed)
        {
            return (joined ? PlaceNotSaved : GameNotSaved) + "\n" + (quitting ? QuitAnyway : LeaveAnyway);
        }

        return null;
    }

    /// <summary>
    /// Hides the menu and its confirmation, wires the buttons of the menu and its pages and starts listening for menu
    /// requests.
    /// </summary>
    public void Start()
    {
        view.SetShown(false);
        view.HideConfirm();
        view.ShowPage(GameMenuView.Page.Menu);
        view.ResumeButton.onClick.AddListener(Close);
        view.CharacterButton.onClick.AddListener(OpenCharacter);
        view.PartyButton.onClick.AddListener(OpenParty);
        view.ControlsButton.onClick.AddListener(OpenControls);
        view.OptionsButton.onClick.AddListener(OpenOptions);
        view.SaveButton.onClick.AddListener(SaveGame);
        view.LeaveButton.onClick.AddListener(RequestLeave);
        view.QuitButton.onClick.AddListener(RequestQuit);
        view.ConfirmYesButton.onClick.AddListener(Confirm);
        view.ConfirmNoButton.onClick.AddListener(CancelConfirm);
        view.PartyPage.BackButton.onClick.AddListener(BackToMenu);
        view.ControlsPage.BackButton.onClick.AddListener(BackToMenu);
        view.ControlsPage.ChangeKeysButton.onClick.AddListener(ChangeKeys);
        menus.MenuRequested += HandleMenuRequested;
    }

    /// <summary>
    /// Stops listening for menu requests, leaves the menu stack and releases the input block and the time freeze,
    /// so nothing stays held when the Gameplay scope goes away.
    /// </summary>
    public void Dispose()
    {
        menus.MenuRequested -= HandleMenuRequested;
        menus.Remove(this);
        inputGate.Unblock(this);
        timeFreeze.Release(this);
        isOpen = false;
        isPaused = false;
    }

    /// <summary>
    /// Puts the Save game button's label back once its answer has shown its time.
    /// </summary>
    public void Tick()
    {
        if (saveAnswered && wallClock.UtcNow >= saveAnswerUntil)
        {
            saveAnswered = false;
            view.SetSave(saving.CanSave, SaveLabel);
        }
    }

    /// <summary>
    /// Cancels the open confirmation, else returns from a page, else closes the menu. Ignored during a transition or
    /// once leaving.
    /// </summary>
    public void HandleBack()
    {
        if (gameFlow.IsTransitioning || isLeaving)
        {
            return;
        }

        if (view.IsConfirmShown)
        {
            CancelConfirm();
        }
        else if (view.ShownPage != GameMenuView.Page.Menu)
        {
            BackToMenu();
        }
        else
        {
            Close();
        }
    }

    /// <summary>
    /// Shows the menu with Resume selected, its title, buttons and scrim for the session, blocks gameplay input and,
    /// with no online session, pauses the game.
    /// </summary>
    public void Open()
    {
        if (isOpen)
        {
            return;
        }

        isOpen = true;
        pending = PendingAction.None;
        bool online = session.IsActive;
        bool joined = online && !session.IsHost;
        view.HideConfirm();
        view.SetTitle(online ? MenuTitle : PausedTitle, !online);
        view.SetButtons(online, !joined);
        saveAnswered = false;
        view.SetSave(saving.CanSave, SaveLabel);
        view.ShowPage(GameMenuView.Page.Menu);
        view.SetShown(true);
        menus.Push(this);
        inputGate.Block(this);
        isPaused = !online;
        if (isPaused)
        {
            timeFreeze.Hold(this);
        }

        selection.Select(view.ResumeButton);
    }

    /// <summary>
    /// Hides the menu, its pages and the options or Character screen if one is open, gives gameplay input back and
    /// resumes the game.
    /// </summary>
    public void Close()
    {
        if (!isOpen)
        {
            return;
        }

        isOpen = false;
        pending = PendingAction.None;
        if (options.IsOpen)
        {
            options.Close();
        }

        if (character.IsOpen)
        {
            character.Close();
        }

        party.Close();
        view.HideConfirm();
        view.ShowPage(GameMenuView.Page.Menu);
        view.SetShown(false);
        menus.Remove(this);
        inputGate.Unblock(this);
        timeFreeze.Release(this);
        isPaused = false;
        selection.Select(null);
    }

    /// <summary>
    /// Opens the menu when the Menu key is pressed with no menu open, unless a transition is running or the
    /// player is leaving.
    /// </summary>
    private void HandleMenuRequested()
    {
        if (!gameFlow.IsTransitioning && !isLeaving)
        {
            Open();
        }
    }

    /// <summary>
    /// Whether the menu's buttons answer: it is open, on its buttons, with no confirmation showing and not leaving.
    /// </summary>
    private bool OnMenu => isOpen && !isLeaving && !view.IsConfirmShown && view.ShownPage == GameMenuView.Page.Menu;

    /// <summary>
    /// Shows the Party page in the menu's place, its first control selected.
    /// </summary>
    private void OpenParty()
    {
        if (!OnMenu)
        {
            return;
        }

        party.Open();
        view.ShowPage(GameMenuView.Page.Party);
        selection.Select(view.PartyPage.FirstSelectable);
    }

    /// <summary>
    /// Shows the Controls page in the menu's place, with the Chat row online, Back selected.
    /// </summary>
    private void OpenControls()
    {
        if (!OnMenu)
        {
            return;
        }

        view.ControlsPage.SetChatShown(session.IsActive);
        view.ShowPage(GameMenuView.Page.Controls);
        selection.Select(view.ControlsPage.BackButton);
    }

    /// <summary>
    /// Returns from a page to the menu's buttons, with the button that opened it selected.
    /// </summary>
    private void BackToMenu()
    {
        if (!isOpen || view.ShownPage == GameMenuView.Page.Menu)
        {
            return;
        }

        bool fromParty = view.ShownPage == GameMenuView.Page.Party;
        party.Close();
        view.ShowPage(GameMenuView.Page.Menu);
        selection.Select(fromParty ? view.PartyButton : view.ControlsButton);
    }

    /// <summary>
    /// Hides the menu and opens the options screen in its place.
    /// </summary>
    private void OpenOptions()
    {
        if (!OnMenu)
        {
            return;
        }

        view.SetShown(false);
        options.Open(HandleOptionsClosed);
    }

    /// <summary>
    /// Hides the menu and opens the Character screen in its place, its preview in the local player's weapon's gear.
    /// </summary>
    private void OpenCharacter()
    {
        if (!OnMenu)
        {
            return;
        }

        view.SetShown(false);
        character.Open(HandleCharacterClosed, localPlayer.Current?.Weapon != null ? localPlayer.Current.Weapon.CurrentWeapon : null);
    }

    /// <summary>
    /// Shows the menu again when the Character screen closes while it is still open, with Character selected.
    /// </summary>
    private void HandleCharacterClosed()
    {
        if (!isOpen)
        {
            return;
        }

        view.SetShown(true);
        selection.Select(view.CharacterButton);
    }

    /// <summary>
    /// The Controls page's Change keys: hides the menu and opens the options screen at its key bindings.
    /// </summary>
    private void ChangeKeys()
    {
        if (!isOpen || isLeaving || view.ShownPage != GameMenuView.Page.Controls)
        {
            return;
        }

        view.SetShown(false);
        options.Open(HandleOptionsClosed, true);
    }

    /// <summary>
    /// Shows the menu again when the options screen closes while it is still open, with the button that opened them
    /// selected: Options on the menu's buttons, Change keys on the Controls page.
    /// </summary>
    private void HandleOptionsClosed()
    {
        if (!isOpen)
        {
            return;
        }

        view.SetShown(true);
        selection.Select(view.ShownPage == GameMenuView.Page.Controls ? view.ControlsPage.ChangeKeysButton : view.OptionsButton);
    }

    /// <summary>
    /// Save game: writes the game now and says on the button whether it was written.
    /// </summary>
    private void SaveGame()
    {
        if (!OnMenu || !saving.CanSave)
        {
            return;
        }

        bool saved = saving.Save();
        view.SetSave(saving.CanSave, saved ? SavedLabel : SaveFailedLabel);
        saveAnswered = true;
        saveAnswerUntil = wallClock.UtcNow + TimeSpan.FromSeconds(settings.MessageSeconds);
    }

    /// <summary>
    /// Leave game button: returns to the main menu, after a confirmation when leaving loses something.
    /// </summary>
    private void RequestLeave()
    {
        Request(PendingAction.Leave);
    }

    /// <summary>
    /// Quit to desktop button: closes the application, after a confirmation when quitting loses something.
    /// </summary>
    private void RequestQuit()
    {
        Request(PendingAction.Quit);
    }

    /// <summary>
    /// Runs <paramref name="action"/> straight away, or, when it would lose something (<see cref="Question"/>), shows
    /// the confirmation with Cancel selected and keeps the action pending. Ignored off the menu's buttons.
    /// </summary>
    private void Request(PendingAction action)
    {
        if (!OnMenu)
        {
            return;
        }

        bool hosting = session.IsActive && session.IsHost;
        bool joined = session.IsActive && !session.IsHost;
        bool quitting = action == PendingAction.Quit;
        string question = Question(hosting, joined, OtherPlayers(), saving.LastWriteFailed, quitting);
        if (question == null)
        {
            Run(action);
            return;
        }

        pending = action;
        view.ShowConfirm(question, quitting ? QuitLabel : LeaveLabel);
        selection.Select(view.ConfirmNoButton);
    }

    /// <summary>
    /// How many registered players are not this machine's own.
    /// </summary>
    private int OtherPlayers()
    {
        Transform local = localPlayer.Current?.Handle?.Transform;
        int count = 0;
        for (int i = 0; i < players.Players.Count; i++)
        {
            PlayerHandle player = players.Players[i];
            if (player != null && (local == null || player.Transform != local))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Confirmation accepted: hides it and runs the pending action.
    /// </summary>
    private void Confirm()
    {
        PendingAction action = pending;
        pending = PendingAction.None;
        view.HideConfirm();
        Run(action);
    }

    /// <summary>
    /// Confirmation declined: hides it, drops the pending action and reselects the button that asked for it.
    /// </summary>
    private void CancelConfirm()
    {
        PendingAction action = pending;
        pending = PendingAction.None;
        view.HideConfirm();
        selection.Select(action == PendingAction.Quit ? view.QuitButton : view.LeaveButton);
    }

    /// <summary>
    /// Quits the application, or marks the menu as leaving and starts the return to the main menu unless a
    /// transition is already running. <see cref="PendingAction.None"/> does nothing.
    /// </summary>
    private void Run(PendingAction action)
    {
        if (action == PendingAction.Quit)
        {
            exit.Quit();
            return;
        }

        if (action == PendingAction.Leave && !gameFlow.IsTransitioning)
        {
            isLeaving = true;
            exit.LeaveToMenu();
        }
    }
}
