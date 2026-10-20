using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

/// <summary>
/// A chunk scope's entry point for a chunk with an <see cref="AreaTerrain"/>: per-chunk terrain sync
/// (Docs/WorldStreaming.md: Networking). In a session it registers the terrain's sync prefab for this chunk with
/// <see cref="ChunkNetworkPrefabs"/>, so a client's copy arrives injected with this chunk's terrain. On the host it then
/// applies the chunk's persisted edits, spawns the <see cref="NetworkAreaTerrain"/> into the chunk's scene as an object
/// of this chunk, so a client receives the edits once it holds the chunk, as a late joiner does, and despawns it before
/// the chunk unloads. Offline nothing is spawned: the edits only need to reach this machine.
/// </summary>
public sealed class ChunkTerrainSync : IStartable, IDisposable
{
    private readonly IObjectResolver resolver;
    private readonly ChunkIdentity identity;
    private readonly AreaTerrain terrain;
    private readonly Scene scene;
    private readonly INetworkObjectSpawner networkObjects;
    private readonly ChunkNetworkPrefabs chunkPrefabs;
    private readonly Worlds worlds;
    private NetworkObject registeredPrefab;
    private NetworkObject spawned;
    private bool following;

    /// <summary>
    /// Creates the sync of <paramref name="chunkTerrain"/> in the chunk <paramref name="chunk"/>'s
    /// <paramref name="chunkScene"/>, instantiating through the chunk's <paramref name="chunkResolver"/>.
    /// </summary>
    public ChunkTerrainSync(
        IObjectResolver chunkResolver,
        ChunkIdentity chunk,
        AreaTerrain chunkTerrain,
        Scene chunkScene,
        INetworkObjectSpawner networkObjectSpawner,
        ChunkNetworkPrefabs networkPrefabs,
        Worlds loadedWorlds)
    {
        resolver = chunkResolver;
        identity = chunk;
        terrain = chunkTerrain;
        scene = chunkScene;
        networkObjects = networkObjectSpawner;
        chunkPrefabs = networkPrefabs;
        worlds = loadedWorlds;
    }

    /// <summary>
    /// The host's spawned sync object, while it lives.
    /// </summary>
    public NetworkObject Spawned => spawned;

    /// <summary>
    /// In a session, registers the terrain's sync prefab for this chunk. Runs from the chunk scope's build callback;
    /// does nothing offline, without a sync prefab or when already registered.
    /// </summary>
    public void RegisterNetworkPrefab()
    {
        if (!networkObjects.IsActive || terrain.SyncPrefab == null || registeredPrefab != null)
        {
            return;
        }

        registeredPrefab = terrain.SyncPrefab;
        chunkPrefabs.Register(registeredPrefab, identity, resolver, scene);
    }

    /// <summary>
    /// Host only: applies the chunk's persisted edits, then instantiates the sync prefab through the chunk's scope, moves
    /// it into the chunk's scene and network-spawns it as an object of this chunk, and despawns it when the chunk starts
    /// unloading. Logs an error when the terrain has no sync prefab.
    /// </summary>
    public void Start()
    {
        if (!networkObjects.IsActive || !networkObjects.IsServer)
        {
            return;
        }

        if (terrain.SyncPrefab == null)
        {
            Debug.LogError($"{terrain.name} has no sync prefab; its terrain edits will not reach clients.", terrain);
            return;
        }

        ApplyPersistedEdits();
        spawned = resolver.Instantiate(terrain.SyncPrefab);
        spawned.name = terrain.SyncPrefab.name;
        SceneManager.MoveGameObjectToScene(spawned.gameObject, scene);
        networkObjects.Spawn(spawned, ChunkId.Of(identity.World, identity.Coords));
        worlds.ChunkUnloading += HandleChunkUnloading;
        following = true;
    }

    /// <summary>
    /// Stops following the chunk's unloading, despawns the sync object if it is still there and unregisters the prefab.
    /// </summary>
    public void Dispose()
    {
        if (following)
        {
            worlds.ChunkUnloading -= HandleChunkUnloading;
            following = false;
        }

        DespawnSync();
        if (registeredPrefab != null)
        {
            chunkPrefabs.Unregister(registeredPrefab, identity);
            registeredPrefab = null;
        }
    }

    /// <summary>
    /// Where the chunk's persisted terrain edits are written onto its tilemaps before the sync object spawns, so it
    /// starts with them: none until the world state keeps them (Docs/WorldStreaming.md: Persistence, Phase 8).
    /// </summary>
    private void ApplyPersistedEdits()
    {
    }

    /// <summary>
    /// Despawns the sync object before this chunk's scene unloads, so clients drop their copies first.
    /// </summary>
    private void HandleChunkUnloading(LoadedWorld world, UnityEngine.Vector2Int coords)
    {
        if (world.Definition == identity.World && coords == identity.Coords)
        {
            DespawnSync();
        }
    }

    /// <summary>
    /// Takes the spawned sync object out of the game, once.
    /// </summary>
    private void DespawnSync()
    {
        if (spawned != null)
        {
            networkObjects.Despawn(spawned);
        }

        spawned = null;
    }
}
