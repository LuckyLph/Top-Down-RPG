using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// Passive options screen (Docs/UiRevamp.md, Phase 8): a full raised panel titled "Options" holding a 472 UI px block
/// centred on the canvas, a rail of category items (<see cref="RailItemView"/>) beside a pane of the current category's
/// rows in a scroll list, with the focused row's description in two lines at the pane's foot; under it the notice line,
/// a warning in Accent shown over it, and the footer's category reset and Back; online the vitals strip at the top
/// right; a listening panel that blocks clicks while a rebind waits for input; and a confirmation for the category's
/// reset. The component stays on an active object and shows or hides its panel, and lays the panel out on whole UI px
/// whenever it shows or the canvas changes size, so it fits every display of the Scale table down to 480 x 270 UI px.
/// </summary>
[DisallowMultipleComponent]
public class OptionsMenuView : MonoBehaviour
{
    public const float BlockWidth = 472f;
    public const float RailWidth = 104f;
    public const float Gap = 8f;
    public const float PaneWidth = 360f;
    public const float ContentTop = 28f;
    public const float ContentBottom = 44f;
    public const float DescriptionHeight = 22f;
    public const float DescriptionGap = 6f;
    public const float ScrollbarRoom = 6f;
    public const float VitalsTop = 6f;
    public const float FooterBottom = 8f;
    public const float FootLineBottom = 28f;

    [SerializeField] private GameObject panel;
    [SerializeField, Tooltip("The column of category items at the block's left.")]
    private RectTransform rail;
    [SerializeField] private RailItemView railItemPrefab;
    [SerializeField, Tooltip("The scroll list of the current category's rows, beside the rail.")]
    private RectTransform list;
    [SerializeField] private ScrollRect scroll;
    [SerializeField] private Transform rowsContainer;
    [SerializeField] private TMP_Text sectionHeaderPrefab;
    [SerializeField] private KeyBindingRowView rowPrefab;
    [SerializeField, Tooltip("The row an on-off preference gets: the row with a box marked while it is on.")]
    private KeyBindingRowView toggleRowPrefab;
    [SerializeField, Tooltip("The two lines at the pane's foot describing the focused row, in TextMuted.")]
    private TMP_Text description;
    [SerializeField, Tooltip("The row holding the category's reset and Back, at the bottom.")]
    private RectTransform footer;
    [SerializeField, FormerlySerializedAs("resetAllButton"), Tooltip("The footer's reset of the current category, shown while something in it differs from its default.")]
    private Button categoryResetButton;
    [SerializeField] private TMP_Text categoryResetText;
    [SerializeField] private Button backButton;
    [SerializeField] private TMP_Text noticeText;
    [SerializeField, Tooltip("The notice line's warning, in Accent, shown over the notice.")]
    private TMP_Text warningText;
    [SerializeField] private VitalsStripView vitals;
    [SerializeField] private GameObject listeningPanel;
    [SerializeField] private TMP_Text listeningText;
    [SerializeField] private GameObject confirmPanel;
    [SerializeField] private TMP_Text confirmText;
    [SerializeField] private Button confirmYesButton;
    [SerializeField] private Button confirmNoButton;

    public Button CategoryResetButton => categoryResetButton;
    public Button BackButton => backButton;
    public Button ConfirmYesButton => confirmYesButton;
    public Button ConfirmNoButton => confirmNoButton;
    public VitalsStripView Vitals => vitals;
    public bool IsShown => panel != null && panel.activeSelf;
    public bool IsListeningShown => listeningPanel != null && listeningPanel.activeSelf;
    public bool IsConfirmShown => confirmPanel != null && confirmPanel.activeSelf;
    public bool IsCategoryResetShown => categoryResetButton != null && categoryResetButton.gameObject.activeSelf;
    internal TMP_Text NoticeText => noticeText;
    internal TMP_Text WarningText => warningText;
    internal TMP_Text ListeningText => listeningText;
    internal TMP_Text Description => description;
    internal TMP_Text CategoryResetText => categoryResetText;
    internal TMP_Text ConfirmText => confirmText;
    internal Transform RowsContainer => rowsContainer;
    internal RectTransform Rail => rail;
    internal RectTransform List => list;
    internal RectTransform Footer => footer;

    /// <summary>
    /// Logs an error when any panel, prefab, container, button or text reference is missing.
    /// </summary>
    private void Awake()
    {
        if (panel == null || rail == null || railItemPrefab == null || list == null || scroll == null || rowsContainer == null || sectionHeaderPrefab == null
            || rowPrefab == null || toggleRowPrefab == null || description == null || footer == null || categoryResetButton == null || categoryResetText == null
            || backButton == null || noticeText == null || warningText == null || vitals == null || listeningPanel == null || listeningText == null
            || confirmPanel == null || confirmText == null || confirmYesButton == null || confirmNoButton == null)
        {
            Debug.LogError($"{name} is missing one of its options screen references.", this);
        }
    }

    /// <summary>
    /// Lays the screen out again when the canvas changes size (a UI size step, the game's window).
    /// </summary>
    private void OnRectTransformDimensionsChange()
    {
        Place();
    }

    /// <summary>
    /// Shows or hides the options panel; the component's own object stays active.
    /// </summary>
    public void SetShown(bool shown)
    {
        panel.SetActive(shown);
        if (shown)
        {
            Place();
        }
    }

    /// <summary>
    /// Appends an item to the rail and returns it.
    /// </summary>
    public RailItemView AddRailItem()
    {
        return Instantiate(railItemPrefab, rail);
    }

