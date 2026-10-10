using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using VContainer;

/// <summary>
/// PlayMode tests for host-authoritative runtime terrain edits replicated to an in-process client.
/// </summary>
public class NetworkTerrainPlayModeTests
{
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
    /// Wall and ground edits made by the host update its navigation grid and reach a late joiner as the same edit
    /// records; editing a cell again replaces its record on the client rather than adding one.
    /// </summary>
    [UnityTest]
    public IEnumerator HostTerrainEdits_ReachALateJoiner_AndLaterEditsFollow()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        yield return null;

        AreaTerrain terrain = Object.FindAnyObjectByType<AreaTerrain>();
        NavigationGrid2D grid = Object.FindAnyObjectByType<NavigationGrid2D>();
        Assert.That(terrain, Is.Not.Null, "The Clearing should have an AreaTerrain.");
        Tilemap collision = grid.BlockingTilemaps[0];
        Tilemap grass = grid.GetComponentInChildren<NavigationTerrainSource2D>().DataTilemap;

        Vector3Int center = FindOpenGround(grid, grass);
        Vector3Int[] wall = { center, center + Vector3Int.up, center + Vector3Int.down };
        Vector3Int hole = center + new Vector3Int(3, 0, 0);

        Assert.That(terrain.TrySetCells(collision, wall, filled: true), Is.True);
        Assert.That(terrain.TrySetCells(grass, new[] { hole }, filled: false), Is.True);
        Assert.That(grid.IsCellWalkable(center), Is.False, "The host's grid follows its own wall.");
        Assert.That(grid.IsCellWalkable(hole), Is.False, "The host's grid follows the emptied ground cell.");

        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        client = InProcessClient.Start(session, gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendReady();

        NetworkAreaTerrain clientCopy = null;
        yield return SceneBootTestHelper.WaitUntil(
            () => (clientCopy = FindCopyManagedBy(client.Manager)) != null && clientCopy.EditCount == 4,
            "a late joiner to receive the edits made before it joined");
        for (int i = 0; i < terrain.Edits.Count; i++)
        {
            Assert.That(clientCopy.GetEdit(i), Is.EqualTo(terrain.Edits[i]));
        }

        Assert.That(terrain.TrySetCells(collision, new[] { center }, filled: false), Is.True);
        yield return SceneBootTestHelper.WaitUntil(() => !clientCopy.GetEdit(0).Filled, "a later edit of the same cell to reach the client");
        Assert.That(clientCopy.EditCount, Is.EqualTo(4), "Editing a cell again replaces its record instead of adding one.");
        Assert.That(grid.IsCellWalkable(center), Is.True);
    }

    /// <summary>
    /// The walkable cell nearest the middle of <paramref name="grid"/> whose neighbours above and below are walkable
    /// too and whose cell three to the right is walkable ground that only <paramref name="ground"/> covers, so
    /// emptying it there leaves no terrain; fails the test when the area has none.
    /// </summary>
    private static Vector3Int FindOpenGround(NavigationGrid2D grid, Tilemap ground)
    {
        NavigationTerrainSource2D[] sources = grid.GetComponentsInChildren<NavigationTerrainSource2D>();
        BoundsInt bounds = grid.WalkableBounds;
        Vector3 middle = bounds.center;
        bool found = false;
        Vector3Int best = default;
        foreach (Vector3Int cell in bounds.allPositionsWithin)
        {
            Vector3Int hole = cell + new Vector3Int(3, 0, 0);
            bool open = grid.IsCellWalkable(cell) && grid.IsCellWalkable(cell + Vector3Int.up) && grid.IsCellWalkable(cell + Vector3Int.down)
                && grid.IsCellWalkable(hole) && ground.HasTile(hole);
            foreach (NavigationTerrainSource2D source in sources)
            {
                open &= source.DataTilemap == ground || !source.DataTilemap.HasTile(hole);
            }

            if (open && (!found || (cell - middle).sqrMagnitude < (best - middle).sqrMagnitude))
            {
                best = cell;
                found = true;
            }
        }

        Assert.That(found, Is.True, "The area should have open ground to edit.");
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
