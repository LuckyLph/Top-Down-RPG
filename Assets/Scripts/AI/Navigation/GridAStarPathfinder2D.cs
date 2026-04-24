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

        if (!navigationGrid.TryGetNearestWalkableCell(request.StartCell, out Vector3Int start))
        {
            return PathResult.Failure;
        }

        Vector3Int goal = request.GoalCell;
        if (!navigationGrid.IsCellWalkable(goal))
        {
            if (!request.AllowPartial)
            {
                return PathResult.Failure;
            }

            if (!navigationGrid.TryGetNearestWalkableCell(goal, out goal))
            {
                return PathResult.Failure;
            }
        }

        List<Vector3Int> openList = new() { start };
        HashSet<Vector3Int> openSet = new() { start };
        HashSet<Vector3Int> closedSet = new();
        Dictionary<Vector3Int, Vector3Int> cameFrom = new();
        Dictionary<Vector3Int, int> gScore = new() { [start] = 0 };
        Dictionary<Vector3Int, int> fScore = new() { [start] = navigationGrid.HeuristicCost(start, goal) };

        Vector3Int closestToGoal = start;
        int closestHeuristic = navigationGrid.HeuristicCost(start, goal);

        while (openList.Count > 0)
        {
            Vector3Int current = GetNodeWithLowestF(openList, fScore);
            if (current == goal)
            {
                return new PathResult(true, false, ReconstructPath(cameFrom, current));
            }

            openList.Remove(current);
            openSet.Remove(current);
            closedSet.Add(current);

            int heuristic = navigationGrid.HeuristicCost(current, goal);
            if (heuristic < closestHeuristic)
            {
                closestHeuristic = heuristic;
                closestToGoal = current;
            }

            foreach (Vector3Int neighbor in navigationGrid.GetNeighbors8(current))
            {
                if (closedSet.Contains(neighbor))
                {
                    continue;
                }

                int tentativeG = gScore[current] + navigationGrid.MovementCost(current, neighbor);
                if (!gScore.TryGetValue(neighbor, out int neighborG) || tentativeG < neighborG)
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    fScore[neighbor] = tentativeG + navigationGrid.HeuristicCost(neighbor, goal);

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

        return new PathResult(true, true, ReconstructPath(cameFrom, closestToGoal));
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
