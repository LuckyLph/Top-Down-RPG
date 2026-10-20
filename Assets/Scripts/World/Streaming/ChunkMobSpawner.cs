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
/// wherever it walks, so it goes with the chunk. Persistence (Docs/WorldStreaming.md: Persistence): a mob killed here
/// is recorded in the chunk's record of its <see cref="WorldState"/>, by its spawn point's <see cref="PersistentId"/>
/// and the world clock's time, and a spawn point whose mob stays dead (<see cref="ChunkState.StaysDead"/>, by its
/// config's respawn rule) is skipped as the chunk loads. Spawns and despawns each mob through
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
    private readonly WorldState worldState;
    private readonly SessionSave sessionSave;
    private readonly List<MobController> mobs = new();
    private readonly List<Health> mobHealths = new();
    private readonly List<string> mobIds = new();
    private readonly List<NetworkObject> registeredPrefabs = new();
    private bool following;

    /// <summary>
    /// Creates the spawner of the chunk <paramref name="chunk"/>'s <paramref name="chunkSpawnPoints"/>, instantiating
    /// through <paramref name="chunkResolver"/> into <paramref name="chunkScene"/>, reading and recording its mobs'
    /// deaths in <paramref name="state"/> at the time of <paramref name="save"/>'s world clock.
    /// </summary>
    public ChunkMobSpawner(
        IObjectResolver chunkResolver,
        ChunkIdentity chunk,
        MobSpawnPoint[] chunkSpawnPoints,
        Scene chunkScene,
        INetworkObjectSpawner networkObjectSpawner,
        ChunkNetworkPrefabs networkPrefabs,
        Worlds loadedWorlds,
        WorldState state,
        SessionSave save)
    {
        resolver = chunkResolver;
        identity = chunk;
        spawnPoints = chunkSpawnPoints;
        scene = chunkScene;
        networkObjects = networkObjectSpawner;
        chunkPrefabs = networkPrefabs;
        worlds = loadedWorlds;
        worldState = state;
        sessionSave = save;
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
    /// Spawns a mob at each spawn point that <see cref="ShouldSpawn"/> lets spawn, gives it this chunk as its home,
    /// network-spawns it in a session and follows its death. Logs an error for a spawn point without a prefab and skips
    /// it.
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
            Health health = mob.GetComponent<Health>();
            if (health != null)
            {
                health.Died += HandleMobDied;
            }

            mobHealths.Add(health);
            mobIds.Add(IdOf(spawnPoint));
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
            if (mobHealths[i] != null)
            {
                mobHealths[i].Died -= HandleMobDied;
            }

            MobSpawning.Despawn(mobs[i], networkObjects);
        }

        mobs.Clear();
        mobHealths.Clear();
        mobIds.Clear();
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
    /// Whether <paramref name="spawnPoint"/> spawns its mob as the chunk loads: unless the chunk's record says its mob
    /// was killed and stays dead, by its config's respawn rule at the world clock's time now. A spawn point without a
    /// persistent id always spawns.
    /// </summary>
    private bool ShouldSpawn(MobSpawnPoint spawnPoint)
    {
        string id = IdOf(spawnPoint);
        if (id == null || !worldState.TryGetChunk(identity.Coords, out ChunkState chunk))
        {
            return true;
        }

        MobConfig config = spawnPoint.MobPrefab != null ? spawnPoint.MobPrefab.Config : null;
        bool respawns = config == null || config.Respawns;
        float delay = config != null ? config.RespawnDelaySeconds : 0f;
        return !chunk.StaysDead(id, sessionSave.Time, respawns, delay);
    }

    /// <summary>
    /// The persistent id of <paramref name="spawnPoint"/>, or null when it has none.
    /// </summary>
    private static string IdOf(MobSpawnPoint spawnPoint)
    {
        return spawnPoint.TryGetComponent(out PersistentId persistentId) && persistentId.HasId ? persistentId.Id : null;
    }

    /// <summary>
    /// Records a killed mob's death in the chunk's record at the world clock's time, by its spawn point's id; warns for
    /// a spawn point without one, whose mob comes back whenever the chunk loads.
    /// </summary>
    private void HandleMobDied(Health health)
    {
        int index = mobHealths.IndexOf(health);
        if (index < 0)
        {
            return;
        }

        health.Died -= HandleMobDied;
        string id = mobIds[index];
        if (id == null)
        {
            Debug.LogWarning($"{health.name} died, but its spawn point in chunk {identity.Coords} has no {nameof(PersistentId)}; its death is not kept.", health);
            return;
        }

        worldState.Chunk(identity.Coords).RecordMobDeath(id, sessionSave.Time);
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
