using System;
using VContainer.Unity;

/// <summary>
/// The in-game menu: <see cref="MenuStack.MenuRequested"/> (the Menu key with no menu open) opens it as a layer on the
/// stack, Back or the Menu key on top of it closes its confirmation or else the menu, and nothing opens or reacts during
/// a transition or once leaving. While open it blocks gameplay input through
/// <see cref="GameplayInputGate"/> and, with no online session, holds the <see cref="TimeFreeze"/>; it lets go of both
/// when it closes or is disposed with the Gameplay scope. Options opens the options screen in its place. Leave game
/// returns to the main menu and Quit closes the application, each after a confirmation for a host, since a host
/// leaving ends the game for everyone.
/// </summary>
public sealed class GameMenuController : IStartable, IDisposable, IMenuLayer
{
    public const string LeaveQuestion = "Leaving ends the game for everyone in it.";
    public const string QuitQuestion = "Quitting ends the game for everyone in it.";
    public const string LeaveLabel = "Leave";
    public const string QuitLabel = "Quit";

    /// <summary>
    /// The action waiting on the host's confirmation, or <see cref="PendingAction.None"/>.
    /// </summary>
    private enum PendingAction
    {
        None,
        Leave,
        Quit,
    }

    private readonly GameMenuView view;
    private readonly OptionsMenuController options;
    private readonly MenuStack menus;
    private readonly GameplayInputGate inputGate;
    private readonly TimeFreeze timeFreeze;
    private readonly GameFlow gameFlow;
    private readonly ISessionStatus session;
    private readonly UISelection selection;
    private PendingAction pending;
    private bool isOpen;
    private bool isPaused;
    private bool isLeaving;

    /// <summary>
    /// Creates a closed menu controller over <paramref name="view"/>, opening <paramref name="options"/> in its place
    /// and asking <paramref name="session"/> whether to pause and whether to confirm leaving.
    /// </summary>
    public GameMenuController(
        GameMenuView view,
        OptionsMenuController options,
        MenuStack menus,
        GameplayInputGate inputGate,
        TimeFreeze timeFreeze,
        GameFlow gameFlow,
        ISessionStatus session,
        UISelection selection)
    {
        this.view = view;
        this.options = options;
        this.menus = menus;
        this.inputGate = inputGate;
        this.timeFreeze = timeFreeze;
        this.gameFlow = gameFlow;
        this.session = session;
        this.selection = selection;
    }

    public bool IsOpen => isOpen;
    public bool IsPaused => isPaused;
    public bool IsLeaving => isLeaving;

    /// <summary>
    /// Hides the menu and its confirmation, wires the buttons and starts listening for menu requests.
    /// </summary>
    public void Start()
    {
        view.SetShown(false);
        view.HideConfirm();
        view.ResumeButton.onClick.AddListener(Close);
        view.OptionsButton.onClick.AddListener(OpenOptions);
        view.LeaveButton.onClick.AddListener(RequestLeave);
        view.QuitButton.onClick.AddListener(RequestQuit);
        view.ConfirmYesButton.onClick.AddListener(Confirm);
        view.ConfirmNoButton.onClick.AddListener(CancelConfirm);
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
    /// Cancels the open confirmation, or else closes the menu. Ignored during a transition or once leaving.
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
        else
        {
            Close();
        }
    }

    /// <summary>
    /// Shows the menu with Resume selected, blocks gameplay input and, with no online session, pauses the game.
    /// </summary>
    public void Open()
    {
        if (isOpen)
        {
            return;
        }

        isOpen = true;
        pending = PendingAction.None;
        view.HideConfirm();
        view.SetShown(true);
        menus.Push(this);
        inputGate.Block(this);
        isPaused = !session.IsActive;
        if (isPaused)
        {
            timeFreeze.Hold(this);
        }

        selection.Select(view.ResumeButton);
    }

    /// <summary>
    /// Hides the menu and the options screen if it is open, gives gameplay input back and resumes the game.
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

        view.HideConfirm();
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
    /// Hides the menu panel and opens the options screen in its place. Ignored while closed or confirming.
    /// </summary>
    private void OpenOptions()
    {
        if (!isOpen || view.IsConfirmShown)
        {
            return;
        }

        view.SetShown(false);
        options.Open(HandleOptionsClosed);
    }

    /// <summary>
    /// Shows the menu again with Options selected when the options screen closes while the menu is still open.
    /// </summary>
    private void HandleOptionsClosed()
    {
        if (!isOpen)
        {
            return;
        }

        view.SetShown(true);
        selection.Select(view.OptionsButton);
    }

    /// <summary>
    /// Leave game button: returns to the main menu, after a confirmation for a host.
    /// </summary>
    private void RequestLeave()
    {
        Request(PendingAction.Leave);
    }

    /// <summary>
    /// Quit button: closes the application, after a confirmation for a host.
    /// </summary>
    private void RequestQuit()
    {
        Request(PendingAction.Quit);
    }

    /// <summary>
    /// Runs <paramref name="action"/> straight away, or, for the host of an online session, shows the confirmation
    /// with Cancel selected and keeps the action pending. Ignored while closed, leaving or already confirming.
    /// </summary>
    private void Request(PendingAction action)
    {
        if (!isOpen || isLeaving || view.IsConfirmShown)
        {
            return;
        }

        if (session.IsActive && session.IsHost)
        {
            pending = action;
            bool leaving = action == PendingAction.Leave;
            view.ShowConfirm(leaving ? LeaveQuestion : QuitQuestion, leaving ? LeaveLabel : QuitLabel);
            selection.Select(view.ConfirmNoButton);
            return;
        }

        Run(action);
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
            ApplicationExit.Quit();
            return;
        }

        if (action == PendingAction.Leave && !gameFlow.IsTransitioning)
        {
            isLeaving = true;
            _ = gameFlow.ShowMainMenuAsync();
        }
    }
}
