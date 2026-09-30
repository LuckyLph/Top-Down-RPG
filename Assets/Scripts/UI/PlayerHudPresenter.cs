using System;
using VContainer.Unity;

public sealed class PlayerHudPresenter : IStartable, IDisposable
{
    private readonly IPlayerLocator player;
    private readonly PlayerWeaponController weaponController;
    private readonly PlayerHudView view;

    public PlayerHudPresenter(IPlayerLocator player, PlayerWeaponController weaponController, PlayerHudView view)
    {
        this.player = player;
        this.weaponController = weaponController;
        this.view = view;
    }

    public void Start()
    {
        if (player.Health != null)
        {
            player.Health.Damaged += HandleDamaged;
            player.Health.Died += HandleDied;
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
