using System.Collections.Generic;
using UnityEngine;

public class GridAStarPathfinder2D : IPathfinder2D
{
    private readonly NavigationGrid2D navigationGrid;

    public GridAStarPathfinder2D(NavigationGrid2D navGrid)
    {
        navigationGrid = navGrid;
    }

    public PathResult FindPath(PathRequest request)
    {
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

        List<Vector3Int> openList = new() { start };
        HashSet<Vector3Int> openSet = new() { start };
        HashSet<Vector3Int> closedSet = new();
        Dictionary<Vector3Int, Vector3Int> cameFrom = new();
        Dictionary<Vector3Int, int> gScore = new() { [start] = 0 };
        Dictionary<Vector3Int, int> fScore = new() { [start] = navigationGrid.HeuristicCost(start, goal, movementProfile) };

        Vector3Int closestToGoal = start;
        int closestHeuristic = navigationGrid.HeuristicCost(start, goal, movementProfile);

        while (openList.Count > 0)
        {
            Vector3Int current = GetNodeWithLowestF(openList, fScore);
            if (current == goal)
            {
                return new PathResult(
                    success: true,
                    isPartial: adjustedGoalToNearestWalkable,
                    goalWasAdjusted: adjustedGoalToNearestWalkable,
                    reachedResolvedGoal: true,
                    cells: ReconstructPath(cameFrom, current));
            }

            openList.Remove(current);
            openSet.Remove(current);
            closedSet.Add(current);

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
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    fScore[neighbor] = tentativeG + navigationGrid.HeuristicCost(neighbor, goal, movementProfile);

                    if (!openSet.Contains(neighbor))
                    {
                        openSet.Add(neighbor);
                        openList.Add(neighbor);
                    }
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

    private static Vector3Int GetNodeWithLowestF(List<Vector3Int> openList, Dictionary<Vector3Int, int> fScore)
    {
        Vector3Int bestNode = openList[0];
        int bestScore = fScore.TryGetValue(bestNode, out int score) ? score : int.MaxValue;

        for (int i = 1; i < openList.Count; i++)
        {
            Vector3Int candidate = openList[i];
            int candidateScore = fScore.TryGetValue(candidate, out int value) ? value : int.MaxValue;
            if (candidateScore < bestScore)
            {
                bestScore = candidateScore;
                bestNode = candidate;
            }
        }

        return bestNode;
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
}
