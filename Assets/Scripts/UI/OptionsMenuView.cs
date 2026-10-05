using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Passive options screen: a panel with the Controls rows, Reset all and Back, a notice line, a listening panel that
/// blocks clicks while a rebind waits for input, and a confirmation for Reset all. The component stays on an active
/// object and shows or hides its panel.
/// </summary>
[DisallowMultipleComponent]
public class OptionsMenuView : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private Transform rowsContainer;
    [SerializeField] private TMP_Text sectionHeaderPrefab;
    [SerializeField] private KeyBindingRowView rowPrefab;
    [SerializeField] private Button resetAllButton;
    [SerializeField] private Button backButton;
    [SerializeField] private TMP_Text noticeText;
    [SerializeField] private GameObject listeningPanel;
    [SerializeField] private TMP_Text listeningText;
    [SerializeField] private GameObject confirmPanel;
    [SerializeField] private Button confirmYesButton;
    [SerializeField] private Button confirmNoButton;

    public Button ResetAllButton => resetAllButton;
    public Button BackButton => backButton;
    public Button ConfirmYesButton => confirmYesButton;
    public Button ConfirmNoButton => confirmNoButton;
    public bool IsShown => panel != null && panel.activeSelf;
    public bool IsListeningShown => listeningPanel != null && listeningPanel.activeSelf;
    public bool IsConfirmShown => confirmPanel != null && confirmPanel.activeSelf;
    internal TMP_Text NoticeText => noticeText;
    internal TMP_Text ListeningText => listeningText;
    internal Transform RowsContainer => rowsContainer;

    /// <summary>
    /// Logs an error when any panel, prefab, container, button or text reference is missing.
    /// </summary>
    private void Awake()
    {
        if (panel == null || rowsContainer == null || sectionHeaderPrefab == null || rowPrefab == null || resetAllButton == null
            || backButton == null || noticeText == null || listeningPanel == null || listeningText == null || confirmPanel == null
            || confirmYesButton == null || confirmNoButton == null)
        {
            Debug.LogError($"{name} is missing one of its options screen references.", this);
        }
    }

    /// <summary>
    /// Shows or hides the options panel; the component's own object stays active.
    /// </summary>
    public void SetShown(bool shown)
    {
        if (panel != null)
        {
            panel.SetActive(shown);
        }
    }

    /// <summary>
    /// Appends a section header reading <paramref name="text"/> to the rows container.
    /// </summary>
    public void AddSectionHeader(string text)
    {
        TMP_Text header = Instantiate(sectionHeaderPrefab, rowsContainer);
        header.text = text;
    }

    /// <summary>
    /// Appends a new, unlabelled binding row to the rows container and returns it.
    /// </summary>
    public KeyBindingRowView AddRow()
    {
        return Instantiate(rowPrefab, rowsContainer);
    }

    /// <summary>
    /// Sets the notice line, used for swap and reset messages; an empty string clears it.
    /// </summary>
    public void SetNotice(string text)
    {
        if (noticeText != null)
        {
            noticeText.text = text;
        }
    }

    /// <summary>
    /// Shows the click-blocking listening panel with <paramref name="prompt"/>.
    /// </summary>
    public void ShowListening(string prompt)
    {
        if (listeningText != null)
        {
            listeningText.text = prompt;
        }

        if (listeningPanel != null)
        {
            listeningPanel.SetActive(true);
        }
    }

    /// <summary>
    /// Hides the listening panel.
    /// </summary>
    public void HideListening()
    {
        if (listeningPanel != null)
        {
            listeningPanel.SetActive(false);
        }
    }

    /// <summary>
    /// Shows or hides the Reset all confirmation.
    /// </summary>
    public void SetConfirmShown(bool shown)
    {
        if (confirmPanel != null)
        {
            confirmPanel.SetActive(shown);
        }
    }
}
