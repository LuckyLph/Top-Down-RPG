using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for <see cref="EntrySlots"/>: players arriving at one place of a world take the first slot no other player of
/// that world stands on, whatever their order in the registry.
/// </summary>
public class EntrySlotsTests
{
    private const float Spacing = 0.6f;

    private readonly List<Object> created = new();
    private PlayerRegistry players;
    private PlayerWorlds playerWorlds;
    private WorldDefinition world;
    private WorldDefinition otherWorld;

    /// <summary>
    /// Builds an empty registry, its world records and two worlds.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        players = new PlayerRegistry();
        playerWorlds = new PlayerWorlds(players);
        world = Track(WorldDefinition.Create("Slots", new Vector2Int(2048, 0), Vector2Int.one, new[] { Vector2Int.zero }));
        otherWorld = Track(WorldDefinition.Create("SlotsOther", new Vector2Int(4096, 0), Vector2Int.one, new[] { Vector2Int.zero }));
    }

    /// <summary>
    /// Destroys what the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        playerWorlds.Dispose();
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
    /// Slot 0 is the place itself, then alternately right and left, one spacing further out every two slots.
    /// </summary>
    [Test]
    public void Slots_AlternateRightAndLeftOfThePlace()
    {
        Vector2 place = new(2060.5f, 40.5f);
        Assert.That(EntrySlots.SlotPosition(place, 0, Spacing), Is.EqualTo(place));
        Assert.That(EntrySlots.SlotPosition(place, 1, Spacing), Is.EqualTo(place + new Vector2(Spacing, 0f)));
        Assert.That(EntrySlots.SlotPosition(place, 2, Spacing), Is.EqualTo(place - new Vector2(Spacing, 0f)));
        Assert.That(EntrySlots.SlotPosition(place, 3, Spacing), Is.EqualTo(place + new Vector2(2f * Spacing, 0f)));
    }

    /// <summary>
    /// The first player takes the place; the next ones take the first free slot, skipping the ones taken however the
    /// players were registered; a player of another world, or the arriving player itself, takes none.
    /// </summary>
    [Test]
    public void FreeSlot_IsTheFirstNoOtherPlayerOfTheWorldStandsOn()
    {
        Vector2 place = new(2060.5f, 40.5f);
        PlayerHandle arriving = Player("Arriving", place, world);
        Assert.That(Free(place, arriving), Is.EqualTo(place), "The arriving player does not count.");

        Player("Elsewhere", place, otherWorld);
        Assert.That(Free(place, arriving), Is.EqualTo(place), "A player in another world does not count.");

        Player("Second slot", EntrySlots.SlotPosition(place, 1, Spacing), world);
        Assert.That(Free(place, arriving), Is.EqualTo(place), "The place itself is still free.");

        Player("On the place", place + new Vector2(0.1f, 0f), world);
        Assert.That(Free(place, arriving), Is.EqualTo(EntrySlots.SlotPosition(place, 2, Spacing)));
        Assert.That(EntrySlots.FreeSlot(place, 0f, world, players, playerWorlds, arriving), Is.EqualTo(place), "No spacing, no slots.");
    }

    private Vector2 Free(Vector2 place, PlayerHandle arriving)
    {
        return EntrySlots.FreeSlot(place, Spacing, world, players, playerWorlds, arriving);
    }

    private PlayerHandle Player(string name, Vector2 position, WorldDefinition playerWorld)
    {
        GameObject body = Track(new GameObject(name));
        body.transform.position = position;
        PlayerHandle handle = new(body.transform, null);
        players.Add(handle);
        playerWorlds.Set(handle, playerWorld);
        return handle;
    }

    private T Track<T>(T item) where T : Object
    {
        created.Add(item);
        return item;
    }
}
