using System;
using UnityEngine;
using VContainer.Unity;

public sealed class DamagePopupPresenter : IStartable, ITickable, IPostLateTickable, IDisposable
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

    public void Tick()
    {
        if (popupLayer != null)
        {
            popupLayer.Tick(Time.deltaTime);
        }
    }

    public void PostLateTick()
    {
        if (popupLayer != null)
        {
            popupLayer.RefreshPositions();
        }
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
