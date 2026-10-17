using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.TestTools.Constraints;
using UnityEngine.Tilemaps;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for <see cref="TerrainControlMap"/>: layer bits and bounds read from the tilemaps, patches inside the bounds,
/// growth outside them or in the margin, one upload per batch of changes, preview bits kept apart from the real ones,
/// all 16 layers' bits round-tripping through a narrow map's 16-bit texture while a 17th layer is refused, and all 32
/// layers' bits through a wide map's RGBA32 texture while a 33rd is refused.
/// </summary>
public class TerrainControlMapTests
{
    private readonly List<Object> createdObjects = new();
    private readonly List<TerrainControlMap> maps = new();
    private Tilemap[] layers;
    private Tile tile;

    /// <summary>
    /// Builds a grid with three empty layer tilemaps (grass, gravel, stone) and a plain tile to paint them with.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        GameObject root = Track(new GameObject("ControlMapGrid", typeof(Grid)));
        tile = Track(ScriptableObject.CreateInstance<Tile>());
        layers = new Tilemap[3];
        for (int i = 0; i < layers.Length; i++)
        {
            GameObject layerObject = new($"Layer{i}", typeof(Tilemap));
            layerObject.transform.SetParent(root.transform);
            layers[i] = layerObject.GetComponent<Tilemap>();
        }
    }

    /// <summary>
    /// Disposes the maps and destroys the grid and tile the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (TerrainControlMap map in maps)
        {
            map.Dispose();
        }

        maps.Clear();
        foreach (Object created in createdObjects)
        {
            if (created != null)
            {
                Object.DestroyImmediate(created);
            }
        }

        createdObjects.Clear();
    }

    /// <summary>
    /// Each cell holds one bit per layer filled there, and the texture holds the same bits once applied.
    /// </summary>
    [Test]
    public void Cells_HoldABitPerFilledLayer()
    {
        Paint(0, new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(2, 2, 0));
        Paint(1, new Vector3Int(1, 0, 0), new Vector3Int(2, 2, 0));
        Paint(2, new Vector3Int(2, 2, 0));
        TerrainControlMap map = CreateMap();
        map.Apply();

        Assert.That(map.GetCell(new Vector3Int(0, 0, 0)), Is.EqualTo(1));
        Assert.That(map.GetCell(new Vector3Int(1, 0, 0)), Is.EqualTo(3));
        Assert.That(map.GetCell(new Vector3Int(2, 2, 0)), Is.EqualTo(7));
        Assert.That(map.GetCell(new Vector3Int(1, 1, 0)), Is.EqualTo(0));
        Assert.That(Texel(map, new Vector3Int(1, 0, 0)), Is.EqualTo(3));
        Assert.That(Texel(map, new Vector3Int(2, 2, 0)), Is.EqualTo(7));
    }

    /// <summary>
    /// With <see cref="TerrainControlMap.NarrowLayers"/> layers every bit round-trips through a narrow map's cells, its
    /// preview and its 16-bit texture: a cell per layer holds that layer's bit alone, a cell with every layer holds all
    /// 16, and a preview on the top layer sets and clears bit 15 alone. A narrow map of more layers is refused.
    /// </summary>
    [Test]
    public void SixteenLayers_RoundTripEveryBit_AndASeventeenthIsRefused()
    {
        Tilemap[] many = new Tilemap[TerrainControlMap.NarrowLayers];
        for (int i = 0; i < many.Length; i++)
        {
            GameObject layerObject = new($"Layer{i}", typeof(Tilemap));
            layerObject.transform.SetParent(layers[0].transform.parent);
            many[i] = layerObject.GetComponent<Tilemap>();
            many[i].SetTile(new Vector3Int(i, 0, 0), tile);
            many[i].SetTile(new Vector3Int(0, 2, 0), tile);
        }

        TerrainControlMap map = new(many);
        maps.Add(map);
        Vector3Int top = new(3, 4, 0);
        map.SetPreview(many.Length - 1, new[] { top }, true);
        map.Apply();

        Assert.That(map.LayerCount, Is.EqualTo(16));
        for (int i = 0; i < many.Length; i++)
        {
            Vector3Int cell = new(i, 0, 0);
            Assert.That(map.GetCell(cell), Is.EqualTo(1 << i), $"layer {i}");
            Assert.That(Texel(map, cell), Is.EqualTo(1 << i), $"texel of layer {i}");
        }

        Assert.That(map.GetCell(new Vector3Int(0, 2, 0)), Is.EqualTo(0xFFFF));
        Assert.That(Texel(map, new Vector3Int(0, 2, 0)), Is.EqualTo(0xFFFF));
        Assert.That(map.GetCell(top), Is.EqualTo(0));
        Assert.That(map.GetShownCell(top), Is.EqualTo(0x8000));
        Assert.That(Texel(map, top), Is.EqualTo(0x8000));

        map.SetPreview(many.Length - 1, new[] { new Vector3Int(0, 2, 0) }, false);
        map.Apply();
        Assert.That(Texel(map, new Vector3Int(0, 2, 0)), Is.EqualTo(0x7FFF));
        Assert.That(map.GetCell(new Vector3Int(0, 2, 0)), Is.EqualTo(0xFFFF));

        Tilemap[] tooMany = new Tilemap[TerrainControlMap.NarrowLayers + 1];
        many.CopyTo(tooMany, 0);
        Assert.That(() => new TerrainControlMap(tooMany), Throws.ArgumentException);
    }

    /// <summary>
    /// With <see cref="TerrainControlMap.WideLayers"/> layers every bit round-trips through a wide map's cells, its
    /// preview and its RGBA32 texture, bit i in byte i / 8 from red up: a cell per layer holds that layer's bit alone, in
    /// its own channel, a cell with every layer holds all 32 (every channel full), and a preview on the top layer sets
    /// and clears bit 31 alone, in alpha. A wide map of more layers is refused.
    /// </summary>
    [Test]
    public void AWideMap_RoundTripsEveryBitOfAFullCell_AndAThirtyThirdLayerIsRefused()
    {
        Tilemap[] many = new Tilemap[TerrainControlMap.WideLayers];
        for (int i = 0; i < many.Length; i++)
        {
            GameObject layerObject = new($"Layer{i}", typeof(Tilemap));
            layerObject.transform.SetParent(layers[0].transform.parent);
            many[i] = layerObject.GetComponent<Tilemap>();
            many[i].SetTile(new Vector3Int(i, 0, 0), tile);
            many[i].SetTile(new Vector3Int(0, 2, 0), tile);
        }

        TerrainControlMap map = new(many, true);
        maps.Add(map);
        Vector3Int full = new(0, 2, 0);
        Vector3Int top = new(3, 4, 0);
        map.SetPreview(many.Length - 1, new[] { top }, true);
        map.Apply();

        Assert.That(map.IsWide, Is.True);
        Assert.That(map.LayerCount, Is.EqualTo(32));
        Assert.That(map.Texture.graphicsFormat, Is.EqualTo(TerrainControlMap.FormatOf(true)));
        Assert.That(map.Texture.graphicsFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
        for (int i = 0; i < many.Length; i++)
        {
            Vector3Int cell = new(i, 0, 0);
            Assert.That(map.GetCell(cell), Is.EqualTo(1u << i), $"layer {i}");
            Assert.That(Texel(map, cell), Is.EqualTo(1u << i), $"texel of layer {i}");
            Color32 channels = Channels(map, cell);
            byte[] expected = new byte[4];
            expected[i / 8] = (byte)(1 << (i % 8));
            Assert.That(new[] { channels.r, channels.g, channels.b, channels.a }, Is.EqualTo(expected), $"channels of layer {i}");
        }

        Assert.That(map.GetCell(full), Is.EqualTo(0xFFFFFFFFu));
        Assert.That(Texel(map, full), Is.EqualTo(0xFFFFFFFFu));
        Assert.That(Channels(map, full), Is.EqualTo(new Color32(255, 255, 255, 255)));
        Assert.That(map.GetCell(top), Is.EqualTo(0u));
        Assert.That(map.GetShownCell(top), Is.EqualTo(0x80000000u));
        Assert.That(Texel(map, top), Is.EqualTo(0x80000000u));
        Assert.That(Channels(map, top), Is.EqualTo(new Color32(0, 0, 0, 128)));

        map.ClearPreview();
        map.SetPreview(many.Length - 1, new[] { full }, false);
        map.Apply();
        Assert.That(Texel(map, full), Is.EqualTo(0x7FFFFFFFu));
        Assert.That(Channels(map, full), Is.EqualTo(new Color32(255, 255, 255, 127)));
        Assert.That(map.GetShownCell(full), Is.EqualTo(0x7FFFFFFFu));
        Assert.That(map.GetCell(full), Is.EqualTo(0xFFFFFFFFu));
        Assert.That(Texel(map, top), Is.Zero, "Clearing the preview shows the real, empty cell again.");

        Tilemap[] tooMany = new Tilemap[TerrainControlMap.WideLayers + 1];
        many.CopyTo(tooMany, 0);
        Assert.That(() => new TerrainControlMap(tooMany, true), Throws.ArgumentException);
    }

    /// <summary>
    /// The bounds are the union of every layer's filled cells plus the margin, even when a tilemap's own bounds still
    /// cover cells emptied since.
    /// </summary>
    [Test]
    public void Bounds_AreTheLayersFilledCellsPlusTheMargin()
    {
        Paint(0, new Vector3Int(-2, -1, 0), new Vector3Int(0, 0, 0));
        Paint(2, new Vector3Int(3, 4, 0), new Vector3Int(9, 9, 0));
        layers[2].SetTile(new Vector3Int(9, 9, 0), null);

        TerrainControlMap map = CreateMap();

        int margin = TerrainControlMap.Margin;
        Assert.That(map.Bounds.min, Is.EqualTo(new Vector3Int(-2 - margin, -1 - margin, 0)));
        Assert.That(map.Bounds.size, Is.EqualTo(new Vector3Int(6 + 2 * margin, 6 + 2 * margin, 1)));
        map.Apply();
        Assert.That(map.Texture.width, Is.EqualTo(map.Bounds.size.x));
        Assert.That(map.Texture.height, Is.EqualTo(map.Bounds.size.y));
        Assert.That(map.Texture.format, Is.EqualTo(TextureFormat.R16));
        Assert.That(map.Texture.filterMode, Is.EqualTo(FilterMode.Point));
    }

    /// <summary>
    /// With no filled cell the map is empty and its texture is a single empty texel.
    /// </summary>
    [Test]
    public void NoFilledCell_GivesAnEmptyMap()
    {
        TerrainControlMap map = CreateMap();
        map.Apply();

        Assert.That(map.IsEmpty, Is.True);
        Assert.That(map.Texture.width, Is.EqualTo(1));
        Assert.That(Texel(map, 0), Is.EqualTo(0));
    }

    /// <summary>
    /// An edit inside the bounds keeps the bounds and the texture, and once applied changes only its own texel.
    /// </summary>
    [Test]
    public void EditInsideTheBounds_PatchesOnlyItsTexel()
    {
        Paint(0, Block(0, 0, 6, 6));
        TerrainControlMap map = CreateMap();
        map.Apply();
        BoundsInt bounds = map.Bounds;
        Texture2D texture = map.Texture;
        ushort[] before = texture.GetPixelData<ushort>(0).ToArray();
        Vector3Int cell = new(2, 3, 0);

        Paint(1, cell);
        Refresh(map, 1, cell);

        Assert.That(map.Bounds, Is.EqualTo(bounds));
        Assert.That(texture.GetPixelData<ushort>(0).ToArray(), Is.EqualTo(before), "Nothing is written before Apply.");
        Assert.That(map.Apply(), Is.True);
        Assert.That(map.Texture, Is.SameAs(texture));
        ushort[] after = texture.GetPixelData<ushort>(0).ToArray();
        int index = IndexOf(map, cell);
        for (int i = 0; i < after.Length; i++)
        {
            Assert.That(after[i], Is.EqualTo(i == index ? 3 : before[i]), $"texel {i}");
        }
    }

    /// <summary>
    /// A filled cell outside the bounds grows them to hold it and its margin, keeps every other cell's bits, and the
    /// texture follows on the next Apply.
    /// </summary>
    [Test]
    public void FilledCellOutsideTheBounds_GrowsTheMap()
    {
        Paint(0, Block(0, 0, 4, 4));
        TerrainControlMap map = CreateMap();
        map.Apply();
        Vector3Int far = new(12, -5, 0);

        Paint(2, far);
        Refresh(map, 2, far);

        Assert.That(map.Bounds.Contains(far + new Vector3Int(TerrainControlMap.Margin, -TerrainControlMap.Margin, 0)), Is.True);
        Assert.That(map.GetCell(far), Is.EqualTo(4));
        Assert.That(map.GetCell(new Vector3Int(3, 3, 0)), Is.EqualTo(1));
        map.Apply();
        Assert.That(map.Texture.width, Is.EqualTo(map.Bounds.size.x));
        Assert.That(Texel(map, far), Is.EqualTo(4));
        Assert.That(Texel(map, new Vector3Int(0, 0, 0)), Is.EqualTo(1));
    }

    /// <summary>
    /// A filled cell in the margin ring grows the bounds, so the margin stays empty for the edge's outer bands, while
    /// an emptied cell outside the bounds changes nothing.
    /// </summary>
    [Test]
    public void FilledCellInTheMargin_GrowsTheMap_EmptiedCellOutsideDoesNot()
    {
        Paint(0, Block(0, 0, 4, 4));
        TerrainControlMap map = CreateMap();
        map.Apply();
        BoundsInt bounds = map.Bounds;
        Vector3Int outside = new(20, 20, 0);
        Refresh(map, 0, outside);
        Assert.That(map.Bounds, Is.EqualTo(bounds));
        Assert.That(map.HasPendingChanges, Is.False);

        Vector3Int inMargin = new(bounds.xMax - 1, 1, 0);
        Paint(0, inMargin);
        Refresh(map, 0, inMargin);

        Assert.That(map.Bounds.xMax, Is.GreaterThanOrEqualTo(inMargin.x + 1 + TerrainControlMap.Margin));
        Assert.That(map.GetCell(inMargin), Is.EqualTo(1));
    }

    /// <summary>
    /// Several changed cells wait for one Apply, which uploads once; an Apply with nothing changed uploads nothing.
    /// </summary>
    [Test]
    public void ChangedCells_GoOutInOneWrite()
    {
        Paint(0, Block(0, 0, 6, 6));
        TerrainControlMap map = CreateMap();
        map.Apply();
        int uploads = map.UploadCount;
        Vector3Int[] cells = { new(1, 1, 0), new(2, 1, 0), new(4, 4, 0) };

        Paint(1, cells);
        Refresh(map, 1, cells);
        layers[0].SetTile(new Vector3Int(0, 0, 0), null);
        Refresh(map, 0, new Vector3Int(0, 0, 0));

        Assert.That(map.UploadCount, Is.EqualTo(uploads));
        Assert.That(map.HasPendingChanges, Is.True);
        Assert.That(map.Apply(), Is.True);
        Assert.That(map.UploadCount, Is.EqualTo(uploads + 1));
        Assert.That(Texel(map, cells[2]), Is.EqualTo(3));
        Assert.That(Texel(map, new Vector3Int(0, 0, 0)), Is.EqualTo(0));
        Assert.That(map.Apply(), Is.False);
        Assert.That(map.UploadCount, Is.EqualTo(uploads + 1));
    }

    /// <summary>
    /// Preview bits show in the texture, filling or emptying a layer, without touching the real bits, and clearing
    /// them shows the real bits again. A filled preview outside the bounds grows them.
    /// </summary>
    [Test]
    public void PreviewBits_ShowAndClear_WithoutTouchingTheRealOnes()
    {
        Paint(0, Block(0, 0, 6, 6));
        Paint(1, new Vector3Int(3, 3, 0));
        TerrainControlMap map = CreateMap();
        map.Apply();
        Vector3Int added = new(1, 1, 0);
        Vector3Int removed = new(3, 3, 0);
        Vector3Int outside = new(10, 2, 0);

        map.SetPreview(1, new[] { added, outside }, true);
        map.SetPreview(1, new[] { removed }, false);
        map.Apply();

        Assert.That(map.GetCell(added), Is.EqualTo(1));
        Assert.That(map.GetCell(removed), Is.EqualTo(3));
        Assert.That(map.GetCell(outside), Is.EqualTo(0));
        Assert.That(Texel(map, added), Is.EqualTo(3));
        Assert.That(Texel(map, removed), Is.EqualTo(1));
        Assert.That(Texel(map, outside), Is.EqualTo(2));
        Assert.That(layers[1].HasTile(added), Is.False);

        map.ClearPreview();
        map.Apply();

        Assert.That(Texel(map, added), Is.EqualTo(1));
        Assert.That(Texel(map, removed), Is.EqualTo(3));
        Assert.That(Texel(map, outside), Is.EqualTo(0));
        Assert.That(map.GetCell(removed), Is.EqualTo(3));
    }

    /// <summary>
    /// A changed position off the z = 0 layer of a tilemap is ignored, as <see cref="TerrainControlMap.Rebuild"/>
    /// ignores it, instead of changing the cell at z = 0.
    /// </summary>
    [Test]
    public void Refresh_IgnoresPositionsOffTheZeroLayer()
    {
        Paint(0, Block(0, 0, 6, 6));
        TerrainControlMap map = CreateMap();
        map.Apply();
        Vector3Int raised = new(2, 2, 1);

        Paint(1, raised);
        Refresh(map, 1, raised);

        Assert.That(map.GetCell(new Vector3Int(2, 2, 0)), Is.EqualTo(1));
        Assert.That(map.HasPendingChanges, Is.False);
    }

    /// <summary>
    /// Refreshing cells inside the bounds and uploading them allocates nothing once warmed up.
    /// </summary>
    [Test]
    public void RefreshAndApply_InsideTheBounds_DoNotAllocate()
    {
        Paint(0, Block(0, 0, 6, 6));
        TerrainControlMap map = CreateMap();
        map.Apply();
        Vector3Int cell = new(2, 2, 0);
        using NativeArray<Vector3Int> positions = new(new[] { cell }, Allocator.Persistent);
        void Toggle()
        {
            layers[1].SetTile(cell, layers[1].HasTile(cell) ? null : tile);
            map.Refresh(1, positions);
            map.Apply();
        }

        Toggle();
        Toggle();

        layers[1].SetTile(cell, tile);
        Assert.That(() =>
        {
            map.Refresh(1, positions);
            map.Apply();
        }, Is.Not.AllocatingGCMemory());
    }

    /// <summary>
    /// Creates a control map over the three layers, disposed in <see cref="TearDown"/>.
    /// </summary>
    private TerrainControlMap CreateMap()
    {
        TerrainControlMap map = new(layers);
        maps.Add(map);
        return map;
    }

    /// <summary>
    /// Fills <paramref name="cells"/> of layer <paramref name="layer"/> with the plain tile.
    /// </summary>
    private void Paint(int layer, params Vector3Int[] cells)
    {
        foreach (Vector3Int cell in cells)
        {
            layers[layer].SetTile(cell, tile);
        }
    }

    /// <summary>
    /// Tells <paramref name="map"/> that <paramref name="cells"/> of layer <paramref name="layer"/> changed.
    /// </summary>
    private static void Refresh(TerrainControlMap map, int layer, params Vector3Int[] cells)
    {
        using NativeArray<Vector3Int> positions = new(cells, Allocator.Temp);
        map.Refresh(layer, positions);
    }

    /// <summary>
    /// The texture's texel for <paramref name="cell"/>.
    /// </summary>
    private static uint Texel(TerrainControlMap map, Vector3Int cell)
    {
        return Texel(map, IndexOf(map, cell));
    }

    /// <summary>
    /// The texture's texel at <paramref name="index"/>: a narrow map's 16 bits or a wide map's 32.
    /// </summary>
    private static uint Texel(TerrainControlMap map, int index)
    {
        return map.IsWide ? map.Texture.GetPixelData<uint>(0)[index] : map.Texture.GetPixelData<ushort>(0)[index];
    }

    /// <summary>
    /// The channels of a wide map's texel for <paramref name="cell"/>, as the texture itself reads them.
    /// </summary>
    private static Color32 Channels(TerrainControlMap map, Vector3Int cell)
    {
        BoundsInt bounds = map.Bounds;
        return map.Texture.GetPixel(cell.x - bounds.xMin, cell.y - bounds.yMin);
    }

    /// <summary>
    /// The index of <paramref name="cell"/> in the texture's data, rows from the bottom.
    /// </summary>
    private static int IndexOf(TerrainControlMap map, Vector3Int cell)
    {
        return (cell.y - map.Bounds.yMin) * map.Bounds.size.x + cell.x - map.Bounds.xMin;
    }

    /// <summary>
    /// The cells of the rectangle from (<paramref name="minX"/>, <paramref name="minY"/>) inclusive to
    /// (<paramref name="maxX"/>, <paramref name="maxY"/>) exclusive.
    /// </summary>
    private static Vector3Int[] Block(int minX, int minY, int maxX, int maxY)
    {
        List<Vector3Int> cells = new();
        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                cells.Add(new Vector3Int(x, y, 0));
            }
        }

        return cells.ToArray();
    }

    /// <summary>
    /// Records <paramref name="created"/> for destruction in <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T created) where T : Object
    {
        createdObjects.Add(created);
        return created;
    }
}
