using System;
using System.Threading;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;

public sealed class NetworkSession : IDisposable
{
    private readonly NetworkManager networkManager;
    private readonly UnityTransport transport;
    private readonly NetworkSettings settings;
    private bool shutdownRequested;

    public NetworkSession(NetworkManager networkManagerPrefab, NetworkSettings settings)
    {
        this.settings = settings;
        networkManager = Object.Instantiate(networkManagerPrefab);
        networkManager.name = networkManagerPrefab.name;
        transport = networkManager.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogError($"{networkManagerPrefab.name} has no {nameof(UnityTransport)}; hosting and joining will fail.", networkManagerPrefab);
        }

        networkManager.OnClientStopped += HandleClientStopped;
    }

    public event Action ConnectionLost;

    public NetworkManager NetworkManager => networkManager;
    public NetworkSettings Settings => settings;
    public bool IsActive => networkManager.IsListening;
    public bool IsHost => networkManager.IsHost;
    public bool IsConnectedClient => networkManager.IsConnectedClient;

    public bool StartHost()
    {
        if (IsActive || transport == null)
        {
            return false;
        }

        shutdownRequested = false;
        transport.SetConnectionData(settings.DefaultAddress, settings.Port, settings.ListenAddress);
        return networkManager.StartHost();
    }

    public async Awaitable<bool> JoinAsync(string address, float timeoutSeconds, CancellationToken cancellation)
    {
        if (IsActive || transport == null)
        {
            return false;
        }

        shutdownRequested = false;
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
        if (networkManager != null && networkManager.IsListening)
        {
            networkManager.Shutdown();
        }
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
        Shutdown();
        Object.Destroy(networkManager.gameObject);
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
