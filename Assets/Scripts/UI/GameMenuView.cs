using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Passive in-game menu (Docs/UiRevamp.md, Phase 6): one raised panel at the centre of the space between the notice band
/// and the command bar, holding its title ("Paused" or "Menu") and a column of buttons, Resume, Character, Party, Controls,
/// Options, Save game, Leave game and Quit to desktop, looping; in its place the Party page or the Controls page; a
/// scrim behind it all, dimming the game only while the game is paused; and a confirmation (question, confirm and
/// cancel). The component stays on an active object and shows or hides its panel. Whichever page shows is laid out and
/// placed on whole UI px whenever it changes or the canvas changes size.
/// </summary>
[DisallowMultipleComponent]
public class GameMenuView : MonoBehaviour
{
    public const float Width = 160f;
    public const float Header = 21f;
    public const float Footer = 6f;
    public const float NoticeBandBottom = 32f;
    public const float CommandBarTop = 68f;

    /// <summary>
    /// What the panel shows: the menu's buttons, or one of its pages.
    /// </summary>
    public enum Page
    {
        Menu,
        Party,
        Controls,
    }

    [SerializeField, Tooltip("The full-screen scrim holding everything, shown while the menu is open.")] private GameObject panel;
    [SerializeField, Tooltip("The scrim's own image, which dims the game only while it is paused.")] private Image scrim;
    [SerializeField, Tooltip("The menu's raised panel.")] private RectTransform menuBox;
    [SerializeField, Tooltip("The column of buttons inside it.")] private RectTransform buttons;
    [SerializeField] private TMP_Text title;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button characterButton;
    [SerializeField] private Button partyButton;
    [SerializeField] private Button controlsButton;
    [SerializeField] private Button optionsButton;
    [SerializeField] private Button saveButton;
    [SerializeField] private TMP_Text saveText;
    [SerializeField] private Button leaveButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private PartyPageView partyPage;
    [SerializeField] private ControlsPageView controlsPage;
    [SerializeField] private GameObject confirmPanel;
    [SerializeField] private TMP_Text confirmText;
    [SerializeField] private Button confirmYesButton;
    [SerializeField] private TMP_Text confirmYesText;
    [SerializeField] private Button confirmNoButton;

    private readonly Button[] looped = new Button[8];
    private Page shownPage;

    public Button ResumeButton => resumeButton;
    public Button CharacterButton => characterButton;
    public Button PartyButton => partyButton;
    public Button ControlsButton => controlsButton;
    public Button OptionsButton => optionsButton;
    public Button SaveButton => saveButton;
    public Button LeaveButton => leaveButton;
    public Button QuitButton => quitButton;
    public Button ConfirmYesButton => confirmYesButton;
    public Button ConfirmNoButton => confirmNoButton;
    public PartyPageView PartyPage => partyPage;
    public ControlsPageView ControlsPage => controlsPage;
    public bool IsShown => panel != null && panel.activeSelf;
    public bool IsConfirmShown => confirmPanel != null && confirmPanel.activeSelf;
    public Page ShownPage => shownPage;
    internal TMP_Text ConfirmText => confirmText;
    internal TMP_Text Title => title;
    internal TMP_Text SaveText => saveText;
    internal bool IsScrimShown => scrim != null && scrim.enabled;
    internal RectTransform MenuBox => menuBox;

    /// <summary>
    /// Logs an error when any panel, button, page or text reference is missing.
    /// </summary>
    private void Awake()
    {
        if (panel == null || scrim == null || menuBox == null || buttons == null || title == null || resumeButton == null
            || characterButton == null || partyButton == null || controlsButton == null || optionsButton == null || saveButton == null || saveText == null
            || leaveButton == null || quitButton == null || partyPage == null || controlsPage == null || confirmPanel == null
            || confirmText == null || confirmYesButton == null || confirmYesText == null || confirmNoButton == null)
        {
            Debug.LogError($"{name} is missing one of its in-game menu references.", this);
        }
    }

    /// <summary>
    /// Follows the pages' changes of size.
    /// </summary>
    private void OnEnable()
    {
        if (partyPage != null)
        {
            partyPage.Resized += Place;
        }

        if (controlsPage != null)
        {
            controlsPage.Resized += Place;
        }
    }

    /// <summary>
    /// Stops following the pages.
    /// </summary>
    private void OnDisable()
    {
        if (partyPage != null)
        {
            partyPage.Resized -= Place;
        }

        if (controlsPage != null)
        {
            controlsPage.Resized -= Place;
        }
    }

    /// <summary>
    /// Places the shown page again when the canvas changes size (a UI size step, the game's window).
    /// </summary>
    private void OnRectTransformDimensionsChange()
    {
        Place();
    }

    /// <summary>
    /// Shows or hides the menu; the component's own object stays active.
    /// </summary>
    public void SetShown(bool shown)
    {
        if (panel != null)
        {
            panel.SetActive(shown);
        }

        if (shown)
        {
            Place();
        }
    }

