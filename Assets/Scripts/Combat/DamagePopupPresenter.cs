using System;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Turns <see cref="CombatEvents"/> into floating text on the <see cref="DamagePopupLayer"/>: damage numbers in the
/// damage type's color (in a smaller text size for periodic ticks), "+N" heals, and the immune text for immune hits and blocked
/// statuses. Also ages the popups each frame and re-projects them after the camera moves.
/// </summary>
public sealed class DamagePopupPresenter : IStartable, ITickable, IPostLateTickable, IDisposable
{
    private readonly CombatEvents combatEvents;
    private readonly DamagePopupLayer popupLayer;
    private readonly Camera camera;
    private readonly CombatSettings settings;

    /// <summary>
    /// Captures the event hub, the popup layer, the camera popups are projected through and the popup styling.
    /// </summary>
    public DamagePopupPresenter(CombatEvents combatEvents, DamagePopupLayer popupLayer, Camera camera, CombatSettings settings)
    {
        this.combatEvents = combatEvents;
        this.popupLayer = popupLayer;
        this.camera = camera;
        this.settings = settings;
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
    /// size for periodic damage.
    /// </summary>
    private void HandleDamageApplied(DamageReport report)
    {
        if (popupLayer == null)
        {
            return;
        }

        if (report.IsImmune)
        {
            popupLayer.Spawn(settings.ImmuneText, settings.ImmuneColor, report.PopupWorldPosition, camera);
            return;
        }

        UiTextSize? size = (report.Flags & DamageFlags.Periodic) != 0 ? settings.PeriodicPopupSize : null;
        popupLayer.Spawn(report.Amount.ToString(), settings.GetColor(report.Type), report.PopupWorldPosition, camera, size);
    }

    /// <summary>
    /// Spawns the immune text over a unit a status immunity protected.
    /// </summary>
    private void HandleStatusBlocked(StatusReport report)
    {
        if (popupLayer != null)
        {
            popupLayer.Spawn(settings.ImmuneText, settings.ImmuneColor, report.PopupWorldPosition, camera);
        }
    }

    /// <summary>
    /// Spawns "+N" in the heal color for the HP a heal restored.
    /// </summary>
    private void HandleHealApplied(HealReport report)
    {
        if (popupLayer != null)
        {
            popupLayer.Spawn("+" + report.Amount, settings.HealColor, report.PopupWorldPosition, camera);
        }
    }
}
