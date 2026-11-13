using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Saved games screen (Docs/UiRevamp.md, Phase 10), a full raised screen over the main menu titled "Saved games", or
/// "Host a saved game" when opened from Play together: a scroll list of cards (<see cref="SessionCardView"/>) in a 472 UI
/// px block centred on whole UI px, as on the options screen, its scrollbar in the 6 UI px the cards leave it; under it
/// the notice line, then Back; over it all the Delete confirmation (the `Dialog` component), its box as tall as its
/// question. Passive: <see cref="SavedGamesController"/> fills it, wires its navigation and selects; the view keeps a card
/// per save, made from its prefab as the list first needs it and kept for the next opening.
/// </summary>
[DisallowMultipleComponent]
public class SavedGamesView : MonoBehaviour
{
    public const float BlockWidth = 472f;
    public const float ContentTop = 28f;
    public const float ListBottom = 52f;
    public const float NoticeBottom = 28f;
    public const float FooterBottom = 8f;
    public const float LineHeight = 11f;
    public const float ConfirmWidth = 240f;
    public const float ConfirmPadding = 8f;
    public const float ButtonHeight = 16f;

    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text title;
    [SerializeField, Tooltip("The scroll list holding the cards.")] private ScrollRect scroll;
    [SerializeField, Tooltip("The scroll list's column of cards.")] private RectTransform cards;
    [SerializeField] private SessionCardView cardPrefab;
    [SerializeField] private TMP_Text notice;
    [SerializeField] private Button backButton;
    [SerializeField, Tooltip("The Delete confirmation, a Dialog.")] private GameObject confirmPanel;
    [SerializeField] private RectTransform confirmBox;
    [SerializeField] private TMP_Text confirmText;
    [SerializeField] private Button confirmYesButton;
    [SerializeField] private Button confirmNoButton;

    private readonly List<SessionCardView> made = new();

    public Button BackButton => backButton;
    public Button ConfirmYesButton => confirmYesButton;
    public Button ConfirmNoButton => confirmNoButton;
    public ScrollRect Scroll => scroll;
    public bool IsShown => panel != null && panel.activeSelf;
    public bool IsConfirmShown => confirmPanel != null && confirmPanel.activeSelf;
    public int CardCount { get; private set; }
    internal TMP_Text Title => title;
    internal TMP_Text Notice => notice;
    internal TMP_Text ConfirmText => confirmText;
    internal RectTransform ConfirmBox => confirmBox;
    internal RectTransform List => (RectTransform)scroll.transform;

    /// <summary>
    /// Raised when a card is made, so whoever runs the screen wires it once.
    /// </summary>
    public event System.Action<SessionCardView> CardMade;

