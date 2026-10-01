using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;

public sealed class NetworkSession : IGameAuthority, IDisposable
{
    public const string ClientReadyMessage = "TopDownRPG.ClientReady";

    private readonly NetworkManager networkManager;
    private readonly UnityTransport transport;
    private readonly NetworkSettings settings;
    private readonly HashSet<ulong> readyClients = new();
    private readonly NetworkObject.VisibilityDelegate visibleToReadyClients;
    private bool shutdownRequested;

    public NetworkSession(NetworkManager networkManagerPrefab, NetworkSettings settings)
    {
        this.settings = settings;
        visibleToReadyClients = IsClientReady;
        networkManager = Object.Instantiate(networkManagerPrefab);
        networkManager.name = networkManagerPrefab.name;
        transport = networkManager.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogError($"{networkManagerPrefab.name} has no {nameof(UnityTransport)}; hosting and joining will fail.", networkManagerPrefab);
        }

        networkManager.OnClientStopped += HandleClientStopped;
        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
    }

    public event Action ConnectionLost;
    public event Action<ulong> ClientReady;

    public NetworkManager NetworkManager => networkManager;
    public NetworkSettings Settings => settings;
    public bool IsActive => networkManager.IsListening;
    public bool IsHost => networkManager.IsHost;
    public bool IsServer => networkManager.IsServer;
    public bool IsConnectedClient => networkManager.IsConnectedClient;
    public bool IsAuthoritative => !IsActive || networkManager.IsServer;
    public ulong LocalClientId => networkManager.LocalClientId;
    public IReadOnlyCollection<ulong> ReadyClients => readyClients;

    public bool StartHost()
    {
        if (IsActive || transport == null)
        {
            return false;
        }

        shutdownRequested = false;
        readyClients.Clear();
        networkManager.SetSingleton();
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

        shutdownRequested = false;
        readyClients.Clear();
        networkManager.SetSingleton();
        string hostAddress = string.IsNullOrWhiteSpace(address) ? settings.DefaultAddress : address.Trim();
        transport.SetConnectionData(hostAddress, settings.Port);
        if (!networkManager.StartClient())
        {
            return false;
        }

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

        using FastBufferWriter writer = new(0, Allocator.Temp);
        networkManager.CustomMessagingManager.SendNamedMessage(ClientReadyMessage, NetworkManager.ServerClientId, writer);
    }

    public void SpawnPlayerObject(NetworkObject instance, ulong ownerClientId)
    {
        instance.CheckObjectVisibility = visibleToReadyClients;
        instance.SpawnAsPlayerObject(ownerClientId, destroyWithScene: true);
    }

    public void RegisterPrefab(NetworkObject prefab, IObjectResolver resolver)
    {
        networkManager.PrefabHandler.AddHandler(prefab, new InjectingNetworkPrefabHandler(prefab, resolver));
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
        if (networkManager == null)
        {
            return;
        }

        networkManager.OnClientStopped -= HandleClientStopped;
        networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        Shutdown();
        Object.Destroy(networkManager.gameObject);
    }

    private void HandleClientReadyMessage(ulong senderClientId, FastBufferReader payload)
    {
        if (!readyClients.Add(senderClientId))
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
        if (shutdownRequested)
        {
            return;
        }

        shutdownRequested = true;
        ConnectionLost?.Invoke();
    }
}
