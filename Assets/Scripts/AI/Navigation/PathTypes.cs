using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct PathRequest
{
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

public readonly struct PathResult
{
    public static readonly PathResult Failure = new(false, false, false, false, Array.Empty<Vector3Int>());

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
