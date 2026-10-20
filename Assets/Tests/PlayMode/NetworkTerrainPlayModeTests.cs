using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

/// <summary>
/// PlayMode tests for host-authoritative runtime terrain edits, replicated per chunk to an in-process client.
/// </summary>
public class NetworkTerrainPlayModeTests
{
    private const int SearchRadius = 8;
    private const string WorldGrassName = "GrassTilemap";

    private InProcessClient client;

    /// <summary>
    /// Disposes the in-process client when the test started one.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        client?.Dispose();
        client = null;
    }

    /// <summary>
    /// Hosting Testlands, the host's chunk under its player has an <see cref="AreaTerrain"/>. Wall and ground edits made
    /// by the host update the world's navigation and reach a late joiner as the same edit records, but only once it
    /// reports that chunk ready, carried by a sync object whose home is that chunk; editing a cell again replaces its
    /// record on the client rather than adding one.
    /// </summary>
    [UnityTest]
    public IEnumerator HostTerrainEdits_ReachALateJoiner_OnceItHoldsTheChunk_AndLaterEditsFollow()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        yield return null;

        WorldDefinition world = gameFlow.CurrentWorld;
        SceneBootTestHelper.ResolveFromGameplay<Worlds>().TryGet(world, out LoadedWorld loaded);
        PlayerController hostPlayer = SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current.Controller;
        Vector2Int chunkCoords = ChunkCoords.ChunkOf(ChunkCoords.CellOf(world.ToLocal(hostPlayer.transform.position)));
        Scene chunkScene = loaded.Chunks[chunkCoords];
        AreaTerrain terrain = SceneQuery.FindFirst<AreaTerrain>(chunkScene);
        Assert.That(terrain, Is.Not.Null, "Every chunk has an AreaTerrain.");
        INavigation2D navigation = loaded.Navigation;
        TerrainMovementProfile2D profile = hostPlayer.ControlSettings.MovementProfile;
        Tilemap collision = SceneQuery.FindFirst<DualGridTilemapModule>(chunkScene).DataTilemap;
        Tilemap grass = FindGrass(chunkScene);

        Vector3Int center = FindOpenGround(navigation, profile, chunkScene, grass, navigation.WorldToCell(hostPlayer.transform.position));
        Vector3Int[] wall = { center, center + Vector3Int.up, center + Vector3Int.down };
        Vector3Int hole = center + new Vector3Int(3, 0, 0);

        Assert.That(terrain.TrySetCells(collision, wall, filled: true), Is.True);
        Assert.That(terrain.TrySetCells(grass, new[] { hole }, filled: false), Is.True);
        Assert.That(navigation.IsCellWalkable(center, profile), Is.False, "The host's navigation follows its own wall.");
        Assert.That(navigation.IsCellWalkable(hole, profile), Is.False, "The host's navigation follows the emptied ground cell.");

        client = InProcessClient.Start(session, SceneBootTestHelper.ResolveFromGameplay<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendJoined();
        yield return SceneBootTestHelper.WaitUntil(() => client.LocalPlayer.Current != null, "the joined client to receive its player");
        for (int i = 0; i < 15; i++)
        {
            yield return null;
        }

        Assert.That(FindCopyManagedBy(client.Manager), Is.Null, "No chunk's edits reach a client that holds no chunk.");

        ChunkId chunk = ChunkId.Of(world, chunkCoords);
        client.SendChunkReady(0, chunk);
        NetworkAreaTerrain clientCopy = null;
        yield return SceneBootTestHelper.WaitUntil(
            () => (clientCopy = FindCopyManagedBy(client.Manager)) != null && clientCopy.EditCount == 4,
            "a client holding the chunk to receive the edits made before it joined");
        Assert.That(client.HomeChunks[clientCopy.NetworkObject], Is.EqualTo(chunk), "The sync object arrives with its home chunk.");
        for (int i = 0; i < terrain.Edits.Count; i++)
        {
            Assert.That(clientCopy.GetEdit(i), Is.EqualTo(terrain.Edits[i]));
        }

        Assert.That(terrain.TrySetCells(collision, new[] { center }, filled: false), Is.True);
        yield return SceneBootTestHelper.WaitUntil(() => !clientCopy.GetEdit(0).Filled, "a later edit of the same cell to reach the client");
        Assert.That(clientCopy.EditCount, Is.EqualTo(4), "Editing a cell again replaces its record instead of adding one.");
        Assert.That(navigation.IsCellWalkable(center, profile), Is.True);
    }

    /// <summary>
    /// The chunk's grass layer, found by its name among the ground layers.
    /// </summary>
    private static Tilemap FindGrass(Scene chunkScene)
    {
        foreach (NavigationTerrainSource2D source in SceneQuery.FindAll<NavigationTerrainSource2D>(chunkScene))
        {
            if (source.DataTilemap.name == WorldGrassName)
            {
                return source.DataTilemap;
            }
        }

        Assert.Fail("The chunk has no grass layer.");
        return null;
    }

    /// <summary>
    /// The walkable cell nearest <paramref name="origin"/>, within <see cref="SearchRadius"/>, whose neighbours above and
    /// below are walkable too and whose cell three to the right is walkable ground that only <paramref name="ground"/>
    /// covers among the chunk's layers, so emptying it there leaves no terrain; fails the test when there is none.
    /// </summary>
    private static Vector3Int FindOpenGround(INavigation2D navigation, TerrainMovementProfile2D profile, Scene chunkScene, Tilemap ground, Vector3Int origin)
    {
        NavigationTerrainSource2D[] sources = SceneQuery.FindAll<NavigationTerrainSource2D>(chunkScene);
        bool found = false;
        Vector3Int best = default;
        for (int dy = -SearchRadius; dy <= SearchRadius; dy++)
        {
            for (int dx = -SearchRadius; dx <= SearchRadius; dx++)
            {
                Vector3Int cell = origin + new Vector3Int(dx, dy, 0);
                Vector3Int hole = cell + new Vector3Int(3, 0, 0);
                bool open = cell != origin && hole != origin
                    && navigation.IsCellWalkable(cell, profile) && navigation.IsCellWalkable(cell + Vector3Int.up, profile)
                    && navigation.IsCellWalkable(cell + Vector3Int.down, profile) && navigation.IsCellWalkable(hole, profile) && ground.HasTile(hole);
                foreach (NavigationTerrainSource2D source in sources)
                {
                    open &= source.DataTilemap == ground || !source.DataTilemap.HasTile(hole);
                }

                if (open && (!found || (cell - origin).sqrMagnitude < (best - origin).sqrMagnitude))
                {
                    best = cell;
                    found = true;
                }
            }
        }

        Assert.That(found, Is.True, "There should be open grass to edit near the entry.");
        return best;
    }

    /// <summary>
    /// The spawned terrain sync object that belongs to <paramref name="manager"/> (host or client side), or null.
    /// </summary>
    private static NetworkAreaTerrain FindCopyManagedBy(NetworkManager manager)
    {
        foreach (NetworkAreaTerrain copy in Object.FindObjectsByType<NetworkAreaTerrain>())
        {
            if (copy.NetworkManager == manager && copy.IsSpawned)
            {
                return copy;
            }
        }

        return null;
    }
}
