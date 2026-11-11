using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Passive player HUD: a health fill reading "72 / 100", built only when the health shown changes, and the weapon slot
/// (<see cref="WeaponSlotView"/>). <see cref="PlayerHudPresenter"/> decides what to show.
/// </summary>
[DisallowMultipleComponent]
public class PlayerHudView : MonoBehaviour
{
    private const string HealthTextFormat = "{0} / {1}";
    private const string UnknownHealthText = "-- / --";

    [SerializeField] private Image healthFill;
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private WeaponSlotView weaponSlot;

    private int shownCurrent = -1;
    private int shownMax = -1;

    internal Image HealthFill => healthFill;
    internal TextMeshProUGUI HealthText => healthText;
    internal WeaponSlotView WeaponSlot => weaponSlot;
    internal Image WeaponIcon => weaponSlot != null ? weaponSlot.Icon : null;

    /// <summary>
    /// Logs an error when the health fill, its text or the weapon slot is missing.
    /// </summary>
    private void Awake()
    {
        if (healthFill == null || healthText == null || weaponSlot == null)
        {
            Debug.LogError($"{name} is missing its health bar or weapon slot.", this);
        }
    }

    /// <summary>
    /// Fills the bar to <paramref name="current"/> over <paramref name="max"/> and shows both, building the text only
    /// when either changed. Values are clamped so the max is at least 1 and the current lies between 0 and the max.
    /// </summary>
    public void SetHealth(int current, int max)
    {
        int clampedMax = Mathf.Max(1, max);
        int clampedCurrent = Mathf.Clamp(current, 0, clampedMax);
        if (clampedCurrent == shownCurrent && clampedMax == shownMax)
        {
            return;
        }

        shownCurrent = clampedCurrent;
        shownMax = clampedMax;
        if (healthFill != null)
        {
            healthFill.fillAmount = (float)clampedCurrent / clampedMax;
        }

        if (healthText != null)
        {
            healthText.text = string.Format(HealthTextFormat, clampedCurrent, clampedMax);
        }
    }

    /// <summary>
    /// Empties the bar and shows unknown health, for when there is no local player.
    /// </summary>
    public void ClearHealth()
    {
        shownCurrent = -1;
        shownMax = -1;
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
    /// Shows <paramref name="weapon"/> in the weapon slot, or the dimmed empty icon when it is null.
    /// </summary>
    public void SetWeapon(PlayerWeapon weapon)
    {
        if (weaponSlot != null)
        {
            weaponSlot.SetWeapon(weapon);
        }
    }

    /// <summary>
    /// Assigns the health fill, health text and weapon slot in code, for tests that build the HUD without a prefab.
    /// </summary>
    internal void ConfigureReferences(Image fill, TextMeshProUGUI text, WeaponSlotView slot)
    {
        healthFill = fill;
        healthText = text;
        weaponSlot = slot;
    }
}
