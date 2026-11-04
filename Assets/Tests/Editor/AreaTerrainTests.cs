using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// Tests for runtime terrain edits through <see cref="AreaTerrain"/> and for <see cref="ChunkTerrainSync"/>, which
/// spawns or registers the network object that replicates a chunk's edits, and restores a chunk's persisted edits from
/// its world's state and records new ones there, offline and on the host.
/// </summary>
public class AreaTerrainTests
{
    private const string SyncPrefabPath = "Assets/Prefabs/Network/AreaTerrainSync.prefab";

    private readonly List<Object> createdObjects = new();
    private GameObject root;
    private Tile groundTile;
    private Tilemap ground;
    private DualGridTilemapModule walls;
    private DualGridRuleTile wallTile;

    /// <summary>
    /// Builds a grid laid out like an area's: a plain ground data tilemap filled with a fresh plain tile, and dual-grid
    /// walls (data and render tilemaps) of a fresh <see cref="DualGridRuleTile"/>.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        root = Track(new GameObject("AreaTerrainRoot", typeof(Grid)));
        groundTile = Track(ScriptableObject.CreateInstance<Tile>());

        GameObject groundObject = new("Ground", typeof(Tilemap));
        groundObject.transform.SetParent(root.transform);
        ground = groundObject.GetComponent<Tilemap>();

