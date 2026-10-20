using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gameplay-scope lookup of the navigation a player paths on, for objects that outlive worlds (players). A player in a
/// world (<see cref="PlayerWorlds"/>) paths on that world's <see cref="WorldNavigation"/>, found through
/// <see cref="Worlds"/>; a player in no world has none.
/// </summary>
public sealed class PlayerNavigation
{
    private readonly IPlayerRegistry players;
    private readonly PlayerWorlds playerWorlds;
    private readonly Worlds worlds;

    /// <summary>
    /// Finds players' handles in <paramref name="registry"/>, their worlds in <paramref name="worldsOfPlayers"/> and the
    /// loaded worlds in <paramref name="loadedWorlds"/>.
    /// </summary>
    public PlayerNavigation(IPlayerRegistry registry, PlayerWorlds worldsOfPlayers, Worlds loadedWorlds)
    {
        players = registry;
        playerWorlds = worldsOfPlayers;
        worlds = loadedWorlds;
    }

    /// <summary>
    /// The navigation the registered player at <paramref name="player"/> paths on: its world's while it is in one; null
    /// while that world is not loaded, and for a player in no world.
    /// </summary>
    public INavigation2D Of(Transform player)
    {
        WorldDefinition world = WorldOf(player);
        return world != null && worlds.TryGet(world, out LoadedWorld loaded) ? loaded.Navigation : null;
    }

    /// <summary>
    /// The world of the registered player whose transform is <paramref name="player"/>, or null.
    /// </summary>
    private WorldDefinition WorldOf(Transform player)
    {
        if (player == null)
        {
            return null;
        }

        IReadOnlyList<PlayerHandle> registered = players.Players;
        for (int i = 0; i < registered.Count; i++)
        {
            if (registered[i].Transform == player)
            {
                return playerWorlds.Get(registered[i]);
            }
        }

        return null;
    }
}
