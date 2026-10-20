using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gameplay-scope record of the trips being prepared: for each travelling player, the target world and entry position
/// its arrival is streamed around. <see cref="WorldStreaming"/> streams each as an interest of its own, so the target
/// world and the chunks around the entry load while the player's screen fades out, and drops it once the player stands
/// at the entry in that world (<see cref="HasArrived"/>) or is gone.
/// </summary>
public sealed class TripArrivals
{
    private const int ArrivalReachCells = 2;

    private readonly Dictionary<PlayerHandle, StreamInterest> held = new();
    private readonly List<PlayerHandle> arrived = new();

    public int Count => held.Count;

    /// <summary>
    /// Streams <paramref name="arrival"/> for <paramref name="player"/> until it arrives or is released.
    /// </summary>
    public void Hold(PlayerHandle player, StreamInterest arrival)
    {
        if (player != null && arrival.World != null)
        {
            held[player] = arrival;
        }
    }

    /// <summary>
    /// Whether a trip of <paramref name="player"/> is being prepared or under way.
    /// </summary>
    public bool IsHeld(PlayerHandle player)
    {
        return player != null && held.ContainsKey(player);
    }

    /// <summary>
    /// Stops streaming <paramref name="player"/>'s arrival.
    /// </summary>
    public void Release(PlayerHandle player)
    {
        if (player != null)
        {
            held.Remove(player);
        }
    }

    /// <summary>
    /// Adds to <paramref name="interests"/> the arrival of every travelling player that has not reached it yet, its
    /// world read from <paramref name="playerWorlds"/>, and drops the arrivals of the players that have or are gone.
    /// Allocates nothing once warm.
    /// </summary>
    public void AddInterests(List<StreamInterest> interests, PlayerWorlds playerWorlds)
    {
        if (held.Count == 0)
        {
            return;
        }

        arrived.Clear();
        foreach (KeyValuePair<PlayerHandle, StreamInterest> arrival in held)
        {
            Transform body = arrival.Key.Transform;
            if (body == null || HasArrived(arrival.Value, playerWorlds.Get(arrival.Key), body.position))
            {
                arrived.Add(arrival.Key);
            }
            else
            {
                interests.Add(arrival.Value);
            }
        }

        for (int i = 0; i < arrived.Count; i++)
        {
            held.Remove(arrived[i]);
        }
    }

    /// <summary>
    /// Forgets every arrival, as the session ends.
    /// </summary>
    public void Clear()
    {
        held.Clear();
    }

    /// <summary>
    /// Whether a player in <paramref name="world"/> at <paramref name="position"/> has reached
    /// <paramref name="arrival"/>: it is in the arrival's world within two cells of the entry.
    /// </summary>
    public static bool HasArrived(StreamInterest arrival, WorldDefinition world, Vector2 position)
    {
        if (world == null || world != arrival.World)
        {
            return false;
        }

        Vector2Int cell = ChunkCoords.CellOf(world.ToLocal(position));
        Vector2Int entry = ChunkCoords.CellOf(world.ToLocal(arrival.Position));
        return Mathf.Abs(cell.x - entry.x) <= ArrivalReachCells && Mathf.Abs(cell.y - entry.y) <= ArrivalReachCells;
    }
}
