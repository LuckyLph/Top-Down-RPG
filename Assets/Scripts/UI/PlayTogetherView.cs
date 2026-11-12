using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Play together (Docs/UiRevamp.md, Phase 9), a full raised screen titled "Play together" over the main menu: a 472 UI
/// px block of two columns centred on whole UI px, as on the options screen.
/// <list type="bullet">
/// <item><b>Host a game:</b> Host and continue with the Continue line under it, Host a new game, and the invite block
/// (<see cref="InviteBlockView"/>).</item>
/// <item><b>Join a game:</b> the address field (the `FieldControl` component), Join, and "Recent" over up to five recent
/// hosts.</item>
/// </list>
/// Under them the notice line, each sentence on a line of its own, then Back; over it all the connecting dialog
/// (<see cref="DialogNoticeView"/>). Passive: <see cref="PlayTogetherController"/> fills it and wires its navigation.
/// </summary>
[DisallowMultipleComponent]
public class PlayTogetherView : MonoBehaviour
{
    public const float BlockWidth = 472f;
    public const float ColumnWidth = 232f;
    public const float ColumnGap = 8f;
    public const float ContentTop = 28f;
    public const float NoticeWidth = 360f;
    public const float NoticeBottom = 28f;
    public const float FooterBottom = 8f;
    public const float LineHeight = 11f;

    [SerializeField] private GameObject panel;
    [SerializeField, Tooltip("The host column, a vertical layout.")] private RectTransform hostColumn;
    [SerializeField] private Button hostContinueButton;
    [SerializeField] private TMP_Text continueLine;
    [SerializeField] private Button hostNewButton;
    [SerializeField] private InviteBlockView invite;
    [SerializeField, Tooltip("The join column, a vertical layout.")] private RectTransform joinColumn;
    [SerializeField] private TMP_InputField field;
    [SerializeField] private Button joinButton;
    [SerializeField] private GameObject recentLabel;
    [SerializeField] private Button[] recentButtons = new Button[0];
    [SerializeField] private TMP_Text[] recentTexts = new TMP_Text[0];
    [SerializeField] private TMP_Text notice;
    [SerializeField] private Button backButton;
    [SerializeField] private DialogNoticeView dialog;

    public Button HostContinueButton => hostContinueButton;
    public Button HostNewButton => hostNewButton;
    public InviteBlockView Invite => invite;
    public TMP_InputField Field => field;
    public Button JoinButton => joinButton;
    public Button BackButton => backButton;
    public DialogNoticeView Dialog => dialog;
    public RectTransform HostColumn => hostColumn;
    public RectTransform JoinColumn => joinColumn;
    public TMP_Text Notice => notice;
    public int RecentCapacity => recentButtons != null ? recentButtons.Length : 0;
    public bool IsShown => panel != null && panel.activeSelf;
    internal string ContinueText => continueLine.text;

    /// <summary>
    /// Logs an error when a part is missing or the recent hosts' buttons and texts differ in number.
    /// </summary>
    private void Awake()
    {
        if (panel == null || hostColumn == null || hostContinueButton == null || continueLine == null || hostNewButton == null || invite == null
            || joinColumn == null || field == null || joinButton == null || recentLabel == null || recentButtons.Length == 0 || recentTexts.Length != recentButtons.Length
            || notice == null || backButton == null || dialog == null)
        {
            Debug.LogError($"{name} is missing one of its Play together references.", this);
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
    /// The recent host button at <paramref name="index"/>.
    /// </summary>
    public Button RecentButton(int index)
    {
        return recentButtons[index];
    }

    /// <summary>
    /// The host shown on the recent host button at <paramref name="index"/>.
    /// </summary>
    public string RecentHost(int index)
    {
        return recentTexts[index].text;
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
    /// Shows Host and continue with <paramref name="line"/> under it, wrapping to as many lines as it needs, or hides
    /// both for null.
    /// </summary>
    public void SetContinue(string line)
    {
        hostContinueButton.gameObject.SetActive(line != null);
        continueLine.gameObject.SetActive(line != null);
        continueLine.text = line ?? string.Empty;
        if (line != null && continueLine.TryGetComponent(out LayoutElement element))
        {
            float width = ColumnWidth - continueLine.margin.x - continueLine.margin.z;
            element.preferredHeight = Mathf.Max(1f, Mathf.Round(continueLine.GetPreferredValues(line, width, 0f).y / LineHeight)) * LineHeight + 2f;
        }
    }

    /// <summary>
    /// Shows <paramref name="hosts"/> on the recent host buttons, at most <see cref="RecentCapacity"/>, hiding the rest
    /// and "Recent" without any; returns how many show.
    /// </summary>
    public int SetRecent(IReadOnlyList<string> hosts)
    {
        int count = Mathf.Min(hosts.Count, RecentCapacity);
        for (int i = 0; i < RecentCapacity; i++)
        {
            bool shown = i < count;
            recentButtons[i].gameObject.SetActive(shown);
            recentTexts[i].text = shown ? hosts[i] : string.Empty;
        }

        recentLabel.SetActive(count > 0);
        return count;
    }

    /// <summary>
    /// Shows <paramref name="text"/> on the notice line, each sentence on a line of its own, or clears it.
    /// </summary>
    public void SetNotice(string text)
    {
        notice.text = DialogNoticeView.BySentence(text);
    }

    /// <summary>
    /// While the screen shows, lays it out for the canvas: the two columns side by side in a block centred on whole UI
    /// px, from <see cref="ContentTop"/> down; the notice line centred <see cref="NoticeBottom"/> up; Back centred
    /// <see cref="FooterBottom"/> up.
    /// </summary>
    internal void Place()
    {
        if (panel == null || !panel.activeInHierarchy || !(transform is RectTransform root))
        {
            return;
        }

        Rect area = root.rect;
        float left = Mathf.Floor((area.width - BlockWidth) * 0.5f);
        float height = area.height - ContentTop - NoticeBottom;
        SetTopLeft(hostColumn, left, ContentTop, ColumnWidth, height);
        SetTopLeft(joinColumn, left + ColumnWidth + ColumnGap, ContentTop, ColumnWidth, height);
        SetBottomCentre(notice.rectTransform, area.width, NoticeBottom);
        SetBottomCentre((RectTransform)backButton.transform, area.width, FooterBottom);
        LayoutRebuilder.ForceRebuildLayoutImmediate(hostColumn);
        LayoutRebuilder.ForceRebuildLayoutImmediate(joinColumn);
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
