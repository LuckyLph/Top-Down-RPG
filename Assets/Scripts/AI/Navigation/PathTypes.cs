using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct PathRequest
{
    public PathRequest(Vector3Int startCell, Vector3Int goalCell, bool allowPartial)
    {
        StartCell = startCell;
        GoalCell = goalCell;
        AllowPartial = allowPartial;
    }

    public Vector3Int StartCell { get; }
    public Vector3Int GoalCell { get; }
    public bool AllowPartial { get; }
}

public readonly struct PathResult
{
    public static readonly PathResult Failure = new(false, false, Array.Empty<Vector3Int>());

    public PathResult(bool success, bool isPartial, IReadOnlyList<Vector3Int> cells)
    {
        Success = success;
        IsPartial = isPartial;
        Cells = cells ?? Array.Empty<Vector3Int>();
    }

    public bool Success { get; }
    public bool IsPartial { get; }
    public IReadOnlyList<Vector3Int> Cells { get; }
}
