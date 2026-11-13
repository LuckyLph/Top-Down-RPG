using System;
using System.Threading;
using UnityEngine;

/// <summary>
/// The scene flow as the main menu's session starter and the network session's lifecycle see it (Docs/UiRevamp.md,
/// Phase 9): whether a transition runs and where the game stands, and starting a session or returning to the menu.
/// Implemented by <see cref="GameFlow"/>, so tests can substitute a fake.
/// </summary>
public interface IGameFlow
{
    event Action TransitionFinished;

    bool IsTransitioning { get; }
    bool IsTravelling { get; }
    bool IsInMenu { get; }
    bool IsInGame { get; }
    GameScenes Scenes { get; }

    /// <summary>
    /// Leaves any session and shows the main menu.
    /// </summary>
    Awaitable ShowMainMenuAsync(CancellationToken cancellation = default);

    /// <summary>
    /// Starts a session in <paramref name="world"/> with the local player at <paramref name="position"/>.
    /// </summary>
    Awaitable StartWorldGameAsync(WorldDefinition world, Vector2 position, CancellationToken cancellation = default);

    /// <summary>
    /// Starts a joined client's session in whichever world the host puts its player.
    /// </summary>
    Awaitable StartJoinedGameAsync(CancellationToken cancellation = default);
}
