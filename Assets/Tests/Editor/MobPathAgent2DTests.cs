using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class MobPathAgent2DTests
{
    private readonly List<Object> createdObjects = new();
    private GameObject root;
    private Tile fillTile;
    private NavigationGrid2D navigationGrid;
    private MobConfig config;
    private Rigidbody2D body;
    private MobPathAgent2D pathAgent;

    [TearDown]
    public void TearDown()
    {
        for (int i = 0; i < createdObjects.Count; i++)
        {
            if (createdObjects[i] != null)
            {
                Object.DestroyImmediate(createdObjects[i]);
            }
        }

        createdObjects.Clear();
    }

    [Test]
    public void FixedTick_UsesArrivalDistance_ForSingleWaypointPath()
    {
        CreateGridRoot();
        TerrainType2D ground = CreateTerrain("ground");
        AddTerrainLayer("Ground", ground, Rect(0, 0, 4, 0));
        BuildGridAndAgent(null);

        PlaceMob(new Vector2(0.5f, 0.5f));
        Vector2 goal = navigationGrid.CellToWorldCenter(new Vector3Int(4, 0, 0));
        Assert.That(pathAgent.BuildPathToWorld(goal, allowPartial: false), Is.True);
        Assert.That(pathAgent.Waypoints.Count, Is.EqualTo(1), "A straight corridor should smooth to a single waypoint.");

        // Inside arrivalDistance (0.15) but outside waypointReachDistance (0.05).
        PlaceMob(goal - new Vector2(0.1f, 0f));
        pathAgent.FixedTick();

        Assert.That(pathAgent.ReachedDestination, Is.True);
    }

    [Test]
    public void FixedTick_KeepsArrivalDistance_OnTicksAfterReachingPenultimateWaypoint()
    {
        CreateGridRoot();
        TerrainType2D ground = CreateTerrain("ground");
        List<Vector3Int> lShape = Rect(0, 0, 4, 0);
        lShape.AddRange(Rect(4, 1, 4, 4));
        AddTerrainLayer("Ground", ground, lShape);
        BuildGridAndAgent(null);

        PlaceMob(new Vector2(0.5f, 0.5f));
        Vector2 goal = navigationGrid.CellToWorldCenter(new Vector3Int(4, 4, 0));
        Assert.That(pathAgent.BuildPathToWorld(goal, allowPartial: false), Is.True);
        Assert.That(pathAgent.Waypoints.Count, Is.EqualTo(2), "The L-shaped corridor should smooth to its corner and the goal.");

        PlaceMob(pathAgent.Waypoints[0]);
        pathAgent.FixedTick();
        Assert.That(pathAgent.ReachedDestination, Is.False);

        PlaceMob(goal - new Vector2(0f, 0.1f));
        pathAgent.FixedTick();

        Assert.That(pathAgent.ReachedDestination, Is.True);
    }

    private void CreateGridRoot()
    {
        root = Track(new GameObject("MobPathAgentTestRoot"));
        root.AddComponent<Grid>();
        fillTile = Track(ScriptableObject.CreateInstance<Tile>());
    }

    private void AddTerrainLayer(string name, TerrainType2D terrain, IEnumerable<Vector3Int> cells)
    {
        GameObject layerObject = new(name);
        layerObject.transform.SetParent(root.transform);
        Tilemap tilemap = layerObject.AddComponent<Tilemap>();
        layerObject.AddComponent<TilemapRenderer>();
        foreach (Vector3Int cell in cells)
        {
            tilemap.SetTile(cell, fillTile);
        }

        layerObject.AddComponent<NavigationTerrainSource2D>().Configure(tilemap, null, null, terrain);
    }

    private void BuildGridAndAgent(TerrainMovementProfile2D profile)
    {
        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.BuildGrid();

        config = Track(ScriptableObject.CreateInstance<MobConfig>());
        config.waypointReachDistance = 0.05f;
        config.arrivalDistance = 0.15f;
        config.movementProfile = profile;

        GameObject mob = Track(new GameObject("Mob"));
        body = mob.AddComponent<Rigidbody2D>();
        MobMotor2D motor = mob.AddComponent<MobMotor2D>();
        pathAgent = mob.AddComponent<MobPathAgent2D>();
        motor.Initialize(config);
        pathAgent.Initialize(navigationGrid, motor, config);
    }

    private void PlaceMob(Vector2 position)
    {
        body.position = position;
        body.transform.position = position;
    }

    private TerrainType2D CreateTerrain(string id)
    {
        TerrainType2D terrain = Track(ScriptableObject.CreateInstance<TerrainType2D>());
        terrain.Configure(id);
        return terrain;
    }

    private static List<Vector3Int> Rect(int minX, int minY, int maxX, int maxY)
    {
        List<Vector3Int> cells = new();
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                cells.Add(new Vector3Int(x, y, 0));
            }
        }

        return cells;
    }

    private T Track<T>(T created) where T : Object
    {
        createdObjects.Add(created);
        return created;
    }
}
