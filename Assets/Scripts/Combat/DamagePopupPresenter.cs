using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Turns <see cref="CombatEvents"/> into floating text on the <see cref="DamagePopupLayer"/>: damage numbers in the
/// damage type's color (in a smaller text size for periodic ticks), "+N" heals, and the immune text for immune hits and blocked
/// statuses. Also ages the popups each frame and re-projects them after the camera moves. A popup that involves another
/// player but not the local one (an ally's hit, tick or heal, or one an ally took) shows as this machine's
/// <see cref="UserSettings.AllyPopups"/> asks (Docs/UiRevamp.md, Phase 3): like any other, at
/// <see cref="CombatSettings.AllyPopupSize"/>, or not at all. Without settings, a local player or a registry, every
/// popup shows.
/// </summary>
public sealed class DamagePopupPresenter : IStartable, ITickable, IPostLateTickable, IDisposable
{
    private readonly CombatEvents combatEvents;
    private readonly DamagePopupLayer popupLayer;
    private readonly Camera camera;
    private readonly CombatSettings settings;
    private readonly UserSettings preferences;
    private readonly LocalPlayerTracker localPlayer;
    private readonly IPlayerRegistry players;

    /// <summary>
    /// Captures the event hub, the popup layer, the camera popups are projected through, the popup styling, this
    /// machine's preferences, its local player and the players, which tell allies' popups apart.
    /// </summary>
    public DamagePopupPresenter(CombatEvents combatEvents, DamagePopupLayer popupLayer, Camera camera, CombatSettings settings, UserSettings preferences, LocalPlayerTracker localPlayer, IPlayerRegistry players)
    {
        this.combatEvents = combatEvents;
        this.popupLayer = popupLayer;
        this.camera = camera;
        this.settings = settings;
        this.preferences = preferences;
        this.localPlayer = localPlayer;
        this.players = players;
    }

    /// <summary>
    /// Subscribes to damage, heal and blocked-status events.
    /// </summary>
    public void Start()
    {
        combatEvents.DamageApplied += HandleDamageApplied;
        combatEvents.HealApplied += HandleHealApplied;
        combatEvents.StatusBlocked += HandleStatusBlocked;
    }

    /// <summary>
    /// Ages the active popups by <see cref="Time.deltaTime"/> rather than the game clock.
    /// </summary>
    public void Tick()
    {
        if (popupLayer != null)
        {
            popupLayer.Tick(Time.deltaTime);
        }
    }

    /// <summary>
    /// Re-projects the active popups after LateUpdate, once the camera has moved for the frame.
    /// </summary>
    public void PostLateTick()
    {
        if (popupLayer != null)
        {
            popupLayer.RefreshPositions();
        }
    }

    /// <summary>
    /// Unsubscribes from the combat events when the scope is disposed.
    /// </summary>
    public void Dispose()
    {
        combatEvents.DamageApplied -= HandleDamageApplied;
        combatEvents.HealApplied -= HandleHealApplied;
        combatEvents.StatusBlocked -= HandleStatusBlocked;
    }

    /// <summary>
    /// Spawns the immune text for an immune hit, otherwise the amount in the damage type's color, at the periodic popup
    /// size for periodic damage, unless it is an ally's popup this machine hides or shows smaller.
    /// </summary>
    private void HandleDamageApplied(DamageReport report)
    {
        if (popupLayer == null)
        {
            return;
        }

        UiTextSize? size = (report.Flags & DamageFlags.Periodic) != 0 && !report.IsImmune ? settings.PeriodicPopupSize : null;
        if (!TrySize(report.Target, report.Source, ref size))
        {
            return;
        }

        if (report.IsImmune)
        {
            popupLayer.Spawn(settings.ImmuneText, settings.ImmuneColor, report.PopupWorldPosition, camera, size);
            return;
        }

        popupLayer.Spawn(report.Amount.ToString(), settings.GetColor(report.Type), report.PopupWorldPosition, camera, size);
    }

    /// <summary>
    /// Spawns the immune text over a unit a status immunity protected, unless it is an ally's popup this machine hides
    /// or shows smaller.
    /// </summary>
    private void HandleStatusBlocked(StatusReport report)
    {
        UiTextSize? size = null;
        if (popupLayer != null && TrySize(report.Target, report.Source, ref size))
        {
            popupLayer.Spawn(settings.ImmuneText, settings.ImmuneColor, report.PopupWorldPosition, camera, size);
        }
    }

    /// <summary>
    /// Spawns "+N" in the heal color for the HP a heal restored, unless it is an ally's popup this machine hides or shows
    /// smaller.
    /// </summary>
    private void HandleHealApplied(HealReport report)
    {
        UiTextSize? size = null;
        if (popupLayer != null && TrySize(report.Target, report.Source, ref size))
        {
            popupLayer.Spawn("+" + report.Amount, settings.HealColor, report.PopupWorldPosition, camera, size);
        }
    }

    /// <summary>
    /// Whether the popup of something <paramref name="source"/> did to <paramref name="target"/> shows, changing
    /// <paramref name="size"/> to <see cref="CombatSettings.AllyPopupSize"/> for an ally's popup shown smaller.
    /// </summary>
    private bool TrySize(Health target, GameObject source, ref UiTextSize? size)
    {
        AllyPopups shown = preferences != null ? preferences.AllyPopups : AllyPopups.Shown;
        if (shown == AllyPopups.Shown || !IsAllies(target, source))
        {
            return true;
        }

        if (shown == AllyPopups.Hidden)
        {
            return false;
        }

        size = settings.AllyPopupSize;
        return true;
    }

    /// <summary>
    /// Whether a popup of something <paramref name="source"/> did to <paramref name="target"/> is an ally's: neither
    /// is the local player, and one of them is another player. Without a local player nothing is.
    /// </summary>
    internal bool IsAllies(Health target, GameObject source)
    {
        LocalPlayer local = localPlayer != null ? localPlayer.Current : null;
        if (local == null || players == null)
        {
            return false;
        }

        Health own = local.Handle.Health;
        DamageReceiver sourceUnit = CombatFactions.UnitOf(source);
        Health sourceHealth = sourceUnit != null ? sourceUnit.Health : null;
        if (target == own || (sourceHealth != null && sourceHealth == own))
        {
            return false;
        }

        return IsPlayer(target) || IsPlayer(sourceHealth);
    }

    /// <summary>
    /// Whether <paramref name="health"/> is a registered player's.
    /// </summary>
    private bool IsPlayer(Health health)
    {
        if (health == null)
        {
            return false;
        }

        IReadOnlyList<PlayerHandle> registered = players.Players;
        for (int i = 0; i < registered.Count; i++)
        {
            if (registered[i].Health == health)
            {
                return true;
            }
        }

        return false;
    }
}
