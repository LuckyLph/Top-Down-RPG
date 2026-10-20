using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where players arriving at one place of a world stand, so they do not stand on each other (Docs/WorldStreaming.md:
/// Removals and migrations): slot 0 on the place, then alternately right and left of it, one spacing further out every
/// two slots. An arriving player takes the first slot no other player in that world stands on, whatever its index in
/// the registry.
/// </summary>
public static class EntrySlots
{
    public const int MaxSlots = 8;

    /// <summary>
    /// The position of slot <paramref name="slot"/> around <paramref name="place"/>, <paramref name="spacing"/> apart.
    /// </summary>
    public static Vector2 SlotPosition(Vector2 place, int slot, float spacing)
    {
        int step = (slot + 1) / 2;
        float side = slot % 2 == 1 ? 1f : -1f;
        return place + Vector2.right * (step * side * spacing);
    }

    /// <summary>
    /// The first slot around <paramref name="place"/> in <paramref name="world"/> that no registered player but
    /// <paramref name="arriving"/> stands within half a spacing of (players in other worlds stand far away and never
    /// count); the place itself when every slot is taken or the spacing is zero.
    /// </summary>
    public static Vector2 FreeSlot(
        Vector2 place,
        float spacing,
        WorldDefinition world,
        IPlayerRegistry players,
        PlayerWorlds playerWorlds,
        PlayerHandle arriving)
    {
        if (spacing <= 0f)
        {
            return place;
        }

        float reach = spacing * 0.5f;
        IReadOnlyList<PlayerHandle> registered = players.Players;
        for (int slot = 0; slot < MaxSlots; slot++)
        {
            Vector2 position = SlotPosition(place, slot, spacing);
            if (!IsTaken(position, reach * reach, world, registered, playerWorlds, arriving))
            {
                return position;
            }
        }

        return place;
    }

    /// <summary>
    /// Whether a player of <paramref name="world"/> other than <paramref name="arriving"/> stands within the square
    /// root of <paramref name="squaredReach"/> of <paramref name="position"/>.
    /// </summary>
    private static bool IsTaken(
        Vector2 position,
        float squaredReach,
        WorldDefinition world,
        IReadOnlyList<PlayerHandle> registered,
        PlayerWorlds playerWorlds,
        PlayerHandle arriving)
    {
        for (int i = 0; i < registered.Count; i++)
        {
            PlayerHandle other = registered[i];
            if (other == arriving || other.Transform == null || playerWorlds.Get(other) != world)
            {
                continue;
            }

            if (((Vector2)other.Transform.position - position).sqrMagnitude < squaredReach)
            {
                return true;
            }
        }

        return false;
    }
}
