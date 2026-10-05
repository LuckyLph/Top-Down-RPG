using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VContainer;

/// <summary>
/// Tests for <see cref="ActiveNavigationGrid"/> and how <see cref="AreaEntry"/> sets it on entering an area and
/// clears it when the area scope is disposed.
/// </summary>
public class ActiveNavigationGridTests
{
    private readonly List<GameObject> sceneObjects = new();
    private readonly List<IObjectResolver> containers = new();
    private PlayerRegistry players;
    private ActiveSpawnPoint activeSpawnPoint;
    private ActiveNavigationGrid activeNavigationGrid;
    private CameraFollow2D cameraFollow;

    /// <summary>
    /// Builds the shared player registry, spawn point and grid holders and a camera with
    /// <see cref="CameraFollow2D"/>.
    /// </summary>
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
    /// <see cref="ActiveNavigationGrid.Clear"/> empties the holder only when given the current grid; another grid
    /// or null leaves it set.
    /// </summary>
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

    /// <summary>
    /// Entering an area makes its grid the active one, and disposing the area container clears it.
    /// </summary>
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

    /// <summary>
    /// Leaving one area and entering the next leaves the holder empty in between and then points at the new grid.
    /// </summary>
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

        Assert.That(activeNavigationGrid.Current, Is.SameAs(newGrid));
    }

    /// <summary>
    /// Entering and disposing an area that has no grid leaves the holder empty.
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
        Assert.That(activeNavigationGrid.Current, Is.Null);

        gridlessArea.Dispose();
        Assert.That(activeNavigationGrid.Current, Is.Null);
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
