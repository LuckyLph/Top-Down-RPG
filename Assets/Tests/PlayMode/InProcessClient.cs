using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Multiplayer.Tools.NetworkSimulator.Runtime;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using Object = UnityEngine.Object;

/// <summary>
/// A second Netcode client running in the host's process for PlayMode tests: a clone of the host's
/// <see cref="NetworkManager"/> connected over loopback, with its own hand-built container of client-side services. It
/// shares the host's scenes, so it loads no world of its own: it sends the join and the per-chunk readiness and release
/// messages a real client's streaming would, for the chunks the host has loaded, and records the host's
/// acknowledgements and the home chunk each chunk object arrives with.
/// </summary>
public sealed class InProcessClient : IDisposable
{
    private readonly IObjectResolver container;
    private readonly NetworkSession host;

    /// <summary>
    /// Captures the started client's manager, container and client-side services.
    /// </summary>
    private InProcessClient(NetworkSession host, NetworkManager manager, IObjectResolver container, PlayerRegistry players, LocalPlayerTracker localPlayer, TestInput input)
    {
        this.host = host;
        Manager = manager;
        this.container = container;
        Players = players;
        LocalPlayer = localPlayer;
        Input = input;
    }

    public NetworkManager Manager { get; }
    public PlayerRegistry Players { get; }
    public LocalPlayerTracker LocalPlayer { get; }
    public TestInput Input { get; }
    public RecordingCameraKick CameraKick { get; private set; }
    public List<ChunkId> Released { get; } = new();
    public Dictionary<NetworkObject, ChunkId> HomeChunks { get; } = new();

    /// <summary>
    /// Resolves a service from the client's own container rather than the host's scopes.
    /// </summary>
    public T Resolve<T>()
    {
        return container.Resolve<T>();
    }

    /// <summary>
    /// Builds the client container (non-authoritative, scripted input, combat, ability and trip services, an empty
    /// world navigation), registers injecting prefab handlers for the player and, routed by the home chunk the host
    /// sends, for the mobs of the host's loaded chunks and the terrain sync object, then starts a cloned manager against
    /// the host's port and records the host's release acknowledgements. It joins with <paramref name="playerId"/>, or a
    /// new id when null. Asserts the start succeeds.
    /// </summary>
    public static InProcessClient Start(NetworkSession session, NetworkObject playerPrefab, string playerId = null)
    {
        Camera camera = Camera.main;
        PlayerControlSettings controlSettings = playerPrefab.GetComponent<PlayerController>().ControlSettings;
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        CombatSettings combatSettings = gameplay.Resolve<CombatSettings>();
        GameScenes gameScenes = gameplay.Resolve<GameScenes>();

        TestInput input = new();
        PlayerRegistry players = new();
        LocalPlayerTracker localPlayer = new();
        RecordingCameraKick cameraKick = new();
        IObjectResolver container = BuildContainer(players, localPlayer, input, camera, controlSettings, combatSettings, gameScenes, new ClientStatus(), gameplay.Resolve<HitFeedbackSettings>(), cameraKick);
        container.Resolve<HitFeedbackPresenter>().Start();

        NetworkManager manager = CloneNetworkManager(session, "InProcessClient");
        GameObject clientObject = manager.gameObject;
        clientObject.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", session.Settings.Port);
        manager.NetworkConfig.ConnectionData = JoinApproval.CreatePayload(Application.version, playerId ?? Guid.NewGuid().ToString("N"));
        manager.PrefabHandler.AddHandler(playerPrefab, new PersistentCopyHandler(new InjectingNetworkPrefabHandler(playerPrefab, container)));
        InProcessClient client = new(session, manager, container, players, localPlayer, input) { CameraKick = cameraKick };
        HashSet<NetworkObject> chunkPrefabs = new();
        foreach (MobSpawnPoint spawnPoint in Object.FindObjectsByType<MobSpawnPoint>())
        {
            NetworkObject mobPrefab = spawnPoint.MobPrefab != null ? spawnPoint.MobPrefab.GetComponent<NetworkObject>() : null;
            if (mobPrefab != null)
            {
                chunkPrefabs.Add(mobPrefab);
            }
        }

        foreach (AreaTerrain terrain in Object.FindObjectsByType<AreaTerrain>())
        {
            if (terrain.SyncPrefab != null)
            {
                chunkPrefabs.Add(terrain.SyncPrefab);
            }
        }

        foreach (NetworkObject prefab in chunkPrefabs)
        {
            manager.PrefabHandler.AddHandler(prefab, new ChunkCopyHandler(client, prefab, container, gameplay.Resolve<Worlds>()));
        }

        Assert.That(manager.StartClient(), Is.True, "The in-process client should start.");
        input.Camera = camera;
        manager.CustomMessagingManager.RegisterNamedMessageHandler(
            NetworkSession.ChunkReleasedMessage,
            (sender, reader) =>
            {
                reader.ReadValueSafe(out NetworkChunkId chunk);
                client.Released.Add(chunk.ToChunkId());
            });
        return client;
    }

