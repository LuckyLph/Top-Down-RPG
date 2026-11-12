using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using Object = UnityEngine.Object;

/// <summary>
/// Owns this machine's <see cref="Unity.Netcode.NetworkManager"/> for the whole app: hosts or joins sessions, admits
/// clients through <see cref="JoinApproval"/> and keys them by their player id, and runs the readiness handshakes that
/// decide what each client receives (Docs/WorldStreaming.md: Networking). A client joins once its Gameplay scene is up
/// and then sees every player; it reports each chunk of its world ready as it loads and then sees the objects whose
/// home chunk that is; before unloading a chunk it releases it and waits until the host has hidden that chunk's objects
/// from it. The host keeps every chunk a client holds.
/// </summary>
public sealed class NetworkSession : IGameAuthority, INetworkObjectSpawner, IClientReadiness, IChunkHandshake, ISessionStatus, IPlayerIdSource, IClientVisibility, INetworkClock, IHostLink, ISessionConnection, IDisposable
{
    public const string ClientJoinedMessage = "TopDownRPG.ClientJoined";
    public const string ChunkReadyMessage = "TopDownRPG.ChunkReady";
    public const string ChunkReleasingMessage = "TopDownRPG.ChunkReleasing";
    public const string ChunkReleasedMessage = "TopDownRPG.ChunkReleased";
    public const NetworkDelivery MessageDelivery = NetworkDelivery.ReliableFragmentedSequenced;
    private const int ChunkMessageSize = 128;

    private readonly NetworkManager networkManager;
    private readonly UnityTransport transport;
    private readonly NetworkSettings settings;
    private readonly UserSettings userSettings;
    private readonly ClientChunkReadiness readiness;
    private readonly HashSet<ulong> admittedClients = new();
    private readonly Dictionary<ulong, string> playerIds = new();
    private readonly HashSet<ChunkId> awaitingRelease = new();
    private readonly HashSet<uint> chunkRoutedPrefabs = new();
    private readonly List<NetworkObject> visibilityBuffer = new();
    private readonly NetworkObject.VisibilityDelegate visibleToJoinedClients;
    private bool shutdownRequested;
    private bool quitting;
    private bool followingHost;

    /// <summary>
    /// Instantiates <paramref name="networkManagerPrefab"/>, hooks connection approval and the client callbacks, and
    /// starts watching for the application quitting; logs an error when the prefab has no
    /// <see cref="UnityTransport"/> or has connection approval off. <paramref name="localSettings"/> gives this install's
    /// player id.
    /// </summary>
    public NetworkSession(NetworkManager networkManagerPrefab, NetworkSettings settings, UserSettings localSettings)
    {
        this.settings = settings;
        userSettings = localSettings;
        readiness = new ClientChunkReadiness(this);
        visibleToJoinedClients = IsClientJoined;
        Application.quitting += HandleQuitting;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
#endif
        networkManager = Object.Instantiate(networkManagerPrefab);
        networkManager.name = networkManagerPrefab.name;
        transport = networkManager.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogError($"{networkManagerPrefab.name} has no {nameof(UnityTransport)}; hosting and joining will fail.", networkManagerPrefab);
        }

        if (!networkManager.NetworkConfig.ConnectionApproval)
        {
            Debug.LogError($"{networkManagerPrefab.name} has Connection Approval off; the host cannot refuse clients on another version or beyond {settings.MaxPlayers} players.", networkManagerPrefab);
        }

