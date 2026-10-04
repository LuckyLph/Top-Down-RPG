using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// A* over a <see cref="NavigationGrid2D"/>'s cost field, with 8 neighbours and no corner cutting. Search state lives
/// in flat arrays indexed like the grid and stamped per search, so nothing is cleared or allocated between searches.
/// </summary>
public class GridAStarPathfinder2D : IPathfinder2D
{
    private static readonly ProfilerMarker FindPathMarker = new("GridAStarPathfinder2D.FindPath");
    private static readonly int[] NeighborDx = { 1, -1, 0, 0, 1, 1, -1, -1 };
    private static readonly int[] NeighborDy = { 0, 0, 1, -1, 1, -1, 1, -1 };

    private readonly NavigationGrid2D navigationGrid;
    private readonly OpenHeap openHeap = new();
    private readonly int[] costSoFar;
    private readonly int[] estimatedTotal;
    private readonly int[] parents;
    private readonly int[] discoveryOrder;
    private readonly int[] knownStamps;
    private readonly int[] closedStamps;
    private int searchStamp;
    private int knownCount;

    public GridAStarPathfinder2D(NavigationGrid2D navGrid)
    {
        navigationGrid = navGrid;
        int cellCount = navGrid != null ? navGrid.CellCount : 0;
        costSoFar = new int[cellCount];
        estimatedTotal = new int[cellCount];
        parents = new int[cellCount];
        discoveryOrder = new int[cellCount];
        knownStamps = new int[cellCount];
        closedStamps = new int[cellCount];
    }

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

        if (!navigationGrid.TryGetNearestWalkableCell(request.StartCell, movementProfile, out Vector3Int startCell))
        {
            return PathResult.Failure;
        }

        Vector3Int goalCell = request.GoalCell;
        bool adjustedGoalToNearestWalkable = false;
        if (!navigationGrid.IsCellWalkable(goalCell, movementProfile))
        {
            if (!request.AllowPartial)
            {
                return PathResult.Failure;
            }

            if (!navigationGrid.TryGetNearestWalkableCell(goalCell, movementProfile, out goalCell))
            {
                return PathResult.Failure;
            }

            adjustedGoalToNearestWalkable = true;
        }

        bool goalIsReachable = navigationGrid.AreCellsConnected(startCell, goalCell, movementProfile);
        if (!goalIsReachable && !request.AllowPartial)
        {
            return PathResult.Failure;
        }

        NavigationGrid2D.CostField field = navigationGrid.GetCostField(movementProfile);
        navigationGrid.TryGetIndex(startCell, out int start);
        navigationGrid.TryGetIndex(goalCell, out int goal);
        int width = navigationGrid.Width;
        int height = navigationGrid.Height;
        int goalX = goal % width;
        int goalY = (goal / width) % height;
        int[] costs = field.Costs;
        int[] diagonalCosts = field.DiagonalCosts;

        int maxExpanded = goalIsReachable ? int.MaxValue : navigationGrid.MaxSearchCells;
        BeginSearch();

        int startHeuristic = Heuristic(start % width, (start / width) % height, goalX, goalY, field);
        Discover(start, 0, startHeuristic, start);
        openHeap.Push(new OpenEntry(start, startHeuristic, startHeuristic, discoveryOrder[start]));

        int closestToGoal = start;
        int closestHeuristic = startHeuristic;

        while (openHeap.Count > 0 && LastExpandedCount < maxExpanded)
        {
            OpenEntry entry = openHeap.Pop();
            int current = entry.Index;
            if (closedStamps[current] == searchStamp || entry.F != estimatedTotal[current])
            {
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

            closedStamps[current] = searchStamp;
            LastExpandedCount++;

            if (entry.H < closestHeuristic)
            {
                closestHeuristic = entry.H;
                closestToGoal = current;
            }

            int x = current % width;
            int y = (current / width) % height;
            int layerStart = current - ((y * width) + x);
            for (int i = 0; i < NeighborDx.Length; i++)
            {
                int neighborX = x + NeighborDx[i];
                int neighborY = y + NeighborDy[i];
                if (neighborX < 0 || neighborY < 0 || neighborX >= width || neighborY >= height)
                {
                    continue;
                }

                int neighbor = layerStart + (neighborY * width) + neighborX;
                if (costs[neighbor] == 0)
                {
                    continue;
                }

                int stepCost = costs[neighbor];
                if (NeighborDx[i] != 0 && NeighborDy[i] != 0)
                {
                    if (costs[layerStart + (y * width) + neighborX] == 0 || costs[layerStart + (neighborY * width) + x] == 0)
                    {
                        continue;
                    }

                    stepCost = diagonalCosts[neighbor];
                }

                bool isKnown = knownStamps[neighbor] == searchStamp;
                if (isKnown && closedStamps[neighbor] == searchStamp)
                {
                    continue;
                }

                int tentativeCost = costSoFar[current] + stepCost;
                if (isKnown && tentativeCost >= costSoFar[neighbor])
                {
                    continue;
                }

                int neighborHeuristic = Heuristic(neighborX, neighborY, goalX, goalY, field);
                Discover(neighbor, tentativeCost, neighborHeuristic, current);
                openHeap.Push(new OpenEntry(neighbor, estimatedTotal[neighbor], neighborHeuristic, discoveryOrder[neighbor]));
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

    private void BeginSearch()
    {
        if (searchStamp == int.MaxValue)
        {
            Array.Clear(knownStamps, 0, knownStamps.Length);
            Array.Clear(closedStamps, 0, closedStamps.Length);
            searchStamp = 0;
        }

        searchStamp++;
        knownCount = 0;
        openHeap.Clear();
    }

    private void Discover(int index, int cost, int heuristic, int parent)
    {
        if (knownStamps[index] != searchStamp)
        {
            knownStamps[index] = searchStamp;
            discoveryOrder[index] = knownCount++;
        }

        costSoFar[index] = cost;
        estimatedTotal[index] = cost + heuristic;
        parents[index] = parent;
    }

    private static int Heuristic(int x, int y, int goalX, int goalY, NavigationGrid2D.CostField field)
    {
        return NavigationGrid2D.OctileCost(Mathf.Abs(x - goalX), Mathf.Abs(y - goalY), field.MinimumCost, field.MinimumDiagonalCost);
    }

    private void ReconstructPath(int start, int end, List<Vector3Int> cells)
    {
        int current = end;
        cells.Add(navigationGrid.GetCell(current));
        while (current != start)
        {
            current = parents[current];
            cells.Add(navigationGrid.GetCell(current));
        }

        cells.Reverse();
    }

    private readonly struct OpenEntry
    {
        public OpenEntry(int index, int f, int h, int order)
        {
            Index = index;
            F = f;
            H = h;
            Order = order;
        }

        public int Index { get; }
        public int F { get; }
        public int H { get; }
        public int Order { get; }
        public bool IsBefore(OpenEntry other)
        {
            if (F != other.F)
            {
                return F < other.F;
            }

            return H != other.H ? H < other.H : Order < other.Order;
        }
    }

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
