using System;
using UnityEngine;
using VContainer.Unity;

public sealed class DamagePopupPresenter : IStartable, ITickable, IPostLateTickable, IDisposable
{
    private readonly CombatEvents combatEvents;
    private readonly DamagePopupLayer popupLayer;
    private readonly Camera camera;
    private readonly CombatSettings settings;

    public DamagePopupPresenter(CombatEvents combatEvents, DamagePopupLayer popupLayer, Camera camera, CombatSettings settings)
    {
        this.combatEvents = combatEvents;
        this.popupLayer = popupLayer;
        this.camera = camera;
        this.settings = settings;
    }

    public void Start()
    {
        combatEvents.DamageApplied += HandleDamageApplied;
        combatEvents.HealApplied += HandleHealApplied;
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
        combatEvents.HealApplied -= HandleHealApplied;
    }

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

        popupLayer.Spawn(report.Amount.ToString(), settings.GetColor(report.Type), report.PopupWorldPosition, camera);
    }

    private void HandleHealApplied(HealReport report)
    {
        if (popupLayer != null)
        {
            popupLayer.Spawn("+" + report.Amount, settings.HealColor, report.PopupWorldPosition, camera);
        }
    }
}
