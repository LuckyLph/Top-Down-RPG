using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Binds the <see cref="StatusBarView"/> to the local player's <see cref="StatusEffects"/> (rebinding when
/// <see cref="LocalPlayerTracker"/> changes): one icon per active status in its kind's group, buffs left and debuffs
/// right, in the order the statuses list them, with the stack count (independent instances counted together), rebuilt
/// on <c>Changed</c>; when a kind has more statuses than its group has icons, the last icon shows "+n" for the rest
/// (Docs/UiRevamp.md, Decisions: Statuses). The elapsed fill of each status icon (from its longest remaining instance)
/// is pushed every frame only when it moved.
/// </summary>
public sealed class StatusBarPresenter : IStartable, ITickable, IDisposable
{
    private const float FillStep = 0.005f;
    private const string NameSeparator = ", ";

    private readonly LocalPlayerTracker localPlayer;
    private readonly StatusBarView view;
    private readonly Group buffs;
    private readonly Group debuffs;
    private readonly List<StatusEffectDefinition> kindDefinitions = new();
    private readonly StringBuilder names = new();
    private StatusEffects statuses;

    /// <summary>
    /// Creates a presenter for <paramref name="view"/> that can show as many statuses of each kind as its group has
    /// icons.
    /// </summary>
    public StatusBarPresenter(LocalPlayerTracker localPlayer, StatusBarView view)
    {
        this.localPlayer = localPlayer;
        this.view = view;
        buffs = new Group(StatusKind.Buff, view.GroupCapacity(StatusKind.Buff));
        debuffs = new Group(StatusKind.Debuff, view.GroupCapacity(StatusKind.Debuff));
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
    /// Refreshes the elapsed fill of every shown status icon. Does nothing while no player is bound.
    /// </summary>
    public void Tick()
    {
        if (statuses == null)
        {
            return;
        }

        RefreshFills(buffs);
        RefreshFills(debuffs);
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
            buffs.Shown = 0;
            debuffs.Shown = 0;
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
    /// Lays both groups out again from the active statuses.
    /// </summary>
    private void Rebuild()
    {
        Fill(buffs);
        Fill(debuffs);
    }

    /// <summary>
    /// Shows the distinct active statuses of <paramref name="group"/>'s kind on its icons, in the order
    /// <see cref="StatusEffects"/> lists them, with their stack counts; when they do not all fit, the last icon shows
    /// "+n" for the rest and lists their names. Hides the unused icons.
    /// </summary>
    private void Fill(Group group)
    {
        kindDefinitions.Clear();
        for (int i = 0; i < statuses.Count; i++)
        {
            StatusEffectDefinition definition = statuses.GetSnapshot(i).Definition;
            if (definition.Kind == group.Kind && !kindDefinitions.Contains(definition))
            {
                kindDefinitions.Add(definition);
            }
        }

        int capacity = group.Definitions.Length;
        bool overflows = kindDefinitions.Count > capacity;
        int shown = overflows ? Mathf.Max(0, capacity - 1) : kindDefinitions.Count;
        for (int i = 0; i < shown; i++)
        {
            StatusEffectDefinition definition = kindDefinitions[i];
            group.Definitions[i] = definition;
            group.Fill[i] = -1f;
            view.Show(group.Kind, i, definition, statuses.GetStacks(definition));
            RefreshFill(group, i);
        }

        group.Shown = shown;
        if (overflows && capacity > 0)
        {
            names.Clear();
            for (int i = shown; i < kindDefinitions.Count; i++)
            {
                names.Append(i > shown ? NameSeparator : string.Empty).Append(kindDefinitions[i].DisplayName);
            }

            view.ShowMore(group.Kind, shown, kindDefinitions.Count - shown, names.ToString());
            view.HideFrom(group.Kind, capacity);
            return;
        }

        view.HideFrom(group.Kind, shown);
    }

    /// <summary>
    /// Pushes the elapsed fill of every status icon of <paramref name="group"/> that moved.
    /// </summary>
    private void RefreshFills(Group group)
    {
        for (int i = 0; i < group.Shown; i++)
        {
            RefreshFill(group, i);
        }
    }

    /// <summary>
    /// Pushes icon <paramref name="index"/>'s elapsed fraction to the view when it has moved by at least
    /// <see cref="FillStep"/> since it was last shown.
    /// </summary>
    private void RefreshFill(Group group, int index)
    {
        StatusEffectDefinition definition = group.Definitions[index];
        float fill = 1f - Mathf.Clamp01(statuses.GetRemaining(definition) / definition.Duration);
        if (Mathf.Abs(fill - group.Fill[index]) < FillStep)
        {
            return;
        }

        group.Fill[index] = fill;
        view.SetElapsed(group.Kind, index, fill);
    }

    /// <summary>
    /// What one kind's group shows: the status on each of its icons, the fill last pushed to each, and how many show a
    /// status (a "+n" icon after them is not counted).
    /// </summary>
    private sealed class Group
    {
        /// <summary>
        /// Creates the state of the group of <paramref name="kind"/> with <paramref name="capacity"/> icons.
        /// </summary>
        public Group(StatusKind kind, int capacity)
        {
            Kind = kind;
            Definitions = new StatusEffectDefinition[capacity];
            Fill = new float[capacity];
        }

        public StatusKind Kind { get; }
        public StatusEffectDefinition[] Definitions { get; }
        public float[] Fill { get; }
        public int Shown { get; set; }
    }
}
