using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Passive player HUD: the command bar's health bar (<see cref="HealthBarView"/>) and the weapon slot
/// (<see cref="WeaponSlotView"/>) beside its two abilities. <see cref="PlayerHudPresenter"/> decides what to show.
/// </summary>
[DisallowMultipleComponent]
public class PlayerHudView : MonoBehaviour
{
    [SerializeField] private HealthBarView healthBar;
    [SerializeField] private WeaponSlotView weaponSlot;

    internal HealthBarView HealthBar => healthBar;
    internal Image HealthFill => healthBar != null ? healthBar.Fill : null;
    internal TextMeshProUGUI HealthText => healthBar != null ? healthBar.Text : null;
    internal WeaponSlotView WeaponSlot => weaponSlot;
    internal Image WeaponIcon => weaponSlot != null ? weaponSlot.Icon : null;

    /// <summary>
    /// Logs an error when the health bar or the weapon slot is missing.
    /// </summary>
    private void Awake()
    {
        if (healthBar == null || weaponSlot == null)
        {
            Debug.LogError($"{name} is missing its health bar or weapon slot.", this);
        }
    }

    /// <summary>
    /// Shows <paramref name="current"/> of <paramref name="max"/> HP with a tick every <paramref name="tickHealth"/>.
    /// </summary>
    public void SetHealth(int current, int max, int tickHealth)
    {
        if (healthBar != null)
        {
            healthBar.SetHealth(current, max, tickHealth);
        }
    }

    /// <summary>
    /// Shows the bar's part from its fill up to <paramref name="fraction"/> as just lost.
    /// </summary>
    public void ShowLoss(float fraction)
    {
        if (healthBar != null)
        {
            healthBar.ShowLoss(fraction);
        }
    }

    /// <summary>
    /// Hides the part just lost.
    /// </summary>
    public void HideLoss()
    {
        if (healthBar != null)
        {
            healthBar.HideLoss();
        }
    }

    /// <summary>
    /// Shows the bar low, its frame Danger at <paramref name="strength"/>, or not.
    /// </summary>
    public void SetLow(bool low, float strength)
    {
        if (healthBar != null)
        {
            healthBar.SetLow(low, strength);
        }
    }

    /// <summary>
    /// Empties the bar and shows unknown health, for when there is no local player.
    /// </summary>
    public void ClearHealth()
    {
        if (healthBar != null)
        {
            healthBar.Clear();
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
    /// Assigns the health bar and weapon slot in code, for tests that build the HUD without a prefab.
    /// </summary>
    internal void ConfigureReferences(HealthBarView bar, WeaponSlotView slot)
    {
        healthBar = bar;
        weaponSlot = slot;
    }
}
