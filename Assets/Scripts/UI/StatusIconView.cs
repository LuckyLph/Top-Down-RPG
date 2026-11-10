using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One passive status icon on the HUD: a frame in the status colour, its icon (or its abbreviation when it has
/// none), a radial fill that darkens as the status runs out, and a stack count above 1. Its frame is a raycast target,
/// so a click on a shown icon never reaches the world. As an <see cref="ITooltipSource"/> it gives its status's name
/// and description.
/// </summary>
[DisallowMultipleComponent]
public class StatusIconView : MonoBehaviour, ITooltipSource
{
    private const int MaxShownStacks = 99;

    private static readonly string[] StacksText = BuildStacksText();

    [SerializeField] private Image frame;
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private Image elapsedFill;
    [SerializeField] private TextMeshProUGUI stacksText;
    [SerializeField, Range(0f, 1f)] private float frameAlpha = 0.9f;

    internal Image Frame => frame;
    internal Image Icon => icon;
    internal TextMeshProUGUI Label => label;
    internal Image ElapsedFill => elapsedFill;
    internal TextMeshProUGUI StacksLabel => stacksText;
    internal StatusEffectDefinition Shown { get; private set; }

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
        gameObject.SetActive(false);
    }

    /// <summary>
    /// The shown status's name and description; false while the icon shows none.
    /// </summary>
    public bool TryGetTooltip(out string title, out string description)
    {
        title = Shown != null ? Shown.DisplayName : null;
        description = Shown != null ? Shown.Description : null;
        return Shown != null;
    }

    /// <summary>
    /// Assigns the icon's images and texts in code, for tests that build it without a prefab.
    /// </summary>
    internal void ConfigureReferences(Image frameImage, Image iconImage, TextMeshProUGUI abbreviation, Image fill, TextMeshProUGUI stacks)
    {
        frame = frameImage;
        icon = iconImage;
        label = abbreviation;
        elapsedFill = fill;
        stacksText = stacks;
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
