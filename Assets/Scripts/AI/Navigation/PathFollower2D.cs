using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds smoothed paths on a <see cref="NavigationGrid2D"/> and follows their waypoints, returning the velocity
/// its owner should move at each fixed tick. Shared by mobs and players; it owns no engine objects.
/// </summary>
public sealed class PathFollower2D
{
    private const float MinProgressSpeedFraction = 0.2f;

    private readonly List<Vector2> waypoints = new();
    private readonly List<Vector3Int> pathCells = new();
    private readonly List<Vector3Int> smoothedCells = new();
    private readonly List<int> routeCost = new();
    private NavigationGrid2D navigationGrid;
    private TerrainMovementProfile2D movementProfile;
    private PathFollowerSettings2D settings;
    private int waypointIndex;
    private bool hasPath;
    private bool reachedDestination = true;
    private bool isPartialPath;
    private bool reachedResolvedGoal;
    private bool goalWasAdjusted;
    private Vector3Int lastGoalCell;
    private bool hasGoalCell;
    private float stalledTime;
    private Vector2 lastProgressPosition;

    public NavigationGrid2D NavigationGrid => navigationGrid;
    public TerrainMovementProfile2D MovementProfile => movementProfile;
    public PathFollowerSettings2D Settings => settings;
    public bool IsConfigured => navigationGrid != null && navigationGrid.IsBuilt;
    public bool HasPath => hasPath;
    public bool ReachedDestination => reachedDestination;
    public bool IsPartialPath => isPartialPath;
    public bool ReachedResolvedGoal => reachedResolvedGoal;
    public bool GoalWasAdjusted => goalWasAdjusted;
    public bool HasGoalCell => hasGoalCell;
    public Vector3Int LastGoalCell => lastGoalCell;
    public float StalledTime => stalledTime;
    public IReadOnlyList<Vector2> Waypoints => waypoints;
    public int NextWaypointIndex => waypointIndex;

    /// <summary>
    /// Sets the grid, terrain rules and distances used from now on, and clears the current path.
    /// </summary>
    public void Configure(NavigationGrid2D grid, TerrainMovementProfile2D profile, PathFollowerSettings2D followerSettings)
    {
        navigationGrid = grid;
        movementProfile = profile;
        settings = followerSettings;
        Clear();
    }

    /// <summary>
    /// Builds a path from <paramref name="start"/> to <paramref name="worldGoal"/>. Returns false, leaving the state
    /// untouched, when no grid is configured; returns false and clears the path when no path exists.
    /// </summary>
    public bool BuildPath(Vector2 start, Vector2 worldGoal, bool allowPartial)
    {
        if (!IsConfigured)
        {
            return false;
        }

        Vector3Int startCell = navigationGrid.WorldToCell(start);
        Vector3Int goalCell = navigationGrid.WorldToCell(worldGoal);

        if (settings.UseStraightLineShortcut && IsStraightLineOptimal(startCell, goalCell))
        {
            hasGoalCell = true;
            lastGoalCell = goalCell;
            waypoints.Clear();
            waypoints.Add(worldGoal);
            FinishPath(start, false, true, false);
            return true;
        }

        PathRequest request = new(startCell, goalCell, allowPartial, movementProfile);
        PathResult result = navigationGrid.Pathfinder.FindPath(request, pathCells);

        hasGoalCell = true;
        lastGoalCell = goalCell;

        if (!result.Success || result.Cells.Count == 0)
        {
            Clear();
            return false;
        }

        waypoints.Clear();
        IReadOnlyList<Vector3Int> smoothed = SmoothPathCells(result.Cells);
        int firstCellIndex = 0;
        if (smoothed.Count > 1 && smoothed[0] == startCell)
        {
            firstCellIndex = 1;
        }

        for (int i = firstCellIndex; i < smoothed.Count; i++)
        {
            bool isFinalCell = i == smoothed.Count - 1;
            Vector2 waypoint = isFinalCell && !result.IsPartial
                ? worldGoal
                : navigationGrid.CellToWorldCenter(smoothed[i]);

            waypoints.Add(waypoint);
        }

        if (waypoints.Count == 0 && !result.IsPartial && Vector2.Distance(start, worldGoal) > settings.ArrivalDistance)
        {
            waypoints.Add(worldGoal);
        }

        FinishPath(start, result.IsPartial, result.ReachedResolvedGoal, result.GoalWasAdjusted);
        return true;
    }

