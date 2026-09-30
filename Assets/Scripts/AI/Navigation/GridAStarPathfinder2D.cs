using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

public class GridAStarPathfinder2D : IPathfinder2D
{
    private static readonly ProfilerMarker FindPathMarker = new("GridAStarPathfinder2D.FindPath");

    private readonly NavigationGrid2D navigationGrid;

    // Search state reused across calls so a warmed-up search allocates nothing. Searches run one at a
    // time on the main thread, so sharing it is safe.
    private readonly OpenHeap openHeap = new();
    private readonly Dictionary<Vector3Int, SearchNode> nodes = new();
    private readonly Vector3Int[] neighborBuffer = new Vector3Int[8];

    public GridAStarPathfinder2D(NavigationGrid2D navGrid)
    {
        navigationGrid = navGrid;
    }

    // Cells closed by the most recent search; lets tests and benchmarks compare search effort.
    internal int LastExpandedCount { get; private set; }

    public PathResult FindPath(PathRequest request)
    {
        return FindPath(request, new List<Vector3Int>());
    }

    public PathResult FindPath(PathRequest request, List<Vector3Int> cellsBuffer)
    {
        using (FindPathMarker.Auto())
        {
            cellsBuffer.Clear();
            return Search(request, cellsBuffer);
        }
    }

    private PathResult Search(PathRequest request, List<Vector3Int> cells)
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
        bool goalIsReachable = navigationGrid.AreCellsConnected(start, goal, movementProfile);
        if (!goalIsReachable && !request.AllowPartial)
        {
            return PathResult.Failure;
        }

        // A reachable goal always ends the search, so only the partial search toward an unreachable goal
        // (which would otherwise flood the start region looking for the closest cell) is capped.
        int maxExpanded = goalIsReachable ? int.MaxValue : navigationGrid.MaxSearchCells;
        openHeap.Clear();
        nodes.Clear();

        int startHeuristic = navigationGrid.HeuristicCost(start, goal, movementProfile);
        nodes[start] = new SearchNode(0, startHeuristic, start, 0);
        openHeap.Push(new OpenEntry(start, startHeuristic, startHeuristic, 0));

        Vector3Int closestToGoal = start;
        int closestHeuristic = startHeuristic;

        while (openHeap.Count > 0 && LastExpandedCount < maxExpanded)
        {
            OpenEntry entry = openHeap.Pop();
            Vector3Int current = entry.Cell;
            SearchNode currentNode = nodes[current];
            if (currentNode.Closed || entry.F != currentNode.F)
            {
                // Stale entry left behind by a later, cheaper push of the same cell.
                continue;
            }

            if (current == goal)
            {
                ReconstructPath(start, current, cells);
                return new PathResult(
                    success: true,
                    isPartial: adjustedGoalToNearestWalkable,
                    goalWasAdjusted: adjustedGoalToNearestWalkable,
                    reachedResolvedGoal: true,
                    cells: cells);
            }

            currentNode.Closed = true;
            nodes[current] = currentNode;
            LastExpandedCount++;

            if (entry.H < closestHeuristic)
            {
                closestHeuristic = entry.H;
                closestToGoal = current;
            }

            int neighborCount = navigationGrid.GetNeighbors8(current, movementProfile, neighborBuffer);
            for (int i = 0; i < neighborCount; i++)
            {
                Vector3Int neighbor = neighborBuffer[i];
                bool isKnown = nodes.TryGetValue(neighbor, out SearchNode neighborNode);
                if (isKnown && neighborNode.Closed)
                {
                    continue;
                }

                int tentativeG = currentNode.G + navigationGrid.MovementCost(current, neighbor, movementProfile);
                if (isKnown && tentativeG >= neighborNode.G)
                {
                    continue;
                }

                int neighborHeuristic = navigationGrid.HeuristicCost(neighbor, goal, movementProfile);
                int order = isKnown ? neighborNode.Order : nodes.Count;
                int f = tentativeG + neighborHeuristic;
                nodes[neighbor] = new SearchNode(tentativeG, f, current, order);
                openHeap.Push(new OpenEntry(neighbor, f, neighborHeuristic, order));
            }
        }

        if (!request.AllowPartial || closestToGoal == start)
        {
            return PathResult.Failure;
        }

        ReconstructPath(start, closestToGoal, cells);
        return new PathResult(
            success: true,
            isPartial: true,
            goalWasAdjusted: adjustedGoalToNearestWalkable,
            reachedResolvedGoal: false,
            cells: cells);
    }

    private void ReconstructPath(Vector3Int start, Vector3Int end, List<Vector3Int> cells)
    {
        Vector3Int current = end;
        cells.Add(current);
        while (current != start)
        {
            current = nodes[current].Parent;
            cells.Add(current);
        }

        cells.Reverse();
    }

    // One record per discovered cell: replaces separate g/f/parent/order maps and the closed set.
    private struct SearchNode
    {
        public SearchNode(int g, int f, Vector3Int parent, int order)
        {
            G = g;
            F = f;
            Parent = parent;
            Order = order;
            Closed = false;
        }

        public int G;
        public int F;
        public Vector3Int Parent;
        public int Order;
        public bool Closed;
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

        public void Clear()
        {
            entries.Clear();
        }

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
