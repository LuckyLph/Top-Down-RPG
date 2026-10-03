using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One passive status icon on the HUD: a frame in the status colour, its icon (or its abbreviation when it has
/// none), a radial fill that darkens as the status runs out, and a stack count above 1.
/// </summary>
[DisallowMultipleComponent]
public class StatusIconView : MonoBehaviour
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

    private void Awake()
    {
        if (frame == null || icon == null || label == null || elapsedFill == null || stacksText == null)
        {
            Debug.LogError($"{name} is missing status icon references.", this);
        }
    }

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

    public void Hide()
    {
        Shown = null;
        gameObject.SetActive(false);
    }

    internal void ConfigureReferences(Image frameImage, Image iconImage, TextMeshProUGUI abbreviation, Image fill, TextMeshProUGUI stacks)
    {
        frame = frameImage;
        icon = iconImage;
        label = abbreviation;
        elapsedFill = fill;
        stacksText = stacks;
    }

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
