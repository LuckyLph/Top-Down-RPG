using System;
using System.Collections.Generic;

/// <summary>
/// The players in one world that its mobs consider: the Gameplay scope's registry narrowed to the players whose
/// <see cref="PlayerWorlds"/> world is this one and that no trip protects (<see cref="TripProtection"/>). A world's scope
/// registers it as its <see cref="IPlayerRegistry"/>, so the mobs its chunks spawn consider only the players in their
/// own world (Docs/WorldStreaming.md: Mobs) and never a traveller before its machine has faded in. A player joins it
/// when it enters the world or its protection ends, and leaves it when it leaves the world or the game or a trip
/// protects it, raising <see cref="PlayerAdded"/> and <see cref="PlayerRemoved"/>.
/// </summary>
public sealed class WorldPlayers : IPlayerRegistry, IDisposable
{
    private readonly PlayerWorlds playerWorlds;
    private readonly TripProtection protection;
    private readonly WorldDefinition world;
    private readonly List<PlayerHandle> players = new();

    /// <summary>
    /// Lists the players of <paramref name="allPlayers"/> that <paramref name="worldsOfPlayers"/> puts in
    /// <paramref name="ownWorld"/> and <paramref name="tripProtection"/> leaves in play, and follows them in and out.
    /// </summary>
    public WorldPlayers(IPlayerRegistry allPlayers, PlayerWorlds worldsOfPlayers, TripProtection tripProtection, WorldDefinition ownWorld)
    {
        playerWorlds = worldsOfPlayers;
        protection = tripProtection;
        world = ownWorld;
        IReadOnlyList<PlayerHandle> registered = allPlayers.Players;
        for (int i = 0; i < registered.Count; i++)
        {
            if (Belongs(registered[i]))
            {
                players.Add(registered[i]);
            }
        }

        playerWorlds.Changed += Follow;
        protection.Changed += Follow;
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
    /// Whether <paramref name="player"/> is in this world and in play; false for null.
    /// </summary>
    public bool Contains(PlayerHandle player)
    {
        return player != null && players.Contains(player);
    }

    /// <summary>
    /// Stops following the players' worlds and protections.
    /// </summary>
    public void Dispose()
    {
        playerWorlds.Changed -= Follow;
        protection.Changed -= Follow;
    }

    /// <summary>
    /// Whether <paramref name="player"/> is in this world and no trip protects it.
    /// </summary>
    private bool Belongs(PlayerHandle player)
    {
        return world != null && playerWorlds.Get(player) == world && !protection.IsProtected(player);
    }

    /// <summary>
    /// Adds a player that entered this world or came back into play, and removes one that left it or went out of play.
    /// </summary>
    private void Follow(PlayerHandle player, WorldDefinition playerWorld)
    {
        Follow(player);
    }

    /// <summary>
    /// Adds or removes <paramref name="player"/> as <see cref="Belongs"/> now says.
    /// </summary>
    private void Follow(PlayerHandle player)
    {
        bool belongs = Belongs(player);
        if (belongs && !players.Contains(player))
        {
            players.Add(player);
            PlayerAdded?.Invoke(player);
        }
        else if (!belongs && players.Remove(player))
        {
            PlayerRemoved?.Invoke(player);
        }
    }
}
