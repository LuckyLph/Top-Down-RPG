using System.Collections.Generic;
using UnityEngine;

public class MobPathAgent2D : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool drawPathGizmos = true;
    [SerializeField] private Color pathColor = new(1f, 1f, 0f, 0.9f);
    [SerializeField] private Color partialPathColor = new(1f, 0.5f, 0.2f, 0.9f);
    [SerializeField] private Color goalColor = new(0.2f, 1f, 0.2f, 0.9f);

    private readonly List<Vector2> waypoints = new();
    private NavigationGrid2D navigationGrid;
    private MobConfig config;
    private MobMotor2D motor;
    private int waypointIndex;
    private bool hasPath;
    private bool reachedDestination = true;
    private bool isPartialPath;
    private bool reachedResolvedGoal;
    private bool goalWasAdjusted;
    private Vector3Int lastGoalCell;
    private bool hasGoalCell;

    public NavigationGrid2D NavigationGrid => navigationGrid;
    public bool HasPath => hasPath;
    public bool ReachedDestination => reachedDestination;
    public bool IsPartialPath => isPartialPath;
    public bool ReachedResolvedGoal => reachedResolvedGoal;
    public bool GoalWasAdjusted => goalWasAdjusted;
    public bool HasGoalCell => hasGoalCell;
    public Vector3Int LastGoalCell => lastGoalCell;

    private void Awake()
    {
        motor = GetComponent<MobMotor2D>();
    }

    public void Initialize(NavigationGrid2D navGrid, MobMotor2D mobMotor, MobConfig mobConfig)
    {
        navigationGrid = navGrid;
        motor = mobMotor;
        config = mobConfig;
        ClearPath();
    }

    public bool IsGoalCellChanged(Vector2 worldGoal)
    {
        if (navigationGrid == null)
        {
            return true;
        }

        Vector3Int currentGoalCell = navigationGrid.WorldToCell(worldGoal);
        return !hasGoalCell || currentGoalCell != lastGoalCell;
    }

    public bool BuildPathToWorld(Vector2 worldGoal, bool allowPartial)
    {
        if (navigationGrid == null || navigationGrid.Pathfinder == null || motor == null || config == null)
        {
            return false;
        }

        Vector3Int startCell = navigationGrid.WorldToCell(motor.Position);
        Vector3Int goalCell = navigationGrid.WorldToCell(worldGoal);

        // Avoid resetting waypoint progression when chasing the same goal cell.
        if (hasGoalCell && hasPath && goalCell == lastGoalCell)
        {
            return true;
        }

        PathRequest request = new(startCell, goalCell, allowPartial, config.MovementProfile);
        PathResult result = navigationGrid.Pathfinder.FindPath(request);

        hasGoalCell = true;
        lastGoalCell = goalCell;

        if (!result.Success || result.Cells.Count == 0)
        {
            ClearPath();
            return false;
        }

        waypoints.Clear();
        IReadOnlyList<Vector3Int> smoothedCells = SmoothPathCells(result.Cells);
        int firstCellIndex = 0;
        if (smoothedCells.Count > 1 && smoothedCells[0] == startCell)
        {
            // Repathing should continue forward from the current cell instead of
            // steering back to its center, which causes visible left/right jitter.
            firstCellIndex = 1;
        }

        for (int i = firstCellIndex; i < smoothedCells.Count; i++)
        {
            bool isFinalCell = i == smoothedCells.Count - 1;
            Vector2 waypoint = isFinalCell && !result.IsPartial
                ? worldGoal
                : navigationGrid.CellToWorldCenter(smoothedCells[i]);

            waypoints.Add(waypoint);
        }

        if (waypoints.Count == 0 && !result.IsPartial && Vector2.Distance(motor.Position, worldGoal) > config.arrivalDistance)
        {
            // Targets inside the same walkable cell still need direct pursuit.
            waypoints.Add(worldGoal);
        }

        // Drop the first waypoint if we are already standing on it.
        if (waypoints.Count > 0 && Vector2.Distance(motor.Position, waypoints[0]) <= config.waypointReachDistance)
        {
            waypoints.RemoveAt(0);
        }

        waypointIndex = 0;
        hasPath = waypoints.Count > 0;
        reachedDestination = !hasPath;
        isPartialPath = result.IsPartial;
        reachedResolvedGoal = result.ReachedResolvedGoal;
        goalWasAdjusted = result.GoalWasAdjusted;

        if (!hasPath)
        {
            motor.Stop();
        }

        return true;
    }

    // Called every frame by idle/patrol/return states, so this uses the grid's cached connectivity
    // instead of running A*. Mirrors the pathfinder: the start snaps to the nearest walkable cell,
    // while an unwalkable goal counts as unreachable.
    public bool CanReachWorldTarget(Vector2 worldGoal)
    {
        if (navigationGrid == null || !navigationGrid.IsBuilt || motor == null || config == null)
        {
            return false;
        }

        TerrainMovementProfile2D movementProfile = config.MovementProfile;
        Vector3Int goalCell = navigationGrid.WorldToCell(worldGoal);
        if (!navigationGrid.IsCellWalkable(goalCell, movementProfile))
        {
            return false;
        }

        Vector3Int startCell = navigationGrid.WorldToCell(motor.Position);
        return navigationGrid.TryGetNearestWalkableCell(startCell, movementProfile, out Vector3Int start)
            && navigationGrid.AreCellsConnected(start, goalCell, movementProfile);
    }

    private IReadOnlyList<Vector3Int> SmoothPathCells(IReadOnlyList<Vector3Int> sourceCells)
    {
        if (sourceCells.Count <= 2 || navigationGrid == null)
        {
            return sourceCells;
        }

        List<Vector3Int> smoothed = new() { sourceCells[0] };
        int anchorIndex = 0;

        while (anchorIndex < sourceCells.Count - 1)
        {
            int furthestVisible = anchorIndex + 1;
            for (int i = anchorIndex + 2; i < sourceCells.Count; i++)
            {
                if (navigationGrid.HasLineOfSightCells(sourceCells[anchorIndex], sourceCells[i], config != null ? config.MovementProfile : null))
                {
                    furthestVisible = i;
                }
                else
                {
                    break;
                }
            }

            smoothed.Add(sourceCells[furthestVisible]);
            anchorIndex = furthestVisible;
        }

        return smoothed;
    }

    public void FixedTick()
    {
        if (motor == null)
        {
            return;
        }

        if (!hasPath || config == null)
        {
            motor.Stop();
            return;
        }

        Vector2 position = motor.Position;
        float threshold = config.waypointReachDistance;

        while (waypointIndex < waypoints.Count && Vector2.Distance(position, waypoints[waypointIndex]) <= threshold)
        {
            waypointIndex++;

            // Be more forgiving for the final waypoint to avoid jitter around destination.
            if (waypointIndex == waypoints.Count - 1)
            {
                threshold = Mathf.Max(config.waypointReachDistance, config.arrivalDistance);
            }
        }

        if (waypointIndex >= waypoints.Count)
        {
            hasPath = false;
            reachedDestination = true;
            motor.Stop();
            return;
        }

        Vector2 nextPoint = waypoints[waypointIndex];
        Vector2 direction = (nextPoint - position).normalized;
        motor.SetDesiredVelocity(direction * motor.MoveSpeed);
    }

    public void ClearPath()
    {
        waypoints.Clear();
        waypointIndex = 0;
        hasPath = false;
        reachedDestination = true;
        isPartialPath = false;
        reachedResolvedGoal = false;
        goalWasAdjusted = false;
        hasGoalCell = false;

        if (motor != null)
        {
            motor.Stop();
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawPathGizmos)
        {
            return;
        }

        if (hasGoalCell && navigationGrid != null)
        {
            Gizmos.color = goalColor;
            Gizmos.DrawWireSphere(navigationGrid.CellToWorldCenter(lastGoalCell), 0.08f);
        }

        if (!hasPath || waypoints.Count == 0)
        {
            return;
        }

        Gizmos.color = isPartialPath ? partialPathColor : pathColor;
        Vector3 previous = transform.position;
        int startIndex = Mathf.Clamp(waypointIndex, 0, waypoints.Count - 1);
        for (int i = startIndex; i < waypoints.Count; i++)
        {
            Vector3 waypoint = waypoints[i];
            Gizmos.DrawLine(previous, waypoint);
            Gizmos.DrawWireSphere(waypoint, 0.06f);
            previous = waypoint;
        }
    }
}
