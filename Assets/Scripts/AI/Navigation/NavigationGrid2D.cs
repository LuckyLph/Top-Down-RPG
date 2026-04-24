using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class NavigationGrid2D : MonoBehaviour
{
    private static readonly Vector3Int[] NeighborOffsets4 =
    {
        new(1, 0, 0),
        new(-1, 0, 0),
        new(0, 1, 0),
        new(0, -1, 0)
    };
    private static readonly Vector3Int[] NeighborOffsets8 =
    {
        new(1, 0, 0),
        new(-1, 0, 0),
        new(0, 1, 0),
        new(0, -1, 0),
        new(1, 1, 0),
        new(1, -1, 0),
        new(-1, 1, 0),
        new(-1, -1, 0)
    };

    [SerializeField] private Tilemap dataTilemap;
    [SerializeField] private Tilemap collisionTilemap;
    [SerializeField] private int nearestCellSearchRadius = 8;
    [Header("Debug")]
    [SerializeField] private bool drawGridBoundsGizmo = true;
    [SerializeField] private bool drawWalkableCellGizmos = false;
    [SerializeField, Min(1)] private int maxWalkableCellsToDraw = 400;
    [SerializeField] private Color gridBoundsColor = new(0.35f, 1f, 1f, 0.8f);
    [SerializeField] private Color walkableCellColor = new(0.25f, 1f, 0.3f, 0.35f);

    private readonly HashSet<Vector3Int> walkableCells = new();
    private GridAStarPathfinder2D pathfinder;
    private BoundsInt walkableBounds;

    public IPathfinder2D Pathfinder => pathfinder;
    public Tilemap DataTilemap => dataTilemap;
    public Tilemap CollisionTilemap => collisionTilemap;
    public BoundsInt WalkableBounds => walkableBounds;
    public bool IsBuilt => pathfinder != null;

    private void Awake()
    {
        BuildGrid();
    }

    public void Configure(Tilemap dataMap, Tilemap collisionMap)
    {
        dataTilemap = dataMap;
        collisionTilemap = collisionMap;
    }

    public void BuildGrid()
    {
        ResolveTilemapsIfNeeded();

        walkableCells.Clear();
        pathfinder = null;
        walkableBounds = default;

        if (dataTilemap == null)
        {
            Debug.LogError("NavigationGrid2D could not find a DataTilemap.", this);
            return;
        }

        BoundsInt bounds = dataTilemap.cellBounds;
        walkableBounds = bounds;

        foreach (Vector3Int cell in bounds.allPositionsWithin)
        {
            if (!dataTilemap.HasTile(cell))
            {
                continue;
            }

            if (collisionTilemap != null && collisionTilemap.HasTile(cell))
            {
                continue;
            }

            walkableCells.Add(cell);
        }

        pathfinder = new GridAStarPathfinder2D(this);
    }

    public bool IsCellWalkable(Vector3Int cell)
    {
        return walkableCells.Contains(cell);
    }

    public Vector3Int WorldToCell(Vector3 worldPosition)
    {
        return dataTilemap != null ? dataTilemap.WorldToCell(worldPosition) : Vector3Int.zero;
    }

    public Vector3 CellToWorldCenter(Vector3Int cell)
    {
        return dataTilemap != null ? dataTilemap.GetCellCenterWorld(cell) : Vector3.zero;
    }

    public IEnumerable<Vector3Int> GetNeighbors4(Vector3Int cell)
    {
        for (int i = 0; i < NeighborOffsets4.Length; i++)
        {
            Vector3Int neighbor = cell + NeighborOffsets4[i];
            if (CanTraverse(cell, neighbor))
            {
                yield return neighbor;
            }
        }
    }

    public IEnumerable<Vector3Int> GetNeighbors8(Vector3Int cell)
    {
        for (int i = 0; i < NeighborOffsets8.Length; i++)
        {
            Vector3Int neighbor = cell + NeighborOffsets8[i];
            if (CanTraverse(cell, neighbor))
            {
                yield return neighbor;
            }
        }
    }

    public bool TryGetNearestWalkableCell(Vector3Int origin, out Vector3Int nearest, int maxRadius = -1)
    {
        if (walkableCells.Contains(origin))
        {
            nearest = origin;
            return true;
        }

        int radiusLimit = maxRadius > 0 ? maxRadius : Mathf.Max(1, nearestCellSearchRadius);
        for (int radius = 1; radius <= radiusLimit; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int dy = radius - Mathf.Abs(dx);

                Vector3Int top = new(origin.x + dx, origin.y + dy, origin.z);
                if (walkableCells.Contains(top))
                {
                    nearest = top;
                    return true;
                }

                Vector3Int bottom = new(origin.x + dx, origin.y - dy, origin.z);
                if (walkableCells.Contains(bottom))
                {
                    nearest = bottom;
                    return true;
                }
            }
        }

        nearest = origin;
        return false;
    }

    public bool CanTraverse(Vector3Int from, Vector3Int to)
    {
        if (!walkableCells.Contains(to))
        {
            return false;
        }

        int dx = to.x - from.x;
        int dy = to.y - from.y;
        int absX = Mathf.Abs(dx);
        int absY = Mathf.Abs(dy);

        if ((absX == 0 && absY == 0) || absX > 1 || absY > 1)
        {
            return false;
        }

        if (absX == 1 && absY == 1)
        {
            // Prevent corner cutting. Both orthogonal cells must be traversable.
            Vector3Int orthogonalA = new(from.x + dx, from.y, from.z);
            Vector3Int orthogonalB = new(from.x, from.y + dy, from.z);
            return walkableCells.Contains(orthogonalA) && walkableCells.Contains(orthogonalB);
        }

        return true;
    }

    public int MovementCost(Vector3Int from, Vector3Int to)
    {
        int dx = Mathf.Abs(to.x - from.x);
        int dy = Mathf.Abs(to.y - from.y);
        return (dx == 1 && dy == 1) ? 14 : 10;
    }

    public int HeuristicCost(Vector3Int from, Vector3Int to)
    {
        int dx = Mathf.Abs(from.x - to.x);
        int dy = Mathf.Abs(from.y - to.y);
        int diagonal = Mathf.Min(dx, dy);
        int straight = Mathf.Max(dx, dy) - diagonal;
        return (14 * diagonal) + (10 * straight);
    }

    public bool HasLineOfSightCells(Vector3Int from, Vector3Int to)
    {
        if (!IsCellWalkable(from) || !IsCellWalkable(to))
        {
            return false;
        }

        int x0 = from.x;
        int y0 = from.y;
        int x1 = to.x;
        int y1 = to.y;
        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        Vector3Int previous = new(x0, y0, from.z);
        while (x0 != x1 || y0 != y1)
        {
            int e2 = err * 2;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }

            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }

            Vector3Int current = new(x0, y0, from.z);
            if (!CanTraverse(previous, current))
            {
                return false;
            }

            previous = current;
        }

        return true;
    }

    private void ResolveTilemapsIfNeeded()
    {
        if (dataTilemap == null)
        {
            GameObject dataObject = GameObject.Find("DataTilemap");
            if (dataObject != null)
            {
                dataTilemap = dataObject.GetComponent<Tilemap>();
            }
        }

        if (collisionTilemap == null)
        {
            GameObject collisionObject = GameObject.Find("CollisionTilemap");
            if (collisionObject != null)
            {
                collisionTilemap = collisionObject.GetComponent<Tilemap>();
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        ResolveTilemapsIfNeeded();

        if (dataTilemap == null)
        {
            return;
        }

        if (drawGridBoundsGizmo)
        {
            DrawGridBoundsGizmo();
        }

        if (!drawWalkableCellGizmos)
        {
            return;
        }

        if (!Application.isPlaying && !IsBuilt)
        {
            BuildGrid();
        }

        Gizmos.color = walkableCellColor;
        int drawn = 0;
        foreach (Vector3Int cell in walkableCells)
        {
            Vector3 center = CellToWorldCenter(cell);
            Gizmos.DrawCube(center, Vector3.one * 0.12f);
            drawn++;
            if (drawn >= maxWalkableCellsToDraw)
            {
                break;
            }
        }
    }

    private void DrawGridBoundsGizmo()
    {
        Bounds localBounds = dataTilemap.localBounds;
        Vector3 center = dataTilemap.transform.TransformPoint(localBounds.center);
        Vector3 size = Vector3.Scale(localBounds.size, dataTilemap.transform.lossyScale);
        if (size.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Gizmos.color = gridBoundsColor;
        Gizmos.DrawWireCube(center, size);
    }
}
