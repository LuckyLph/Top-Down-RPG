using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A path query in grid cells: start, goal, whether a partial path is acceptable, and the movement profile whose
/// costs apply.
/// </summary>
public readonly struct PathRequest
{
    /// <summary>
    /// Creates a request from <paramref name="startCell"/> to <paramref name="goalCell"/>; a null
    /// <paramref name="movementProfile"/> treats every terrain as walkable at the default cost.
    /// </summary>
    public PathRequest(Vector3Int startCell, Vector3Int goalCell, bool allowPartial, TerrainMovementProfile2D movementProfile = null)
    {
        StartCell = startCell;
        GoalCell = goalCell;
        AllowPartial = allowPartial;
        MovementProfile = movementProfile;
    }

    public Vector3Int StartCell { get; }
    public Vector3Int GoalCell { get; }
    public bool AllowPartial { get; }
    public TerrainMovementProfile2D MovementProfile { get; }
}

/// <summary>
/// The outcome of a path query: whether it succeeded, whether the path is partial, whether its goal was moved to the
/// nearest walkable cell, whether it reaches that resolved goal, and its cells from start to end.
/// </summary>
public readonly struct PathResult
{
    public static readonly PathResult Failure = new(false, false, false, false, Array.Empty<Vector3Int>());

    /// <summary>
    /// Creates a result; a null <paramref name="cells"/> becomes an empty list.
    /// </summary>
    public PathResult(bool success, bool isPartial, bool goalWasAdjusted, bool reachedResolvedGoal, IReadOnlyList<Vector3Int> cells)
    {
        Success = success;
        IsPartial = isPartial;
        GoalWasAdjusted = goalWasAdjusted;
        ReachedResolvedGoal = reachedResolvedGoal;
        Cells = cells ?? Array.Empty<Vector3Int>();
    }

    public bool Success { get; }
    public bool IsPartial { get; }
    public bool GoalWasAdjusted { get; }
    public bool ReachedResolvedGoal { get; }
    public IReadOnlyList<Vector3Int> Cells { get; }
}
