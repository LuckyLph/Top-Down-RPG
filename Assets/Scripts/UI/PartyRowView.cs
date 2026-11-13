using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A passive row of the in-game menu's Party page (Docs/UiRevamp.md, Phase 6), the <c>PartyRow</c> component: a
/// player's 5 px slot badge, "Player 2", a tag ("You", "Host") in a muted colour, and either a small health bar (a
/// <c>BarSmall</c>) or a line saying why there is none: "Down" in the danger colour, or "In another world" or "Joining"
/// muted. The badge takes its slot's colour role from its <see cref="UiTheme"/>. Each setter changes the row only when
/// what it shows changed, so a presenter can set it every frame.
/// </summary>
[DisallowMultipleComponent]
public class PartyRowView : MonoBehaviour
{
    [SerializeField] private Image badge;
    [SerializeField] private TMP_Text playerName;
    [SerializeField, Tooltip("\"You\" or \"Host\".")] private TMP_Text playerTag;
    [SerializeField, Tooltip("The muted line shown in place of the bar.")] private TMP_Text state;
    [SerializeField, Tooltip("\"Down\", shown in place of the bar.")] private GameObject down;
    [SerializeField, Tooltip("The bar's track, holding its fill and frame.")] private GameObject bar;
    [SerializeField] private Image fill;
    [SerializeField] private UiTheme theme;

    private int shownSlot = -1;
    private string shownTag;
    private string shownState;
    private bool shownDown;
    private float shownFraction = -1f;

    internal int ShownSlot => shownSlot;
    internal string ShownTag => shownTag;
    internal string ShownState => shownState;
    internal bool IsDownShown => shownDown;
    internal float ShownFraction => bar != null && bar.activeSelf ? shownFraction : -1f;
    internal bool IsShown => gameObject.activeSelf;
    internal Image Badge => badge;

    /// <summary>
    /// Logs an error when a part or the theme is missing.
    /// </summary>
    private void Awake()
    {
        if (badge == null || playerName == null || playerTag == null || state == null || down == null || bar == null || fill == null || theme == null)
        {
            Debug.LogError($"{name} needs a badge, a name, a tag, a state line, a Down line, a bar with its fill and a UI theme.", this);
        }
    }

    /// <summary>
    /// Shows the row for slot <paramref name="slot"/>: its badge in the slot's colour and its label, with
    /// <paramref name="tagText"/> after it (none for null or empty).
    /// </summary>
    public void ShowPlayer(int slot, string tagText)
    {
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        if (slot != shownSlot)
        {
            shownSlot = slot;
            if (badge != null && theme != null)
            {
                badge.color = theme.Colour(PlayerSlots.Role(slot));
            }

            if (playerName != null)
            {
                playerName.text = PlayerSlots.Label(slot);
            }
        }

        tagText ??= string.Empty;
        if (tagText != shownTag)
        {
            shownTag = tagText;
            if (playerTag != null)
            {
                playerTag.text = tagText;
            }
        }
    }

    /// <summary>
    /// Shows the bar filled to <paramref name="fraction"/>, in place of any line.
    /// </summary>
    public void ShowHealth(float fraction)
    {
        Show(null, false);
        fraction = Mathf.Clamp01(fraction);
        if (Mathf.Approximately(fraction, shownFraction))
        {
            return;
        }

        shownFraction = fraction;
        if (fill != null)
        {
            fill.fillAmount = fraction;
        }
    }

    /// <summary>
    /// Shows <paramref name="text"/>, muted, in place of the bar.
    /// </summary>
    public void ShowState(string text)
    {
        Show(text, false);
    }

    /// <summary>
    /// Shows "Down" in place of the bar.
    /// </summary>
    public void ShowDown()
    {
        Show(null, true);
    }

    /// <summary>
    /// Hides the row.
    /// </summary>
    public void Hide()
    {
        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Shows the muted <paramref name="text"/> (none for null), "Down" when <paramref name="isDown"/>, or else the bar.
    /// </summary>
    private void Show(string text, bool isDown)
    {
        if (text == shownState && isDown == shownDown && bar != null && bar.activeSelf == (text == null && !isDown))
        {
            return;
        }

        shownState = text;
        shownDown = isDown;
        if (state != null)
        {
            state.text = text ?? string.Empty;
            state.gameObject.SetActive(text != null);
        }

        if (down != null)
        {
            down.SetActive(isDown);
        }

        if (bar != null)
        {
            bar.SetActive(text == null && !isDown);
        }
    }

    /// <summary>
    /// Assigns the parts in code, for tests that build the row without a prefab.
    /// </summary>
    internal void ConfigureReferences(Image badgeImage, TMP_Text nameText, TMP_Text tagText, TMP_Text stateText, GameObject downLine, GameObject barRoot, Image fillImage, UiTheme uiTheme)
    {
        badge = badgeImage;
        playerName = nameText;
        playerTag = tagText;
        state = stateText;
        down = downLine;
        bar = barRoot;
        fill = fillImage;
        theme = uiTheme;
    }
}
