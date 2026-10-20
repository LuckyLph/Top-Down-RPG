using System;
using System.Collections.Generic;

/// <summary>
/// Gameplay-scope record of the world each registered player is in (null for a player in no world, as in an area
/// session), with its trip count and the entry its latest trip leads to. The trip count goes up by one with every trip
/// the trip's authority approves (<see cref="Travel"/>), so the per-chunk readiness can tell reports from before a trip
/// from those after it. Offline and on the host it is set directly; on a client it follows each player's networked
/// world (<see cref="PlayerWorldSync"/>, Docs/WorldStreaming.md: Networking).
/// </summary>
public sealed class PlayerWorlds : IDisposable
{
    private struct Record
    {
        public WorldDefinition World;
        public int Trip;
        public string Entry;
    }

    private readonly IPlayerRegistry players;
    private readonly Dictionary<PlayerHandle, Record> records = new();

    /// <summary>
    /// Creates the record and forgets players as they leave <paramref name="registry"/>.
    /// </summary>
    public PlayerWorlds(IPlayerRegistry registry)
    {
        players = registry;
        players.PlayerRemoved += Forget;
    }

    /// <summary>
    /// Raised with the player and its new world whenever its world or its trip count changes.
    /// </summary>
    public event Action<PlayerHandle, WorldDefinition> Changed;

    /// <summary>
    /// The world <paramref name="player"/> is in, or null.
    /// </summary>
    public WorldDefinition Get(PlayerHandle player)
    {
        return player != null && records.TryGetValue(player, out Record record) ? record.World : null;
    }

    /// <summary>
    /// How many trips <paramref name="player"/> has been approved since it entered the game; 0 for a player not
    /// recorded.
    /// </summary>
    public int TripOf(PlayerHandle player)
    {
        return player != null && records.TryGetValue(player, out Record record) ? record.Trip : 0;
    }

    /// <summary>
    /// The name of the entry <paramref name="player"/>'s latest trip leads to; empty before its first trip.
    /// </summary>
    public string EntryOf(PlayerHandle player)
    {
        return player != null && records.TryGetValue(player, out Record record) && record.Entry != null ? record.Entry : string.Empty;
    }

    /// <summary>
    /// Puts <paramref name="player"/> in <paramref name="world"/> (null for none), keeping its trip count, and raises
    /// <see cref="Changed"/> when that changes its world. Taking a player out of every world forgets its trips.
    /// </summary>
    public void Set(PlayerHandle player, WorldDefinition world)
    {
        if (player == null || Get(player) == world)
        {
            return;
        }

        if (world == null)
        {
            records.Remove(player);
        }
        else
        {
            records.TryGetValue(player, out Record record);
            record.World = world;
            records[player] = record;
        }

        Changed?.Invoke(player, world);
    }

    /// <summary>
    /// Puts <paramref name="player"/> in <paramref name="world"/> for a trip to its entry <paramref name="entryName"/>,
    /// counting one more trip, and raises <see cref="Changed"/>, even when the trip leads back into the same world.
    /// Nothing for a null player or world.
    /// </summary>
    public void Travel(PlayerHandle player, WorldDefinition world, string entryName)
    {
        if (player == null || world == null)
        {
            return;
        }

        records.TryGetValue(player, out Record record);
        Apply(player, world, record.Trip + 1, entryName);
    }

    /// <summary>
    /// Sets <paramref name="player"/>'s world, trip count and trip entry as another machine decided them (a client
    /// following the host), raising <see cref="Changed"/> when the world or the trip count changes. A null world takes
    /// the player out of every world.
    /// </summary>
    public void Apply(PlayerHandle player, WorldDefinition world, int trip, string entryName)
    {
        if (player == null)
        {
            return;
        }

        if (world == null)
        {
            Set(player, null);
            return;
        }

        bool known = records.TryGetValue(player, out Record previous);
        records[player] = new Record { World = world, Trip = trip, Entry = entryName ?? string.Empty };
        if (!known || previous.World != world || previous.Trip != trip)
        {
            Changed?.Invoke(player, world);
        }
    }

    /// <summary>
    /// Stops forgetting players as they leave.
    /// </summary>
    public void Dispose()
    {
        players.PlayerRemoved -= Forget;
    }

    private void Forget(PlayerHandle player)
    {
        Set(player, null);
    }
}
