using System;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Binds the <see cref="PlayerHudView"/> to the local player (rebinding when <see cref="LocalPlayerTracker"/>
/// changes): the health bar (Docs/UiRevamp.md, Decisions: Health) on every damage, heal, death and restore and on any
/// change a network sync made silently, with a tick every <see cref="HudSettings.TickHealth"/> HP, the part just lost
/// shown for <see cref="HudSettings.RecentLossSeconds"/>, and the frame in Danger at or under
/// <see cref="HudSettings.LowHealthShare"/>, pulsing once every <see cref="HudSettings.LowPulseSeconds"/> unless the
/// player reduces flashing; and the equipped weapon in the weapon slot. On this machine only.
/// </summary>
public sealed class PlayerHudPresenter : IStartable, ITickable, IDisposable
{
    private readonly LocalPlayerTracker localPlayer;
    private readonly PlayerHudView view;
    private readonly IClock clock;
    private readonly HudSettings settings;
    private readonly UserSettings preferences;
    private Health health;
    private PlayerWeaponController weaponController;
    private int shownCurrent = -1;
    private int shownMax = -1;
    private bool lossShown;
    private float lossFrom;
    private float lossUntil;
    private bool low;

    /// <summary>
    /// Creates a presenter that drives <paramref name="view"/> for whichever player <paramref name="localPlayer"/>
    /// tracks, timing the loss and the pulse on <paramref name="clock"/> by <paramref name="settings"/> and holding the
    /// pulse still while <paramref name="preferences"/> reduce flashing.
    /// </summary>
    public PlayerHudPresenter(LocalPlayerTracker localPlayer, PlayerHudView view, IClock clock, HudSettings settings, UserSettings preferences)
    {
        this.localPlayer = localPlayer;
        this.view = view;
        this.clock = clock;
        this.settings = settings;
        this.preferences = preferences;
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
    /// Catches a health change a network sync made without an event, hides the loss once its time is up, and pulses
    /// a low bar's frame. Two integer compares when nothing changed; never allocates.
    /// </summary>
    public void Tick()
    {
        if (health == null)
        {
            return;
        }

        RefreshHealth();
        float now = clock.Time;
        if (lossShown && now >= lossUntil)
        {
            lossShown = false;
            view.HideLoss();
        }

        if (low)
        {
            view.SetLow(true, PulseStrength(now));
        }
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
            RefreshHealth();
        }
        else
        {
            view.ClearHealth();
        }

        if (weaponController != null)
        {
            weaponController.EquippedWeaponChanged += HandleEquippedWeaponChanged;
        }

        HandleEquippedWeaponChanged(weaponController != null ? weaponController.CurrentWeapon : null);
    }

    /// <summary>
    /// Unsubscribes from the bound player's health and weapon events and forgets them and what was shown of them.
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
        shownCurrent = -1;
        shownMax = -1;
        lossShown = false;
        low = false;
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
    /// Pushes the bound health to the view when it differs from what was shown: a drop shows the part just lost from
    /// the highest health of the loss still showing, restarting its time; a rise past that part hides it; and the
    /// frame turns low or back.
    /// </summary>
    private void RefreshHealth()
    {
        int current = health.CurrentHealth;
        int max = Mathf.Max(1, health.MaxHealth);
        if (current == shownCurrent && max == shownMax)
        {
            return;
        }

        if (shownMax == max && current < shownCurrent)
        {
            float before = (float)shownCurrent / max;
            lossFrom = lossShown ? Mathf.Max(lossFrom, before) : before;
            lossShown = true;
            lossUntil = clock.Time + settings.RecentLossSeconds;
            view.ShowLoss(lossFrom);
        }
        else if (lossShown && (shownMax != max || (float)current / max >= lossFrom))
        {
            lossShown = false;
            view.HideLoss();
        }

        shownCurrent = current;
        shownMax = max;
        view.SetHealth(current, max, settings.TickHealth);
        low = current <= max * settings.LowHealthShare;
        view.SetLow(low, low ? PulseStrength(clock.Time) : 1f);
    }

    /// <summary>
    /// A low bar frame's opacity at <paramref name="now"/>: full at the start of each pulse, easing down to
    /// <see cref="HudSettings.LowPulseFloor"/> at its middle and back; full throughout while the player reduces
    /// flashing.
    /// </summary>
    private float PulseStrength(float now)
    {
        if (preferences != null && preferences.ReduceFlashing)
        {
            return 1f;
        }

        float phase = Mathf.Repeat(now, settings.LowPulseSeconds) / settings.LowPulseSeconds;
        float dip = 0.5f - 0.5f * Mathf.Cos(phase * 2f * Mathf.PI);
        return Mathf.Lerp(1f, settings.LowPulseFloor, dip);
    }
}
