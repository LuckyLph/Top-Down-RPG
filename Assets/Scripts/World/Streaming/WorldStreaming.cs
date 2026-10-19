using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

/// <summary>
/// Gameplay-scope driver of seamless worlds: each frame it tells its <see cref="ChunkStreamer"/> where the players
/// this machine streams for stand (every player offline or on the host, its own player on a client), and runs the
/// streamer's next load or unload, one at a time: a world's root scene under the Gameplay scope, a chunk under its
/// world's scope. It is the <see cref="IWorldSession"/> <see cref="GameFlow"/> enters and leaves worlds through.
/// </summary>
public sealed class WorldStreaming : IWorldSession, ITickable, IDisposable
{
    private const int MaxFramesWaitingForPlayer = 600;

    private readonly ChunkStreamer streamer;
    private readonly SceneLoader sceneLoader;
    private readonly LifetimeScope gameplayScope;
    private readonly Worlds worlds;
    private readonly PlayerWorlds playerWorlds;
    private readonly IPlayerRegistry players;
    private readonly LocalPlayerTracker localPlayer;
    private readonly IGameAuthority authority;
    private readonly IClock clock;
    private readonly CameraFollow2D cameraFollow;
    private readonly List<StreamInterest> interests = new();
    private readonly CancellationTokenSource disposeCancellation = new();
    private bool active;

    /// <summary>
    /// Creates the driver with a streamer made from <paramref name="settings"/>, loading through
    /// <paramref name="sceneLoader"/> with <paramref name="gameplayScope"/> as the parent of every world's scope.
    /// </summary>
    public WorldStreaming(
        WorldStreamingSettings settings,
        SceneLoader sceneLoader,
        LifetimeScope gameplayScope,
        Worlds worlds,
        PlayerWorlds playerWorlds,
        IPlayerRegistry players,
        LocalPlayerTracker localPlayer,
        IGameAuthority authority,
        IClock clock,
        CameraFollow2D cameraFollow)
    {
        streamer = settings.CreateStreamer();
        this.sceneLoader = sceneLoader;
        this.gameplayScope = gameplayScope;
        this.worlds = worlds;
        this.playerWorlds = playerWorlds;
        this.players = players;
        this.localPlayer = localPlayer;
        this.authority = authority;
        this.clock = clock;
        this.cameraFollow = cameraFollow;
    }

    public bool IsActive => active;

    /// <summary>
    /// The streamer, for tests that check what is loaded.
    /// </summary>
    internal ChunkStreamer Streamer => streamer;

    /// <summary>
    /// Updates the streamer with where the streamed players stand and starts its next action when none is running.
    /// </summary>
    public void Tick()
    {
        if (active)
        {
            CollectInterests();
            streamer.SetInterests(interests, clock.Time);
        }

        if (!streamer.IsBusy && streamer.TryNext(out StreamAction action))
        {
            _ = RunAsync(action, disposeCancellation.Token);
        }
    }

    /// <summary>
    /// Moves the players this machine moves into <paramref name="world"/> at its entry <paramref name="entryName"/>,
    /// snaps the camera there, and completes once the world and the chunks around the local player are loaded. Throws
    /// when no local player appears within <see cref="MaxFramesWaitingForPlayer"/> frames.
    /// </summary>
    public async Awaitable EnterAsync(WorldDefinition world, string entryName, CancellationToken cancellation)
    {
        if (world == null)
        {
            throw new ArgumentNullException(nameof(world));
        }

        streamer.Resume();
        active = true;
        for (int frame = 0; localPlayer.Current == null; frame++)
        {
            if (frame >= MaxFramesWaitingForPlayer)
            {
                throw new InvalidOperationException($"No local player appeared to enter {world.name} with.");
            }

            await Awaitable.NextFrameAsync(cancellation);
        }

        WorldDefinition.Entry entry = world.FindEntry(entryName);
        Vector2 position = world.ToWorld(entry != null ? entry.Position : world.LocalBounds.center);
        IReadOnlyList<PlayerHandle> party = players.Players;
        for (int i = 0; i < party.Count; i++)
        {
            if (party[i].Transform != null && party[i].Transform.TryGetComponent(out PlayerController controller) && controller.SimulatesMovement)
            {
                playerWorlds.Set(party[i], world);
                controller.Teleport(position);
            }
        }

        cameraFollow.SnapToTarget();
        StreamInterest arrival = new(world, position);
        while (!streamer.IsSettled(arrival))
        {
            await Awaitable.NextFrameAsync(cancellation);
        }
    }

