using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Tilemaps;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

/// <summary>
/// Tests for <see cref="TerrainBrush"/> on the chunks of a seamless world (<see cref="WorldBrushStroke"/>): a 2 x 2
/// world whose chunks (0, 0), (1, 0) and (0, 1) are built in memory and linked, each a grid at the world's origin with
/// the project's style and a grass layer, chunk (0, 0) also with walls, a sand layer, a raised tilemap and a decor
/// tilemap; chunk (1, 1) is part of the world but not open. A stroke across a border writes each cell into the chunk
/// owning it, on its layer of the same material (added where missing, also to a neighbour whose skirt the stroke
/// reaches), its walls (copied where missing) or its raised tilemap (set up where missing), and one undo takes all of
/// it back. Cells of a chunk that is not open, or outside the world, are skipped with a warning; a tilemap of another
/// kind is written only inside its chunk. A flood fill spreads across the border. The hover preview shows on each
/// chunk's renderer it covers. A grid that is no chunk is painted as before. The test reads the project's style,
/// materials, ground and raised tiles and walls rule tile, and changes none of them.
/// </summary>
public class WorldBrushStrokeTests
{
    private const string WallsRuleTilePath = "Assets/Tiles/tilesets/walls_DualGridRuleTile.asset";
    private const string RaisedTilePath = "Assets/Tiles/Raised/Tile_Raised.asset";

    private readonly List<Object> created = new();
    private readonly Dictionary<Vector2Int, WorldChunk> chunks = new();
    private WorldDefinition world;
    private TerrainStyle style;
    private TerrainMaterial grass;
    private TerrainMaterial sand;
    private TileBase sandTile;
    private TerrainBrush brush;
    private TerrainBrushEditor editor;

