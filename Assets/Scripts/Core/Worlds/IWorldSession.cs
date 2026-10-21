using System.Threading;
using UnityEngine;

/// <summary>
/// What the gameplay scope provides so <see cref="GameFlow"/> can start a session in a seamless world (or join the
/// host's), take the local player to another world's entry and end the session, and so the host can load a joining
/// player's world before placing it there: the streaming of worlds and chunks around the players, behind the screen
/// fade.
/// </summary>
public interface IWorldSession
{
    /// <summary>
    /// Whether a world session is running: players are in worlds and chunks stream around them.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Puts the players this machine moves into <paramref name="world"/> at the world position
    /// <paramref name="position"/> and completes once the world and the chunks around them are loaded. With authority,
    /// a position the local player can no longer stand on (a saved place whose ground changed) moves them on to walkable
    /// ground first.
    /// </summary>
    Awaitable EnterAsync(WorldDefinition world, Vector2 position, CancellationToken cancellation);

    /// <summary>
    /// Loads <paramref name="world"/> and the chunks around <paramref name="position"/> for this machine's simulation
    /// without moving anyone, as the host does before spawning the joining player <paramref name="playerId"/> there,
    /// and completes once they are loaded, or at once when no world session runs. Returns where to place that player:
    /// the position, or walkable ground near it (loaded too) when the player can no longer stand there.
    /// </summary>
    Awaitable<Vector2> PrepareAsync(WorldDefinition world, Vector2 position, string playerId, CancellationToken cancellation);

    /// <summary>
    /// Starts a joined client's session: waits for the host to spawn this machine's player and put it in a world, then
    /// for that world and the chunks around the player to load, and returns the world. Returns null when the session
    /// ends first; throws when no player is placed within the wait.
    /// </summary>
    Awaitable<WorldDefinition> JoinAsync(CancellationToken cancellation);

    /// <summary>
    /// Ends a trip of the local player, already put in <paramref name="world"/> by the trip's authority: moves it to
    /// the entry <paramref name="entryName"/> (the default entry when there is none of that name) and completes once
    /// the world and the chunks around it are loaded. The worlds it left stay loaded for the empty-world grace.
    /// </summary>
    Awaitable ArriveAsync(WorldDefinition world, string entryName, CancellationToken cancellation);

    /// <summary>
    /// Ends the session: unloads every chunk and world, one at a time, and completes once nothing is left.
    /// </summary>
    Awaitable LeaveAsync(CancellationToken cancellation);
}
