using System;
using VContainer.Unity;

/// <summary>
/// The in-game menu: the Menu key opens it, Back or the Menu key closes it (or its confirmation) one level per tick,
/// and nothing opens or reacts during a transition or once leaving. While open it blocks gameplay input through
/// <see cref="GameplayInputGate"/> and, with no online session, holds the <see cref="TimeFreeze"/>; it lets go of both
/// when it closes or is disposed with the Gameplay scope. Options opens the options screen in its place. Leave game
/// returns to the main menu and Quit closes the application, each after a confirmation for a host, since a host
/// leaving ends the game for everyone.
/// </summary>
public sealed class GameMenuController : IStartable, ITickable, IDisposable
{
    public const string LeaveQuestion = "Leaving ends the game for everyone in it.";
    public const string QuitQuestion = "Quitting ends the game for everyone in it.";
    public const string LeaveLabel = "Leave";
    public const string QuitLabel = "Quit";

    private enum PendingAction
    {
        None,
        Leave,
        Quit,
    }

    private readonly GameMenuView view;
    private readonly OptionsMenuController options;
    private readonly IMenuInput menuInput;
    private readonly GameplayInputGate inputGate;
    private readonly TimeFreeze timeFreeze;
    private readonly GameFlow gameFlow;
    private readonly ISessionStatus session;
    private readonly UISelection selection;
    private PendingAction pending;
    private bool isOpen;
    private bool isPaused;
    private bool isLeaving;
    private int ticks;
    private int ignoreInputUntilTick;

    public GameMenuController(
        GameMenuView view,
        OptionsMenuController options,
        IMenuInput menuInput,
        GameplayInputGate inputGate,
        TimeFreeze timeFreeze,
        GameFlow gameFlow,
        ISessionStatus session,
        UISelection selection)
    {
        this.view = view;
        this.options = options;
        this.menuInput = menuInput;
        this.inputGate = inputGate;
        this.timeFreeze = timeFreeze;
        this.gameFlow = gameFlow;
        this.session = session;
        this.selection = selection;
    }

    public bool IsOpen => isOpen;
    public bool IsPaused => isPaused;
    public bool IsLeaving => isLeaving;

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
    }

    public void Dispose()
    {
        inputGate.Unblock(this);
        timeFreeze.Release(this);
    }

    public void Tick()
    {
        ticks++;
        if (gameFlow.IsTransitioning || isLeaving)
        {
            return;
        }

        if (!isOpen)
        {
            if (menuInput.MenuPressedThisFrame)
            {
                Open();
            }

            return;
        }

        if (options.IsOpen || ticks <= ignoreInputUntilTick)
        {
            return;
        }

        if (!menuInput.BackPressedThisFrame && !menuInput.MenuPressedThisFrame)
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
        inputGate.Block(this);
        isPaused = !session.IsOnline;
        if (isPaused)
        {
            timeFreeze.Hold(this);
        }

        ignoreInputUntilTick = ticks + 1;
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
        inputGate.Unblock(this);
        timeFreeze.Release(this);
        isPaused = false;
        selection.Select(null);
    }

    private void OpenOptions()
    {
        if (!isOpen || view.IsConfirmShown)
        {
            return;
        }

        view.SetShown(false);
        options.Open(HandleOptionsClosed);
    }

    private void HandleOptionsClosed()
    {
        if (!isOpen)
        {
            return;
        }

        view.SetShown(true);
        ignoreInputUntilTick = ticks + 1;
        selection.Select(view.OptionsButton);
    }

    private void RequestLeave()
    {
        Request(PendingAction.Leave);
    }

    private void RequestQuit()
    {
        Request(PendingAction.Quit);
    }

    private void Request(PendingAction action)
    {
        if (!isOpen || isLeaving || view.IsConfirmShown)
        {
            return;
        }

        if (session.IsOnline && session.IsHost)
        {
            pending = action;
            bool leaving = action == PendingAction.Leave;
            view.ShowConfirm(leaving ? LeaveQuestion : QuitQuestion, leaving ? LeaveLabel : QuitLabel);
            selection.Select(view.ConfirmNoButton);
            return;
        }

        Run(action);
    }

    private void Confirm()
    {
        PendingAction action = pending;
        pending = PendingAction.None;
        view.HideConfirm();
        Run(action);
    }

    private void CancelConfirm()
    {
        PendingAction action = pending;
        pending = PendingAction.None;
        view.HideConfirm();
        selection.Select(action == PendingAction.Quit ? view.QuitButton : view.LeaveButton);
    }

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
