using System;
using System.Threading;
using UnityEngine;

/// <summary>
/// <see cref="IGameFlow"/> whose state tests set directly and whose starts are recorded instead of loading scenes; every
/// start completes at once, and <see cref="FinishTransition"/> raises the transition's end.
/// </summary>
public sealed class FakeGameFlow : IGameFlow
{
    public event Action TransitionFinished;

    public bool IsTransitioning { get; set; }
    public bool IsTravelling { get; set; }
    public bool IsInMenu { get; set; } = true;
    public bool IsInGame { get; set; }
    public GameScenes Scenes { get; set; }
    public int MenusShown { get; private set; }
    public int WorldStarts { get; private set; }
    public int JoinedStarts { get; private set; }
    public WorldDefinition StartedWorld { get; private set; }
    public Vector2 StartedPosition { get; private set; }

    /// <summary>
    /// Counts the request to show the menu.
    /// </summary>
    public Awaitable ShowMainMenuAsync(CancellationToken cancellation = default)
    {
        MenusShown++;
        return Completed();
    }

    /// <summary>
    /// Records the world and position a session started at.
    /// </summary>
    public Awaitable StartWorldGameAsync(WorldDefinition world, Vector2 position, CancellationToken cancellation = default)
    {
        WorldStarts++;
        StartedWorld = world;
        StartedPosition = position;
        return Completed();
    }

    /// <summary>
    /// Counts the joined game's start.
    /// </summary>
    public Awaitable StartJoinedGameAsync(CancellationToken cancellation = default)
    {
        JoinedStarts++;
        return Completed();
    }

    /// <summary>
    /// Raises <see cref="TransitionFinished"/>.
    /// </summary>
    public void FinishTransition()
    {
        TransitionFinished?.Invoke();
    }

    /// <summary>
    /// An awaitable already done.
    /// </summary>
    private static Awaitable Completed()
    {
        AwaitableCompletionSource source = new();
        source.SetResult();
        return source.Awaitable;
    }
}
