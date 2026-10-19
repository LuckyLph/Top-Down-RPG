using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for a chunk's terrain renderer (Docs/WorldStreaming.md: Terrain): a control map's sources count only inside
/// their cells and a fixed area bounds it; a chunk's control map covers its cells and the skirt around them while its
/// quads cover only its cells; the skirt reads a neighbour's border cells without writing the chunk's own tilemaps,
/// follows the neighbour's tile changes there and empties when cleared; a material the skirt shows that no layer draws
/// adds a layer at its place in the style's order, at once or when a tile first lands in its strip; raised ground
/// across a border turns the raised look on only once the strip holds raised ground; a baked coast map is used when it
/// fits the control map, and one of the wrong size is warned about and built instead. Each test builds its own grids,
/// style and materials.
/// </summary>
public class TerrainSkirtTests
{
    private static readonly RectInt OwnCells = new(0, 0, 16, 16);
    private static readonly RectInt NeighbourCells = new(16, 0, 16, 16);
    private static readonly RectInt Strip = new(16, 0, 4, 16);

    private readonly List<Object> created = new();
    private Tile tile;
    private TerrainStyle style;
    private TerrainMaterial grass;
    private TerrainMaterial gravel;
    private TerrainMaterial stone;

    /// <summary>
    /// Creates a tile and three plain materials in a style whose layer order is grass, gravel, stone.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        tile = Track(ScriptableObject.CreateInstance<Tile>());
        TerrainMaterial[] plain = TestTerrainStyle.PlainMaterials(created);
        grass = plain[0];
        gravel = plain[1];
        stone = plain[2];
        style = TestTerrainStyle.Create(created, plain);
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
    /// A source sets its layer's bit only inside its cells, a fixed area makes the bounds that area whatever is filled
    /// beyond it, and a change on the source's tilemap inside its cells reaches the map while one outside does not.
    /// </summary>
    [Test]
    public void ASource_CountsOnlyInsideItsCells_AndAFixedAreaBoundsTheMap()
    {
        Tilemap own = NewTilemap("Own");
        Tilemap neighbour = NewTilemap("Neighbour");
        Fill(own, OwnCells);
        Fill(neighbour, NeighbourCells);
        RectInt area = TerrainBlendRenderer.ChunkControlArea(OwnCells, 4);
        TerrainControlMap map = new(new[] { own }, false, "Test", area, new[] { new TerrainControlMap.Source(neighbour, 0, Strip) });
        try
        {
            Assert.That(map.Bounds, Is.EqualTo(new BoundsInt(-5, -5, 0, 26, 26, 1)));
            Assert.That(map.GetCell(new Vector3Int(17, 3, 0)), Is.EqualTo(1u), "A strip cell is filled.");
            Assert.That(map.GetCell(new Vector3Int(20, 3, 0)), Is.EqualTo(0u), "Past the strip the neighbour is not read.");
            Assert.That(map.Reads(neighbour), Is.True);

            neighbour.SetTile(new Vector3Int(17, 3, 0), null);
            neighbour.SetTile(new Vector3Int(20, 4, 0), null);
            using NativeArray<Vector3Int> changed = new(new[] { new Vector3Int(17, 3, 0), new Vector3Int(20, 4, 0) }, Allocator.Temp);
            Assert.That(map.Refresh(neighbour, changed), Is.True);

            Assert.That(map.GetCell(new Vector3Int(17, 3, 0)), Is.EqualTo(0u), "The cleared strip cell follows.");
            Assert.That(map.Bounds, Is.EqualTo(new BoundsInt(-5, -5, 0, 26, 26, 1)), "The bounds stay the area.");
        }
        finally
        {
            map.Dispose();
        }
    }

