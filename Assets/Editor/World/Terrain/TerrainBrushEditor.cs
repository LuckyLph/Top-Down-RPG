using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Tilemaps;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// The editor of <see cref="TerrainBrush"/>. On a ground layer, a tilemap that a <see cref="TerrainBlendRenderer"/>
/// (<see cref="TerrainBlendRenderer.FindLayerDrawing"/>) is drawing (<see cref="TerrainBlendRenderer.IsDrawing"/>), it
/// previews a paint, an erase or a box fill by writing preview bits for that layer into the renderer's control map
/// (<see cref="TerrainBlendRenderer.SetPreview"/>): filled for a paint or a box fill, empty for an erase, and empty for
/// a paint or a box while shift is held, since the click then erases. It shows exactly the cells the click writes at
/// z = 0: on a dual-grid layer every cell of the stroke, since the brush writes the module's data tile there, and on a
/// plain layer, when filling, only the cells whose brush cell holds a tile. It never writes the tilemap, navigation or
/// anything saved, and a new preview replaces the last. A flood fill shows no preview on a ground layer (see
/// Docs/TerrainBlending.md's Phase 3 notes). The preview is cleared when the stroke is committed
/// (<see cref="RegisterUndo"/>) or ends (the mouse is released), when the pointer leaves the Scene view, the tool or the
/// brush changes, the paint target changes, after an undo or redo, when a scene closes, before an assembly reload and
/// when the editor is disabled. During the Scene view's GUI a clear waits for the end of it, so a hover that replaces
/// the preview uploads once; a clear that happens, and a preview that added chunks, have the Scene view repaint so
/// they show at once. Updating the preview allocates nothing once its list holds the largest stroke. In the Tile
/// Palette it warns when the picked tile is a ground material's that the target layer is not drawn with
/// (<see cref="PaletteProblem"/>), naming the area's layer for it, or, when the area has none, offering a button that
/// adds that material's layer (<see cref="AddMissingLayer"/>, Docs/TerrainLayers32.md's Phase 4) and makes it the paint
/// target. On every other tilemap, a renderer that is off or has not built included, it is the
/// <see cref="DualGridBrushEditor"/>.
/// </summary>
[CustomEditor(typeof(TerrainBrush))]
public class TerrainBrushEditor : DualGridBrushEditor
{
    private readonly List<Vector3Int> previewCells = new();
    private readonly List<TileBase> pickedTiles = new();
    private TerrainBlendRenderer previewRenderer;
    private GridBrushBase.Tool sceneTool;
    private bool wasExecuting;
    private bool shiftHeld;
    private bool inPaintSceneGUI;
    private bool clearDeferred;

    public override string tooltip => "Paints ground layers with a hover preview drawn by their Terrain Blend Renderer, and any other tilemap as the Dual Grid Brush.";

    /// <summary>
    /// Clears the preview whenever the paint target or the brush changes, a scene closes or an assembly reload starts,
    /// on top of what the grid brush editor clears it for (including its own disabling).
    /// </summary>
    protected override void OnEnable()
    {
        base.OnEnable();
        GridPaintingState.scenePaintTargetChanged += HandlePaintTargetChanged;
        GridPaintingState.brushChanged += HandleBrushChanged;
        EditorSceneManager.sceneClosing += HandleSceneClosing;
        AssemblyReloadEvents.beforeAssemblyReload += ClearTerrainPreview;
    }

    /// <summary>
    /// Stops following those changes; the grid brush editor's disabling clears the preview.
    /// </summary>
    protected override void OnDisable()
    {
        GridPaintingState.scenePaintTargetChanged -= HandlePaintTargetChanged;
        GridPaintingState.brushChanged -= HandleBrushChanged;
        EditorSceneManager.sceneClosing -= HandleSceneClosing;
        AssemblyReloadEvents.beforeAssemblyReload -= ClearTerrainPreview;
        base.OnDisable();
    }

