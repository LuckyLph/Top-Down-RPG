using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for <see cref="ChunkCoords"/>: which chunk holds a cell, a chunk's cells, and which chunks a rectangle of
/// cells overlaps.
/// </summary>
public class ChunkCoordsTests
{
    /// <summary>
    /// Cells map to chunks by rounding down, below zero too, and a position to the cell it lies in.
    /// </summary>
    [Test]
    public void ChunkOf_RoundsDown_BelowZeroToo()
    {
        Assert.That(ChunkCoords.ChunkOf(new Vector2Int(0, 63)), Is.EqualTo(new Vector2Int(0, 0)));
        Assert.That(ChunkCoords.ChunkOf(new Vector2Int(64, 127)), Is.EqualTo(new Vector2Int(1, 1)));
        Assert.That(ChunkCoords.ChunkOf(new Vector2Int(-1, -64)), Is.EqualTo(new Vector2Int(-1, -1)));
        Assert.That(ChunkCoords.ChunkOf(new Vector2Int(-65, 0)), Is.EqualTo(new Vector2Int(-2, 0)));
        Assert.That(ChunkCoords.CellOf(new Vector2(3.99f, -0.01f)), Is.EqualTo(new Vector2Int(3, -1)));
    }

    /// <summary>
    /// A chunk's bounds are its 64 x 64 cells, and the squared distance to them is 0 inside and grows outside.
    /// </summary>
    [Test]
    public void Bounds_AndDistance_FollowTheChunksCells()
    {
        Assert.That(ChunkCoords.Bounds(new Vector2Int(1, 2)), Is.EqualTo(new RectInt(64, 128, 64, 64)));
        Assert.That(ChunkCoords.SquaredDistance(new Vector2Int(70, 130), new Vector2Int(1, 2)), Is.EqualTo(0));
        Assert.That(ChunkCoords.SquaredDistance(new Vector2Int(60, 130), new Vector2Int(1, 2)), Is.EqualTo(16));
        Assert.That(ChunkCoords.SquaredDistance(new Vector2Int(130, 200), new Vector2Int(1, 2)), Is.EqualTo(3 * 3 + 9 * 9));
    }

    /// <summary>
    /// A rectangle adds every chunk it overlaps inside the world's size, and none outside it.
    /// </summary>
    [Test]
    public void AddOverlapping_AddsTheChunksInsideTheWorld()
    {
        HashSet<Vector2Int> chunks = new();
        ChunkCoords.AddOverlapping(new RectInt(-10, 60, 80, 10), new Vector2Int(2, 2), chunks);

        Assert.That(chunks, Is.EquivalentTo(new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) }));
        Assert.That(ChunkCoords.Overlaps(new RectInt(60, 0, 4, 4), new Vector2Int(1, 0)), Is.False);
        Assert.That(ChunkCoords.Overlaps(new RectInt(60, 0, 5, 4), new Vector2Int(1, 0)), Is.True);
    }

    /// <summary>
    /// The cells around a centre reach the extents on every side.
    /// </summary>
    [Test]
    public void Around_ReachesTheExtentsOnEverySide()
    {
        Assert.That(ChunkCoords.Around(new Vector2Int(10, 20), new Vector2Int(3, 2)), Is.EqualTo(new RectInt(7, 18, 7, 5)));
    }
}
