using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Passive in-game menu: a panel with Resume, Options, Leave game and Quit, and a confirmation (question, confirm
/// and cancel) for a host about to end the game for everyone. The component stays on an active object and shows or
/// hides its panel.
/// </summary>
[DisallowMultipleComponent]
public class GameMenuView : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button optionsButton;
    [SerializeField] private Button leaveButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private GameObject confirmPanel;
    [SerializeField] private TMP_Text confirmText;
    [SerializeField] private Button confirmYesButton;
    [SerializeField] private TMP_Text confirmYesText;
    [SerializeField] private Button confirmNoButton;

    public Button ResumeButton => resumeButton;
    public Button OptionsButton => optionsButton;
    public Button LeaveButton => leaveButton;
    public Button QuitButton => quitButton;
    public Button ConfirmYesButton => confirmYesButton;
    public Button ConfirmNoButton => confirmNoButton;
    public bool IsShown => panel != null && panel.activeSelf;
    public bool IsConfirmShown => confirmPanel != null && confirmPanel.activeSelf;
    internal TMP_Text ConfirmText => confirmText;

    /// <summary>
    /// Logs an error when any panel, button or text reference is missing.
    /// </summary>
    private void Awake()
    {
        if (panel == null || resumeButton == null || optionsButton == null || leaveButton == null || quitButton == null
            || confirmPanel == null || confirmText == null || confirmYesButton == null || confirmYesText == null || confirmNoButton == null)
        {
            Debug.LogError($"{name} is missing one of its in-game menu references.", this);
        }
    }

    /// <summary>
    /// Shows or hides the menu panel; the component's own object stays active.
    /// </summary>
    public void SetShown(bool shown)
    {
        if (panel != null)
        {
            panel.SetActive(shown);
        }
    }

    /// <summary>
    /// Shows the confirmation with <paramref name="question"/> and <paramref name="confirmLabel"/> on its confirm
    /// button.
    /// </summary>
    public void ShowConfirm(string question, string confirmLabel)
    {
        if (confirmText != null)
        {
            confirmText.text = question;
        }

        if (confirmYesText != null)
        {
            confirmYesText.text = confirmLabel;
        }

        if (confirmPanel != null)
        {
            confirmPanel.SetActive(true);
        }
    }

    /// <summary>
    /// Hides the confirmation, leaving the menu panel as it is.
    /// </summary>
    public void HideConfirm()
    {
        if (confirmPanel != null)
        {
            confirmPanel.SetActive(false);
        }
    }
}
