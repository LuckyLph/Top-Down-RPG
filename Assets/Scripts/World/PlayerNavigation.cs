using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gameplay-scope lookup of the navigation a player paths on, for objects that outlive areas and worlds (players). A
/// player in a world (<see cref="PlayerWorlds"/>) paths on that world's <see cref="WorldNavigation"/>, found through
/// <see cref="Worlds"/>; a player in no world, in an area session, paths on the area's grid, which
/// <see cref="AreaEntry"/> sets and clears as the area comes and goes.
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
    /// The navigation of the current area; null outside an area session or for an area without a grid.
    /// </summary>
    public INavigation2D Area { get; private set; }

    /// <summary>
    /// Makes <paramref name="navigation"/> the current area's, replacing any previous one.
    /// </summary>
    public void SetArea(INavigation2D navigation)
    {
        Area = navigation;
    }

    /// <summary>
    /// Clears the area's navigation if it is still <paramref name="navigation"/>, leaving a newer area's alone.
    /// </summary>
    public void ClearArea(INavigation2D navigation)
    {
        if (ReferenceEquals(Area, navigation))
        {
            Area = null;
        }
    }

    /// <summary>
    /// The navigation the registered player at <paramref name="player"/> paths on: its world's while it is in one (null
    /// while that world is not loaded), the area's otherwise.
    /// </summary>
    public INavigation2D Of(Transform player)
    {
        WorldDefinition world = WorldOf(player);
        if (world == null)
        {
            return Area;
        }

        return worlds.TryGet(world, out LoadedWorld loaded) ? loaded.Navigation : null;
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
