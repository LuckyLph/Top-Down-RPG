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

    public override void OnPaintSceneGUI(GridLayout gridLayout, GameObject brushTarget, BoundsInt bounds, GridBrushBase.Tool tool, bool executing)
    {
        lastDualGridTilemapModule = brushTarget.GetComponent<DualGridTilemapModule>();

        base.OnPaintSceneGUI(gridLayout, brushTarget, bounds, tool, executing);
    }

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

    protected virtual void DualGridBoxFillPreview(DualGridTilemapModule dualGridTilemapModule, BoundsInt bounds)
    {
        foreach (var position in bounds.allPositionsWithin)
        {
            dualGridTilemapModule.SetEditorPreviewTile(position, previewTile);
        }

        lastBounds = bounds;
        lastTool = GridBrushBase.Tool.Box;
    }

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

    private BoundsInt GetBrushBounds(Vector3Int position)
    {
        var min = position - brush.pivot;
        var max = min + brush.size;
        return new BoundsInt(min, max - min);
    }

    private static void ClearEditorPreviewTiles(DualGridTilemapModule dualGridTilemapModule, BoundsInt bounds)
    {
        foreach (Vector3Int location in bounds.allPositionsWithin)
        {
            dualGridTilemapModule.ClearEditorPreviewTile(location);
        }
    }
}
