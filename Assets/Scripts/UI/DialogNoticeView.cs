using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The `DialogNotice` component (Docs/UiRevamp.md, Phase 9), a variant of `Dialog`: over the modal scrim, a raised box
/// holding a message, each sentence starting on its own line, and one or two plain buttons, the first primary and a
/// second secondary; a lone button is centred. The box is as tall as its message and stands at the screen's centre on
/// whole UI px. Passive: whoever shows it wires its buttons and selects one.
/// </summary>
[DisallowMultipleComponent]
public class DialogNoticeView : MonoBehaviour
{
    public const float BoxWidth = 240f;
    public const float Padding = 8f;
    public const float LineHeight = 11f;
    public const float ButtonWidth = 96f;
    public const float ButtonHeight = 16f;
    public const float ButtonGap = 8f;

    [SerializeField] private RectTransform box;
    [SerializeField] private TMP_Text message;
    [SerializeField] private RectTransform answers;
    [SerializeField] private Button primaryButton;
    [SerializeField] private TMP_Text primaryText;
    [SerializeField] private Button secondaryButton;
    [SerializeField] private TMP_Text secondaryText;

    public Button PrimaryButton => primaryButton;
    public Button SecondaryButton => secondaryButton;
    public RectTransform Box => box;
    public bool IsShown => gameObject.activeSelf;
    internal string Message => message.text;

    /// <summary>
    /// Logs an error when a part is missing.
    /// </summary>
    private void Awake()
    {
        if (box == null || message == null || answers == null || primaryButton == null || primaryText == null || secondaryButton == null || secondaryText == null)
        {
            Debug.LogError($"{name} needs its box, message, answers and both buttons with their texts.", this);
        }
    }

    /// <summary>
    /// Lays the box out again when the screen changes size.
    /// </summary>
    private void OnRectTransformDimensionsChange()
    {
        Place();
    }

    /// <summary>
    /// Shows <paramref name="text"/> (each sentence on a line of its own) with a primary button reading
    /// <paramref name="primaryLabel"/> and a secondary one reading <paramref name="secondaryLabel"/>; a null label hides
    /// its button.
    /// </summary>
    public void Show(string text, string primaryLabel, string secondaryLabel)
    {
        gameObject.SetActive(true);
        primaryButton.gameObject.SetActive(primaryLabel != null);
        secondaryButton.gameObject.SetActive(secondaryLabel != null);
        primaryText.text = primaryLabel ?? string.Empty;
        secondaryText.text = secondaryLabel ?? string.Empty;
        SetMessage(text);
    }

    /// <summary>
    /// Shows <paramref name="text"/> as the message, each sentence on a line of its own, and sizes the box to it.
    /// </summary>
    public void SetMessage(string text)
    {
        message.text = BySentence(text);
        Place();
    }

    /// <summary>
    /// Hides the dialog.
    /// </summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    /// <summary>
    /// <paramref name="text"/> with a line break after each full stop that a space follows, so each sentence starts on
    /// its own line.
    /// </summary>
    public static string BySentence(string text)
    {
        return string.IsNullOrEmpty(text) ? string.Empty : text.Replace(". ", ".\n");
    }

    /// <summary>
    /// While shown, sizes the box to its message's lines and its buttons, centres it on whole UI px, and stands the
    /// shown buttons centred along its foot.
    /// </summary>
    internal void Place()
    {
        if (box == null || !gameObject.activeInHierarchy || !(transform is RectTransform root))
        {
            return;
        }

        float textWidth = BoxWidth - Padding * 2f;
        float lines = Mathf.Max(1f, Mathf.Round(message.GetPreferredValues(message.text, textWidth, 0f).y / LineHeight));
        float height = Padding + lines * LineHeight + Padding + ButtonHeight + Padding;
        height += height % 2f;
        Rect area = root.rect;
        box.anchorMin = new Vector2(0f, 1f);
        box.anchorMax = new Vector2(0f, 1f);
        box.pivot = new Vector2(0f, 1f);
        box.sizeDelta = new Vector2(BoxWidth, height);
        box.anchoredPosition = new Vector2(Mathf.Floor((area.width - BoxWidth) * 0.5f), -Mathf.Floor((area.height - height) * 0.5f));
        RectTransform text = message.rectTransform;
        text.anchorMin = new Vector2(0f, 1f);
        text.anchorMax = new Vector2(0f, 1f);
        text.pivot = new Vector2(0f, 1f);
        text.sizeDelta = new Vector2(textWidth, lines * LineHeight);
        text.anchoredPosition = new Vector2(Padding, -Padding);
        bool both = primaryButton.gameObject.activeSelf && secondaryButton.gameObject.activeSelf;
        float width = both ? ButtonWidth * 2f + ButtonGap : ButtonWidth;
        answers.anchorMin = Vector2.zero;
        answers.anchorMax = Vector2.zero;
        answers.pivot = Vector2.zero;
        answers.sizeDelta = new Vector2(width, ButtonHeight);
        answers.anchoredPosition = new Vector2(Mathf.Floor((BoxWidth - width) * 0.5f), Padding);
    }
}
