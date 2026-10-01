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

public sealed class NetworkSession : IGameAuthority, INetworkObjectSpawner, IDisposable
{
    public const string ClientReadyMessage = "TopDownRPG.ClientReady";
    public const string AreaAnnouncementMessage = "TopDownRPG.AreaAnnouncement";
    private const int AnnouncementBufferSize = 1024;

    private readonly NetworkManager networkManager;
    private readonly UnityTransport transport;
    private readonly NetworkSettings settings;
    private readonly HashSet<ulong> readyClients = new();
    private readonly NetworkObject.VisibilityDelegate visibleToReadyClients;
    private readonly List<NetworkObject> despawnBuffer = new();
    private AreaAnnouncement? currentArea;
    private bool shutdownRequested;
    private bool quitting;

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
    public bool IsAuthoritative => !IsActive || networkManager.IsServer;
    public ulong LocalClientId => networkManager.LocalClientId;
    public IReadOnlyCollection<ulong> ReadyClients => readyClients;
    public int AreaEpoch { get; private set; }

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

    public async Awaitable<bool> JoinAsync(string address, float timeoutSeconds, CancellationToken cancellation)
    {
        if (IsActive || transport == null)
        {
            return false;
        }

        ResetSessionState();
        string hostAddress = string.IsNullOrWhiteSpace(address) ? settings.DefaultAddress : address.Trim();
        transport.SetConnectionData(hostAddress, settings.Port);
        if (!networkManager.StartClient())
        {
            return false;
        }

        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(AreaAnnouncementMessage, HandleAreaAnnouncementMessage);

        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        try
        {
            while (!networkManager.IsConnectedClient)
            {
                if (!networkManager.IsListening || Time.realtimeSinceStartup > deadline)
                {
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

    public void Shutdown()
    {
        shutdownRequested = true;
        readyClients.Clear();
        if (networkManager != null && networkManager.IsListening)
        {
            networkManager.Shutdown();
        }
    }

    public bool IsClientReady(ulong clientId)
    {
        return clientId == NetworkManager.ServerClientId || readyClients.Contains(clientId);
    }

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

    public void BeginAnnouncedArea(AreaAnnouncement announcement)
    {
        if (!networkManager.IsServer)
        {
            AreaEpoch = announcement.Epoch;
        }
    }

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

    public bool HasPlayerObject(ulong clientId)
    {
        return networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) && client.PlayerObject != null;
    }

    public void Spawn(NetworkObject instance)
    {
        instance.CheckObjectVisibility = visibleToReadyClients;
        instance.Spawn(destroyWithScene: true);
    }

    public void SpawnPlayerObject(NetworkObject instance, ulong ownerClientId)
    {
        instance.CheckObjectVisibility = visibleToReadyClients;
        instance.SpawnAsPlayerObject(ownerClientId, destroyWithScene: true);
    }

    public void RegisterPrefab(NetworkObject prefab, IObjectResolver resolver, Scene targetScene)
    {
        networkManager.PrefabHandler.AddHandler(prefab, new InjectingNetworkPrefabHandler(prefab, resolver, targetScene));
    }

    public void UnregisterPrefab(NetworkObject prefab)
    {
        if (networkManager != null)
        {
            networkManager.PrefabHandler.RemoveHandler(prefab);
        }
    }

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

        networkManager.OnClientStopped -= HandleClientStopped;
        networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        networkManager.OnClientConnectedCallback -= HandleClientConnected;
        Shutdown();
        Object.Destroy(networkManager.gameObject);
    }

    private void HandleQuitting()
    {
        quitting = true;
    }

#if UNITY_EDITOR
    private void HandlePlayModeStateChanged(UnityEditor.PlayModeStateChange change)
    {
        if (change == UnityEditor.PlayModeStateChange.ExitingPlayMode)
        {
            quitting = true;
        }
    }
#endif

    private void ResetSessionState()
    {
        shutdownRequested = false;
        readyClients.Clear();
        currentArea = null;
        AreaEpoch = 0;
        networkManager.SetSingleton();
    }

    private void SendAreaAnnouncement(ulong clientId, AreaAnnouncement announcement)
    {
        using FastBufferWriter writer = new(AnnouncementBufferSize, Allocator.Temp);
        announcement.Write(writer);
        networkManager.CustomMessagingManager.SendNamedMessage(AreaAnnouncementMessage, clientId, writer);
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (networkManager.IsServer && clientId != NetworkManager.ServerClientId && currentArea.HasValue)
        {
            SendAreaAnnouncement(clientId, currentArea.Value);
        }
    }

    private void HandleAreaAnnouncementMessage(ulong senderClientId, FastBufferReader payload)
    {
        AreaAnnounced?.Invoke(AreaAnnouncement.Read(payload));
    }

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

    private void HandleClientDisconnected(ulong clientId)
    {
        readyClients.Remove(clientId);
    }

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
