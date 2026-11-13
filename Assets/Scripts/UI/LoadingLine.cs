using TMPro;
using UnityEngine;
using VContainer;

/// <summary>
/// The line over the fader while a transition loads (Docs/UiRevamp.md, Phase 9), on a canvas above
/// `TransitionCanvas` in `Main`: it draws <see cref="LoadingMessage"/>'s text, such as "Joining 100.101.102.103's
/// game…", centred on the screen, and hides with none. Injected through the Main scope's auto-inject objects, so Core
/// stays free of TextMesh Pro.
/// </summary>
[DisallowMultipleComponent]
public class LoadingLine : MonoBehaviour
{
    [SerializeField] private TMP_Text line;

    private LoadingMessage message;

    /// <summary>
    /// Receives the message to draw and follows it while enabled.
    /// </summary>
    [Inject]
    public void Construct(LoadingMessage loadingMessage)
    {
        Unfollow();
        message = loadingMessage;
        if (isActiveAndEnabled)
        {
            Follow();
        }
    }

    /// <summary>
    /// Logs an error without its text.
    /// </summary>
    private void Awake()
    {
        if (line == null)
        {
            Debug.LogError($"{name} has no line assigned.", this);
        }
    }

    /// <summary>
    /// Follows the message once injected.
    /// </summary>
    private void OnEnable()
    {
        Follow();
    }

    /// <summary>
    /// Stops following the message.
    /// </summary>
    private void OnDisable()
    {
        Unfollow();
    }

    /// <summary>
    /// Subscribes to the message and shows it as it is now.
    /// </summary>
    private void Follow()
    {
        if (message == null)
        {
            Show();
            return;
        }

        message.Changed -= Show;
        message.Changed += Show;
        Show();
    }

    /// <summary>
    /// Unsubscribes from the message.
    /// </summary>
    private void Unfollow()
    {
        if (message != null)
        {
            message.Changed -= Show;
        }
    }

    /// <summary>
    /// Shows the message's text, or hides the line without any.
    /// </summary>
    private void Show()
    {
        if (line == null)
        {
            return;
        }

        string text = message != null ? message.Text : string.Empty;
        line.text = text;
        line.gameObject.SetActive(text.Length > 0);
    }
}
