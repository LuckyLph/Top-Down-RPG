using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A passive health bar on a screen (Docs/UiRevamp.md, Decisions: Health), the <c>Bar</c> component: the fill in its
/// role (<c>HealthPlayer</c> for players), "72 / 100" outlined over it, a tick every so many HP down from the top of
/// the fill, the part just lost in <c>HealthLost</c> under the fill, and the frame in <c>Danger</c> while the health is
/// low, at a strength the presenter pulses. The text is built and the ticks laid out only when what they show changes;
/// ticks come from a hidden template and are kept for reuse. Colours are read from its <see cref="UiTheme"/> when it
/// wakes.
/// </summary>
[DisallowMultipleComponent]
public class HealthBarView : MonoBehaviour
{
    private const string TextFormat = "{0} / {1}";
    private const string UnknownText = "-- / --";

    [SerializeField] private Image fill;
    [SerializeField, Tooltip("The part just lost, under the fill, in HealthLost.")]
    private Image lost;
    [SerializeField, Tooltip("The 1 px frame, in Border, and in Danger while the health is low.")]
    private Image frame;
    [SerializeField] private TextMeshProUGUI text;
    [SerializeField, Tooltip("A hidden tick, copied once for each tick the bar shows, under a parent as wide as the fill.")]
    private Image tickTemplate;
    [SerializeField] private UiTheme theme;

    private readonly List<Image> ticks = new();
    private Color frameColor = Color.white;
    private Color lowColor = Color.red;
    private int shownCurrent = -1;
    private int shownMax = -1;
    private int shownTickHealth = -1;
    private int shownTicks;

    internal Image Fill => fill;
    internal Image Lost => lost;
    internal Image Frame => frame;
    internal TextMeshProUGUI Text => text;
    internal int TickCount => shownTicks;
    internal bool IsLow { get; private set; }

    /// <summary>
    /// The tick at <paramref name="index"/>, for tests.
    /// </summary>
    internal Image Tick(int index)
    {
        return index >= 0 && index < shownTicks ? ticks[index] : null;
    }

    /// <summary>
    /// Reads the frame's colours from the theme. Logs an error when the theme or any reference is missing.
    /// </summary>
    private void Awake()
    {
        if (theme == null || fill == null || lost == null || frame == null || text == null || tickTemplate == null)
        {
            Debug.LogError($"{name} is missing its theme or health bar references.", this);
        }

        ReadTheme();
    }

    /// <summary>
    /// Fills the bar to <paramref name="current"/> over <paramref name="max"/>, shows both, and puts a tick every
    /// <paramref name="tickHealth"/> HP, rebuilding the text only when the numbers changed and the ticks only when the
    /// maximum or the spacing did. Values are clamped so the max is at least 1 and the current lies between 0 and it.
    /// </summary>
    public void SetHealth(int current, int max, int tickHealth)
    {
        int clampedMax = Mathf.Max(1, max);
        int clampedCurrent = Mathf.Clamp(current, 0, clampedMax);
        if (clampedMax != shownMax || tickHealth != shownTickHealth)
        {
            LayoutTicks(clampedMax, Mathf.Max(1, tickHealth));
        }

        if (clampedCurrent == shownCurrent && clampedMax == shownMax)
        {
            return;
        }

        shownCurrent = clampedCurrent;
        shownMax = clampedMax;
        if (fill != null)
        {
            fill.fillAmount = (float)clampedCurrent / clampedMax;
        }

        if (text != null)
        {
            text.text = string.Format(TextFormat, clampedCurrent, clampedMax);
        }
    }

    /// <summary>
    /// Shows the part of the bar from the fill's end up to <paramref name="fraction"/> of it as just lost.
    /// </summary>
    public void ShowLoss(float fraction)
    {
        if (lost != null)
        {
            lost.fillAmount = Mathf.Clamp01(fraction);
            lost.enabled = true;
        }
    }

    /// <summary>
    /// Hides the part just lost.
    /// </summary>
    public void HideLoss()
    {
        if (lost != null)
        {
            lost.enabled = false;
        }
    }

    /// <summary>
    /// Turns the frame Danger at <paramref name="strength"/> (0 to 1, its opacity) while <paramref name="low"/>, and
    /// back to Border otherwise.
    /// </summary>
    public void SetLow(bool low, float strength)
    {
        IsLow = low;
        if (frame == null)
        {
            return;
        }

        Color color = lowColor;
        color.a *= Mathf.Clamp01(strength);
        frame.color = low ? color : frameColor;
    }

    /// <summary>
    /// Empties the bar, shows unknown health, hides the loss and the ticks and ends the low state, for when there is
    /// no player.
    /// </summary>
    public void Clear()
    {
        shownCurrent = -1;
        shownMax = -1;
        shownTickHealth = -1;
        if (fill != null)
        {
            fill.fillAmount = 0f;
        }

        if (text != null)
        {
            text.text = UnknownText;
        }

        HideLoss();
        SetLow(false, 1f);
        ShowTicks(0);
    }

    /// <summary>
    /// Assigns the bar's images, text, tick template and theme in code and reads the theme, for tests that build the
    /// bar without a prefab.
    /// </summary>
    internal void ConfigureReferences(Image fillImage, Image lostImage, Image frameImage, TextMeshProUGUI label, Image tick, UiTheme uiTheme)
    {
        fill = fillImage;
        lost = lostImage;
        frame = frameImage;
        text = label;
        tickTemplate = tick;
        theme = uiTheme;
        ReadTheme();
    }

    /// <summary>
    /// Puts a tick at every multiple of <paramref name="tickHealth"/> below <paramref name="max"/>, anchored at its
    /// share of the bar's width, copying the template for ticks it has not made yet.
    /// </summary>
    private void LayoutTicks(int max, int tickHealth)
    {
        shownTickHealth = tickHealth;
        if (tickTemplate == null)
        {
            return;
        }

        int count = (max - 1) / tickHealth;
        while (ticks.Count < count)
        {
            Image tick = Instantiate(tickTemplate, tickTemplate.transform.parent);
            tick.name = $"Tick{ticks.Count + 1}";
            ticks.Add(tick);
        }

        for (int i = 0; i < count; i++)
        {
            RectTransform rect = ticks[i].rectTransform;
            float share = (float)((i + 1) * tickHealth) / max;
            rect.anchorMin = new Vector2(share, 1f);
            rect.anchorMax = new Vector2(share, 1f);
            rect.anchoredPosition = Vector2.zero;
        }

        ShowTicks(count);
    }

    /// <summary>
    /// Shows the first <paramref name="count"/> ticks and hides the rest.
    /// </summary>
    private void ShowTicks(int count)
    {
        shownTicks = count;
        for (int i = 0; i < ticks.Count; i++)
        {
            ticks[i].gameObject.SetActive(i < count);
        }
    }

    /// <summary>
    /// Reads the frame's Border and Danger colours from the theme, when there is one.
    /// </summary>
    private void ReadTheme()
    {
        if (theme == null)
        {
            return;
        }

        frameColor = theme.Colour(UiColour.Border);
        lowColor = theme.Colour(UiColour.Danger);
    }
}
