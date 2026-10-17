using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for <see cref="TerrainCoastMap"/>: the signed distance to the coast of the layer with waves, rising out to sea
/// and falling inland, the map's edge never a coast, a bridge over the land leaving it land (in a map of three layers and
/// in the top layers of a wide one), a single texel without a layer with waves, and the distances worked out again only
/// when the coast moves.
/// </summary>
public class TerrainCoastMapTests
{
    private const int SandLayer = 0;
    private const int SeaLayer = 1;
    private const int DockLayer = 2;
    private const int Size = 16;
    private const int CoastRow = 8;

    private readonly List<Object> createdObjects = new();
    private readonly List<System.IDisposable> disposables = new();
    private Tilemap[] layers;
    private Tile tile;

    /// <summary>
    /// Builds a grid with three layer tilemaps (sand, sea, a dock) and a plain tile to paint them with; the sand fills a
    /// 16 x 16 block and the sea its rows from <see cref="CoastRow"/> up.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        GameObject root = Track(new GameObject("CoastMapGrid", typeof(Grid)));
        tile = Track(ScriptableObject.CreateInstance<Tile>());
        layers = new Tilemap[3];
        for (int i = 0; i < layers.Length; i++)
        {
            GameObject layerObject = new($"Layer{i}", typeof(Tilemap));
            layerObject.transform.SetParent(root.transform);
            layers[i] = layerObject.GetComponent<Tilemap>();
        }

