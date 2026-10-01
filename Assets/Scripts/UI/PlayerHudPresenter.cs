using System;
using VContainer.Unity;

public sealed class PlayerHudPresenter : IStartable, IDisposable
{
    private readonly LocalPlayerTracker localPlayer;
    private readonly PlayerHudView view;
    private Health health;
    private PlayerWeaponController weaponController;

    public PlayerHudPresenter(LocalPlayerTracker localPlayer, PlayerHudView view)
    {
        this.localPlayer = localPlayer;
        this.view = view;
    }

    public void Start()
    {
        localPlayer.Changed += Bind;
        Bind(localPlayer.Current);
    }

    public void Dispose()
    {
        localPlayer.Changed -= Bind;
        Unbind();
    }

    private void Bind(LocalPlayer player)
    {
        Unbind();

        health = player != null ? player.Handle.Health : null;
        weaponController = player != null ? player.Weapon : null;

        if (health != null)
        {
            health.Damaged += HandleDamaged;
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

    private void Unbind()
    {
        if (health != null)
        {
            health.Damaged -= HandleDamaged;
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

    private void HandleDamaged(Health.DamageEvent _)
    {
        RefreshHealth();
    }

    private void HandleDied(Health _)
    {
        RefreshHealth();
    }

    private void HandleRestored(Health _)
    {
        RefreshHealth();
    }

    private void HandleEquippedWeaponChanged(PlayerWeapon weapon)
    {
        view.SetWeapon(weapon != null ? weapon.HudIcon : null);
    }

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
