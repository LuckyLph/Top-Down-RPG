using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using UnityEngine.Tilemaps;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

/// <summary>
/// Tests for <see cref="TerrainBrush"/> and <see cref="TerrainBrushEditor"/> on a grid of their own: a terrain renderer
/// over a plain grass layer (a block painted with the palette's tile), a dual-grid gravel layer and an empty plain stone
/// layer, beside a plain tilemap and a dual-grid walls tilemap that no renderer draws. Strokes run as the Tile Palette
/// runs them, the editor's undo registration and then the brush. On a ground layer, paint, erase, box fill and box
/// erase write exactly the stroke's cells, the palette's tile on a plain layer and the module's data tile on a dual-grid
/// one, and an undo restores the tilemaps and the control map; elsewhere the brush is the Dual Grid Brush, and so is its
/// preview on the layers of a renderer that is off. The editor's previews write preview bits only, on the cells the
/// click would write, a new one replacing the last; a paint or a box shows an erase while shift is held, and pressing
/// or releasing shift redoes the preview; they are cleared when the stroke is committed or ends, the pointer leaves,
/// the tool changes or the editor goes; a flood fill previews exactly the cells it then fills; raised ground previews
/// through the renderer's elevation map; walls painted beside the ground get the ground under them; updating the
/// previews allocates nothing; and a stroke still reaches a built navigation grid in edit mode (Docs/KnownGaps.md,
/// Phase 6, for the flood fill, raised ground and walls).
/// </summary>
public class TerrainBrushTests
{
    private readonly List<Object> createdObjects = new();
    private GameObject root;
    private Grid grid;
    private Tilemap[] layers;
    private Tilemap decor;
    private Tilemap walls;
    private DualGridTilemapModule gravelModule;
    private DualGridTilemapModule wallsModule;
    private Tile paletteTile;
    private TerrainBlendRenderer renderer;
    private TerrainBrush brush;
    private TerrainBrushEditor editor;

    /// <summary>
    /// Builds the grid, its five tilemaps (grass painted over a 10 x 6 block), the renderer over the three ground
    /// layers with its own style and materials, and a one-cell brush holding the palette's tile with its editor.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        root = Track(new GameObject("TerrainBrushGrid", typeof(Grid)));
        root.SetActive(false);
        grid = root.GetComponent<Grid>();
        paletteTile = Track(ScriptableObject.CreateInstance<Tile>());
        paletteTile.name = "Palette Tile";
        layers = new[] { CreateTilemap("Grass"), CreateTilemap("Gravel"), CreateTilemap("Stone") };
        gravelModule = AddModule(layers[1], "Gravel Rule Tile");
        decor = CreateTilemap("Decor");
        walls = CreateTilemap("Walls");
        wallsModule = AddModule(walls, "Walls Rule Tile");
        for (int y = 0; y < 6; y++)
        {
            for (int x = 0; x < 10; x++)
            {
                layers[0].SetTile(new Vector3Int(x, y, 0), paletteTile);
            }
        }

        TerrainStyle style = TestTerrainStyle.Create(createdObjects);
        TerrainMaterial[] materials = TestTerrainStyle.PlainMaterials(createdObjects);
        renderer = root.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(style, new TerrainBlendRenderer.Layer(layers[0], materials[0]), new TerrainBlendRenderer.Layer(layers[1], materials[1]), new TerrainBlendRenderer.Layer(layers[2], materials[2]));
        root.SetActive(true);

