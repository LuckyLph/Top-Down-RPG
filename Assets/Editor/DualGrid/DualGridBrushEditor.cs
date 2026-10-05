using System;
using UnityEditor;
using UnityEditor.Tilemaps;
using UnityEngine;

/// <summary>
/// The custom editor for <see cref="DualGridBrush"/>.
/// </summary>
/// <remarks>
/// This editor is completely compatible with the default <see cref="GridBrush"/>, but with added functionality for Dual Grid Tilemaps.
/// <para></para>
/// It's responsible for preview tiles and Undo operations.
/// </remarks>
[CustomEditor(typeof(DualGridBrush), false)]
public class DualGridBrushEditor : GridBrushEditor
{
    private DualGridTilemapModule lastDualGridTilemapModule;

    private DualGridPreviewTile previewTile;
    private DualGridPreviewTile emptyPreviewTile;

    private BoundsInt? lastBounds;
    private GridBrushBase.Tool? lastTool;

    /// <summary>
    /// Whether a preview is shown while painting a Tilemap in the Flood Fill Tool.
    /// </summary>
    /// <remarks>
    /// Editor Preference taken from Grid Brush Preferences.
    /// </remarks>
    private static bool ShowFloodFillPreview => EditorPrefs.GetBool("GridBrush.EnableFloodFillPreview", true);

    /// <summary>
    /// Lazily creates the filled and empty preview tiles and redirects the selection away from a render tilemap
    /// before the tool activates.
    /// </summary>
    public override void OnToolActivated(GridBrushBase.Tool tool)
    {
        if (previewTile == null)
            previewTile = DualGridPreviewTile.Filled;

        if (emptyPreviewTile == null)
            emptyPreviewTile = DualGridPreviewTile.NotFilled;

        ProtectAgainstEditingRenderTilemap();

        base.OnToolActivated(tool);
    }

    /// <summary>
    /// Controls whether this brush should actively prevent any direct changes to the a Render Tilemap.
    /// </summary>
    protected virtual void ProtectAgainstEditingRenderTilemap()
    {
        var currentSelection = Selection.activeObject as GameObject;
        if (currentSelection == null) return;

        var dualGridTilemapModuleFromRenderTilemap = currentSelection.GetComponentInImmediateParent<DualGridTilemapModule>();
        bool isPaintingOnRenderTilemap = dualGridTilemapModuleFromRenderTilemap != null;
        if (isPaintingOnRenderTilemap)
        {
            Debug.LogWarning($"Current selection {currentSelection.name} is a Render Tilemap and painting on it is not permitted. Changed to associated Data Tilemap {dualGridTilemapModuleFromRenderTilemap.DataTilemap.name}.");
            Selection.activeObject = dualGridTilemapModuleFromRenderTilemap.DataTilemap.gameObject;
        }
    }

    /// <summary>
    /// Remembers the dual-grid module under the brush (null on plain tilemaps) so <see cref="ClearPreview"/> knows
    /// which preview to clear, then draws the default scene GUI.
    /// </summary>
    public override void OnPaintSceneGUI(GridLayout gridLayout, GameObject brushTarget, BoundsInt bounds, GridBrushBase.Tool tool, bool executing)
    {
        lastDualGridTilemapModule = brushTarget.GetComponent<DualGridTilemapModule>();

        base.OnPaintSceneGUI(gridLayout, brushTarget, bounds, tool, executing);
    }

    /// <summary>
    /// Previews a paint stroke over the brush footprint at <paramref name="position"/> on a dual-grid target; default
    /// preview on other tilemaps.
    /// </summary>
    public override void PaintPreview(GridLayout gridLayout, GameObject brushTarget, Vector3Int position)
    {
        if (brushTarget.TryGetComponent(out DualGridTilemapModule dualGridTilemapModule))
        {
            BoundsInt bounds = GetBrushBounds(position);
            DualGridPaintPreview(dualGridTilemapModule, bounds);
        }
        else
        {
            base.PaintPreview(gridLayout, brushTarget, position);
        }
    }

