using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// A* over a <see cref="NavigationMap2D"/>'s cost field, with 8 neighbours, no corner cutting and no step across the
/// cliff of raised ground, stepping from one page into a loaded neighbouring page as within a page. Search state lives
/// in flat arrays indexed like the map's <see cref="NavigationPagePool"/>, shared by every map in it and stamped per
/// search, so nothing is cleared or allocated between searches.
/// </summary>
public class GridAStarPathfinder2D : IPathfinder2D
{
    private static readonly ProfilerMarker FindPathMarker = new("GridAStarPathfinder2D.FindPath");
    private static readonly int[] NeighborDx = { 1, -1, 0, 0, 1, 1, -1, -1 };
    private static readonly int[] NeighborDy = { 0, 0, 1, -1, 1, -1, 1, -1 };
    private static readonly byte[] HorizontalSide = BuildSides(true);
    private static readonly byte[] VerticalSide = BuildSides(false);

    private readonly NavigationMap2D map;
    private readonly int[] neighborOffsets = new int[8];

    /// <summary>
    /// Creates the pathfinder of <paramref name="navigationMap"/>, searching with its pool's shared arrays.
    /// </summary>
    internal GridAStarPathfinder2D(NavigationMap2D navigationMap)
    {
        map = navigationMap;
    }

    internal int LastExpandedCount { get; private set; }

    /// <summary>
    /// Finds a path for <paramref name="request"/> into a newly allocated cell list.
    /// </summary>
    public PathResult FindPath(PathRequest request)
    {
        return FindPath(request, new List<Vector3Int>());
    }

    /// <summary>
    /// Finds a path for <paramref name="request"/>, clearing <paramref name="cellsBuffer"/> and filling it with the
    /// cells from start to end; a successful result's cells are that buffer.
    /// </summary>
    public PathResult FindPath(PathRequest request, List<Vector3Int> cellsBuffer)
    {
        using (FindPathMarker.Auto())
        {
            cellsBuffer.Clear();
            return Search(request, cellsBuffer);
        }
    }

