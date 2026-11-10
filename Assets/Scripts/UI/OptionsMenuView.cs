using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Passive options screen: a panel with the rows (the Character section's Look row from the catalog of looks it names
/// and its colour rows, each with a chip showing its swatch, from the colours it names, the Display section's rows, its
/// on-off preferences as toggle rows, then the Controls rows), Reset
/// all and Back, a notice line, a listening panel that blocks clicks while a rebind waits for input, and a confirmation
/// for Reset all. The component stays on an active object and shows or hides its panel.
/// </summary>
[DisallowMultipleComponent]
public class OptionsMenuView : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private Transform rowsContainer;
    [SerializeField] private TMP_Text sectionHeaderPrefab;
    [SerializeField] private KeyBindingRowView rowPrefab;
    [SerializeField, Tooltip("The row a colour choice gets: the row with a chip showing the picked swatch.")]
    private KeyBindingRowView swatchRowPrefab;
    [SerializeField, Tooltip("The row an on-off preference gets: the row with a box marked while it is on.")]
    private KeyBindingRowView toggleRowPrefab;
    [SerializeField] private Button resetAllButton;
    [SerializeField] private Button backButton;
    [SerializeField] private TMP_Text noticeText;
    [SerializeField] private GameObject listeningPanel;
    [SerializeField] private TMP_Text listeningText;
    [SerializeField] private GameObject confirmPanel;
    [SerializeField] private Button confirmYesButton;
    [SerializeField] private Button confirmNoButton;
    [SerializeField, Tooltip("The looks a player can choose in the Character section; none hides the section.")]
    private CharacterLookCatalog looks;
    [SerializeField, Tooltip("The colours a player can pick in the Character section, a row per choice that a look's sheets carry; none shows no colour rows.")]
    private CharacterColours colours;

    public Button ResetAllButton => resetAllButton;
    public Button BackButton => backButton;
    public Button ConfirmYesButton => confirmYesButton;
    public Button ConfirmNoButton => confirmNoButton;
    public CharacterLookCatalog Looks => looks;
    public CharacterColours Colours => colours;
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
        if (panel == null || rowsContainer == null || sectionHeaderPrefab == null || rowPrefab == null || swatchRowPrefab == null || toggleRowPrefab == null || resetAllButton == null
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
    /// Adds a row with a swatch chip beside its button, for a colour choice.
    /// </summary>
    public KeyBindingRowView AddSwatchRow()
    {
        return Instantiate(swatchRowPrefab, rowsContainer);
    }

    /// <summary>
    /// Adds a row with a box beside its button, marked while it is on, for an on-off preference.
    /// </summary>
    public KeyBindingRowView AddToggleRow()
    {
        return Instantiate(toggleRowPrefab, rowsContainer);
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
