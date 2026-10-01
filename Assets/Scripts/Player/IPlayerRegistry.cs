using System;
using System.Collections.Generic;

public interface IPlayerRegistry
{
    event Action<PlayerHandle> PlayerAdded;
    event Action<PlayerHandle> PlayerRemoved;

    IReadOnlyList<PlayerHandle> Players { get; }
    bool AnyAlive { get; }

    bool Contains(PlayerHandle player);
}
