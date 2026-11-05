using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

/// <summary>
/// The main menu's buttons: new game, continue the latest save, host, join an address, options and quit. Every session
/// runs in seamless worlds (Docs/WorldStreaming.md) and has a save file of its own (Docs/KnownGaps.md, Phase 5): New
/// Game starts a new session and enters the starting world's entry; Continue, and Host Game when there is a save,
/// continue the latest (<see cref="GameSave.TryLoadLatest"/>), the local player starting at its saved place
/// (<see cref="PlayerStarts"/>). The latest can be this machine's copy of its place in a host's session whose host is
/// gone, which goes on as a session of its own; returning here after losing a host says so. Starting anything is refused
/// while a transition runs or a join is in progress, and a status line reports save and connection problems.
/// </summary>
[DisallowMultipleComponent]
public class MainMenuController : MonoBehaviour
{
    internal const string HostGoneStatus = "The host is gone. Continue carries on alone from where you were.";

    [SerializeField] private Selectable firstSelected;
    [SerializeField] private Button continueButton;
    [SerializeField] private TMP_InputField joinAddressField;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private GameObject buttonsPanel;
    [SerializeField] private Button optionsButton;

    private GameFlow gameFlow;
    private NetworkSession session;
    private GameSave gameSave;
    private SessionSave sessionSave;
    private PlayerStarts playerStarts;
    private OptionsMenuController options;
    private UISelection selection;
    private bool isJoining;

    internal Button ContinueButton => continueButton;
    internal string Status => statusText != null ? statusText.text : string.Empty;
    internal Button OptionsButton => optionsButton;
    internal GameObject ButtonsPanel => buttonsPanel;

    /// <summary>
    /// Receives the scene flow, network session, save, the session's saved state and where players start, the options
    /// screen and the UI selection services.
    /// </summary>
    [Inject]
    public void Construct(
        GameFlow flow,
        NetworkSession networkSession,
        GameSave save,
        SessionSave savedSession,
        PlayerStarts starts,
        OptionsMenuController optionsMenu,
        UISelection uiSelection)
    {
        gameFlow = flow;
        session = networkSession;
        gameSave = save;
        sessionSave = savedSession;
        playerStarts = starts;
        options = optionsMenu;
        selection = uiSelection;
    }

    /// <summary>
    /// Logs an error when the Continue button, the buttons panel or the Options button is missing.
    /// </summary>
    private void Awake()
    {
        if (continueButton == null)
        {
            Debug.LogError($"{nameof(MainMenuController)} has no Continue button assigned.", this);
        }

        if (buttonsPanel == null || optionsButton == null)
        {
            Debug.LogError($"{nameof(MainMenuController)} needs its buttons panel and Options button assigned.", this);
        }
    }

    /// <summary>
    /// Enables Continue only when a save exists and selects it (or the first button otherwise), fills an empty
    /// join address with the session's default, and clears the status line, or says the host is gone when a copy of
    /// this machine's place was just saved with its host gone.
    /// </summary>
    private void Start()
    {
        bool hostGone = gameSave != null && gameSave.TakeHostGoneNotice();
        bool hasSave = gameSave != null && gameSave.HasSave;
        if (continueButton != null)
        {
            continueButton.interactable = hasSave;
        }

        Selectable selected = hasSave && continueButton != null ? continueButton : firstSelected;
        if (selection != null)
        {
            selection.Select(selected);
        }

        if (joinAddressField != null && session != null && string.IsNullOrEmpty(joinAddressField.text))
        {
            joinAddressField.text = session.Settings.DefaultAddress;
        }

        SetStatus(hostGone && hasSave ? HostGoneStatus : string.Empty);
    }

    /// <summary>
    /// New game button: starts a new offline session, in the starting world at its entry, with a save file of its own
    /// from its first save.
    /// </summary>
    public void StartNewGame()
    {
        if (!CanStartGame())
        {
            return;
        }

        sessionSave.StartNew();
        StartLocalPlayerSession("There is no world to start.");
    }

    /// <summary>
    /// Continue button: continues the latest save offline, the local player starting at its saved place, or reports
    /// that there is no save.
    /// </summary>
    public void ContinueGame()
    {
        if (!CanStartGame())
        {
            return;
        }

        if (!gameSave.TryLoadLatest(out GameSaveData data))
        {
            SetStatus("No saved game to continue.");
            return;
        }

        sessionSave.StartFrom(data);
        StartLocalPlayerSession("There is no world to continue in.");
    }

