using UnityEngine;
using static UnityEngine.RuleTile;

public static class TilingRuleExtensions
{
    /// <summary>
    /// Calculates the relative neighbor offset from the <paramref name="dataTileOffset"/> and returns the correct index of the neighbor.
    /// </summary>
    public static int GetNeighborIndex(this TilingRule rule, Vector3Int dataTileOffset)
    {
        Vector3Int neighborOffsetPosition = DualGridUtils.ConvertDataTileOffsetToNeighborOffset(dataTileOffset);
        return rule.m_NeighborPositions.IndexOf(neighborOffsetPosition);
    }
}
