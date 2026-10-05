using System;
using System.Collections.Generic;

/// <summary>
/// The Gameplay scope's list of players, written by <see cref="PlayerBinder"/> as players are bound and unbound and
/// read by everything else through <see cref="IPlayerRegistry"/>.
/// </summary>
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

    /// <summary>
    /// Whether <paramref name="player"/> is currently registered; false for null.
    /// </summary>
    public bool Contains(PlayerHandle player)
    {
        return player != null && players.Contains(player);
    }

    /// <summary>
    /// Appends <paramref name="player"/> and raises <see cref="PlayerAdded"/>. False, with no event, for null or an
    /// already registered player.
    /// </summary>
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

    /// <summary>
    /// Removes <paramref name="player"/> and raises <see cref="PlayerRemoved"/>; later players shift down a slot.
    /// False, with no event, for null or a player that is not registered.
    /// </summary>
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
