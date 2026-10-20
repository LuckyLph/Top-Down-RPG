using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The rules of waystones (Docs/WorldStreaming.md: Death and respawn), plain and free of the session: a waystone is a
/// world entry a player discovers by walking within a few cells of it, and a world's default entry counts as discovered
/// from the start. A dead player respawns at the discovered waystone of its own world nearest in a straight line to
/// where it died; a tie goes to the entry the world lists first. Discoveries are a player's list of
/// <see cref="GameSaveData.Waystone"/>s (world id and entry name), as the save keeps them.
/// </summary>
public static class WaystoneRules
{
    /// <summary>
    /// Whether <paramref name="entry"/> of <paramref name="world"/> counts as discovered by a player who discovered
    /// <paramref name="discovered"/>: it is the world's default entry, or a waystone the list names.
    /// </summary>
    public static bool IsDiscovered(WorldDefinition world, WorldDefinition.Entry entry, IReadOnlyList<GameSaveData.Waystone> discovered)
    {
        if (world == null || entry == null)
        {
            return false;
        }

        return entry == world.DefaultEntry || (entry.Waystone && Contains(discovered, world.WorldId, entry.Name));
    }

    /// <summary>
    /// Whether <paramref name="waystones"/> names the entry <paramref name="entryName"/> of the world
    /// <paramref name="worldId"/>.
    /// </summary>
    public static bool Contains(IReadOnlyList<GameSaveData.Waystone> waystones, string worldId, string entryName)
    {
        if (waystones == null)
        {
            return false;
        }

        for (int i = 0; i < waystones.Count; i++)
        {
            GameSaveData.Waystone waystone = waystones[i];
            if (waystone != null && waystone.WorldId == worldId && waystone.Entry == entryName)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The entry of <paramref name="world"/> a dead player at the world position <paramref name="position"/> respawns
    /// at: the discovered one (<see cref="IsDiscovered"/>) nearest in a straight line, the first listed on a tie. False
    /// when the world has no entry.
    /// </summary>
    public static bool TryFindNearest(WorldDefinition world, Vector2 position, IReadOnlyList<GameSaveData.Waystone> discovered, out WorldDefinition.Entry nearest)
    {
        nearest = null;
        if (world == null)
        {
            return false;
        }

        float best = float.PositiveInfinity;
        IReadOnlyList<WorldDefinition.Entry> entries = world.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            WorldDefinition.Entry entry = entries[i];
            if (!IsDiscovered(world, entry, discovered))
            {
                continue;
            }

            float distance = (world.ToWorld(entry.Position) - position).sqrMagnitude;
            if (distance < best)
            {
                best = distance;
                nearest = entry;
            }
        }

        return nearest != null;
    }

    /// <summary>
    /// The first waystone of <paramref name="world"/>, in its list's order, that a player at the world position
    /// <paramref name="position"/> discovers now: one not discovered yet (<see cref="IsDiscovered"/>) whose cell lies
    /// within <paramref name="reachCells"/> cells of the player's, along each axis. False when there is none.
    /// </summary>
    public static bool TryFindDiscovery(WorldDefinition world, Vector2 position, int reachCells, IReadOnlyList<GameSaveData.Waystone> discovered, out WorldDefinition.Entry found)
    {
        found = null;
        if (world == null)
        {
            return false;
        }

        Vector2Int cell = ChunkCoords.CellOf(world.ToLocal(position));
        IReadOnlyList<WorldDefinition.Entry> entries = world.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            WorldDefinition.Entry entry = entries[i];
            if (entry == null || !entry.Waystone || IsDiscovered(world, entry, discovered))
            {
                continue;
            }

            Vector2Int entryCell = ChunkCoords.CellOf(entry.Position);
            if (Mathf.Abs(entryCell.x - cell.x) <= reachCells && Mathf.Abs(entryCell.y - cell.y) <= reachCells)
            {
                found = entry;
                return true;
            }
        }

        return false;
    }
}
