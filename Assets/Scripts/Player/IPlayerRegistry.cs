using System;
using System.Collections.Generic;

/// <summary>
/// Read-only view of the players in the current Gameplay scope, local and remote, in the order they were added.
/// </summary>
public interface IPlayerRegistry
{
    event Action<PlayerHandle> PlayerAdded;
    event Action<PlayerHandle> PlayerRemoved;

    IReadOnlyList<PlayerHandle> Players { get; }
    bool AnyAlive { get; }

    /// <summary>
    /// Whether <paramref name="player"/> is currently registered; false for null.
    /// </summary>
    bool Contains(PlayerHandle player);
}
