using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

/// <summary>
/// A chunk scope's entry point that spawns one mob per <see cref="MobSpawnPoint"/> of its chunk into the chunk's
/// scene, and takes them out again before the chunk unloads. Offline or on the host it spawns them once the chunk is
/// ready (in <c>PostStart</c>, after <see cref="ChunkNavigation"/> added the chunk's page in <c>Start</c>), through the
/// chunk's scope, so they path on their world's <see cref="WorldNavigation"/> and consider only its
/// <see cref="WorldPlayers"/>; each mob gets its home chunk, which bounds its patrol and its leash. In a session it
/// spawns them as network objects of this chunk, which reach each client once it holds the chunk, and network-despawns
/// them. In a session it registers each distinct mob prefab for this chunk (<see cref="ChunkNetworkPrefabs"/>): on a
/// client so the host's mobs whose home is this chunk arrive injected and in this chunk's scene, on the host so they
/// carry their home chunk. On a client it spawns nothing. A mob stays in its home chunk's scene for its whole life,
/// wherever it walks, so it goes with the chunk. Shares its spawning with <see cref="AreaMobSpawner"/> through
/// <see cref="MobSpawning"/>.
/// </summary>
public sealed class ChunkMobSpawner : IPostStartable, IDisposable
{
    private readonly IObjectResolver resolver;
    private readonly ChunkIdentity identity;
    private readonly MobSpawnPoint[] spawnPoints;
    private readonly Scene scene;
    private readonly INetworkObjectSpawner networkObjects;
    private readonly ChunkNetworkPrefabs chunkPrefabs;
    private readonly Worlds worlds;
    private readonly List<MobController> mobs = new();
    private readonly List<NetworkObject> registeredPrefabs = new();
    private bool following;

    /// <summary>
    /// Creates the spawner of the chunk <paramref name="chunk"/>'s <paramref name="chunkSpawnPoints"/>, instantiating
    /// through <paramref name="chunkResolver"/> into <paramref name="chunkScene"/>.
    /// </summary>
    public ChunkMobSpawner(
        IObjectResolver chunkResolver,
        ChunkIdentity chunk,
        MobSpawnPoint[] chunkSpawnPoints,
        Scene chunkScene,
        INetworkObjectSpawner networkObjectSpawner,
        ChunkNetworkPrefabs networkPrefabs,
        Worlds loadedWorlds)
    {
        resolver = chunkResolver;
        identity = chunk;
        spawnPoints = chunkSpawnPoints;
        scene = chunkScene;
        networkObjects = networkObjectSpawner;
        chunkPrefabs = networkPrefabs;
        worlds = loadedWorlds;
    }

    /// <summary>
    /// The mobs this spawner spawned and has not despawned; a killed one reads as null.
    /// </summary>
    public IReadOnlyList<MobController> Mobs => mobs;

    /// <summary>
    /// The chunk's area in world units: its 64 x 64 cells from its world's origin.
    /// </summary>
    public Rect HomeArea
    {
        get
        {
            RectInt cells = ChunkCoords.Bounds(identity.Coords);
            Vector2 min = identity.World != null ? identity.World.ToWorld(cells.min) : (Vector2)cells.min;
            return new Rect(min, cells.size);
        }
    }

    /// <summary>
    /// In a session, registers each distinct networked mob prefab of the chunk's spawn points for this chunk. Runs from
    /// the chunk scope's build callback; does nothing offline.
    /// </summary>
    public void RegisterNetworkPrefabs()
    {
        if (!networkObjects.IsActive || registeredPrefabs.Count > 0)
        {
            return;
        }

        MobSpawning.CollectNetworkPrefabs(spawnPoints, registeredPrefabs);
        foreach (NetworkObject prefab in registeredPrefabs)
        {
            chunkPrefabs.Register(prefab, identity, resolver, scene);
        }
    }

    /// <summary>
    /// Spawns the chunk's mobs when this machine has authority (offline or the host), and despawns them when the chunk
    /// starts unloading (<see cref="Worlds.ChunkUnloading"/>).
    /// </summary>
    public void PostStart()
    {
        if (!MobSpawning.HasAuthority(networkObjects))
        {
            return;
        }

        worlds.ChunkUnloading += HandleChunkUnloading;
        following = true;
        SpawnAll();
    }

    /// <summary>
    /// Spawns a mob at each spawn point that <see cref="ShouldSpawn"/> lets spawn, gives it this chunk as its home and
    /// network-spawns it in a session. Logs an error for a spawn point without a prefab and skips it.
    /// </summary>
    public void SpawnAll()
    {
        Rect home = HomeArea;
        foreach (MobSpawnPoint spawnPoint in spawnPoints)
        {
            if (spawnPoint == null || !ShouldSpawn(spawnPoint))
            {
                continue;
            }

            MobController mob = MobSpawning.Instantiate(spawnPoint, resolver, scene);
            if (mob == null)
            {
                continue;
            }

            mob.SetHome(home);
            MobSpawning.NetworkSpawn(mob, networkObjects, ChunkId.Of(identity.World, identity.Coords));
            mobs.Add(mob);
        }
    }

    /// <summary>
    /// Takes every mob this spawner spawned out of the game: network-despawned in a session, destroyed offline. Killed
    /// mobs are already gone.
    /// </summary>
    public void DespawnAll()
    {
        for (int i = 0; i < mobs.Count; i++)
        {
            MobSpawning.Despawn(mobs[i], networkObjects);
        }

        mobs.Clear();
    }

    /// <summary>
    /// Stops following the chunk's unloading, despawns any mob still here and unregisters the prefabs it registered, as
    /// the chunk's scope goes away.
    /// </summary>
    public void Dispose()
    {
        if (following)
        {
            worlds.ChunkUnloading -= HandleChunkUnloading;
            following = false;
        }

        DespawnAll();
        foreach (NetworkObject prefab in registeredPrefabs)
        {
            chunkPrefabs.Unregister(prefab, identity);
        }

        registeredPrefabs.Clear();
    }

    /// <summary>
    /// Whether <paramref name="spawnPoint"/> spawns its mob as the chunk loads: always, until the world state records
    /// killed mobs and their respawn delay (Docs/WorldStreaming.md: Persistence, Phase 8).
    /// </summary>
    private bool ShouldSpawn(MobSpawnPoint spawnPoint)
    {
        return true;
    }

    /// <summary>
    /// Despawns the mobs before this chunk's scene unloads, so in a session clients drop their copies first.
    /// </summary>
    private void HandleChunkUnloading(LoadedWorld world, Vector2Int coords)
    {
        if (world.Definition == identity.World && coords == identity.Coords)
        {
            DespawnAll();
        }
    }
}
