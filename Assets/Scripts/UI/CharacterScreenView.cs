using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Passive Character screen (Docs/UiRevamp.md, Phase 7): a full raised panel titled "Character" holding, on the left,
/// the preview stage's texture in a box at a whole multiple of its art pixels (<see cref="MultipleFor"/>) with Turn left,
/// the pose and Turn right under it; on the right a row per group (<see cref="RowChipsView"/>); the focused control's
/// name on the foot line, or a warning over it; Reset all and Back; the swatch picker; a confirmation for Reset all; and
/// online the vitals strip at the top right. The component stays on an active object and shows or hides its panel, and
/// lays the panel out on whole UI px whenever it shows or the canvas changes size, so it fits every display of the
/// Scale table down to 480 x 270 UI px.
/// </summary>
[DisallowMultipleComponent]
public sealed class CharacterScreenView : MonoBehaviour
{
    public const float ContentTop = 28f;
    public const float ContentBottom = 44f;
    public const float Gap = 8f;
    public const float ControlsGap = 4f;
    public const float ControlsHeight = 18f;
    public const float ListWidest = 340f;
    public const float ListNarrowest = 284f;
    public const float WideValue = 130f;
    public const float NarrowValue = 112f;
    public const float VitalsTop = 6f;
    public const float FooterBottom = 8f;
    public const float FootLineBottom = 28f;

    [SerializeField] private GameObject panel;
    [SerializeField, Tooltip("The box the preview shows in, framed, its texture inset by a pixel.")]
    private RectTransform previewBox;
    [SerializeField] private RawImage preview;
    [SerializeField, Tooltip("The row under the box holding Turn left, the pose and Turn right.")]
    private RectTransform controls;
    [SerializeField] private Button turnLeftButton;
    [SerializeField] private Button poseButton;
    [SerializeField] private TMP_Text poseText;
    [SerializeField] private Button turnRightButton;
    [SerializeField, Tooltip("The column the rows stack in.")]
    private RectTransform rows;
    [SerializeField] private RowChipsView rowPrefab;
    [SerializeField, Tooltip("The foot line naming the focused control, in TextMuted.")]
    private TMP_Text focusLine;
    [SerializeField, Tooltip("The foot line's warning, in Accent, shown over the focused control's name.")]
    private TMP_Text warningLine;
    [SerializeField, Tooltip("The row holding Reset all and Back, at the bottom.")]
    private RectTransform footer;
    [SerializeField] private Button resetAllButton;
    [SerializeField] private Button backButton;
    [SerializeField] private GameObject confirmPanel;
    [SerializeField] private Button confirmYesButton;
    [SerializeField] private Button confirmNoButton;
    [SerializeField] private SwatchPickerView picker;
    [SerializeField] private VitalsStripView vitals;
    [SerializeField, Tooltip("The stage drawing the character the box shows.")]
    private CharacterPreviewStage stage;
    [SerializeField, Tooltip("The looks a player can choose.")]
    private CharacterLookCatalog looks;
    [SerializeField, Tooltip("The colours a player can pick.")]
    private CharacterColours colours;

    public Button TurnLeftButton => turnLeftButton;
    public Button PoseButton => poseButton;
    public Button TurnRightButton => turnRightButton;
    public Button ResetAllButton => resetAllButton;
    public Button BackButton => backButton;
    public Button ConfirmYesButton => confirmYesButton;
    public Button ConfirmNoButton => confirmNoButton;
    public SwatchPickerView Picker => picker;
    public VitalsStripView Vitals => vitals;
    public CharacterPreviewStage Stage => stage;
    public CharacterLookCatalog Looks => looks;
    public CharacterColours Colours => colours;
    public bool IsShown => panel != null && panel.activeSelf;
    public bool IsConfirmShown => confirmPanel != null && confirmPanel.activeSelf;
    public int Multiple { get; private set; } = 1;
    internal RectTransform PreviewBox => previewBox;
    internal RectTransform Controls => controls;
    internal RectTransform Rows => rows;
    internal RawImage Preview => preview;
    internal TMP_Text PoseText => poseText;
    internal TMP_Text FocusLine => focusLine;
    internal TMP_Text WarningLine => warningLine;

    /// <summary>
    /// The content's bottom, in UI px down from the panel's top: where the rows and the picker stop.
    /// </summary>
    public float ContentBottomY => ((RectTransform)transform).rect.height - ContentBottom;