    /// <summary>
    /// Logs an error when a part is missing.
    /// </summary>
    private void Awake()
    {
        if (panel == null || title == null || scroll == null || cards == null || cardPrefab == null || notice == null || backButton == null
            || confirmPanel == null || confirmBox == null || confirmText == null || confirmYesButton == null || confirmNoButton == null)
        {
            Debug.LogError($"{name} is missing one of its Saved games references.", this);
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
    /// The card at <paramref name="index"/>, from the top.
    /// </summary>
    public SessionCardView Card(int index)
    {
        return made[index];
    }

    /// <summary>
    /// Shows <paramref name="count"/> cards, making those the list does not have yet, and hides the rest.
    /// </summary>
    public void SetCardCount(int count)
    {
        while (made.Count < count)
        {
            SessionCardView card = Instantiate(cardPrefab, cards);
            card.name = $"{cardPrefab.name} {made.Count + 1}";
            made.Add(card);
            CardMade?.Invoke(card);
        }

        for (int i = 0; i < made.Count; i++)
        {
            made[i].gameObject.SetActive(i < count);
        }

        CardCount = count;
        LayoutRebuilder.ForceRebuildLayoutImmediate(cards);
    }

    /// <summary>
    /// Shows <paramref name="text"/> as the title.
    /// </summary>
    public void SetTitle(string text)
    {
        title.text = text;
    }

    /// <summary>
    /// Shows <paramref name="text"/> on the notice line, each sentence on a line of its own, or clears it.
    /// </summary>
    public void SetNotice(string text)
    {
        notice.text = DialogNoticeView.BySentence(text);
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
    /// Shows the Delete confirmation asking <paramref name="question"/>, its box sized to its lines.
    /// </summary>
    public void ShowConfirm(string question)
    {
        confirmText.text = question;
        confirmPanel.SetActive(true);
        PlaceConfirm();
    }

    /// <summary>
    /// Hides the Delete confirmation.
    /// </summary>
    public void HideConfirm()
    {
        confirmPanel.SetActive(false);
    }

    /// <summary>
    /// Scrolls the list by the least that shows <paramref name="card"/> whole.
    /// </summary>
    public void Reveal(SessionCardView card)
    {
        ScrollIntoView.Reveal(scroll, (RectTransform)card.transform);
    }

    /// <summary>
    /// While the screen shows, lays it out for the canvas: the list in the block centred on whole UI px, from
    /// <see cref="ContentTop"/> down to <see cref="ListBottom"/> above the bottom; the notice line centred
    /// <see cref="NoticeBottom"/> up; Back centred <see cref="FooterBottom"/> up; and the confirmation's box.
    /// </summary>
    internal void Place()
    {
        if (panel == null || !panel.activeInHierarchy || !(transform is RectTransform root))
        {
            return;
        }

        Rect area = root.rect;
        RectTransform list = List;
        list.anchorMin = new Vector2(0f, 1f);
        list.anchorMax = new Vector2(0f, 1f);
        list.pivot = new Vector2(0f, 1f);
        list.anchoredPosition = new Vector2(Mathf.Floor((area.width - BlockWidth) * 0.5f), -ContentTop);
        list.sizeDelta = new Vector2(BlockWidth, Mathf.Round(area.height - ContentTop - ListBottom));
        SetBottomCentre(notice.rectTransform, area.width, NoticeBottom);
        SetBottomCentre((RectTransform)backButton.transform, area.width, FooterBottom);
        LayoutRebuilder.ForceRebuildLayoutImmediate(cards);
        PlaceConfirm();
    }

    /// <summary>
    /// While the confirmation shows, sizes its box to the question's lines and its buttons, rounded up to an even height,
    /// and centres it on whole UI px.
    /// </summary>
    private void PlaceConfirm()
    {
        if (confirmPanel == null || !confirmPanel.activeInHierarchy || !(transform is RectTransform root))
        {
            return;
        }

        float textWidth = ConfirmWidth - ConfirmPadding * 2f;
        float lines = Mathf.Max(1f, Mathf.Round(confirmText.GetPreferredValues(confirmText.text, textWidth, 0f).y / LineHeight));
        float height = ConfirmPadding + lines * LineHeight + ConfirmPadding + ButtonHeight + ConfirmPadding;
        height += height % 2f;
        Rect area = root.rect;
        confirmBox.anchorMin = new Vector2(0f, 1f);
        confirmBox.anchorMax = new Vector2(0f, 1f);
        confirmBox.pivot = new Vector2(0f, 1f);
        confirmBox.sizeDelta = new Vector2(ConfirmWidth, height);
        confirmBox.anchoredPosition = new Vector2(Mathf.Floor((area.width - ConfirmWidth) * 0.5f), -Mathf.Floor((area.height - height) * 0.5f));
        RectTransform text = confirmText.rectTransform;
        text.anchorMin = new Vector2(0f, 1f);
        text.anchorMax = new Vector2(0f, 1f);
        text.pivot = new Vector2(0f, 1f);
        text.sizeDelta = new Vector2(textWidth, lines * LineHeight);
        text.anchoredPosition = new Vector2(ConfirmPadding, -ConfirmPadding);
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
}
