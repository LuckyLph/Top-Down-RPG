using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Passive player HUD: a health fill with an "HP" readout and the equipped weapon's icon, dimmed when there is no
/// weapon. <see cref="PlayerHudPresenter"/> decides what to show.
/// </summary>
[DisallowMultipleComponent]
public class PlayerHudView : MonoBehaviour
{
    private const string HealthTextFormat = "HP {0}";
    private const string UnknownHealthText = "HP --";

    [SerializeField] private Image healthFill;
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private Image weaponIcon;
    [SerializeField] private Color activeWeaponIconTint = Color.white;
    [SerializeField] private Color missingWeaponIconTint = new(1f, 1f, 1f, 0.25f);

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
    /// Assigns the health fill, health text and weapon icon in code, for tests that build the HUD without a prefab.
    /// </summary>
    internal void ConfigureReferences(Image fill, TextMeshProUGUI text, Image icon)
    {
        healthFill = fill;
        healthText = text;
        weaponIcon = icon;
    }
}
