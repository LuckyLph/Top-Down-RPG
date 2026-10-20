using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// Tests for <see cref="ChunkMobSpawner"/> and <see cref="ChunkNetworkPrefabs"/>: a chunk's mobs spawned offline and on
/// the host with their home chunk, despawned when the chunk unloads or its scope goes, spawn points without a prefab,
/// a killed mob recorded in its world's state and skipped as its chunk loads until its respawn delay has passed, and
/// on a client each mob prefab registered once however many chunks list it, each copy arriving in its chunk.
/// </summary>
public class ChunkMobSpawnerTests
{
    private const string WeaselPrefabPath = "Assets/Prefabs/Mobs/Weasel.prefab";
    private static readonly Vector2Int Origin = new(2048, 0);
    private static readonly Vector2Int Chunk = new(1, 0);

    private readonly List<Object> created = new();
    private readonly List<ChunkMobSpawner> spawners = new();
    private WorldDefinition world;
    private Worlds worlds;
    private ManualClock gameClock;
    private SessionSave sessionSave;

    /// <summary>
    /// Creates the 2 x 2 chunk world at (2048, 0) the spawners belong to, the registry of loaded worlds, and an empty
    /// session whose world clock the test moves.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        world = WorldDefinition.Create("Spawner", Origin, new Vector2Int(2, 2), new[] { Vector2Int.zero, Chunk });
        created.Add(world);
        worlds = new Worlds();
        gameClock = new ManualClock();
        sessionSave = new SessionSave(gameClock);
        sessionSave.StartNew();
    }

    /// <summary>
    /// Disposes the spawners and destroys the objects and scenes the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (ChunkMobSpawner spawner in spawners)
        {
            spawner.Dispose();
        }

        spawners.Clear();
        foreach (Object createdObject in created)
        {
            if (createdObject != null)
            {
                Object.DestroyImmediate(createdObject);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// Offline, the spawner spawns one injected mob per spawn point at its position in the chunk's scene, each with
    /// the chunk's cells as its home, and destroys them when its scope is disposed.
    /// </summary>
    [Test]
    public void Offline_SpawnsAMobPerSpawnPoint_WithTheChunkAsHome_AndDestroysThemOnDispose()
    {
        MobController template = CreateMobTemplate(networked: false);
        MobSpawnPoint first = CreateSpawnPoint("MobSpawn_A", template, LocalToWorld(70.5f, 10.5f));
        MobSpawnPoint second = CreateSpawnPoint("MobSpawn_B", template, LocalToWorld(100.5f, 40.5f));
        WorldNavigation navigation = new(new NavigationPagePool());
        ChunkMobSpawner spawner = CreateSpawner(new[] { first, null, second }, new FakeNetworkObjects(), navigation);

        spawner.PostStart();

        Assert.That(spawner.Mobs.Count, Is.EqualTo(2));
        Rect home = new(Origin.x + 64f, Origin.y, 64f, 64f);
        Assert.That(spawner.HomeArea, Is.EqualTo(home));
        for (int i = 0; i < spawner.Mobs.Count; i++)
        {
            MobController mob = spawner.Mobs[i];
            MobSpawnPoint spawnPoint = i == 0 ? first : second;
            Assert.That(mob, Is.Not.SameAs(template));
            Assert.That((Vector2)mob.transform.position, Is.EqualTo((Vector2)spawnPoint.transform.position));
            Assert.That(mob.gameObject.scene, Is.EqualTo(SceneManager.GetActiveScene()));
            Assert.That(mob.Navigation, Is.SameAs(navigation), "The mob is injected from the chunk's scope.");
            Assert.That(mob.Patrol.HasHome, Is.True);
            Assert.That(mob.Patrol.HomeArea, Is.EqualTo(home));
        }

        List<MobController> spawned = new(spawner.Mobs);
        spawner.Dispose();

        Assert.That(spawned.TrueForAll(mob => mob == null), Is.True, "Disposing the chunk's scope takes its mobs out.");
        Assert.That(spawner.Mobs, Is.Empty);
    }

    /// <summary>
    /// On the host the spawner registers its mob prefab (so each spawn carries its home chunk), network-spawns its mobs
    /// as objects of its chunk, and network-despawns them when its own chunk starts unloading, but not when another chunk
    /// does.
    /// </summary>
    [Test]
    public void OnTheHost_NetworkSpawnsItsMobs_AndDespawnsThemWhenItsChunkUnloads()
    {
        MobController template = CreateMobTemplate(networked: true);
        MobSpawnPoint first = CreateSpawnPoint("MobSpawn_A", template, LocalToWorld(70.5f, 10.5f));
        MobSpawnPoint second = CreateSpawnPoint("MobSpawn_B", template, LocalToWorld(100.5f, 40.5f));
        FakeNetworkObjects network = new() { IsActive = true, IsServer = true };
        ChunkMobSpawner spawner = CreateSpawner(new[] { first, second }, network, new WorldNavigation(new NavigationPagePool()));
        LoadedWorld loaded = new(world, default, null, null);
        worlds.Add(loaded);
        worlds.AddChunk(loaded, Vector2Int.zero, default);
        worlds.AddChunk(loaded, Chunk, default);

        spawner.RegisterNetworkPrefabs();
        spawner.PostStart();
        Assert.That(network.Spawned.Count, Is.EqualTo(2));
        Assert.That(network.Registered, Is.EqualTo(new[] { template.GetComponent<NetworkObject>() }), "The host registers the prefab so its spawns carry their home chunk.");
        foreach (NetworkObject mob in network.Spawned)
        {
            Assert.That(network.HomeChunks[mob], Is.EqualTo(ChunkId.Of(world, Chunk)), "Each mob is an object of its home chunk.");
        }

        worlds.RemoveChunk(loaded, Vector2Int.zero);
        Assert.That(network.Despawned, Is.Empty, "Another chunk unloading leaves these mobs alone.");

        worlds.RemoveChunk(loaded, Chunk);
        Assert.That(network.Despawned, Is.EquivalentTo(network.Spawned));
        Assert.That(spawner.Mobs, Is.Empty);
    }

    /// <summary>
    /// A mob killed offline is recorded in its chunk's record of the world's state, by its spawn point's persistent id,
    /// at the world clock's time; a mob whose spawn point has no id is not recorded.
    /// </summary>
    [Test]
    public void AKilledMob_IsRecordedInItsChunk_ByItsSpawnPointsId_AtTheWorldClocksTime()
    {
        MobController template = CreateMobTemplate(networked: false);
        template.gameObject.AddComponent<Health>();
        MobSpawnPoint kept = CreateSpawnPoint("MobSpawn_A", template, LocalToWorld(70.5f, 10.5f), "weasel-a");
        MobSpawnPoint unkept = CreateSpawnPoint("MobSpawn_B", template, LocalToWorld(100.5f, 40.5f));
        ChunkMobSpawner spawner = CreateSpawner(new[] { kept, unkept }, new FakeNetworkObjects(), new WorldNavigation(new NavigationPagePool()));
        spawner.PostStart();
        gameClock.Advance(42f);

        Health keptHealth = spawner.Mobs[0].GetComponent<Health>();
        keptHealth.ApplyDamage(keptHealth.MaxHealth);
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no PersistentId"));
        Health unkeptHealth = spawner.Mobs[1].GetComponent<Health>();
        unkeptHealth.ApplyDamage(unkeptHealth.MaxHealth);

        WorldState state = sessionSave.WorldOf(world.WorldId);
        Assert.That(state.TryGetChunk(Chunk, out ChunkState chunk), Is.True);
        Assert.That(chunk.TryGetMobDeath("weasel-a", out double diedAt), Is.True);
        Assert.That(diedAt, Is.EqualTo(42d));
        Assert.That(chunk.DeadMobCount, Is.EqualTo(1), "A spawn point without an id keeps no death.");
    }

    /// <summary>
    /// As its chunk loads, a spawn point whose mob was killed less than its respawn delay ago spawns nothing while the
    /// others spawn; once the delay has passed it spawns again, and a mob that never respawns stays away.
    /// </summary>
    [Test]
    public void AKilledMob_IsSkippedAsItsChunkLoads_UntilItsDelayHasPassed()
    {
        MobController weasel = CreateMobTemplate(networked: false, MobConfig.Create(respawnDelaySeconds: 100f));
        MobController boss = CreateMobTemplate(networked: false, MobConfig.Create(respawns: false));
        MobSpawnPoint killed = CreateSpawnPoint("MobSpawn_Killed", weasel, LocalToWorld(70.5f, 10.5f), "weasel-killed");
        MobSpawnPoint alive = CreateSpawnPoint("MobSpawn_Alive", weasel, LocalToWorld(100.5f, 40.5f), "weasel-alive");
        MobSpawnPoint bossPoint = CreateSpawnPoint("MobSpawn_Boss", boss, LocalToWorld(110.5f, 50.5f), "boss");
        ChunkState chunk = sessionSave.WorldOf(world.WorldId).Chunk(Chunk);
        chunk.RecordMobDeath("weasel-killed", 0d);
        chunk.RecordMobDeath("boss", 0d);
        gameClock.Time = 99f;

        ChunkMobSpawner early = CreateSpawner(new[] { killed, alive, bossPoint }, new FakeNetworkObjects(), new WorldNavigation(new NavigationPagePool()));
        early.PostStart();
        Assert.That(early.Mobs.Count, Is.EqualTo(1), "Only the mob that never died spawns before the delay.");
        Assert.That((Vector2)early.Mobs[0].transform.position, Is.EqualTo((Vector2)alive.transform.position));
        early.Dispose();
        spawners.Remove(early);

        gameClock.Time = 100f;
        ChunkMobSpawner late = CreateSpawner(new[] { killed, alive, bossPoint }, new FakeNetworkObjects(), new WorldNavigation(new NavigationPagePool()));
        late.PostStart();
        Assert.That(late.Mobs.Count, Is.EqualTo(2), "The killed weasel comes back once its delay has passed; the boss stays dead.");
        Assert.That(chunk.TryGetMobDeath("weasel-killed", out _), Is.False, "A mob that came back is forgotten.");
        Assert.That(chunk.TryGetMobDeath("boss", out _), Is.True);
    }

    /// <summary>
    /// A spawn point without a prefab logs an error and spawns nothing; the others still spawn.
    /// </summary>
    [Test]
    public void ASpawnPointWithoutAPrefab_LogsAnError_AndSpawnsNothing()
    {
        MobController template = CreateMobTemplate(networked: false);
        MobSpawnPoint empty = CreateSpawnPoint("MobSpawn_Empty", null, LocalToWorld(70.5f, 10.5f));
        MobSpawnPoint full = CreateSpawnPoint("MobSpawn_Full", template, LocalToWorld(80.5f, 10.5f));
        ChunkMobSpawner spawner = CreateSpawner(new[] { empty, full }, new FakeNetworkObjects(), new WorldNavigation(new NavigationPagePool()));

        LogAssert.Expect(LogType.Error, "MobSpawn_Empty has no mob prefab assigned; nothing spawns there.");
        spawner.PostStart();

        Assert.That(spawner.Mobs.Count, Is.EqualTo(1));
    }

    /// <summary>
    /// On a client two chunks listing the same mob prefab on several spawn points install one handler for it, spawn
    /// nothing of their own, and the handler stays until the last of them goes.
    /// </summary>
    [Test]
    public void OnAClient_RegistersEachMobPrefabOnce_SpawnsNothing_AndUnregistersWithTheLastChunk()
    {
        MobController weasel = AssetDatabase.LoadAssetAtPath<MobController>(WeaselPrefabPath);
        NetworkObject weaselPrefab = weasel.GetComponent<NetworkObject>();
        FakeNetworkObjects network = new() { IsActive = true, IsServer = false };
        ChunkNetworkPrefabs prefabs = new(network);
        ChunkMobSpawner west = CreateSpawner(
            new[] { CreateSpawnPoint("West_A", weasel, LocalToWorld(10.5f, 10.5f)), CreateSpawnPoint("West_B", weasel, LocalToWorld(20.5f, 10.5f)) },
            network,
            new WorldNavigation(new NavigationPagePool()),
            Vector2Int.zero,
            prefabs);
        ChunkMobSpawner east = CreateSpawner(
            new[] { CreateSpawnPoint("East_A", weasel, LocalToWorld(70.5f, 10.5f)) },
            network,
            new WorldNavigation(new NavigationPagePool()),
            Chunk,
            prefabs);

        west.RegisterNetworkPrefabs();
        east.RegisterNetworkPrefabs();
        west.PostStart();
        east.PostStart();

        Assert.That(network.Registered, Is.EqualTo(new[] { weaselPrefab }), "Netcode keeps one handler per prefab.");
        Assert.That(network.Spawned, Is.Empty);
        Assert.That(west.Mobs, Is.Empty, "Clients receive mobs from the host instead of spawning their own.");
        Assert.That(east.Mobs, Is.Empty);

        west.Dispose();
        Assert.That(prefabs.IsRegistered(weaselPrefab), Is.True, "The eastern chunk still needs the handler.");
        Assert.That(network.Registered, Is.EqualTo(new[] { weaselPrefab }));

        east.Dispose();
        Assert.That(prefabs.IsRegistered(weaselPrefab), Is.False);
        Assert.That(network.Registered, Is.Empty);
    }

    /// <summary>
    /// Offline nothing is registered.
    /// </summary>
    [Test]
    public void Offline_RegistersNothing()
    {
        MobSpawnPoint spawnPoint = CreateSpawnPoint("MobSpawn_A", AssetDatabase.LoadAssetAtPath<MobController>(WeaselPrefabPath), LocalToWorld(70.5f, 10.5f));
        FakeNetworkObjects network = new();
        ChunkMobSpawner spawner = CreateSpawner(new[] { spawnPoint }, network, new WorldNavigation(new NavigationPagePool()));

        spawner.RegisterNetworkPrefabs();

        Assert.That(network.Registered, Is.Empty);
    }

    /// <summary>
    /// The one handler of a prefab two chunks registered creates each copy through the scope of the chunk holding its
    /// position (told apart here by the clock each scope injects); a copy outside both goes to the nearer one.
    /// </summary>
    [Test]
    public void AClientsCopy_IsInjectedByTheChunkHoldingItsPosition()
    {
        GameObject prefabObject = new("NetworkedProbe");
        created.Add(prefabObject);
        NetworkObject prefab = prefabObject.AddComponent<NetworkObject>();
        prefabObject.AddComponent<MeleeDamageDealer>();
        FakeNetworkObjects network = new() { IsActive = true, IsServer = false };
        ChunkNetworkPrefabs prefabs = new(network);
        prefabs.Register(prefab, new ChunkIdentity(world, Vector2Int.zero), ResolverWithClockAt(100f), default);
        prefabs.Register(prefab, new ChunkIdentity(world, Chunk), ResolverWithClockAt(200f), default);
        prefabs.Register(prefab, new ChunkIdentity(world, Chunk), ResolverWithClockAt(300f), default);
        INetworkPrefabInstanceHandler handler = network.Handlers[prefab];

        Assert.That(ClockOf(handler.Instantiate(0, LocalToWorld(70.5f, 30.5f), Quaternion.identity)), Is.EqualTo(200f).Within(0.001f));
        Assert.That(ClockOf(handler.Instantiate(0, LocalToWorld(60.5f, 30.5f), Quaternion.identity)), Is.EqualTo(100f).Within(0.001f));
        Assert.That(ClockOf(handler.Instantiate(0, LocalToWorld(130.5f, 30.5f), Quaternion.identity)), Is.EqualTo(200f).Within(0.001f));
        Assert.That(network.Registered, Is.EqualTo(new[] { prefab }));

        prefabs.Dispose();
        Assert.That(network.Registered, Is.Empty);
    }

    /// <summary>
    /// The one handler of a prefab two chunks registered creates each copy through the scope of the home chunk the host
    /// sends with it, wherever the copy stands (a mob that walked into the neighbouring chunk); a home chunk no chunk
    /// registered falls back to the position, with a warning.
    /// </summary>
    [Test]
    public void AClientsCopy_IsInjectedByItsHomeChunk_WhereverItStands()
    {
        GameObject prefabObject = new("NetworkedProbe");
        created.Add(prefabObject);
        NetworkObject prefab = prefabObject.AddComponent<NetworkObject>();
        prefabObject.AddComponent<MeleeDamageDealer>();
        FakeNetworkObjects network = new() { IsActive = true, IsServer = false };
        ChunkNetworkPrefabs prefabs = new(network);
        prefabs.Register(prefab, new ChunkIdentity(world, Vector2Int.zero), ResolverWithClockAt(100f), default);
        prefabs.Register(prefab, new ChunkIdentity(world, Chunk), ResolverWithClockAt(200f), default);
        NetworkPrefabInstanceHandlerWithData<NetworkChunkId> handler = (NetworkPrefabInstanceHandlerWithData<NetworkChunkId>)network.Handlers[prefab];
        NetworkChunkId eastern = new(ChunkId.Of(world, Chunk));
        NetworkChunkId western = new(ChunkId.Of(world, Vector2Int.zero));

        Assert.That(ClockOf(handler.Instantiate(0, LocalToWorld(60.5f, 30.5f), Quaternion.identity, eastern)), Is.EqualTo(200f).Within(0.001f), "A mob of the eastern chunk that walked west still belongs to the east.");
        Assert.That(ClockOf(handler.Instantiate(0, LocalToWorld(70.5f, 30.5f), Quaternion.identity, western)), Is.EqualTo(100f).Within(0.001f));

        NetworkChunkId elsewhere = new(ChunkId.Of(world, new Vector2Int(1, 1)));
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("No loaded chunk registers NetworkedProbe"));
        Assert.That(ClockOf(handler.Instantiate(0, LocalToWorld(70.5f, 30.5f), Quaternion.identity, elsewhere)), Is.EqualTo(200f).Within(0.001f));
        prefabs.Dispose();
    }

    /// <summary>
    /// A spawner of the chunk at <paramref name="coords"/> (the eastern chunk by default) over
    /// <paramref name="spawnPoints"/> in the active scene, whose resolver injects mobs with
    /// <paramref name="navigation"/>; disposed on teardown.
    /// </summary>
    private ChunkMobSpawner CreateSpawner(
        MobSpawnPoint[] spawnPoints,
        FakeNetworkObjects network,
        WorldNavigation navigation,
        Vector2Int? coords = null,
        ChunkNetworkPrefabs prefabs = null)
    {
        ContainerBuilder builder = new();
        builder.RegisterInstance<INavigation2D>(navigation);
        builder.RegisterInstance<IPlayerRegistry>(new PlayerRegistry());
        builder.RegisterInstance<IRandom>(new SystemRandom(1));
        builder.RegisterInstance<IClock>(new ManualClock());
        builder.RegisterInstance(TestCombat.CreateHitService(FixedGameAuthority.Authoritative));
        ChunkMobSpawner spawner = new(
            builder.Build(),
            new ChunkIdentity(world, coords ?? Chunk),
            spawnPoints,
            SceneManager.GetActiveScene(),
            network,
            prefabs ?? new ChunkNetworkPrefabs(network),
            worlds,
            sessionSave.WorldOf(world.WorldId),
            sessionSave);
        spawners.Add(spawner);
        return spawner;
    }

    /// <summary>
    /// A mob stand-in in the scene (a brain and the components it requires, and a <see cref="NetworkObject"/> when
    /// <paramref name="networked"/>) that spawn points use as their prefab, with <paramref name="config"/> when given.
    /// </summary>
    private MobController CreateMobTemplate(bool networked, MobConfig config = null)
    {
        GameObject template = new("MobTemplate");
        created.Add(template);
        template.transform.position = new Vector3(-500f, -500f, 0f);
        template.AddComponent<Rigidbody2D>().gravityScale = 0f;
        if (networked)
        {
            template.AddComponent<NetworkObject>();
        }

        MobController mob = template.AddComponent<MobController>();
        if (config != null)
        {
            created.Add(config);
            SerializedObject serialized = new(mob);
            serialized.FindProperty("config").objectReferenceValue = config;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        return mob;
    }

    /// <summary>
    /// A tracked spawn point named <paramref name="name"/> at <paramref name="position"/> that spawns
    /// <paramref name="prefab"/> (null for none), with the persistent id <paramref name="persistentId"/> when given.
    /// </summary>
    private MobSpawnPoint CreateSpawnPoint(string name, MobController prefab, Vector2 position, string persistentId = null)
    {
        GameObject spawnObject = new(name);
        created.Add(spawnObject);
        spawnObject.transform.position = position;
        MobSpawnPoint spawnPoint = spawnObject.AddComponent<MobSpawnPoint>();
        SerializedObject serialized = new(spawnPoint);
        serialized.FindProperty("mobPrefab").objectReferenceValue = prefab;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        if (persistentId != null)
        {
            SerializedObject id = new(spawnObject.AddComponent<PersistentId>());
            id.FindProperty("id").stringValue = persistentId;
            id.ApplyModifiedPropertiesWithoutUndo();
        }

        return spawnPoint;
    }

    /// <summary>
    /// The world position of the world-local point (<paramref name="x"/>, <paramref name="y"/>).
    /// </summary>
    private Vector2 LocalToWorld(float x, float y)
    {
        return world.ToWorld(new Vector2(x, y));
    }

    /// <summary>
    /// A resolver that injects a clock standing at <paramref name="time"/>.
    /// </summary>
    private static IObjectResolver ResolverWithClockAt(float time)
    {
        ContainerBuilder builder = new();
        builder.RegisterInstance<IClock>(new ManualClock { Time = time });
        builder.RegisterInstance(TestCombat.CreateHitService(FixedGameAuthority.Authoritative));
        return builder.Build();
    }

    /// <summary>
    /// The time of the clock <paramref name="instance"/>'s melee dealer was injected with; destroys the instance on
    /// teardown.
    /// </summary>
    private float ClockOf(NetworkObject instance)
    {
        created.Add(instance.gameObject);
        MeleeDamageDealer dealer = instance.GetComponent<MeleeDamageDealer>();
        dealer.ResetCooldown(readyImmediately: false);
        return dealer.NextAttackTime - dealer.AttackInterval;
    }
}
