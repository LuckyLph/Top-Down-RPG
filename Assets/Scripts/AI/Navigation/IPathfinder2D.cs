using System.Collections.Generic;
using UnityEngine;

public interface IPathfinder2D
{
    PathResult FindPath(PathRequest request);

    PathResult FindPath(PathRequest request, List<Vector3Int> cellsBuffer);
}
