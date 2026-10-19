using System.Threading;
using UnityEngine;

/// <summary>
/// What the gameplay scope provides so <see cref="GameFlow"/> can start a session in a seamless world and end it: the
/// streaming of worlds and chunks around the players, behind the screen fade.
/// </summary>
public interface IWorldSession
{
    /// <summary>
    /// Whether a world session is running: players are in worlds and chunks stream around them.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Puts the players this machine moves into <paramref name="world"/> at its entry <paramref name="entryName"/> (its
    /// default entry when null) and completes once the world and the chunks around them are loaded.
    /// </summary>
    Awaitable EnterAsync(WorldDefinition world, string entryName, CancellationToken cancellation);

    /// <summary>
    /// Ends the session: unloads every chunk and world, one at a time, and completes once nothing is left.
    /// </summary>
    Awaitable LeaveAsync(CancellationToken cancellation);
}
