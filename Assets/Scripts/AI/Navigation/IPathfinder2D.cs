using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Finds paths between cells of a navigation grid; implemented by <see cref="GridAStarPathfinder2D"/>.
/// </summary>
public interface IPathfinder2D
{
    /// <summary>
    /// Finds a path for <paramref name="request"/> into a newly allocated cell list.
    /// </summary>
    PathResult FindPath(PathRequest request);

    /// <summary>
    /// Finds a path for <paramref name="request"/>, reusing <paramref name="cellsBuffer"/> for its cells so nothing is
    /// allocated.
    /// </summary>
    PathResult FindPath(PathRequest request, List<Vector3Int> cellsBuffer);
}
