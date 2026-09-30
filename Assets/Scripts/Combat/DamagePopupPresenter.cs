using System;
using UnityEngine;
using VContainer.Unity;

// Turns published damage into floating numbers on the Gameplay scene's popup layer.
public sealed class DamagePopupPresenter : IStartable, IDisposable
{
    private readonly CombatEvents combatEvents;
    private readonly DamagePopupLayer popupLayer;
    private readonly Camera camera;

    public DamagePopupPresenter(CombatEvents combatEvents, DamagePopupLayer popupLayer, Camera camera)
    {
        this.combatEvents = combatEvents;
        this.popupLayer = popupLayer;
        this.camera = camera;
    }

    public void Start()
    {
        combatEvents.DamageApplied += HandleDamageApplied;
    }

    public void Dispose()
    {
        combatEvents.DamageApplied -= HandleDamageApplied;
    }

    private void HandleDamageApplied(DamageReport report)
    {
        if (popupLayer != null)
        {
            popupLayer.Spawn(report.Amount, report.PopupWorldPosition, camera);
        }
    }
}
