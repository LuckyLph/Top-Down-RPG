using System;
using System.Collections.Generic;

public sealed class PlayerRegistry : IPlayerRegistry
{
    private readonly List<PlayerHandle> players = new();

    public event Action<PlayerHandle> PlayerAdded;
    public event Action<PlayerHandle> PlayerRemoved;

    public IReadOnlyList<PlayerHandle> Players => players;

    public bool AnyAlive
    {
        get
        {
            foreach (PlayerHandle player in players)
            {
                if (player.IsAlive)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public bool Contains(PlayerHandle player)
    {
        return player != null && players.Contains(player);
    }

    public bool Add(PlayerHandle player)
    {
        if (player == null || players.Contains(player))
        {
            return false;
        }

        players.Add(player);
        PlayerAdded?.Invoke(player);
        return true;
    }

    public bool Remove(PlayerHandle player)
    {
        if (player == null || !players.Remove(player))
        {
            return false;
        }

        PlayerRemoved?.Invoke(player);
        return true;
    }
}