    /// <summary>
    /// The A* search. Strict requests fail when the goal is unwalkable or in another region. Partial ones move an
    /// unwalkable goal to the nearest walkable cell and, when the goal cannot be reached, return the path to the
    /// expanded cell closest to it after at most <see cref="NavigationMap2D.MaxSearchCells"/> expansions. On a world's
    /// map a partial request toward a goal where no page is loaded, with no walkable cell near it, heads for the goal
    /// the same way, so its path ends at the loaded edge.
    /// </summary>
    private PathResult Search(PathRequest request, List<Vector3Int> cells)
    {
        LastExpandedCount = 0;
        if (map == null || !map.IsBuilt)
        {
            return PathResult.Failure;
        }

        TerrainMovementProfile2D movementProfile = request.MovementProfile;
        if (!map.TryGetNearestWalkableCell(request.StartCell, movementProfile, out Vector3Int startCell))
        {
            return PathResult.Failure;
        }

        Vector3Int goalCell = request.GoalCell;
        bool adjustedGoalToNearestWalkable = false;
        bool goalIsMissing = false;
        if (!map.IsCellWalkable(goalCell, movementProfile))
        {
            if (!request.AllowPartial)
            {
                return PathResult.Failure;
            }

            if (map.TryGetNearestWalkableCell(goalCell, movementProfile, out Vector3Int nearestGoal))
            {
                goalCell = nearestGoal;
                adjustedGoalToNearestWalkable = true;
            }
            else if (map.SearchesTowardMissingPages && !map.HasPageAt(goalCell))
            {
                goalIsMissing = true;
            }
            else
            {
                return PathResult.Failure;
            }
        }

        bool goalIsReachable = !goalIsMissing && map.AreCellsConnected(startCell, goalCell, movementProfile);
        if (!goalIsReachable && !request.AllowPartial)
        {
            return PathResult.Failure;
        }

        NavigationPagePool pool = map.Pool;
        NavigationCostField field = map.GetCostField(movementProfile);
        map.TryGetIndex(startCell, out int start);
        int goal = -1;
        if (!goalIsMissing)
        {
            map.TryGetIndex(goalCell, out goal);
        }

        int goalX = goalCell.x;
        int goalY = goalCell.y;
        int[] costs = field.Costs;
        int[] diagonalCosts = field.DiagonalCosts;
        byte[] cliffSides = pool.CliffSides;
        NavigationPage[] slots = pool.Slots;
        int slotSize = pool.SlotSize;
        bool hasCliff = map.HasCliffSides;

        SearchState state = pool.Search;
        state.Begin(pool.CellCapacity);
        int[] costSoFar = state.CostSoFar;
        int[] estimatedTotal = state.EstimatedTotal;
        int[] closedStamps = state.ClosedStamps;
        int[] knownStamps = state.KnownStamps;
        int[] discoveryOrder = state.DiscoveryOrder;
        int stamp = state.Stamp;
        OpenHeap openHeap = state.Heap;

        int maxExpanded = goalIsReachable ? int.MaxValue : map.MaxSearchCells;
        int startHeuristic = Heuristic(startCell.x, startCell.y, goalX, goalY, field);
        state.Discover(start, 0, startHeuristic, start);
        openHeap.Push(new OpenEntry(start, startHeuristic, startHeuristic, discoveryOrder[start]));

        int closestToGoal = start;
        int closestHeuristic = startHeuristic;
        NavigationPage page = null;
        int pageSlot = -1;
        int width = 0;
        int height = 0;
        int pageBase = 0;
        int pageX = 0;
        int pageY = 0;

        while (openHeap.Count > 0 && LastExpandedCount < maxExpanded)
        {
            OpenEntry entry = openHeap.Pop();
            int current = entry.Index;
            if (closedStamps[current] == stamp || entry.F != estimatedTotal[current])
            {
                continue;
            }

            if (current == goal)
            {
                ReconstructPath(state, start, current, cells);
                return new PathResult(
                    success: true,
                    isPartial: adjustedGoalToNearestWalkable,
                    goalWasAdjusted: adjustedGoalToNearestWalkable,
                    reachedResolvedGoal: true,
                    cells: cells);
            }

            closedStamps[current] = stamp;
            LastExpandedCount++;

            if (entry.H < closestHeuristic)
            {
                closestHeuristic = entry.H;
                closestToGoal = current;
            }

            int slot = current / slotSize;
            if (slot != pageSlot)
            {
                pageSlot = slot;
                page = slots[slot];
                width = page.Width;
                height = page.Height;
                pageBase = page.Base;
                pageX = page.Rect.xMin;
                pageY = page.Rect.yMin;
                for (int i = 0; i < neighborOffsets.Length; i++)
                {
                    neighborOffsets[i] = (NeighborDy[i] * width) + NeighborDx[i];
                }
            }

            int local = current - pageBase;
            int x = local % width;
            int y = local / width;
            int cellX = pageX + x;
            int cellY = pageY + y;
            bool interior = x > 0 && y > 0 && x < width - 1 && y < height - 1;
            for (int i = 0; i < NeighborDx.Length; i++)
            {
                int dx = NeighborDx[i];
                int dy = NeighborDy[i];
                int neighbor = interior ? current + neighborOffsets[i] : page.IndexAt(x + dx, y + dy);
                if (neighbor < 0 || costs[neighbor] == 0)
                {
                    continue;
                }

                int stepCost = costs[neighbor];
                if (dx != 0 && dy != 0)
                {
                    int horizontal = interior ? current + dx : page.IndexAt(x + dx, y);
                    int vertical = interior ? current + (dy * width) : page.IndexAt(x, y + dy);
                    if (horizontal < 0 || vertical < 0 || costs[horizontal] == 0 || costs[vertical] == 0)
                    {
                        continue;
                    }

                    stepCost = diagonalCosts[neighbor];
                    if (hasCliff
                        && ((cliffSides[current] & (HorizontalSide[i] | VerticalSide[i])) != 0
                            || (cliffSides[horizontal] & VerticalSide[i]) != 0
                            || (cliffSides[vertical] & HorizontalSide[i]) != 0))
                    {
                        continue;
                    }
                }
                else if (hasCliff && (cliffSides[current] & (HorizontalSide[i] | VerticalSide[i])) != 0)
                {
                    continue;
                }

                bool isKnown = knownStamps[neighbor] == stamp;
                if (isKnown && closedStamps[neighbor] == stamp)
                {
                    continue;
                }

                int tentativeCost = costSoFar[current] + stepCost;
                if (isKnown && tentativeCost >= costSoFar[neighbor])
                {
                    continue;
                }

                int neighborHeuristic = Heuristic(cellX + dx, cellY + dy, goalX, goalY, field);
                state.Discover(neighbor, tentativeCost, neighborHeuristic, current);
                openHeap.Push(new OpenEntry(neighbor, estimatedTotal[neighbor], neighborHeuristic, discoveryOrder[neighbor]));
            }
        }

        if (!request.AllowPartial || closestToGoal == start)
        {
            return PathResult.Failure;
        }

        ReconstructPath(state, start, closestToGoal, cells);
        return new PathResult(
            success: true,
            isPartial: true,
            goalWasAdjusted: adjustedGoalToNearestWalkable,
            reachedResolvedGoal: false,
            cells: cells);
    }

    /// <summary>
    /// The side a step in neighbour direction <c>i</c> leaves its cell by, along x (<paramref name="horizontal"/>) or
    /// along y; 0 where the step does not move along that axis.
    /// </summary>
    private static byte[] BuildSides(bool horizontal)
    {
        byte[] sides = new byte[NeighborDx.Length];
        for (int i = 0; i < sides.Length; i++)
        {
            int along = horizontal ? NeighborDx[i] : NeighborDy[i];
            sides[i] = along == 0 ? (byte)0 : horizontal ? NavigationMap2D.SideToward(along, 0) : NavigationMap2D.SideToward(0, along);
        }

        return sides;
    }

