using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The HUD's passive weapon slot: the equipped weapon's icon in the <see cref="UiTheme"/>'s Icon role, or IconMissing
/// without a weapon (both read once when it wakes). As an <see cref="ITooltipSource"/> it gives the weapon's name and its
/// damage, type and range (<see cref="TooltipDetails"/>, Docs/UiRevamp.md, Decisions: Weapon).
/// </summary>
[DisallowMultipleComponent]
public class WeaponSlotView : MonoBehaviour, ITooltipSource
{
    [SerializeField] private Image icon;
    [SerializeField] private UiTheme theme;

    private Color activeIconTint = Color.white;
    private Color missingIconTint = Color.clear;
    private PlayerWeapon shown;
    private string details;

    public event Action TooltipChanged;

    internal Image Icon => icon;
    internal PlayerWeapon Shown => shown;

    /// <summary>
    /// Reads the icon's tints from the theme; logs an error without a theme or an icon.
    /// </summary>
    private void Awake()
    {
        if (theme == null || icon == null)
        {
            Debug.LogError($"{name} is missing its theme or weapon icon.", this);
        }

        ReadTheme();
    }

    /// <summary>
    /// Shows <paramref name="weapon"/>'s HUD icon in the active tint, or an empty icon in the dimmed missing-weapon tint
    /// when it has none or there is no weapon, and builds the tooltip's numbers.
    /// </summary>
    public void SetWeapon(PlayerWeapon weapon)
    {
        shown = weapon;
        details = TooltipDetails.For(weapon);
        Sprite sprite = weapon != null ? weapon.HudIcon : null;
        if (icon != null)
        {
            icon.sprite = sprite;
            icon.color = sprite == null ? missingIconTint : activeIconTint;
        }

        TooltipChanged?.Invoke();
    }

    /// <summary>
    /// The shown weapon's name and its damage, type and range, with no description; false without a weapon.
    /// </summary>
    public bool TryGetTooltip(out string title, out string details, out string description)
    {
        title = shown != null ? shown.DisplayName : null;
        details = shown != null ? this.details : null;
        description = null;
        return shown != null;
    }

    /// <summary>
    /// Assigns the icon and theme in code and reads the theme, for tests that build the slot without a prefab.
    /// </summary>
    internal void ConfigureReferences(Image iconImage, UiTheme uiTheme)
    {
        icon = iconImage;
        theme = uiTheme;
        ReadTheme();
    }

    /// <summary>
    /// Reads the active and missing icon tints from the theme's Icon and IconMissing roles, when there is one.
    /// </summary>
    private void ReadTheme()
    {
        if (theme != null)
        {
            activeIconTint = theme.Colour(UiColour.Icon);
            missingIconTint = theme.Colour(UiColour.IconMissing);
        }
    }
}
