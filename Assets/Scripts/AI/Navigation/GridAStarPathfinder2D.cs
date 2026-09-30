using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

public class GridAStarPathfinder2D : IPathfinder2D
{
    private static readonly ProfilerMarker FindPathMarker = new("GridAStarPathfinder2D.FindPath");

    private readonly NavigationGrid2D navigationGrid;

    public GridAStarPathfinder2D(NavigationGrid2D navGrid)
    {
        navigationGrid = navGrid;
    }

    // Cells closed by the most recent search; lets tests and benchmarks compare search effort.
    internal int LastExpandedCount { get; private set; }

    public PathResult FindPath(PathRequest request)
    {
        using (FindPathMarker.Auto())
        {
            return Search(request);
        }
    }

    private PathResult Search(PathRequest request)
    {
        LastExpandedCount = 0;
        if (navigationGrid == null || !navigationGrid.IsBuilt)
        {
            return PathResult.Failure;
        }

        TerrainMovementProfile2D movementProfile = request.MovementProfile;

        if (!navigationGrid.TryGetNearestWalkableCell(request.StartCell, movementProfile, out Vector3Int start))
        {
            return PathResult.Failure;
        }

        Vector3Int goal = request.GoalCell;
        bool adjustedGoalToNearestWalkable = false;
        if (!navigationGrid.IsCellWalkable(goal, movementProfile))
        {
            if (!request.AllowPartial)
            {
                return PathResult.Failure;
            }

            if (!navigationGrid.TryGetNearestWalkableCell(goal, movementProfile, out goal))
            {
                return PathResult.Failure;
            }

            adjustedGoalToNearestWalkable = true;
        }

        // Connectivity is cached per profile, so a goal in another region fails without searching it.
        if (!request.AllowPartial && !navigationGrid.AreCellsConnected(start, goal, movementProfile))
        {
            return PathResult.Failure;
        }

        int maxExpanded = navigationGrid.MaxSearchCells;

        OpenHeap openHeap = new();
        HashSet<Vector3Int> closedSet = new();
        Dictionary<Vector3Int, Vector3Int> cameFrom = new();
        int startHeuristic = navigationGrid.HeuristicCost(start, goal, movementProfile);
        Dictionary<Vector3Int, int> gScore = new() { [start] = 0 };
        Dictionary<Vector3Int, int> fScore = new() { [start] = startHeuristic };
        Dictionary<Vector3Int, int> insertionOrder = new() { [start] = 0 };
        openHeap.Push(new OpenEntry(start, startHeuristic, startHeuristic, 0));

        Vector3Int closestToGoal = start;
        int closestHeuristic = startHeuristic;

        while (openHeap.Count > 0 && LastExpandedCount < maxExpanded)
        {
            OpenEntry entry = openHeap.Pop();
            Vector3Int current = entry.Cell;
            if (closedSet.Contains(current) || entry.F != fScore[current])
            {
                // Stale entry left behind by a later, cheaper push of the same cell.
                continue;
            }

            if (current == goal)
            {
                return new PathResult(
                    success: true,
                    isPartial: adjustedGoalToNearestWalkable,
                    goalWasAdjusted: adjustedGoalToNearestWalkable,
                    reachedResolvedGoal: true,
                    cells: ReconstructPath(cameFrom, current));
            }

            closedSet.Add(current);
            LastExpandedCount++;

            int heuristic = navigationGrid.HeuristicCost(current, goal, movementProfile);
            if (heuristic < closestHeuristic)
            {
                closestHeuristic = heuristic;
                closestToGoal = current;
            }

            foreach (Vector3Int neighbor in navigationGrid.GetNeighbors8(current, movementProfile))
            {
                if (closedSet.Contains(neighbor))
                {
                    continue;
                }

                int tentativeG = gScore[current] + navigationGrid.MovementCost(current, neighbor, movementProfile);
                if (!gScore.TryGetValue(neighbor, out int neighborG) || tentativeG < neighborG)
                {
                    int neighborHeuristic = navigationGrid.HeuristicCost(neighbor, goal, movementProfile);
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    fScore[neighbor] = tentativeG + neighborHeuristic;

                    if (!insertionOrder.TryGetValue(neighbor, out int order))
                    {
                        order = insertionOrder.Count;
                        insertionOrder[neighbor] = order;
                    }

                    openHeap.Push(new OpenEntry(neighbor, fScore[neighbor], neighborHeuristic, order));
                }
            }
        }

        if (!request.AllowPartial || closestToGoal == start)
        {
            return PathResult.Failure;
        }

        return new PathResult(
            success: true,
            isPartial: true,
            goalWasAdjusted: adjustedGoalToNearestWalkable,
            reachedResolvedGoal: false,
            cells: ReconstructPath(cameFrom, closestToGoal));
    }

    private static IReadOnlyList<Vector3Int> ReconstructPath(Dictionary<Vector3Int, Vector3Int> cameFrom, Vector3Int current)
    {
        List<Vector3Int> path = new() { current };
        while (cameFrom.TryGetValue(current, out Vector3Int previous))
        {
            current = previous;
            path.Add(current);
        }

        path.Reverse();
        return path;
    }

    private readonly struct OpenEntry
    {
        public OpenEntry(Vector3Int cell, int f, int h, int order)
        {
            Cell = cell;
            F = f;
            H = h;
            Order = order;
        }

        public Vector3Int Cell { get; }
        public int F { get; }
        public int H { get; }
        public int Order { get; }

        // Ties on F go to the cell closer to the goal, so the search runs down one of many equal-cost
        // routes instead of widening across all of them (open 8-way grids tie constantly). Remaining
        // ties go to the cell first added to the open set, keeping paths deterministic.
        public bool IsBefore(OpenEntry other)
        {
            if (F != other.F)
            {
                return F < other.F;
            }

            return H != other.H ? H < other.H : Order < other.Order;
        }
    }

    // Binary min-heap. Decrease-key is handled by pushing a new entry and skipping stale ones on pop.
    private sealed class OpenHeap
    {
        private readonly List<OpenEntry> entries = new();

        public int Count => entries.Count;

        public void Push(OpenEntry entry)
        {
            entries.Add(entry);
            int index = entries.Count - 1;
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (!entries[index].IsBefore(entries[parent]))
                {
                    break;
                }

                (entries[index], entries[parent]) = (entries[parent], entries[index]);
                index = parent;
            }
        }

        public OpenEntry Pop()
        {
            OpenEntry top = entries[0];
            int lastIndex = entries.Count - 1;
            entries[0] = entries[lastIndex];
            entries.RemoveAt(lastIndex);

            int index = 0;
            while (true)
            {
                int left = (index * 2) + 1;
                if (left >= entries.Count)
                {
                    break;
                }

                int right = left + 1;
                int smallest = right < entries.Count && entries[right].IsBefore(entries[left]) ? right : left;
                if (!entries[smallest].IsBefore(entries[index]))
                {
                    break;
                }

                (entries[index], entries[smallest]) = (entries[smallest], entries[index]);
                index = smallest;
            }

            return top;
        }
    }
}
