using System;
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

    private InProcessClient(NetworkManager manager, IObjectResolver container, PlayerRegistry players, LocalPlayerTracker localPlayer, TestInput input)
    {
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

    public static InProcessClient Start(NetworkSession session, NetworkObject playerPrefab)
    {
        TestInput input = new();
        PlayerRegistry players = new();
        LocalPlayerTracker localPlayer = new();

        ContainerBuilder builder = new();
        builder.RegisterInstance(players).As<IPlayerRegistry>().AsSelf();
        builder.RegisterInstance(localPlayer);
        builder.RegisterInstance<IPlayerInput>(input);
        builder.RegisterInstance<IClock>(UnityClock.Shared);
        builder.RegisterInstance<IGameAuthority>(new ClientAuthority());
        builder.Register<ActiveSpawnPoint>(Lifetime.Singleton);
        builder.Register<LocalPlayerCommandSource>(Lifetime.Singleton);
        builder.Register<PlayerBinder>(Lifetime.Singleton);
        builder.Register<CombatEvents>(Lifetime.Singleton);
        builder.Register<DamageService>(Lifetime.Singleton);
        builder.Register<SlashSpawner>(Lifetime.Singleton);
        IObjectResolver container = builder.Build();

        NetworkManager manager = CloneNetworkManager(session, "InProcessClient");
        GameObject clientObject = manager.gameObject;
        clientObject.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", session.Settings.Port);
        manager.PrefabHandler.AddHandler(playerPrefab, new InjectingNetworkPrefabHandler(playerPrefab, container));
        Assert.That(manager.StartClient(), Is.True, "The in-process client should start.");
        return new InProcessClient(manager, container, players, localPlayer, input);
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
        using FastBufferWriter writer = new(0, Allocator.Temp);
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

    public sealed class TestInput : IPlayerInput
    {
        public Vector2 Move { get; set; }
        public bool AttackPressedThisFrame { get; set; }
        public bool GameplayEnabled => true;
    }

    private sealed class ClientAuthority : IGameAuthority
    {
        public bool IsAuthoritative => false;
    }
}