        brush = Track(ScriptableObject.CreateInstance<TerrainBrush>());
        brush.SetTile(Vector3Int.zero, paletteTile);
        editor = (TerrainBrushEditor)Editor.CreateEditor(brush);
    }

    /// <summary>
    /// Destroys the editor first (which clears its preview), clears the undo history of every tilemap and destroys
    /// what the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        if (editor != null)
        {
            Object.DestroyImmediate(editor);
        }

        foreach (Tilemap tilemap in root.GetComponentsInChildren<Tilemap>(true))
        {
            Undo.ClearUndo(tilemap);
        }

        for (int i = createdObjects.Count - 1; i >= 0; i--)
        {
            if (createdObjects[i] != null)
            {
                Object.DestroyImmediate(createdObjects[i]);
            }
        }

        createdObjects.Clear();
    }

    /// <summary>
    /// Painting an empty cell of a plain ground layer writes the palette's tile there and nowhere else, and the
    /// control map follows; an undo restores both.
    /// </summary>
    [Test]
    public void Paint_OnAPlainGroundLayer_WritesThePalettesTile_AndAnUndoRestoresIt()
    {
        Tilemap stone = layers[2];
        Vector3Int cell = new(3, 3, 0);

        Stroke(GridBrushBase.Tool.Paint, stone, () => brush.Paint(grid, stone.gameObject, cell));

        Assert.That(FilledCells(stone), Is.EquivalentTo(new[] { cell }));
        Assert.That(stone.GetTile(cell), Is.SameAs(paletteTile));
        Assert.That(renderer.ControlMap.GetCell(cell), Is.EqualTo(5));

        Undo.PerformUndo();

        Assert.That(FilledCells(stone), Is.Empty);
        Assert.That(renderer.ControlMap.GetCell(cell), Is.EqualTo(1));
    }

    /// <summary>
    /// Erasing a cell of a plain ground layer empties exactly that cell, and an undo brings it and its control map
    /// bits back.
    /// </summary>
    [Test]
    public void Erase_OnAPlainGroundLayer_EmptiesTheCell_AndAnUndoRestoresIt()
    {
        Tilemap grass = layers[0];
        Vector3Int cell = new(4, 2, 0);
        List<Vector3Int> before = FilledCells(grass);

        Stroke(GridBrushBase.Tool.Erase, grass, () => brush.Erase(grid, grass.gameObject, cell));

        List<Vector3Int> expected = new(before);
        expected.Remove(cell);
        Assert.That(FilledCells(grass), Is.EquivalentTo(expected));
        Assert.That(renderer.ControlMap.GetCell(cell), Is.EqualTo(0));

        Undo.PerformUndo();

        Assert.That(FilledCells(grass), Is.EquivalentTo(before));
        Assert.That(grass.GetTile(cell), Is.SameAs(paletteTile));
        Assert.That(renderer.ControlMap.GetCell(cell), Is.EqualTo(1));
    }

    /// <summary>
    /// A box fill of a plain ground layer writes the palette's tile into exactly the box, and an undo empties it again.
    /// </summary>
    [Test]
    public void BoxFill_OnAPlainGroundLayer_WritesExactlyTheBox_AndAnUndoRestoresIt()
    {
        Tilemap stone = layers[2];
        BoundsInt box = new(1, 1, 0, 3, 2, 1);

        Stroke(GridBrushBase.Tool.Box, stone, () => brush.BoxFill(grid, stone.gameObject, box));

        Assert.That(FilledCells(stone), Is.EquivalentTo(CellsIn(box)));
        foreach (Vector3Int cell in box.allPositionsWithin)
        {
            Assert.That(stone.GetTile(cell), Is.SameAs(paletteTile));
            Assert.That(renderer.ControlMap.GetCell(cell), Is.EqualTo(5));
        }

        Undo.PerformUndo();

        Assert.That(FilledCells(stone), Is.Empty);
        Assert.That(renderer.ControlMap.GetCell(new Vector3Int(2, 2, 0)), Is.EqualTo(1));
    }

    /// <summary>
    /// A box erase of a plain ground layer empties exactly the box, and an undo fills it again.
    /// </summary>
    [Test]
    public void BoxErase_OnAPlainGroundLayer_EmptiesExactlyTheBox_AndAnUndoRestoresIt()
    {
        Tilemap grass = layers[0];
        BoundsInt box = new(2, 1, 0, 4, 3, 1);
        List<Vector3Int> before = FilledCells(grass);

        Stroke(GridBrushBase.Tool.Box, grass, () => brush.BoxErase(grid, grass.gameObject, box));

        List<Vector3Int> expected = new(before);
        expected.RemoveAll(cell => box.Contains(cell));
        Assert.That(FilledCells(grass), Is.EquivalentTo(expected));
        Assert.That(renderer.ControlMap.GetCell(new Vector3Int(3, 2, 0)), Is.EqualTo(0));

        Undo.PerformUndo();

        Assert.That(FilledCells(grass), Is.EquivalentTo(before));
        Assert.That(renderer.ControlMap.GetCell(new Vector3Int(3, 2, 0)), Is.EqualTo(1));
    }

    /// <summary>
    /// On a dual-grid ground layer a paint and a box fill write the module's data tile, never the palette's, into
    /// exactly the stroke's cells, and an erase empties its cell; undoing each restores the data tilemap, the render
    /// tilemap and the control map.
    /// </summary>
    [Test]
    public void StrokesOnADualGridGroundLayer_WriteTheModulesDataTile_AndUndosRestoreThem()
    {
        Tilemap gravel = layers[1];
        Vector3Int cell = new(6, 4, 0);
        BoundsInt box = new(1, 1, 0, 2, 3, 1);

        Stroke(GridBrushBase.Tool.Paint, gravel, () => brush.Paint(grid, gravel.gameObject, cell));
        Assert.That(FilledCells(gravel), Is.EquivalentTo(new[] { cell }));
        Assert.That(gravel.GetTile(cell), Is.SameAs(gravelModule.DataTile));
        Assert.That(renderer.ControlMap.GetCell(cell), Is.EqualTo(3));
        int renderTiles = FilledCells(gravelModule.RenderTilemap).Count;
        Assert.That(renderTiles, Is.EqualTo(4));

        Stroke(GridBrushBase.Tool.Box, gravel, () => brush.BoxFill(grid, gravel.gameObject, box));
        List<Vector3Int> filled = CellsIn(box);
        filled.Add(cell);
        Assert.That(FilledCells(gravel), Is.EquivalentTo(filled));
        foreach (Vector3Int boxCell in box.allPositionsWithin)
        {
            Assert.That(gravel.GetTile(boxCell), Is.SameAs(gravelModule.DataTile));
        }

        Stroke(GridBrushBase.Tool.Erase, gravel, () => brush.Erase(grid, gravel.gameObject, cell));
        Assert.That(FilledCells(gravel), Is.EquivalentTo(CellsIn(box)));
        Assert.That(renderer.ControlMap.GetCell(cell), Is.EqualTo(1));

        Undo.PerformUndo();
        Assert.That(FilledCells(gravel), Is.EquivalentTo(filled));
        Assert.That(renderer.ControlMap.GetCell(cell), Is.EqualTo(3));

        Undo.PerformUndo();
        Assert.That(FilledCells(gravel), Is.EquivalentTo(new[] { cell }));
        Assert.That(FilledCells(gravelModule.RenderTilemap).Count, Is.EqualTo(renderTiles));
        Assert.That(renderer.ControlMap.GetCell(new Vector3Int(1, 1, 0)), Is.EqualTo(1));

        Undo.PerformUndo();
        Assert.That(FilledCells(gravel), Is.Empty);
        Assert.That(FilledCells(gravelModule.RenderTilemap), Is.Empty);
        Assert.That(renderer.ControlMap.GetCell(cell), Is.EqualTo(1));
    }

    /// <summary>
    /// On tilemaps no renderer draws the brush is the Dual Grid Brush: a plain tilemap gets the palette's tile and
    /// Unity's preview tile, a dual-grid one its module's data tile and the module's render preview, and the renderer
    /// shows no preview for either.
    /// </summary>
    [Test]
    public void OnTilemapsNoRendererDraws_TheBrushIsTheDualGridBrush()
    {
        Vector3Int cell = new(2, 8, 0);
        Vector3Int hovered = new(5, 8, 0);

        Stroke(GridBrushBase.Tool.Paint, decor, () => brush.Paint(grid, decor.gameObject, cell));
        Stroke(GridBrushBase.Tool.Paint, walls, () => brush.Paint(grid, walls.gameObject, cell));

        Assert.That(decor.GetTile(cell), Is.SameAs(paletteTile));
        Assert.That(walls.GetTile(cell), Is.SameAs(wallsModule.DataTile));
        Assert.That(FilledCells(wallsModule.RenderTilemap).Count, Is.EqualTo(4));

        editor.OnToolActivated(GridBrushBase.Tool.Paint);
        editor.PaintPreview(grid, decor.gameObject, hovered);
        Assert.That(decor.GetEditorPreviewTile(hovered), Is.SameAs(paletteTile));
        Assert.That(renderer.ControlMap.HasPreview, Is.False);
        editor.ClearPreview();
        Assert.That(decor.GetEditorPreviewTile(hovered), Is.Null);

        try
        {
            editor.PaintPreview(grid, walls.gameObject, hovered);
            for (int corner = 0; corner < DualGridUtils.CornerCount; corner++)
            {
                Assert.That(wallsModule.RenderTilemap.GetEditorPreviewTile(DualGridUtils.GetRenderTilePosition(hovered, corner)), Is.SameAs(wallsModule.RenderTile));
            }

            Assert.That(walls.HasTile(hovered), Is.False);
            Assert.That(renderer.ControlMap.HasPreview, Is.False);
        }
        finally
        {
            wallsModule.ClearAllPreviewTiles();
        }
    }

    /// <summary>
    /// The paint, erase and box fill previews write preview bits for the target layer only: filled for a paint or a
    /// box fill, empty for an erase, each replacing the last, with no tile, editor preview tile or change mark on any
    /// tilemap.
    /// </summary>
    [Test]
    public void Previews_WritePreviewBitsOnly()
    {
        Vector3Int cell = new(3, 2, 0);
        BoundsInt box = new(5, 1, 0, 2, 2, 1);
        int[] dirtyCounts = DirtyCounts();

        editor.PaintPreview(grid, layers[2].gameObject, cell);
        Assert.That(renderer.ControlMap.GetShownCell(cell), Is.EqualTo(5));
        Assert.That(renderer.ControlMap.GetCell(cell), Is.EqualTo(1));

        editor.ErasePreview(grid, layers[0].gameObject, cell);
        Assert.That(renderer.ControlMap.GetShownCell(cell), Is.EqualTo(0), "The erase replaced the paint.");
        Assert.That(renderer.ControlMap.GetCell(cell), Is.EqualTo(1));

        editor.BoxFillPreview(grid, layers[1].gameObject, box);
        Assert.That(renderer.ControlMap.GetShownCell(cell), Is.EqualTo(1), "The box fill replaced the erase.");
        foreach (Vector3Int boxCell in box.allPositionsWithin)
        {
            Assert.That(renderer.ControlMap.GetShownCell(boxCell), Is.EqualTo(3));
            Assert.That(renderer.ControlMap.GetCell(boxCell), Is.EqualTo(1));
        }

        Assert.That(FilledCells(layers[1]), Is.Empty);
        Assert.That(FilledCells(layers[2]), Is.Empty);
        Assert.That(FilledCells(layers[0]).Count, Is.EqualTo(60));
        foreach (Tilemap layer in layers)
        {
            Assert.That(layer.GetEditorPreviewTile(cell), Is.Null);
            Assert.That(layer.GetEditorPreviewTile(box.min), Is.Null);
        }

        Assert.That(FilledCells(gravelModule.RenderTilemap), Is.Empty);
        Assert.That(DirtyCounts(), Is.EqualTo(dirtyCounts));
        renderer.FlushPendingChanges();
        Assert.That(TexelOf(box.min), Is.EqualTo(3));
    }

    /// <summary>
    /// A preview covers the cells the click would write: on a dual-grid layer every cell of the stroke, even where
    /// the brush holds no tile; on a plain layer, when filling, only the cells whose brush cell holds one; and every
    /// cell when erasing.
    /// </summary>
    [Test]
    public void Previews_CoverTheCellsTheClickWrites()
    {
        brush.Init(new Vector3Int(2, 1, 1));
        brush.SetTile(Vector3Int.zero, paletteTile);
        Vector3Int left = new(3, 7, 0);
        Vector3Int right = new(4, 7, 0);

        editor.PaintPreview(grid, layers[2].gameObject, left);
        Assert.That(renderer.ControlMap.GetShownCell(left), Is.EqualTo(4));
        Assert.That(renderer.ControlMap.GetShownCell(right), Is.EqualTo(0), "The brush holds no tile there.");

        editor.PaintPreview(grid, layers[1].gameObject, left);
        Assert.That(renderer.ControlMap.GetShownCell(left), Is.EqualTo(2));
        Assert.That(renderer.ControlMap.GetShownCell(right), Is.EqualTo(2), "A dual-grid layer gets its data tile in every cell.");

        Stroke(GridBrushBase.Tool.Paint, layers[1], () => brush.Paint(grid, layers[1].gameObject, left));
        Stroke(GridBrushBase.Tool.Paint, layers[2], () => brush.Paint(grid, layers[2].gameObject, left));
        Assert.That(FilledCells(layers[1]), Is.EquivalentTo(new[] { left, right }));
        Assert.That(FilledCells(layers[2]), Is.EquivalentTo(new[] { left }));

        editor.ErasePreview(grid, layers[1].gameObject, left);
        Assert.That(renderer.ControlMap.GetShownCell(left), Is.EqualTo(4));
        Assert.That(renderer.ControlMap.GetShownCell(right), Is.EqualTo(0));
    }

    /// <summary>
    /// While shift is held a click of the Paint tool and the box tool's release erase, so their previews show the
    /// cells empty.
    /// </summary>
    [Test]
    public void ThePaintAndBoxPreviews_WhileErasing_ShowTheCellsEmpty()
    {
        BoundsInt box = new(2, 2, 0, 3, 2, 1);
        Vector3Int cell = new(7, 4, 0);

        editor.BoxPreview(grid, layers[0].gameObject, box, true);

        foreach (Vector3Int boxCell in box.allPositionsWithin)
        {
            Assert.That(renderer.ControlMap.GetShownCell(boxCell), Is.EqualTo(0));
            Assert.That(renderer.ControlMap.GetCell(boxCell), Is.EqualTo(1));
        }

        editor.PaintPreview(grid, layers[0].gameObject, cell, true);

        Assert.That(renderer.ControlMap.GetShownCell(cell), Is.EqualTo(0));
        Assert.That(renderer.ControlMap.GetShownCell(box.min), Is.EqualTo(1), "The paint preview replaced the box.");
        Assert.That(FilledCells(layers[0]).Count, Is.EqualTo(60));
    }

    /// <summary>
    /// Pressing or releasing shift redoes the hovered paint or box preview as an erase or a fill, since the grid brush
    /// editor would not refresh it; while shift does not change nothing is redone.
    /// </summary>
    [Test]
    public void PressingOrReleasingShift_RedoesThePreview()
    {
        Vector3Int cell = new(7, 4, 0);
        BoundsInt hovered = new(cell, Vector3Int.one);
        BoundsInt box = new(2, 2, 0, 3, 2, 1);

        editor.FollowShift(layers[0].gameObject, hovered, GridBrushBase.Tool.Paint, true);
        Assert.That(renderer.ControlMap.GetShownCell(cell), Is.EqualTo(0));

        editor.FollowShift(layers[2].gameObject, hovered, GridBrushBase.Tool.Paint, false);
        Assert.That(renderer.ControlMap.GetShownCell(cell), Is.EqualTo(5));

        editor.ClearPreview();
        editor.FollowShift(layers[2].gameObject, hovered, GridBrushBase.Tool.Paint, false);
        Assert.That(renderer.ControlMap.HasPreview, Is.False, "Shift did not change.");

        editor.FollowShift(layers[0].gameObject, box, GridBrushBase.Tool.Box, true);
        Assert.That(renderer.ControlMap.GetShownCell(box.min), Is.EqualTo(0));
        Assert.That(renderer.ControlMap.GetShownCell(cell), Is.EqualTo(1));
    }

    /// <summary>
    /// On a ground layer of a renderer that is off, the editor shows the Dual Grid Brush's preview instead: the module's
    /// render preview on a dual-grid layer and Unity's preview tile on a plain one.
    /// </summary>
    [Test]
    public void OnTheLayersOfARendererThatIsOff_TheDualGridBrushPreviewShows()
    {
        Vector3Int cell = new(5, 8, 0);
        renderer.enabled = false;
        editor.OnToolActivated(GridBrushBase.Tool.Paint);

        editor.PaintPreview(grid, layers[2].gameObject, cell);
        Assert.That(layers[2].GetEditorPreviewTile(cell), Is.SameAs(paletteTile));
        editor.ClearPreview();
        Assert.That(layers[2].GetEditorPreviewTile(cell), Is.Null);

        try
        {
            editor.PaintPreview(grid, layers[1].gameObject, cell);
            for (int corner = 0; corner < DualGridUtils.CornerCount; corner++)
            {
                Assert.That(gravelModule.RenderTilemap.GetEditorPreviewTile(DualGridUtils.GetRenderTilePosition(cell, corner)), Is.SameAs(gravelModule.RenderTile));
            }

            Assert.That(layers[1].HasTile(cell), Is.False);
        }
        finally
        {
            gravelModule.ClearAllPreviewTiles();
        }
    }

    /// <summary>
    /// A preview over another cell, on the same layer or another one, replaces the last one, even without a clear in
    /// between.
    /// </summary>
    [Test]
    public void ANewPreview_ReplacesTheLast()
    {
        Vector3Int first = new(2, 2, 0);
        Vector3Int second = new(7, 3, 0);

        editor.PaintPreview(grid, layers[2].gameObject, first);
        Assert.That(renderer.ControlMap.GetShownCell(first), Is.EqualTo(5));

        editor.PaintPreview(grid, layers[2].gameObject, second);
        Assert.That(renderer.ControlMap.GetShownCell(first), Is.EqualTo(1));
        Assert.That(renderer.ControlMap.GetShownCell(second), Is.EqualTo(5));

        editor.PaintPreview(grid, layers[1].gameObject, first);
        Assert.That(renderer.ControlMap.GetShownCell(second), Is.EqualTo(1));
        Assert.That(renderer.ControlMap.GetShownCell(first), Is.EqualTo(3));
    }

    /// <summary>
    /// The preview is gone once the stroke ends (the mouse is released), once a stroke is committed, when the pointer
    /// leaves the Scene view, when the tool is deactivated and when the editor is destroyed.
    /// </summary>
    [Test]
    public void ThePreview_IsClearedWhenTheStrokeEndsOrThePointerLeaves()
    {
        Vector3Int cell = new(4, 4, 0);

        editor.PaintPreview(grid, layers[2].gameObject, cell);
        editor.FollowStroke(true);
        Assert.That(renderer.ControlMap.HasPreview, Is.True, "The preview stays while the stroke goes on.");
        editor.FollowStroke(false);
        Assert.That(renderer.ControlMap.HasPreview, Is.False, "Releasing the mouse ends the stroke.");

        editor.PaintPreview(grid, layers[2].gameObject, cell);
        editor.RegisterUndo(layers[2].gameObject, GridBrushBase.Tool.Paint);
        Assert.That(renderer.ControlMap.HasPreview, Is.False, "Committing the stroke clears it.");

        editor.PaintPreview(grid, layers[2].gameObject, cell);
        editor.OnMouseLeave();
        Assert.That(renderer.ControlMap.HasPreview, Is.False, "Leaving the Scene view clears it.");

        editor.PaintPreview(grid, layers[2].gameObject, cell);
        editor.OnToolDeactivated(GridBrushBase.Tool.Paint);
        Assert.That(renderer.ControlMap.HasPreview, Is.False, "Changing the tool clears it.");

        editor.PaintPreview(grid, layers[2].gameObject, cell);
        Object.DestroyImmediate(editor);
        Assert.That(renderer.ControlMap.HasPreview, Is.False, "Losing the editor clears it.");
        renderer.FlushPendingChanges();
        Assert.That(TexelOf(cell), Is.EqualTo(1));
    }

    /// <summary>
    /// A flood fill on a ground layer previews the cells it would fill through the renderer, no editor preview tile, and
    /// filling then writes the palette's tile into exactly those cells: an enclosed two-cell hole of a plain layer, and
    /// from an empty cell beside the block the whole empty region within the tilemap's bounds.
    /// </summary>
    [Test]
    public void FloodFill_OnAGroundLayer_PreviewsTheCellsItFills_AndFillsThem()
    {
        Tilemap grass = layers[0];
        Vector3Int hole = new(4, 3, 0);
        grass.SetTile(hole, null);
        grass.SetTile(hole + Vector3Int.right, null);

        editor.FloodFillPreview(grid, grass.gameObject, hole);

        Assert.That(renderer.ControlMap.HasPreview, Is.True);
        Assert.That(renderer.ControlMap.GetShownCell(hole), Is.EqualTo(1));
        Assert.That(renderer.ControlMap.GetShownCell(hole + Vector3Int.right), Is.EqualTo(1));
        Assert.That(renderer.ControlMap.GetShownCell(new Vector3Int(12, 3, 0)), Is.EqualTo(0));
        Assert.That(grass.GetEditorPreviewTile(hole), Is.Null);

        Stroke(GridBrushBase.Tool.FloodFill, grass, () => brush.FloodFill(grid, grass.gameObject, hole));

        Assert.That(renderer.ControlMap.HasPreview, Is.False);
        Assert.That(grass.GetTile(hole), Is.SameAs(paletteTile));
        Assert.That(FilledCells(grass).Count, Is.EqualTo(60));
        Assert.That(renderer.ControlMap.GetCell(hole), Is.EqualTo(1));

        grass.SetTile(new Vector3Int(9, 0, 0), null);
        grass.SetTile(new Vector3Int(9, 1, 0), null);
        List<Vector3Int> previewed = new();
        TerrainBrush.FloodCells(grass, new Vector3Int(9, 0, 0), paletteTile, previewed);
        Assert.That(previewed, Is.EquivalentTo(new[] { new Vector3Int(9, 0, 0), new Vector3Int(9, 1, 0) }), "within the tilemap's bounds");
        Stroke(GridBrushBase.Tool.FloodFill, grass, () => brush.FloodFill(grid, grass.gameObject, new Vector3Int(9, 0, 0)));
        Assert.That(FilledCells(grass).Count, Is.EqualTo(60));
    }

    /// <summary>
    /// On raised ground, a paint, an erase and a flood fill preview through the renderer's elevation map: the cells show
    /// raised while the raised tilemap stays as it was, an erase shows raised cells level, and clearing the preview
    /// shows the tilemap's own again.
    /// </summary>
    [Test]
    public void OnRaisedGround_ThePreviewShowsThroughTheElevationMap()
    {
        GameObject raisedObject = new("Raised", typeof(Tilemap));
        raisedObject.transform.SetParent(root.transform, false);
        Tilemap raised = raisedObject.GetComponent<Tilemap>();
        RaisedGroundTile raisedTile = Track(RaisedGroundTile.Create(false));
        raised.SetTile(new Vector3Int(2, 2, 0), raisedTile);
        renderer.Configure(renderer.Style, raised, new TerrainBlendRenderer.Layer(layers[0], renderer.Layers[0].Material), new TerrainBlendRenderer.Layer(layers[1], renderer.Layers[1].Material), new TerrainBlendRenderer.Layer(layers[2], renderer.Layers[2].Material));
        renderer.Rebuild();
        brush.SetTile(Vector3Int.zero, raisedTile);
        Vector3Int cell = new(5, 2, 0);

        editor.PaintPreview(grid, raised.gameObject, cell, false);
        renderer.FlushPendingChanges();

        Assert.That(renderer.ElevationMap.GetCell(cell), Is.EqualTo(RaisedGround.Raised));
        Assert.That(raised.HasTile(cell), Is.False);

        editor.PaintPreview(grid, raised.gameObject, new Vector3Int(2, 2, 0), true);
        renderer.FlushPendingChanges();
        Assert.That(renderer.ElevationMap.GetCell(new Vector3Int(2, 2, 0)), Is.EqualTo(RaisedGround.Level));
        Assert.That(renderer.ElevationMap.GetCell(cell), Is.EqualTo(RaisedGround.Level), "a new preview replaces the last");

        editor.ClearPreview();
        renderer.FlushPendingChanges();
        Assert.That(renderer.ElevationMap.GetCell(new Vector3Int(2, 2, 0)), Is.EqualTo(RaisedGround.Raised));

        Assert.That(editor.TryShowFloodPreview(raised.gameObject, cell), Is.True);
        renderer.FlushPendingChanges();
        Assert.That(renderer.ElevationMap.GetCell(cell), Is.EqualTo(RaisedGround.Raised));
        Assert.That(renderer.ElevationMap.GetCell(new Vector3Int(3, 2, 0)), Is.EqualTo(RaisedGround.Raised), "the flood fills the empty cells joined to it");
        Assert.That(renderer.ElevationMap.GetCell(new Vector3Int(5, 3, 0)), Is.EqualTo(RaisedGround.Level), "within the raised tilemap's bounds, the start cell taken in");
        editor.ClearPreview();
    }

    /// <summary>
    /// Walls box filled beside the ground, on cells with no ground, get the ground of their neighbours under them, the
    /// tile their neighbours hold, so the ground's corner leaves no gap at the wall; a wall cell with no ground beside it
    /// gets none, and one undo takes walls and ground back (Docs/KnownGaps.md, Phase 6).
    /// </summary>
    [Test]
    public void BoxFill_OnWallsBesideTheGround_PaintsTheGroundUnderThem()
    {
        BoundsInt box = new(10, 0, 0, 1, 7, 1);

        Stroke(GridBrushBase.Tool.Box, walls, () => brush.BoxFill(grid, walls.gameObject, box));

        for (int y = 0; y < 7; y++)
        {
            Assert.That(walls.HasTile(new Vector3Int(10, y, 0)), Is.True);
            Assert.That(layers[0].GetTile(new Vector3Int(10, y, 0)), Is.SameAs(paletteTile), $"ground under the wall at (10, {y})");
        }

        Stroke(GridBrushBase.Tool.Box, walls, () => brush.BoxFill(grid, walls.gameObject, new BoundsInt(15, 0, 0, 1, 1, 1)));
        Assert.That(layers[0].HasTile(new Vector3Int(15, 0, 0)), Is.False, "no ground beside it");

        Undo.PerformUndo();
        Undo.PerformUndo();
        Assert.That(layers[0].HasTile(new Vector3Int(10, 0, 0)), Is.False);
        Assert.That(walls.HasTile(new Vector3Int(10, 0, 0)), Is.False);
    }

    /// <summary>
    /// Once the previews' list has held a stroke, updating the paint, erase and box fill previews allocates nothing.
    /// </summary>
    [Test]
    public void UpdatingThePreview_AllocatesNothing()
    {
        for (int x = 0; x < 3; x++)
        {
            UpdatePreviews(x);
        }

        Assert.That(() =>
        {
            for (int x = 0; x < 8; x++)
            {
                UpdatePreviews(x);
            }
        }, Is.Not.AllocatingGCMemory());
        Assert.That(renderer.ControlMap.HasPreview, Is.True);

        void UpdatePreviews(int x)
        {
            editor.PaintPreview(grid, layers[2].gameObject, new Vector3Int(x, 2, 0));
            editor.ErasePreview(grid, layers[0].gameObject, new Vector3Int(x, 3, 0));
            editor.BoxFillPreview(grid, layers[1].gameObject, new BoundsInt(x, 1, 0, 3, 2, 1));
            renderer.FlushPendingChanges();
        }
    }

    /// <summary>
    /// Outside play mode, a stroke on a ground layer reaches a built navigation grid through the editor's tile change
    /// listener, so its gizmo shows the new cell.
    /// </summary>
    [Test]
    public void AStrokeOutsidePlayMode_UpdatesABuiltNavigationGrid()
    {
        TerrainType2D ground = Track(TerrainType2D.Create("ground"));
        NavigationTerrainSource2D source = layers[0].gameObject.AddComponent<NavigationTerrainSource2D>();
        source.Configure(ground, 0);
        NavigationGrid2D navigation = root.AddComponent<NavigationGrid2D>();
        navigation.ConfigureSources(source);
        navigation.BuildGrid();
        Vector3Int cell = new(12, 2, 0);
        int version = navigation.Version;
        Assert.That(navigation.IsCellWalkable(cell), Is.False);

        Stroke(GridBrushBase.Tool.Paint, layers[0], () => brush.Paint(grid, layers[0].gameObject, cell));

        Assert.That(navigation.IsCellWalkable(cell), Is.True);
        Assert.That(navigation.Version, Is.GreaterThan(version));
    }

    /// <summary>
    /// Runs <paramref name="write"/> as one undo step after the editor registers its undo for
    /// <paramref name="target"/>, as the Tile Palette does for a stroke.
    /// </summary>
    private void Stroke(GridBrushBase.Tool tool, Tilemap target, Action write)
    {
        Undo.IncrementCurrentGroup();
        editor.RegisterUndo(target.gameObject, tool);
        write();
        Undo.IncrementCurrentGroup();
    }

    /// <summary>
    /// Creates a tilemap named <paramref name="name"/> under the grid, without a renderer.
    /// </summary>
    private Tilemap CreateTilemap(string name)
    {
        GameObject tilemapObject = new(name, typeof(Tilemap));
        tilemapObject.transform.SetParent(root.transform, false);
        return tilemapObject.GetComponent<Tilemap>();
    }

    /// <summary>
    /// Makes <paramref name="data"/> a dual-grid data tilemap: a render tilemap under it and a module painting a fresh
    /// rule tile.
    /// </summary>
    private DualGridTilemapModule AddModule(Tilemap data, string ruleTileName)
    {
        GameObject renderObject = new("RenderTilemap", typeof(Tilemap));
        renderObject.transform.SetParent(data.transform, false);
        renderObject.transform.localPosition = new Vector3(-0.5f, -0.5f, 0f);
        DualGridRuleTile ruleTile = Track(ScriptableObject.CreateInstance<DualGridRuleTile>());
        ruleTile.name = ruleTileName;
        Track(ruleTile.RefreshDataTile());
        DualGridTilemapModule module = data.gameObject.AddComponent<DualGridTilemapModule>();
        module.RenderTile = ruleTile;
        return module;
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
    /// Every cell of <paramref name="bounds"/>.
    /// </summary>
    private static List<Vector3Int> CellsIn(BoundsInt bounds)
    {
        List<Vector3Int> cells = new();
        foreach (Vector3Int cell in bounds.allPositionsWithin)
        {
            cells.Add(cell);
        }

        return cells;
    }

    /// <summary>
    /// How often each of the grid's tilemaps has been marked changed.
    /// </summary>
    private int[] DirtyCounts()
    {
        Tilemap[] tilemaps = root.GetComponentsInChildren<Tilemap>(true);
        int[] counts = new int[tilemaps.Length];
        for (int i = 0; i < tilemaps.Length; i++)
        {
            counts[i] = EditorUtility.GetDirtyCount(tilemaps[i]);
        }

        return counts;
    }

    /// <summary>
    /// The control map texture's texel for <paramref name="cell"/>: 16 bits in a narrow map, 32 in a wide one.
    /// </summary>
    private uint TexelOf(Vector3Int cell)
    {
        TerrainControlMap map = renderer.ControlMap;
        BoundsInt bounds = map.Bounds;
        int index = (cell.y - bounds.yMin) * bounds.size.x + cell.x - bounds.xMin;
        return map.IsWide ? map.Texture.GetPixelData<uint>(0)[index] : map.Texture.GetPixelData<ushort>(0)[index];
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
