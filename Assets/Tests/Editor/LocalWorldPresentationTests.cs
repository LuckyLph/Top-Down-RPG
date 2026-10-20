using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for <see cref="LocalWorldPresentation.ShouldDraw"/>: a chunk draws its ground only in the local player's
/// world, once the interest rectangle overlaps it, and keeps drawing until it leaves the wider rectangle of the unload
/// hysteresis, so walking back and forth over the edge of the interest rectangle rebuilds nothing; and
/// <see cref="LocalWorldPresentation.PresentedWorld"/>: the world shown follows the local player's world once the player
/// stands in it, so a trip keeps the world being left on screen until the player arrives.
/// </summary>
public class LocalWorldPresentationTests
{
    private static readonly RectInt Draw = new(10, 10, 40, 40);
    private static readonly RectInt Keep = new(0, 0, 70, 70);

    /// <summary>
    /// A chunk the interest rectangle overlaps draws; one only the wider rectangle overlaps keeps drawing if it was and
    /// stays hidden if it was not; one outside both, or in another world, never draws.
    /// </summary>
    [Test]
    public void AChunkDraws_InTheLocalWorld_FromTheInterestRectangle_UntilItLeavesTheWiderOne()
    {
        Vector2Int overlapped = new(0, 0);
        Vector2Int onlyKept = new(1, 1);
        Vector2Int outside = new(2, 0);
        Assert.That(ChunkCoords.Overlaps(Draw, onlyKept), Is.False);
        Assert.That(ChunkCoords.Overlaps(Keep, onlyKept), Is.True);

        Assert.That(LocalWorldPresentation.ShouldDraw(true, false, overlapped, Draw, Keep), Is.True);
        Assert.That(LocalWorldPresentation.ShouldDraw(true, true, onlyKept, Draw, Keep), Is.True, "A drawn chunk keeps drawing within the wider rectangle.");
        Assert.That(LocalWorldPresentation.ShouldDraw(true, false, onlyKept, Draw, Keep), Is.False, "A hidden chunk stays hidden until the interest rectangle reaches it.");
        Assert.That(LocalWorldPresentation.ShouldDraw(true, true, outside, Draw, Keep), Is.False);
        Assert.That(LocalWorldPresentation.ShouldDraw(false, true, overlapped, Draw, Keep), Is.False, "Another world's chunk never draws.");
    }

    /// <summary>
    /// A player put in another world but still standing in the one shown keeps that one shown; once on the other
    /// world's cells, or with nothing shown yet, its own world is shown; a player in no world shows none.
    /// </summary>
    [Test]
    public void ThePresentedWorld_FollowsThePlayerOnceItStandsInItsWorld()
    {
        WorldDefinition outside = WorldDefinition.Create("Outside", new Vector2Int(2048, 0), Vector2Int.one, new[] { Vector2Int.zero });
        WorldDefinition inside = WorldDefinition.Create("Inside", new Vector2Int(4096, 0), Vector2Int.one, new[] { Vector2Int.zero });
        try
        {
            Vector2 atTheDoor = new(2048f + 30.5f, 30.5f);
            Vector2 atTheEntry = new(4096f + 32f, 32f);
            Assert.That(LocalWorldPresentation.PresentedWorld(inside, atTheDoor, outside), Is.SameAs(outside), "Until it arrives, the world left stays shown.");
            Assert.That(LocalWorldPresentation.PresentedWorld(inside, atTheEntry, outside), Is.SameAs(inside));
            Assert.That(LocalWorldPresentation.PresentedWorld(inside, atTheDoor, null), Is.SameAs(inside));
            Assert.That(LocalWorldPresentation.PresentedWorld(outside, atTheDoor, outside), Is.SameAs(outside));
            Assert.That(LocalWorldPresentation.PresentedWorld(null, atTheDoor, outside), Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(outside);
            Object.DestroyImmediate(inside);
        }
    }
}