    /// <summary>
    /// Places filled preview tiles over <paramref name="bounds"/> and records them as a paint preview for later
    /// clearing.
    /// </summary>
    protected virtual void DualGridPaintPreview(DualGridTilemapModule dualGridTilemapModule, BoundsInt bounds)
    {
        foreach (var position in bounds.allPositionsWithin)
        {
            dualGridTilemapModule.SetEditorPreviewTile(position, previewTile);
        }

        lastBounds = bounds;
        lastTool = GridBrushBase.Tool.Paint;
    }

#if UNITY_2023_1_OR_NEWER
    /// <summary>
    /// Previews an erase over the brush footprint at <paramref name="position"/> on a dual-grid target; default preview
    /// on other tilemaps.
    /// </summary>
    public override void ErasePreview(GridLayout gridLayout, GameObject brushTarget, Vector3Int position)
    {
        if (brushTarget.TryGetComponent(out DualGridTilemapModule dualGridTilemapModule))
        {
            BoundsInt bounds = GetBrushBounds(position);
            DualGridErasePreview(dualGridTilemapModule, bounds);
        }
        else
        {
            base.ErasePreview(gridLayout, brushTarget, position);
        }
    }

    /// <summary>
    /// Places empty preview tiles over <paramref name="bounds"/> and records them as an erase preview for later
    /// clearing.
    /// </summary>
    private void DualGridErasePreview(DualGridTilemapModule dualGridTilemapModule, BoundsInt bounds)
    {
        foreach (var position in bounds.allPositionsWithin)
        {
            dualGridTilemapModule.SetEditorPreviewTile(position, emptyPreviewTile);
        }

        lastBounds = bounds;
        lastTool = GridBrushBase.Tool.Erase;
    }
#endif

    /// <summary>
    /// Previews a box fill of <paramref name="bounds"/> on a dual-grid target; default preview on other tilemaps.
    /// </summary>
    public override void BoxFillPreview(GridLayout gridLayout, GameObject brushTarget, BoundsInt bounds)
    {
        if (brushTarget.TryGetComponent(out DualGridTilemapModule dualGridTilemapModule))
        {
            DualGridBoxFillPreview(dualGridTilemapModule, bounds);
        }
        else
        {
            base.BoxFillPreview(gridLayout, brushTarget, bounds);
        }
    }

    /// <summary>
    /// Places filled preview tiles over <paramref name="bounds"/> and records them as a box preview for later clearing.
    /// </summary>
    protected virtual void DualGridBoxFillPreview(DualGridTilemapModule dualGridTilemapModule, BoundsInt bounds)
    {
        foreach (var position in bounds.allPositionsWithin)
        {
            dualGridTilemapModule.SetEditorPreviewTile(position, previewTile);
        }

        lastBounds = bounds;
        lastTool = GridBrushBase.Tool.Box;
    }

    /// <summary>
    /// Previews a flood fill from <paramref name="position"/> on a dual-grid target; default preview on other
    /// tilemaps.
    /// </summary>
    public override void FloodFillPreview(GridLayout gridLayout, GameObject brushTarget, Vector3Int position)
    {
        if (brushTarget.TryGetComponent(out DualGridTilemapModule dualGridTilemapModule))
        {
            DualGridFloodFillPreview(dualGridTilemapModule, position);
        }
        else
        {
            base.FloodFillPreview(gridLayout, brushTarget, position);
        }
    }

