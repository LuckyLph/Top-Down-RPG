using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

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

    [Inject]
    public void Construct(GameFlow flow, NetworkSession networkSession, GameSave save, OptionsMenuController optionsMenu, UISelection uiSelection)
    {
        gameFlow = flow;
        session = networkSession;
        gameSave = save;
        options = optionsMenu;
        selection = uiSelection;
    }

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

    public void StartNewGame()
    {
        if (!CanStartGame())
        {
            return;
        }

        _ = gameFlow.StartNewGameAsync();
    }

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

    public void HostGame()
    {
        if (!CanStartGame())
        {
            return;
        }

        if (!session.StartHost())
        {
            SetStatus("Could not start hosting.");
            Debug.LogError($"{nameof(MainMenuController)} could not start hosting on port {session.Settings.Port}.", this);
            return;
        }

        gameSave.GetSavedOrStartingArea(out SceneDefinition area, out string spawnId);
        _ = gameFlow.StartNewGameAsync(area, spawnId);
    }

    public void JoinGame()
    {
        if (!CanStartGame())
        {
            return;
        }

        _ = JoinGameAsync();
    }

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
            SetStatus("Connected. Joining the host's area...");
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

    private void HandleOptionsClosed()
    {
        SetButtonsShown(true);
        if (selection != null)
        {
            selection.Select(optionsButton);
        }
    }

    private void SetButtonsShown(bool shown)
    {
        if (buttonsPanel != null)
        {
            buttonsPanel.SetActive(shown);
        }
    }

    public void QuitGame()
    {
        ApplicationExit.Quit();
    }

    private bool CanStartGame()
    {
        return EnsureInjected() && !isJoining && !gameFlow.IsTransitioning;
    }

    private bool EnsureInjected()
    {
        if (gameFlow != null && session != null && gameSave != null)
        {
            return true;
        }

        Debug.LogError($"{nameof(MainMenuController)} was not injected; load MainMenu through the Main scene.", this);
        return false;
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }
}