    /// <summary>
    /// A client-side container like a real client's Gameplay scope offers player and mob copies: the registry, the
    /// local player, scripted input, combat (projectiles, which nothing ticks here), statuses and abilities, the
    /// players' worlds and trips (with
    /// <paramref name="sessionStatus"/> saying which side of the session it is), an always-ready readiness, an empty
    /// world navigation, so its players walk in straight lines, pooled effects, in-memory local settings (whose look its
    /// own player follows), the night lights' presenter without a day clock (not started, so its own player's lantern
    /// never lights itself and burns only on its key) and, given <paramref name="hitFeedbackSettings"/>, the hit feedback
    /// presenter (not started) kicking <paramref name="cameraKick"/>.
    /// </summary>
    public static IObjectResolver BuildContainer(
        PlayerRegistry players,
        LocalPlayerTracker localPlayer,
        TestInput input,
        Camera camera,
        PlayerControlSettings controlSettings,
        CombatSettings combatSettings,
        GameScenes gameScenes,
        ISessionStatus sessionStatus,
        HitFeedbackSettings hitFeedbackSettings = null,
        ICameraKick cameraKick = null)
    {
        ContainerBuilder builder = new();
        builder.RegisterInstance(players).As<IPlayerRegistry>().AsSelf();
        builder.RegisterInstance(localPlayer);
        builder.RegisterInstance<IPlayerInput>(input);
        builder.RegisterInstance<IClock>(UnityClock.Shared);
        builder.RegisterInstance<IGameAuthority>(new FixedAuthority(!sessionStatus.IsActive || sessionStatus.IsHost));
        builder.RegisterInstance(sessionStatus);
        builder.RegisterInstance(camera);
        builder.RegisterInstance(controlSettings);
        builder.RegisterInstance(gameScenes);
        builder.Register<Worlds>(Lifetime.Singleton);
        builder.Register<PlayerWorlds>(Lifetime.Singleton);
        builder.Register<TripArrivals>(Lifetime.Singleton);
        builder.Register<WorldExits>(Lifetime.Singleton);
        builder.Register<LocalTripAuthority>(Lifetime.Singleton);
        builder.Register<NetworkTripAuthority>(Lifetime.Singleton);
        builder.Register<WaystoneNotices>(Lifetime.Singleton);
        builder.Register<PlayerNavigation>(Lifetime.Singleton);
        builder.Register<PointerTargetPicker>(Lifetime.Singleton);
        builder.Register<LocalPlayerCommandSource>(Lifetime.Singleton);
        builder.Register<PlayerBinder>(Lifetime.Singleton);
        builder.RegisterInstance(combatSettings);
        builder.Register<CombatEvents>(Lifetime.Singleton);
        builder.Register<DamageService>(Lifetime.Singleton);
        builder.Register<StatusEffectService>(Lifetime.Singleton);
        builder.Register<HitService>(Lifetime.Singleton);
        builder.Register<StatusVisualPool>(Lifetime.Singleton).WithParameter(default(Scene));
        builder.RegisterInstance<IClientReadiness>(new ReadyClients());
        builder.Register<AbilityService>(Lifetime.Singleton);
        builder.Register<AttackService>(Lifetime.Singleton);
        builder.Register<ProjectileVisualPool>(Lifetime.Singleton).WithParameter(default(Scene));
        builder.Register<ProjectileService>(Lifetime.Singleton);
        builder.Register<EffectPool>(Lifetime.Singleton).WithParameter(default(Scene));
        if (hitFeedbackSettings != null)
        {
            builder.RegisterInstance(hitFeedbackSettings);
            builder.RegisterInstance(cameraKick ?? new RecordingCameraKick());
            builder.Register<HitFeedbackPresenter>(Lifetime.Singleton);
        }

        builder.RegisterInstance<IRandom>(new SystemRandom(1));
        builder.RegisterInstance<INavigation2D>(new WorldNavigation(new NavigationPagePool()));
        builder.RegisterInstance(new UserSettings(new MemorySaveStore()));
        builder.Register(resolver => new PropLighting(null, resolver.Resolve<IClock>(), resolver.Resolve<Worlds>()), Lifetime.Singleton);
        return builder.Build();
    }

