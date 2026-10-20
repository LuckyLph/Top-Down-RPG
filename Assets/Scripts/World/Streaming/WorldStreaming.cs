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
/// world's scope. It also streams the arrival of every trip under way (<see cref="TripArrivals"/>), so a trip's target
/// world loads while the traveller's screen fades out. It is the <see cref="IWorldSession"/> <see cref="GameFlow"/>
/// enters and leaves worlds through, and that brings the local player to a trip's entry. In a session it runs the
/// client's half of the per-chunk handshake (Docs/WorldStreaming.md: Networking): a client reports each chunk ready
/// once it is loaded, again for the chunks of its new world when its player's trip count changes, and releases each
/// chunk before unloading it, waiting for the host; the host's streamer keeps every chunk a client holds.
/// </summary>
public sealed class WorldStreaming : IWorldSession, IStartable, ITickable, IDisposable
{
    private const int MaxFramesWaitingForPlayer = 600;
    private const float JoinTimeoutSeconds = 60f;

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
    private readonly TripArrivals arrivals;
    private readonly IChunkHandshake handshake;
    private readonly ISessionStatus sessionStatus;
    private readonly List<StreamInterest> interests = new();
    private readonly CancellationTokenSource disposeCancellation = new();
    private bool active;
    private PlayerHandle reportedPlayer;
    private int reportedTrip;

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
        CameraFollow2D cameraFollow,
        TripArrivals arrivals,
        IChunkHandshake chunkHandshake,
        ISessionStatus session)
    {
        streamer = settings.CreateStreamer(chunkHandshake);
        handshake = chunkHandshake;
        sessionStatus = session;
        this.sceneLoader = sceneLoader;
        this.gameplayScope = gameplayScope;
        this.worlds = worlds;
        this.playerWorlds = playerWorlds;
        this.players = players;
        this.localPlayer = localPlayer;
        this.authority = authority;
        this.clock = clock;
        this.cameraFollow = cameraFollow;
        this.arrivals = arrivals;
    }

    public bool IsActive => active;

    /// <summary>
    /// The streamer, for tests that check what is loaded.
    /// </summary>
    internal ChunkStreamer Streamer => streamer;

    /// <summary>
    /// Follows the local player's trips, to report its new world's chunks again.
    /// </summary>
    public void Start()
    {
        playerWorlds.Changed += HandlePlayerWorldChanged;
    }

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

        Vector2 position = EntryPosition(world, entryName);
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
    /// Waits, up to <see cref="JoinTimeoutSeconds"/> of real time, for the host to spawn this machine's player and put
    /// it in a world, snaps the camera to it and completes once that world and the chunks around the player are loaded,
    /// returning the world. Returns null when the session ends first; throws on the timeout.
    /// </summary>
    public async Awaitable<WorldDefinition> JoinAsync(CancellationToken cancellation)
    {
        streamer.Resume();
        active = true;
        float deadline = Time.realtimeSinceStartup + JoinTimeoutSeconds;
        WorldDefinition world = null;
        while (world == null)
        {
            if (!sessionStatus.IsActive)
            {
                return null;
            }

            if (Time.realtimeSinceStartup > deadline)
            {
                throw new InvalidOperationException($"The host placed no player for this machine within {JoinTimeoutSeconds} s.");
            }

            LocalPlayer local = localPlayer.Current;
            world = local != null ? playerWorlds.Get(local.Handle) : null;
            if (world == null)
            {
                await Awaitable.NextFrameAsync(cancellation);
            }
        }

        cameraFollow.SnapToTarget();
        while (!streamer.IsSettled(new StreamInterest(world, localPlayer.Current.Transform.position)))
        {
            if (!sessionStatus.IsActive)
            {
                return null;
            }

            await Awaitable.NextFrameAsync(cancellation);
        }

        return world;
    }

    /// <summary>
    /// Moves the local player, which the trip's authority has put in <paramref name="world"/>, to its entry
    /// <paramref name="entryName"/>, snaps the camera there, and completes once the world and the chunks around the
    /// entry are loaded. The world it left keeps everything for the empty-world grace. Throws without a local player.
    /// </summary>
    public async Awaitable ArriveAsync(WorldDefinition world, string entryName, CancellationToken cancellation)
    {
        if (world == null)
        {
            throw new ArgumentNullException(nameof(world));
        }

        LocalPlayer local = localPlayer.Current;
        if (local == null || local.Controller == null)
        {
            throw new InvalidOperationException($"No local player to bring to {world.name}.");
        }

        if (playerWorlds.Get(local.Handle) != world)
        {
            Debug.LogWarning($"The local player arrives in {world.name} but is recorded in another world.");
        }

        Vector2 position = EntryPosition(world, entryName);
        local.Controller.Teleport(position);
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
        arrivals.Clear();
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
        playerWorlds.Changed -= HandlePlayerWorldChanged;
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    /// <summary>
    /// The world position of <paramref name="world"/>'s entry <paramref name="entryName"/> (its default entry, with a
    /// warning, for an unknown name), or of its middle when it has no entry.
    /// </summary>
    private static Vector2 EntryPosition(WorldDefinition world, string entryName)
    {
        WorldDefinition.Entry entry = world.FindEntry(entryName);
        return world.ToWorld(entry != null ? entry.Position : world.LocalBounds.center);
    }

    /// <summary>
    /// Fills the interests with the players this machine streams for (every registered player in a world when it has
    /// authority, its local player otherwise) and the arrivals of the trips under way.
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

        arrivals.AddInterests(interests, playerWorlds);
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
        handshake.ReportChunkReady(LocalTrip(), ChunkId.Of(world, coords));
    }

    private async Awaitable UnloadChunkAsync(WorldDefinition world, Vector2Int coords, CancellationToken cancellation)
    {
        if (!worlds.TryGet(world, out LoadedWorld loaded) || !loaded.Chunks.ContainsKey(coords))
        {
            return;
        }

        await handshake.ReleaseChunkAsync(ChunkId.Of(world, coords), cancellation);
        if (!worlds.TryGet(world, out loaded) || !loaded.Chunks.TryGetValue(coords, out Scene scene))
        {
            return;
        }

        worlds.RemoveChunk(loaded, coords);
        await sceneLoader.UnloadAsync(scene, cancellation);
    }

    /// <summary>
    /// The local player's trip count, which this machine's chunk reports carry.
    /// </summary>
    private int LocalTrip()
    {
        LocalPlayer local = localPlayer.Current;
        return local != null ? playerWorlds.TripOf(local.Handle) : 0;
    }

    /// <summary>
    /// When the local player's trip count changes, reports again every loaded chunk of its new world with the new count:
    /// a chunk kept from an earlier visit is up here, and its report from before the trip may have been ignored.
    /// </summary>
    private void HandlePlayerWorldChanged(PlayerHandle player, WorldDefinition world)
    {
        LocalPlayer local = localPlayer.Current;
        if (local == null || player != local.Handle)
        {
            return;
        }

        int trip = playerWorlds.TripOf(player);
        bool newTrip = player != reportedPlayer || trip != reportedTrip;
        reportedPlayer = player;
        reportedTrip = trip;
        if (!newTrip || world == null || !worlds.TryGet(world, out LoadedWorld loaded))
        {
            return;
        }

        foreach (Vector2Int coords in loaded.Chunks.Keys)
        {
            handshake.ReportChunkReady(trip, ChunkId.Of(world, coords));
        }
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
