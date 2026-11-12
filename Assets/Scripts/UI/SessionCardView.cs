using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The `SessionCard` component (Docs/UiRevamp.md, Phase 10), a variant of `Panel`: one saved game on the Saved games
/// screen, two lines (the place in Text, a joined copy's tag in TextMuted after it, then the time played and when it was
/// saved in TextMuted) and, at its right, its actions: Play, Host and Delete, Play hidden while the screen hosts. Only
/// the selected card shows its actions and its frame in Accent; the others' actions stay reachable by the arrows but
/// hidden and blind to the pointer, so moving onto one shows it. A click on the card raises <see cref="Clicked"/>.
/// Passive: the Saved games controller fills it, wires its actions and selects them.
/// </summary>
[DisallowMultipleComponent]
public sealed class SessionCardView : MonoBehaviour, IPointerClickHandler
{
    [SerializeField, Tooltip("The frame in Accent shown while the card is selected, over its frame in Border.")]
    private Image selectedFrame;
    [SerializeField] private TMP_Text placeText;
    [SerializeField, Tooltip("A joined copy's tag, after the place.")] private TMP_Text tagText;
    [SerializeField, Tooltip("The time played and when it was saved.")] private TMP_Text detailsText;
    [SerializeField, Tooltip("The actions, hidden on every card but the selected one.")] private CanvasGroup actions;
    [SerializeField] private Button playButton;
    [SerializeField] private Button hostButton;
    [SerializeField] private Button deleteButton;

    private readonly Button[] shown = new Button[3];

    /// <summary>
    /// Raised when the pointer clicks the card outside its actions.
    /// </summary>
    public event Action<SessionCardView> Clicked;

    public Button PlayButton => playButton;
    public Button HostButton => hostButton;
    public Button DeleteButton => deleteButton;
    public bool IsSelected { get; private set; }
    public bool IsHosting { get; private set; }
    public int ActionCount { get; private set; }
    internal TMP_Text PlaceText => placeText;
    internal TMP_Text TagText => tagText;
    internal TMP_Text DetailsText => detailsText;
    internal CanvasGroup Actions => actions;
    internal Image SelectedFrame => selectedFrame;

    /// <summary>
    /// Logs an error when a part is missing, and shows the card unselected with its three actions.
    /// </summary>
    private void Awake()
    {
        if (selectedFrame == null || placeText == null || tagText == null || detailsText == null || actions == null || playButton == null || hostButton == null
            || deleteButton == null)
        {
            Debug.LogError($"{name} is missing one of its session card references.", this);
            return;
        }

        SetHosting(IsHosting);
        SetSelected(false);
    }

    /// <summary>
    /// Shows <paramref name="place"/>, <paramref name="tag"/> after it (none when empty) and
    /// <paramref name="details"/> under them.
    /// </summary>
    public void Set(string place, string tag, string details)
    {
        placeText.text = place;
        detailsText.text = details;
        bool tagged = !string.IsNullOrEmpty(tag);
        tagText.gameObject.SetActive(tagged);
        tagText.text = tagged ? ContinueLine.Separator + tag : string.Empty;
        if (tagged)
        {
            RectTransform rect = tagText.rectTransform;
            float left = placeText.rectTransform.anchoredPosition.x + Mathf.Round(placeText.GetPreferredValues(place).x);
            rect.anchoredPosition = new Vector2(left, rect.anchoredPosition.y);
        }
    }

    /// <summary>
    /// Shows Host and Delete without Play while <paramref name="hosting"/>, else all three.
    /// </summary>
    public void SetHosting(bool hosting)
    {
        IsHosting = hosting;
        playButton.gameObject.SetActive(!hosting);
        int count = 0;
        if (!hosting)
        {
            shown[count++] = playButton;
        }

        shown[count++] = hostButton;
        shown[count++] = deleteButton;
        ActionCount = count;
    }

    /// <summary>
    /// The shown action at <paramref name="index"/>, from the left: Play, Host, Delete (Host, Delete while hosting).
    /// </summary>
    public Button Action(int index)
    {
        return shown[Mathf.Clamp(index, 0, ActionCount - 1)];
    }

    /// <summary>
    /// The index of <paramref name="target"/> among the shown actions; -1 when it is none of them.
    /// </summary>
    public int IndexOf(GameObject target)
    {
        for (int i = 0; i < ActionCount; i++)
        {
            if (shown[i].gameObject == target)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Frames the card in Accent and shows its actions while <paramref name="selected"/>; else hides them from sight and
    /// from the pointer, leaving them to the arrows.
    /// </summary>
    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        selectedFrame.enabled = selected;
        actions.alpha = selected ? 1f : 0f;
        actions.blocksRaycasts = selected;
    }

    /// <summary>
    /// A click on the card itself: raises <see cref="Clicked"/>.
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            Clicked?.Invoke(this);
        }
    }
}