    /// <summary>
    /// Shows the menu's title, and dims the game behind it only when <paramref name="dimmed"/> (the game is paused).
    /// </summary>
    public void SetTitle(string text, bool dimmed)
    {
        if (title != null)
        {
            title.text = text;
        }

        if (scrim != null)
        {
            scrim.enabled = dimmed;
        }
    }

    /// <summary>
    /// Shows the Party button when <paramref name="party"/> (online) and the Save game button when
    /// <paramref name="save"/> (offline or hosting), and makes the shown buttons loop, top to bottom.
    /// </summary>
    public void SetButtons(bool party, bool save)
    {
        if (partyButton != null)
        {
            partyButton.gameObject.SetActive(party);
        }

        if (saveButton != null)
        {
            saveButton.gameObject.SetActive(save);
        }

        int count = 0;
        Add(resumeButton, ref count);
        Add(characterButton, ref count);
        Add(partyButton, ref count);
        Add(controlsButton, ref count);
        Add(optionsButton, ref count);
        Add(saveButton, ref count);
        Add(leaveButton, ref count);
        Add(quitButton, ref count);
        Loop(looped, count, true);
        Place();
    }

    /// <summary>
    /// Lets the Save game button be pressed or not, and shows <paramref name="label"/> on it.
    /// </summary>
    public void SetSave(bool interactable, string label)
    {
        if (saveButton != null)
        {
            saveButton.interactable = interactable;
        }

        if (saveText != null)
        {
            saveText.text = label;
        }
    }

    /// <summary>
    /// Shows <paramref name="page"/> in the panel's place: the menu's buttons, the Party page or the Controls page.
    /// </summary>
    public void ShowPage(Page page)
    {
        shownPage = page;
        if (menuBox != null)
        {
            menuBox.gameObject.SetActive(page == Page.Menu);
        }

        if (partyPage != null)
        {
            partyPage.gameObject.SetActive(page == Page.Party);
        }

        if (controlsPage != null)
        {
            controlsPage.gameObject.SetActive(page == Page.Controls);
        }

        Place();
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

    /// <summary>
    /// While the menu shows, lays the shown page out at its content's height, <see cref="Width"/> wide, and places it on
    /// whole UI px at the centre of the space between the notice band and the command bar's region, never above the
    /// canvas's top edge.
    /// </summary>
    internal void Place()
    {
        RectTransform box;
        RectTransform content;
        switch (shownPage)
        {
            case Page.Party:
                box = partyPage != null ? partyPage.Box : null;
                content = partyPage != null ? partyPage.Content : null;
                break;
            case Page.Controls:
                box = controlsPage != null ? controlsPage.Box : null;
                content = controlsPage != null ? controlsPage.Content : null;
                break;
            default:
                box = menuBox;
                content = buttons;
                break;
        }

        if (box == null || content == null || panel == null || !panel.activeInHierarchy || !(transform is RectTransform root))
        {
            return;
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        float contentHeight = Mathf.Ceil(LayoutUtility.GetPreferredHeight(content));
        content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, contentHeight);
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        float height = Header + contentHeight + Footer;
        Vector2 canvas = root.rect.size;
        float x = Mathf.Floor((canvas.x - Width) * 0.5f);
        float top = Mathf.Max(0f, Mathf.Floor((NoticeBandBottom + canvas.y - CommandBarTop - height) * 0.5f));
        box.anchorMin = new Vector2(0f, 1f);
        box.anchorMax = new Vector2(0f, 1f);
        box.pivot = new Vector2(0f, 1f);
        box.sizeDelta = new Vector2(Width, height);
        box.anchoredPosition = new Vector2(x, -top);
    }

    /// <summary>
    /// Makes the first <paramref name="count"/> of <paramref name="selectables"/> a column (or, when not
    /// <paramref name="vertical"/>, a row) that wraps around at both ends.
    /// </summary>
    internal static void Loop(Selectable[] selectables, int count, bool vertical)
    {
        for (int i = 0; i < count; i++)
        {
            Navigation navigation = new()
            {
                mode = Navigation.Mode.Explicit,
                wrapAround = true,
            };
            Selectable previous = selectables[(i + count - 1) % count];
            Selectable next = selectables[(i + 1) % count];
            if (vertical)
            {
                navigation.selectOnUp = previous;
                navigation.selectOnDown = next;
            }
            else
            {
                navigation.selectOnLeft = previous;
                navigation.selectOnRight = next;
                navigation.selectOnUp = previous;
                navigation.selectOnDown = next;
            }

            selectables[i].navigation = navigation;
        }
    }

    /// <summary>
    /// Adds <paramref name="button"/> to the loop when it shows.
    /// </summary>
    private void Add(Button button, ref int count)
    {
        if (button != null && button.gameObject.activeSelf)
        {
            looped[count++] = button;
        }
    }
}
