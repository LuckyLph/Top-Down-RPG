using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One passive status icon on the HUD: a frame in the status colour, its icon (or its abbreviation when it has
/// none), a radial fill that darkens as the status runs out, and a stack count above 1; or, as the last icon of a full
/// group, "+n" for the statuses that do not fit, on the theme's Surface (Docs/UiRevamp.md, Decisions: Statuses). A
/// debuff's icon (<c>StatusIconDebuff</c>) frames it in Danger with a notch in its top right corner. Its frame is a
/// raycast target, so a click on a shown icon never reaches the world. As an <see cref="ITooltipSource"/> it gives its
/// status's name and description, or the names of the statuses "+n" stands for.
/// </summary>
[DisallowMultipleComponent]
public class StatusIconView : MonoBehaviour, ITooltipSource
{
    private const int MaxShownStacks = 99;
    private const int MaxShownMore = 99;

    private static readonly string[] StacksText = BuildStacksText();
    private static readonly string[] MoreText = BuildMoreText();
    private static readonly string[] MoreTitles = BuildMoreTitles();

    [SerializeField] private Image frame;
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private Image elapsedFill;
    [SerializeField] private TextMeshProUGUI stacksText;
    [SerializeField, Range(0f, 1f)] private float frameAlpha = 0.9f;
    [SerializeField, Tooltip("The theme whose Surface frames a \"+n\" icon.")] private UiTheme theme;

    private int more;
    private string moreNames;

    internal Image Frame => frame;
    internal Image Icon => icon;
    internal TextMeshProUGUI Label => label;
    internal Image ElapsedFill => elapsedFill;
    internal TextMeshProUGUI StacksLabel => stacksText;
    internal StatusEffectDefinition Shown { get; private set; }
    internal int More => more;

    public event Action TooltipChanged;

    /// <summary>
    /// Logs an error when any image or text reference is missing.
    /// </summary>
    private void Awake()
    {
        if (frame == null || icon == null || label == null || elapsedFill == null || stacksText == null)
        {
            Debug.LogError($"{name} is missing status icon references.", this);
        }
    }

    /// <summary>
    /// Activates the icon for <paramref name="definition"/>: frame in its colour, its icon or else its
    /// abbreviation, and <paramref name="stacks"/> when above 1 (capped at <see cref="MaxShownStacks"/>).
    /// </summary>
    public void Show(StatusEffectDefinition definition, int stacks)
    {
        Shown = definition;
        more = 0;
        moreNames = null;
        Sprite sprite = definition.Icon;
        if (frame != null)
        {
            Color color = definition.Color;
            color.a = frameAlpha;
            frame.color = color;
        }

        if (icon != null)
        {
            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }

        if (label != null)
        {
            label.text = sprite == null ? definition.Abbreviation : string.Empty;
        }

        if (stacksText != null)
        {
            stacksText.text = StacksText[Mathf.Clamp(stacks, 0, MaxShownStacks)];
        }

        gameObject.SetActive(true);
        TooltipChanged?.Invoke();
    }

    /// <summary>
    /// Activates the icon as "+<paramref name="count"/>" on the theme's Surface, for the statuses of its group that do not
    /// fit, whose names its tooltip lists (<paramref name="names"/>); no icon, stacks or fill.
    /// </summary>
    public void ShowMore(int count, string names)
    {
        Shown = null;
        more = Mathf.Clamp(count, 1, MaxShownMore);
        moreNames = names;
        if (frame != null)
        {
            frame.color = theme != null ? theme.Colour(UiColour.Surface) : Color.black;
        }

        if (icon != null)
        {
            icon.sprite = null;
            icon.enabled = false;
        }

        if (label != null)
        {
            label.text = MoreText[more];
        }

        if (stacksText != null)
        {
            stacksText.text = string.Empty;
        }

        SetElapsed(0f);
        gameObject.SetActive(true);
        TooltipChanged?.Invoke();
    }

    /// <summary>
    /// Darkens <paramref name="fraction"/> of the icon, from 0 (just applied) to 1 (about to end).
    /// </summary>
    public void SetElapsed(float fraction)
    {
        if (elapsedFill != null)
        {
            elapsedFill.fillAmount = Mathf.Clamp01(fraction);
        }
    }

    /// <summary>
    /// Deactivates the icon and forgets the status it showed.
    /// </summary>
    public void Hide()
    {
        Shown = null;
        more = 0;
        moreNames = null;
        gameObject.SetActive(false);
    }

    /// <summary>
    /// The shown status's name and description, with no numbers, or for "+n" how many more and their names; false
    /// while the icon shows neither.
    /// </summary>
    public bool TryGetTooltip(out string title, out string details, out string description)
    {
        details = null;
        if (more > 0)
        {
            title = MoreTitles[more];
            description = moreNames;
            return true;
        }

        title = Shown != null ? Shown.DisplayName : null;
        description = Shown != null ? Shown.Description : null;
        return Shown != null;
    }

    /// <summary>
    /// Assigns the icon's images and texts in code, for tests that build it without a prefab.
    /// </summary>
    internal void ConfigureReferences(Image frameImage, Image iconImage, TextMeshProUGUI abbreviation, Image fill, TextMeshProUGUI stacks, UiTheme uiTheme = null)
    {
        theme = uiTheme;
        frame = frameImage;
        icon = iconImage;
        label = abbreviation;
        elapsedFill = fill;
        stacksText = stacks;
    }

    /// <summary>
    /// Precomputes the "+n" labels from 0 to <see cref="MaxShownMore"/> so showing one never allocates.
    /// </summary>
    private static string[] BuildMoreText()
    {
        string[] text = new string[MaxShownMore + 1];
        for (int i = 0; i < text.Length; i++)
        {
            text[i] = $"+{i}";
        }

        return text;
    }

    /// <summary>
    /// Precomputes the "+n" tooltips' titles, "2 more", from 0 to <see cref="MaxShownMore"/>.
    /// </summary>
    private static string[] BuildMoreTitles()
    {
        string[] text = new string[MaxShownMore + 1];
        for (int i = 0; i < text.Length; i++)
        {
            text[i] = $"{i} more";
        }

        return text;
    }

    /// <summary>
    /// Precomputes the stack labels from 0 to <see cref="MaxShownStacks"/> so showing stacks never allocates;
    /// 0 and 1 are empty strings.
    /// </summary>
    private static string[] BuildStacksText()
    {
        string[] text = new string[MaxShownStacks + 1];
        for (int i = 0; i < text.Length; i++)
        {
            text[i] = i > 1 ? i.ToString() : string.Empty;
        }

        return text;
    }
}
