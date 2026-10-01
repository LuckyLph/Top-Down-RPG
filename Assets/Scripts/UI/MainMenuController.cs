using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VContainer;

[DisallowMultipleComponent]
public class MainMenuController : MonoBehaviour
{
    [SerializeField] private Selectable firstSelected;
    [SerializeField] private Button continueButton;
    [SerializeField] private TMP_InputField joinAddressField;
    [SerializeField] private TMP_Text statusText;

    private GameFlow gameFlow;
    private NetworkSession session;
    private GameSave gameSave;
    private bool isJoining;

    internal Button ContinueButton => continueButton;

    [Inject]
    public void Construct(GameFlow flow, NetworkSession networkSession, GameSave save)
    {
        gameFlow = flow;
        session = networkSession;
        gameSave = save;
    }

    private void Awake()
    {
        if (continueButton == null)
        {
            Debug.LogError($"{nameof(MainMenuController)} has no Continue button assigned.", this);
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
        if (selected != null && EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(selected.gameObject);
        }

        if (joinAddressField != null && session != null && string.IsNullOrEmpty(joinAddressField.text))
        {
            joinAddressField.text = session.Settings.DefaultAddress;
        }

        SetStatus(string.Empty);
    }

    public void StartNewGame()
    {
        if (!EnsureInjected())
        {
            return;
        }

        _ = gameFlow.StartNewGameAsync();
    }

    public void ContinueGame()
    {
        if (!EnsureInjected())
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
        if (!EnsureInjected() || isJoining)
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
        if (!EnsureInjected() || isJoining)
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

        SetStatus(connected ? "Connected. Joining the host's area..." : "Could not connect to the host.");
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
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
