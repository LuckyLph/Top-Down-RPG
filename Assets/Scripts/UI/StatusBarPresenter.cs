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

    /// <summary>
    /// Creates a presenter for <paramref name="view"/> that can show as many statuses as the view has icons.
    /// </summary>
    public StatusBarPresenter(LocalPlayerTracker localPlayer, StatusBarView view)
    {
        this.localPlayer = localPlayer;
        this.view = view;
        shownDefinitions = new StatusEffectDefinition[view.Capacity];
        shownFill = new float[view.Capacity];
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
    /// Stops following local player changes and unsubscribes from the bound player's statuses.
    /// </summary>
    public void Dispose()
    {
        localPlayer.Changed -= Bind;
        Unbind();
    }

    /// <summary>
    /// Refreshes the elapsed fill of every shown icon. Does nothing while no player is bound.
    /// </summary>
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

    /// <summary>
    /// Switches to <paramref name="player"/>'s <see cref="StatusEffects"/> and rebuilds the icons, or clears the bar
    /// when there is no player or it has no statuses component.
    /// </summary>
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

    /// <summary>
    /// Stops listening to the bound player's statuses and forgets them.
    /// </summary>
    private void Unbind()
    {
        if (statuses != null)
        {
            statuses.Changed -= Rebuild;
        }

        statuses = null;
    }

    /// <summary>
    /// Lays the icons out again from the active statuses, buffs first then debuffs, and hides the unused icons.
    /// </summary>
    private void Rebuild()
    {
        shownCount = 0;
        AddKind(StatusKind.Buff);
        AddKind(StatusKind.Debuff);
        view.HideFrom(shownCount);
    }

    /// <summary>
    /// Appends an icon, with its stack count and fill, for each distinct active status of <paramref name="kind"/>
    /// in the order <see cref="StatusEffects"/> lists them, until the view is full.
    /// </summary>
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

    /// <summary>
    /// Whether <paramref name="definition"/> already has an icon in the current layout.
    /// </summary>
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

    /// <summary>
    /// Pushes icon <paramref name="index"/>'s elapsed fraction to the view when it has moved by at least
    /// <see cref="FillStep"/> since it was last shown.
    /// </summary>
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
