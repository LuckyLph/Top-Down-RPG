using System.Collections.Generic;
using UnityEngine;

public interface IPathfinder2D
{
    // Allocates a new cell list for the result.
    PathResult FindPath(PathRequest request);

    // Clears cellsBuffer and writes the path into it instead of allocating; the result's Cells is that
    // list, so it is only valid until the buffer is reused.
    PathResult FindPath(PathRequest request, List<Vector3Int> cellsBuffer);
}
