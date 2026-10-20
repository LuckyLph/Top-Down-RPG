using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for <see cref="WorldLayout"/>: the first free origin skips the slot at the origin and the slots worlds take,
/// fits a full-size world, and gives up when the layout is full; a world too close to another, or leaving the layout,
/// is refused with a reason naming it, and a gap of exactly <see cref="WorldLayout.MinimumGap"/> is allowed; validating
/// a layout names each world that breaks it. Each test builds its own unsaved worlds.
/// </summary>
public class WorldLayoutTests
{
    private readonly List<WorldDefinition> created = new();

    /// <summary>
    /// Destroys the worlds the test built.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (WorldDefinition world in created)
        {
            Object.DestroyImmediate(world);
        }

        created.Clear();
    }

    /// <summary>
    /// With no world, the first free origin is the slot right of the origin; with the Sandbox's and its interior's
    /// slots taken, the next one along; a full 16 x 16 world fits there too, 1024 units from its neighbour.
    /// </summary>
    [Test]
    public void TheFirstFreeOrigin_SkipsTheOriginAndTakenSlots()
    {
        Assert.That(WorldLayout.TryFindFreeOrigin(new Vector2Int(2, 2), new WorldDefinition[0], out Vector2Int origin), Is.True);
        Assert.That(origin, Is.EqualTo(new Vector2Int(2048, 0)));

        List<WorldDefinition> worlds = new() { World("A", new Vector2Int(2048, 0), new Vector2Int(2, 2)), World("B", new Vector2Int(4096, 0), Vector2Int.one) };
        Assert.That(WorldLayout.TryFindFreeOrigin(new Vector2Int(2, 2), worlds, out origin), Is.True);
        Assert.That(origin, Is.EqualTo(new Vector2Int(6144, 0)));

        Assert.That(WorldLayout.TryFindFreeOrigin(new Vector2Int(16, 16), worlds, out origin), Is.True);
        Assert.That(origin, Is.EqualTo(new Vector2Int(6144, 0)));
        Assert.That(WorldLayout.Problem(origin, new Vector2Int(16, 16), worlds), Is.Null);
    }

    /// <summary>
    /// A world placed off the slots in between two others pushes the next free origin past them.
    /// </summary>
    [Test]
    public void TheFirstFreeOrigin_StaysClearOfAWorldOffTheSlots()
    {
        List<WorldDefinition> worlds = new() { World("Odd", new Vector2Int(2600, 0), Vector2Int.one) };

        Assert.That(WorldLayout.TryFindFreeOrigin(Vector2Int.one, worlds, out Vector2Int origin), Is.True);
        Assert.That(origin, Is.EqualTo(new Vector2Int(4096, 0)), "(2048, 0) is 488 units from it.");
    }

    /// <summary>
    /// When every slot is taken by a full world, no origin is free.
    /// </summary>
    [Test]
    public void TheFirstFreeOrigin_FailsWhenTheLayoutIsFull()
    {
        List<WorldDefinition> worlds = new();
        int slots = WorldLayout.Extent / WorldLayout.SlotSize;
        for (int y = 0; y < slots; y++)
        {
            for (int x = 0; x < slots; x++)
            {
                worlds.Add(World($"W{x}_{y}", new Vector2Int(x * WorldLayout.SlotSize, y * WorldLayout.SlotSize), new Vector2Int(16, 16)));
            }
        }

        Assert.That(WorldLayout.TryFindFreeOrigin(Vector2Int.one, worlds, out _), Is.False);
    }

    /// <summary>
    /// A world closer than 1024 units to another is refused, naming the other; exactly 1024 units apart is allowed,
    /// diagonally measured as a distance; a world reaching past the layout's extent is refused.
    /// </summary>
    [Test]
    public void AnOrigin_TooCloseOrOutsideTheLayout_IsRefused()
    {
        List<WorldDefinition> worlds = new() { World("Home", new Vector2Int(2048, 0), new Vector2Int(2, 2)) };

        Assert.That(WorldLayout.Problem(new Vector2Int(2048 + 128 + 1000, 0), Vector2Int.one, worlds), Does.Contain("Home"));
        Assert.That(WorldLayout.Problem(new Vector2Int(2048 + 128 + 1024, 0), Vector2Int.one, worlds), Is.Null);
        Assert.That(WorldLayout.Problem(new Vector2Int(2048 + 128 + 800, 128 + 800), Vector2Int.one, worlds), Is.Null, "About 1131 units apart diagonally.");
        Assert.That(WorldLayout.Problem(new Vector2Int(2048 + 128 + 700, 128 + 700), Vector2Int.one, worlds), Does.Contain("Home"));
        Assert.That(WorldLayout.Problem(new Vector2Int(16384 - 32, 0), Vector2Int.one, worlds), Does.Contain("leaves the layout"));
        Assert.That(WorldLayout.Problem(new Vector2Int(2048, 0), new Vector2Int(2, 2), worlds, worlds[0]), Is.Null, "A world is not compared with itself.");
    }

    /// <summary>
    /// Validating a layout names the pair that comes too close and the world that leaves it, and nothing else.
    /// </summary>
    [Test]
    public void Validate_NamesEachWorldThatBreaksTheLayout()
    {
        List<WorldDefinition> worlds = new()
        {
            World("A", new Vector2Int(2048, 0), new Vector2Int(2, 2)),
            World("B", new Vector2Int(2600, 0), Vector2Int.one),
            World("C", new Vector2Int(8192, 0), Vector2Int.one),
            World("Far", new Vector2Int(20000, 0), Vector2Int.one),
        };

        List<string> problems = WorldLayout.Validate(worlds);

        Assert.That(problems.Count, Is.EqualTo(2));
        Assert.That(problems[0], Does.StartWith("World A").And.Contain("B's"));
        Assert.That(problems[1], Does.StartWith("World Far").And.Contain("leaves the layout"));
    }

    /// <summary>
    /// An unsaved world named <paramref name="name"/>, destroyed at tear down.
    /// </summary>
    private WorldDefinition World(string name, Vector2Int origin, Vector2Int size)
    {
        WorldDefinition world = WorldDefinition.Create(name, origin, size, new Vector2Int[0]);
        created.Add(world);
        return world;
    }
}
