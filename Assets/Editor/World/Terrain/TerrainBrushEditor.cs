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
/// anything saved, and a new preview replaces the last. A flood fill shows the cells it would fill
/// (<see cref="WorldBrushStroke.FloodCells"/> on a chunk, <see cref="TerrainBrush.FloodCells"/> elsewhere, the brush
/// filling those same cells). On an area's raised ground it previews a paint, an erase, a box or a flood fill the same
/// way through the renderer's elevation map (<see cref="TerrainBlendRenderer.SetRaisedPreview"/>), each cell as the
/// brush's tile would raise it, on the chunk's renderer and its linked neighbours' on a seamless world. On the walls of
/// a chunk it previews with the modules of every open chunk the stroke covers, each cell on the module of the chunk
/// owning it and each render cell on the module drawing it, so the corners along a border read both chunks' previews
/// (Docs/KnownGaps.md, Phase 6). The preview is cleared when the stroke is committed
/// (<see cref="RegisterUndo"/>) or ends (the mouse is released), when the pointer leaves the Scene view, the tool or the
/// brush changes, the paint target changes, after an undo or redo, when a scene closes, before an assembly reload and
/// when the editor is disabled. During the Scene view's GUI a clear waits for the end of it, so a hover that replaces
/// the preview uploads once; a clear that happens, and a preview that added chunks, have the Scene view repaint so
/// they show at once. Updating the preview allocates nothing once its list holds the largest stroke. In the Tile
/// Palette it warns when the picked tile is a ground material's that the target layer is not drawn with
/// (<see cref="PaletteProblem"/>), naming the area's layer for it, or, when the area has none, offering a button that
/// adds that material's layer (<see cref="AddMissingLayer"/>, Docs/TerrainLayers32.md's Phase 4) and makes it the paint
/// target. On a ground layer of a chunk of a seamless world the preview shows on the renderer of each chunk it covers,
/// the target's and its linked neighbours', on their layer of the same material, each given the cells within its skirt
/// so the preview blends across the border. On every other tilemap, a renderer that is off or has not built included,
/// it is the <see cref="DualGridBrushEditor"/>.
/// </summary>
[CustomEditor(typeof(TerrainBrush))]
public class TerrainBrushEditor : DualGridBrushEditor
{
    private readonly List<Vector3Int> previewCells = new();
    private readonly List<TileBase> pickedTiles = new();
    private readonly List<Vector3Int> chunkPreviewCells = new();
    private List<TerrainBlendRenderer> previewRenderers = new();
    private List<TerrainBlendRenderer> shownRenderers = new();
    private GridBrushBase.Tool sceneTool;
    private bool wasExecuting;
    private bool shiftHeld;
    private bool inPaintSceneGUI;
    private bool clearDeferred;
    private readonly List<byte> raisedKinds = new();
    private readonly List<TerrainBlendRenderer> raisedRenderers = new();
    private readonly List<Vector3Int> wallCells = new();
    private readonly List<DualGridTilemapModule> wallOwners = new();
    private readonly List<DualGridTilemapModule> wallModules = new();
    private readonly HashSet<(DualGridTilemapModule Module, Vector3Int Cell)> wallRenderCells = new();
    private DualGridPreviewTile filledWallTile;
    private DualGridPreviewTile emptyWallTile;

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
        AssemblyReloadEvents.beforeAssemblyReload += ClearAllPreviews;
    }

    /// <summary>
    /// Stops following those changes; the grid brush editor's disabling clears the preview.
    /// </summary>
    protected override void OnDisable()
    {
        GridPaintingState.scenePaintTargetChanged -= HandlePaintTargetChanged;
        GridPaintingState.brushChanged -= HandleBrushChanged;
        EditorSceneManager.sceneClosing -= HandleSceneClosing;
        AssemblyReloadEvents.beforeAssemblyReload -= ClearAllPreviews;
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
    /// step), and returns its tilemap; null, after logging why, when it cannot be added. On a chunk of a world it links
    /// the chunk with its neighbours again (<see cref="WorldChunk.RefreshLinks"/>), so their skirts read the new layer.
    /// </summary>
    internal static Tilemap AddMissingLayer(GameObject target, TerrainMaterial material)
    {
        TerrainBlendRenderer renderer = target != null && target.TryGetComponent(out Tilemap tilemap) ? TerrainBlendRenderer.FindLayerDrawing(tilemap, out _) : null;
        if (renderer == null)
        {
            return null;
        }

        Tilemap added = GroundLayerMigration.AddLayer(renderer.GetComponent<Grid>(), material);
        if (added != null && renderer.TryGetComponent(out WorldChunk chunk))
        {
            chunk.RefreshLinks();
        }

        return added;
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
            ClearAllPreviews();
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
            shown = TryShowAnyPreview(brushTarget, FootprintAt(position.min), !shift);
        }
        else if (tool == GridBrushBase.Tool.Box)
        {
            shown = TryShowAnyPreview(brushTarget, position, !shift);
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
        if (!TryShowAnyPreview(brushTarget, FootprintAt(position), !erasing))
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
        if (!TryShowAnyPreview(brushTarget, FootprintAt(position), false))
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
        if (!TryShowAnyPreview(brushTarget, bounds, !erasing))
        {
            base.BoxFillPreview(gridLayout, brushTarget, bounds);
        }
    }

    /// <summary>
    /// Previews the flood fill from <paramref name="position"/>: the cells it would fill on a ground layer, on raised
    /// ground and on a chunk's walls; the Dual Grid Brush's preview elsewhere.
    /// </summary>
    public override void FloodFillPreview(GridLayout gridLayout, GameObject brushTarget, Vector3Int position)
    {
        if (!TryShowFloodPreview(brushTarget, position))
        {
            base.FloodFillPreview(gridLayout, brushTarget, position);
        }
    }

    /// <summary>
    /// Clears the ground layer preview (<see cref="ClearTerrainPreview"/>), the raised ground's and a chunk's walls',
    /// then whatever preview the Dual Grid Brush's editor shows.
    /// </summary>
    public override void ClearPreview()
    {
        ClearTerrainPreview();
        ClearRaisedPreview();
        ClearWallsPreview();
        base.ClearPreview();
    }

    /// <summary>
    /// Clears the ground layer, raised ground and walls previews as the stroke is committed, then records the undo step
    /// as the Dual Grid Brush does: the data and render tilemaps of a dual-grid layer, or the tilemap.
    /// </summary>
    public override void RegisterUndo(GameObject brushTarget, GridBrushBase.Tool tool)
    {
        ClearTerrainPreview();
        ClearRaisedPreview();
        ClearWallsPreview();
        base.RegisterUndo(brushTarget, tool);
    }

    /// <summary>
    /// Previews <paramref name="bounds"/> filled or empty on a ground layer, raised ground or a chunk's walls, whichever
    /// <paramref name="brushTarget"/> is; returns false, showing nothing, on any other tilemap.
    /// </summary>
    private bool TryShowAnyPreview(GameObject brushTarget, BoundsInt bounds, bool filled)
    {
        return TryShowTerrainPreview(brushTarget, bounds, filled) || TryShowRaisedPreview(brushTarget, bounds, filled) || TryShowWallsPreview(brushTarget, bounds, filled);
    }

    /// <summary>
    /// Previews the cells a flood fill from <paramref name="position"/> would fill on a ground layer, raised ground or a
    /// chunk's walls (<see cref="WorldBrushStroke.FloodCells"/>, <see cref="TerrainBrush.FloodCells"/>); returns false,
    /// showing nothing, on any other tilemap.
    /// </summary>
    internal bool TryShowFloodPreview(GameObject brushTarget, Vector3Int position)
    {
        TileBase fill = ((TerrainBrush)brush).FloodTile;
        bool onChunk = WorldBrushStroke.TryResolve(brushTarget, out WorldBrushStroke.Target target);
        if (onChunk && target.TargetKind == WorldBrushStroke.Kind.Walls)
        {
            WorldBrushStroke.FloodCells(target, position, target.Module.DataTile, wallCells);
            return ShowWalls(target, wallCells, true);
        }

        TerrainBlendRenderer ground = RendererDrawing(brushTarget, out Tilemap groundTilemap, out _);
        TerrainBlendRenderer raised = RaisedRendererOf(brushTarget, out Tilemap raisedTilemap);
        if (ground == null && raised == null)
        {
            return false;
        }

        if (onChunk)
        {
            WorldBrushStroke.FloodCells(target, position, fill, previewCells);
        }
        else
        {
            TerrainBrush.FloodCells(ground != null ? groundTilemap : raisedTilemap, position, fill, previewCells);
        }

        if (ground != null)
        {
            ShowTerrainCells(ground, groundTilemap, true);
        }
        else
        {
            ShowRaisedCells(raised, RaisedGround.KindOf(fill));
        }

        return true;
    }

    /// <summary>
    /// The renderer that reads <paramref name="brushTarget"/>'s tilemap as its raised ground and draws it now, with the
    /// tilemap; null for any other tilemap.
    /// </summary>
    private static TerrainBlendRenderer RaisedRendererOf(GameObject brushTarget, out Tilemap tilemap)
    {
        tilemap = null;
        if (brushTarget == null || !brushTarget.TryGetComponent(out tilemap))
        {
            return null;
        }

        TerrainBlendRenderer renderer = tilemap.GetComponentInParent<TerrainBlendRenderer>();
        return renderer != null && renderer.IsDrawingRaised(tilemap) ? renderer : null;
    }

    /// <summary>
    /// On raised ground, replaces the last raised preview with <paramref name="bounds"/>' cells at z = 0, each raised as
    /// the brush's tile there would raise it when <paramref name="filled"/> (level where its cell holds no tile), else
    /// level, and returns true; returns false, showing nothing, on any other tilemap.
    /// </summary>
    private bool TryShowRaisedPreview(GameObject brushTarget, BoundsInt bounds, bool filled)
    {
        TerrainBlendRenderer renderer = RaisedRendererOf(brushTarget, out _);
        if (renderer == null)
        {
            return false;
        }

        previewCells.Clear();
        raisedKinds.Clear();
        foreach (Vector3Int cell in bounds.allPositionsWithin)
        {
            if (cell.z == 0)
            {
                previewCells.Add(cell);
                raisedKinds.Add(filled ? RaisedGround.KindOf(BrushTileAt(cell - bounds.min)) : RaisedGround.Level);
            }
        }

        ShowRaised(renderer);
        return true;
    }

    /// <summary>
    /// Shows <see cref="previewCells"/> raised as <paramref name="kind"/> on <paramref name="renderer"/> and, on a
    /// chunk, its linked neighbours (<see cref="ShowRaised"/>).
    /// </summary>
    private void ShowRaisedCells(TerrainBlendRenderer renderer, byte kind)
    {
        raisedKinds.Clear();
        for (int i = 0; i < previewCells.Count; i++)
        {
            raisedKinds.Add(kind);
        }

        ShowRaised(renderer);
    }

    /// <summary>
    /// Shows <see cref="previewCells"/> raised as <see cref="raisedKinds"/> on <paramref name="renderer"/> and, when it
    /// draws a chunk, on its linked neighbours, whose skirt reads the chunk's raised ground, clearing it from every
    /// renderer showing it before that does not show it now.
    /// </summary>
    private void ShowRaised(TerrainBlendRenderer renderer)
    {
        List<TerrainBlendRenderer> showing = new() { renderer };
        if (renderer.TryGetComponent(out WorldChunk chunk))
        {
            for (int i = 0; i < chunk.Neighbours.Count; i++)
            {
                TerrainBlendRenderer neighbour = chunk.Neighbours[i] != null ? chunk.Neighbours[i].Terrain : null;
                if (neighbour != null && !showing.Contains(neighbour))
                {
                    showing.Add(neighbour);
                }
            }
        }

        foreach (TerrainBlendRenderer shown in raisedRenderers)
        {
            if (shown != null && !showing.Contains(shown))
            {
                shown.ClearRaisedPreview();
            }
        }

        raisedRenderers.Clear();
        foreach (TerrainBlendRenderer shown in showing)
        {
            if (shown.SetRaisedPreview(previewCells, raisedKinds))
            {
                raisedRenderers.Add(shown);
            }
        }

        SceneView.RepaintAll();
    }

    /// <summary>
    /// Removes the raised preview from every renderer showing it.
    /// </summary>
    private void ClearRaisedPreview()
    {
        if (raisedRenderers.Count == 0)
        {
            return;
        }

        foreach (TerrainBlendRenderer shown in raisedRenderers)
        {
            if (shown != null)
            {
                shown.ClearRaisedPreview();
            }
        }

        raisedRenderers.Clear();
        SceneView.RepaintAll();
    }

    /// <summary>
    /// On a chunk's walls, previews <paramref name="bounds"/>' cells at z = 0 filled or empty with the open chunks'
    /// modules (<see cref="ShowWalls"/>) and returns true; returns false, showing nothing, on any other tilemap.
    /// </summary>
    private bool TryShowWallsPreview(GameObject brushTarget, BoundsInt bounds, bool filled)
    {
        if (!WorldBrushStroke.TryResolve(brushTarget, out WorldBrushStroke.Target target) || target.TargetKind != WorldBrushStroke.Kind.Walls)
        {
            return false;
        }

        wallCells.Clear();
        foreach (Vector3Int cell in bounds.allPositionsWithin)
        {
            if (cell.z == 0)
            {
                wallCells.Add(cell);
            }
        }

        return ShowWalls(target, wallCells, filled);
    }

    /// <summary>
    /// Replaces the last walls preview with <paramref name="cells"/> shown filled or empty: each cell as an editor
    /// preview tile on the data tilemap of the open chunk owning it (the target's walls or the like walls of a linked
    /// neighbour; a cell no open chunk owns is left out), then each render cell around them on the module drawing it
    /// (<see cref="DualGridTilemapModule.OwnsRenderTile"/>), a preview of its render tile where one of its four data cells
    /// is previewed filled and none elsewhere, refreshed so its corner reads every chunk's preview. Returns true.
    /// </summary>
    private bool ShowWalls(WorldBrushStroke.Target target, List<Vector3Int> cells, bool filled)
    {
        List<Vector3Int> shown = new(cells);
        ClearWallsPreview();
        filledWallTile ??= DualGridPreviewTile.Filled;
        emptyWallTile ??= DualGridPreviewTile.NotFilled;
        wallModules.Add(target.Module);
        for (int i = 0; i < target.Chunk.Neighbours.Count; i++)
        {
            DualGridTilemapModule neighbour = target.Chunk.Neighbours[i] != null ? WorldBrushStroke.WallsLike(target.Chunk.Neighbours[i], target.Module) : null;
            if (neighbour != null && !wallModules.Contains(neighbour))
            {
                wallModules.Add(neighbour);
            }
        }

        foreach (Vector3Int cell in shown)
        {
            DualGridTilemapModule owner = ModuleHolding(cell);
            if (owner == null)
            {
                continue;
            }

            owner.DataTilemap.SetEditorPreviewTile(cell, filled ? filledWallTile : emptyWallTile);
            wallCells.Add(cell);
            wallOwners.Add(owner);
            for (int corner = 0; corner < DualGridUtils.CornerCount; corner++)
            {
                Vector3Int renderCell = DualGridUtils.GetRenderTilePosition(cell, corner);
                foreach (DualGridTilemapModule module in wallModules)
                {
                    if (module.OwnsRenderTile(renderCell))
                    {
                        wallRenderCells.Add((module, renderCell));
                    }
                }
            }
        }

        foreach ((DualGridTilemapModule module, Vector3Int renderCell) in wallRenderCells)
        {
            bool anyFilled = false;
            for (int corner = 0; corner < DualGridUtils.CornerCount && !anyFilled; corner++)
            {
                Vector3Int dataCell = DualGridUtils.GetDataTilePosition(renderCell, corner);
                DualGridTilemapModule owner = ModuleHolding(dataCell);
                anyFilled = owner != null && owner.DataTilemap.GetEditorPreviewTile(dataCell) is DualGridPreviewTile tile && tile.IsFilled;
            }

            module.RenderTilemap.SetEditorPreviewTile(renderCell, anyFilled ? module.RenderTile : null);
            module.RenderTilemap.RefreshTile(renderCell);
        }

        SceneView.RepaintAll();
        return true;
    }

    /// <summary>
    /// Of the walls modules a preview uses, the one of the chunk whose cells hold <paramref name="cell"/>, or null.
    /// </summary>
    private DualGridTilemapModule ModuleHolding(Vector3Int cell)
    {
        foreach (DualGridTilemapModule module in wallModules)
        {
            WorldChunk chunk = module != null ? module.GetComponentInParent<WorldChunk>() : null;
            if (chunk != null && chunk.Cells.Contains(new Vector2Int(cell.x, cell.y)))
            {
                return module;
            }
        }

        return null;
    }

    /// <summary>
    /// Removes the walls preview: the preview tiles from the data cells and render cells it set, refreshing the render
    /// cells.
    /// </summary>
    private void ClearWallsPreview()
    {
        if (wallOwners.Count == 0 && wallRenderCells.Count == 0)
        {
            wallCells.Clear();
            wallModules.Clear();
            return;
        }

        for (int i = 0; i < wallOwners.Count; i++)
        {
            if (wallOwners[i] != null)
            {
                wallOwners[i].DataTilemap.SetEditorPreviewTile(wallCells[i], null);
            }
        }

        foreach ((DualGridTilemapModule module, Vector3Int renderCell) in wallRenderCells)
        {
            if (module != null)
            {
                module.RenderTilemap.SetEditorPreviewTile(renderCell, null);
                module.RenderTilemap.RefreshTile(renderCell);
            }
        }

        wallCells.Clear();
        wallOwners.Clear();
        wallModules.Clear();
        wallRenderCells.Clear();
        SceneView.RepaintAll();
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
    /// (<paramref name="filled"/>), on the target's renderer or, on a chunk, on the renderers of the chunks they cover
    /// (<see cref="ShowOnChunk"/>), cancels a clear waiting for the end of the Scene view's GUI, and returns true;
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

        ShowTerrainCells(renderer, tilemap, filled);
        return true;
    }

    /// <summary>
    /// Replaces the last ground layer preview with <see cref="previewCells"/> shown filled or empty
    /// (<paramref name="filled"/>) on <paramref name="renderer"/>'s <paramref name="tilemap"/> or, on a chunk, on the
    /// renderers of the chunks they cover (<see cref="ShowOnChunk"/>), and cancels a clear waiting for the end of the
    /// Scene view's GUI.
    /// </summary>
    private void ShowTerrainCells(TerrainBlendRenderer renderer, Tilemap tilemap, bool filled)
    {
        shownRenderers.Clear();
        TerrainMaterial material = renderer.Layers[renderer.LayerIndexOf(tilemap)].Material;
        if (renderer.TryGetComponent(out WorldChunk chunk) && chunk.World != null && material != null)
        {
            ShowOnChunk(chunk, material, filled);
            for (int i = 0; i < chunk.Neighbours.Count; i++)
            {
                ShowOnChunk(chunk.Neighbours[i], material, filled);
            }
        }
        else
        {
            Show(renderer, tilemap, previewCells, filled);
        }

        for (int i = 0; i < previewRenderers.Count; i++)
        {
            if (previewRenderers[i] != null && !shownRenderers.Contains(previewRenderers[i]))
            {
                previewRenderers[i].ClearPreview();
            }
        }

        (previewRenderers, shownRenderers) = (shownRenderers, previewRenderers);
        shownRenderers.Clear();
        clearDeferred = false;
    }

    /// <summary>
    /// Shows the preview cells within <paramref name="chunk"/>'s cells and skirt on its renderer's layer of
    /// <paramref name="material"/>, when it draws one and some cells fall there.
    /// </summary>
    private void ShowOnChunk(WorldChunk chunk, TerrainMaterial material, bool filled)
    {
        TerrainBlendRenderer renderer = chunk != null ? chunk.Terrain : null;
        Tilemap layer = renderer != null ? LayerDrawing(renderer, material) : null;
        if (layer == null || !renderer.IsDrawing(layer))
        {
            return;
        }

        RectInt cells = chunk.Cells;
        RectInt reach = new(cells.xMin - WorldChunk.SkirtCells, cells.yMin - WorldChunk.SkirtCells, cells.width + 2 * WorldChunk.SkirtCells, cells.height + 2 * WorldChunk.SkirtCells);
        chunkPreviewCells.Clear();
        for (int i = 0; i < previewCells.Count; i++)
        {
            if (reach.Contains(new Vector2Int(previewCells[i].x, previewCells[i].y)))
            {
                chunkPreviewCells.Add(previewCells[i]);
            }
        }

        if (chunkPreviewCells.Count > 0)
        {
            Show(renderer, layer, chunkPreviewCells, filled);
        }
    }

    /// <summary>
    /// Shows <paramref name="cells"/> on <paramref name="renderer"/>'s <paramref name="layer"/> and records it as
    /// showing a preview, repainting the Scene view when the preview added chunks.
    /// </summary>
    private void Show(TerrainBlendRenderer renderer, Tilemap layer, List<Vector3Int> cells, bool filled)
    {
        int chunks = renderer.Chunks.Count;
        renderer.SetPreview(layer, cells, filled);
        shownRenderers.Add(renderer);
        if (renderer.Chunks.Count != chunks)
        {
            SceneView.RepaintAll();
        }
    }

    /// <summary>
    /// The tilemap of <paramref name="renderer"/>'s layer drawing <paramref name="material"/>, or null.
    /// </summary>
    private static Tilemap LayerDrawing(TerrainBlendRenderer renderer, TerrainMaterial material)
    {
        for (int i = 0; i < renderer.Layers.Count; i++)
        {
            TerrainBlendRenderer.Layer layer = renderer.Layers[i];
            if (layer != null && layer.Material == material && layer.Tilemap != null)
            {
                return layer.Tilemap;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the brush cell a box fill uses at <paramref name="local"/> from the box's corner holds a tile, as the
    /// grid brush checks before writing a cell of a plain tilemap.
    /// </summary>
    private bool BrushHoldsTileAt(Vector3Int local)
    {
        return BrushTileAt(local) != null;
    }

    /// <summary>
    /// The tile of the brush cell a box fill uses at <paramref name="local"/> from the box's corner, or null.
    /// </summary>
    private TileBase BrushTileAt(Vector3Int local)
    {
        return brush.cellCount > 0 ? brush.cells[brush.GetCellIndexWrapAround(local.x, local.y, local.z)].tile : null;
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
    /// Removes the ground layer, raised ground and walls previews.
    /// </summary>
    private void ClearAllPreviews()
    {
        ClearTerrainPreview();
        ClearRaisedPreview();
        ClearWallsPreview();
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
            clearDeferred = previewRenderers.Count > 0;
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
        if (previewRenderers.Count == 0)
        {
            return;
        }

        for (int i = 0; i < previewRenderers.Count; i++)
        {
            if (previewRenderers[i] != null)
            {
                previewRenderers[i].ClearPreview();
            }
        }

        previewRenderers.Clear();
        SceneView.RepaintAll();
    }

    /// <summary>
    /// Clears the preview when another tilemap becomes the paint target.
    /// </summary>
    private void HandlePaintTargetChanged(GameObject target)
    {
        ClearAllPreviews();
    }

    /// <summary>
    /// Clears the preview when the Tile Palette's brush changes.
    /// </summary>
    private void HandleBrushChanged(GridBrushBase changed)
    {
        ClearAllPreviews();
    }

    /// <summary>
    /// Clears the preview before a scene closes.
    /// </summary>
    private void HandleSceneClosing(Scene scene, bool removingScene)
    {
        ClearAllPreviews();
    }
}
