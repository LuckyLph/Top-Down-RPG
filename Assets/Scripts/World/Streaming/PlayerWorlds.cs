using System;
using System.Collections.Generic;

/// <summary>
/// Gameplay-scope record of the world each registered player is in; null for a player in no world (an area session).
/// Offline it is set directly; online the host's value reaches every machine (Docs/WorldStreaming.md: Networking).
/// </summary>
public sealed class PlayerWorlds : IDisposable
{
    private readonly IPlayerRegistry players;
    private readonly Dictionary<PlayerHandle, WorldDefinition> worlds = new();

    /// <summary>
    /// Creates the record and forgets players as they leave <paramref name="registry"/>.
    /// </summary>
    public PlayerWorlds(IPlayerRegistry registry)
    {
        players = registry;
        players.PlayerRemoved += Forget;
    }

    public event Action<PlayerHandle, WorldDefinition> Changed;

    /// <summary>
    /// The world <paramref name="player"/> is in, or null.
    /// </summary>
    public WorldDefinition Get(PlayerHandle player)
    {
        return player != null && worlds.TryGetValue(player, out WorldDefinition world) ? world : null;
    }

    /// <summary>
    /// Puts <paramref name="player"/> in <paramref name="world"/> (null for none) and raises <see cref="Changed"/> when
    /// that changes its world.
    /// </summary>
    public void Set(PlayerHandle player, WorldDefinition world)
    {
        if (player == null || Get(player) == world)
        {
            return;
        }

        if (world == null)
        {
            worlds.Remove(player);
        }
        else
        {
            worlds[player] = world;
        }

        Changed?.Invoke(player, world);
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
