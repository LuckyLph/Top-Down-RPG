using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Passive player HUD: a health fill with an "HP" readout and the equipped weapon's icon, in the
/// <see cref="UiTheme"/>'s Icon role, or IconMissing when there is no weapon. <see cref="PlayerHudPresenter"/> decides
/// what to show.
/// </summary>
[DisallowMultipleComponent]
public class PlayerHudView : MonoBehaviour
{
    private const string HealthTextFormat = "HP {0}";
    private const string UnknownHealthText = "HP --";

    [SerializeField] private Image healthFill;
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private Image weaponIcon;
    [SerializeField] private UiTheme theme;

    private Color activeWeaponIconTint = Color.white;
    private Color missingWeaponIconTint = Color.clear;

    internal Image HealthFill => healthFill;
    internal TextMeshProUGUI HealthText => healthText;
    internal Image WeaponIcon => weaponIcon;

    /// <summary>
    /// Reads the weapon icon's tints from the theme; logs an error without a theme.
    /// </summary>
    private void Awake()
    {
        if (theme == null)
        {
            Debug.LogError($"{name} has no UI theme.", this);
        }

        ReadTheme();
    }

    /// <summary>
    /// Fills the bar to <paramref name="current"/> over <paramref name="max"/> and shows the current HP. Values are
    /// clamped so the max is at least 1 and the current lies between 0 and the max.
    /// </summary>
    public void SetHealth(int current, int max)
    {
        int clampedMax = Mathf.Max(1, max);
        int clampedCurrent = Mathf.Clamp(current, 0, clampedMax);

        if (healthFill != null)
        {
            healthFill.fillAmount = (float)clampedCurrent / clampedMax;
        }

        if (healthText != null)
        {
            healthText.text = string.Format(HealthTextFormat, clampedCurrent);
        }
    }

    /// <summary>
    /// Empties the bar and shows unknown HP, for when there is no local player.
    /// </summary>
    public void ClearHealth()
    {
        if (healthFill != null)
        {
            healthFill.fillAmount = 0f;
        }

        if (healthText != null)
        {
            healthText.text = UnknownHealthText;
        }
    }

    /// <summary>
    /// Shows <paramref name="icon"/> in the active tint, or an empty icon in the dimmed missing-weapon tint when it
    /// is null.
    /// </summary>
    public void SetWeapon(Sprite icon)
    {
        if (weaponIcon == null)
        {
            return;
        }

        weaponIcon.sprite = icon;
        weaponIcon.color = icon == null ? missingWeaponIconTint : activeWeaponIconTint;
    }

    /// <summary>
    /// Assigns the health fill, health text, weapon icon and theme in code, for tests that build the HUD without a
    /// prefab.
    /// </summary>
    internal void ConfigureReferences(Image fill, TextMeshProUGUI text, Image icon, UiTheme uiTheme)
    {
        healthFill = fill;
        healthText = text;
        weaponIcon = icon;
        theme = uiTheme;
        ReadTheme();
    }

    /// <summary>
    /// Reads the active and missing weapon icon tints from the theme's Icon and IconMissing roles, when there is one.
    /// </summary>
    private void ReadTheme()
    {
        if (theme != null)
        {
            activeWeaponIconTint = theme.Colour(UiColour.Icon);
            missingWeaponIconTint = theme.Colour(UiColour.IconMissing);
        }
    }
}