    /// <summary>
    /// Copies the session's <see cref="NetworkManager"/> object under an inactive parent, strips its network simulator
    /// before it wakes, then moves it to the scene root as <paramref name="name"/>. The copy is not started.
    /// </summary>
    public static NetworkManager CloneNetworkManager(NetworkSession session, string name)
    {
        GameObject inactiveParent = new("InactiveParent");
        inactiveParent.SetActive(false);
        GameObject clone = Object.Instantiate(session.NetworkManager.gameObject, inactiveParent.transform);
        clone.name = name;
        if (clone.TryGetComponent(out NetworkSimulator simulator))
        {
            Object.DestroyImmediate(simulator);
        }

        clone.transform.SetParent(null);
        Object.Destroy(inactiveParent);
        return clone.GetComponent<NetworkManager>();
    }

    /// <summary>
    /// Joins, as a real client's Gameplay scope does once it is up, then reports every chunk the host has loaded ready
    /// for trip 0, as a real client does once its world has loaded around its player.
    /// </summary>
    public void SendReady()
    {
        SendJoined();
        foreach (ChunkId chunk in HostChunks())
        {
            SendChunkReady(0, chunk);
        }
    }

    /// <summary>
    /// Tells the host this client's Gameplay scene is up, so it receives the players and its own.
    /// </summary>
    public void SendJoined()
    {
        using FastBufferWriter writer = new(sizeof(byte), Allocator.Temp);
        writer.WriteValueSafe((byte)1);
        Manager.CustomMessagingManager.SendNamedMessage(NetworkSession.ClientJoinedMessage, NetworkManager.ServerClientId, writer, NetworkSession.MessageDelivery);
    }

    /// <summary>
    /// Reports <paramref name="chunk"/> ready during trip <paramref name="trip"/>; a stale trip lets tests check the host
    /// ignores it.
    /// </summary>
    public void SendChunkReady(int trip, ChunkId chunk)
    {
        using FastBufferWriter writer = new(128, Allocator.Temp);
        writer.WriteValueSafe(trip);
        writer.WriteValueSafe(new NetworkChunkId(chunk));
        Manager.CustomMessagingManager.SendNamedMessage(NetworkSession.ChunkReadyMessage, NetworkManager.ServerClientId, writer, NetworkSession.MessageDelivery);
    }

    /// <summary>
    /// Tells the host this client is about to unload <paramref name="chunk"/>; its acknowledgement lands in
    /// <see cref="Released"/>.
    /// </summary>
    public void SendChunkReleasing(ChunkId chunk)
    {
        using FastBufferWriter writer = new(128, Allocator.Temp);
        writer.WriteValueSafe(new NetworkChunkId(chunk));
        Manager.CustomMessagingManager.SendNamedMessage(NetworkSession.ChunkReleasingMessage, NetworkManager.ServerClientId, writer, NetworkSession.MessageDelivery);
    }