    /// <summary>
    /// Logs an error when any reference is missing.
    /// </summary>
    private void Awake()
    {
        if (panel == null || previewBox == null || preview == null || controls == null || turnLeftButton == null || poseButton == null
            || poseText == null || turnRightButton == null || rows == null || rowPrefab == null || focusLine == null || warningLine == null
            || footer == null || resetAllButton == null || backButton == null || confirmPanel == null || confirmYesButton == null || confirmNoButton == null
            || picker == null || vitals == null || stage == null || looks == null || colours == null)
        {
            Debug.LogError($"{name} is missing one of its Character screen references.", this);
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
    /// The largest whole multiple of the stage's art pixels whose box, with the controls under it, fits the content's
    /// height on a canvas of <paramref name="height"/> UI px and leaves the rows at least <see cref="ListNarrowest"/> of
    /// its <paramref name="width"/>; at least 1.
    /// </summary>
    public static int MultipleFor(float width, float height)
    {
        for (int multiple = 4; multiple > 1; multiple--)
        {
            float box = CharacterPreviewStage.Size * multiple + 2f;
            if (ContentTop + box + ControlsGap + ControlsHeight <= height - ContentBottom && Gap + box + Gap + ListNarrowest + Gap <= width)
            {
                return multiple;
            }
        }

        return 1;
    }

    /// <summary>
    /// Shows or hides the screen; the component's own object stays active.
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
    /// Shows <paramref name="texture"/>, the stage's, in the box.
    /// </summary>
    public void SetPreview(Texture texture)
    {
        preview.texture = texture;
    }

    /// <summary>
    /// Shows <paramref name="text"/> on the pose's button.
    /// </summary>
    public void SetPose(string text)
    {
        poseText.text = text;
    }

    /// <summary>
    /// Shows <paramref name="text"/> on the foot line, the focused control's name.
    /// </summary>
    public void SetFocusLine(string text)
    {
        focusLine.text = text;
    }

    /// <summary>
    /// Shows <paramref name="text"/> on the Back button: "Back", or "Start" on the first New game (Docs/UiRevamp.md,
    /// Phase 11).
    /// </summary>
    public void SetBackLabel(string text)
    {
        TMP_Text label = backButton.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.text = text;
        }
    }

    /// <summary>
    /// Shows <paramref name="text"/> as the foot line's warning over the focused control's name, or clears it for null.
    /// </summary>
    public void SetWarning(string text)
    {
        bool shown = !string.IsNullOrEmpty(text);
        warningLine.text = shown ? text : string.Empty;
        focusLine.enabled = !shown;
    }

    /// <summary>
    /// Shows or hides the Reset all confirmation.
    /// </summary>
    public void SetConfirmShown(bool shown)
    {
        confirmPanel.SetActive(shown);
    }

    /// <summary>
    /// Appends a row to the column.
    /// </summary>
    public RowChipsView AddRow()
    {
        return Instantiate(rowPrefab, rows);
    }

    /// <summary>
    /// While the screen shows, lays it out for the canvas: the box at <see cref="MultipleFor"/> its size, the rows
    /// beside it, the box and the rows centred together, the controls centred under the box, the footer and the foot
    /// line centred at the bottom, the vitals strip's right edge on the rows', all on whole UI px.
    /// </summary>
    internal void Place()
    {
        if (panel == null || !panel.activeInHierarchy || !(transform is RectTransform root))
        {
            return;
        }

        Rect area = root.rect;
        Multiple = MultipleFor(area.width, area.height);
        float box = CharacterPreviewStage.Size * Multiple + 2f;
        float list = Mathf.Min(ListWidest, area.width - Gap * 3f - box);
        float left = Mathf.Floor((area.width - (box + Gap + list)) * 0.5f);
        SetTopLeft(previewBox, left, ContentTop, box, box);
        SetTopLeft(controls, left, ContentTop + box + ControlsGap, box, ControlsHeight);
        SetTopLeft(rows, left + box + Gap, ContentTop, list, area.height - ContentBottom - ContentTop);
        float value = list >= ListWidest - 10f ? WideValue : NarrowValue;
        for (int i = 0; i < rows.childCount; i++)
        {
            if (rows.GetChild(i).TryGetComponent(out RowChipsView row))
            {
                row.SetValueWidth(value);
            }
        }

        SetBottomCentre(footer, area.width, FooterBottom);
        SetBottomCentre(focusLine.rectTransform, area.width, FootLineBottom);
        SetBottomCentre(warningLine.rectTransform, area.width, FootLineBottom);
        RectTransform strip = (RectTransform)vitals.transform;
        strip.anchorMin = new Vector2(0f, 1f);
        strip.anchorMax = new Vector2(0f, 1f);
        strip.pivot = new Vector2(1f, 1f);
        strip.anchoredPosition = new Vector2(left + box + Gap + list, -VitalsTop);
        LayoutRebuilder.ForceRebuildLayoutImmediate(rows);
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