        GameObject wallObject = new("Walls", typeof(Tilemap));
        wallObject.transform.SetParent(root.transform);
        GameObject renderObject = new("WallsRender", typeof(Tilemap));
        renderObject.transform.SetParent(wallObject.transform);
        wallTile = Track(ScriptableObject.CreateInstance<DualGridRuleTile>());
        Track(wallTile.RefreshDataTile());
        walls = wallObject.AddComponent<DualGridTilemapModule>();
        walls.RenderTile = wallTile;
    }

    /// <summary>
    /// Destroys the grid hierarchy, tiles and spawned objects the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Object created in createdObjects)
        {
            if (created != null)
            {
                Object.DestroyImmediate(created);
            }
        }

        createdObjects.Clear();
    }

    /// <summary>
    /// On the authority, setting cells of a plain ground layer fills them with its fill tile or clears them, and keeps
    /// one edit record per cell, replacing a re-edited cell's record in place and raising
    /// <see cref="AreaTerrain.EditRecorded"/> with its index.
    /// </summary>
    [Test]
    public void TrySetCells_WithAuthority_ChangesTheTiles_AndRecordsTheLastWritePerCell()
    {
        AreaTerrain terrain = CreateTerrain(FixedGameAuthority.Authoritative);
        List<int> recorded = new();
        terrain.EditRecorded += recorded.Add;
        Vector3Int[] cells = { new(0, 0, 0), new(1, 0, 0) };

        Assert.That(terrain.TrySetCells(ground, cells, filled: true), Is.True);
        Assert.That(ground.GetTile(cells[0]), Is.SameAs(groundTile));
        Assert.That(ground.GetTile(cells[1]), Is.SameAs(groundTile));

        Assert.That(terrain.TrySetCells(ground, new[] { cells[0] }, filled: false), Is.True);
        Assert.That(ground.HasTile(cells[0]), Is.False);

        Assert.That(terrain.Edits, Is.EqualTo(new[] { new TerrainEdit(0, cells[0], false), new TerrainEdit(0, cells[1], true) }));
        Assert.That(recorded, Is.EqualTo(new[] { 0, 1, 0 }), "Editing a cell again replaces its record in place.");
    }

    /// <summary>
    /// Filling a cell of the dual-grid walls paints the module's data tile, refreshes its render tilemap and records
    /// the edit.
    /// </summary>
    [Test]
    public void TrySetCells_OnADualGridLayer_PaintsThroughItsModule()
    {
        AreaTerrain terrain = CreateTerrain(FixedGameAuthority.Authoritative);

        Assert.That(terrain.TrySetCells(walls.DataTilemap, new[] { Vector3Int.zero }, filled: true), Is.True);

        Assert.That(walls.DataTilemap.GetTile(Vector3Int.zero), Is.SameAs(wallTile.DataTile));
        Assert.That(walls.RenderTilemap.HasTile(new Vector3Int(1, 1, 0)), Is.True);
        Assert.That(terrain.Edits, Is.EqualTo(new[] { new TerrainEdit(1, Vector3Int.zero, true) }));
    }

    /// <summary>
    /// Without authority, setting cells fails and neither changes tiles nor records edits.
    /// </summary>
    [Test]
    public void TrySetCells_WithoutAuthority_ChangesAndRecordsNothing()
    {
        AreaTerrain terrain = CreateTerrain(new FixedGameAuthority(false));

        Assert.That(terrain.TrySetCells(ground, new[] { Vector3Int.zero }, filled: true), Is.False);

        Assert.That(ground.HasTile(Vector3Int.zero), Is.False);
        Assert.That(terrain.Edits, Is.Empty);
    }

    /// <summary>
    /// Setting cells on a tilemap that is not one of the terrain's layers logs an error, fails and records nothing.
    /// </summary>
    [Test]
    public void TrySetCells_OnATilemapThatIsNotALayer_LogsAnErrorAndFails()
    {
        AreaTerrain terrain = CreateTerrain(FixedGameAuthority.Authoritative);
        Tilemap other = new GameObject("Other", typeof(Tilemap)).GetComponent<Tilemap>();
        other.transform.SetParent(root.transform);

        LogAssert.Expect(LogType.Error, "Other is not one of Terrain's editable terrain layers.");
        Assert.That(terrain.TrySetCells(other, new[] { Vector3Int.zero }, filled: true), Is.False);
        Assert.That(terrain.Edits, Is.Empty);
    }

    /// <summary>
    /// On a client, replicated edits fill a plain ground layer's cells with its fill tile and clear them, paint the
    /// dual-grid walls through their module, skip edits for a layer the area does not have, and are not recorded.
    /// </summary>
    [Test]
    public void ApplyReplicated_AppliesEveryLayer_WithoutRecording()
    {
        AreaTerrain terrain = CreateTerrain(new FixedGameAuthority(false));
        ground.SetTile(new Vector3Int(5, 5, 0), groundTile);

        terrain.ApplyReplicated(new[]
        {
            new TerrainEdit(0, new Vector3Int(4, 4, 0), true),
            new TerrainEdit(0, new Vector3Int(5, 5, 0), false),
            new TerrainEdit(1, new Vector3Int(2, 2, 0), true),
            new TerrainEdit(7, new Vector3Int(9, 9, 0), true)
        });

        Assert.That(ground.GetTile(new Vector3Int(4, 4, 0)), Is.SameAs(groundTile));
        Assert.That(ground.HasTile(new Vector3Int(5, 5, 0)), Is.False);
        Assert.That(walls.DataTilemap.GetTile(new Vector3Int(2, 2, 0)), Is.SameAs(wallTile.DataTile));
        Assert.That(walls.RenderTilemap.HasTile(new Vector3Int(3, 3, 0)), Is.True, "The walls' render tilemap follows.");
        Assert.That(terrain.Edits, Is.Empty, "Replicated edits are the host's; clients do not record them.");
    }

    /// <summary>
    /// The editor's dev toggle fills an empty cell of a plain ground layer with its fill tile and empties a filled one,
    /// recording both through the area terrain on the authority, and is refused on a client.
    /// </summary>
    [Test]
    public void DevToggle_FillsAnEmptyCellAndEmptiesAFilledOne_ThroughTheAreaTerrain()
    {
        AreaTerrain terrain = CreateTerrain(FixedGameAuthority.Authoritative);
        Vector3Int cell = new(3, 2, 0);

        Assert.That(AreaTerrainDevMenu.Toggle(terrain, ground, cell), Is.True);
        Assert.That(ground.GetTile(cell), Is.SameAs(groundTile));
        Assert.That(AreaTerrainDevMenu.Toggle(terrain, ground, cell), Is.True);
        Assert.That(ground.HasTile(cell), Is.False);
        Assert.That(terrain.Edits, Is.EqualTo(new[] { new TerrainEdit(0, cell, false) }));

        AreaTerrain client = CreateTerrain(new FixedGameAuthority(false));
        Assert.That(AreaTerrainDevMenu.Toggle(client, ground, cell), Is.False);
        Assert.That(ground.HasTile(cell), Is.False);
    }

    /// <summary>
    /// The dev menu edits the cell under the view's centre at z = 0, the plane the terrain renderer and navigation read,
    /// even from a camera's position off that plane on a grid whose cells have depth.
    /// </summary>
    [Test]
    public void DevMenuCell_IsTheCellUnderThePoint_AtZeroZ()
    {
        root.GetComponent<Grid>().cellSize = Vector3.one;

        Assert.That(AreaTerrainDevMenu.CellAt(ground, new Vector3(2.5f, -3.5f, -10f)), Is.EqualTo(new Vector3Int(2, -4, 0)));
    }

    /// <summary>
    /// On the host a chunk's sync registers its prefab for the chunk (so its spawn carries the home chunk), spawns one
    /// <see cref="NetworkAreaTerrain"/> object of that chunk, and despawns it when its own chunk starts unloading, not
    /// when another one does.
    /// </summary>
    [Test]
    public void ChunkSync_OnTheHost_SpawnsTheSyncObjectAsAnObjectOfItsChunk_AndDespawnsItWithTheChunk()
    {
        NetworkObject prefab = LoadSyncPrefab();
        AreaTerrain terrain = CreateTerrain(FixedGameAuthority.Authoritative, prefab);
        FakeNetworkObjects network = new() { IsActive = true, IsServer = true };
        WorldDefinition world = Track(WorldDefinition.Create("TerrainSync", new Vector2Int(2048, 0), new Vector2Int(2, 1), new[] { Vector2Int.zero, Vector2Int.right }));
        Worlds worlds = new();
        LoadedWorld loaded = new(world, default, null, null);
        worlds.Add(loaded);
        worlds.AddChunk(loaded, Vector2Int.zero, default);
        worlds.AddChunk(loaded, Vector2Int.right, default);
        WorldState state = new(world.WorldId);
        state.Chunk(Vector2Int.right).RecordTerrainEdit(terrain.KeyOf(0), new Vector2Int(70, 3), true);
        ChunkTerrainSync sync = CreateChunkSync(terrain, network, world, Vector2Int.right, worlds, state: state);

        sync.RegisterNetworkPrefab();
        sync.Start();

        Assert.That(terrain.Edits, Is.EqualTo(new[] { new TerrainEdit(0, new Vector3Int(70, 3, 0), true) }), "The persisted edit is the terrain's before its sync object spawns, so it replicates.");
        Assert.That(ground.GetTile(new Vector3Int(70, 3, 0)), Is.SameAs(groundTile));
        Assert.That(network.Registered, Is.EqualTo(new[] { prefab }), "The host registers the prefab so its spawn carries the home chunk.");
        Assert.That(network.Spawned.Count, Is.EqualTo(1));
        NetworkObject spawned = network.Spawned[0];
        Track(spawned.gameObject);
        Assert.That(spawned.GetComponent<NetworkAreaTerrain>(), Is.Not.Null);
        Assert.That(network.HomeChunks[spawned], Is.EqualTo(ChunkId.Of(world, Vector2Int.right)));

        worlds.RemoveChunk(loaded, Vector2Int.zero);
        Assert.That(network.Despawned, Is.Empty, "Another chunk unloading leaves the sync object alone.");
        worlds.RemoveChunk(loaded, Vector2Int.right);
        Assert.That(network.Despawned, Is.EqualTo(new[] { spawned }), "The sync object goes before its chunk unloads.");

        sync.Dispose();
        Assert.That(network.Despawned.Count, Is.EqualTo(1), "Nothing is despawned twice.");
        Assert.That(network.Registered, Is.Empty);
    }

    /// <summary>
    /// On a client a chunk's sync registers its prefab for the chunk once however often asked, spawns nothing, and
    /// unregisters the prefab when disposed.
    /// </summary>
    [Test]
    public void ChunkSync_OnAClient_RegistersThePrefabOnce_SpawnsNothing_AndUnregistersOnDispose()
    {
        NetworkObject prefab = LoadSyncPrefab();
        AreaTerrain terrain = CreateTerrain(new FixedGameAuthority(false), prefab);
        FakeNetworkObjects network = new() { IsActive = true, IsServer = false };
        WorldDefinition world = Track(WorldDefinition.Create("TerrainSync", new Vector2Int(2048, 0), new Vector2Int(1, 1), new[] { Vector2Int.zero }));
        ChunkNetworkPrefabs prefabs = new(network);
        WorldState state = new(world.WorldId);
        state.Chunk(Vector2Int.zero).RecordTerrainEdit(terrain.KeyOf(0), new Vector2Int(1, 1), true);
        ChunkTerrainSync sync = CreateChunkSync(terrain, network, world, Vector2Int.zero, new Worlds(), prefabs, state);

        sync.RegisterNetworkPrefab();
        sync.RegisterNetworkPrefab();
        sync.Start();

        Assert.That(network.Registered, Is.EqualTo(new[] { prefab }));
        Assert.That(prefabs.IsRegistered(prefab), Is.True);
        Assert.That(network.Spawned, Is.Empty);
        Assert.That(terrain.Edits, Is.Empty, "A client restores nothing: the host's edits reach it through the sync object.");

        sync.Dispose();
        Assert.That(network.Registered, Is.Empty);
    }

    /// <summary>
    /// Offline a chunk's sync restores the chunk's persisted edits from its world's state (a filled ground cell, an
    /// emptied one and a wall), skipping a layer the terrain does not have with a warning, and records every new edit in
    /// the chunk's record, until disposed.
    /// </summary>
    [Test]
    public void ChunkSync_Offline_RestoresThePersistedEdits_AndRecordsNewOnes()
    {
        AreaTerrain terrain = CreateTerrain(FixedGameAuthority.Authoritative);
        ground.SetTile(new Vector3Int(4, 4, 0), groundTile);
        WorldDefinition world = Track(WorldDefinition.Create("TerrainSync", new Vector2Int(2048, 0), new Vector2Int(1, 1), new[] { Vector2Int.zero }));
        WorldState state = new(world.WorldId);
        ChunkState chunk = state.Chunk(Vector2Int.zero);
        chunk.RecordTerrainEdit(terrain.KeyOf(0), new Vector2Int(1, 1), true);
        chunk.RecordTerrainEdit(terrain.KeyOf(0), new Vector2Int(4, 4), false);
        chunk.RecordTerrainEdit(AreaTerrain.WallsKey, new Vector2Int(2, 2), true);
        chunk.RecordTerrainEdit("a-material-gone", new Vector2Int(9, 9), true);
        ChunkTerrainSync sync = CreateChunkSync(terrain, new FakeNetworkObjects(), world, Vector2Int.zero, new Worlds(), state: state);

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("layers it does not have"));
        sync.Start();

        Assert.That(ground.GetTile(new Vector3Int(1, 1, 0)), Is.SameAs(groundTile));
        Assert.That(ground.GetTile(new Vector3Int(4, 4, 0)), Is.Null);
        Assert.That(walls.DataTilemap.HasTile(new Vector3Int(2, 2, 0)), Is.True);
        Assert.That(terrain.Edits.Count, Is.EqualTo(3), "The restored edits are the terrain's own, as if just made.");

        terrain.TrySetCells(ground, new[] { new Vector3Int(6, 1, 0) }, true);
        Assert.That(chunk.TerrainEdits, Does.Contain(new GameSaveData.CellEdit(terrain.KeyOf(0), new Vector2Int(6, 1), true)), "A new edit is recorded in the chunk's record.");

        sync.Dispose();
        int recorded = chunk.TerrainEdits.Count;
        terrain.TrySetCells(ground, new[] { new Vector3Int(7, 1, 0) }, true);
        Assert.That(chunk.TerrainEdits.Count, Is.EqualTo(recorded), "Nothing is recorded once the chunk's scope is gone.");
    }

    /// <summary>
    /// Saves name a layer by what it draws: a ground layer by the id of the material the grid's renderer draws on its
    /// tilemap, the walls as walls, a tilemap the renderer does not draw by its name; an unknown name finds no layer.
    /// </summary>
    [Test]
    public void KeyOf_NamesALayerByItsMaterial_TheWallsAsWalls_AndAnyOtherByItsTilemap()
    {
        TerrainMaterial grass = Track(TerrainMaterial.Create("Grass", null, null, 1, TerrainMaterial.CellMode.None, Color.green, default));
        AreaTerrain terrain = CreateDrawnTerrain(out Tilemap grassLayer, out Tilemap sandLayer, grass, null);

        Assert.That(terrain.KeyOf(0), Is.EqualTo(grass.MaterialId));
        Assert.That(grass.MaterialId, Is.EqualTo("test-Grass"));
        Assert.That(terrain.KeyOf(1), Is.EqualTo(AreaTerrain.TilemapKeyPrefix + sandLayer.name), "A tilemap with no material drawn is named by its tilemap.");
        Assert.That(terrain.KeyOf(2), Is.EqualTo(AreaTerrain.WallsKey));
        Assert.That(terrain.KeyOf(3), Is.Empty);
        Assert.That(terrain.IndexOfKey(grass.MaterialId), Is.EqualTo(0));
        Assert.That(terrain.IndexOfKey(AreaTerrain.WallsKey), Is.EqualTo(2));
        Assert.That(terrain.IndexOfKey("no-such-material"), Is.EqualTo(-1));
        Assert.That(grassLayer, Is.Not.Null);
    }

    /// <summary>
    /// Edits saved from a chunk whose layers were grass, sand and the walls are restored onto the same materials' layers
    /// after the chunk's layers were reordered (as the writer's pruning or a new layer order does), each on its own
    /// tilemap, the walls' too.
    /// </summary>
    [Test]
    public void SavedEdits_AreKept_AcrossALayerReorder()
    {
        TerrainMaterial grass = Track(TerrainMaterial.Create("Grass", null, null, 1, TerrainMaterial.CellMode.None, Color.green, default));
        TerrainMaterial sand = Track(TerrainMaterial.Create("Sand", null, null, 1, TerrainMaterial.CellMode.None, Color.yellow, default));
        AreaTerrain before = CreateDrawnTerrain(out Tilemap grassBefore, out Tilemap sandBefore, grass, sand);
        WorldDefinition world = Track(WorldDefinition.Create("TerrainReorder", new Vector2Int(2048, 0), new Vector2Int(1, 1), new[] { Vector2Int.zero }));
        WorldState state = new(world.WorldId);
        ChunkTerrainSync recording = CreateChunkSync(before, new FakeNetworkObjects(), world, Vector2Int.zero, new Worlds(), state: state);
        recording.Start();
        before.TrySetCells(grassBefore, new[] { new Vector3Int(3, 3, 0) }, true);
        before.TrySetCells(sandBefore, new[] { new Vector3Int(5, 5, 0) }, true);
        before.TrySetCells(walls.DataTilemap, new[] { new Vector3Int(7, 7, 0) }, true);
        recording.Dispose();

        AreaTerrain after = CreateDrawnTerrain(out Tilemap grassAfter, out Tilemap sandAfter, grass, sand, reversed: true);
        Assert.That(after.KeyOf(0), Is.EqualTo(AreaTerrain.WallsKey), "The reordered terrain lists the walls first.");
        Assert.That(after.KeyOf(1), Is.EqualTo(sand.MaterialId));
        Assert.That(after.KeyOf(2), Is.EqualTo(grass.MaterialId));
        walls.ClearDataTiles(new[] { new Vector3Int(7, 7, 0) });
        ChunkTerrainSync restoring = CreateChunkSync(after, new FakeNetworkObjects(), world, Vector2Int.zero, new Worlds(), state: state);
        restoring.Start();

        Assert.That(grassAfter.HasTile(new Vector3Int(3, 3, 0)), Is.True, "The grass edit lands on the grass layer, now third.");
        Assert.That(grassAfter.HasTile(new Vector3Int(5, 5, 0)), Is.False);
        Assert.That(sandAfter.HasTile(new Vector3Int(5, 5, 0)), Is.True, "The sand edit lands on the sand layer, now second.");
        Assert.That(sandAfter.HasTile(new Vector3Int(3, 3, 0)), Is.False);
        Assert.That(walls.DataTilemap.HasTile(new Vector3Int(7, 7, 0)), Is.True, "The wall edit lands on the walls, now first.");
        restoring.Dispose();
    }

    /// <summary>
    /// With no active network session a chunk's sync neither registers nor spawns anything.
    /// </summary>
    [Test]
    public void ChunkSync_Offline_RegistersAndSpawnsNothing()
    {
        AreaTerrain terrain = CreateTerrain(FixedGameAuthority.Authoritative, LoadSyncPrefab());
        FakeNetworkObjects network = new();
        WorldDefinition world = Track(WorldDefinition.Create("TerrainSync", new Vector2Int(2048, 0), new Vector2Int(1, 1), new[] { Vector2Int.zero }));
        ChunkTerrainSync sync = CreateChunkSync(terrain, network, world, Vector2Int.zero, new Worlds());

        sync.RegisterNetworkPrefab();
        sync.Start();
        sync.Dispose();

        Assert.That(network.Registered, Is.Empty);
        Assert.That(network.Spawned, Is.Empty);
    }

    /// <summary>
    /// A <see cref="ChunkTerrainSync"/> of <paramref name="terrain"/> for the chunk at <paramref name="coords"/> of
    /// <paramref name="world"/>, over <paramref name="network"/> and <paramref name="worlds"/>, keeping its edits in
    /// <paramref name="state"/> (an empty world state when null).
    /// </summary>
    private ChunkTerrainSync CreateChunkSync(AreaTerrain terrain, FakeNetworkObjects network, WorldDefinition world, Vector2Int coords, Worlds worlds, ChunkNetworkPrefabs prefabs = null, WorldState state = null)
    {
        return new ChunkTerrainSync(
            BuildResolver(terrain),
            new ChunkIdentity(world, coords),
            terrain,
            SceneManager.GetActiveScene(),
            network,
            prefabs ?? new ChunkNetworkPrefabs(network),
            worlds,
            state ?? new WorldState(world.WorldId));
    }

    /// <summary>
    /// Creates an <see cref="AreaTerrain"/> under the grid with the plain ground as layer 0, filled with its plain tile,
    /// and the dual-grid walls as layer 1, as in an area, injected with <paramref name="authority"/> and, when given,
    /// using <paramref name="syncPrefab"/>.
    /// </summary>
    private AreaTerrain CreateTerrain(IGameAuthority authority, NetworkObject syncPrefab = null)
    {
        GameObject terrainObject = new("Terrain");
        terrainObject.transform.SetParent(root.transform);
        AreaTerrain terrain = terrainObject.AddComponent<AreaTerrain>();
        terrain.Configure(new AreaTerrain.Layer(ground, groundTile), new AreaTerrain.Layer(walls.DataTilemap, null));
        terrain.Construct(authority);
        if (syncPrefab != null)
        {
            SerializedObject serialized = new(terrain);
            serialized.FindProperty("syncPrefab").objectReferenceValue = syncPrefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        return terrain;
    }

    /// <summary>
    /// Creates, on an inactive grid of its own (so its renderer builds nothing), an <see cref="AreaTerrain"/> listing a
    /// grass layer, a sand layer and the test's walls (the walls first and the two layers swapped when
    /// <paramref name="reversed"/>), its renderer drawing <paramref name="grass"/> and <paramref name="sand"/> (none on
    /// the sand layer when null) on them, filled with its own tiles, on the authority.
    /// </summary>
    private AreaTerrain CreateDrawnTerrain(out Tilemap grassLayer, out Tilemap sandLayer, TerrainMaterial grass, TerrainMaterial sand, bool reversed = false)
    {
        GameObject gridObject = Track(new GameObject("DrawnTerrain", typeof(Grid)));
        gridObject.SetActive(false);
        grassLayer = new GameObject("GrassTilemap", typeof(Tilemap)).GetComponent<Tilemap>();
        grassLayer.transform.SetParent(gridObject.transform);
        sandLayer = new GameObject("SandTilemap", typeof(Tilemap)).GetComponent<Tilemap>();
        sandLayer.transform.SetParent(gridObject.transform);
        TerrainBlendRenderer renderer = gridObject.AddComponent<TerrainBlendRenderer>();
        List<TerrainBlendRenderer.Layer> drawn = new() { new TerrainBlendRenderer.Layer(grassLayer, grass) };
        if (sand != null)
        {
            drawn.Add(new TerrainBlendRenderer.Layer(sandLayer, sand));
        }

        if (reversed)
        {
            drawn.Reverse();
        }

        renderer.Configure(null, drawn.ToArray());
        AreaTerrain terrain = gridObject.AddComponent<AreaTerrain>();
        AreaTerrain.Layer grassEntry = new(grassLayer, Track(ScriptableObject.CreateInstance<Tile>()));
        AreaTerrain.Layer sandEntry = new(sandLayer, Track(ScriptableObject.CreateInstance<Tile>()));
        AreaTerrain.Layer wallsEntry = new(walls.DataTilemap, null);
        terrain.Configure(reversed ? new[] { wallsEntry, sandEntry, grassEntry } : new[] { grassEntry, sandEntry, wallsEntry });
        terrain.Construct(FixedGameAuthority.Authoritative);
        return terrain;
    }

    /// <summary>
    /// Loads the project's terrain sync network prefab and asserts it exists.
    /// </summary>
    private static NetworkObject LoadSyncPrefab()
    {
        NetworkObject prefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(SyncPrefabPath);
        Assert.That(prefab, Is.Not.Null);
        return prefab;
    }

    /// <summary>
    /// Builds a container holding only <paramref name="terrain"/>, for the sync to inject spawned objects from.
    /// </summary>
    private static IObjectResolver BuildResolver(AreaTerrain terrain)
    {
        ContainerBuilder builder = new();
        builder.RegisterInstance(terrain);
        return builder.Build();
    }

    /// <summary>
    /// Records <paramref name="created"/> for destruction in <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T created) where T : Object
    {
        createdObjects.Add(created);
        return created;
    }
}
