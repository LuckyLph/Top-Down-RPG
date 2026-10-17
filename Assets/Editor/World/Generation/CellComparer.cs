using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Compares grid cells for the sets and dictionaries of cells scene generation and its tests build. It treats cells
/// as <see cref="Vector3Int"/>'s own equality does, but with a hash that spreads small coordinates: Unity's mixes
/// x with y shifted by four bits, so an area's few thousand cells share about a thousand hash codes and every lookup
/// walks a chain of several (Docs/SceneGeneration.md).
/// </summary>
public sealed class CellComparer : IEqualityComparer<Vector3Int>
{
    public static readonly CellComparer Instance = new();

    private CellComparer()
    {
    }

    /// <summary>
    /// Whether <paramref name="a"/> and <paramref name="b"/> are the same cell.
    /// </summary>
    public bool Equals(Vector3Int a, Vector3Int b)
    {
        return a.x == b.x && a.y == b.y && a.z == b.z;
    }

    /// <summary>
    /// A hash of <paramref name="cell"/> from its coordinates, each multiplied by its own large prime.
    /// </summary>
    public int GetHashCode(Vector3Int cell)
    {
        unchecked
        {
            return cell.x * 73856093 ^ cell.y * 19349663 ^ cell.z * 83492791;
        }
    }
}