    /// <summary>
    /// Builds the world's three open chunks, linked, and a one-cell brush with its editor.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        style = AssetDatabase.LoadAssetAtPath<TerrainStyle>(WorldAuthoring.StylePath);
        grass = WorldAuthoring.MaterialNamed("Grass");
        sand = WorldAuthoring.MaterialNamed("Sand");
        sandTile = AssetDatabase.LoadAssetAtPath<TileBase>(TerrainTileWriter.TilePath(sand));
        world = Track(WorldDefinition.Create("BrushTest", new Vector2Int(2048, 0), new Vector2Int(2, 2), new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) }));
        foreach (Vector2Int coords in new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1) })
        {
            chunks[coords] = NewChunk(coords);
        }

        Grid first = chunks[Vector2Int.zero].GetComponent<Grid>();
        GroundLayerMigration.AddLayer(first, sand);
        AddWalls(first.transform);
        AreaGroundSetup.SetUpRaised(first, new List<string>());
        Tilemap decor = new GameObject("Decor", typeof(Tilemap)).GetComponent<Tilemap>();
        decor.transform.SetParent(first.transform, false);
        foreach (WorldChunk chunk in chunks.Values)
        {
            foreach (WorldChunk other in chunks.Values)
            {
                chunk.Link(other);
            }

            chunk.RefreshLinks();
        }

        brush = Track(ScriptableObject.CreateInstance<TerrainBrush>());
        brush.SetTile(Vector3Int.zero, sandTile);
        editor = (TerrainBrushEditor)Editor.CreateEditor(brush);
        Undo.IncrementCurrentGroup();
    }

    /// <summary>
    /// Destroys the editor, clears the undo history of what the test made and destroys it.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        if (editor != null)
        {
            Object.DestroyImmediate(editor);
        }

        foreach (WorldChunk chunk in chunks.Values)
        {
            if (chunk != null)
            {
                Undo.ClearUndo(chunk.gameObject);
                foreach (Component component in chunk.GetComponentsInChildren<Component>(true))
                {
                    Undo.ClearUndo(component);
                }
            }
        }

        chunks.Clear();
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
    /// A box of sand across the border at x = 64, painted on chunk (0, 0)'s sand layer, lands on each chunk's own
    /// cells: chunk (1, 0) gets a sand layer holding its part, nothing past the border stays on chunk (0, 0), and chunk
    /// (0, 1), whose skirt the box does not reach, gets no layer. One undo takes the cells and the added layer back.
    /// </summary>
    [Test]
    public void BoxFill_OnAGroundLayer_WritesEachCellToItsChunk_AddingTheLayerWhereMissing_AsOneUndoStep()
    {
        Tilemap target = Layer(Vector2Int.zero, sand);
        BoundsInt box = new(60, 10, 0, 8, 3, 1);

        Stroke(target, () => brush.BoxFill(target.layoutGrid, target.gameObject, box));

        Tilemap east = Layer(new Vector2Int(1, 0), sand);
        Assert.That(east, Is.Not.Null, "Chunk (1, 0) gets a sand layer.");
        Assert.That(FilledCells(target), Is.EquivalentTo(CellsOf(new RectInt(60, 10, 4, 3))));
        Assert.That(FilledCells(east), Is.EquivalentTo(CellsOf(new RectInt(64, 10, 4, 3))));
        Assert.That(east.GetTile(new Vector3Int(64, 10, 0)), Is.SameAs(sandTile));
        Assert.That(Layer(new Vector2Int(0, 1), sand), Is.Null, "The box is far from chunk (0, 1)'s skirt.");

        Undo.PerformUndo();

        Assert.That(FilledCells(target), Is.Empty);
        Assert.That(Layer(new Vector2Int(1, 0), sand), Is.Null, "The added layer goes with the same undo.");
    }

    /// <summary>
    /// Painting sand on chunk (0, 0)'s own cells within four cells of its border with chunk (0, 1) gives chunk (0, 1)
    /// an empty sand layer, so its skirt can show that sand, and chunk (1, 0), whose skirt it does not reach, none.
    /// </summary>
    [Test]
    public void Paint_NearABorder_GivesTheNeighbourWhoseSkirtItReachesTheLayer()
    {
        Tilemap target = Layer(Vector2Int.zero, sand);

        Stroke(target, () => brush.Paint(target.layoutGrid, target.gameObject, new Vector3Int(10, 61, 0)));

        Tilemap north = Layer(new Vector2Int(0, 1), sand);
        Assert.That(north, Is.Not.Null);
        Assert.That(FilledCells(north), Is.Empty);
        Assert.That(Layer(new Vector2Int(1, 0), sand), Is.Null);
        Assert.That(target.GetTile(new Vector3Int(10, 61, 0)), Is.SameAs(sandTile));
    }

    /// <summary>
    /// The palette's Add the layer button on a chunk (<see cref="TerrainBrushEditor.AddMissingLayer"/>) links the chunk
    /// with its neighbours again: chunk (0, 0), which already draws sand, reads sand painted on the new sand layer of
    /// chunk (1, 0) within its skirt.
    /// </summary>
    [Test]
    public void AddingAMissingLayerOnAChunk_GivesTheNeighboursSkirtItsCells()
    {
        Tilemap added = TerrainBrushEditor.AddMissingLayer(Layer(new Vector2Int(1, 0), grass).gameObject, sand);
        Assert.That(added, Is.Not.Null);

        added.SetTile(new Vector3Int(65, 10, 0), sandTile);

        TerrainBlendRenderer west = chunks[Vector2Int.zero].GetComponent<TerrainBlendRenderer>();
        int sandIndex = new List<TerrainMaterial>(west.BuiltMaterials).IndexOf(sand);
        Assert.That(sandIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(west.ControlMap.GetCell(new Vector3Int(65, 10, 0)) & (1u << sandIndex), Is.Not.Zero, "Chunk (0, 0)'s skirt shows the sand.");
    }

    /// <summary>
    /// The brush marks a chunk's coast map out of date as it paints the sea or its shore within the map's reach
    /// (Docs/KnownGaps.md, Phase 5), and its glow map as it paints lava within the glow's reach: sand painted on the
    /// grass far from any sea marks nothing; ocean painted in chunk (0, 0)'s middle marks its coast map alone, farther
    /// than the bake reaches into its neighbours; ocean painted by the border with chunk (1, 0) marks that chunk's too;
    /// erasing the grass of a cell, which leaves a hole the coast reads as sea, marks it; lava marks the glow map of the
    /// chunk it lies in alone, and no coast map. One undo takes a mark back with the stroke.
    /// </summary>
    [Test]
    public void PaintingTheSeaItsShoreOrLava_MarksTheMapsReachingIt_OutOfDate()
    {
        TerrainMaterial ocean = WorldAuthoring.MaterialNamed("Ocean");
        TerrainMaterial lava = WorldAuthoring.MaterialNamed("Lava");
        foreach (WorldChunk chunk in chunks.Values)
        {
            SerializedObject data = new(chunk);
            data.FindProperty("coastSource").stringValue = "baked";
            data.FindProperty("glowSource").stringValue = "baked";
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        Tilemap sandLayer = Layer(Vector2Int.zero, sand);
        Stroke(sandLayer, () => brush.Paint(sandLayer.layoutGrid, sandLayer.gameObject, new Vector3Int(10, 10, 0)));
        AssertMarks("sand on grass", false, false, false, false);

        Tilemap oceanLayer = GroundLayerMigration.AddLayer(chunks[Vector2Int.zero].GetComponent<Grid>(), ocean);
        brush.SetTile(Vector3Int.zero, AssetDatabase.LoadAssetAtPath<TileBase>(TerrainTileWriter.TilePath(ocean)));
        Stroke(oceanLayer, () => brush.Paint(oceanLayer.layoutGrid, oceanLayer.gameObject, new Vector3Int(10, 10, 0)));
        AssertMarks("ocean in the middle of chunk (0, 0)", true, false, false, false);

        Undo.PerformUndo();
        AssertMarks("the stroke undone", false, false, false, false);

        Stroke(oceanLayer, () => brush.Paint(oceanLayer.layoutGrid, oceanLayer.gameObject, new Vector3Int(60, 10, 0)));
        AssertMarks("ocean by the border with chunk (1, 0)", true, true, false, false);

        Tilemap northGrass = Layer(new Vector2Int(0, 1), grass);
        Stroke(northGrass, () => brush.Erase(northGrass.layoutGrid, northGrass.gameObject, new Vector3Int(10, 120, 0)));
        Assert.That(chunks[new Vector2Int(0, 1)].CoastSource, Is.EqualTo(WorldAuthoring.StaleSource), "a hole in the grass is sea to the coast map");

        Tilemap lavaLayer = GroundLayerMigration.AddLayer(chunks[new Vector2Int(1, 0)].GetComponent<Grid>(), lava);
        brush.SetTile(Vector3Int.zero, AssetDatabase.LoadAssetAtPath<TileBase>(TerrainTileWriter.TilePath(lava)));
        Stroke(lavaLayer, () => brush.Paint(lavaLayer.layoutGrid, lavaLayer.gameObject, new Vector3Int(100, 30, 0)));
        Assert.That(chunks[new Vector2Int(1, 0)].GlowSource, Is.EqualTo(WorldAuthoring.StaleSource), "lava marks its chunk's glow map");
        Assert.That(chunks[Vector2Int.zero].GlowSource, Is.EqualTo("baked"), "lava 36 cells from chunk (0, 0) is beyond the glow's reach");
        Assert.That(WorldAuthoring.IsGlowStale(chunks[new Vector2Int(1, 0)], "baked"), Is.True, "a marked map is stale whatever it was baked from");
    }

    /// <summary>
    /// Asserts which of chunks (0, 0), (1, 0), (0, 1) have their coast map marked out of date after
    /// <paramref name="what"/>, and that no glow map is (<paramref name="glow"/> for chunk (0, 0)'s).
    /// </summary>
    private void AssertMarks(string what, bool origin, bool east, bool north, bool glow)
    {
        Assert.That(chunks[Vector2Int.zero].CoastSource == WorldAuthoring.StaleSource, Is.EqualTo(origin), $"{what}: chunk (0, 0)'s coast map");
        Assert.That(chunks[new Vector2Int(1, 0)].CoastSource == WorldAuthoring.StaleSource, Is.EqualTo(east), $"{what}: chunk (1, 0)'s coast map");
        Assert.That(chunks[new Vector2Int(0, 1)].CoastSource == WorldAuthoring.StaleSource, Is.EqualTo(north), $"{what}: chunk (0, 1)'s coast map");
        Assert.That(chunks[Vector2Int.zero].GlowSource == WorldAuthoring.StaleSource, Is.EqualTo(glow), $"{what}: chunk (0, 0)'s glow map");
    }

    /// <summary>
    /// Erasing grass across the border at y = 64 empties the cells on both chunks' grass layers.
    /// </summary>
    [Test]
    public void BoxErase_AcrossABorder_EmptiesBothChunksCells()
    {
        Tilemap target = Layer(Vector2Int.zero, grass);
        BoundsInt box = new(5, 62, 0, 2, 4, 1);

        Stroke(target, () => brush.BoxErase(target.layoutGrid, target.gameObject, box));

        Tilemap north = Layer(new Vector2Int(0, 1), grass);
        foreach (Vector3Int cell in box.allPositionsWithin)
        {
            Assert.That(target.GetTile(cell), Is.Null, $"{cell} on chunk (0, 0)");
            Assert.That(north.GetTile(cell), Is.Null, $"{cell} on chunk (0, 1)");
        }

        Assert.That(north.GetTile(new Vector3Int(5, 66, 0)), Is.Not.Null);
    }

    /// <summary>
    /// Walls painted across the border at x = 64 go through each chunk's wall module: chunk (1, 0), which had none,
    /// gets a copy of chunk (0, 0)'s walls holding its part, and one undo removes it and the cells again.
    /// </summary>
    [Test]
    public void BoxFill_OnWalls_CopiesTheWallsToAChunkWithout_AndWritesItsPart()
    {
        DualGridTilemapModule walls = chunks[Vector2Int.zero].GetComponentInChildren<DualGridTilemapModule>();
        BoundsInt box = new(62, 30, 0, 4, 1, 1);

        Stroke(walls.DataTilemap, () => brush.BoxFill(walls.DataTilemap.layoutGrid, walls.gameObject, box));

        DualGridTilemapModule east = WorldBrushStroke.WallsLike(chunks[new Vector2Int(1, 0)], walls);
        Assert.That(east, Is.Not.Null);
        Assert.That(FilledCells(walls.DataTilemap), Is.EquivalentTo(CellsOf(new RectInt(62, 30, 2, 1))));
        Assert.That(FilledCells(east.DataTilemap), Is.EquivalentTo(CellsOf(new RectInt(64, 30, 2, 1))));
        Assert.That(east.DataTilemap.GetTile(new Vector3Int(64, 30, 0)), Is.SameAs(walls.DataTile));

        Undo.PerformUndo();

        Assert.That(WorldBrushStroke.WallsLike(chunks[new Vector2Int(1, 0)], walls), Is.Null);
        Assert.That(FilledCells(walls.DataTilemap), Is.Empty);
    }

    /// <summary>
    /// Raised ground painted across the border at y = 64 sets up chunk (0, 1)'s raised tilemap, which its renderer then
    /// draws, and writes each part to its chunk.
    /// </summary>
    [Test]
    public void BoxFill_OnRaisedGround_SetsUpTheRaisedTilemapWhereMissing()
    {
        TileBase raisedTile = AssetDatabase.LoadAssetAtPath<TileBase>(RaisedTilePath);
        brush.SetTile(Vector3Int.zero, raisedTile);
        Tilemap raised = chunks[Vector2Int.zero].Terrain.RaisedTilemap;
        BoundsInt box = new(20, 62, 0, 3, 4, 1);

        Stroke(raised, () => brush.BoxFill(raised.layoutGrid, raised.gameObject, box));

        Tilemap north = chunks[new Vector2Int(0, 1)].GetComponent<TerrainBlendRenderer>().RaisedTilemap;
        Assert.That(north, Is.Not.Null);
        Assert.That(FilledCells(raised), Is.EquivalentTo(CellsOf(new RectInt(20, 62, 3, 2))));
        Assert.That(FilledCells(north), Is.EquivalentTo(CellsOf(new RectInt(20, 64, 3, 2))));
    }

    /// <summary>
    /// Cells of chunk (1, 1), whose scene is not open, and cells outside the world are skipped with a warning; the
    /// cells of open chunks are written.
    /// </summary>
    [Test]
    public void CellsOfAChunkNotOpen_OrOutsideTheWorld_AreSkippedWithAWarning()
    {
        Tilemap target = Layer(new Vector2Int(1, 0), grass);
        WorldBrushStroke.TryResolve(target.gameObject, out WorldBrushStroke.Target resolved);
        List<TileChangeData> changes = new()
        {
            Change(new Vector3Int(70, 63, 0)),
            Change(new Vector3Int(70, 64, 0)),
            Change(new Vector3Int(130, 10, 0)),
        };
        target.SetTile(new Vector3Int(70, 63, 0), null);

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("skipped 1 cells of chunk \\(1, 1\\).*not open; 1 cells of chunk \\(2, 0\\), which the world"));
        int skipped = WorldBrushStroke.Apply(resolved, changes);

        Assert.That(skipped, Is.EqualTo(2));
        Assert.That(target.GetTile(new Vector3Int(70, 63, 0)), Is.SameAs(sandTile));
    }

    /// <summary>
    /// On a tilemap that is no ground layer, walls or raised ground, a stroke across the border writes only the target
    /// chunk's cells and warns about the rest.
    /// </summary>
    [Test]
    public void OnATilemapOfAnotherKind_OnlyItsChunksCellsAreWritten()
    {
        Tilemap decor = chunks[Vector2Int.zero].transform.Find("Decor").GetComponent<Tilemap>();
        BoundsInt box = new(62, 5, 0, 4, 1, 1);

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("only ground layers, walls and raised ground paint across chunk borders"));
        Stroke(decor, () => brush.BoxFill(decor.layoutGrid, decor.gameObject, box));

        Assert.That(FilledCells(decor), Is.EquivalentTo(CellsOf(new RectInt(62, 5, 2, 1))));
    }

    /// <summary>
    /// A flood fill inside a ring of sand drawn across the border at x = 64 fills the ring's inside on both chunks and
    /// nothing outside it.
    /// </summary>
    [Test]
    public void FloodFill_SpreadsAcrossTheBorder_WithinItsBounds()
    {
        Tilemap target = Layer(Vector2Int.zero, sand);
        RectInt ring = new(60, 20, 9, 7);
        List<TileChangeData> outline = new();
        foreach (Vector2Int cell in ring.allPositionsWithin)
        {
            if (cell.x == ring.xMin || cell.y == ring.yMin || cell.x == ring.xMax - 1 || cell.y == ring.yMax - 1)
            {
                outline.Add(Change(new Vector3Int(cell.x, cell.y, 0)));
            }
        }

        WorldBrushStroke.TryResolve(target.gameObject, out WorldBrushStroke.Target resolved);
        WorldBrushStroke.Apply(resolved, outline);

        Stroke(target, () => brush.FloodFill(target.layoutGrid, target.gameObject, new Vector3Int(62, 23, 0)));

        Tilemap east = Layer(new Vector2Int(1, 0), sand);
        Assert.That(FilledCells(target).Count + FilledCells(east).Count, Is.EqualTo(ring.width * ring.height));
        Assert.That(east.GetTile(new Vector3Int(66, 23, 0)), Is.SameAs(sandTile));
        Assert.That(target.GetTile(new Vector3Int(59, 23, 0)), Is.Null);
        Assert.That(east.GetTile(new Vector3Int(69, 23, 0)), Is.Null);
    }

    /// <summary>
    /// The hover preview of a box across the border at x = 64 shows on both chunks' renderers, and clearing it clears
    /// both.
    /// </summary>
    [Test]
    public void ThePreview_ShowsOnTheRendererOfEachChunkItCovers()
    {
        Tilemap target = Layer(Vector2Int.zero, grass);
        TerrainBlendRenderer west = chunks[Vector2Int.zero].Terrain;
        TerrainBlendRenderer east = chunks[new Vector2Int(1, 0)].Terrain;

        editor.BoxPreview(target.layoutGrid, target.gameObject, new BoundsInt(60, 40, 0, 8, 2, 1), true);

        Assert.That(west.ControlMap.HasPreview, Is.True);
        Assert.That(east.ControlMap.HasPreview, Is.True);
        Assert.That(chunks[new Vector2Int(0, 1)].Terrain.ControlMap.HasPreview, Is.False);

        editor.ClearPreview();

        Assert.That(west.ControlMap.HasPreview, Is.False);
        Assert.That(east.ControlMap.HasPreview, Is.False);
    }

    /// <summary>
    /// On a grid that is no chunk, a box past where a chunk would end is written whole to the target tilemap, as the
    /// brush always painted an area.
    /// </summary>
    [Test]
    public void OnAStandaloneArea_TheBrushPaintsAsBefore()
    {
        Grid area = Track(new GameObject("Area", typeof(Grid))).GetComponent<Grid>();
        Tilemap ground = new GameObject("Ground", typeof(Tilemap)).GetComponent<Tilemap>();
        ground.transform.SetParent(area.transform, false);
        BoundsInt box = new(60, 10, 0, 8, 3, 1);

        Assert.That(WorldBrushStroke.TryResolve(ground.gameObject, out _), Is.False);
        Stroke(ground, () => brush.BoxFill(area, ground.gameObject, box));

        Assert.That(FilledCells(ground), Is.EquivalentTo(CellsOf(new RectInt(60, 10, 8, 3))));
    }

    /// <summary>
    /// Runs <paramref name="write"/> as one undo step after the editor registers its undo for
    /// <paramref name="target"/>, as the Tile Palette does for a stroke.
    /// </summary>
    private void Stroke(Tilemap target, Action write)
    {
        Undo.IncrementCurrentGroup();
        editor.RegisterUndo(target.gameObject, GridBrushBase.Tool.Paint);
        write();
        Undo.IncrementCurrentGroup();
    }

    /// <summary>
    /// A chunk of the world at <paramref name="coords"/>: a grid at the world's origin with its marker, a renderer in
    /// the project's style and an empty grass layer, as <see cref="WorldAuthoring.CreateChunk"/> makes one, with its
    /// cells filled with grass.
    /// </summary>
    private WorldChunk NewChunk(Vector2Int coords)
    {
        GameObject gridObject = Track(new GameObject($"Chunk {coords}", typeof(Grid)));
        gridObject.transform.position = new Vector3(world.Origin.x, world.Origin.y, 0f);
        gridObject.SetActive(false);
        WorldChunk chunk = gridObject.AddComponent<WorldChunk>();
        chunk.Configure(world, coords, null);
        gridObject.AddComponent<TerrainBlendRenderer>().Configure(style);
        Tilemap ground = GroundLayerMigration.AddLayer(gridObject.GetComponent<Grid>(), grass);
        List<Vector3Int> cells = CellsOf(ChunkCoords.Bounds(coords));
        TileBase[] tiles = new TileBase[cells.Count];
        Array.Fill(tiles, AssetDatabase.LoadAssetAtPath<TileBase>(TerrainTileWriter.TilePath(grass)));
        ground.SetTiles(cells.ToArray(), tiles);
        gridObject.SetActive(true);
        return chunk;
    }

    /// <summary>
    /// Walls under <paramref name="parent"/>: a data tilemap with its module and half-cell render tilemap painting the
    /// project's walls rule tile.
    /// </summary>
    private static void AddWalls(Transform parent)
    {
        GameObject data = new("CollisionTilemap", typeof(Tilemap));
        data.transform.SetParent(parent, false);
        GameObject render = new("RenderTilemap", typeof(Tilemap));
        render.transform.SetParent(data.transform, false);
        render.transform.localPosition = new Vector3(-0.5f, -0.5f, 0f);
        DualGridTilemapModule module = data.AddComponent<DualGridTilemapModule>();
        module.RenderTile = AssetDatabase.LoadAssetAtPath<DualGridRuleTile>(WallsRuleTilePath);
    }

    /// <summary>
    /// The tilemap of the layer drawing <paramref name="material"/> on the chunk at <paramref name="coords"/>, or null.
    /// </summary>
    private Tilemap Layer(Vector2Int coords, TerrainMaterial material)
    {
        return WorldBrushStroke.LayerOf(chunks[coords], material);
    }

    /// <summary>
    /// A change writing the sand tile into <paramref name="cell"/>.
    /// </summary>
    private TileChangeData Change(Vector3Int cell)
    {
        return new TileChangeData { position = cell, tile = sandTile, color = Color.white, transform = Matrix4x4.identity };
    }

    /// <summary>
    /// The cells of <paramref name="rect"/> at z = 0.
    /// </summary>
    private static List<Vector3Int> CellsOf(RectInt rect)
    {
        List<Vector3Int> cells = new();
        foreach (Vector2Int cell in rect.allPositionsWithin)
        {
            cells.Add(new Vector3Int(cell.x, cell.y, 0));
        }

        return cells;
    }

    /// <summary>
    /// The filled cells of <paramref name="tilemap"/>.
    /// </summary>
    private static List<Vector3Int> FilledCells(Tilemap tilemap)
    {
        List<Vector3Int> filled = new();
        foreach (Vector3Int cell in tilemap.cellBounds.allPositionsWithin)
        {
            if (tilemap.HasTile(cell))
            {
                filled.Add(cell);
            }
        }

        return filled;
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
