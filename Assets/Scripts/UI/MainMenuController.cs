using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VContainer;

[DisallowMultipleComponent]
public class MainMenuController : MonoBehaviour
{
    [SerializeField] private Selectable firstSelected;
    [SerializeField] private TMP_InputField joinAddressField;
    [SerializeField] private TMP_Text statusText;

    private GameFlow gameFlow;
    private NetworkSession session;
    private bool isJoining;

    [Inject]
    public void Construct(GameFlow flow, NetworkSession networkSession)
    {
        gameFlow = flow;
        session = networkSession;
    }

    private void Start()
    {
        if (firstSelected != null && EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(firstSelected.gameObject);
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

        _ = gameFlow.StartNewGameAsync();
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
        if (gameFlow != null && session != null)
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
