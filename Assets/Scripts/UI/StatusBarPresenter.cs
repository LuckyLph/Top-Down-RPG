using System;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Binds the <see cref="StatusBarView"/> to the local player's <see cref="StatusEffects"/> (rebinding when
/// <see cref="LocalPlayerTracker"/> changes): one icon per active status, buffs then debuffs, with the stack count
/// (independent instances counted together) rebuilt on <c>Changed</c>, and the elapsed fill of each icon (from its
/// longest remaining instance) pushed every frame only when it moved.
/// </summary>
public sealed class StatusBarPresenter : IStartable, ITickable, IDisposable
{
    private const float FillStep = 0.005f;

    private readonly LocalPlayerTracker localPlayer;
    private readonly StatusBarView view;
    private readonly StatusEffectDefinition[] shownDefinitions;
    private readonly float[] shownFill;
    private StatusEffects statuses;
    private int shownCount;

    public StatusBarPresenter(LocalPlayerTracker localPlayer, StatusBarView view)
    {
        this.localPlayer = localPlayer;
        this.view = view;
        shownDefinitions = new StatusEffectDefinition[view.Capacity];
        shownFill = new float[view.Capacity];
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

    public void Tick()
    {
        if (statuses == null)
        {
            return;
        }

        for (int i = 0; i < shownCount; i++)
        {
            RefreshFill(i);
        }
    }

    private void Bind(LocalPlayer player)
    {
        Unbind();
        statuses = player != null ? player.Controller.GetComponent<StatusEffects>() : null;
        if (statuses == null)
        {
            shownCount = 0;
            view.Clear();
            return;
        }

        statuses.Changed += Rebuild;
        Rebuild();
    }

    private void Unbind()
    {
        if (statuses != null)
        {
            statuses.Changed -= Rebuild;
        }

        statuses = null;
    }

    private void Rebuild()
    {
        shownCount = 0;
        AddKind(StatusKind.Buff);
        AddKind(StatusKind.Debuff);
        view.HideFrom(shownCount);
    }

    private void AddKind(StatusKind kind)
    {
        for (int i = 0; i < statuses.Count && shownCount < shownDefinitions.Length; i++)
        {
            StatusEffectDefinition definition = statuses.GetSnapshot(i).Definition;
            if (definition.Kind != kind || IsShown(definition))
            {
                continue;
            }

            int index = shownCount++;
            shownDefinitions[index] = definition;
            shownFill[index] = -1f;
            view.Show(index, definition, statuses.GetStacks(definition));
            RefreshFill(index);
        }
    }

    private bool IsShown(StatusEffectDefinition definition)
    {
        for (int i = 0; i < shownCount; i++)
        {
            if (shownDefinitions[i] == definition)
            {
                return true;
            }
        }

        return false;
    }

    private void RefreshFill(int index)
    {
        StatusEffectDefinition definition = shownDefinitions[index];
        float fill = 1f - Mathf.Clamp01(statuses.GetRemaining(definition) / definition.Duration);
        if (Mathf.Abs(fill - shownFill[index]) < FillStep)
        {
            return;
        }

        shownFill[index] = fill;
        view.SetElapsed(index, fill);
    }
}
