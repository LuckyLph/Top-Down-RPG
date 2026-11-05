using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for the World Editor's Place markers step (<see cref="WorldMarkers"/>, Docs/KnownGaps.md, Phase 3), on a world
/// of two chunks built in memory, their ground walked by the navigation: a waystone gets a blocking stone two cells north
/// of its entry, naming it, and no plain entry gets one; a road exit gets a signpost one row clear above it, pointing west
/// in the world's western half and east in its eastern, an exit into an interior a doorway on its bottom row and one out
/// of an interior a doormat across or along it, by its shape; the markers are written once, a second run changing nothing
/// and planning the same cells, a stone's own collider never pushing it on; an entry moved into the other chunk moves its
/// stone there, and one taken away loses it; a cell blocked by a prop or without ground sends the stone to the next of
/// its offsets, never onto an entry's row of arrival slots. The project's worlds hold the markers Place markers writes:
/// Testlands', the Sandbox's and its interior's checked by name and place, and every world's in step.
/// </summary>
public class WorldMarkersTests
{
    private static readonly Vector2Int Origin = new(20480, 0);
    private static readonly string[] ProjectWorlds =
    {
        "Assets/Worlds/Testlands/Testlands.asset", "Assets/Worlds/Sandbox/Sandbox.asset", "Assets/Worlds/SandboxInterior/SandboxInterior.asset",
        "Assets/Worlds/Overworld/Overworld.asset"
    };

    private readonly List<Object> created = new();
    private readonly List<Scene> opened = new();
    private WorldDefinition world;
    private Dictionary<Vector2Int, Grid> grids;
    private TerrainType2D ground;
    private Tile tile;

