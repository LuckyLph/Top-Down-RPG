using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A passive party frame (Docs/UiRevamp.md, Phase 5), the <c>PartyFrame</c> component: another player's slot badge with
/// its number, "Player 2", and either a health bar with up to four debuff pips or a line saying why there is none
/// ("Respawning in 2", "In another world", "Joining"). The badge takes its slot's colour role from its
/// <see cref="UiTheme"/>. Each setter changes the frame only when what it shows changed, so a presenter can set it every
/// frame without rebuilding the canvas.
/// </summary>
[DisallowMultipleComponent]
public class PartyFrameView : MonoBehaviour
{
    [SerializeField] private Image badge;
    [SerializeField] private TMP_Text number;
    [SerializeField] private TMP_Text playerName;
    [SerializeField, Tooltip("The line shown in place of the bar.")] private TMP_Text state;
    [SerializeField, Tooltip("The bar's track, holding its fill.")] private GameObject bar;
    [SerializeField] private Image fill;
    [SerializeField, Tooltip("The debuff pips, from the frame's right edge leftwards.")] private Image[] pips = new Image[0];
    [SerializeField] private UiTheme theme;

    private int shownSlot = -1;
    private string shownState;
    private float shownFraction = -1f;
    private int shownPips = -1;
    private readonly Color[] shownColours = new Color[4];

    internal int ShownSlot => shownSlot;
    internal string ShownState => shownState;
    internal float ShownFraction => shownFraction;
    internal int ShownPips => shownPips;
    internal bool IsShown => gameObject.activeSelf;
    internal Image Badge => badge;
    internal TMP_Text State => state;
    internal Image Fill => fill;
    internal Image[] Pips => pips;

    /// <summary>
    /// Logs an error when a part or the theme is missing.
    /// </summary>
    private void Awake()
    {
        if (badge == null || number == null || playerName == null || state == null || bar == null || fill == null || pips == null || theme == null)
        {
            Debug.LogError($"{name} needs a badge, its number, a name, a state line, a bar with its fill, pips and a UI theme.", this);
        }
    }

    /// <summary>
    /// Shows the frame for slot <paramref name="slot"/>: its badge in the slot's colour, its number and its label.
    /// </summary>
    public void ShowSlot(int slot)
    {
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        if (slot == shownSlot)
        {
            return;
        }

        shownSlot = slot;
        if (badge != null && theme != null)
        {
            badge.color = theme.Colour(PlayerSlots.Role(slot));
        }

        if (number != null)
        {
            number.text = PlayerSlots.IsSlot(slot) ? DigitOf(slot) : string.Empty;
        }

        if (playerName != null)
        {
            playerName.text = PlayerSlots.Label(slot);
        }
    }

    /// <summary>
    /// Shows the health bar filled to <paramref name="fraction"/>, in place of any state line.
    /// </summary>
    public void ShowHealth(float fraction)
    {
        SetState(null);
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
    /// Shows <paramref name="text"/> in place of the bar and its pips.
    /// </summary>
    public void ShowState(string text)
    {
        SetState(text);
        ShowDebuffs(shownColours, 0);
    }

    /// <summary>
    /// Shows the first <paramref name="count"/> of <paramref name="colours"/> as debuff pips, from the right, and hides
    /// the rest.
    /// </summary>
    public void ShowDebuffs(Color[] colours, int count)
    {
        count = pips != null ? Mathf.Clamp(count, 0, Mathf.Min(pips.Length, shownColours.Length)) : 0;
        bool same = count == shownPips;
        for (int i = 0; same && i < count; i++)
        {
            same = shownColours[i] == colours[i];
        }

        if (same)
        {
            return;
        }

        shownPips = count;
        for (int i = 0; pips != null && i < pips.Length; i++)
        {
            Image pip = pips[i];
            if (pip == null)
            {
                continue;
            }

            bool shown = i < count;
            if (shown)
            {
                shownColours[i] = colours[i];
                pip.color = colours[i];
            }

            if (pip.enabled != shown)
            {
                pip.enabled = shown;
            }
        }
    }

    /// <summary>
    /// Hides the frame.
    /// </summary>
    public void Hide()
    {
        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Assigns the parts and the theme in code, for tests that build the frame without a prefab.
    /// </summary>
    internal void ConfigureReferences(Image badgeImage, TMP_Text numberText, TMP_Text nameText, TMP_Text stateText, GameObject barObject, Image fillImage, Image[] pipImages, UiTheme uiTheme)
    {
        badge = badgeImage;
        number = numberText;
        playerName = nameText;
        state = stateText;
        bar = barObject;
        fill = fillImage;
        pips = pipImages;
        theme = uiTheme;
    }

    /// <summary>
    /// Shows <paramref name="text"/> on the state line in place of the bar, or the bar for null.
    /// </summary>
    private void SetState(string text)
    {
        if (ReferenceEquals(text, shownState) && (state == null || state.gameObject.activeSelf == (text != null)))
        {
            return;
        }

        shownState = text;
        bool line = text != null;
        if (state != null)
        {
            if (line)
            {
                state.text = text;
            }

            state.gameObject.SetActive(line);
        }

        if (bar != null)
        {
            bar.SetActive(!line);
        }

        if (line)
        {
            shownFraction = -1f;
        }
    }

    /// <summary>
    /// The digit of slot <paramref name="slot"/>, built once per slot.
    /// </summary>
    private static string DigitOf(int slot)
    {
        switch (slot)
        {
            case 1:
                return "1";
            case 2:
                return "2";
            case 3:
                return "3";
            default:
                return "4";
        }
    }
}