    /// <summary>
    /// True when the goal cell is walkable and in the same region as the nearest walkable cell to the start.
    /// </summary>
    public bool CanReach(Vector2 start, Vector2 worldGoal)
    {
        if (!IsConfigured)
        {
            return false;
        }

        Vector3Int goalCell = navigationGrid.WorldToCell(worldGoal);
        if (!navigationGrid.IsCellWalkable(goalCell, movementProfile))
        {
            return false;
        }

        Vector3Int startCell = navigationGrid.WorldToCell(start);
        return navigationGrid.TryGetNearestWalkableCell(startCell, movementProfile, out Vector3Int nearestStart, settings.NearestCellSearchRadius)
            && navigationGrid.AreCellsConnected(nearestStart, goalCell, movementProfile);
    }

    /// <summary>
    /// Advances along the path from <paramref name="position"/> and returns the velocity toward the next waypoint
    /// at <paramref name="moveSpeed"/>, or zero once there is no path. Stall time grows while the position moves
    /// slower than a fraction of <paramref name="moveSpeed"/>.
    /// </summary>
    public Vector2 Tick(Vector2 position, float moveSpeed, float deltaTime)
    {
        if (!hasPath)
        {
            return Vector2.zero;
        }

        TrackProgress(position, moveSpeed, deltaTime);

        while (waypointIndex < waypoints.Count && Vector2.Distance(position, waypoints[waypointIndex]) <= GetReachThreshold(waypointIndex))
        {
            waypointIndex++;
        }

        if (waypointIndex >= waypoints.Count)
        {
            hasPath = false;
            reachedDestination = true;
            return Vector2.zero;
        }

        Vector2 direction = (waypoints[waypointIndex] - position).normalized;
        return direction * moveSpeed;
    }

    /// <summary>
    /// Drops the current path and goal.
    /// </summary>
    public void Clear()
    {
        waypoints.Clear();
        waypointIndex = 0;
        hasPath = false;
        reachedDestination = true;
        isPartialPath = false;
        reachedResolvedGoal = false;
        goalWasAdjusted = false;
        hasGoalCell = false;
        stalledTime = 0f;
    }

    private bool IsStraightLineOptimal(Vector3Int startCell, Vector3Int goalCell)
    {
        return navigationGrid.TryGetLineCost(startCell, goalCell, movementProfile, out int lineCost)
            && lineCost <= navigationGrid.HeuristicCost(startCell, goalCell, movementProfile);
    }

    private void FinishPath(Vector2 start, bool isPartial, bool reachedGoal, bool goalAdjusted)
    {
        if (waypoints.Count > 0 && Vector2.Distance(start, waypoints[0]) <= settings.WaypointReachDistance)
        {
            waypoints.RemoveAt(0);
        }

        waypointIndex = 0;
        stalledTime = 0f;
        lastProgressPosition = start;
        hasPath = waypoints.Count > 0;
        reachedDestination = !hasPath;
        isPartialPath = isPartial;
        reachedResolvedGoal = reachedGoal;
        goalWasAdjusted = goalAdjusted;
    }

    private IReadOnlyList<Vector3Int> SmoothPathCells(IReadOnlyList<Vector3Int> sourceCells)
    {
        if (sourceCells.Count <= 2)
        {
            return sourceCells;
        }

        routeCost.Clear();
        routeCost.Add(0);
        for (int i = 1; i < sourceCells.Count; i++)
        {
            routeCost.Add(routeCost[i - 1] + navigationGrid.MovementCost(sourceCells[i - 1], sourceCells[i], movementProfile));
        }

        smoothedCells.Clear();
        smoothedCells.Add(sourceCells[0]);
        int anchorIndex = 0;

        while (anchorIndex < sourceCells.Count - 1)
        {
            int furthestVisible = anchorIndex + 1;
            for (int i = anchorIndex + 2; i < sourceCells.Count; i++)
            {
                if (navigationGrid.TryGetLineCost(sourceCells[anchorIndex], sourceCells[i], movementProfile, out int lineCost)
                    && lineCost <= routeCost[i] - routeCost[anchorIndex])
                {
                    furthestVisible = i;
                }
                else
                {
                    break;
                }
            }

            smoothedCells.Add(sourceCells[furthestVisible]);
            anchorIndex = furthestVisible;
        }

        return smoothedCells;
    }

    private void TrackProgress(Vector2 position, float moveSpeed, float deltaTime)
    {
        float minStep = moveSpeed * MinProgressSpeedFraction * deltaTime;
        stalledTime = (position - lastProgressPosition).sqrMagnitude < minStep * minStep ? stalledTime + deltaTime : 0f;
        lastProgressPosition = position;
    }

    private float GetReachThreshold(int index)
    {
        return index == waypoints.Count - 1
            ? Mathf.Max(settings.WaypointReachDistance, settings.ArrivalDistance)
            : settings.WaypointReachDistance;
    }
}