    /// <summary>
    /// Host button: starts hosting, then continues the latest save when there is one, or starts a new game, the host's
    /// player starting at its saved place or the starting world's entry. Reports and logs a failure to start the host, or
    /// a catalogue without a starting world.
    /// </summary>
    public void HostGame()
    {
        if (!CanStartGame())
        {
            return;
        }

        if (gameFlow.Scenes.StartingWorld == null)
        {
            SetStatus("There is no world to host.");
            Debug.LogError($"{nameof(GameScenes)} has no starting world; hosting needs one.", this);
            return;
        }

        if (!session.StartHost())
        {
            SetStatus("Could not start hosting.");
            Debug.LogError($"{nameof(MainMenuController)} could not start hosting on port {session.Settings.Port}.", this);
            return;
        }

        if (gameSave.TryLoadLatest(out GameSaveData data))
        {
            sessionSave.StartFrom(data);
        }
        else
        {
            sessionSave.StartNew();
        }

        StartLocalPlayerSession("There is no world to host.");
    }

    /// <summary>
    /// Starts a world session with the local player where <see cref="PlayerStarts"/> puts this install's player id, or
    /// shows <paramref name="noWorldStatus"/> when there is nowhere to start.
    /// </summary>
    private void StartLocalPlayerSession(string noWorldStatus)
    {
        if (!playerStarts.TryGetStart(session.LocalPlayerId, out WorldDefinition world, out Vector2 position))
        {
            SetStatus(noWorldStatus);
            return;
        }

        _ = gameFlow.StartWorldGameAsync(world, position);
    }

    /// <summary>
    /// Join button: connects to the address in the join field in the background, then starts the joined game.
    /// </summary>
    public void JoinGame()
    {
        if (!CanStartGame())
        {
            return;
        }

        _ = JoinGameAsync();
    }

    /// <summary>
    /// Connects to the host within the session's timeout, blocking other starts meanwhile, then starts the joined game
    /// in whichever world the host puts this player; otherwise reports why: refused with the host's reason, or
    /// unreachable. Stops quietly if the menu is destroyed.
    /// </summary>
    private async Awaitable JoinGameAsync()
    {
        isJoining = true;
        string address = joinAddressField != null ? joinAddressField.text : string.Empty;
        SetStatus("Connecting...");

        bool connected;
        try
        {
            connected = await session.JoinAsync(address, session.Settings.ConnectTimeoutSeconds, destroyCancellationToken);
        }
        catch (System.OperationCanceledException)
        {
            return;
        }
        finally
        {
            isJoining = false;
        }

        if (connected)
        {
            SetStatus("Connected. Joining the host's world...");
            sessionSave.StartNew();
            _ = gameFlow.StartJoinedGameAsync();
        }
        else
        {
            SetStatus(session.JoinRefusal != null ? $"The host refused to let you in: {session.JoinRefusal}" : "Could not connect to the host.");
        }
    }

    /// <summary>
    /// Swaps the menu buttons for the options screen, unless a transition runs; they come back, with Options
    /// selected, when it closes.
    /// </summary>
    public void OpenOptions()
    {
        if (options == null || gameFlow == null || gameFlow.IsTransitioning || options.IsOpen)
        {
            return;
        }

        SetButtonsShown(false);
        options.Open(HandleOptionsClosed);
    }

    /// <summary>
    /// Brings the menu buttons back with Options selected when the options screen closes.
    /// </summary>
    private void HandleOptionsClosed()
    {
        SetButtonsShown(true);
        if (selection != null)
        {
            selection.Select(optionsButton);
        }
    }

    /// <summary>
    /// Shows or hides the panel holding the menu buttons.
    /// </summary>
    private void SetButtonsShown(bool shown)
    {
        if (buttonsPanel != null)
        {
            buttonsPanel.SetActive(shown);
        }
    }

    /// <summary>
    /// Quit button: closes the application.
    /// </summary>
    public void QuitGame()
    {
        ApplicationExit.Quit();
    }

    /// <summary>
    /// True when the menu was injected and neither a join nor a scene transition is in progress.
    /// </summary>
    private bool CanStartGame()
    {
        return EnsureInjected() && !isJoining && !gameFlow.IsTransitioning;
    }

    /// <summary>
    /// True when the flow, session, save, session state and player starts were injected; otherwise logs an error
    /// pointing at loading the menu through the Main scene.
    /// </summary>
    private bool EnsureInjected()
    {
        if (gameFlow != null && session != null && gameSave != null && sessionSave != null && playerStarts != null)
        {
            return true;
        }

        Debug.LogError($"{nameof(MainMenuController)} was not injected; load MainMenu through the Main scene.", this);
        return false;
    }

    /// <summary>
    /// Shows <paramref name="message"/> on the status line, when there is one.
    /// </summary>
    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }
}
