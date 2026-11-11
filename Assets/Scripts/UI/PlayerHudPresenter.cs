using System;
using VContainer.Unity;

/// <summary>
/// Binds the <see cref="PlayerHudView"/> to the local player (rebinding when <see cref="LocalPlayerTracker"/>
/// changes): refreshes the health bar on every damage, heal, death and restore, and shows the equipped weapon in the
/// weapon slot.
/// </summary>
public sealed class PlayerHudPresenter : IStartable, IDisposable
{
    private readonly LocalPlayerTracker localPlayer;
    private readonly PlayerHudView view;
    private Health health;
    private PlayerWeaponController weaponController;

    /// <summary>
    /// Creates a presenter that drives <paramref name="view"/> for whichever player <paramref name="localPlayer"/>
    /// tracks.
    /// </summary>
    public PlayerHudPresenter(LocalPlayerTracker localPlayer, PlayerHudView view)
    {
        this.localPlayer = localPlayer;
        this.view = view;
    }

    /// <summary>
    /// Starts following local player changes and binds the current player.
    /// </summary>
    public void Start()
    {
        localPlayer.Changed += Bind;
        Bind(localPlayer.Current);
    }

    /// <summary>
    /// Stops following local player changes and unsubscribes from the bound player's health and weapon.
    /// </summary>
    public void Dispose()
    {
        localPlayer.Changed -= Bind;
        Unbind();
    }

    /// <summary>
    /// Switches to <paramref name="player"/>'s health and weapon controller and refreshes the HUD; with no player
    /// it shows unknown health and no weapon.
    /// </summary>
    private void Bind(LocalPlayer player)
    {
        Unbind();

        health = player != null ? player.Handle.Health : null;
        weaponController = player != null ? player.Weapon : null;

        if (health != null)
        {
            health.Damaged += HandleDamaged;
            health.Healed += HandleHealed;
            health.Died += HandleDied;
            health.Restored += HandleRestored;
        }

        if (weaponController != null)
        {
            weaponController.EquippedWeaponChanged += HandleEquippedWeaponChanged;
        }

        RefreshHealth();
        HandleEquippedWeaponChanged(weaponController != null ? weaponController.CurrentWeapon : null);
    }

    /// <summary>
    /// Unsubscribes from the bound player's health and weapon events and forgets them.
    /// </summary>
    private void Unbind()
    {
        if (health != null)
        {
            health.Damaged -= HandleDamaged;
            health.Healed -= HandleHealed;
            health.Died -= HandleDied;
            health.Restored -= HandleRestored;
        }

        if (weaponController != null)
        {
            weaponController.EquippedWeaponChanged -= HandleEquippedWeaponChanged;
        }

        health = null;
        weaponController = null;
    }

    /// <summary>
    /// Refreshes the health bar after the player takes damage.
    /// </summary>
    private void HandleDamaged(Health.DamageEvent _)
    {
        RefreshHealth();
    }

    /// <summary>
    /// Refreshes the health bar after the player is healed.
    /// </summary>
    private void HandleHealed(Health _, int amount)
    {
        RefreshHealth();
    }

    /// <summary>
    /// Refreshes the health bar when the player dies.
    /// </summary>
    private void HandleDied(Health _)
    {
        RefreshHealth();
    }

    /// <summary>
    /// Refreshes the health bar when the player's health is refilled to the maximum.
    /// </summary>
    private void HandleRestored(Health _)
    {
        RefreshHealth();
    }

    /// <summary>
    /// Shows <paramref name="weapon"/> in the weapon slot, its icon and tooltip, or the dimmed empty icon when there is
    /// none.
    /// </summary>
    private void HandleEquippedWeaponChanged(PlayerWeapon weapon)
    {
        view.SetWeapon(weapon);
    }

    /// <summary>
    /// Pushes the bound health's current and max values to the view, or clears it when nothing is bound.
    /// </summary>
    private void RefreshHealth()
    {
        if (health == null)
        {
            view.ClearHealth();
            return;
        }

        view.SetHealth(health.CurrentHealth, health.MaxHealth);
    }
}
