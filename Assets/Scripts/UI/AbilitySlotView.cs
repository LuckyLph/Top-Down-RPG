using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One passive slot of the ability bar: icon (or the ability's name when it has none, or an empty state),
/// radial cooldown fill with whole seconds remaining, key label and a failure flash.
/// </summary>
[DisallowMultipleComponent]
public class AbilitySlotView : MonoBehaviour
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
    [SerializeField] private Color filledFrameColor = new(0.12f, 0.12f, 0.16f, 0.85f);
    [SerializeField] private Color emptyFrameColor = new(0.12f, 0.12f, 0.16f, 0.35f);
    [SerializeField] private Color flashColor = new(0.9f, 0.15f, 0.15f, 0.6f);

    internal Image Icon => icon;
    internal TextMeshProUGUI NameText => nameText;
    internal Image CooldownFill => cooldownFill;
    internal TextMeshProUGUI CooldownText => cooldownText;
    internal Image Flash => flash;
    internal bool IsEmpty { get; private set; } = true;

    private void Awake()
    {
        if (frame == null || icon == null || nameText == null || cooldownFill == null || cooldownText == null || flash == null)
        {
            Debug.LogError($"{name} is missing ability slot references.", this);
        }
    }

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

    public void SetFlash(float alpha)
    {
        if (flash != null)
        {
            Color color = flashColor;
            color.a *= Mathf.Clamp01(alpha);
            flash.color = color;
        }
    }

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
