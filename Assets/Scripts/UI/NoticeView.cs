using TMPro;
using UnityEngine;

/// <summary>
/// The HUD's notice line (Docs/UiRevamp.md, Phase 3): a <c>Panel</c> at the top centre holding a <c>NoticeLine</c>,
/// which wraps, its text centred in the panel less the side padding. <see cref="Show"/> sizes the panel to its text,
/// up to <see cref="MaxWidth"/> UI px wide, <see cref="MaxLines"/> lines and <see cref="MaxHeight"/> UI px tall, and
/// shows it; <see cref="Hide"/> hides it. Passive: <see cref="NoticePresenter"/> says what it shows. Showing the text it
/// already shows does nothing, so the presenter may call it every frame.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class NoticeView : MonoBehaviour
{
    [SerializeField, Tooltip("The notice line inside the panel, stretched to it less the side padding.")]
    private TMP_Text text;
    [SerializeField, Min(16f), Tooltip("The widest the panel grows, in UI px: the notice band's width.")]
    private float maxWidth = 240f;
    [SerializeField, Min(1), Tooltip("The most lines a notice wraps to.")]
    private int maxLines = 2;
    [SerializeField, Min(8f), Tooltip("The tallest the panel grows, in UI px: the notice band's height. Two lines take it all, their padding shrinking to fit.")]
    private float maxHeight = 24f;
    [SerializeField, Min(0f), Tooltip("UI px between the panel's sides and the text.")]
    private float horizontalPadding = 4f;
    [SerializeField, Min(0f), Tooltip("UI px between the panel's top and bottom and the text's lines, less where the lines would pass the tallest panel.")]
    private float verticalPadding = 2f;

    private RectTransform panel;
    private string shown;

    public float MaxWidth => maxWidth;
    public int MaxLines => maxLines;
    public float MaxHeight => maxHeight;
    public bool IsShown => shown != null && gameObject.activeSelf;
    public string Text => shown;
    internal TMP_Text Label => text;

    /// <summary>
    /// Caches the panel's rect and logs an error without a text.
    /// </summary>
    private void Awake()
    {
        panel = (RectTransform)transform;
        if (text == null)
        {
            Debug.LogError($"{name} has no notice text assigned.", this);
        }
    }

    /// <summary>
    /// Shows <paramref name="notice"/>, wrapped to the widest line that fits, on a panel sized to it; nothing when it
    /// already shows that text.
    /// </summary>
    public void Show(string notice)
    {
        if (text == null || (IsShown && ReferenceEquals(notice, shown)))
        {
            return;
        }

        shown = notice;
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        Fit(notice);
    }

    /// <summary>
    /// Hides the notice.
    /// </summary>
    public void Hide()
    {
        shown = null;
        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Lays <paramref name="notice"/> out at the widest panel, then shrinks the panel to its widest line, an even
    /// number of UI px so it stays centred on the band, and to its lines, at most <see cref="maxLines"/> and
    /// <see cref="maxHeight"/> UI px.
    /// </summary>
    private void Fit(string notice)
    {
        if (panel == null)
        {
            panel = (RectTransform)transform;
        }

        float lineHeight = LineHeight();
        text.rectTransform.offsetMin = new Vector2(horizontalPadding, 0f);
        text.rectTransform.offsetMax = new Vector2(-horizontalPadding, 0f);
        panel.sizeDelta = new Vector2(maxWidth, Height(maxLines, lineHeight));
        text.text = notice;
        text.ForceMeshUpdate(true);
        int lines = Mathf.Clamp(text.textInfo.lineCount, 1, maxLines);
        float width = Mathf.Ceil(text.textBounds.size.x) + 2f * horizontalPadding;
        width = Mathf.Min(maxWidth, Mathf.Ceil(width * 0.5f) * 2f);
        panel.sizeDelta = new Vector2(width, Height(lines, lineHeight));
        text.ForceMeshUpdate(true);
    }

    /// <summary>
    /// The panel's height for <paramref name="lines"/> lines of <paramref name="lineHeight"/> UI px: the lines and the
    /// padding, at most <see cref="maxHeight"/>.
    /// </summary>
    private float Height(int lines, float lineHeight)
    {
        return Mathf.Min(maxHeight, lines * lineHeight + 2f * verticalPadding);
    }

    /// <summary>
    /// The height of one line of the text's font at its size, in UI px.
    /// </summary>
    private float LineHeight()
    {
        TMP_FontAsset font = text.font;
        if (font == null || font.faceInfo.pointSize <= 0f)
        {
            return text.fontSize;
        }

        return Mathf.Ceil(font.faceInfo.lineHeight * text.fontSize / font.faceInfo.pointSize);
    }
}