        Paint(SandLayer, 0, 0, Size - 1, Size - 1);
        Paint(SeaLayer, 0, CoastRow, Size - 1, Size - 1);
    }

    /// <summary>
    /// Disposes the maps and destroys the grid and tile the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (System.IDisposable disposable in disposables)
        {
            disposable.Dispose();
        }

        disposables.Clear();
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
    /// Down the middle column the distance rises steadily from the land, through about 0 at the coast, out to sea, about
    /// a pixel per pixel: negative inland, positive and near its true value a few cells out.
    /// </summary>
    [Test]
    public void TheDistance_RisesOutToSea_AndFallsInland()
    {
        TerrainControlMap controlMap = ControlMap();
        TerrainCoastMap coastMap = Build(controlMap, SeaLayer, 0u);

        float previous = float.MinValue;
        for (int cy = CoastRow - 2; cy < Size - 2; cy++)
        {
            float distance = DistanceAt(coastMap, controlMap, Size / 2, cy);
            Assert.That(distance, Is.GreaterThan(previous), $"row {cy}");
            previous = distance;
        }

        Assert.That(DistanceAt(coastMap, controlMap, Size / 2, CoastRow - 2), Is.LessThan(-32f));
        Assert.That(DistanceAt(coastMap, controlMap, Size / 2, CoastRow + 3), Is.EqualTo(3.5f * TerrainStyle.PixelsPerCell).Within(4f));
        Assert.That(Mathf.Abs(DistanceAt(coastMap, controlMap, Size / 2, CoastRow) - 0.5f * TerrainStyle.PixelsPerCell), Is.LessThan(4f));
    }

    /// <summary>
    /// A sea filling every cell has no coast, though the map's edge lies around it: every texel lies far out at sea.
    /// </summary>
    [Test]
    public void TheMapsEdge_IsNoCoast()
    {
        Paint(SeaLayer, 0, 0, Size - 1, Size - 1);
        TerrainControlMap controlMap = ControlMap();
        TerrainCoastMap coastMap = Build(controlMap, SeaLayer, 0u);

        foreach (ushort texel in coastMap.Texture.GetPixelData<ushort>(0))
        {
            Assert.That(Decode(texel), Is.GreaterThan(4f * TerrainStyle.PixelsPerCell));
        }
    }

    /// <summary>
    /// A dock painted over the beach counts as sea when it is no bridge, as a layer above the sea does, and as the land
    /// under it when it is one.
    /// </summary>
    [Test]
    public void ABridgeOverTheLand_LeavesItLand()
    {
        Paint(DockLayer, 6, 2, 9, CoastRow + 1);
        TerrainControlMap controlMap = ControlMap();

        TerrainCoastMap asSea = Build(controlMap, SeaLayer, 0u);
        TerrainCoastMap asBridge = Build(controlMap, SeaLayer, 1u << DockLayer);

        Assert.That(DistanceAt(asSea, controlMap, 8, 3), Is.GreaterThan(0f));
        Assert.That(DistanceAt(asBridge, controlMap, 8, 3), Is.LessThan(-32f));
    }

    /// <summary>
    /// In a wide control map whose sand, sea and dock are its last three layers (29 to 31, the dock's bit the top one),
    /// the dock over the beach counts as sea when it is no bridge and as land when it is one, as in a map of three layers.
    /// </summary>
    [Test]
    public void ABridgeOverTheLand_LeavesItLand_InTheTopLayersOfAWideMap()
    {
        Paint(DockLayer, 6, 2, 9, CoastRow + 1);
        Tilemap[] wide = new Tilemap[TerrainControlMap.WideLayers];
        int first = wide.Length - layers.Length;
        layers.CopyTo(wide, first);
        TerrainControlMap controlMap = new(wide, true);
        controlMap.Apply();
        disposables.Add(controlMap);

        TerrainCoastMap asSea = Build(controlMap, first + SeaLayer, 0u);
        TerrainCoastMap asBridge = Build(controlMap, first + SeaLayer, 1u << (first + DockLayer));

        Assert.That(first + DockLayer, Is.EqualTo(31));
        Assert.That(DistanceAt(asSea, controlMap, 8, 3), Is.GreaterThan(0f));
        Assert.That(DistanceAt(asBridge, controlMap, 8, 3), Is.LessThan(-32f));
        Assert.That(DistanceAt(asBridge, controlMap, 8, Size - 2), Is.GreaterThan(32f), "Out at sea is still sea.");
    }

    /// <summary>
    /// Without a layer with waves the map is a single texel, which the shader never reads.
    /// </summary>
    [Test]
    public void WithoutALayerWithWaves_TheMapIsOneTexel()
    {
        TerrainCoastMap coastMap = Build(ControlMap(), -1, 0u);

        Assert.That(coastMap.Texture.width, Is.EqualTo(1));
        Assert.That(coastMap.Texture.height, Is.EqualTo(1));
    }

    /// <summary>
    /// Building again after a change that moves no cell between land and sea, painting more sand under the sea, keeps
    /// the texture as it was; painting the sea further in works the distances out and uploads them again.
    /// </summary>
    [Test]
    public void TheDistances_AreWorkedOutAgain_OnlyWhenTheCoastMoves()
    {
        TerrainControlMap controlMap = ControlMap();
        TerrainCoastMap coastMap = Build(controlMap, SeaLayer, 0u);
        uint uploads = coastMap.Texture.updateCount;
        float before = DistanceAt(coastMap, controlMap, Size / 2, CoastRow - 1);

        layers[SandLayer].SetTile(new Vector3Int(Size / 2, Size - 1, 0), null);
        layers[SandLayer].SetTile(new Vector3Int(Size / 2, Size - 1, 0), tile);
        controlMap.Rebuild();
        controlMap.Apply();
        coastMap.Build(controlMap, SeaLayer, 0u);
        Assert.That(coastMap.Texture.updateCount, Is.EqualTo(uploads));

        Paint(SeaLayer, 0, CoastRow - 2, Size - 1, CoastRow - 1);
        controlMap.Rebuild();
        controlMap.Apply();
        coastMap.Build(controlMap, SeaLayer, 0u);
        Assert.That(coastMap.Texture.updateCount, Is.GreaterThan(uploads));
        Assert.That(DistanceAt(coastMap, controlMap, Size / 2, CoastRow - 1), Is.GreaterThan(before + TerrainStyle.PixelsPerCell));
    }

    /// <summary>
    /// A control map over the three layers, applied, disposed in <see cref="TearDown"/>.
    /// </summary>
    private TerrainControlMap ControlMap()
    {
        TerrainControlMap controlMap = new(layers);
        controlMap.Apply();
        disposables.Add(controlMap);
        return controlMap;
    }

    /// <summary>
    /// A coast map built over <paramref name="controlMap"/>, disposed in <see cref="TearDown"/>.
    /// </summary>
    private TerrainCoastMap Build(TerrainControlMap controlMap, int waveLayer, uint bridges)
    {
        TerrainCoastMap coastMap = new("Test Coast Map");
        disposables.Add(coastMap);
        coastMap.Build(controlMap, waveLayer, bridges);
        return coastMap;
    }

    /// <summary>
    /// The distance in pixels the coast map holds at the centre of cell (<paramref name="cx"/>, <paramref name="cy"/>),
    /// the texel just up and right of it.
    /// </summary>
    private static float DistanceAt(TerrainCoastMap coastMap, TerrainControlMap controlMap, int cx, int cy)
    {
        BoundsInt bounds = controlMap.Bounds;
        int half = TerrainCoastMap.TexelsPerCell / 2;
        int x = (cx - bounds.xMin) * TerrainCoastMap.TexelsPerCell + half;
        int y = (cy - bounds.yMin) * TerrainCoastMap.TexelsPerCell + half;
        return Decode(coastMap.Texture.GetPixelData<ushort>(0)[y * coastMap.Texture.width + x]);
    }

    /// <summary>
    /// A texel's distance in pixels.
    /// </summary>
    private static float Decode(ushort texel)
    {
        return texel / 65535f * TerrainCoastMap.DistanceRange + TerrainCoastMap.MinDistance;
    }

    /// <summary>
    /// Fills the block of cells from (<paramref name="minX"/>, <paramref name="minY"/>) to (<paramref name="maxX"/>,
    /// <paramref name="maxY"/>) on a layer.
    /// </summary>
    private void Paint(int layer, int minX, int minY, int maxX, int maxY)
    {
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                layers[layer].SetTile(new Vector3Int(x, y, 0), tile);
            }
        }
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
