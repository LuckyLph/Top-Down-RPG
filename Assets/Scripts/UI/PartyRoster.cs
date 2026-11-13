using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The registered players with a slot, for the HUD's party frames and the in-game menu's Party page (Docs/UiRevamp.md,
/// Phases 5 and 6): it follows the registry, keeps each player's <see cref="PlayerSlot"/> and
/// <see cref="StatusEffects"/>, notes when this machine saw it die, and lists them in slot order. Ordering allocates
/// nothing.
/// </summary>
public sealed class PartyRoster : IDisposable
{
    private readonly IPlayerRegistry players;
    private readonly List<PartyMember> members = new(PlayerSlots.Count);
    private bool started;

    /// <summary>
    /// Follows the players of <paramref name="players"/> once started.
    /// </summary>
    public PartyRoster(IPlayerRegistry players)
    {
        this.players = players;
    }

    /// <summary>
    /// Follows the registry and takes the players already in it.
    /// </summary>
    public void Start()
    {
        if (started)
        {
            return;
        }

        started = true;
        players.PlayerAdded += Add;
        players.PlayerRemoved += Remove;
        IReadOnlyList<PlayerHandle> party = players.Players;
        for (int i = 0; i < party.Count; i++)
        {
            Add(party[i]);
        }
    }

    /// <summary>
    /// Stops following the registry and forgets the players.
    /// </summary>
    public void Dispose()
    {
        if (!started)
        {
            return;
        }

        started = false;
        players.PlayerAdded -= Add;
        players.PlayerRemoved -= Remove;
        members.Clear();
    }

    /// <summary>
    /// Notes each player's death at <paramref name="now"/>, then fills <paramref name="order"/> with the players that
    /// have a slot, in slot order, leaving out the one on <paramref name="leftOut"/> (none when null), and returns how
    /// many, at most the array's length.
    /// </summary>
    public int Order(float now, Transform leftOut, PartyMember[] order)
    {
        int count = 0;
        for (int i = 0; i < members.Count; i++)
        {
            PartyMember member = members[i];
            member.Track(now);
            if ((leftOut != null && member.Handle.Transform == leftOut) || member.Slot == null || !PlayerSlots.IsSlot(member.Slot.Number) || count >= order.Length)
            {
                continue;
            }

            int at = count++;
            while (at > 0 && order[at - 1].Slot.Number > member.Slot.Number)
            {
                order[at] = order[at - 1];
                at--;
            }

            order[at] = member;
        }

        Array.Clear(order, count, order.Length - count);
        return count;
    }

    /// <summary>
    /// Starts following <paramref name="player"/>, its slot and its statuses.
    /// </summary>
    private void Add(PlayerHandle player)
    {
        if (player == null || player.Transform == null)
        {
            return;
        }

        for (int i = 0; i < members.Count; i++)
        {
            if (members[i].Handle == player)
            {
                return;
            }
        }

        player.Transform.TryGetComponent(out PlayerSlot slot);
        player.Transform.TryGetComponent(out StatusEffects statuses);
        members.Add(new PartyMember(player, slot, statuses));
    }

    /// <summary>
    /// Stops following <paramref name="player"/>.
    /// </summary>
    private void Remove(PlayerHandle player)
    {
        for (int i = members.Count - 1; i >= 0; i--)
        {
            if (members[i].Handle == player)
            {
                members.RemoveAt(i);
            }
        }
    }
}
