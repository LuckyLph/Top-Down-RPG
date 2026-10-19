using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds smoothed paths on an <see cref="INavigation2D"/> (an area's grid or a world's navigation) and follows their
/// waypoints, returning the velocity its owner should move at each fixed tick. When the navigation changes under a path
/// (its <see cref="INavigation2D.Version"/> moves on: a tile edit, a chunk's page loading or unloading), the rest of the
/// path is checked once and rebuilt toward the same goal if a cell on it was blocked.
/// Shared by mobs and players; it owns no engine objects.
/// </summary>
public sealed class PathFollower2D
{
    private const float MinProgressSpeedFraction = 0.2f;

    private readonly List<Vector2> waypoints = new();
    private readonly List<Vector3Int> pathCells = new();
    private readonly List<Vector3Int> smoothedCells = new();
    private readonly List<int> routeCost = new();
    private INavigation2D navigation;
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
    private bool pathFollowsGrid;
    private int pathGridVersion;
    private Vector2 pathGoal;
    private bool pathAllowsPartial;

    public INavigation2D Navigation => navigation;
    public TerrainMovementProfile2D MovementProfile => movementProfile;
    public PathFollowerSettings2D Settings => settings;
    public bool IsConfigured => navigation != null && navigation.IsBuilt;
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
    /// Sets the navigation, terrain rules and distances used from now on, and clears the current path.
    /// </summary>
    public void Configure(INavigation2D pathNavigation, TerrainMovementProfile2D profile, PathFollowerSettings2D followerSettings)
    {
        navigation = pathNavigation;
        movementProfile = profile;
        settings = followerSettings;
        Clear();
    }

    /// <summary>
    /// Builds a path from <paramref name="start"/> to <paramref name="worldGoal"/>, moved off the cliff face first
    /// (<see cref="INavigation2D.MoveOffCliffFace"/>). Returns false, leaving the state untouched, when no grid is
    /// configured; returns false and clears the path when no path exists.
    /// </summary>
    public bool BuildPath(Vector2 start, Vector2 worldGoal, bool allowPartial)
    {
        if (!IsConfigured)
        {
            return false;
        }

        worldGoal = navigation.MoveOffCliffFace(worldGoal);
        Vector3Int startCell = navigation.WorldToCell(start);
        Vector3Int goalCell = navigation.WorldToCell(worldGoal);

        if (settings.UseStraightLineShortcut && IsStraightLineOptimal(startCell, goalCell))
        {
            BuildStraightPath(start, worldGoal);
            hasGoalCell = true;
            lastGoalCell = goalCell;
            RememberGridPath(worldGoal, allowPartial);
            return true;
        }

        PathRequest request = new(startCell, goalCell, allowPartial, movementProfile);
        PathResult result = navigation.Pathfinder.FindPath(request, pathCells);

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
                : navigation.CellToWorldCenter(smoothed[i]);

            waypoints.Add(waypoint);
        }

        if (waypoints.Count == 0 && !result.IsPartial && Vector2.Distance(start, worldGoal) > settings.ArrivalDistance)
        {
            waypoints.Add(worldGoal);
        }

