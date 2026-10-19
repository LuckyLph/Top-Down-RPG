using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Multiplayer.Tools.NetworkSimulator.Runtime;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;

/// <summary>
/// A second Netcode client running in the host's process for PlayMode tests: a clone of the host's
/// <see cref="NetworkManager"/> connected over loopback, with its own hand-built container of client-side services.
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
    public List<AreaAnnouncement> Announcements { get; } = new();

    /// <summary>
    /// Resolves a service from the client's own container rather than the host's scopes.
    /// </summary>
    public T Resolve<T>()
    {
        return container.Resolve<T>();
    }

    /// <summary>
    /// Builds the client container (non-authoritative, scripted input, combat and ability services, the scene's grid
    /// and terrain), registers injecting prefab handlers for the player, the scene's mobs and the terrain sync object,
    /// then starts a cloned manager against the host's port and records area announcements. Asserts the start succeeds.
    /// </summary>
    public static InProcessClient Start(NetworkSession session, NetworkObject playerPrefab)
    {
        NavigationGrid2D navigationGrid = Object.FindAnyObjectByType<NavigationGrid2D>();
        AreaTerrain areaTerrain = Object.FindAnyObjectByType<AreaTerrain>();
        Camera camera = Camera.main;
        PlayerControlSettings controlSettings = playerPrefab.GetComponent<PlayerController>().ControlSettings;
        CombatSettings combatSettings = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container.Resolve<CombatSettings>();

        TestInput input = new();
        PlayerRegistry players = new();
        LocalPlayerTracker localPlayer = new();

        ContainerBuilder builder = new();
        builder.RegisterInstance(players).As<IPlayerRegistry>().AsSelf();
        builder.RegisterInstance(localPlayer);
        builder.RegisterInstance<IPlayerInput>(input);
        builder.RegisterInstance<IClock>(UnityClock.Shared);
        builder.RegisterInstance<IGameAuthority>(new ClientAuthority());
        builder.RegisterInstance(camera);
        builder.RegisterInstance(controlSettings);
        builder.Register<ActiveSpawnPoint>(Lifetime.Singleton);
        builder.Register<Worlds>(Lifetime.Singleton);
        builder.Register<PlayerWorlds>(Lifetime.Singleton);
        builder.Register<PlayerNavigation>(Lifetime.Singleton);
        builder.Register<PointerTargetPicker>(Lifetime.Singleton);
        builder.Register<LocalPlayerCommandSource>(Lifetime.Singleton);
        builder.Register<PlayerBinder>(Lifetime.Singleton);
        builder.RegisterInstance(combatSettings);
        builder.Register<CombatEvents>(Lifetime.Singleton);
        builder.Register<DamageService>(Lifetime.Singleton);
        builder.Register<StatusEffectService>(Lifetime.Singleton);
        builder.Register<HitService>(Lifetime.Singleton);
        builder.Register<StatusVisualPool>(Lifetime.Singleton).WithParameter(default(UnityEngine.SceneManagement.Scene));
        builder.RegisterInstance<IClientReadiness>(new ReadyClients());
        builder.Register<AbilityService>(Lifetime.Singleton);
        builder.Register<SlashSpawner>(Lifetime.Singleton);
        builder.Register<EffectSpawner>(Lifetime.Singleton);
        builder.RegisterInstance<IRandom>(new SystemRandom(1));
        if (navigationGrid != null)
        {
            builder.RegisterInstance(navigationGrid).As<INavigation2D>().AsSelf();
        }

        builder.Register(_ => Object.FindAnyObjectByType<AreaTerrain>(), Lifetime.Transient);

        IObjectResolver container = builder.Build();

        NetworkManager manager = CloneNetworkManager(session, "InProcessClient");
        GameObject clientObject = manager.gameObject;
        clientObject.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", session.Settings.Port);
        manager.NetworkConfig.ConnectionData = JoinApproval.CreatePayload(Application.version);
        manager.PrefabHandler.AddHandler(playerPrefab, new PersistentCopyHandler(new InjectingNetworkPrefabHandler(playerPrefab, container)));
        HashSet<NetworkObject> mobPrefabs = new();
        foreach (MobSpawnPoint spawnPoint in Object.FindObjectsByType<MobSpawnPoint>())
        {
            NetworkObject mobPrefab = spawnPoint.MobPrefab != null ? spawnPoint.MobPrefab.GetComponent<NetworkObject>() : null;
            if (mobPrefab != null && mobPrefabs.Add(mobPrefab))
            {
                manager.PrefabHandler.AddHandler(mobPrefab, new PersistentCopyHandler(new InjectingNetworkPrefabHandler(mobPrefab, container)));
            }
        }

        if (areaTerrain != null && areaTerrain.SyncPrefab != null)
        {
            manager.PrefabHandler.AddHandler(areaTerrain.SyncPrefab, new PersistentCopyHandler(new InjectingNetworkPrefabHandler(areaTerrain.SyncPrefab, container)));
        }

        Assert.That(manager.StartClient(), Is.True, "The in-process client should start.");
        input.Camera = camera;
        InProcessClient client = new(session, manager, container, players, localPlayer, input);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(
            NetworkSession.AreaAnnouncementMessage,
            (sender, reader) => client.Announcements.Add(AreaAnnouncement.Read(reader)));
        return client;
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
    /// Tells the host this client is ready for the host's current area epoch.
    /// </summary>
    public void SendReady()
    {
        SendReady(host.AreaEpoch);
    }

    /// <summary>
    /// Sends the client-ready message for <paramref name="areaEpoch"/> to the server, as a real client does once it has
    /// loaded an area; a stale epoch lets tests check that the host ignores it.
    /// </summary>
    public void SendReady(int areaEpoch)
    {
        using FastBufferWriter writer = new(sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(areaEpoch);
        Manager.CustomMessagingManager.SendNamedMessage(NetworkSession.ClientReadyMessage, NetworkManager.ServerClientId, writer);
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
    /// <see cref="IClientReadiness"/> for the client container that treats every client as ready.
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
    }

    /// <summary>
    /// <see cref="IGameAuthority"/> for the client container: never authoritative, as on a real client.
    /// </summary>
    private sealed class ClientAuthority : IGameAuthority
    {
        public bool IsAuthoritative => false;
    }
}