    /// <summary>
    /// The ids of every chunk of every world the host has loaded.
    /// </summary>
    public static List<ChunkId> HostChunks()
    {
        List<ChunkId> chunks = new();
        Worlds worlds = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container.Resolve<Worlds>();
        foreach (LoadedWorld world in worlds.Loaded)
        {
            foreach (Vector2Int coords in world.Chunks.Keys)
            {
                chunks.Add(ChunkId.Of(world.Definition, coords));
            }
        }

        return chunks;
    }

    /// <summary>
    /// Disables every collider on the player copies this client knows about, so they do not collide with the host's
    /// objects in the shared physics scene.
    /// </summary>
    public void DisableColliders()
    {
        foreach (PlayerHandle handle in Players.Players)
        {
            foreach (Collider2D playerCollider in handle.Transform.GetComponentsInChildren<Collider2D>())
            {
                playerCollider.enabled = false;
            }
        }
    }

    /// <summary>
    /// Shuts down and destroys the client's manager if it still exists, then disposes the client container.
    /// </summary>
    public void Dispose()
    {
        if (Manager != null)
        {
            Manager.Shutdown();
            Object.Destroy(Manager.gameObject);
        }

        container.Dispose();
    }

    /// <summary>
    /// The player in <paramref name="players"/> whose network object belongs to <paramref name="ownerClientId"/>, or
    /// null when there is none.
    /// </summary>
    public static PlayerController FindCopyOwnedBy(PlayerRegistry players, ulong ownerClientId)
    {
        foreach (PlayerHandle handle in players.Players)
        {
            NetworkObject networkObject = handle.Transform.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.OwnerClientId == ownerClientId)
            {
                return handle.Transform.GetComponent<PlayerController>();
            }
        }