    /// <summary>
    /// Stops streaming and unloads every chunk and world, one at a time, then takes every player out of its world.
    /// </summary>
    public async Awaitable LeaveAsync(CancellationToken cancellation)
    {
        active = false;
        streamer.ReleaseAll();
        while (!streamer.IsEmpty)
        {
            await Awaitable.NextFrameAsync(cancellation);
        }

        IReadOnlyList<PlayerHandle> party = players.Players;
        for (int i = party.Count - 1; i >= 0; i--)
        {
            playerWorlds.Set(party[i], null);
        }
    }

    /// <summary>
    /// Stops any load or unload still running. The scenes themselves go when the session's scenes unload.
    /// </summary>
    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    /// <summary>
    /// Fills the interests with the players this machine streams for: every registered player in a world when it has
    /// authority, its local player otherwise.
    /// </summary>
    private void CollectInterests()
    {
        interests.Clear();
        IReadOnlyList<PlayerHandle> party = players.Players;
        PlayerHandle local = localPlayer.Current != null ? localPlayer.Current.Handle : null;
        for (int i = 0; i < party.Count; i++)
        {
            PlayerHandle player = party[i];
            if (player.Transform == null || (!authority.IsAuthoritative && player != local))
            {
                continue;
            }

            WorldDefinition world = playerWorlds.Get(player);
            if (world != null)
            {
                interests.Add(new StreamInterest(world, player.Transform.position));
            }
        }
    }

    /// <summary>
    /// Runs one of the streamer's actions and tells it how it ended. A failure is logged; the streamer does not try a
    /// failed load again.
    /// </summary>
    private async Awaitable RunAsync(StreamAction action, CancellationToken cancellation)
    {
        bool success = false;
        try
        {
            switch (action.Kind)
            {
                case StreamActionKind.LoadWorld:
                    await LoadWorldAsync(action.World, cancellation);
                    break;
                case StreamActionKind.LoadChunk:
                    await LoadChunkAsync(action.World, action.Chunk, cancellation);
                    break;
                case StreamActionKind.UnloadChunk:
                    await UnloadChunkAsync(action.World, action.Chunk, cancellation);
                    break;
                case StreamActionKind.UnloadWorld:
                    await UnloadWorldAsync(action.World, cancellation);
                    break;
            }

            success = true;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            Debug.LogError($"{nameof(WorldStreaming)} could not {action}: {exception.Message}");
            Debug.LogException(exception);
        }

        streamer.Complete(success);
    }

    private async Awaitable LoadWorldAsync(WorldDefinition world, CancellationToken cancellation)
    {
        Scene scene;
        using (LifetimeScope.Enqueue(builder => builder.RegisterInstance(world)))
        using (LifetimeScope.EnqueueParent(gameplayScope))
        {
            scene = await sceneLoader.LoadAdditiveAsync(world.RootScene, cancellation);
        }

        LifetimeScope scope = LifetimeScope.Find<WorldLifetimeScope>(scene);
        if (scope == null)
        {
            Debug.LogError($"{world.name}'s root scene has no {nameof(WorldLifetimeScope)}; its chunks get no scope parent.", world);
        }

        WorldNavigation navigation = null;
        if (scope != null && scope.Container != null)
        {
            scope.Container.TryResolve(out navigation);
        }

        worlds.Add(new LoadedWorld(world, scene, scope, SceneQuery.FindFirst<WorldLighting>(scene), navigation));
    }

    private async Awaitable LoadChunkAsync(WorldDefinition world, Vector2Int coords, CancellationToken cancellation)
    {
        if (!worlds.TryGet(world, out LoadedWorld loaded) || !world.TryGetChunkScene(coords, out SceneDefinition chunkScene))
        {
            throw new InvalidOperationException($"{world.name} is not loaded or has no chunk at {coords}.");
        }

        ChunkIdentity identity = new(world, coords);
        Scene scene;
        using (LifetimeScope.Enqueue(builder => builder.RegisterInstance(identity)))
        using (LifetimeScope.EnqueueParent(loaded.Scope != null ? loaded.Scope : gameplayScope))
        {
            scene = await sceneLoader.LoadAdditiveAsync(chunkScene, cancellation);
        }

        worlds.AddChunk(loaded, coords, scene);
    }

    private async Awaitable UnloadChunkAsync(WorldDefinition world, Vector2Int coords, CancellationToken cancellation)
    {
        if (!worlds.TryGet(world, out LoadedWorld loaded) || !loaded.Chunks.TryGetValue(coords, out Scene scene))
        {
            return;
        }

        worlds.RemoveChunk(loaded, coords);
        await sceneLoader.UnloadAsync(scene, cancellation);
    }

    private async Awaitable UnloadWorldAsync(WorldDefinition world, CancellationToken cancellation)
    {
        if (!worlds.TryGet(world, out LoadedWorld loaded))
        {
            return;
        }

        worlds.Remove(loaded);
        await sceneLoader.UnloadAsync(loaded.Scene, cancellation);
    }
}
