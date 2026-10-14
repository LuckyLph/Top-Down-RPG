using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for <see cref="TerrainChunkSet"/>, the chunk quads a terrain renderer draws with: fitting bounds creates one
/// chunk per chunk-grid square they touch, as hidden, never saved children on the given sorting, each quad clipped to the
/// bounds; fitting smaller bounds destroys the chunks they no longer reach, empty bounds destroy them all, and clearing
/// destroys every chunk object and mesh.
/// </summary>
public class TerrainChunkSetTests
{
    private const int ChunkSize = 8;
    private GameObject owner;
    private TerrainChunkSet chunks;

    /// <summary>
    /// Creates a grid object to own the chunks and an empty set of 8-cell chunks.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        owner = new GameObject("ChunkOwner", typeof(Grid));
        chunks = new TerrainChunkSet(owner, owner.GetComponent<Grid>(), ChunkSize);
    }

    /// <summary>
    /// Clears the chunks and destroys the owner.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        chunks.Clear();
        Object.DestroyImmediate(owner);
    }

    /// <summary>
    /// Bounds from (-3, -2) over 20 x 5 cells touch chunk columns -1 to 2 and rows -1 to 0: eight chunks, each a hidden,
    /// never saved child of the owner on the given sorting layer and order, its quad inside the bounds.
    /// </summary>
    [Test]
    public void Fitting_CreatesAChunkPerSquareTheBoundsTouch_ClippedToThem()
    {
        int sortingLayerId = SortingLayer.NameToID("Terrain");
        BoundsInt bounds = new(new Vector3Int(-3, -2, 0), new Vector3Int(20, 5, 1));

        chunks.Fit(bounds, false, null, sortingLayerId, 3);

        Assert.That(chunks.Renderers.Count, Is.EqualTo(8));
        foreach (MeshRenderer chunk in chunks.Renderers)
        {
            Assert.That(chunk.transform.parent, Is.EqualTo(owner.transform));
            Assert.That(chunk.gameObject.hideFlags, Is.EqualTo(HideFlags.HideAndDontSave));
            Assert.That(chunk.sortingLayerID, Is.EqualTo(sortingLayerId));
            Assert.That(chunk.sortingOrder, Is.EqualTo(3));
            Bounds quad = chunk.GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.That(quad.min.x, Is.GreaterThanOrEqualTo(-3f));
            Assert.That(quad.max.x, Is.LessThanOrEqualTo(17f));
            Assert.That(quad.min.y, Is.GreaterThanOrEqualTo(-2f));
            Assert.That(quad.max.y, Is.LessThanOrEqualTo(3f));
        }
    }

    /// <summary>
    /// Fitting bounds inside one chunk square destroys the other chunks' objects and meshes, and empty bounds destroy the
    /// last one.
    /// </summary>
    [Test]
    public void FittingSmallerOrEmptyBounds_DestroysTheChunksNoLongerReached()
    {
        chunks.Fit(new BoundsInt(new Vector3Int(-3, -2, 0), new Vector3Int(20, 5, 1)), false, null, 0, 0);
        List<MeshRenderer> before = new(chunks.Renderers);

        chunks.Fit(new BoundsInt(new Vector3Int(1, 1, 0), new Vector3Int(4, 2, 1)), false, null, 0, 0);

        Assert.That(chunks.Renderers.Count, Is.EqualTo(1));
        Assert.That(before.FindAll(chunk => chunk == null).Count, Is.EqualTo(7));
        MeshRenderer kept = chunks.Renderers[0];
        Mesh keptMesh = kept.GetComponent<MeshFilter>().sharedMesh;

        chunks.Fit(new BoundsInt(new Vector3Int(1, 1, 0), new Vector3Int(4, 2, 1)), true, null, 0, 0);

        Assert.That(chunks.Renderers, Is.Empty);
        Assert.That(kept == null, Is.True);
        Assert.That(keptMesh == null, Is.True);
    }

    /// <summary>
    /// Clearing destroys every chunk object and mesh.
    /// </summary>
    [Test]
    public void Clearing_DestroysEveryChunk()
    {
        chunks.Fit(new BoundsInt(new Vector3Int(0, 0, 0), new Vector3Int(16, 8, 1)), false, null, 0, 0);
        List<MeshRenderer> before = new(chunks.Renderers);
        List<Mesh> meshes = before.ConvertAll(chunk => chunk.GetComponent<MeshFilter>().sharedMesh);

        chunks.Clear();

        Assert.That(before.Count, Is.EqualTo(2));
        Assert.That(chunks.Renderers, Is.Empty);
        Assert.That(before.TrueForAll(chunk => chunk == null), Is.True);
        Assert.That(meshes.TrueForAll(mesh => mesh == null), Is.True);
        Assert.That(owner.transform.childCount, Is.Zero);
    }
}
