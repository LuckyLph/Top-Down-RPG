using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using VContainer;
using VContainer.Unity;
using Object = UnityEngine.Object;

/// <summary>
/// A bare host running in the test process, so the editor's own session can be tested as a joining client: a clone of
/// the session's <see cref="NetworkManager"/> listening on its port, with a hand-built container of host-side player
/// services and no scenes of its own. It records the client's join, chunk reports and releases, spawns the client's
/// player in a given world when it joins (unless told not to), and acknowledges releases at once or when told.
/// </summary>
public sealed class InProcessHost : IDisposable
{
    private readonly IObjectResolver container;
    private readonly NetworkObject playerPrefab;

    /// <summary>
    /// Captures the started manager, its container and the player prefab it spawns clients' players from.
    /// </summary>
    private InProcessHost(NetworkManager manager, IObjectResolver container, NetworkObject playerPrefab)
    {
        Manager = manager;
        this.container = container;
        this.playerPrefab = playerPrefab;
    }

    public NetworkManager Manager { get; }
    public PlayerRegistry Players => container.Resolve<PlayerRegistry>();
    public PlayerWorlds PlayerWorlds => container.Resolve<PlayerWorlds>();
    public List<ulong> Joined { get; } = new();
    public List<(int Trip, ChunkId Chunk)> Ready { get; } = new();
    public List<ChunkId> Releasing { get; } = new();
    public HashSet<ChunkId> HeldAcknowledgements { get; } = new();
    public WorldDefinition SpawnWorld { get; set; }
    public Vector2 SpawnPosition { get; set; }
    public bool SpawnsJoinedPlayers { get; set; } = true;
    public string RefusalForClients { get; set; }

    /// <summary>
    /// Starts a host cloned from <paramref name="session"/>'s manager on its port, approving every client (or refusing
    /// remote ones with <paramref name="refusal"/>), with a container built from <paramref name="gameScenes"/>,
    /// <paramref name="combatSettings"/>, <paramref name="gameplaySettings"/> and <paramref name="playerPrefab"/>'s
    /// control settings. Asserts it starts.
    /// </summary>
    public static InProcessHost Start(NetworkSession session, NetworkObject playerPrefab, GameScenes gameScenes, CombatSettings combatSettings, GameplaySettings gameplaySettings, string refusal = null)
    {
        NetworkManager manager = InProcessClient.CloneNetworkManager(session, "RemoteHost");
        manager.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", session.Settings.Port, "127.0.0.1");
        IObjectResolver container = null;
        if (playerPrefab != null)
        {
            container = InProcessClient.BuildContainer(
                new PlayerRegistry(),
                new LocalPlayerTracker(),
                new InProcessClient.TestInput(),
                Camera.main,
                playerPrefab.GetComponent<PlayerController>().ControlSettings,
                combatSettings,
                gameplaySettings,
                gameScenes,
                new HostStatus());
        }

        InProcessHost host = new(manager, container, playerPrefab) { RefusalForClients = refusal };
        manager.ConnectionApprovalCallback = (request, response) =>
        {
            bool refuse = host.RefusalForClients != null && request.ClientNetworkId != NetworkManager.ServerClientId;
            response.Approved = !refuse;
            response.Reason = refuse ? JoinApproval.EncodeRefusal(host.RefusalForClients) : null;
        };
        Assert.That(manager.StartHost(), Is.True, "The remote host should start.");
        CustomMessagingManager messages = manager.CustomMessagingManager;
        messages.RegisterNamedMessageHandler(NetworkSession.ClientJoinedMessage, (sender, reader) => host.HandleJoined(sender));
        messages.RegisterNamedMessageHandler(
            NetworkSession.ChunkReadyMessage,
            (sender, reader) =>
            {
                reader.ReadValueSafe(out int trip);
                reader.ReadValueSafe(out NetworkChunkId chunk);
                host.Ready.Add((trip, chunk.ToChunkId()));
            });
        messages.RegisterNamedMessageHandler(
            NetworkSession.ChunkReleasingMessage,
            (sender, reader) =>
            {
                reader.ReadValueSafe(out NetworkChunkId chunk);
                ChunkId released = chunk.ToChunkId();
                host.Releasing.Add(released);
                if (!host.HeldAcknowledgements.Contains(released))
                {
                    host.Acknowledge(sender, released);
                }
            });
        return host;
    }

    /// <summary>
    /// The most recently connected client's id.
    /// </summary>
    public ulong LastClientId => Manager.ConnectedClientsIds[Manager.ConnectedClientsIds.Count - 1];

    /// <summary>
    /// The host's copy of <paramref name="clientId"/>'s player, or null.
    /// </summary>
    public PlayerController PlayerOf(ulong clientId)
    {
        return container != null ? InProcessClient.FindCopyOwnedBy(Players, clientId) : null;
    }

    /// <summary>
    /// Acknowledges <paramref name="clientId"/>'s release of <paramref name="chunk"/>, as the real host does once it has
    /// hidden that chunk's objects, and stops holding that acknowledgement back.
    /// </summary>
    public void Acknowledge(ulong clientId, ChunkId chunk)
    {
        HeldAcknowledgements.Remove(chunk);
        using FastBufferWriter writer = new(128, Allocator.Temp);
        writer.WriteValueSafe(new NetworkChunkId(chunk));
        Manager.CustomMessagingManager.SendNamedMessage(NetworkSession.ChunkReleasedMessage, clientId, writer, NetworkSession.MessageDelivery);
    }

    /// <summary>
    /// Shuts the host down and destroys its manager, then disposes its container.
    /// </summary>
    public void Dispose()
    {
        if (Manager != null)
        {
            Manager.Shutdown();
            Object.Destroy(Manager.gameObject);
        }

        if (container != null)
        {
            container.Dispose();
        }
    }

    /// <summary>
    /// Records a client's join and, when it spawns joined players, spawns its player at <see cref="SpawnPosition"/> in
    /// <see cref="SpawnWorld"/>.
    /// </summary>
    private void HandleJoined(ulong clientId)
    {
        Joined.Add(clientId);
        if (!SpawnsJoinedPlayers || container == null)
        {
            return;
        }

        PlayerController player = container.Instantiate(playerPrefab.GetComponent<PlayerController>());
        player.Teleport(SpawnPosition);
        player.GetComponent<PlayerWorldSync>().SetSpawnWorld(SpawnWorld);
        NetworkManager previous = NetworkManager.Singleton;
        Manager.SetSingleton();
        try
        {
            player.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
        }
        finally
        {
            if (previous != null)
            {
                previous.SetSingleton();
            }
        }
    }

    /// <summary>
    /// <see cref="ISessionStatus"/> of a host: in a session, hosting.
    /// </summary>
    private sealed class HostStatus : ISessionStatus
    {
        public bool IsActive => true;
        public bool IsHost => true;
    }
}
