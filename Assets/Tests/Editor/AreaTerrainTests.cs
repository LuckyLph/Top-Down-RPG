using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.TestTools;
using VContainer;

public class AreaTerrainTests
{
    private const string SyncPrefabPath = "Assets/Prefabs/Network/AreaTerrainSync.prefab";

    private readonly List<Object> createdObjects = new();
    private GameObject root;
    private Tile wallTile;
    private Tilemap walls;
    private DualGridTilemapModule ground;
    private DualGridRuleTile groundTile;

    [SetUp]
    public void SetUp()
    {
        root = Track(new GameObject("AreaTerrainRoot", typeof(Grid)));
        wallTile = Track(ScriptableObject.CreateInstance<Tile>());

        GameObject wallObject = new("Walls", typeof(Tilemap));
        wallObject.transform.SetParent(root.transform);
        walls = wallObject.GetComponent<Tilemap>();

        GameObject groundObject = new("Ground", typeof(Tilemap));
        groundObject.transform.SetParent(root.transform);
        GameObject renderObject = new("GroundRender", typeof(Tilemap));
        renderObject.transform.SetParent(groundObject.transform);
        groundTile = Track(ScriptableObject.CreateInstance<DualGridRuleTile>());
        Track(groundTile.RefreshDataTile());
        ground = groundObject.AddComponent<DualGridTilemapModule>();
        ground.RenderTile = groundTile;
    }

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

    [Test]
    public void TrySetCells_WithAuthority_ChangesTheTiles_AndRecordsTheLastWritePerCell()
    {
        AreaTerrain terrain = CreateTerrain(FixedGameAuthority.Authoritative);
        List<int> recorded = new();
        terrain.EditRecorded += recorded.Add;
        Vector3Int[] cells = { new(0, 0, 0), new(1, 0, 0) };

        Assert.That(terrain.TrySetCells(walls, cells, filled: true), Is.True);
        Assert.That(walls.GetTile(cells[0]), Is.SameAs(wallTile));
        Assert.That(walls.GetTile(cells[1]), Is.SameAs(wallTile));

        Assert.That(terrain.TrySetCells(walls, new[] { cells[0] }, filled: false), Is.True);
        Assert.That(walls.HasTile(cells[0]), Is.False);

        Assert.That(terrain.Edits, Is.EqualTo(new[] { new TerrainEdit(1, cells[0], false), new TerrainEdit(1, cells[1], true) }));
        Assert.That(recorded, Is.EqualTo(new[] { 0, 1, 0 }), "Editing a cell again replaces its record in place.");
    }

    [Test]
    public void TrySetCells_OnADualGridLayer_PaintsThroughItsModule()
    {
        AreaTerrain terrain = CreateTerrain(FixedGameAuthority.Authoritative);

        Assert.That(terrain.TrySetCells(ground.DataTilemap, new[] { Vector3Int.zero }, filled: true), Is.True);

        Assert.That(ground.DataTilemap.GetTile(Vector3Int.zero), Is.SameAs(groundTile.DataTile));
        Assert.That(ground.RenderTilemap.HasTile(new Vector3Int(1, 1, 0)), Is.True);
        Assert.That(terrain.Edits, Is.EqualTo(new[] { new TerrainEdit(0, Vector3Int.zero, true) }));
    }

    [Test]
    public void TrySetCells_WithoutAuthority_ChangesAndRecordsNothing()
    {
        AreaTerrain terrain = CreateTerrain(new FixedGameAuthority(false));

        Assert.That(terrain.TrySetCells(walls, new[] { Vector3Int.zero }, filled: true), Is.False);

        Assert.That(walls.HasTile(Vector3Int.zero), Is.False);
        Assert.That(terrain.Edits, Is.Empty);
    }

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

