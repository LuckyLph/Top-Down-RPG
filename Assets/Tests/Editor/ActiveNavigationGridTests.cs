using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VContainer;

public class ActiveNavigationGridTests
{
    private readonly List<GameObject> sceneObjects = new();
    private readonly List<IObjectResolver> containers = new();
    private PlayerRegistry players;
    private ActiveSpawnPoint activeSpawnPoint;
    private ActiveNavigationGrid activeNavigationGrid;
    private CameraFollow2D cameraFollow;

    [SetUp]
    public void SetUp()
    {
        players = new PlayerRegistry();
        activeSpawnPoint = new ActiveSpawnPoint();
        activeNavigationGrid = new ActiveNavigationGrid();
        GameObject cameraObject = new("Camera");
        sceneObjects.Add(cameraObject);
        cameraFollow = cameraObject.AddComponent<CameraFollow2D>();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (IObjectResolver container in containers)
        {
            container.Dispose();
        }

        containers.Clear();

        foreach (GameObject sceneObject in sceneObjects)
        {
            if (sceneObject != null)
            {
                Object.DestroyImmediate(sceneObject);
            }
        }

        sceneObjects.Clear();
    }

    [Test]
    public void Clear_OnlyClearsTheGridItWasGiven()
    {
        NavigationGrid2D first = CreateGrid("First");
        NavigationGrid2D second = CreateGrid("Second");

        Assert.That(activeNavigationGrid.Current, Is.Null);

        activeNavigationGrid.Set(first);
        Assert.That(activeNavigationGrid.Current, Is.SameAs(first));

        activeNavigationGrid.Clear(second);
        Assert.That(activeNavigationGrid.Current, Is.SameAs(first));

        activeNavigationGrid.Clear(null);
        Assert.That(activeNavigationGrid.Current, Is.SameAs(first));

        activeNavigationGrid.Clear(first);
        Assert.That(activeNavigationGrid.Current, Is.Null);
    }

    [Test]
    public void EnteringAnArea_SetsItsGrid_AndDisposingTheAreaScopeClearsIt()
    {
        NavigationGrid2D grid = CreateGrid("Clearing");
        IObjectResolver area = BuildAreaContainer(grid);

        area.Resolve<IAreaEntry>().Enter();
        Assert.That(activeNavigationGrid.Current, Is.SameAs(grid));

        area.Dispose();
        Assert.That(activeNavigationGrid.Current, Is.Null);
    }

    [Test]
    public void ChangingArea_PointsAtTheNewAreasGrid()
    {
        NavigationGrid2D oldGrid = CreateGrid("Old");
        NavigationGrid2D newGrid = CreateGrid("New");
        IObjectResolver oldArea = BuildAreaContainer(oldGrid);
        oldArea.Resolve<IAreaEntry>().Enter();

        oldArea.Dispose();
        Assert.That(activeNavigationGrid.Current, Is.Null, "Between areas nothing points at the unloaded grid.");

        IObjectResolver newArea = BuildAreaContainer(newGrid);
        newArea.Resolve<IAreaEntry>().Enter();
        Assert.That(activeNavigationGrid.Current, Is.SameAs(newGrid));
    }

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

        Assert.That(activeNavigationGrid.Current, Is.SameAs(newGrid));
    }

    [Test]
    public void AnAreaWithoutAGrid_LeavesTheHolderEmpty()
    {
        NavigationGrid2D grid = CreateGrid("Clearing");
        IObjectResolver gridArea = BuildAreaContainer(grid);
        gridArea.Resolve<IAreaEntry>().Enter();
        gridArea.Dispose();

        IObjectResolver gridlessArea = BuildAreaContainer(null);
        gridlessArea.Resolve<IAreaEntry>().Enter();
        Assert.That(activeNavigationGrid.Current, Is.Null);

        gridlessArea.Dispose();
        Assert.That(activeNavigationGrid.Current, Is.Null);
    }

    private IObjectResolver BuildAreaContainer(NavigationGrid2D grid)
    {
        ContainerBuilder builder = new();
        builder.RegisterInstance(new AreaEntryRequest(null, "start"));
        builder.RegisterInstance<IPlayerRegistry>(players);
        builder.RegisterInstance(activeSpawnPoint);
        builder.RegisterInstance(activeNavigationGrid);
        builder.RegisterInstance(cameraFollow);
        builder.Register<AreaEntry>(Lifetime.Singleton)
            .As<IAreaEntry>()
            .WithParameter(new[] { CreateSpawnPoint() })
            .WithParameter(grid);
        IObjectResolver container = builder.Build();
        containers.Add(container);
        return container;
    }

    private NavigationGrid2D CreateGrid(string name)
    {
        GameObject gridObject = new(name);
        sceneObjects.Add(gridObject);
        return gridObject.AddComponent<NavigationGrid2D>();
    }

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
