using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

/// <summary>
/// A chunk scope's entry point for a chunk with an <see cref="AreaTerrain"/>: per-chunk terrain sync
/// (Docs/WorldStreaming.md: Networking) and persistence (Persistence). Offline and on the host it restores the chunk's
/// edits from its record in the world's <see cref="WorldState"/> as the chunk loads and records every edit made after
/// that. In a session it registers the terrain's sync prefab for this chunk with <see cref="ChunkNetworkPrefabs"/>, so a
/// client's copy arrives injected with this chunk's terrain; the host then spawns the <see cref="NetworkAreaTerrain"/>
/// into the chunk's scene as an object of this chunk, after restoring the edits, so a client receives them once it holds
/// the chunk, as a late joiner does, and despawns it before the chunk unloads. Offline nothing is spawned: the edits only
/// need to reach this machine.
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
    private readonly WorldState worldState;
    private NetworkObject registeredPrefab;
    private NetworkObject spawned;
    private bool following;
    private bool recording;

    /// <summary>
    /// Creates the sync of <paramref name="chunkTerrain"/> in the chunk <paramref name="chunk"/>'s
    /// <paramref name="chunkScene"/>, instantiating through the chunk's <paramref name="chunkResolver"/> and keeping the
    /// chunk's edits in <paramref name="state"/>.
    /// </summary>
    public ChunkTerrainSync(
        IObjectResolver chunkResolver,
        ChunkIdentity chunk,
        AreaTerrain chunkTerrain,
        Scene chunkScene,
        INetworkObjectSpawner networkObjectSpawner,
        ChunkNetworkPrefabs networkPrefabs,
        Worlds loadedWorlds,
        WorldState state)
    {
        resolver = chunkResolver;
        identity = chunk;
        terrain = chunkTerrain;
        scene = chunkScene;
        networkObjects = networkObjectSpawner;
        chunkPrefabs = networkPrefabs;
        worlds = loadedWorlds;
        worldState = state;
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
    /// Offline and on the host: restores the chunk's persisted edits and records new ones. On the host it then
    /// instantiates the sync prefab through the chunk's scope, moves it into the chunk's scene and network-spawns it as
    /// an object of this chunk, and despawns it when the chunk starts unloading; it logs an error when the terrain has
    /// no sync prefab.
    /// </summary>
    public void Start()
    {
        if (networkObjects.IsActive && !networkObjects.IsServer)
        {
            return;
        }

        ApplyPersistedEdits();
        if (!networkObjects.IsActive)
        {
            return;
        }

        if (terrain.SyncPrefab == null)
        {
            Debug.LogError($"{terrain.name} has no sync prefab; its terrain edits will not reach clients.", terrain);
            return;
        }

        spawned = resolver.Instantiate(terrain.SyncPrefab);
        spawned.name = terrain.SyncPrefab.name;
        SceneManager.MoveGameObjectToScene(spawned.gameObject, scene);
        networkObjects.Spawn(spawned, ChunkId.Of(identity.World, identity.Coords));
        worlds.ChunkUnloading += HandleChunkUnloading;
        following = true;
    }

    /// <summary>
    /// Stops recording edits and following the chunk's unloading, despawns the sync object if it is still there and
    /// unregisters the prefab.
    /// </summary>
    public void Dispose()
    {
        if (recording)
        {
            terrain.EditRecorded -= HandleEditRecorded;
            recording = false;
        }

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
    /// Writes the chunk's persisted terrain edits onto its tilemaps, recorded as the terrain's edits so they replicate,
    /// then starts recording every new edit in the chunk's record.
    /// </summary>
    private void ApplyPersistedEdits()
    {
        if (worldState.TryGetChunk(identity.Coords, out ChunkState chunk) && chunk.TerrainEdits.Count > 0)
        {
            terrain.RestoreEdits(chunk.TerrainEdits);
        }

        terrain.EditRecorded += HandleEditRecorded;
        recording = true;
    }

    /// <summary>
    /// Records the terrain's edit at <paramref name="index"/> in the chunk's record, its layer named by what it draws
    /// (<see cref="AreaTerrain.KeyOf"/>).
    /// </summary>
    private void HandleEditRecorded(int index)
    {
        TerrainEdit edit = terrain.Edits[index];
        worldState.Chunk(identity.Coords).RecordTerrainEdit(terrain.KeyOf(edit.Layer), (Vector2Int)edit.Cell, edit.Filled);
    }

    /// <summary>
    /// Despawns the sync object before this chunk's scene unloads, so clients drop their copies first.
    /// </summary>
    private void HandleChunkUnloading(LoadedWorld world, Vector2Int coords)
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