    /// <summary>
    /// Flood fills preview tiles from <paramref name="position"/>, mirrors them onto the whole render tilemap and
    /// records the data tilemap bounds as a flood fill preview. Skipped when the flood fill preview preference is off.
    /// </summary>
    protected virtual void DualGridFloodFillPreview(DualGridTilemapModule dualGridTilemapModule, Vector3Int position)
    {
        if (!ShowFloodFillPreview) return;

        dualGridTilemapModule.DataTilemap.EditorPreviewFloodFill(position, previewTile);
        dualGridTilemapModule.UpdateAllPreviewRenderTiles();

        var bounds = new BoundsInt(position, Vector3Int.one);
        var origin = dualGridTilemapModule.DataTilemap.origin;
        bounds.min = origin;
        bounds.max = origin + dualGridTilemapModule.DataTilemap.size;

        lastBounds = bounds;
        lastTool = GridBrushBase.Tool.FloodFill;
    }

    /// <summary>
    /// Clears the last dual-grid preview when the brush was last over a dual-grid module; default clear otherwise.
    /// </summary>
    public override void ClearPreview()
    {
        if (lastDualGridTilemapModule != null)
        {
            DualGridClearPreview();
        }
        else
        {
            base.ClearPreview();
        }
    }

    /// <summary>
    /// Removes the preview tiles left by the last preview, all of them after a flood fill or just the recorded bounds
    /// otherwise, then forgets that preview.
    /// </summary>
    protected virtual void DualGridClearPreview()
    {
        if (lastBounds == null || lastTool == null)
            return;

        switch (lastTool)
        {
            case GridBrushBase.Tool.FloodFill:
                {
                    lastDualGridTilemapModule.ClearAllPreviewTiles();
                    break;
                }
            case GridBrushBase.Tool.Box:
                {
                    Vector3Int min = lastBounds.Value.position;
                    Vector3Int max = min + lastBounds.Value.size;
                    var bounds = new BoundsInt(min, max - min);
                    ClearEditorPreviewTiles(lastDualGridTilemapModule, bounds);
                    break;
                }
            case GridBrushBase.Tool.Erase:
            case GridBrushBase.Tool.Paint:
                {
                    ClearEditorPreviewTiles(lastDualGridTilemapModule, lastBounds.Value);
                    break;
                }
        }

        lastBounds = null;
        lastTool = null;
    }

    /// <summary>
    /// On a dual-grid target, clears the pending preview and records both the data and render tilemaps in one undo
    /// step named after <paramref name="tool"/>; default undo on other tilemaps.
    /// </summary>
    public override void RegisterUndo(GameObject brushTarget, GridBrushBase.Tool tool)
    {
        if (brushTarget.TryGetComponent(out DualGridTilemapModule dualGridTilemapModule))
        {
            if (lastBounds.HasValue) ClearEditorPreviewTiles(dualGridTilemapModule, lastBounds.Value);

            Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { dualGridTilemapModule.DataTilemap, dualGridTilemapModule.RenderTilemap }, $"{GetVerb(tool)} dual grid {dualGridTilemapModule.name}");
        }
        else
        {
            base.RegisterUndo(brushTarget, tool);
        }

        static string GetVerb(GridBrushBase.Tool tool)
        {
            string toolName = tool.ToString();

            if (toolName.EndsWith("e", StringComparison.OrdinalIgnoreCase))
                toolName = toolName.Substring(0, toolName.Length - 1);

            return toolName + "ing";
        }
    }

    /// <summary>
    /// The cells covered by the brush when its pivot sits on <paramref name="position"/>.
    /// </summary>
    private BoundsInt GetBrushBounds(Vector3Int position)
    {
        var min = position - brush.pivot;
        var max = min + brush.size;
        return new BoundsInt(min, max - min);
    }

    /// <summary>
    /// Removes the editor preview from every data cell in <paramref name="bounds"/> and updates the render previews.
    /// </summary>
    private static void ClearEditorPreviewTiles(DualGridTilemapModule dualGridTilemapModule, BoundsInt bounds)
    {
        foreach (Vector3Int location in bounds.allPositionsWithin)
        {
            dualGridTilemapModule.ClearEditorPreviewTile(location);
        }
    }
}
