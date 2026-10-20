using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;

/// <summary>
/// Gameplay-scope registry of the network prefabs that the loaded chunks of a client's worlds register, so the host's
/// copies arrive injected by their chunk's scope and in their chunk's scene. Netcode keeps one handler per prefab while
/// several chunks register the same prefab, so this keeps one <see cref="Router"/> per prefab, installed when the first
/// chunk registers it and removed when the last one unregisters it, which sends each copy to the registered chunk
/// holding its spawn position (the nearest one when none does).
/// </summary>
public sealed class ChunkNetworkPrefabs : IDisposable
{
    private readonly INetworkObjectSpawner networkObjects;
    private readonly Dictionary<NetworkObject, Router> routers = new();

    /// <summary>
    /// Creates the registry, installing handlers through <paramref name="networkObjectSpawner"/>.
    /// </summary>
    public ChunkNetworkPrefabs(INetworkObjectSpawner networkObjectSpawner)
    {
        networkObjects = networkObjectSpawner;
    }

    /// <summary>
    /// Makes copies of <paramref name="prefab"/> spawned in <paramref name="chunk"/> come from
    /// <paramref name="resolver"/> into <paramref name="scene"/>. A chunk registering a prefab twice counts once.
    /// </summary>
    public void Register(NetworkObject prefab, ChunkIdentity chunk, IObjectResolver resolver, Scene scene)
    {
        if (!routers.TryGetValue(prefab, out Router router))
        {
            router = new Router(prefab);
            routers.Add(prefab, router);
            networkObjects.RegisterPrefab(prefab, router);
        }

        router.Add(chunk, resolver, scene);
    }

    /// <summary>
    /// Forgets <paramref name="chunk"/>'s registration of <paramref name="prefab"/>, removing the prefab's handler once no
    /// chunk has it.
    /// </summary>
    public void Unregister(NetworkObject prefab, ChunkIdentity chunk)
    {
        if (routers.TryGetValue(prefab, out Router router) && router.Remove(chunk) && router.IsEmpty)
        {
            routers.Remove(prefab);
            networkObjects.UnregisterPrefab(prefab);
        }
    }

    /// <summary>
    /// Whether some chunk has <paramref name="prefab"/> registered.
    /// </summary>
    public bool IsRegistered(NetworkObject prefab)
    {
        return routers.ContainsKey(prefab);
    }

    /// <summary>
    /// Removes every handler still installed.
    /// </summary>
    public void Dispose()
    {
        foreach (NetworkObject prefab in routers.Keys)
        {
            networkObjects.UnregisterPrefab(prefab);
        }

        routers.Clear();
    }

    /// <summary>
    /// The one handler of a prefab: creates each copy through the registered chunk holding its spawn position, or the
    /// nearest registered chunk, and destroys copies on despawn.
    /// </summary>
    private sealed class Router : INetworkPrefabInstanceHandler
    {
        private readonly NetworkObject prefab;
        private readonly List<Route> routes = new();

        /// <summary>
        /// Creates the handler of <paramref name="routedPrefab"/>, with no route yet.
        /// </summary>
        public Router(NetworkObject routedPrefab)
        {
            prefab = routedPrefab;
        }

        public bool IsEmpty => routes.Count == 0;

        /// <summary>
        /// Adds a route to <paramref name="chunk"/>, unless it has one.
        /// </summary>
        public void Add(ChunkIdentity chunk, IObjectResolver resolver, Scene scene)
        {
            if (IndexOf(chunk) < 0)
            {
                routes.Add(new Route(chunk, new InjectingNetworkPrefabHandler(prefab, resolver, scene)));
            }
        }

        /// <summary>
        /// Removes the route to <paramref name="chunk"/>; false when it had none.
        /// </summary>
        public bool Remove(ChunkIdentity chunk)
        {
            int index = IndexOf(chunk);
            if (index < 0)
            {
                return false;
            }

            routes.RemoveAt(index);
            return true;
        }

        /// <summary>
        /// Creates the copy through the route of the chunk holding <paramref name="position"/>, or the nearest one.
        /// </summary>
        public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
        {
            return RouteAt(position).Handler.Instantiate(ownerClientId, position, rotation);
        }

        /// <summary>
        /// Destroys the despawned copy's game object; ignores one that is already gone.
        /// </summary>
        public void Destroy(NetworkObject networkObject)
        {
            if (networkObject != null)
            {
                UnityEngine.Object.Destroy(networkObject.gameObject);
            }
        }

        /// <summary>
        /// The route whose chunk holds <paramref name="position"/>, else the one whose chunk lies nearest to it.
        /// </summary>
        private Route RouteAt(Vector3 position)
        {
            Route nearest = routes[0];
            long nearestDistance = long.MaxValue;
            for (int i = 0; i < routes.Count; i++)
            {
                ChunkIdentity chunk = routes[i].Chunk;
                Vector2Int cell = ChunkCoords.CellOf(chunk.World != null ? chunk.World.ToLocal(position) : (Vector2)position);
                long distance = ChunkCoords.SquaredDistance(cell, chunk.Coords);
                if (distance < nearestDistance)
                {
                    nearest = routes[i];
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        /// <summary>
        /// The index of the route to <paramref name="chunk"/>'s world and coordinates, or -1.
        /// </summary>
        private int IndexOf(ChunkIdentity chunk)
        {
            for (int i = 0; i < routes.Count; i++)
            {
                if (routes[i].Chunk.World == chunk.World && routes[i].Chunk.Coords == chunk.Coords)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>
    /// One chunk's registration of a prefab: the chunk and the handler that injects copies from its scope into its scene.
    /// </summary>
    private readonly struct Route
    {
        /// <summary>
        /// Pairs <paramref name="chunk"/> with its <paramref name="handler"/>.
        /// </summary>
        public Route(ChunkIdentity chunk, InjectingNetworkPrefabHandler handler)
        {
            Chunk = chunk;
            Handler = handler;
        }

        public ChunkIdentity Chunk { get; }
        public InjectingNetworkPrefabHandler Handler { get; }
    }
}