    /// <summary>
    /// A world of two chunks side by side, every cell of both ground the navigation walks, with a default waystone
    /// `start`, a waystone `east` in the second chunk and a plain entry `gate`.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        world = Track(WorldDefinition.Create("Markers", Origin, new Vector2Int(2, 1), new[] { Vector2Int.zero, Vector2Int.right },
            new WorldDefinition.Entry("start", new Vector2(10.5f, 20.5f), true),
            new WorldDefinition.Entry("east", new Vector2(70.5f, 20.5f), true),
            new WorldDefinition.Entry("gate", new Vector2(30.5f, 20.5f), false)));
        Track(world.RootScene);
        foreach (WorldDefinition.Chunk chunk in world.Chunks)
        {
            Track(chunk.Scene);
        }

        ground = Track(TerrainType2D.Create("ground"));
        tile = Track(ScriptableObject.CreateInstance<Tile>());
        grids = new Dictionary<Vector2Int, Grid> { [Vector2Int.zero] = BuildChunk(Vector2Int.zero), [Vector2Int.right] = BuildChunk(Vector2Int.right) };
    }

    /// <summary>
    /// Destroys what the test made and closes the scenes it opened, unchanged.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        for (int i = created.Count - 1; i >= 0; i--)
        {
            if (created[i] != null)
            {
                Object.DestroyImmediate(created[i]);
            }
        }

        created.Clear();
        WorldAuthoring.CloseScenes(opened, false);
        opened.Clear();
    }

    /// <summary>
    /// Each waystone gets a stone two cells north of its entry, in the chunk owning that cell, naming its entry; the plain
    /// entry gets none. A road exit near the west edge gets a signpost pointing west one row clear above it, one near the
    /// east edge one pointing east; an exit into an interior a doorway standing on the middle of its bottom row; an exit
    /// out of one two cells wide a doormat across it, two cells tall one along it; an exit with no marker nothing.
    /// </summary>
    [Test]
    public void Plan_StandsEachWaystoneTwoNorthOfItsEntry_AndMarksEachExitByItsKind()
    {
        WorldExit west = AddExit(new Vector2Int(5, 30), new Vector2Int(1, 2), ExitMarker.Signpost);
        WorldExit east = AddExit(new Vector2Int(120, 30), new Vector2Int(1, 2), ExitMarker.Signpost);
        WorldExit door = AddExit(new Vector2Int(40, 10), new Vector2Int(1, 2), ExitMarker.Door);
        WorldExit across = AddExit(new Vector2Int(90, 5), new Vector2Int(2, 1), ExitMarker.Doormat);
        WorldExit along = AddExit(new Vector2Int(100, 5), new Vector2Int(1, 2), ExitMarker.Doormat);
        AddExit(new Vector2Int(50, 50), Vector2Int.one, ExitMarker.None);
        List<string> report = new();

        Dictionary<Vector2Int, List<WorldMarkers.Placement>> plan = WorldMarkers.Plan(world, grids, report);

        Assert.That(report, Is.Empty);
        Assert.That(plan[Vector2Int.zero].ConvertAll(p => p.Name), Is.EqualTo(new[] { "Waystone_start", $"Signpost_{west.Id}", $"Door_{door.Id}" }));
        Assert.That(plan[Vector2Int.right].ConvertAll(p => p.Name), Is.EqualTo(new[] { "Waystone_east", $"Signpost_{east.Id}", $"Doormat_{across.Id}", $"Doormat_{along.Id}" }));
        WorldMarkers.Placement start = plan[Vector2Int.zero][0];
        Assert.That(start.Cell, Is.EqualTo(new Vector2Int(10, 22)), "Two cells north of its entry.");
        Assert.That(start.Entry, Is.EqualTo("start"));
        Assert.That(start.Prefab, Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(WorldMarkers.WaystonePath)));
        Assert.That(plan[Vector2Int.right][0].Cell, Is.EqualTo(new Vector2Int(70, 22)));
        AssertFootprintOn(start, new Vector2Int(10, 22));

        WorldMarkers.Placement westSign = plan[Vector2Int.zero][1];
        Assert.That(westSign.Cell, Is.EqualTo(new Vector2Int(5, 33)), "One row clear above the exit's top row.");
        Assert.That(westSign.Prefab.name, Is.EqualTo($"Signpost_{MarkerRecipe.SignpostWest}"));
        Assert.That(plan[Vector2Int.right][1].Cell, Is.EqualTo(new Vector2Int(120, 33)));
        Assert.That(plan[Vector2Int.right][1].Prefab.name, Is.EqualTo($"Signpost_{MarkerRecipe.SignpostEast}"));

        WorldMarkers.Placement doorway = plan[Vector2Int.zero][2];
        Assert.That(doorway.Prefab, Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(WorldMarkers.DoorwayPath)));
        Assert.That((Vector2)doorway.Position, Is.EqualTo(world.ToWorld(new Vector2(40.5f, 10.5f))), "On the middle of its bottom row.");
        Assert.That(plan[Vector2Int.right][2].Prefab.name, Is.EqualTo($"Doormat_{MarkerRecipe.DoormatAcross}"));
        Assert.That((Vector2)plan[Vector2Int.right][2].Position, Is.EqualTo(world.ToWorld(new Vector2(91f, 5.5f))), "On its middle.");
        Assert.That(plan[Vector2Int.right][3].Prefab.name, Is.EqualTo($"Doormat_{MarkerRecipe.DoormatAlong}"));
    }

    /// <summary>
    /// Writing makes each chunk's markers under its grid's root, in order, each where its plan puts it, a stone naming
    /// its entry and sleeping; a second write changes nothing and the markers are in step, and planning again, the
    /// stones' own colliders now on their cells, puts them on the same cells.
    /// </summary>
    [Test]
    public void Write_MakesTheMarkersOnce_AndASecondRunChangesNothing()
    {
        AddExit(new Vector2Int(120, 30), new Vector2Int(1, 2), ExitMarker.Signpost);
        Dictionary<Vector2Int, List<WorldMarkers.Placement>> plan = WorldMarkers.Plan(world, grids, new List<string>());

        foreach (KeyValuePair<Vector2Int, Grid> chunk in grids)
        {
            Assert.That(WorldMarkers.WriteChunk(chunk.Value, plan[chunk.Key]), Is.True, $"chunk {chunk.Key} is written");
        }

        Transform root = grids[Vector2Int.right].transform.Find(WorldMarkers.RootName);
        Assert.That(root, Is.Not.Null);
        Assert.That(root.childCount, Is.EqualTo(2));
        WaystoneMarker stone = root.GetChild(0).GetComponent<WaystoneMarker>();
        Assert.That(stone.EntryName, Is.EqualTo("east"));
        Assert.That(stone.transform.position, Is.EqualTo(plan[Vector2Int.right][0].Position));
        Assert.That(stone.IsAwake, Is.False, "It sleeps until this machine's player discovers it.");
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(stone.gameObject), Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(WorldMarkers.WaystonePath)));

        foreach (KeyValuePair<Vector2Int, Grid> chunk in grids)
        {
            Assert.That(WorldMarkers.WriteChunk(chunk.Value, plan[chunk.Key]), Is.False, $"chunk {chunk.Key} is in step");
            Assert.That(WorldMarkers.Differs(chunk.Value, plan[chunk.Key]), Is.False);
        }

        Dictionary<Vector2Int, List<WorldMarkers.Placement>> again = WorldMarkers.Plan(world, grids, new List<string>());
        foreach (KeyValuePair<Vector2Int, Grid> chunk in grids)
        {
            Assert.That(again[chunk.Key].ConvertAll(p => p.Cell), Is.EqualTo(plan[chunk.Key].ConvertAll(p => p.Cell)), $"chunk {chunk.Key}'s markers stay on their cells");
        }
    }

    /// <summary>
    /// A waystone moved into the other chunk takes its stone there, its old chunk losing its markers and its root; a
    /// waystone taken away loses its stone.
    /// </summary>
    [Test]
    public void AMovedEntry_MovesItsStone_IntoAnotherChunkToo_AndAGoneOneLosesIt()
    {
        WriteAll();
        world.SetEntries(new[]
        {
            new WorldDefinition.Entry("start", new Vector2(10.5f, 20.5f), true),
            new WorldDefinition.Entry("east", new Vector2(40.5f, 30.5f), true)
        });

        WriteAll();

        Assert.That(grids[Vector2Int.right].transform.Find(WorldMarkers.RootName), Is.Null, "The second chunk holds no marker any more.");
        Transform root = grids[Vector2Int.zero].transform.Find(WorldMarkers.RootName);
        Assert.That(Names(root), Is.EqualTo(new[] { "Waystone_start", "Waystone_east" }));
        Assert.That(root.GetChild(1).GetComponent<WaystoneMarker>().EntryName, Is.EqualTo("east"));
        AssertFootprintOn(new WorldMarkers.Placement(null, root.GetChild(1).position, null, null, default), new Vector2Int(40, 32));

        world.SetEntries(new[] { new WorldDefinition.Entry("start", new Vector2(10.5f, 20.5f), true) });
        WriteAll();
        Assert.That(Names(grids[Vector2Int.zero].transform.Find(WorldMarkers.RootName)), Is.EqualTo(new[] { "Waystone_start" }));
    }

    /// <summary>
    /// A prop blocking the cell two north of an entry sends its stone one north; with that cell bare of ground too, to the
    /// next offset, north-west two; no stone ever stands on the entry's row.
    /// </summary>
    [Test]
    public void ABlockedCell_SendsTheStoneToItsNextOffset_NeverOntoTheEntrysRow()
    {
        GameObject rock = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Props/Rocks/Rock_Grey_0.prefab");
        Assert.That(PropGridSnapping.TryGetFootprint(rock, out Rect footprint), Is.True);
        GameObject placed = Track((GameObject)PrefabUtility.InstantiatePrefab(rock));
        placed.transform.position = PropGridSnapping.RootOnCells(grids[Vector2Int.zero], new Vector3Int(10, 22, 0), footprint);

        Assert.That(StoneCell("start"), Is.EqualTo(new Vector2Int(10, 21)), "One north, past the rock.");

        grids[Vector2Int.zero].GetComponentInChildren<Tilemap>().SetTile(new Vector3Int(10, 21, 0), null);
        Assert.That(StoneCell("start"), Is.EqualTo(new Vector2Int(9, 22)), "North-west two, past the cell without ground.");
        Assert.That(StoneCell("start").y, Is.Not.EqualTo(20), "Never on the entry's row.");
    }

    /// <summary>
    /// The project's worlds hold the markers Place markers writes, every chunk in step: Testlands' stones two north of
    /// `start` and `north` and its signpost east of its road's end, pointing east; the Sandbox's stone, its doorway on
    /// the road into its interior and its signpost to Testlands, pointing west; its interior's doormat across its exit
    /// and no stone, its `start` being no waystone.
    /// </summary>
    [Test]
    public void TheProjectsWorlds_HoldTheirMarkers_InStep()
    {
        foreach (string path in ProjectWorlds)
        {
            WorldDefinition projectWorld = AssetDatabase.LoadAssetAtPath<WorldDefinition>(path);
            Assert.That(WorldMarkers.Stale(projectWorld), Is.Empty, projectWorld.name);
        }

        WorldDefinition testlands = AssetDatabase.LoadAssetAtPath<WorldDefinition>(ProjectWorlds[0]);
        Assert.That(MarkersOf(testlands, new Vector2Int(0, 0)), Is.EqualTo(new[] { "Waystone_start@(24, 42)" }));
        Assert.That(MarkersOf(testlands, new Vector2Int(0, 1)), Is.EqualTo(new[] { "Waystone_north@(30, 92)" }));
        Assert.That(MarkersOf(testlands, new Vector2Int(1, 0)), Has.Count.EqualTo(1).And.All.StartsWith("Signpost_").And.All.EndsWith("@(120, 42)"));
        Assert.That(PrefabOf(testlands, new Vector2Int(1, 0), 0), Is.EqualTo($"Signpost_{MarkerRecipe.SignpostEast}"));

        WorldDefinition sandbox = AssetDatabase.LoadAssetAtPath<WorldDefinition>(ProjectWorlds[1]);
        List<string> sandboxMarkers = MarkersOf(sandbox, Vector2Int.zero);
        Assert.That(sandboxMarkers, Has.Count.EqualTo(3));
        Assert.That(sandboxMarkers[0], Is.EqualTo("Waystone_start@(30, 44)"));
        Assert.That(sandboxMarkers[1], Does.StartWith("Door_").And.EndWith("@(36, 41)"));
        Assert.That(sandboxMarkers[2], Does.StartWith("Signpost_").And.EndWith("@(3, 44)"));
        Assert.That(PrefabOf(sandbox, Vector2Int.zero, 2), Is.EqualTo($"Signpost_{MarkerRecipe.SignpostWest}"));

        WorldDefinition interior = AssetDatabase.LoadAssetAtPath<WorldDefinition>(ProjectWorlds[2]);
        Assert.That(interior.DefaultEntry.Waystone, Is.False, "The interior's start is no waystone.");
        Assert.That(MarkersOf(interior, Vector2Int.zero), Has.Count.EqualTo(1).And.All.StartsWith("Doormat_").And.All.EndsWith("@(32, 24)"));
        Assert.That(PrefabOf(interior, Vector2Int.zero, 0), Is.EqualTo($"Doormat_{MarkerRecipe.DoormatAcross}"));
    }

    /// <summary>
    /// Plans and writes every chunk of the test world.
    /// </summary>
    private void WriteAll()
    {
        Dictionary<Vector2Int, List<WorldMarkers.Placement>> plan = WorldMarkers.Plan(world, grids, new List<string>());
        foreach (KeyValuePair<Vector2Int, Grid> chunk in grids)
        {
            plan.TryGetValue(chunk.Key, out List<WorldMarkers.Placement> placements);
            WorldMarkers.WriteChunk(chunk.Value, placements);
        }
    }

    /// <summary>
    /// The cell the plan stands the stone of the waystone <paramref name="entry"/> on.
    /// </summary>
    private Vector2Int StoneCell(string entry)
    {
        foreach (List<WorldMarkers.Placement> placements in WorldMarkers.Plan(world, grids, new List<string>()).Values)
        {
            foreach (WorldMarkers.Placement placement in placements)
            {
                if (placement.Entry == entry)
                {
                    return placement.Cell;
                }
            }
        }

        Assert.Fail($"The waystone '{entry}' has no stone.");
        return default;
    }

    /// <summary>
    /// Asserts the stone rooted where <paramref name="placement"/> stands blocks exactly <paramref name="cell"/>.
    /// </summary>
    private void AssertFootprintOn(WorldMarkers.Placement placement, Vector2Int cell)
    {
        GameObject stone = AssetDatabase.LoadAssetAtPath<GameObject>(WorldMarkers.WaystonePath);
        Assert.That(PropGridSnapping.TryGetFootprint(stone, out Rect footprint), Is.True);
        BoundsInt blocked = PropGridSnapping.BlockedCells(grids[Vector2Int.zero], placement.Position, footprint);
        Assert.That(blocked, Is.EqualTo(new BoundsInt(cell.x, cell.y, 0, 1, 1, 1)), "The stone blocks its cell, and only it.");
    }

    /// <summary>
    /// The names of <paramref name="root"/>'s children, in order.
    /// </summary>
    private static List<string> Names(Transform root)
    {
        List<string> names = new();
        foreach (Transform child in root)
        {
            names.Add(child.name);
        }

        return names;
    }

    /// <summary>
    /// The markers of <paramref name="projectWorld"/>'s chunk at <paramref name="coords"/>, each its name and the cell
    /// holding its root, opened beside the test's scene.
    /// </summary>
    private List<string> MarkersOf(WorldDefinition projectWorld, Vector2Int coords)
    {
        Transform root = OpenGrid(projectWorld, coords).transform.Find(WorldMarkers.RootName);
        List<string> markers = new();
        foreach (Transform child in root)
        {
            Vector2Int cell = ChunkCoords.CellOf(projectWorld.ToLocal(child.position));
            markers.Add($"{child.name}@({cell.x}, {cell.y})");
            if (child.TryGetComponent(out WaystoneMarker stone))
            {
                Assert.That(child.name, Is.EqualTo($"Waystone_{stone.EntryName}"), "A stone names its entry.");
            }
        }

        return markers;
    }

    /// <summary>
    /// The prefab of marker <paramref name="index"/> of <paramref name="projectWorld"/>'s chunk at
    /// <paramref name="coords"/>.
    /// </summary>
    private string PrefabOf(WorldDefinition projectWorld, Vector2Int coords, int index)
    {
        Transform root = OpenGrid(projectWorld, coords).transform.Find(WorldMarkers.RootName);
        return PrefabUtility.GetCorrespondingObjectFromSource(root.GetChild(index).gameObject).name;
    }

    /// <summary>
    /// The grid of <paramref name="projectWorld"/>'s chunk at <paramref name="coords"/>, its scene opened beside the
    /// test's when it is not open.
    /// </summary>
    private Grid OpenGrid(WorldDefinition projectWorld, Vector2Int coords)
    {
        Assert.That(projectWorld.TryGetChunkScene(coords, out SceneDefinition definition), Is.True);
        Scene scene = SceneManager.GetSceneByPath(definition.ScenePath);
        if (!scene.isLoaded)
        {
            scene = EditorSceneManager.OpenScene(definition.ScenePath, OpenSceneMode.Additive);
            opened.Add(scene);
        }

        return SceneQuery.FindFirst<Grid>(scene);
    }

    /// <summary>
    /// A chunk grid at the world's origin with one ground layer filling its cells and navigation over it that obstacles
    /// on the Obstacles layer block.
    /// </summary>
    private Grid BuildChunk(Vector2Int coords)
    {
        GameObject gridObject = Track(new GameObject($"Chunk {coords}", typeof(Grid)));
        gridObject.SetActive(false);
        gridObject.transform.position = new Vector3(Origin.x, Origin.y, 0f);
        gridObject.AddComponent<WorldChunk>().Configure(world, coords, null);
        GameObject layer = new("Ground", typeof(Tilemap));
        layer.transform.SetParent(gridObject.transform, false);
        Tilemap tilemap = layer.GetComponent<Tilemap>();
        RectInt cells = ChunkCoords.Bounds(coords);
        TileBase[] tiles = new TileBase[cells.width * cells.height];
        for (int i = 0; i < tiles.Length; i++)
        {
            tiles[i] = tile;
        }

        tilemap.SetTilesBlock(new BoundsInt(cells.x, cells.y, 0, cells.width, cells.height, 1), tiles);
        NavigationTerrainSource2D source = layer.AddComponent<NavigationTerrainSource2D>();
        source.Configure(ground, 0);
        NavigationGrid2D navigation = gridObject.AddComponent<NavigationGrid2D>();
        navigation.ConfigureSources(source);
        navigation.ConfigureObstacleLayers(LayerMask.GetMask("Obstacles"));
        gridObject.SetActive(true);
        return gridObject.GetComponent<Grid>();
    }

    /// <summary>
    /// An exit of the test world on <paramref name="cell"/> covering <paramref name="size"/>, shown by
    /// <paramref name="marker"/>, with a persistent id.
    /// </summary>
    private WorldExit AddExit(Vector2Int cell, Vector2Int size, ExitMarker marker)
    {
        GameObject exitObject = Track(new GameObject($"Exit {cell}"));
        exitObject.transform.position = world.ToWorld(cell + Vector2.one * 0.5f);
        WorldExit exit = exitObject.AddComponent<WorldExit>();
        PersistentIds.SetId(exitObject.GetComponent<PersistentId>(), PersistentIds.NewId());
        WorldAuthoring.ConfigureExit(exit, world, string.Empty, size, marker);
        return exit;
    }

    private T Track<T>(T item) where T : Object
    {
        created.Add(item);
        return item;
    }
}
