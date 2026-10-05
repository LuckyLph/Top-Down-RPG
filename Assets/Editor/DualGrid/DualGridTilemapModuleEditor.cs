using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Inspector for <see cref="DualGridTilemapModule"/>: keeps the render tilemap child and tilemap colliders consistent
/// with the module settings, and draws optional data and render tile handles in the scene view. Also hosts the
/// factory methods that create dual-grid tilemaps.
/// </summary>
[CanEditMultipleObjects]
[CustomEditor(typeof(DualGridTilemapModule))]
public class DualGridTilemapModuleEditor : UnityEditor.Editor
{
    /// <summary>
    /// Labels and tooltips used by the inspector.
    /// </summary>
    private static class Styles
    {
        public static readonly GUIContent RenderTile = EditorGUIUtility.TrTextContent("Dual Grid Rule Tile", "The Render Tile that will be applied in the Render Tilemap.");
        public static readonly GUIContent EnableTilemapCollider = EditorGUIUtility.TrTextContent("Enable Tilemap Collider", "If a TilemapCollider2D should be active based on the Dual Grid Rule Tile's default collider type.");
        public static readonly GUIContent GameObjectOrigin = EditorGUIUtility.TrTextContent("Game Object Origin", "Determines which tilemap the GameObjects defined in the Dual Grid Rule Tile should be in.");
    }

    private DualGridTilemapModule targetDualGridTilemapModule;

    private bool hasMultipleTargets = false;
    private List<DualGridTilemapModule> targetDualGridTilemapModules = new();

    private bool showDataTileBoundaries = false;

    private bool showRenderTileBoundaries = false;
    private bool showRenderTileConnections = false;

    /// <summary>
    /// Creates a root "Dual Grid" GameObject with a <see cref="Grid"/> in the active scene.
    /// </summary>
    public static Grid CreateNewDualGrid()
    {
        var newDualGrid = new GameObject("Dual Grid");
        return newDualGrid.AddComponent<Grid>();
    }

    /// <summary>
    /// Creates a data tilemap with a <see cref="DualGridTilemapModule"/> under <paramref name="grid"/> (a new grid when
    /// null) and gives it a render tilemap child and matching colliders.
    /// </summary>
    public static DualGridTilemapModule CreateNewDualGridTilemap(Grid grid = null)
    {
        if (grid == null) grid = CreateNewDualGrid();

        var newDataTilemap = new GameObject("DataTilemap");
        newDataTilemap.AddComponent<Tilemap>();
        var dualGridTilemapModule = newDataTilemap.AddComponent<DualGridTilemapModule>();
        newDataTilemap.transform.parent = grid.transform;

        InitializeRenderTilemap(dualGridTilemapModule);

        return dualGridTilemapModule;
    }

    /// <summary>
    /// Caches the inspected modules and repairs each one's render tilemap, stray renderer and colliders.
    /// </summary>
    private void OnEnable()
    {
        targetDualGridTilemapModule = (DualGridTilemapModule)target;

        hasMultipleTargets = targets.Length > 1;

        if (hasMultipleTargets) targetDualGridTilemapModules = targets.Cast<DualGridTilemapModule>().ToList();
        else targetDualGridTilemapModules = new List<DualGridTilemapModule>() { target as DualGridTilemapModule };

        targetDualGridTilemapModules.ForEach(dualGridTilemapModule => InitializeRenderTilemap(dualGridTilemapModule));
    }

    /// <summary>
    /// Creates the render tilemap child when missing, removes any renderer from the data tilemap and syncs the
    /// tilemap colliders. Ignores a null module.
    /// </summary>
    private static void InitializeRenderTilemap(DualGridTilemapModule dualGridTilemapModule)
    {
        if (dualGridTilemapModule == null) return;

        if (dualGridTilemapModule.RenderTilemap == null)
        {
            CreateRenderTilemapObject(dualGridTilemapModule);
        }

        DestroyTilemapRendererInDataTilemap(dualGridTilemapModule);
        UpdateTilemapColliderComponents(dualGridTilemapModule);
    }

