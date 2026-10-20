using System;
using System.Collections.Generic;

/// <summary>
/// The players in one world: the Gameplay scope's registry narrowed to the players whose <see cref="PlayerWorlds"/>
/// world is this one. A world's scope registers it as its <see cref="IPlayerRegistry"/>, so the mobs its chunks spawn
/// consider only the players in their own world (Docs/WorldStreaming.md: Mobs). A player joins it when it enters the
/// world and leaves it when it leaves the world or the game, raising <see cref="PlayerAdded"/> and
/// <see cref="PlayerRemoved"/>.
/// </summary>
public sealed class WorldPlayers : IPlayerRegistry, IDisposable
{
    private readonly PlayerWorlds playerWorlds;
    private readonly WorldDefinition world;
    private readonly List<PlayerHandle> players = new();

    /// <summary>
    /// Lists the players of <paramref name="allPlayers"/> that <paramref name="worldsOfPlayers"/> puts in
    /// <paramref name="ownWorld"/>, and follows them in and out of it.
    /// </summary>
    public WorldPlayers(IPlayerRegistry allPlayers, PlayerWorlds worldsOfPlayers, WorldDefinition ownWorld)
    {
        playerWorlds = worldsOfPlayers;
        world = ownWorld;
        IReadOnlyList<PlayerHandle> registered = allPlayers.Players;
        for (int i = 0; i < registered.Count; i++)
        {
            if (world != null && playerWorlds.Get(registered[i]) == world)
            {
                players.Add(registered[i]);
            }
        }

        playerWorlds.Changed += Follow;
    }

    public event Action<PlayerHandle> PlayerAdded;
    public event Action<PlayerHandle> PlayerRemoved;

    public WorldDefinition World => world;
    public IReadOnlyList<PlayerHandle> Players => players;

    public bool AnyAlive
    {
        get
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].IsAlive)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Whether <paramref name="player"/> is in this world; false for null.
    /// </summary>
    public bool Contains(PlayerHandle player)
    {
        return player != null && players.Contains(player);
    }

    /// <summary>
    /// Stops following the players' worlds.
    /// </summary>
    public void Dispose()
    {
        playerWorlds.Changed -= Follow;
    }

    /// <summary>
    /// Adds a player that entered this world and removes one that left it.
    /// </summary>
    private void Follow(PlayerHandle player, WorldDefinition playerWorld)
    {
        bool inWorld = world != null && playerWorld == world;
        if (inWorld && !players.Contains(player))
        {
            players.Add(player);
            PlayerAdded?.Invoke(player);
        }
        else if (!inWorld && players.Remove(player))
        {
            PlayerRemoved?.Invoke(player);
        }
    }
}
