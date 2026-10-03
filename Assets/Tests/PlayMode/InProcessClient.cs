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

public sealed class InProcessClient : IDisposable
{
    private readonly IObjectResolver container;
    private readonly NetworkSession host;

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

    public T Resolve<T>()
    {
        return container.Resolve<T>();
    }

    public static InProcessClient Start(NetworkSession session, NetworkObject playerPrefab)
    {
        NavigationGrid2D navigationGrid = Object.FindAnyObjectByType<NavigationGrid2D>();
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
        builder.Register<ActiveNavigationGrid>(Lifetime.Singleton);
        builder.Register<PointerTargetPicker>(Lifetime.Singleton);
        builder.Register<LocalPlayerCommandSource>(Lifetime.Singleton);
        builder.Register<PlayerBinder>(Lifetime.Singleton);
        builder.RegisterInstance(combatSettings);
        builder.Register<CombatEvents>(Lifetime.Singleton);
        builder.Register<DamageService>(Lifetime.Singleton);
        builder.Register<AbilityService>(Lifetime.Singleton);
        builder.Register<SlashSpawner>(Lifetime.Singleton);
        builder.Register<EffectSpawner>(Lifetime.Singleton);
        builder.RegisterInstance<IRandom>(new SystemRandom(1));
        if (navigationGrid != null)
        {
            builder.RegisterInstance(navigationGrid);
        }

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

        Assert.That(manager.StartClient(), Is.True, "The in-process client should start.");
        input.Camera = camera;
        InProcessClient client = new(session, manager, container, players, localPlayer, input);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(
            NetworkSession.AreaAnnouncementMessage,
            (sender, reader) => client.Announcements.Add(AreaAnnouncement.Read(reader)));
        return client;
    }

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

    public void SendReady()
    {
        SendReady(host.AreaEpoch);
    }

    public void SendReady(int areaEpoch)
    {
        using FastBufferWriter writer = new(sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(areaEpoch);
        Manager.CustomMessagingManager.SendNamedMessage(NetworkSession.ClientReadyMessage, NetworkManager.ServerClientId, writer);
    }

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

    public void Dispose()
    {
        if (Manager != null)
        {
            Manager.Shutdown();
            Object.Destroy(Manager.gameObject);
        }

        container.Dispose();
    }

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

    private sealed class PersistentCopyHandler : INetworkPrefabInstanceHandler
    {
        private readonly INetworkPrefabInstanceHandler inner;

        public PersistentCopyHandler(INetworkPrefabInstanceHandler inner)
        {
            this.inner = inner;
        }

        public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
        {
            NetworkObject instance = inner.Instantiate(ownerClientId, position, rotation);
            Object.DontDestroyOnLoad(instance.gameObject);
            return instance;
        }

        public void Destroy(NetworkObject networkObject)
        {
            inner.Destroy(networkObject);
        }
    }

    public sealed class TestInput : IPlayerInput
    {
        public Camera Camera { get; set; }
        public bool GameplayEnabled => true;
        public Vector2 PointerScreenPosition { get; set; }
        public bool IsPointerOverUI => false;
        public bool MovePressedThisFrame { get; set; }
        public bool MoveHeld { get; set; }
        public bool StopPressedThisFrame { get; set; }

        public int AbilityPressedThisFrame { get; set; } = -1;

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

    private sealed class ClientAuthority : IGameAuthority
    {
        public bool IsAuthoritative => false;
    }
}
