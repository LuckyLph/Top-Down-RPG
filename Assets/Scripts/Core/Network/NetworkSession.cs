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
/// clients through <see cref="JoinApproval"/>, announces the host's area to clients and runs the ready handshake that
/// keeps spawned objects hidden from a client until it has loaded the current area.
/// </summary>
public sealed class NetworkSession : IGameAuthority, INetworkObjectSpawner, IClientReadiness, ISessionStatus, IDisposable
{
    public const string ClientReadyMessage = "TopDownRPG.ClientReady";
    public const string AreaAnnouncementMessage = "TopDownRPG.AreaAnnouncement";
    private const int AnnouncementBufferSize = 1024;

    private readonly NetworkManager networkManager;
    private readonly UnityTransport transport;
    private readonly NetworkSettings settings;
    private readonly HashSet<ulong> readyClients = new();
    private readonly HashSet<ulong> admittedClients = new();
    private readonly NetworkObject.VisibilityDelegate visibleToReadyClients;
    private readonly List<NetworkObject> despawnBuffer = new();
    private AreaAnnouncement? currentArea;
    private bool shutdownRequested;
    private bool quitting;
    private bool followingHost;

    /// <summary>
    /// Instantiates <paramref name="networkManagerPrefab"/>, hooks connection approval and the client callbacks, and
    /// starts watching for the application quitting; logs an error when the prefab has no
    /// <see cref="UnityTransport"/> or has connection approval off.
    /// </summary>
    public NetworkSession(NetworkManager networkManagerPrefab, NetworkSettings settings)
    {
        this.settings = settings;
        visibleToReadyClients = IsClientReady;
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
    public event Action<ulong> ClientReady;
    public event Action<AreaAnnouncement> AreaAnnounced;

    public NetworkManager NetworkManager => networkManager;
    public NetworkSettings Settings => settings;
    public bool IsActive => networkManager.IsListening;
    public bool IsHost => networkManager.IsHost;
    public bool IsServer => networkManager.IsServer;
    public bool IsConnectedClient => networkManager.IsConnectedClient;
    public bool IsAuthoritative => IsActive ? networkManager.IsServer : !followingHost;
    public ulong LocalClientId => networkManager.LocalClientId;
    public IReadOnlyCollection<ulong> ReadyClients => readyClients;
    public int AreaEpoch { get; private set; }
    public string JoinRefusal { get; private set; }

    /// <summary>
    /// Starts hosting on the configured listen address and port and listens for clients' ready reports. Returns false
    /// when a session is already running, the transport is missing or Netcode fails to start.
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

        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(ClientReadyMessage, HandleClientReadyMessage);
        return true;
    }

    /// <summary>
    /// Connects as a client to <paramref name="address"/> (the default address when blank), sending this build's
    /// version, and waits up to <paramref name="timeoutSeconds"/> of real time for the connection. Returns false at
    /// once when a session runs or the client cannot start; on a refusal, disconnect or timeout it records any
    /// refusal in <see cref="JoinRefusal"/>, shuts down and returns false; on cancellation it shuts down and rethrows.
    /// </summary>
    public async Awaitable<bool> JoinAsync(string address, float timeoutSeconds, CancellationToken cancellation)
    {
        if (IsActive || transport == null)
        {
            return false;
        }

        ResetSessionState();
        string hostAddress = string.IsNullOrWhiteSpace(address) ? settings.DefaultAddress : address.Trim();
        transport.SetConnectionData(hostAddress, settings.Port);
        networkManager.NetworkConfig.ConnectionData = JoinApproval.CreatePayload(Application.version);
        if (!networkManager.StartClient())
        {
            return false;
        }

        followingHost = true;
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(AreaAnnouncementMessage, HandleAreaAnnouncementMessage);

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
        readyClients.Clear();
        if (networkManager != null && networkManager.IsListening)
        {
            networkManager.Shutdown();
        }
    }

    /// <summary>
    /// Whether <paramref name="clientId"/> reported ready for the current area epoch; always true for the host's own
    /// client.
    /// </summary>
    public bool IsClientReady(ulong clientId)
    {
        return clientId == NetworkManager.ServerClientId || readyClients.Contains(clientId);
    }

    /// <summary>
    /// Client only: tells the host this machine has loaded the area of the current <see cref="AreaEpoch"/>. Does
    /// nothing on the host or when not connected.
    /// </summary>
    public void NotifyReady()
    {
        if (!networkManager.IsConnectedClient || networkManager.IsServer)
        {
            return;
        }

        using FastBufferWriter writer = new(sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(AreaEpoch);
        networkManager.CustomMessagingManager.SendNamedMessage(ClientReadyMessage, NetworkManager.ServerClientId, writer);
    }

    /// <summary>
    /// Client only: adopts <paramref name="announcement"/>'s epoch as it starts loading that area, so its next ready
    /// report matches the host's.
    /// </summary>
    public void BeginAnnouncedArea(AreaAnnouncement announcement)
    {
        if (!networkManager.IsServer)
        {
            AreaEpoch = announcement.Epoch;
        }
    }

    /// <summary>
    /// Host only: starts a new area epoch, clears every client's readiness and sends the area to each connected
    /// client; it is also kept to send to clients that connect later.
    /// </summary>
    public void AnnounceArea(string scenePath, string spawnId, bool newSession)
    {
        if (!networkManager.IsServer)
        {
            return;
        }

        AreaEpoch++;
        readyClients.Clear();
        currentArea = new AreaAnnouncement(scenePath, spawnId, AreaEpoch, newSession);
        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            if (clientId != NetworkManager.ServerClientId)
            {
                SendAreaAnnouncement(clientId, currentArea.Value);
            }
        }
    }