    /// <summary>
    /// Clears the preview when the stroke ends (<see cref="FollowStroke"/>), lets the grid brush editor refresh the
    /// preview for the hovered cells and draw its outline, redoes the preview when shift is pressed or released
    /// (<see cref="FollowShift"/>), then runs a clear left waiting by the refresh unless a new preview replaced it.
    /// </summary>
    public override void OnPaintSceneGUI(GridLayout gridLayout, GameObject brushTarget, BoundsInt position, GridBrushBase.Tool tool, bool executing)
    {
        inPaintSceneGUI = true;
        sceneTool = tool;
        try
        {
            FollowStroke(executing);
            base.OnPaintSceneGUI(gridLayout, brushTarget, position, tool, executing);
            FollowShift(brushTarget, position, tool, IsShiftHeld());
        }
        finally
        {
            inPaintSceneGUI = false;
        }

        if (clearDeferred)
        {
            ClearTerrainPreviewNow();
        }
    }

    /// <summary>
    /// Draws the brush's settings in the Tile Palette, then a warning when the picked tiles do not suit the paint
    /// target's ground layer (<see cref="PaletteProblem"/>).
    /// </summary>
    public override void OnPaintInspectorGUI()
    {
        base.OnPaintInspectorGUI();
        pickedTiles.Clear();
        foreach (GridBrush.BrushCell cell in brush.cells)
        {
            pickedTiles.Add(cell.tile);
        }

        GameObject target = GridPaintingState.scenePaintTarget;
        string problem = PaletteProblem(target, pickedTiles);
        if (problem == null)
        {
            return;
        }

        EditorGUILayout.HelpBox(problem, MessageType.Warning);
        TerrainMaterial missing = MissingMaterial(target, pickedTiles);
        if (missing != null && GUILayout.Button($"Add the {missing.name} layer"))
        {
            Tilemap added = AddMissingLayer(target, missing);
            if (added != null)
            {
                GridPaintingState.scenePaintTarget = added.gameObject;
            }
        }
    }

    /// <summary>
    /// The first ground material among <paramref name="tiles"/> (<see cref="TerrainTileWriter.MaterialOf"/>) that the
    /// area of <paramref name="target"/>, a ground layer, has no layer for, or null when there is none or
    /// <paramref name="target"/> is no ground layer.
    /// </summary>
    internal static TerrainMaterial MissingMaterial(GameObject target, IEnumerable<TileBase> tiles)
    {
        if (target == null || !target.TryGetComponent(out Tilemap tilemap))
        {
            return null;
        }

        TerrainBlendRenderer renderer = TerrainBlendRenderer.FindLayerDrawing(tilemap, out _);
        if (renderer == null)
        {
            return null;
        }

        foreach (TileBase tile in tiles)
        {
            TerrainMaterial material = TerrainTileWriter.MaterialOf(tile);
            if (material != null && !Draws(renderer, material))
            {
                return material;
            }
        }

        return null;
    }

    /// <summary>
    /// Adds <paramref name="material"/>'s ground layer to the area of <paramref name="target"/>, a ground layer, at its
    /// place in the style's layer order (<see cref="GroundLayerMigration.AddLayer(Grid, TerrainMaterial)"/>, one undo
    /// step), and returns its tilemap; null, after logging why, when it cannot be added.
    /// </summary>
    internal static Tilemap AddMissingLayer(GameObject target, TerrainMaterial material)
    {
        TerrainBlendRenderer renderer = target != null && target.TryGetComponent(out Tilemap tilemap) ? TerrainBlendRenderer.FindLayerDrawing(tilemap, out _) : null;
        if (renderer == null)
        {
            return null;
        }

        return GroundLayerMigration.AddLayer(renderer.GetComponent<Grid>(), material);
    }