        networkManager.ConnectionApprovalCallback = HandleConnectionApproval;
        networkManager.OnClientStopped += HandleClientStopped;
        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        networkManager.OnClientConnectedCallback += HandleClientConnected;
    }

    public event Action ConnectionLost;
    public event Action<ulong> ClientJoined;

    public NetworkManager NetworkManager => networkManager;
    public NetworkSettings Settings => settings;
    public bool IsActive => networkManager.IsListening;
    public bool IsHost => networkManager.IsHost;
    public bool IsServer => networkManager.IsServer;
    public bool IsConnectedClient => networkManager.IsConnectedClient;
    public bool IsAuthoritative => IsActive ? networkManager.IsServer : !followingHost;
    public bool IsFollowingHost => followingHost;
    public bool IsHostLost => followingHost && !IsActive;
    public ulong LocalClientId => networkManager.LocalClientId;
    public IReadOnlyCollection<ulong> JoinedClients => readiness.JoinedClients;
    public string JoinRefusal { get; private set; }

    /// <summary>
    /// The address of the last join, <c>host:port</c> (Docs/UiRevamp.md, Phase 9), which the session's lifecycle
    /// names when the connection is lost; empty before any.
    /// </summary>
    public string JoinedAddress { get; private set; } = string.Empty;

    /// <summary>
    /// The host's network time in seconds (a client's estimate of it); zero outside a session.
    /// </summary>
    public double ServerTime => networkManager.IsListening ? networkManager.ServerTime.Time : 0d;

    /// <summary>
    /// The host's per-client readiness, for tests that check the handshake.
    /// </summary>
    internal ClientChunkReadiness Readiness => readiness;

    /// <summary>
    /// Starts hosting on the configured listen address and port and listens for clients' joins and chunk reports.
    /// Returns false when a session is already running, the transport is missing or Netcode fails to start.
    /// </summary>
    public bool StartHost()
    {
        if (IsActive || transport == null)
        {
            return false;
        }

        ResetSessionState();
        transport.SetConnectionData(settings.DefaultAddress, settings.Port, settings.ListenAddress);
        if (!networkManager.StartHost())
        {
            return false;
        }

        CustomMessagingManager messages = networkManager.CustomMessagingManager;
        messages.RegisterNamedMessageHandler(ClientJoinedMessage, HandleClientJoinedMessage);
        messages.RegisterNamedMessageHandler(ChunkReadyMessage, HandleChunkReadyMessage);
        messages.RegisterNamedMessageHandler(ChunkReleasingMessage, HandleChunkReleasingMessage);
        return true;
    }

    /// <summary>
    /// Connects as a client to <paramref name="host"/> on <paramref name="port"/> (Docs/UiRevamp.md, Phase 9),
    /// sending this build's version and this install's player id, and waits up to <paramref name="timeoutSeconds"/> of
    /// real time for the connection. Returns false at once when a session runs, the host is blank or the client cannot
    /// start; on a refusal, disconnect or timeout it records any refusal in <see cref="JoinRefusal"/>, shuts down and
    /// returns false; on cancellation it shuts down and rethrows, leaving no session behind.
    /// </summary>
    public async Awaitable<bool> JoinAsync(string host, ushort port, float timeoutSeconds, CancellationToken cancellation)
    {
        if (IsActive || transport == null || string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        ResetSessionState();
        string hostAddress = host.Trim();
        JoinedAddress = new JoinAddress(hostAddress, port).ToString();
        transport.SetConnectionData(hostAddress, port);
        networkManager.NetworkConfig.ConnectionData = JoinApproval.CreatePayload(Application.version, LocalPlayerId);
        if (!networkManager.StartClient())
        {
            return false;
        }

        followingHost = true;
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(ChunkReleasedMessage, HandleChunkReleasedMessage);

        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        try
        {
            while (!networkManager.IsConnectedClient)
            {
                if (!networkManager.IsListening || Time.realtimeSinceStartup > deadline)
                {
                    JoinRefusal = JoinApproval.TryDecodeRefusal(networkManager.DisconnectReason, out string refusal) ? refusal : null;
                    Shutdown();
                    return false;
                }

                await Awaitable.NextFrameAsync(cancellation);
            }
        }
        catch (OperationCanceledException)
        {
            Shutdown();
            throw;
        }

        return true;
    }

    /// <summary>
    /// Ends the session on purpose (so it is not reported as <see cref="ConnectionLost"/>), stops following a host so
    /// this machine is authoritative again, and forgets every client's readiness. Safe to call when not running.
    /// </summary>
    public void Shutdown()
    {
        shutdownRequested = true;
        followingHost = false;
        readiness.Clear();
        awaitingRelease.Clear();
        if (networkManager != null && networkManager.IsListening)
        {
            networkManager.Shutdown();
        }
    }

    /// <summary>
    /// This install's player id, from the user settings (made on first use).
    /// </summary>
    public string LocalPlayerId => userSettings != null ? userSettings.PlayerId : string.Empty;

    /// <summary>
    /// The player id <paramref name="clientId"/> joined with: the host's own for the host's client; empty for a client
    /// the host does not know. A client that came without an id, or with one already in the session, has a new id for
    /// this session only.
    /// </summary>
    public string PlayerIdOf(ulong clientId)
    {
        if (clientId == NetworkManager.ServerClientId)
        {
            return LocalPlayerId;
        }

        return playerIds.TryGetValue(clientId, out string playerId) ? playerId : string.Empty;
    }

    /// <summary>
    /// Whether <paramref name="clientId"/>'s Gameplay scene is up, so it may receive the players; always true for the
    /// host's own client.
    /// </summary>
    public bool IsClientJoined(ulong clientId)
    {
        return clientId == NetworkManager.ServerClientId || readiness.IsJoined(clientId);
    }

    /// <inheritdoc />
    public bool IsClientReady(ulong clientId)
    {
        return clientId == NetworkManager.ServerClientId || readiness.IsSettled(clientId);
    }

    /// <inheritdoc />
    public bool IsClientReady(ulong clientId, ChunkId chunk)
    {
        return clientId == NetworkManager.ServerClientId || readiness.IsReady(clientId, chunk);
    }

    /// <inheritdoc />
    public void BeginTrip(ulong clientId, int trip, string worldId)
    {
        if (networkManager.IsServer)
        {
            readiness.BeginTrip(clientId, trip, worldId);
        }
    }

    /// <inheritdoc />
    public bool IsChunkHeld(ChunkId chunk)
    {
        return networkManager.IsServer && readiness.IsHeld(chunk);
    }

    /// <summary>
    /// Client only: tells the host this machine's Gameplay scene is up and its player prefab registered, so it may
    /// receive the players and its own. Does nothing on the host or when not connected.
    /// </summary>
    public void NotifyJoined()
    {
        if (!networkManager.IsConnectedClient || networkManager.IsServer)
        {
            return;
        }

        using FastBufferWriter writer = new(sizeof(byte), Allocator.Temp);
        writer.WriteValueSafe((byte)1);
        networkManager.CustomMessagingManager.SendNamedMessage(ClientJoinedMessage, NetworkManager.ServerClientId, writer, MessageDelivery);
    }

    /// <inheritdoc />
    public void ReportChunkReady(int trip, ChunkId chunk)
    {
        if (!networkManager.IsConnectedClient || networkManager.IsServer)
        {
            return;
        }

        using FastBufferWriter writer = new(ChunkMessageSize, Allocator.Temp);
        writer.WriteValueSafe(trip);
        writer.WriteValueSafe(new NetworkChunkId(chunk));
        networkManager.CustomMessagingManager.SendNamedMessage(ChunkReadyMessage, NetworkManager.ServerClientId, writer, MessageDelivery);
    }

    /// <inheritdoc />
    public async Awaitable ReleaseChunkAsync(ChunkId chunk, CancellationToken cancellation)
    {
        if (!networkManager.IsConnectedClient || networkManager.IsServer)
        {
            return;
        }

        awaitingRelease.Add(chunk);
        try
        {
            using (FastBufferWriter writer = new(ChunkMessageSize, Allocator.Temp))
            {
                writer.WriteValueSafe(new NetworkChunkId(chunk));
                networkManager.CustomMessagingManager.SendNamedMessage(ChunkReleasingMessage, NetworkManager.ServerClientId, writer, MessageDelivery);
            }

            while (awaitingRelease.Contains(chunk) && networkManager.IsConnectedClient)
            {
                await Awaitable.NextFrameAsync(cancellation);
            }
        }
        finally
        {
            awaitingRelease.Remove(chunk);
        }
    }

    /// <summary>
    /// Host only: whether connected client <paramref name="clientId"/> already has a player object.
    /// </summary>
    public bool HasPlayerObject(ulong clientId)
    {
        return networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) && client.PlayerObject != null;
    }

    /// <summary>
    /// Host only: spawns <paramref name="instance"/> as a server-owned object visible to every joined client and
    /// destroyed with its scene.
    /// </summary>
    public void Spawn(NetworkObject instance)
    {
        instance.CheckObjectVisibility = visibleToJoinedClients;
        instance.Spawn(destroyWithScene: true);
    }

    /// <summary>
    /// Host only: spawns <paramref name="instance"/> as a server-owned object of <paramref name="homeChunk"/>: it is
    /// visible to the clients that hold that chunk, whichever trip they reported it in, destroyed with its scene, and
    /// carries the chunk as its
    /// instantiation data when its prefab has a handler here (<c>ChunkNetworkPrefabs</c>), so each client creates its
    /// copy in that chunk's scope.
    /// </summary>
    public void Spawn(NetworkObject instance, ChunkId homeChunk)
    {
        if (chunkRoutedPrefabs.Contains(instance.PrefabIdHash))
        {
            networkManager.PrefabHandler.SetInstantiationData(instance, new NetworkChunkId(homeChunk));
        }

        instance.CheckObjectVisibility = clientId => clientId == NetworkManager.ServerClientId || readiness.Holds(clientId, homeChunk);
        instance.Spawn(destroyWithScene: true);
    }

    /// <summary>
    /// Host only: spawns <paramref name="instance"/> as <paramref name="ownerClientId"/>'s player object, visible to
    /// every joined client whatever its world, and destroyed with its scene.
    /// </summary>
    public void SpawnPlayerObject(NetworkObject instance, ulong ownerClientId)
    {
        instance.CheckObjectVisibility = visibleToJoinedClients;
        instance.SpawnAsPlayerObject(ownerClientId, destroyWithScene: true);
    }

    /// <summary>
    /// Installs an <see cref="InjectingNetworkPrefabHandler"/> for <paramref name="prefab"/> so client copies are
    /// injected from <paramref name="resolver"/> and placed in <paramref name="targetScene"/>.
    /// </summary>
    public void RegisterPrefab(NetworkObject prefab, IObjectResolver resolver, Scene targetScene)
    {
        networkManager.PrefabHandler.AddHandler(prefab, new InjectingNetworkPrefabHandler(prefab, resolver, targetScene));
    }

    /// <summary>
    /// Installs <paramref name="handler"/> for <paramref name="prefab"/>; logs an error when the prefab already has one,
    /// since Netcode keeps one handler per prefab.
    /// </summary>
    public void RegisterPrefab(NetworkObject prefab, INetworkPrefabInstanceHandler handler)
    {
        if (!networkManager.PrefabHandler.AddHandler(prefab, handler))
        {
            Debug.LogError($"{prefab.name} already has a network prefab handler; the new one is ignored.", prefab);
            return;
        }

        if (handler is NetworkPrefabInstanceHandlerWithData<NetworkChunkId>)
        {
            chunkRoutedPrefabs.Add(prefab.PrefabIdHash);
        }
    }

    /// <summary>
    /// Host only: despawns <paramref name="instance"/> and destroys it, or destroys it when it is not spawned (the
    /// session ended first). Nothing for null.
    /// </summary>
    public void Despawn(NetworkObject instance)
    {
        if (instance == null)
        {
            return;
        }

        if (instance.IsSpawned && networkManager != null && networkManager.IsServer)
        {
            instance.Despawn(true);
        }
        else
        {
            Object.Destroy(instance.gameObject);
        }
    }

    /// <summary>
    /// Removes <paramref name="prefab"/>'s handler; does nothing once the NetworkManager is destroyed.
    /// </summary>
    public void UnregisterPrefab(NetworkObject prefab)
    {
        chunkRoutedPrefabs.Remove(prefab.PrefabIdHash);
        if (networkManager != null)
        {
            networkManager.PrefabHandler.RemoveHandler(prefab);
        }
    }

    /// <summary>
    /// Shows <paramref name="clientId"/> every spawned object whose visibility check now lets it see it.
    /// </summary>
    void IClientVisibility.ShowNowVisible(ulong clientId)
    {
        CollectSpawnedWithVisibility();
        foreach (NetworkObject spawned in visibilityBuffer)
        {
            if (!spawned.IsNetworkVisibleTo(clientId) && spawned.CheckObjectVisibility(clientId))
            {
                spawned.NetworkShow(clientId);
            }
        }

        visibilityBuffer.Clear();
    }

    /// <summary>
    /// Hides from <paramref name="clientId"/> every spawned object it sees whose visibility check no longer lets it.
    /// </summary>
    void IClientVisibility.HideNoLongerVisible(ulong clientId)
    {
        CollectSpawnedWithVisibility();
        foreach (NetworkObject spawned in visibilityBuffer)
        {
            if (spawned.IsNetworkVisibleTo(clientId) && !spawned.CheckObjectVisibility(clientId))
            {
                spawned.NetworkHide(clientId);
            }
        }

        visibilityBuffer.Clear();
    }

    /// <summary>
    /// Sends <paramref name="clientId"/> the acknowledgement of its release of <paramref name="chunk"/>, on the same
    /// ordered channel as the hides that went before it, so it arrives after them.
    /// </summary>
    void IClientVisibility.AcknowledgeRelease(ulong clientId, ChunkId chunk)
    {
        if (!networkManager.IsServer || !networkManager.ConnectedClients.ContainsKey(clientId))
        {
            return;
        }

        using FastBufferWriter writer = new(ChunkMessageSize, Allocator.Temp);
        writer.WriteValueSafe(new NetworkChunkId(chunk));
        networkManager.CustomMessagingManager.SendNamedMessage(ChunkReleasedMessage, clientId, writer, MessageDelivery);
    }

    /// <summary>
    /// Unhooks every callback, shuts the session down and destroys the NetworkManager so it never outlives the Main
    /// scope.
    /// </summary>
    public void Dispose()
    {
        Application.quitting -= HandleQuitting;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
#endif
        if (networkManager == null)
        {
            return;
        }

        networkManager.ConnectionApprovalCallback = null;
        networkManager.OnClientStopped -= HandleClientStopped;
        networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        networkManager.OnClientConnectedCallback -= HandleClientConnected;
        Shutdown();
        Object.Destroy(networkManager.gameObject);
    }

    /// <summary>
    /// Fills the buffer with the spawned objects that have a visibility check, so showing and hiding them does not
    /// touch the list being walked.
    /// </summary>
    private void CollectSpawnedWithVisibility()
    {
        visibilityBuffer.Clear();
        if (!networkManager.IsServer)
        {
            return;
        }

        foreach (NetworkObject spawned in networkManager.SpawnManager.SpawnedObjectsList)
        {
            if (spawned != null && spawned.CheckObjectVisibility != null)
            {
                visibilityBuffer.Add(spawned);
            }
        }
    }

    /// <summary>
    /// Notes that the application is quitting, so the client stopping is not reported as a lost connection.
    /// </summary>
    private void HandleQuitting()
    {
        quitting = true;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Treats leaving Play Mode like quitting, so the client stopping is not reported as a lost connection.
    /// </summary>
    private void HandlePlayModeStateChanged(UnityEditor.PlayModeStateChange change)
    {
        if (change == UnityEditor.PlayModeStateChange.ExitingPlayMode)
        {
            quitting = true;
        }
    }
#endif

    /// <summary>
    /// Clears everything left from a previous session (readiness, admissions, player ids, refusal, releases waiting)
    /// and makes this NetworkManager the Netcode singleton, which Netcode falls back to when spawning.
    /// </summary>
    private void ResetSessionState()
    {
        shutdownRequested = false;
        followingHost = false;
        readiness.Clear();
        admittedClients.Clear();
        playerIds.Clear();
        awaitingRelease.Clear();
        JoinRefusal = null;
        networkManager.SetSingleton();
    }

    /// <summary>
    /// Host: approves its own client, and other clients through <see cref="JoinApproval.Evaluate"/>, counting
    /// connected clients plus those approved but not yet connected; a refusal goes back as a tagged disconnect
    /// reason. Records an approved client's player id, giving one that came without an id, or with an id already in
    /// the session, a new id for this session with a warning. Never creates a player object, since players are
    /// spawned once a client joins.
    /// </summary>
    private void HandleConnectionApproval(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        response.CreatePlayerObject = false;
        if (request.ClientNetworkId == NetworkManager.ServerClientId)
        {
            response.Approved = true;
            return;
        }

        int playerCount = networkManager.ConnectedClientsIds.Count + admittedClients.Count;
        string refusal = JoinApproval.Evaluate(request.Payload, Application.version, playerCount, settings.MaxPlayers);
        if (refusal != null)
        {
            response.Approved = false;
            response.Reason = JoinApproval.EncodeRefusal(refusal);
            return;
        }

        response.Approved = true;
        admittedClients.Add(request.ClientNetworkId);
        JoinApproval.ReadPayload(request.Payload, out _, out string playerId);
        if (string.IsNullOrEmpty(playerId) || IsPlayerIdInSession(playerId))
        {
            string reason = string.IsNullOrEmpty(playerId) ? "came without a player id" : $"shares the player id {playerId} with a player already here";
            Debug.LogWarning($"Client {request.ClientNetworkId} {reason}; it plays as a new player this session.");
            playerId = Guid.NewGuid().ToString("N");
        }

        playerIds[request.ClientNetworkId] = playerId;
    }

    /// <summary>
    /// Whether the host or a client already admitted has <paramref name="playerId"/>.
    /// </summary>
    private bool IsPlayerIdInSession(string playerId)
    {
        if (playerId == LocalPlayerId)
        {
            return true;
        }

        foreach (string known in playerIds.Values)
        {
            if (known == playerId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Moves <paramref name="clientId"/> from admitted to connected.
    /// </summary>
    private void HandleClientConnected(ulong clientId)
    {
        admittedClients.Remove(clientId);
    }

    /// <summary>
    /// Host: records that a client's Gameplay scene is up, shows it the players and raises <see cref="ClientJoined"/>;
    /// a repeated join changes nothing.
    /// </summary>
    private void HandleClientJoinedMessage(ulong senderClientId, FastBufferReader payload)
    {
        if (senderClientId != NetworkManager.ServerClientId && readiness.Join(senderClientId))
        {
            ClientJoined?.Invoke(senderClientId);
        }
    }

    /// <summary>
    /// Host: takes a client's report that a chunk is up, ignoring one from before its latest trip.
    /// </summary>
    private void HandleChunkReadyMessage(ulong senderClientId, FastBufferReader payload)
    {
        payload.ReadValueSafe(out int trip);
        payload.ReadValueSafe(out NetworkChunkId chunk);
        readiness.ReportReady(senderClientId, trip, chunk.ToChunkId());
    }

    /// <summary>
    /// Host: takes a client's release of a chunk, hiding that chunk's objects from it before acknowledging.
    /// </summary>
    private void HandleChunkReleasingMessage(ulong senderClientId, FastBufferReader payload)
    {
        payload.ReadValueSafe(out NetworkChunkId chunk);
        readiness.Release(senderClientId, chunk.ToChunkId());
    }

    /// <summary>
    /// Client: the host acknowledged the release of a chunk, which may now unload.
    /// </summary>
    private void HandleChunkReleasedMessage(ulong senderClientId, FastBufferReader payload)
    {
        payload.ReadValueSafe(out NetworkChunkId chunk);
        awaitingRelease.Remove(chunk.ToChunkId());
    }

    /// <summary>
    /// Forgets a disconnected client's admission, player id and readiness, which releases every chunk it held.
    /// </summary>
    private void HandleClientDisconnected(ulong clientId)
    {
        admittedClients.Remove(clientId);
        playerIds.Remove(clientId);
        readiness.Leave(clientId);
    }

    /// <summary>
    /// Raises <see cref="ConnectionLost"/> once when the local client stops without <see cref="Shutdown"/> having been
    /// called, unless the application is quitting or leaving Play Mode.
    /// </summary>
    private void HandleClientStopped(bool wasHost)
    {
        if (shutdownRequested || quitting)
        {
            return;
        }

        shutdownRequested = true;
        ConnectionLost?.Invoke();
    }
}