    [Test]
    public void ApplyReplicated_AppliesEveryLayer_WithoutRecording()
    {
        AreaTerrain terrain = CreateTerrain(new FixedGameAuthority(false));
        walls.SetTile(new Vector3Int(5, 5, 0), wallTile);

        terrain.ApplyReplicated(new[]
        {
            new TerrainEdit(0, new Vector3Int(2, 2, 0), true),
            new TerrainEdit(1, new Vector3Int(4, 4, 0), true),
            new TerrainEdit(1, new Vector3Int(5, 5, 0), false),
            new TerrainEdit(7, new Vector3Int(9, 9, 0), true)
        });

        Assert.That(ground.DataTilemap.HasTile(new Vector3Int(2, 2, 0)), Is.True);
        Assert.That(walls.GetTile(new Vector3Int(4, 4, 0)), Is.SameAs(wallTile));
        Assert.That(walls.HasTile(new Vector3Int(5, 5, 0)), Is.False);
        Assert.That(terrain.Edits, Is.Empty, "Replicated edits are the host's; clients do not record them.");
    }

    [Test]
    public void Sync_OnTheHost_SpawnsTheSyncObjectIntoTheArea()
    {
        AreaTerrain terrain = CreateTerrain(FixedGameAuthority.Authoritative, LoadSyncPrefab());
        FakeNetworkObjects network = new() { IsActive = true, IsServer = true };
        AreaTerrainSync sync = new(BuildResolver(terrain), terrain, SceneManager.GetActiveScene(), network);

        sync.RegisterNetworkPrefab();
        sync.Start();

        Assert.That(network.Registered, Is.Empty);
        Assert.That(network.Spawned.Count, Is.EqualTo(1));
        Track(network.Spawned[0].gameObject);
        Assert.That(network.Spawned[0].GetComponent<NetworkAreaTerrain>(), Is.Not.Null);
    }

    [Test]
    public void Sync_OnAClient_RegistersThePrefabOnce_SpawnsNothing_AndUnregistersOnDispose()
    {
        NetworkObject prefab = LoadSyncPrefab();
        AreaTerrain terrain = CreateTerrain(new FixedGameAuthority(false), prefab);
        FakeNetworkObjects network = new() { IsActive = true, IsServer = false };
        AreaTerrainSync sync = new(BuildResolver(terrain), terrain, SceneManager.GetActiveScene(), network);

        sync.RegisterNetworkPrefab();
        sync.RegisterNetworkPrefab();
        sync.Start();

        Assert.That(network.Registered, Is.EqualTo(new[] { prefab }));
        Assert.That(network.Spawned, Is.Empty);

        sync.Dispose();
        Assert.That(network.Registered, Is.Empty);
    }

    [Test]
    public void Sync_Offline_DoesNothing()
    {
        AreaTerrain terrain = CreateTerrain(FixedGameAuthority.Authoritative, LoadSyncPrefab());
        FakeNetworkObjects network = new();
        AreaTerrainSync sync = new(BuildResolver(terrain), terrain, SceneManager.GetActiveScene(), network);

        sync.RegisterNetworkPrefab();
        sync.Start();

        Assert.That(network.Registered, Is.Empty);
        Assert.That(network.Spawned, Is.Empty);
    }

    private AreaTerrain CreateTerrain(IGameAuthority authority, NetworkObject syncPrefab = null)
    {
        GameObject terrainObject = new("Terrain");
        terrainObject.transform.SetParent(root.transform);
        AreaTerrain terrain = terrainObject.AddComponent<AreaTerrain>();
        terrain.Configure(new AreaTerrain.Layer(ground.DataTilemap, null), new AreaTerrain.Layer(walls, wallTile));
        terrain.Construct(authority);
        if (syncPrefab != null)
        {
            SerializedObject serialized = new(terrain);
            serialized.FindProperty("syncPrefab").objectReferenceValue = syncPrefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        return terrain;
    }

    private static NetworkObject LoadSyncPrefab()
    {
        NetworkObject prefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(SyncPrefabPath);
        Assert.That(prefab, Is.Not.Null);
        return prefab;
    }

    private static IObjectResolver BuildResolver(AreaTerrain terrain)
    {
        ContainerBuilder builder = new();
        builder.RegisterInstance(terrain);
        return builder.Build();
    }

    private T Track<T>(T created) where T : Object
    {
        createdObjects.Add(created);
        return created;
    }
}