    /// <summary>
    /// Whether <paramref name="renderer"/> lists a layer drawn with <paramref name="material"/>.
    /// </summary>
    private static bool Draws(TerrainBlendRenderer renderer, TerrainMaterial material)
    {
        foreach (TerrainBlendRenderer.Layer layer in renderer.Layers)
        {
            if (layer != null && layer.Material == material && layer.Tilemap != null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Why painting <paramref name="tiles"/> on <paramref name="target"/> would not paint what they show, or null when
    /// it would or <paramref name="target"/> is no ground layer (a tilemap a <see cref="TerrainBlendRenderer"/> lists).
    /// A ground layer is drawn with its own material whatever tile its cells hold, so a ground material's tile (see
    /// <see cref="TerrainTileWriter.MaterialOf"/>) only paints its material on that material's layer: on another layer
    /// of the area it names the layer to pick instead, and for a material the area has no layer for (each area lists
    /// only the layers it uses, Docs/TerrainTypes.md) it says so, and the Tile Palette offers to add it.
    /// </summary>
    internal static string PaletteProblem(GameObject target, IEnumerable<TileBase> tiles)
    {
        if (target == null || !target.TryGetComponent(out Tilemap tilemap))
        {
            return null;
        }

        TerrainBlendRenderer renderer = TerrainBlendRenderer.FindLayerDrawing(tilemap, out int layer);
        if (renderer == null)
        {
            return null;
        }

        TerrainMaterial drawn = renderer.Layers[layer].Material;
        foreach (TileBase tile in tiles)
        {
            TerrainMaterial material = TerrainTileWriter.MaterialOf(tile);
            if (material == null || material == drawn)
            {
                continue;
            }

            string drawnName = drawn != null ? drawn.name : "no material";
            for (int i = 0; i < renderer.Layers.Count; i++)
            {
                TerrainBlendRenderer.Layer other = renderer.Layers[i];
                if (other.Material == material && other.Tilemap != null)
                {
                    return $"{tile.name} is {material.name}'s tile, which this area draws on {other.Tilemap.name}; painted on {tilemap.name} it would be drawn as {drawnName}. Pick {other.Tilemap.name} as the paint target.";
                }
            }

            return $"{material.name} is not one of this area's ground layers, so {tile.name} painted on {tilemap.name} would be drawn as {drawnName}. Add the {material.name} layer to paint it.";
        }

        return null;
    }

    /// <summary>
    /// Clears the preview when a stroke ends: <paramref name="executing"/>, whether the mouse is held down on the
    /// Scene view, turning false. The Scene view passes it with every event.
    /// </summary>
    internal void FollowStroke(bool executing)
    {
        if (wasExecuting && !executing)
        {
            ClearTerrainPreview();
        }

        wasExecuting = executing;
    }

    /// <summary>
    /// Redoes the preview of a paint or a box hovering <paramref name="position"/> on a ground layer when
    /// <paramref name="shift"/> changes, since shift turns their click into an erase and the grid brush editor only
    /// refreshes its preview when the hovered cells, the target, the tool or the brush change.
    /// </summary>
    internal void FollowShift(GameObject brushTarget, BoundsInt position, GridBrushBase.Tool tool, bool shift)
    {
        if (shift == shiftHeld)
        {
            return;
        }

        shiftHeld = shift;
        bool shown = false;
        if (tool == GridBrushBase.Tool.Paint)
        {
            shown = TryShowTerrainPreview(brushTarget, FootprintAt(position.min), !shift);
        }
        else if (tool == GridBrushBase.Tool.Box)
        {
            shown = TryShowTerrainPreview(brushTarget, position, !shift);
        }

        if (shown)
        {
            SceneView.RepaintAll();
        }
    }

    /// <summary>
    /// Previews a paint of the brush's footprint at <paramref name="position"/>, or an erase while shift is held with
    /// the Paint tool, since the click then erases (<see cref="PaintPreview(GridLayout, GameObject, Vector3Int, bool)"/>).
    /// </summary>
    public override void PaintPreview(GridLayout gridLayout, GameObject brushTarget, Vector3Int position)
    {
        PaintPreview(gridLayout, brushTarget, position, sceneTool == GridBrushBase.Tool.Paint && IsShiftHeld());
    }

    /// <summary>
    /// Previews a paint of the brush's footprint at <paramref name="position"/>, or an erase when
    /// <paramref name="erasing"/>, on a ground layer; the Dual Grid Brush's paint preview elsewhere.
    /// </summary>
    internal void PaintPreview(GridLayout gridLayout, GameObject brushTarget, Vector3Int position, bool erasing)
    {
        if (!TryShowTerrainPreview(brushTarget, FootprintAt(position), !erasing))
        {
            base.PaintPreview(gridLayout, brushTarget, position);
        }
    }

    /// <summary>
    /// Previews an erase of the brush's footprint at <paramref name="position"/>: empty cells on a ground layer, the
    /// Dual Grid Brush's preview elsewhere.
    /// </summary>
    public override void ErasePreview(GridLayout gridLayout, GameObject brushTarget, Vector3Int position)
    {
        if (!TryShowTerrainPreview(brushTarget, FootprintAt(position), false))
        {
            base.ErasePreview(gridLayout, brushTarget, position);
        }
    }

    /// <summary>
    /// Previews a box fill of <paramref name="bounds"/>, or a box erase while shift is held, since releasing the mouse
    /// then erases (<see cref="BoxPreview"/>).
    /// </summary>
    public override void BoxFillPreview(GridLayout gridLayout, GameObject brushTarget, BoundsInt bounds)
    {
        BoxPreview(gridLayout, brushTarget, bounds, IsShiftHeld());
    }

    /// <summary>
    /// Previews a box fill of <paramref name="bounds"/>, or a box erase when <paramref name="erasing"/>, on a ground
    /// layer; the Dual Grid Brush's box preview elsewhere.
    /// </summary>
    internal void BoxPreview(GridLayout gridLayout, GameObject brushTarget, BoundsInt bounds, bool erasing)
    {
        if (!TryShowTerrainPreview(brushTarget, bounds, !erasing))
        {
            base.BoxFillPreview(gridLayout, brushTarget, bounds);
        }
    }

    /// <summary>
    /// Shows no flood fill preview on a ground layer; the Dual Grid Brush's preview elsewhere.
    /// </summary>
    public override void FloodFillPreview(GridLayout gridLayout, GameObject brushTarget, Vector3Int position)
    {
        if (RendererDrawing(brushTarget, out _, out _) == null)
        {
            base.FloodFillPreview(gridLayout, brushTarget, position);
        }
    }

    /// <summary>
    /// Clears the ground layer preview (<see cref="ClearTerrainPreview"/>), then whatever preview the Dual Grid
    /// Brush's editor shows.
    /// </summary>
    public override void ClearPreview()
    {
        ClearTerrainPreview();
        base.ClearPreview();
    }

    /// <summary>
    /// Clears the ground layer preview as the stroke is committed, then records the undo step as the Dual Grid Brush
    /// does: the data and render tilemaps of a dual-grid layer, or the tilemap.
    /// </summary>
    public override void RegisterUndo(GameObject brushTarget, GridBrushBase.Tool tool)
    {
        ClearTerrainPreview();
        base.RegisterUndo(brushTarget, tool);
    }

    /// <summary>
    /// The renderer drawing <paramref name="brushTarget"/>'s tilemap as a ground layer right now, with the tilemap
    /// and whether the brush writes every cell of a stroke there (a dual-grid layer); null when no renderer lists the
    /// tilemap, or the one listing it is off, has not built, or has not rebuilt since the tilemap joined its layers.
    /// </summary>
    private static TerrainBlendRenderer RendererDrawing(GameObject brushTarget, out Tilemap tilemap, out bool writesEveryCell)
    {
        tilemap = null;
        writesEveryCell = false;
        if (brushTarget == null || !brushTarget.TryGetComponent(out tilemap))
        {
            return null;
        }

        TerrainBlendRenderer renderer = TerrainBlendRenderer.FindLayerDrawing(tilemap, out _);
        if (renderer == null || !renderer.IsDrawing(tilemap))
        {
            return null;
        }

        writesEveryCell = brushTarget.TryGetComponent(out DualGridTilemapModule _);
        return renderer;
    }

    /// <summary>
    /// On a ground layer, replaces the last preview with <paramref name="bounds"/>' cells shown filled or empty
    /// (<paramref name="filled"/>), cancels a clear waiting for the end of the Scene view's GUI, and returns true;
    /// returns false, showing nothing, on any other tilemap.
    /// </summary>
    private bool TryShowTerrainPreview(GameObject brushTarget, BoundsInt bounds, bool filled)
    {
        TerrainBlendRenderer renderer = RendererDrawing(brushTarget, out Tilemap tilemap, out bool writesEveryCell);
        if (renderer == null)
        {
            return false;
        }

        previewCells.Clear();
        foreach (Vector3Int cell in bounds.allPositionsWithin)
        {
            if (cell.z == 0 && (!filled || writesEveryCell || BrushHoldsTileAt(cell - bounds.min)))
            {
                previewCells.Add(cell);
            }
        }

        if (previewRenderer != null && previewRenderer != renderer)
        {
            previewRenderer.ClearPreview();
        }

        int chunks = renderer.Chunks.Count;
        renderer.SetPreview(tilemap, previewCells, filled);
        previewRenderer = renderer;
        clearDeferred = false;
        if (renderer.Chunks.Count != chunks)
        {
            SceneView.RepaintAll();
        }

        return true;
    }

    /// <summary>
    /// Whether the brush cell a box fill uses at <paramref name="local"/> from the box's corner holds a tile, as the
    /// grid brush checks before writing a cell of a plain tilemap.
    /// </summary>
    private bool BrushHoldsTileAt(Vector3Int local)
    {
        return brush.cellCount > 0 && brush.cells[brush.GetCellIndexWrapAround(local.x, local.y, local.z)].tile != null;
    }

    /// <summary>
    /// The cells covered by the brush when its pivot sits on <paramref name="position"/>.
    /// </summary>
    private BoundsInt FootprintAt(Vector3Int position)
    {
        return new BoundsInt(position - brush.pivot, brush.size);
    }

    /// <summary>
    /// Whether shift is held in the event being handled; false outside the editor's GUI.
    /// </summary>
    private static bool IsShiftHeld()
    {
        return Event.current != null && Event.current.shift;
    }

    /// <summary>
    /// Removes the ground layer preview. During the Scene view's GUI the clear waits for the end of it, where
    /// <see cref="OnPaintSceneGUI"/> runs it unless a new preview replaced this one first, so a hover refresh, which
    /// clears and then previews, uploads once.
    /// </summary>
    private void ClearTerrainPreview()
    {
        if (inPaintSceneGUI)
        {
            clearDeferred = previewRenderer != null;
            return;
        }

        ClearTerrainPreviewNow();
    }

    /// <summary>
    /// Removes the ground layer preview, if one is shown (which shrinks the renderer's map back if the preview grew
    /// it), and has the Scene view repaint so the cleared cells show at once.
    /// </summary>
    private void ClearTerrainPreviewNow()
    {
        clearDeferred = false;
        TerrainBlendRenderer shown = previewRenderer;
        previewRenderer = null;
        if (shown == null)
        {
            return;
        }

        shown.ClearPreview();
        SceneView.RepaintAll();
    }

    /// <summary>
    /// Clears the preview when another tilemap becomes the paint target.
    /// </summary>
    private void HandlePaintTargetChanged(GameObject target)
    {
        ClearTerrainPreview();
    }

    /// <summary>
    /// Clears the preview when the Tile Palette's brush changes.
    /// </summary>
    private void HandleBrushChanged(GridBrushBase changed)
    {
        ClearTerrainPreview();
    }

    /// <summary>
    /// Clears the preview before a scene closes.
    /// </summary>
    private void HandleSceneClosing(Scene scene, bool removingScene)
    {
        ClearTerrainPreview();
    }
}