        return null;
    }

    /// <summary>
    /// Prefab handler decorator that keeps the client's spawned copies across scene loads.
    /// </summary>
    private sealed class PersistentCopyHandler : INetworkPrefabInstanceHandler
    {
        private readonly INetworkPrefabInstanceHandler inner;

        /// <summary>
        /// Wraps <paramref name="inner"/>, which does the actual instantiation and destruction.
        /// </summary>
        public PersistentCopyHandler(INetworkPrefabInstanceHandler inner)
        {
            this.inner = inner;
        }

        /// <summary>
        /// Instantiates through the inner handler and marks the copy DontDestroyOnLoad.
        /// </summary>
        public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
        {
            NetworkObject instance = inner.Instantiate(ownerClientId, position, rotation);
            Object.DontDestroyOnLoad(instance.gameObject);
            return instance;
        }

        /// <summary>
        /// Leaves destruction to the inner handler.
        /// </summary>
        public void Destroy(NetworkObject networkObject)
        {
            inner.Destroy(networkObject);
        }
    }

    /// <summary>
    /// The client's handler of a chunk object's prefab (a mob, a terrain sync object): records the home chunk the host
    /// sends with each copy and injects the copy from the client container, given the <see cref="AreaTerrain"/> of that
    /// chunk in the host's (shared) scene, and keeps it across scene loads.
    /// </summary>
    private sealed class ChunkCopyHandler : NetworkPrefabInstanceHandlerWithData<NetworkChunkId>
    {
        private readonly InProcessClient client;
        private readonly NetworkObject prefab;
        private readonly IObjectResolver container;
        private readonly Worlds hostWorlds;

        /// <summary>
        /// Creates the handler of <paramref name="chunkPrefab"/> for <paramref name="owner"/>, injecting from
        /// <paramref name="clientContainer"/> and finding chunk scenes in <paramref name="worlds"/>.
        /// </summary>
        public ChunkCopyHandler(InProcessClient owner, NetworkObject chunkPrefab, IObjectResolver clientContainer, Worlds worlds)
        {
            client = owner;
            prefab = chunkPrefab;
            container = clientContainer;
            hostWorlds = worlds;
        }

        /// <summary>
        /// Creates an injected copy, given the home chunk's terrain when the host has that chunk loaded, records its home
        /// chunk and keeps it across scene loads.
        /// </summary>
        public override NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation, NetworkChunkId homeChunk)
        {
            ChunkId home = homeChunk.ToChunkId();
            AreaTerrain terrain = TerrainOf(home);
            NetworkObject instance;
            using (IScopedObjectResolver scope = container.CreateScope(builder =>
                   {
                       if (terrain != null)
                       {
                           builder.RegisterInstance(terrain);
                       }
                   }))
            {
                instance = new InjectingNetworkPrefabHandler(prefab, scope).Instantiate(ownerClientId, position, rotation);
            }

            Object.DontDestroyOnLoad(instance.gameObject);
            client.HomeChunks[instance] = home;
            return instance;
        }

        /// <summary>
        /// Destroys the despawned copy's game object; ignores one that is already gone.
        /// </summary>
        public override void Destroy(NetworkObject networkObject)
        {
            if (networkObject != null)
            {
                client.HomeChunks.Remove(networkObject);
                Object.Destroy(networkObject.gameObject);
            }
        }

        /// <summary>
        /// The <see cref="AreaTerrain"/> of the host's loaded chunk <paramref name="chunk"/>, or null.
        /// </summary>
        private AreaTerrain TerrainOf(ChunkId chunk)
        {
            foreach (LoadedWorld world in hostWorlds.Loaded)
            {
                if (ChunkId.Of(world.Definition, chunk.Coords).Equals(chunk) && world.Chunks.TryGetValue(chunk.Coords, out Scene scene))
                {
                    return SceneQuery.FindFirst<AreaTerrain>(scene);
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Scripted <see cref="IPlayerInput"/> whose pointer and button state tests set directly; gameplay input is
    /// always enabled and the pointer is never over UI.
    /// </summary>
    public sealed class TestInput : IPlayerInput
    {
        public Camera Camera { get; set; }
        public bool GameplayEnabled => true;
        public Vector2 PointerScreenPosition { get; set; }
        public bool IsPointerOverUI => false;
        public bool MovePressedThisFrame { get; set; }
        public bool MoveHeld { get; set; }
        public bool AttackMovePressedThisFrame { get; set; }
        public bool StopPressedThisFrame { get; set; }
        public bool LanternPressedThisFrame { get; set; }

        public int AbilityPressedThisFrame { get; set; } = -1;

        /// <summary>
        /// True only for the slot stored in <see cref="AbilityPressedThisFrame"/>, which is -1 when no ability is
        /// pressed.
        /// </summary>
        public bool WasAbilityPressedThisFrame(int slot)
        {
            return slot == AbilityPressedThisFrame;
        }

        /// <summary>
        /// Points at <paramref name="worldPoint"/> through the camera; the click itself is
        /// <see cref="MovePressedThisFrame"/>, held for one frame by the caller.
        /// </summary>
        public void PointAt(Vector2 worldPoint)
        {
            Vector3 screen = Camera.WorldToScreenPoint(new Vector3(worldPoint.x, worldPoint.y, 0f));
            PointerScreenPosition = new Vector2(screen.x, screen.y);
        }
    }

    /// <summary>
    /// <see cref="IClientReadiness"/> for a test container that treats every client as ready everywhere.
    /// </summary>
    private sealed class ReadyClients : IClientReadiness
    {
        /// <summary>
        /// Always true.
        /// </summary>
        public bool IsClientReady(ulong clientId)
        {
            return true;
        }

        /// <summary>
        /// Always true.
        /// </summary>
        public bool IsClientReady(ulong clientId, ChunkId chunk)
        {
            return true;
        }

        /// <summary>
        /// Records nothing.
        /// </summary>
        public void BeginTrip(ulong clientId, int trip)
        {
        }
    }

    /// <summary>
    /// <see cref="ISessionStatus"/> of a joined client: in a session, not hosting.
    /// </summary>
    private sealed class ClientStatus : ISessionStatus
    {
        public bool IsActive => true;
        public bool IsHost => false;
    }

    /// <summary>
    /// <see cref="IGameAuthority"/> for a test container: authoritative or not, as built.
    /// </summary>
    private sealed class FixedAuthority : IGameAuthority
    {
        /// <summary>
        /// Creates an authority that always answers <paramref name="authoritative"/>.
        /// </summary>
        public FixedAuthority(bool authoritative)
        {
            IsAuthoritative = authoritative;
        }

        public bool IsAuthoritative { get; }
    }
}
