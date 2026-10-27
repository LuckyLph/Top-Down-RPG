using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for <see cref="SharedTerrainArrays"/> and the renderers drawing with them (WorldScale.md's Phase 2): a style's
/// arrays hold its layer order, a slice per material, are built once for every user and destroyed with the last one, a
/// style without an order has none, disposing destroys them all and hands out no more, and a change to one of the
/// order's materials or to the style retires them for the next user; renderers given them draw with their style's arrays,
/// each layer at its material's slice in the lookup texture, a renderer drawing a material the order does not list builds
/// its own, a built renderer given them or taken off them rebuilds, and after a change to a material every renderer
/// rebuilds onto new arrays while the old are destroyed with the last renderer drawing with them. Each test builds its
/// own style and materials with <see cref="TestTerrainStyle"/>.
/// </summary>
public class SharedTerrainArraysTests
{
    private readonly List<Object> created = new();
    private TerrainMaterial[] materials;
    private TerrainStyle style;
    private SharedTerrainArrays shared;
    private Tile tile;

    /// <summary>
    /// Builds plain grass, gravel and stone materials, a style ordering them, the shared arrays and a tile.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        materials = TestTerrainStyle.PlainMaterials(created);
        style = TestTerrainStyle.Create(created, materials);
        shared = new SharedTerrainArrays();
        tile = ScriptableObject.CreateInstance<Tile>();
        created.Add(tile);
    }

    /// <summary>
    /// Destroys the grids first, so their renderers release what they hold, then the shared arrays and everything else
    /// the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        for (int i = created.Count - 1; i >= 0; i--)
        {
            if (created[i] is GameObject && created[i] != null)
            {
                Object.DestroyImmediate(created[i]);
            }
        }

        shared.Dispose();
        foreach (Object createdObject in created)
        {
            if (createdObject != null)
            {
                Object.DestroyImmediate(createdObject);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// Two users of a style get the same arrays, a slice per material of its layer order in that order, unreadable; the
    /// first release keeps them and the last destroys them, and the next user gets new ones.
    /// </summary>
    [Test]
    public void AStylesArrays_AreBuiltOnceForEveryUser_AndDestroyedWithTheLast()
    {
        TerrainMaterialArrays first = shared.Acquire(style);
        TerrainMaterialArrays second = shared.Acquire(style);

        Assert.That(second, Is.SameAs(first));
        Assert.That(shared.AliveCount, Is.EqualTo(1));
        Assert.That(first.Albedo.depth, Is.EqualTo(3));
        Assert.That(first.Field.depth, Is.EqualTo(3));
        Assert.That(first.Height.depth, Is.EqualTo(3));
        Assert.That(first.Albedo.isReadable, Is.False);
        for (int i = 0; i < materials.Length; i++)
        {
            Assert.That(first.SliceOf(materials[i]), Is.EqualTo(i));
        }

        Texture2DArray albedo = first.Albedo;
        shared.Release(first);
        Assert.That(albedo != null, Is.True, "A release keeps the arrays while another user holds them.");
        Assert.That(first.IsDisposed, Is.False);

        shared.Release(second);
        Assert.That(albedo == null, Is.True, "The last release destroys the arrays.");
        Assert.That(first.IsDisposed, Is.True);
        Assert.That(shared.AliveCount, Is.EqualTo(0));
        Assert.That(shared.Acquire(style), Is.Not.SameAs(first), "The next user gets new arrays.");
    }

    /// <summary>
    /// No style, and a style without a layer order, have no arrays to share.
    /// </summary>
    [Test]
    public void AStyleWithoutALayerOrder_HasNoneToShare()
    {
        TerrainStyle unordered = TestTerrainStyle.Create(created);

        Assert.That(shared.Acquire(unordered), Is.Null);
        Assert.That(shared.Acquire(null), Is.Null);
        Assert.That(shared.AliveCount, Is.EqualTo(0));
    }

    /// <summary>
    /// Disposing destroys every array still held, a later release does nothing, and no more are handed out.
    /// </summary>
    [Test]
    public void Disposing_DestroysEveryArray_AndHandsOutNoMore()
    {
        TerrainMaterialArrays arrays = shared.Acquire(style);
        Texture2DArray height = arrays.Height;

        shared.Dispose();

        Assert.That(height == null, Is.True);
        Assert.That(arrays.IsDisposed, Is.True);
        Assert.That(() => shared.Release(arrays), Throws.Nothing);
        Assert.That(shared.Acquire(style), Is.Null);
    }

    /// <summary>
    /// A change to one of the order's materials, and one to the style through the inspector, retire the arrays: the
    /// next user gets new ones, and the retired stay until their last user releases them.
    /// </summary>
    [Test]
    public void AChangeToAMaterialOrTheStyle_RetiresTheArrays_ForTheNextUser()
    {
        TerrainMaterialArrays before = shared.Acquire(style);

        materials[1].NotifyChanged();
        TerrainMaterialArrays after = shared.Acquire(style);

        Assert.That(after, Is.Not.SameAs(before));
        Assert.That(shared.AliveCount, Is.EqualTo(2));
        shared.Release(before);
        Assert.That(before.IsDisposed, Is.True, "The retired arrays go with their last user.");
        Assert.That(after.IsDisposed, Is.False);

        SerializedObject settings = new(style);
        settings.FindProperty("edgeNoise").floatValue = 2f;
        settings.ApplyModifiedPropertiesWithoutUndo();

        Assert.That(shared.Acquire(style), Is.Not.SameAs(after), "A change to the style retires them too.");
    }

    /// <summary>
    /// Two renderers given the shared arrays, one over grass and stone and one over gravel and stone, draw with the
    /// style's arrays, each layer at its material's slice in its lookup texture; disabling one keeps them for the other,
    /// and disabling both destroys them.
    /// </summary>
    [Test]
    public void RenderersGivenThem_DrawWithTheStylesArrays_EachLayerAtItsMaterialsSlice()
    {
        TerrainBlendRenderer grassAndStone = CreateRenderer("GrassAndStone", true, materials[0], materials[2]);
        TerrainBlendRenderer gravelAndStone = CreateRenderer("GravelAndStone", true, materials[1], materials[2]);

        Assert.That(grassAndStone.SharesArrays, Is.True);
        Assert.That(gravelAndStone.SharesArrays, Is.True);
        Assert.That(gravelAndStone.AlbedoArray, Is.SameAs(grassAndStone.AlbedoArray));
        Assert.That(grassAndStone.AlbedoArray.depth, Is.EqualTo(3));
        Assert.That(grassAndStone.RuntimeMaterial.GetTexture("_FieldArray"), Is.SameAs(grassAndStone.FieldArray));
        Assert.That(SlicesOf(grassAndStone), Is.EqualTo(new[] { 0, 2 }));
        Assert.That(SlicesOf(gravelAndStone), Is.EqualTo(new[] { 1, 2 }));

        Texture2DArray albedo = grassAndStone.AlbedoArray;
        grassAndStone.enabled = false;
        Assert.That(albedo != null, Is.True, "The arrays stay while a renderer draws with them.");
        Assert.That(gravelAndStone.AlbedoArray, Is.SameAs(albedo));

        gravelAndStone.enabled = false;
        Assert.That(albedo == null, Is.True, "The last renderer takes them with it.");
        Assert.That(shared.AliveCount, Is.EqualTo(0));
    }

    /// <summary>
    /// A renderer drawing a material the style's order does not list builds its own arrays, a slice per layer at its
    /// own index, and holds none of the shared ones.
    /// </summary>
    [Test]
    public void ARendererDrawingAMaterialOutsideTheOrder_BuildsItsOwnArrays()
    {
        TerrainMaterial sand = TestTerrainStyle.PlainMaterial("Sand", TerrainMaterial.CellMode.None, created);

        TerrainBlendRenderer renderer = CreateRenderer("WithSand", true, materials[0], sand);

        Assert.That(renderer.SharesArrays, Is.False);
        Assert.That(renderer.AlbedoArray.depth, Is.EqualTo(2));
        Assert.That(SlicesOf(renderer), Is.EqualTo(new[] { 0, 1 }));
        Assert.That(shared.AliveCount, Is.EqualTo(0));
    }

    /// <summary>
    /// A built renderer given the shared arrays rebuilds onto them, and taken off them builds its own again, releasing
    /// them.
    /// </summary>
    [Test]
    public void ABuiltRenderer_GivenThemOrTakenOffThem_Rebuilds()
    {
        TerrainBlendRenderer renderer = CreateRenderer("Ground", false, materials[0], materials[1], materials[2]);
        Assert.That(renderer.SharesArrays, Is.False);
        Texture2DArray own = renderer.AlbedoArray;

        renderer.Construct(shared);

        Assert.That(renderer.SharesArrays, Is.True);
        Assert.That(own == null, Is.True, "Its own arrays are destroyed.");
        Assert.That(shared.AliveCount, Is.EqualTo(1));

        renderer.Construct(null);

        Assert.That(renderer.SharesArrays, Is.False);
        Assert.That(renderer.AlbedoArray, Is.Not.Null);
        Assert.That(shared.AliveCount, Is.EqualTo(0));
    }

    /// <summary>
    /// After a change to a material both renderers rebuild: the first onto new arrays while the second still draws with
    /// the old, and once the second has rebuilt both draw with the new ones and the old are destroyed.
    /// </summary>
    [Test]
    public void AfterAChangeToAMaterial_EveryRendererRebuildsOntoNewArrays()
    {
        TerrainBlendRenderer first = CreateRenderer("First", true, materials[0], materials[2]);
        TerrainBlendRenderer second = CreateRenderer("Second", true, materials[1], materials[2]);
        Texture2DArray before = first.AlbedoArray;

        materials[2].NotifyChanged();
        Assert.That(first.IsRebuildScheduled, Is.True);
        Assert.That(second.IsRebuildScheduled, Is.True);
        first.RunScheduledRebuild();

        Assert.That(first.AlbedoArray, Is.Not.SameAs(before));
        Assert.That(second.AlbedoArray, Is.SameAs(before));
        Assert.That(shared.AliveCount, Is.EqualTo(2));

        second.RunScheduledRebuild();

        Assert.That(second.AlbedoArray, Is.SameAs(first.AlbedoArray));
        Assert.That(before == null, Is.True, "The old arrays go with the last renderer drawing with them.");
        Assert.That(shared.AliveCount, Is.EqualTo(1));
    }

    /// <summary>
    /// An active grid named <paramref name="gridName"/> with a renderer drawing a layer per material of
    /// <paramref name="layerMaterials"/>, each painted over a few cells, given the shared arrays before it first builds
    /// when <paramref name="share"/> is set.
    /// </summary>
    private TerrainBlendRenderer CreateRenderer(string gridName, bool share, params TerrainMaterial[] layerMaterials)
    {
        GameObject gridObject = new(gridName, typeof(Grid));
        created.Add(gridObject);
        gridObject.SetActive(false);
        TerrainBlendRenderer.Layer[] layers = new TerrainBlendRenderer.Layer[layerMaterials.Length];
        for (int i = 0; i < layers.Length; i++)
        {
            Tilemap tilemap = new GameObject(layerMaterials[i].name, typeof(Tilemap)).GetComponent<Tilemap>();
            tilemap.transform.SetParent(gridObject.transform, false);
            for (int x = 0; x < 4; x++)
            {
                tilemap.SetTile(new Vector3Int(x + i, 1, 0), tile);
            }

            layers[i] = new TerrainBlendRenderer.Layer(tilemap, layerMaterials[i]);
        }

        TerrainBlendRenderer renderer = gridObject.AddComponent<TerrainBlendRenderer>();
        if (share)
        {
            renderer.Construct(shared);
        }

        renderer.Configure(style, layers);
        gridObject.SetActive(true);
        Assert.That(renderer.ControlMap, Is.Not.Null, $"{gridName} builds.");
        return renderer;
    }

    /// <summary>
    /// The slice of the arrays each layer of <paramref name="renderer"/> draws, from its lookup texture.
    /// </summary>
    private static int[] SlicesOf(TerrainBlendRenderer renderer)
    {
        int[] slices = new int[renderer.Layers.Count];
        for (int i = 0; i < slices.Length; i++)
        {
            slices[i] = Mathf.RoundToInt(renderer.Lut.GetPixel(TerrainBlendLut.LayerSliceColumn, TerrainBlendLut.LayerRow(i)).r);
        }

        return slices;
    }
}