    /// <summary>
    /// Creates the "RenderTilemap" child of <paramref name="targetModule"/>, offset by half a cell so its cells sit on
    /// the data tilemap's corners, with its own tilemap and renderer.
    /// </summary>
    internal static GameObject CreateRenderTilemapObject(DualGridTilemapModule targetModule)
    {
        var renderTilemapObject = new GameObject("RenderTilemap");
        renderTilemapObject.transform.parent = targetModule.transform;
        renderTilemapObject.transform.localPosition = new Vector3(-0.5f, -0.5f, 0f);

        renderTilemapObject.AddComponent<Tilemap>();
        renderTilemapObject.AddComponent<TilemapRenderer>();

        return renderTilemapObject;
    }

    /// <summary>
    /// Destroys a <see cref="TilemapRenderer"/> on the data tilemap, with a warning, since only the render tilemap
    /// draws.
    /// </summary>
    private static void DestroyTilemapRendererInDataTilemap(DualGridTilemapModule dualGridTilemapModule)
    {
        TilemapRenderer renderer = dualGridTilemapModule.GetComponent<TilemapRenderer>();
        DestroyComponentIfExists(renderer, "Dual Grid Tilemaps cannot have TilemapRenderers in the same GameObject. TilemapRenderer has been destroyed.");
    }