    /// <summary>
    /// Appends a section header reading <paramref name="text"/> to the rows container and returns it.
    /// </summary>
    public TMP_Text AddSectionHeader(string text)
    {
        TMP_Text header = Instantiate(sectionHeaderPrefab, rowsContainer);
        header.text = text;
        return header;
    }

    /// <summary>
    /// Appends a new, unlabelled binding row to the rows container and returns it.
    /// </summary>
    public KeyBindingRowView AddRow()
    {
        return Instantiate(rowPrefab, rowsContainer);
    }

    /// <summary>
    /// Adds a row with a box beside its button, marked while it is on, for an on-off preference.
    /// </summary>
    public KeyBindingRowView AddToggleRow()
    {
        return Instantiate(toggleRowPrefab, rowsContainer);
    }

    /// <summary>
    /// Shows <paramref name="text"/> at the pane's foot, the focused row's description.
    /// </summary>
    public void SetDescription(string text)
    {
        description.text = text;
    }

    /// <summary>
    /// Labels the footer's reset <paramref name="label"/>, the category's, and shows it when <paramref name="shown"/>;
    /// Back keeps its place either way.
    /// </summary>
    public void SetCategoryReset(string label, bool shown)
    {
        categoryResetText.text = label;
        if (categoryResetButton.gameObject.activeSelf != shown)
        {
            categoryResetButton.gameObject.SetActive(shown);
        }
    }

    /// <summary>
    /// Shows the rows from the top, after the category's rows changed.
    /// </summary>
    public void ScrollToTop()
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)rowsContainer);
        scroll.verticalNormalizedPosition = 1f;
    }

    /// <summary>
    /// Sets the notice line, used for swap and reset messages; an empty string clears it.
    /// </summary>
    public void SetNotice(string text)
    {
        noticeText.text = text;
    }

    /// <summary>
    /// Shows <paramref name="text"/> as the warning over the notice, which hides while it shows, or clears it for null.
    /// </summary>
    public void SetWarning(string text)
    {
        bool shown = !string.IsNullOrEmpty(text);
        warningText.text = shown ? text : string.Empty;
        noticeText.enabled = !shown;
    }

    /// <summary>
    /// Shows the click-blocking listening panel with <paramref name="prompt"/>.
    /// </summary>
    public void ShowListening(string prompt)
    {
        listeningText.text = prompt;
        listeningPanel.SetActive(true);
    }

    /// <summary>
    /// Hides the listening panel.
    /// </summary>
    public void HideListening()
    {
        listeningPanel.SetActive(false);
    }

    /// <summary>
    /// Shows or hides the category reset's confirmation.
    /// </summary>
    public void SetConfirmShown(bool shown)
    {
        confirmPanel.SetActive(shown);
    }

    /// <summary>
    /// Sets the confirmation's question.
    /// </summary>
    public void SetConfirmQuestion(string text)
    {
        confirmText.text = text;
    }

    /// <summary>
    /// While the screen shows, lays it out for the canvas: the block centred, the rail at its left and the pane beside
    /// it, the rows leaving room for the scrollbar, the description at the pane's foot, the notice line and the footer
    /// centred at the bottom, the vitals strip's right edge on the pane's, all on whole UI px.
    /// </summary>
    internal void Place()
    {
        if (panel == null || !panel.activeInHierarchy || !(transform is RectTransform root))
        {
            return;
        }

        Rect area = root.rect;
        float left = Mathf.Floor((area.width - BlockWidth) * 0.5f);
        float pane = left + RailWidth + Gap;
        float descriptionTop = area.height - ContentBottom - DescriptionHeight;
        SetTopLeft(rail, left, ContentTop, RailWidth, area.height - ContentTop - ContentBottom);
        SetTopLeft(list, pane, ContentTop, PaneWidth, descriptionTop - DescriptionGap - ContentTop);
        SetTopLeft(description.rectTransform, pane, descriptionTop, PaneWidth - ScrollbarRoom, DescriptionHeight);
        SetBottomCentre(footer, area.width, FooterBottom);
        SetBottomCentre(noticeText.rectTransform, area.width, FootLineBottom);
        SetBottomCentre(warningText.rectTransform, area.width, FootLineBottom);
        RectTransform strip = (RectTransform)vitals.transform;
        strip.anchorMin = new Vector2(0f, 1f);
        strip.anchorMax = new Vector2(0f, 1f);
        strip.pivot = new Vector2(1f, 1f);
        strip.anchoredPosition = new Vector2(pane + PaneWidth, -VitalsTop);
        LayoutRebuilder.ForceRebuildLayoutImmediate(rail);
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)rowsContainer);
    }

    /// <summary>
    /// Anchors <paramref name="rect"/> at its parent's bottom left and centres it across a parent
    /// <paramref name="width"/> UI px wide, <paramref name="bottom"/> UI px up, on whole UI px.
    /// </summary>
    private static void SetBottomCentre(RectTransform rect, float width, float bottom)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(Mathf.Floor((width - rect.sizeDelta.x) * 0.5f), bottom);
    }

    /// <summary>
    /// Anchors <paramref name="rect"/> at its parent's top left and stands it at (<paramref name="x"/>,
    /// <paramref name="y"/>) UI px from there, y going down, <paramref name="width"/> by <paramref name="height"/>.
    /// </summary>
    private static void SetTopLeft(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(Mathf.Round(x), -Mathf.Round(y));
        rect.sizeDelta = new Vector2(Mathf.Round(width), Mathf.Round(height));
    }
}
