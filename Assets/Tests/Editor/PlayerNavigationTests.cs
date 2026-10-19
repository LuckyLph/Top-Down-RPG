using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VContainer;

/// <summary>
/// Tests for <see cref="PlayerNavigation"/>: how <see cref="AreaEntry"/> sets the area's navigation on entering an
/// area and clears it when the area scope is disposed, and a player in a world finding its world's navigation.
/// </summary>
public class PlayerNavigationTests
{
    private readonly List<GameObject> sceneObjects = new();
    private readonly List<IObjectResolver> containers = new();
    private PlayerRegistry players;
    private ActiveSpawnPoint activeSpawnPoint;
    private PlayerWorlds playerWorlds;
    private Worlds worlds;
    private PlayerNavigation playerNavigation;
    private CameraFollow2D cameraFollow;

    /// <summary>
    /// Builds the shared player registry, world records, players' navigation and spawn point holder, and a camera with
    /// <see cref="CameraFollow2D"/>.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        players = new PlayerRegistry();
        activeSpawnPoint = new ActiveSpawnPoint();
        playerWorlds = new PlayerWorlds(players);
        worlds = new Worlds();
        playerNavigation = new PlayerNavigation(players, playerWorlds, worlds);
        GameObject cameraObject = new("Camera");
        sceneObjects.Add(cameraObject);
        cameraFollow = cameraObject.AddComponent<CameraFollow2D>();
    }

    /// <summary>
    /// Disposes every area container the test built and destroys the scene objects it created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (IObjectResolver container in containers)
        {
            container.Dispose();
        }

        containers.Clear();
        playerWorlds.Dispose();

        foreach (GameObject sceneObject in sceneObjects)
        {
            if (sceneObject != null)
            {
                Object.DestroyImmediate(sceneObject);
            }
        }

        sceneObjects.Clear();
    }

    /// <summary>
    /// <see cref="PlayerNavigation.ClearArea"/> empties the area's navigation only when given the current one; another
    /// grid or null leaves it set.
    /// </summary>
    [Test]
    public void ClearArea_OnlyClearsTheGridItWasGiven()
    {
        NavigationGrid2D first = CreateGrid("First");
        NavigationGrid2D second = CreateGrid("Second");

        Assert.That(playerNavigation.Area, Is.Null);

        playerNavigation.SetArea(first);
        Assert.That(playerNavigation.Area, Is.SameAs(first));

        playerNavigation.ClearArea(second);
        Assert.That(playerNavigation.Area, Is.SameAs(first));

        playerNavigation.ClearArea(null);
        Assert.That(playerNavigation.Area, Is.SameAs(first));

        playerNavigation.ClearArea(first);
        Assert.That(playerNavigation.Area, Is.Null);
    }

    /// <summary>
    /// A player in no world paths on the area's navigation; once in a world it paths on that world's navigation while
    /// the world is loaded, and on nothing while it is not.
    /// </summary>
    [Test]
    public void Of_APlayerInAWorld_PathsOnItsWorldsNavigation_AndOnTheAreasOtherwise()
    {
        NavigationGrid2D area = CreateGrid("Clearing");
        playerNavigation.SetArea(area);
        GameObject playerObject = new("Player");
        sceneObjects.Add(playerObject);
        PlayerHandle player = new(playerObject.transform, null);
        players.Add(player);
        WorldDefinition world = WorldDefinition.Create("PlayerNavigation", new Vector2Int(2048, 0), Vector2Int.one, new[] { Vector2Int.zero });
        WorldNavigation navigation = new(new NavigationPagePool());
        try
        {
            Assert.That(playerNavigation.Of(playerObject.transform), Is.SameAs(area));

            playerWorlds.Set(player, world);
            Assert.That(playerNavigation.Of(playerObject.transform), Is.Null, "Its world is not loaded yet.");

            worlds.Add(new LoadedWorld(world, default, null, null, navigation));
            Assert.That(playerNavigation.Of(playerObject.transform), Is.SameAs(navigation));

            playerWorlds.Set(player, null);
            Assert.That(playerNavigation.Of(playerObject.transform), Is.SameAs(area));
        }
        finally
        {
            Object.DestroyImmediate(world);
        }
    }

    /// <summary>
    /// Entering an area makes its grid the players' navigation, and disposing the area container clears it.
    /// </summary>
    [Test]
    public void EnteringAnArea_SetsItsGrid_AndDisposingTheAreaScopeClearsIt()
    {
        NavigationGrid2D grid = CreateGrid("Clearing");
        IObjectResolver area = BuildAreaContainer(grid);

        area.Resolve<IAreaEntry>().Enter();
        Assert.That(playerNavigation.Area, Is.SameAs(grid));

        area.Dispose();
        Assert.That(playerNavigation.Area, Is.Null);
    }

    /// <summary>
    /// Leaving one area and entering the next leaves the area's navigation empty in between and then points at the new grid.
    /// </summary>
    [Test]
    public void ChangingArea_PointsAtTheNewAreasGrid()
    {
        NavigationGrid2D oldGrid = CreateGrid("Old");
        NavigationGrid2D newGrid = CreateGrid("New");
        IObjectResolver oldArea = BuildAreaContainer(oldGrid);
        oldArea.Resolve<IAreaEntry>().Enter();

        oldArea.Dispose();
        Assert.That(playerNavigation.Area, Is.Null, "Between areas nothing points at the unloaded grid.");

        IObjectResolver newArea = BuildAreaContainer(newGrid);
        newArea.Resolve<IAreaEntry>().Enter();
        Assert.That(playerNavigation.Area, Is.SameAs(newGrid));
    }

    /// <summary>
    /// Disposing the old area after the new one was entered does not clear the new area's grid.
    /// </summary>
    [Test]
    public void DisposingTheOldArea_AfterTheNewOneWasEntered_KeepsTheNewGrid()
    {
        NavigationGrid2D oldGrid = CreateGrid("Old");
        NavigationGrid2D newGrid = CreateGrid("New");
        IObjectResolver oldArea = BuildAreaContainer(oldGrid);
        oldArea.Resolve<IAreaEntry>().Enter();
        IObjectResolver newArea = BuildAreaContainer(newGrid);
        newArea.Resolve<IAreaEntry>().Enter();

        oldArea.Dispose();

        Assert.That(playerNavigation.Area, Is.SameAs(newGrid));
    }

    /// <summary>
    /// Entering and disposing an area that has no grid leaves the area's navigation empty.
    /// </summary>
    [Test]
    public void AnAreaWithoutAGrid_LeavesTheHolderEmpty()
    {
        NavigationGrid2D grid = CreateGrid("Clearing");
        IObjectResolver gridArea = BuildAreaContainer(grid);
        gridArea.Resolve<IAreaEntry>().Enter();
        gridArea.Dispose();

        IObjectResolver gridlessArea = BuildAreaContainer(null);
        gridlessArea.Resolve<IAreaEntry>().Enter();
        Assert.That(playerNavigation.Area, Is.Null);

        gridlessArea.Dispose();
        Assert.That(playerNavigation.Area, Is.Null);
    }

    /// <summary>
    /// Builds a tracked container that resolves an <see cref="AreaEntry"/> for an area with one "start" spawn
    /// point and <paramref name="grid"/> as its navigation grid (null for none).
    /// </summary>
    private IObjectResolver BuildAreaContainer(NavigationGrid2D grid)
    {
        ContainerBuilder builder = new();
        builder.RegisterInstance(new AreaEntryRequest(null, "start"));
        builder.RegisterInstance<IPlayerRegistry>(players);
        builder.RegisterInstance(activeSpawnPoint);
        builder.RegisterInstance(playerNavigation);
        builder.RegisterInstance(cameraFollow);
        builder.Register<AreaEntry>(Lifetime.Singleton)
            .As<IAreaEntry>()
            .WithParameter(new[] { CreateSpawnPoint() })
            .WithParameter(grid);
        IObjectResolver container = builder.Build();
        containers.Add(container);
        return container;
    }

    /// <summary>
    /// Creates a tracked game object with an empty <see cref="NavigationGrid2D"/>.
    /// </summary>
    private NavigationGrid2D CreateGrid(string name)
    {
        GameObject gridObject = new(name);
        sceneObjects.Add(gridObject);
        return gridObject.AddComponent<NavigationGrid2D>();
    }

    /// <summary>
    /// Creates a tracked spawn point whose id is "start".
    /// </summary>
    private SpawnPoint CreateSpawnPoint()
    {
        GameObject spawnObject = new("SpawnPoint");
        sceneObjects.Add(spawnObject);
        SpawnPoint spawnPoint = spawnObject.AddComponent<SpawnPoint>();
        UnityEditor.SerializedObject serialized = new(spawnPoint);
        serialized.FindProperty("spawnId").stringValue = "start";
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return spawnPoint;
    }
}
