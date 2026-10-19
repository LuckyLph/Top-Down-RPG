using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Draws the collision tiles of every enabled <see cref="TilemapCollider2D"/>, selected or not, in the Scene view and in
/// the Game view with gizmos on: a translucent fill and an outline on each cell whose tile has a collider. The walls'
/// collision tilemap has no renderer and sits on the Obstacles layer the camera culls, so nothing else shows it.
/// Toggled from the Tools menu.
/// </summary>
public static class CollisionTileGizmo
{
    private const string EnabledPrefKey = "TopDownRPG.Gizmos.CollisionTiles";
    private const string ToggleMenuPath = ProjectScenes.MenuRoot + "Gizmos/Collision Tiles";
    private const int OutlinePointsPerCell = 8;

    private static readonly Color FillColor = new(1f, 0.25f, 0.2f, 0.25f);
    private static readonly Color OutlineColor = new(1f, 0.3f, 0.2f, 0.8f);

    private static readonly List<Vector3Int> cells = new();
    private static TileBase[] tiles = Array.Empty<TileBase>();
    private static Vector3[] outline = Array.Empty<Vector3>();

    public static bool Enabled
    {
        get => EditorPrefs.GetBool(EnabledPrefKey, true);
        set
        {
            EditorPrefs.SetBool(EnabledPrefKey, value);
            SceneView.RepaintAll();
        }
    }

    /// <summary>
    /// Flips the gizmo on or off from the Tools menu.
    /// </summary>
    [MenuItem(ToggleMenuPath)]
    private static void Toggle()
    {
        Enabled = !Enabled;
    }

    /// <summary>
    /// Keeps the menu checkmark in step with the setting; the entry is always available.
    /// </summary>
    [MenuItem(ToggleMenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(ToggleMenuPath, Enabled);
        return true;
    }

    /// <summary>
    /// Fills and outlines each collision cell of the tilemap under <paramref name="collider"/>, in the tilemap's space.
    /// </summary>
    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawCollisionTiles(TilemapCollider2D collider, GizmoType gizmoType)
    {
        if (!Enabled || !collider.isActiveAndEnabled || !collider.TryGetComponent(out Tilemap tilemap))
        {
            return;
        }

        CollectCollisionCells(tilemap, cells);
        if (cells.Count == 0)
        {
            return;
        }

        int pointCount = cells.Count * OutlinePointsPerCell;
        if (outline.Length < pointCount)
        {
            outline = new Vector3[pointCount];
        }

        Vector3 size = new(tilemap.cellSize.x, tilemap.cellSize.y, 0f);
        Vector3 halfRight = new(size.x * 0.5f, 0f, 0f);
        Vector3 halfUp = new(0f, size.y * 0.5f, 0f);
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = tilemap.transform.localToWorldMatrix;
        Gizmos.color = FillColor;
        for (int i = 0; i < cells.Count; i++)
        {
            Vector3 centre = tilemap.GetCellCenterLocal(cells[i]);
            Gizmos.DrawCube(centre, size);
            Vector3 bottomLeft = centre - halfRight - halfUp;
            Vector3 bottomRight = centre + halfRight - halfUp;
            Vector3 topRight = centre + halfRight + halfUp;
            Vector3 topLeft = centre - halfRight + halfUp;
            int point = i * OutlinePointsPerCell;
            outline[point] = bottomLeft;
            outline[point + 1] = bottomRight;
            outline[point + 2] = bottomRight;
            outline[point + 3] = topRight;
            outline[point + 4] = topRight;
            outline[point + 5] = topLeft;
            outline[point + 6] = topLeft;
            outline[point + 7] = bottomLeft;
        }

        Gizmos.color = OutlineColor;
        Gizmos.DrawLineList(outline.AsSpan(0, pointCount));
        Gizmos.matrix = previousMatrix;
    }

    /// <summary>
    /// Replaces the contents of <paramref name="result"/> with every cell of <paramref name="tilemap"/> holding a tile
    /// whose collider type is not <see cref="Tile.ColliderType.None"/>, in the order the tilemap returns its block: z,
    /// then x, then y.
    /// </summary>
    internal static void CollectCollisionCells(Tilemap tilemap, List<Vector3Int> result)
    {
        result.Clear();
        BoundsInt bounds = tilemap.cellBounds;
        int depth = bounds.size.z;
        int rowSize = depth * bounds.size.x;
        int count = rowSize * bounds.size.y;
        if (count <= 0)
        {
            return;
        }

        if (tiles.Length < count)
        {
            tiles = new TileBase[count];
        }

        tilemap.GetTilesBlockNonAlloc(bounds, tiles);
        for (int i = 0; i < count; i++)
        {
            if (tiles[i] == null)
            {
                continue;
            }

            Vector3Int cell = new(bounds.xMin + i % rowSize / depth, bounds.yMin + i / rowSize, bounds.zMin + i % depth);
            if (tilemap.GetColliderType(cell) != Tile.ColliderType.None)
            {
                result.Add(cell);
            }
        }

        Array.Clear(tiles, 0, count);
    }
}
