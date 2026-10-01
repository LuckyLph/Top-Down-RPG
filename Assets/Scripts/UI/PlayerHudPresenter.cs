using System;
using VContainer.Unity;

public sealed class PlayerHudPresenter : IStartable, IDisposable
{
    private readonly PlayerHandle player;
    private readonly PlayerWeaponController weaponController;
    private readonly PlayerHudView view;

    public PlayerHudPresenter(LocalPlayer localPlayer, PlayerHudView view)
    {
        player = localPlayer.Handle;
        weaponController = localPlayer.Weapon;
        this.view = view;
    }

    public void Start()
    {
        if (player.Health != null)
        {
            player.Health.Damaged += HandleDamaged;
            player.Health.Died += HandleDied;
            player.Health.Restored += HandleRestored;
        }

        if (weaponController != null)
        {
            weaponController.EquippedWeaponChanged += HandleEquippedWeaponChanged;
        }

        RefreshHealth();
        HandleEquippedWeaponChanged(weaponController != null ? weaponController.CurrentWeapon : null);
    }

    public void Dispose()
    {
        if (player.Health != null)
        {
            player.Health.Damaged -= HandleDamaged;
            player.Health.Died -= HandleDied;
            player.Health.Restored -= HandleRestored;
        }

        if (weaponController != null)
        {
            weaponController.EquippedWeaponChanged -= HandleEquippedWeaponChanged;
        }
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
        if (player.Health == null)
        {
            view.ClearHealth();
            return;
        }

        view.SetHealth(player.Health.CurrentHealth, player.Health.MaxHealth);
    }
}