        FinishPath(start, result.IsPartial, result.ReachedResolvedGoal, result.GoalWasAdjusted);
        RememberGridPath(worldGoal, allowPartial);
        return true;
    }

    /// <summary>
    /// Replaces the path with a single waypoint at <paramref name="worldGoal"/>, ignoring the grid. Used where there
    /// is no grid to path on, and by the straight-line shortcut.
    /// </summary>
    public void BuildStraightPath(Vector2 start, Vector2 worldGoal)
    {
        hasGoalCell = false;
        pathFollowsGrid = false;
        waypoints.Clear();
        waypoints.Add(worldGoal);
        FinishPath(start, false, true, false);
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

        Vector3Int goalCell = navigation.WorldToCell(worldGoal);
        if (!navigation.IsCellWalkable(goalCell, movementProfile))
        {
            return false;
        }

        Vector3Int startCell = navigation.WorldToCell(start);
        return navigation.TryGetNearestWalkableCell(startCell, movementProfile, out Vector3Int nearestStart, settings.NearestCellSearchRadius)
            && navigation.AreCellsConnected(nearestStart, goalCell, movementProfile);
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

        if (pathFollowsGrid && navigation.Version != pathGridVersion && !RevalidatePath(position))
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
        pathFollowsGrid = false;
    }

    /// <summary>
    /// Records the grid version, goal and partial flag of a path built on the grid, so <see cref="Tick"/> can check it
    /// again after the grid changes.
    /// </summary>
    private void RememberGridPath(Vector2 worldGoal, bool allowPartial)
    {
        pathFollowsGrid = true;
        pathGridVersion = navigation.Version;
        pathGoal = worldGoal;
        pathAllowsPartial = allowPartial;
    }

    /// <summary>
    /// After the grid changed: keeps the path when every remaining segment can still be walked, otherwise rebuilds it
    /// from <paramref name="position"/> toward the same goal. False when no path is left.
    /// </summary>
    private bool RevalidatePath(Vector2 position)
    {
        pathGridVersion = navigation.Version;
        if (IsRemainingPathWalkable(position))
        {
            return true;
        }

        BuildPath(position, pathGoal, pathAllowsPartial);
        return hasPath;
    }

    /// <summary>
    /// Whether the straight lines from <paramref name="position"/> through each remaining waypoint can all still be
    /// walked.
    /// </summary>
    private bool IsRemainingPathWalkable(Vector2 position)
    {
        Vector3Int from = navigation.WorldToCell(position);
        for (int i = waypointIndex; i < waypoints.Count; i++)
        {
            Vector3Int to = navigation.WorldToCell(waypoints[i]);
            if (to != from && !navigation.TryGetLineCost(from, to, movementProfile, out _))
            {
                return false;
            }

            from = to;
        }

        return true;
    }

    /// <summary>
    /// Whether the straight line between the two cells is walkable and costs no more than the octile heuristic, so no
    /// route can be cheaper.
    /// </summary>
    private bool IsStraightLineOptimal(Vector3Int startCell, Vector3Int goalCell)
    {
        return navigation.TryGetLineCost(startCell, goalCell, movementProfile, out int lineCost)
            && lineCost <= navigation.HeuristicCost(startCell, goalCell, movementProfile);
    }

    /// <summary>
    /// Common end of a successful build: drops a first waypoint already within reach of <paramref name="start"/>,
    /// resets progress tracking, and records the path flags.
    /// </summary>
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

    /// <summary>
    /// Skips intermediate cells of the A* route wherever the straight line from an anchor cell is walkable and costs no
    /// more than the route it replaces. Returns <paramref name="sourceCells"/> itself when it has two cells or fewer.
    /// </summary>
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
            routeCost.Add(routeCost[i - 1] + navigation.MovementCost(sourceCells[i - 1], sourceCells[i], movementProfile));
        }

        smoothedCells.Clear();
        smoothedCells.Add(sourceCells[0]);
        int anchorIndex = 0;

        while (anchorIndex < sourceCells.Count - 1)
        {
            int furthestVisible = anchorIndex + 1;
            for (int i = anchorIndex + 2; i < sourceCells.Count; i++)
            {
                if (navigation.TryGetLineCost(sourceCells[anchorIndex], sourceCells[i], movementProfile, out int lineCost)
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

    /// <summary>
    /// Grows the stall time while the position moved less than a fifth of <paramref name="moveSpeed"/> over the step,
    /// and resets it otherwise.
    /// </summary>
    private void TrackProgress(Vector2 position, float moveSpeed, float deltaTime)
    {
        float minStep = moveSpeed * MinProgressSpeedFraction * deltaTime;
        stalledTime = (position - lastProgressPosition).sqrMagnitude < minStep * minStep ? stalledTime + deltaTime : 0f;
        lastProgressPosition = position;
    }

    /// <summary>
    /// How close counts as reaching the waypoint at <paramref name="index"/>: the larger of the reach and arrival
    /// distances for the last one, the reach distance for the others.
    /// </summary>
    private float GetReachThreshold(int index)
    {
        return index == waypoints.Count - 1
            ? Mathf.Max(settings.WaypointReachDistance, settings.ArrivalDistance)
            : settings.WaypointReachDistance;
    }
}
