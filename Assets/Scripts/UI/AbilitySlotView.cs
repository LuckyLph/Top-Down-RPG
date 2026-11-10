using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One passive slot of the ability bar: icon (or the ability's name when it has none, or an empty state),
/// radial cooldown fill with whole seconds remaining in its top left corner, key label in its top right and a failure
/// flash. Its frame and flash take their colours from the <see cref="UiTheme"/>'s Surface, SurfaceEmpty and Danger
/// roles, read once when it wakes. As an <see cref="ITooltipSource"/> it gives its ability's name and description.
/// </summary>
[DisallowMultipleComponent]
public class AbilitySlotView : MonoBehaviour, ITooltipSource
{
    private const int MaxShownSeconds = 99;

    private static readonly string[] SecondsText = BuildSecondsText();

    [SerializeField] private Image frame;
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private Image cooldownFill;
    [SerializeField] private TextMeshProUGUI cooldownText;
    [SerializeField] private TextMeshProUGUI keyText;
    [SerializeField] private Image flash;
    [SerializeField] private UiTheme theme;
    [SerializeField, Range(0f, 1f), Tooltip("The failure flash's opacity at its strongest, over the slot.")]
    private float flashOpacity = 0.6f;

    private Color filledFrameColor;
    private Color emptyFrameColor;
    private Color flashColor;
    private AbilityDefinition shown;

    public event Action TooltipChanged;

    internal Image Icon => icon;
    internal TextMeshProUGUI NameText => nameText;
    internal Image CooldownFill => cooldownFill;
    internal TextMeshProUGUI CooldownText => cooldownText;
    internal TextMeshProUGUI KeyText => keyText;
    internal Image Flash => flash;
    internal bool IsEmpty { get; private set; } = true;
    internal AbilityDefinition Shown => shown;

    /// <summary>
    /// Reads the frame and flash colours from the theme. Logs an error when the theme or any required image or text
    /// reference is missing. The key label is optional.
    /// </summary>
    private void Awake()
    {
        if (theme == null || frame == null || icon == null || nameText == null || cooldownFill == null || cooldownText == null || flash == null)
        {
            Debug.LogError($"{name} is missing its theme or ability slot references.", this);
        }

        ReadTheme();
    }

    /// <summary>
    /// Sets the bound key label, when the slot has one.
    /// </summary>
    public void SetKey(string key)
    {
        if (keyText != null)
        {
            keyText.text = key;
        }
    }

    /// <summary>
    /// Shows <paramref name="ability"/>: its icon, or its name when it has no icon; an empty slot when null. Resets
    /// the cooldown and the flash.
    /// </summary>
    public void SetAbility(AbilityDefinition ability)
    {
        IsEmpty = ability == null;
        shown = ability;
        Sprite abilityIcon = ability != null ? ability.Icon : null;
        if (icon != null)
        {
            icon.sprite = abilityIcon;
            icon.enabled = abilityIcon != null;
        }

        if (nameText != null)
        {
            nameText.text = ability != null && abilityIcon == null ? ability.DisplayName : string.Empty;
        }

        if (frame != null)
        {
            frame.color = IsEmpty ? emptyFrameColor : filledFrameColor;
        }

        SetCooldown(0f, 0);
        SetFlash(0f);
        TooltipChanged?.Invoke();
    }

    /// <summary>
    /// The shown ability's name and description; false for an empty slot.
    /// </summary>
    public bool TryGetTooltip(out string title, out string description)
    {
        title = shown != null ? shown.DisplayName : null;
        description = shown != null ? shown.Description : null;
        return shown != null;
    }

    /// <summary>
    /// Fills <paramref name="fraction"/> of the slot radially and shows <paramref name="seconds"/> (hidden at 0).
    /// </summary>
    public void SetCooldown(float fraction, int seconds)
    {
        if (cooldownFill != null)
        {
            cooldownFill.fillAmount = Mathf.Clamp01(fraction);
        }

        if (cooldownText != null)
        {
            cooldownText.text = SecondsText[Mathf.Clamp(seconds, 0, MaxShownSeconds)];
        }
    }

    /// <summary>
    /// Shows the failure flash at <paramref name="alpha"/> (clamped to 0..1) of its configured opacity, hiding it at 0.
    /// </summary>
    public void SetFlash(float alpha)
    {
        if (flash != null)
        {
            float shown = Mathf.Clamp01(alpha);
            Color color = flashColor;
            color.a = flashOpacity * shown;
            flash.color = color;
            flash.enabled = shown > 0f;
        }
    }

    /// <summary>
    /// Assigns the slot's images and texts in code, for tests that build the slot without a prefab.
    /// </summary>
    internal void ConfigureReferences(
        Image frameImage,
        Image iconImage,
        TextMeshProUGUI abilityName,
        Image fill,
        TextMeshProUGUI seconds,
        TextMeshProUGUI key,
        Image flashImage)
    {
        frame = frameImage;
        icon = iconImage;
        nameText = abilityName;
        cooldownFill = fill;
        cooldownText = seconds;
        keyText = key;
        flash = flashImage;
    }

    /// <summary>
    /// Sets the theme in code and reads its colours, for tests that build the slot without a prefab.
    /// </summary>
    internal void ConfigureTheme(UiTheme uiTheme)
    {
        theme = uiTheme;
        ReadTheme();
    }

    /// <summary>
    /// Reads the filled and empty frame colours and the flash colour from the theme, when there is one.
    /// </summary>
    private void ReadTheme()
    {
        if (theme == null)
        {
            return;
        }

        filledFrameColor = theme.Colour(UiColour.Surface);
        emptyFrameColor = theme.Colour(UiColour.SurfaceEmpty);
        flashColor = theme.Colour(UiColour.Danger);
    }

    /// <summary>
    /// Precomputes the cooldown labels from 0 to <see cref="MaxShownSeconds"/> so showing seconds never allocates;
    /// 0 is an empty string.
    /// </summary>
    private static string[] BuildSecondsText()
    {
        string[] texts = new string[MaxShownSeconds + 1];
        texts[0] = string.Empty;
        for (int i = 1; i <= MaxShownSeconds; i++)
        {
            texts[i] = i.ToString();
        }

        return texts;
    }
}