    /// <summary>
    /// Adds or removes <see cref="TilemapCollider2D"/> components to match the module and data tile: none when
    /// colliders are disabled or the type is None, on the render tilemap for Sprite, on the data tilemap for Grid.
    /// </summary>
    internal static void UpdateTilemapColliderComponents(DualGridTilemapModule dualGridTilemapModule, bool shouldLogWarnings = true)
    {
        TilemapCollider2D tilemapColliderFromDataTilemap = dualGridTilemapModule.DataTilemap.GetComponent<TilemapCollider2D>();
        TilemapCollider2D tilemapColliderFromRenderTilemap = dualGridTilemapModule.RenderTilemap.GetComponent<TilemapCollider2D>();

        string warningMessage;
        if (dualGridTilemapModule.EnableTilemapCollider == false)
        {
            warningMessage = "Dual Grid Tilemaps cannot have Tilemap Colliders 2D if not enabled in Dual Grid Tilemap Module.";
            DestroyComponentIfExists(tilemapColliderFromDataTilemap, shouldLogWarnings ? warningMessage : null);
            DestroyComponentIfExists(tilemapColliderFromRenderTilemap, shouldLogWarnings ? warningMessage : null);
            return;
        }

        switch (dualGridTilemapModule.DataTile.colliderType)
        {
            case Tile.ColliderType.None:
                warningMessage = "Dual Grid Tilemaps cannot have Tilemap Colliders 2D if Dual Grid Tile has collider type set to none.";
                DestroyComponentIfExists(tilemapColliderFromDataTilemap, shouldLogWarnings ? warningMessage : null);
                DestroyComponentIfExists(tilemapColliderFromRenderTilemap, shouldLogWarnings ? warningMessage : null);
                break;
            case Tile.ColliderType.Sprite:
                warningMessage = "Dual Grid Tilemaps cannot have Tilemap Colliders 2D in the Data Tilemap if Dual Grid Tile has collider type set to Sprite.";
                DestroyComponentIfExists(tilemapColliderFromDataTilemap, shouldLogWarnings ? warningMessage : null);
                if (tilemapColliderFromRenderTilemap == null) dualGridTilemapModule.RenderTilemap.gameObject.AddComponent<TilemapCollider2D>();
                break;
            case Tile.ColliderType.Grid:
                warningMessage = "Dual Grid Tilemaps cannot have Tilemap Colliders 2D in the Render Tilemap if Dual Grid Tile has collider type set to Grid.";
                if (tilemapColliderFromDataTilemap == null) dualGridTilemapModule.DataTilemap.gameObject.AddComponent<TilemapCollider2D>();
                DestroyComponentIfExists(tilemapColliderFromRenderTilemap, shouldLogWarnings ? warningMessage : null);
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Immediately destroys <paramref name="component"/> when it exists, logging <paramref name="warningMessage"/>
    /// first unless it is null.
    /// </summary>
    private static void DestroyComponentIfExists(Component component, string warningMessage = null)
    {
        if (component != null)
        {
            if (warningMessage != null)
                Debug.LogWarning(warningMessage);

            DestroyImmediate(component);
        }
    }

    /// <summary>
    /// Draws the render tile, collider and GameObject origin settings for all selected modules, rebuilding tiles or
    /// colliders when one changes, then the scene view handle toggles.
    /// </summary>
    public override void OnInspectorGUI()
    {
        if (hasMultipleTargets) Undo.RecordObjects(targetDualGridTilemapModules.ToArray(), $"Updated {targetDualGridTilemapModules.Count} Dual Grid Tilemap Modules");
        else Undo.RecordObject(targetDualGridTilemapModule, $"Updated '{targetDualGridTilemapModule.name}' Dual Grid Rule Tile");

        EditorGUI.BeginChangeCheck();

        EditorGUI.BeginChangeCheck();
        EditorGUI.showMixedValue = hasMultipleTargets && targetDualGridTilemapModules.HasDifferentValues(dualGridTilemapModule => dualGridTilemapModule.RenderTile);
        var renderTile = EditorGUILayout.ObjectField(Styles.RenderTile, targetDualGridTilemapModule.RenderTile, typeof(DualGridRuleTile), false) as DualGridRuleTile;
        if (EditorGUI.EndChangeCheck())
        {
            foreach (var dualGridTilemapModule in targetDualGridTilemapModules)
            {
                dualGridTilemapModule.RenderTile = renderTile;
                dualGridTilemapModule.DataTilemap.RefreshAllTiles();
                dualGridTilemapModule.RefreshRenderTilemap();
            }
        }

        EditorGUI.BeginChangeCheck();
        EditorGUI.showMixedValue = hasMultipleTargets && targetDualGridTilemapModules.HasDifferentValues(dualGridTilemapModule => dualGridTilemapModule.EnableTilemapCollider);
        var enableTilemapCollider = EditorGUILayout.Toggle(Styles.EnableTilemapCollider, targetDualGridTilemapModule.EnableTilemapCollider);
        if (EditorGUI.EndChangeCheck())
        {
            foreach (var dualGridTilemapModule in targetDualGridTilemapModules)
            {
                dualGridTilemapModule.EnableTilemapCollider = enableTilemapCollider;
                UpdateTilemapColliderComponents(dualGridTilemapModule, shouldLogWarnings: false);
            }
        }

        EditorGUI.BeginChangeCheck();
        EditorGUI.showMixedValue = hasMultipleTargets && targetDualGridTilemapModules.HasDifferentValues(dualGridTilemapModule => dualGridTilemapModule.GameObjectOrigin);
        var gameObjectOrigin = (GameObjectOrigin)EditorGUILayout.EnumPopup(Styles.GameObjectOrigin, targetDualGridTilemapModule.GameObjectOrigin);
        if (EditorGUI.EndChangeCheck())
        {
            foreach (var dualGridTilemapModule in targetDualGridTilemapModules)
            {
                dualGridTilemapModule.GameObjectOrigin = gameObjectOrigin;
                dualGridTilemapModule.DataTilemap.RefreshAllTiles();
                dualGridTilemapModule.RefreshRenderTilemap();
            }
        }

        GUILayout.Space(5);
        GUILayout.Label("Tools", EditorStyles.boldLabel);

        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(targetDualGridTilemapModule);
        }

        EditorGUI.BeginChangeCheck();
        GUILayout.Label("Visualization Handles", EditorStyles.boldLabel);
        showDataTileBoundaries = EditorGUILayout.Toggle("Data Tile Boundaries", showDataTileBoundaries);
        showRenderTileBoundaries = EditorGUILayout.Toggle("Render Tile Boundaries", showRenderTileBoundaries);
        showRenderTileConnections = EditorGUILayout.Toggle("Render Tile Connections", showRenderTileConnections);

        if (EditorGUI.EndChangeCheck())
        {
            SceneView.RepaintAll();
        }
    }

    /// <summary>
    /// Draws the enabled tile handles for every selected module.
    /// </summary>
    private void OnSceneGUI()
    {
        foreach (var dualGridTilemapModule in targetDualGridTilemapModules)
        {
            DrawDataTileHandles(dualGridTilemapModule);
            DrawRenderTileHandles(dualGridTilemapModule);
        }
    }

    /// <summary>
    /// Outlines every filled data tile in green when data tile boundaries are enabled.
    /// </summary>
    private void DrawDataTileHandles(DualGridTilemapModule dualGridTilemapModule)
    {
        if (!showDataTileBoundaries) return;

        foreach (var position in dualGridTilemapModule.DataTilemap.cellBounds.allPositionsWithin)
        {
            if (!dualGridTilemapModule.DataTilemap.HasTile(position)) continue;

            Vector3 tileCenter = dualGridTilemapModule.DataTilemap.GetCellCenterWorld(position);

            Handles.color = Color.green;
            DrawTileBoundaries(dualGridTilemapModule.DataTilemap, tileCenter, thickness: 3);
        }
    }

    /// <summary>
    /// For every filled render tile, outlines it in yellow and draws red arrows to its filled data tile corners, each
    /// when its toggle is enabled.
    /// </summary>
    private void DrawRenderTileHandles(DualGridTilemapModule dualGridTilemapModule)
    {
        if (!showRenderTileBoundaries && !showRenderTileConnections) return;

        foreach (var renderTilePosition in dualGridTilemapModule.RenderTilemap.cellBounds.allPositionsWithin)
        {
            if (!dualGridTilemapModule.RenderTilemap.HasTile(renderTilePosition)) continue;

            Vector3 tileCenter = dualGridTilemapModule.RenderTilemap.GetCellCenterWorld(renderTilePosition);

            Handles.color = Color.yellow;
            if (showRenderTileBoundaries) DrawTileBoundaries(dualGridTilemapModule.RenderTilemap, tileCenter, thickness: 1);

            Handles.color = Color.red;
            if (showRenderTileConnections) DrawRenderTileConnections(dualGridTilemapModule.DataTilemap, dualGridTilemapModule.RenderTilemap, renderTilePosition, tileCenter);
        }
    }

    /// <summary>
    /// Draws a dot at <paramref name="tileCenter"/> and the outline of one scaled cell of <paramref name="tilemap"/>
    /// around it, in the current handle color.
    /// </summary>
    private static void DrawTileBoundaries(Tilemap tilemap, Vector3 tileCenter, float thickness)
    {
        if (tilemap == null) return;

        Handles.DrawSolidDisc(tileCenter, Vector3.forward, radius: 0.05f);

        var squareSizeX = tilemap.cellSize.x * tilemap.transform.lossyScale.x;
        var squareSizeY = tilemap.cellSize.y * tilemap.transform.lossyScale.y;

        Vector3 topLeft = tileCenter + new Vector3(-squareSizeX / 2, squareSizeY / 2, 0);
        Vector3 topRight = tileCenter + new Vector3(squareSizeX / 2, squareSizeY / 2, 0);
        Vector3 bottomLeft = tileCenter + new Vector3(-squareSizeX / 2, -squareSizeY / 2, 0);
        Vector3 bottomRight = tileCenter + new Vector3(squareSizeX / 2, -squareSizeY / 2, 0);

        Handles.DrawLine(topLeft, topRight, thickness);
        Handles.DrawLine(topRight, bottomRight, thickness);
        Handles.DrawLine(bottomRight, bottomLeft, thickness);
        Handles.DrawLine(bottomLeft, topLeft, thickness);
    }

    /// <summary>
    /// Draws an arrow from a render tile's center toward each of its four data tile corners that holds a tile.
    /// </summary>
    private static void DrawRenderTileConnections(Tilemap dataTilemap, Tilemap renderTilemap, Vector3Int renderTilePosition, Vector3 tileCenter)
    {
        if (dataTilemap == null || renderTilemap == null) return;

        for (int cornerIndex = 0; cornerIndex < DualGridUtils.CornerCount; cornerIndex++)
        {
            Vector3Int dataTilePosition = DualGridUtils.GetDataTilePosition(renderTilePosition, cornerIndex);
            if (dataTilemap.HasTile(dataTilePosition))
            {
                Vector3Int dataTileOffset = dataTilePosition - renderTilePosition;
                Vector3Int neighborOffset = DualGridUtils.ConvertDataTileOffsetToNeighborOffset(dataTileOffset);

                Vector3 corner = tileCenter + new Vector3(neighborOffset.x * renderTilemap.cellSize.x * 0.3f, neighborOffset.y * renderTilemap.cellSize.y * 0.3f, 0f);

                DrawArrow(tileCenter, corner);
            }
        }

        static void DrawArrow(Vector3 start, Vector3 end, float arrowHeadLength = 0.15f, float arrowHeadAngle = 30f)
        {
            Handles.DrawLine(start, end);

            Vector3 direction = (end - start).normalized;

            Vector3 right = Quaternion.Euler(0, 0, arrowHeadAngle) * -direction;
            Vector3 left = Quaternion.Euler(0, 0, -arrowHeadAngle) * -direction;

            Handles.DrawLine(end, end + right * arrowHeadLength);
            Handles.DrawLine(end, end + left * arrowHeadLength);
        }
    }
}