    /// <summary>
    /// Host only: despawns every spawned network object that lives in <paramref name="scene"/>.
    /// </summary>
    public void DespawnObjectsIn(Scene scene)
    {
        if (!networkManager.IsServer || !scene.IsValid())
        {
            return;
        }

        despawnBuffer.Clear();
        foreach (NetworkObject spawned in networkManager.SpawnManager.SpawnedObjectsList)
        {
            if (spawned != null && spawned.gameObject.scene == scene)
            {
                despawnBuffer.Add(spawned);
            }
        }

        foreach (NetworkObject spawned in despawnBuffer)
        {
            spawned.Despawn();
        }

        despawnBuffer.Clear();
    }

    /// <summary>
    /// Host only: whether connected client <paramref name="clientId"/> already has a player object.
    /// </summary>
    public bool HasPlayerObject(ulong clientId)
    {
        return networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) && client.PlayerObject != null;
    }

    /// <summary>
    /// Host only: spawns <paramref name="instance"/> as a server-owned object, visible to ready clients only and
    /// destroyed with its scene.
    /// </summary>
    public void Spawn(NetworkObject instance)
    {
        instance.CheckObjectVisibility = visibleToReadyClients;
        instance.Spawn(destroyWithScene: true);
    }

    /// <summary>
    /// Host only: spawns <paramref name="instance"/> as <paramref name="ownerClientId"/>'s player object, visible to
    /// ready clients only and destroyed with its scene.
    /// </summary>
    public void SpawnPlayerObject(NetworkObject instance, ulong ownerClientId)
    {
        instance.CheckObjectVisibility = visibleToReadyClients;
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
        if (networkManager != null)
        {
            networkManager.PrefabHandler.RemoveHandler(prefab);
        }
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
    /// Clears everything left from a previous session (readiness, admissions, refusal, current area, epoch) and makes
    /// this NetworkManager the Netcode singleton, which Netcode falls back to when spawning.
    /// </summary>
    private void ResetSessionState()
    {
        shutdownRequested = false;
        followingHost = false;
        readyClients.Clear();
        admittedClients.Clear();
        JoinRefusal = null;
        currentArea = null;
        AreaEpoch = 0;
        networkManager.SetSingleton();
    }

    /// <summary>
    /// Sends <paramref name="announcement"/> to client <paramref name="clientId"/> as a named message.
    /// </summary>
    private void SendAreaAnnouncement(ulong clientId, AreaAnnouncement announcement)
    {
        using FastBufferWriter writer = new(AnnouncementBufferSize, Allocator.Temp);
        announcement.Write(writer);
        networkManager.CustomMessagingManager.SendNamedMessage(AreaAnnouncementMessage, clientId, writer);
    }

    /// <summary>
    /// Host: approves its own client, and other clients through <see cref="JoinApproval.Evaluate"/>, counting
    /// connected clients plus those approved but not yet connected; a refusal goes back as a tagged disconnect
    /// reason. Never creates a player object, since players are spawned once a client is ready.
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
    }

    /// <summary>
    /// Moves <paramref name="clientId"/> from admitted to connected and, on the host, sends a late joiner the current
    /// area.
    /// </summary>
    private void HandleClientConnected(ulong clientId)
    {
        admittedClients.Remove(clientId);
        if (networkManager.IsServer && clientId != NetworkManager.ServerClientId && currentArea.HasValue)
        {
            SendAreaAnnouncement(clientId, currentArea.Value);
        }
    }

    /// <summary>
    /// Client: reads the host's area announcement and raises <see cref="AreaAnnounced"/>.
    /// </summary>
    private void HandleAreaAnnouncementMessage(ulong senderClientId, FastBufferReader payload)
    {
        AreaAnnounced?.Invoke(AreaAnnouncement.Read(payload));
    }

    /// <summary>
    /// Host: records a client's ready report for the current epoch (ignoring stale epochs and repeats), shows it every
    /// object spawned through this session that it cannot see yet, and raises <see cref="ClientReady"/>.
    /// </summary>
    private void HandleClientReadyMessage(ulong senderClientId, FastBufferReader payload)
    {
        payload.ReadValueSafe(out int epoch);
        if (epoch != AreaEpoch || !readyClients.Add(senderClientId))
        {
            return;
        }

        foreach (NetworkObject spawned in networkManager.SpawnManager.SpawnedObjectsList)
        {
            if (spawned.CheckObjectVisibility == visibleToReadyClients && !spawned.IsNetworkVisibleTo(senderClientId))
            {
                spawned.NetworkShow(senderClientId);
            }
        }

        ClientReady?.Invoke(senderClientId);
    }

    /// <summary>
    /// Forgets a disconnected client's admission and readiness.
    /// </summary>
    private void HandleClientDisconnected(ulong clientId)
    {
        admittedClients.Remove(clientId);
        readyClients.Remove(clientId);
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
