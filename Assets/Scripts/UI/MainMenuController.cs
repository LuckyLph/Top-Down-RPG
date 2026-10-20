using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

/// <summary>
/// The main menu's buttons: new game, continue from the save, host, join an address, options and quit. Starting
/// anything is refused while a transition runs or a join is in progress, and a status line reports save and
/// connection problems.
/// </summary>
[DisallowMultipleComponent]
public class MainMenuController : MonoBehaviour
{
    [SerializeField] private Selectable firstSelected;
    [SerializeField] private Button continueButton;
    [SerializeField] private TMP_InputField joinAddressField;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private GameObject buttonsPanel;
    [SerializeField] private Button optionsButton;

    private GameFlow gameFlow;
    private NetworkSession session;
    private GameSave gameSave;
    private OptionsMenuController options;
    private UISelection selection;
    private bool isJoining;

    internal Button ContinueButton => continueButton;
    internal Button OptionsButton => optionsButton;
    internal GameObject ButtonsPanel => buttonsPanel;

    /// <summary>
    /// Receives the scene flow, network session, save, options screen and UI selection services.
    /// </summary>
    [Inject]
    public void Construct(GameFlow flow, NetworkSession networkSession, GameSave save, OptionsMenuController optionsMenu, UISelection uiSelection)
    {
        gameFlow = flow;
        session = networkSession;
        gameSave = save;
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
    /// join address with the session's default and clears the status line.
    /// </summary>
    private void Start()
    {
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

        SetStatus(string.Empty);
    }

    /// <summary>
    /// New game button: starts an offline game in the starting area.
    /// </summary>
    public void StartNewGame()
    {
        if (!CanStartGame())
        {
            return;
        }

        _ = gameFlow.StartNewGameAsync();
    }

    /// <summary>
    /// Continue button: starts an offline game in the saved area and spawn, or reports that there is no save.
    /// </summary>
    public void ContinueGame()
    {
        if (!CanStartGame())
        {
            return;
        }

        if (!gameSave.TryLoadArea(out SceneDefinition area, out string spawnId))
        {
            SetStatus("No saved game to continue.");
            return;
        }

        _ = gameFlow.StartNewGameAsync(area, spawnId);
    }

    /// <summary>
    /// Host button: starts hosting and then a game in the starting world at its entry (hosting from a save comes with
    /// world saves, Docs/WorldStreaming.md's Phase 8). Reports and logs a failure to start the host, or a catalogue
    /// without a starting world.
    /// </summary>
    public void HostGame()
    {
        if (!CanStartGame())
        {
            return;
        }

        WorldDefinition world = gameFlow.Scenes.StartingWorld;
        if (world == null)
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

        _ = gameFlow.StartWorldGameAsync(world, gameFlow.Scenes.StartingWorldEntry);
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
    /// True when the flow, session and save were injected; otherwise logs an error pointing at loading the menu
    /// through the Main scene.
    /// </summary>
    private bool EnsureInjected()
    {
        if (gameFlow != null && session != null && gameSave != null)
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
