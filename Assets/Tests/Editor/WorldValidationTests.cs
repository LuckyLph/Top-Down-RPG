using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for <see cref="WorldValidation"/> on a chunk built in memory: chunk (1, 0) of a world at (2048, 0), owning
/// cells (64, 0) to (127, 63). Spawn points, mob spawn points and walk-through props on its cells pass and those past
/// its border are refused by name; a blocking prop whose footprint crosses the border is refused and one inside it
/// passes, by the cells the navigation would block; a mob spawn point without a <see cref="PersistentId"/>, an id not
/// set and an id two objects share are refused, naming every object holding it.
/// </summary>
public class WorldValidationTests
{
    private readonly List<Object> created = new();
    private WorldDefinition world;
    private WorldChunk chunk;
    private Transform props;

    /// <summary>
    /// Builds the world, the chunk's grid with its marker and an empty Props root.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        world = Track(WorldDefinition.Create("ValidationTest", new Vector2Int(2048, 0), new Vector2Int(2, 1), new[] { new Vector2Int(0, 0), new Vector2Int(1, 0) }));
        GameObject gridObject = Track(new GameObject("Grid", typeof(Grid)));
        gridObject.transform.position = new Vector3(2048f, 0f, 0f);
        gridObject.SetActive(false);
        chunk = gridObject.AddComponent<WorldChunk>();
        chunk.Configure(world, new Vector2Int(1, 0), null);
        gridObject.SetActive(true);
        props = Track(new GameObject(PropPlacementWindow.PropsRootName)).transform;
    }

    /// <summary>
    /// Destroys what the test created.
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
    }

    /// <summary>
    /// Objects on the chunk's cells pass; a spawn point, a mob spawn point and a walk-through prop one cell past its
    /// western border are each refused, by name and cell.
    /// </summary>
    [Test]
    public void ObjectsOutsideTheChunk_AreRefusedByName()
    {
        Place<SpawnPoint>("Inside spawn", 70.5f, 10.5f);
        Place<MobSpawnPoint>("Inside mob", 100.5f, 60.5f).gameObject.AddComponent<PersistentId>();
        PersistentIds.MakeUnique(new List<PersistentId>(Object.FindObjectsByType<PersistentId>(FindObjectsInactive.Include)));
        Assert.That(WorldValidation.ValidateChunk(chunk), Is.Empty);

        Place<SpawnPoint>("Outside spawn", 63.5f, 10.5f);
        MobSpawnPoint outsideMob = Place<MobSpawnPoint>("Outside mob", 100.5f, 64.5f);
        PersistentIds.SetId(outsideMob.gameObject.AddComponent<PersistentId>(), PersistentIds.NewId());
        GameObject plant = Track(new GameObject("Fern"));
        plant.transform.SetParent(props, false);
        plant.transform.position = new Vector3(2048f + 128.5f, 5f, 0f);

        List<string> problems = WorldValidation.ValidateChunk(chunk);

        Assert.That(problems.Count, Is.EqualTo(3), string.Join("\n", problems));
        Assert.That(problems.Exists(p => p.Contains("Outside spawn") && p.Contains("(63, 10)")), Is.True);
        Assert.That(problems.Exists(p => p.Contains("Outside mob") && p.Contains("(100, 64)")), Is.True);
        Assert.That(problems.Exists(p => p.Contains("Fern") && p.Contains("prop")), Is.True);
    }

    /// <summary>
    /// A blocking prop two cells wide standing on the chunk's western border blocks cells on both sides and is refused;
    /// moved one cell east it blocks only the chunk's cells and passes.
    /// </summary>
    [Test]
    public void ABlockingPropWhoseFootprintCrossesTheBorder_IsRefused()
    {
        GameObject rock = Track(new GameObject("Rock"));
        rock.transform.SetParent(props, false);
        BoxCollider2D footprint = rock.AddComponent<BoxCollider2D>();
        footprint.size = new Vector2(1.8f, 0.8f);
        footprint.offset = new Vector2(0f, 0.4f);
        rock.transform.position = new Vector3(2048f + 64f, 20f, 0f);

        List<string> problems = WorldValidation.ValidateChunk(chunk);
        Assert.That(problems.Count, Is.EqualTo(1));
        Assert.That(problems[0], Does.Contain("Rock").And.Contain("crossing its chunk's border").And.Contain("(63, 20)"));

        rock.transform.position = new Vector3(2048f + 65f, 20f, 0f);
        Assert.That(WorldValidation.ValidateChunk(chunk), Is.Empty);
        Assert.That(WorldValidation.FootprintInside(chunk.GetComponent<Grid>(), rock.transform.position, new Rect(footprint.offset - footprint.size * 0.5f, footprint.size), chunk.Cells, out BoundsInt blocked), Is.True);
        Assert.That(blocked.xMin, Is.EqualTo(64));
        Assert.That(blocked.xMax, Is.EqualTo(66));
    }

    /// <summary>
    /// A mob spawn point without a persistent id, and a persistent id never assigned, are refused.
    /// </summary>
    [Test]
    public void MissingIds_AreRefused()
    {
        Place<MobSpawnPoint>("No id", 80.5f, 10.5f);
        Place<MobSpawnPoint>("Empty id", 81.5f, 10.5f).gameObject.AddComponent<PersistentId>();

        List<string> problems = WorldValidation.ValidateChunk(chunk);

        Assert.That(problems.Count, Is.EqualTo(2), string.Join("\n", problems));
        Assert.That(problems.Exists(p => p.Contains("No id") && p.Contains("without a PersistentId")), Is.True);
        Assert.That(problems.Exists(p => p.Contains("Empty id") && p.Contains("has no id")), Is.True);
    }

    /// <summary>
    /// Two objects sharing an id are refused in one line naming both; distinct ids pass.
    /// </summary>
    [Test]
    public void DuplicateIds_AreRefused_NamingEveryHolder()
    {
        PersistentId first = Place<MobSpawnPoint>("First", 80.5f, 10.5f).gameObject.AddComponent<PersistentId>();
        PersistentId second = Place<MobSpawnPoint>("Second", 81.5f, 10.5f).gameObject.AddComponent<PersistentId>();
        PersistentId third = Place<MobSpawnPoint>("Third", 82.5f, 10.5f).gameObject.AddComponent<PersistentId>();
        PersistentIds.SetId(first, "shared");
        PersistentIds.SetId(second, "shared");
        PersistentIds.SetId(third, "own");

        List<string> problems = WorldValidation.ValidateIds(new[] { first, second, third });

        Assert.That(problems.Count, Is.EqualTo(1));
        Assert.That(problems[0], Does.Contain("shared").And.Contain("First").And.Contain("Second").And.Not.Contain("Third"));
        Assert.That(WorldValidation.ValidateIds(new[] { first, third }), Is.Empty);
    }

    /// <summary>
    /// An exit on the chunk's cells leading to a listed world's entry passes; one whose cells cross the border, one
    /// leading to a world not listed, one naming an entry its target lacks and one leading nowhere are each refused,
    /// by name.
    /// </summary>
    [Test]
    public void WorldExits_AreCheckedForTheirCellsTargetAndEntry()
    {
        WorldDefinition target = Track(WorldDefinition.Create("ExitTarget", new Vector2Int(4096, 0), Vector2Int.one, new[] { Vector2Int.zero }, new WorldDefinition.Entry("start", new Vector2(10.5f, 10.5f), true)));
        WorldDefinition other = Track(WorldDefinition.Create("NotListed", new Vector2Int(6144, 0), Vector2Int.one, new[] { Vector2Int.zero }, new WorldDefinition.Entry("start", new Vector2(10.5f, 10.5f), true)));
        WorldDefinition[] listed = { world, target };
        PlaceExit("Good exit", 70.5f, 10.5f, target, "start", Vector2Int.one);
        PlaceExit("Default exit", 71.5f, 10.5f, target, string.Empty, new Vector2Int(2, 2));
        Assert.That(WorldValidation.ValidateChunk(chunk, listed), Is.Empty);

        PlaceExit("Wide exit", 126.5f, 10.5f, target, "start", new Vector2Int(3, 1));
        PlaceExit("Unlisted exit", 80.5f, 10.5f, other, "start", Vector2Int.one);
        PlaceExit("Lost exit", 81.5f, 10.5f, target, "cellar", Vector2Int.one);
        PlaceExit("Blind exit", 82.5f, 10.5f, null, null, Vector2Int.one);

        List<string> problems = WorldValidation.ValidateChunk(chunk, listed);

        Assert.That(problems.Count, Is.EqualTo(4), string.Join("\n", problems));
        Assert.That(problems.Exists(p => p.Contains("Wide exit") && p.Contains("(126, 10) to (128, 10)")), Is.True);
        Assert.That(problems.Exists(p => p.Contains("Unlisted exit") && p.Contains("does not list")), Is.True);
        Assert.That(problems.Exists(p => p.Contains("Lost exit") && p.Contains("'cellar'")), Is.True);
        Assert.That(problems.Exists(p => p.Contains("Blind exit") && p.Contains("no world")), Is.True);
    }

    /// <summary>
    /// A world exit named <paramref name="name"/> with a persistent id at world-local (<paramref name="x"/>,
    /// <paramref name="y"/>), leading to <paramref name="target"/>'s entry <paramref name="entry"/>.
    /// </summary>
    private void PlaceExit(string name, float x, float y, WorldDefinition target, string entry, Vector2Int size)
    {
        WorldExit exit = Place<WorldExit>(name, x, y);
        PersistentIds.SetId(exit.GetComponent<PersistentId>(), PersistentIds.NewId());
        WorldAuthoring.ConfigureExit(exit, target, entry, size);
    }

    /// <summary>
    /// A new object named <paramref name="name"/> with a <typeparamref name="T"/> at world-local
    /// (<paramref name="x"/>, <paramref name="y"/>).
    /// </summary>
    private T Place<T>(string name, float x, float y) where T : Component
    {
        GameObject placed = Track(new GameObject(name));
        placed.transform.position = new Vector3(2048f + x, y, 0f);
        return placed.AddComponent<T>();
    }

    /// <summary>
    /// Records <paramref name="target"/> for tear down and returns it.
    /// </summary>
    private T Track<T>(T target) where T : Object
    {
        created.Add(target);
        return target;
    }
}