    /// <summary>
    /// The octile distance from a cell to the goal at the field's cheapest straight and diagonal costs, so it never
    /// overestimates.
    /// </summary>
    private static int Heuristic(int x, int y, int goalX, int goalY, NavigationCostField field)
    {
        return NavigationMap2D.OctileCost(Mathf.Abs(x - goalX), Mathf.Abs(y - goalY), field.MinimumCost, field.MinimumDiagonalCost);
    }

    /// <summary>
    /// Follows the parents back from <paramref name="end"/> to <paramref name="start"/> and appends those cells to
    /// <paramref name="cells"/> in start-to-end order.
    /// </summary>
    private void ReconstructPath(SearchState state, int start, int end, List<Vector3Int> cells)
    {
        int current = end;
        cells.Add(map.GetCell(current));
        while (current != start)
        {
            current = state.Parents[current];
            cells.Add(map.GetCell(current));
        }

        cells.Reverse();
    }

    /// <summary>
    /// The arrays one search works in, sized to a pool and shared by every map in it: cost so far, estimated total,
    /// parent and discovery order per cell, the stamps marking cells known and closed in the current search, and the
    /// open heap.
    /// </summary>
    internal sealed class SearchState
    {
        public readonly OpenHeap Heap = new();
        public int[] CostSoFar = Array.Empty<int>();
        public int[] EstimatedTotal = Array.Empty<int>();
        public int[] Parents = Array.Empty<int>();
        public int[] DiscoveryOrder = Array.Empty<int>();
        public int[] KnownStamps = Array.Empty<int>();
        public int[] ClosedStamps = Array.Empty<int>();
        public int Stamp;
        private int knownCount;

        /// <summary>
        /// Starts a new search over <paramref name="cellCapacity"/> cells: grows the arrays when the pool grew, takes a
        /// new stamp (clearing the stamp arrays only when it would overflow) and empties the open heap.
        /// </summary>
        public void Begin(int cellCapacity)
        {
            if (CostSoFar.Length < cellCapacity)
            {
                Array.Resize(ref CostSoFar, cellCapacity);
                Array.Resize(ref EstimatedTotal, cellCapacity);
                Array.Resize(ref Parents, cellCapacity);
                Array.Resize(ref DiscoveryOrder, cellCapacity);
                Array.Resize(ref KnownStamps, cellCapacity);
                Array.Resize(ref ClosedStamps, cellCapacity);
            }

            if (Stamp == int.MaxValue)
            {
                Array.Clear(KnownStamps, 0, KnownStamps.Length);
                Array.Clear(ClosedStamps, 0, ClosedStamps.Length);
                Stamp = 0;
            }

            Stamp++;
            knownCount = 0;
            Heap.Clear();
        }

        /// <summary>
        /// Records the cell at <paramref name="index"/> as known in this search with its cost, estimated total and
        /// parent, giving it a discovery order the first time it is seen.
        /// </summary>
        public void Discover(int index, int cost, int heuristic, int parent)
        {
            if (KnownStamps[index] != Stamp)
            {
                KnownStamps[index] = Stamp;
                DiscoveryOrder[index] = knownCount++;
            }

            CostSoFar[index] = cost;
            EstimatedTotal[index] = cost + heuristic;
            Parents[index] = parent;
        }
    }

    /// <summary>
    /// An open-list entry: cell index, estimated total cost (F), heuristic (H) and discovery order. An entry whose F
    /// no longer matches its cell's is stale and skipped when popped.
    /// </summary>
    internal readonly struct OpenEntry
    {
        /// <summary>
        /// Captures a cell's index, scores and discovery order as they are when it is pushed.
        /// </summary>
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

        /// <summary>
        /// Heap order: lower F first, then lower H, then earlier discovery, so ties break deterministically.
        /// </summary>
        public bool IsBefore(OpenEntry other)
        {
            if (F != other.F)
            {
                return F < other.F;
            }

            return H != other.H ? H < other.H : Order < other.Order;
        }
    }

    /// <summary>
    /// Binary min-heap of <see cref="OpenEntry"/> values ordered by <see cref="OpenEntry.IsBefore"/>.
    /// </summary>
    internal sealed class OpenHeap
    {
        private readonly List<OpenEntry> entries = new();

        public int Count => entries.Count;

        /// <summary>
        /// Removes every entry, keeping the capacity.
        /// </summary>
        public void Clear()
        {
            entries.Clear();
        }

        /// <summary>
        /// Adds <paramref name="entry"/> and sifts it up to its place.
        /// </summary>
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

        /// <summary>
        /// Removes and returns the first entry in heap order; the heap must not be empty.
        /// </summary>
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