    /// <summary>
    /// A chunk's renderer builds its control map over its cells and the skirt, draws quads over its own cells only,
    /// reads its neighbour's strip without writing its own tilemap, follows a change there, and with the skirt cleared
    /// draws the neighbour's cells as empty again.
    /// </summary>
    [Test]
    public void AChunk_DrawsOnlyItsCells_AndItsSkirtFollowsTheNeighbour()
    {
        Tilemap neighbour = NewTilemap("Neighbour");
        Fill(neighbour, NeighbourCells);
        TerrainBlendRenderer renderer = NewChunk(out Tilemap own, grass);
        Fill(own, OwnCells);
        int ownTiles = CountTiles(own);
        renderer.SetSkirt(new[] { new TerrainSkirtSource(neighbour, grass, Strip) });

        Assert.That(renderer.IsChunk, Is.True);
        Assert.That(renderer.ControlMap.Bounds, Is.EqualTo(new BoundsInt(-5, -5, 0, 26, 26, 1)));
        Assert.That(QuadBounds(renderer), Is.EqualTo(new Rect(0f, 0f, 16f, 16f)), "The quads cover the chunk's cells only.");
        Assert.That(renderer.ControlMap.GetCell(new Vector3Int(19, 8, 0)), Is.EqualTo(1u));
        Assert.That(CountTiles(own), Is.EqualTo(ownTiles), "The chunk's own tilemap holds no skirt cell.");

        neighbour.SetTile(new Vector3Int(16, 8, 0), null);
        Assert.That(renderer.ControlMap.GetCell(new Vector3Int(16, 8, 0)), Is.EqualTo(0u), "A neighbour's change in the strip reaches the map.");

        renderer.SetSkirt(new TerrainSkirtSource[0]);
        Assert.That(renderer.ControlMap.GetCell(new Vector3Int(17, 8, 0)), Is.EqualTo(0u), "Without the skirt the neighbour's cells are empty.");
    }

    /// <summary>
    /// A material the skirt shows that none of the chunk's layers draws gets a layer at its place in the style's order;
    /// one whose strip is empty gets none until a tile lands in the strip, which rebuilds the renderer with it.
    /// </summary>
    [Test]
    public void ASkirtMaterialNoLayerDraws_AddsALayerAtItsPlace_OnceItsStripHasATile()
    {
        Tilemap neighbourStone = NewTilemap("NeighbourStone");
        Tilemap neighbourGravel = NewTilemap("NeighbourGravel");
        Fill(neighbourStone, new RectInt(16, 4, 2, 2));
        TerrainBlendRenderer renderer = NewChunk(out Tilemap own, grass);
        Fill(own, OwnCells);
        renderer.SetSkirt(new[] { new TerrainSkirtSource(neighbourStone, stone, Strip), new TerrainSkirtSource(neighbourGravel, gravel, Strip) });

        Assert.That(renderer.BuiltMaterials, Is.EqualTo(new[] { grass, stone }), "Stone gets a layer; gravel's strip is empty.");
        Assert.That(renderer.ControlMap.GetCell(new Vector3Int(16, 4, 0)), Is.EqualTo(2u));

        neighbourGravel.SetTile(new Vector3Int(17, 9, 0), tile);

        Assert.That(renderer.BuiltMaterials, Is.EqualTo(new[] { grass, gravel, stone }), "Gravel's first tile adds its layer between grass and stone.");
        Assert.That(renderer.ControlMap.GetCell(new Vector3Int(17, 9, 0)), Is.EqualTo(2u));
        Assert.That(renderer.ControlMap.GetCell(new Vector3Int(16, 4, 0)), Is.EqualTo(4u));
        Assert.That(renderer.Layers.Count, Is.EqualTo(1), "Its own layer list is unchanged.");
    }

    /// <summary>
    /// A neighbour's raised tilemap whose strip is level leaves the raised look off; once raised ground lands in the
    /// strip the renderer rebuilds with it on, and its elevation map shows the strip's raised cell.
    /// </summary>
    [Test]
    public void RaisedGroundAcrossABorder_TurnsTheRaisedLookOn_OnceTheStripHoldsSome()
    {
        Tilemap raised = NewTilemap("NeighbourRaised");
        RaisedGroundTile raisedTile = Track(RaisedGroundTile.Create(false));
        TerrainBlendRenderer renderer = NewChunk(out Tilemap own, grass);
        Fill(own, OwnCells);
        renderer.SetSkirt(new[] { TerrainSkirtSource.Raised(raised, Strip) });

        Assert.That(renderer.RuntimeMaterial.IsKeywordEnabled("_TERRAIN_RAISED"), Is.False);

        raised.SetTile(new Vector3Int(17, 5, 0), raisedTile);
        renderer.FlushPendingChanges();

        Assert.That(renderer.RuntimeMaterial.IsKeywordEnabled("_TERRAIN_RAISED"), Is.True);
        Assert.That(renderer.ElevationMap.GetCell(new Vector3Int(17, 5, 0)), Is.EqualTo(RaisedGround.Raised));
        Assert.That(renderer.ElevationMap.GetCell(new Vector3Int(5, 5, 0)), Is.EqualTo(RaisedGround.Level));
    }

    /// <summary>
    /// A chunk with a material with waves takes a baked coast map of its control map's size instead of building one;
    /// a map of another size is warned about and the coast map is built instead.
    /// </summary>
    [Test]
    public void ABakedCoastMap_IsUsedWhenItFitsTheControlMap()
    {
        TerrainMaterial ocean = WavesMaterial();
        RectInt area = TerrainBlendRenderer.ChunkControlArea(OwnCells, 4);
        Texture2D baked = Track(new Texture2D(area.width * TerrainCoastMap.TexelsPerCell, area.height * TerrainCoastMap.TexelsPerCell, TextureFormat.R16, false, true) { name = "Baked" });
        TerrainBlendRenderer renderer = NewChunk(out Tilemap own, ocean, baked);
        Fill(own, OwnCells);
        renderer.Rebuild();

        Assert.That(renderer.IsCoastBaked, Is.True);
        Assert.That(renderer.CoastTexture, Is.SameAs(baked));

        Texture2D wrong = Track(new Texture2D(8, 8, TextureFormat.R16, false, true) { name = "Wrong" });
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("baked coast map Wrong"));
        renderer.SetChunk(OwnCells, 4, wrong);

        Assert.That(renderer.IsCoastBaked, Is.False);
        Assert.That(renderer.CoastTexture, Is.Not.SameAs(wrong));
        Assert.That(renderer.CoastTexture.width, Is.EqualTo(area.width * TerrainCoastMap.TexelsPerCell));
    }

    /// <summary>
    /// A material with waves (animated, with a wave spacing).
    /// </summary>
    private TerrainMaterial WavesMaterial()
    {
        TerrainMaterial ocean = TestTerrainStyle.PlainMaterial("Ocean", TerrainMaterial.CellMode.None, created);
        UnityEditor.SerializedObject data = new(ocean);
        data.FindProperty("framesPerSecond").floatValue = 8f;
        data.FindProperty("waveSpacing").floatValue = 48f;
        data.ApplyModifiedPropertiesWithoutUndo();
        Assert.That(ocean.HasWaves, Is.True);
        return ocean;
    }

    /// <summary>
    /// An enabled chunk renderer over <see cref="OwnCells"/> with one layer of <paramref name="material"/> on a new
    /// tilemap, given in <paramref name="own"/>, and <paramref name="coastMap"/> as its baked coast map.
    /// </summary>
    private TerrainBlendRenderer NewChunk(out Tilemap own, TerrainMaterial material, Texture2D coastMap = null)
    {
        GameObject gridObject = Track(new GameObject("Chunk", typeof(Grid)));
        gridObject.SetActive(false);
        own = new GameObject("Own", typeof(Tilemap)).GetComponent<Tilemap>();
        own.transform.SetParent(gridObject.transform, false);
        TerrainBlendRenderer renderer = gridObject.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(style, new TerrainBlendRenderer.Layer(own, material));
        renderer.SetChunk(OwnCells, 4, coastMap);
        gridObject.SetActive(true);
        return renderer;
    }

    /// <summary>
    /// A tilemap under its own new grid.
    /// </summary>
    private Tilemap NewTilemap(string tilemapName)
    {
        GameObject gridObject = Track(new GameObject(tilemapName + "Grid", typeof(Grid)));
        Tilemap tilemap = new GameObject(tilemapName, typeof(Tilemap)).GetComponent<Tilemap>();
        tilemap.transform.SetParent(gridObject.transform, false);
        return tilemap;
    }

    /// <summary>
    /// Fills <paramref name="cells"/> of <paramref name="tilemap"/> with the test tile.
    /// </summary>
    private void Fill(Tilemap tilemap, RectInt cells)
    {
        List<Vector3Int> positions = new();
        foreach (Vector2Int cell in cells.allPositionsWithin)
        {
            positions.Add(new Vector3Int(cell.x, cell.y, 0));
        }

        TileBase[] tiles = new TileBase[positions.Count];
        System.Array.Fill(tiles, tile);
        tilemap.SetTiles(positions.ToArray(), tiles);
    }

    /// <summary>
    /// How many tiles <paramref name="tilemap"/> holds.
    /// </summary>
    private static int CountTiles(Tilemap tilemap)
    {
        int count = 0;
        foreach (TileBase held in tilemap.GetTilesBlock(tilemap.cellBounds))
        {
            count += held != null ? 1 : 0;
        }

        return count;
    }

    /// <summary>
    /// The world rectangle the renderer's quads cover together.
    /// </summary>
    private static Rect QuadBounds(TerrainBlendRenderer renderer)
    {
        Bounds bounds = renderer.Chunks[0].bounds;
        foreach (MeshRenderer chunk in renderer.Chunks)
        {
            bounds.Encapsulate(chunk.bounds);
        }

        return new Rect(bounds.min.x, bounds.min.y, bounds.size.x, bounds.size.y);
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
