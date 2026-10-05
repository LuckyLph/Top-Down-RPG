using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static DualGridRuleTile;

/// <summary>
/// Neighbor rule presets for dual-grid rule tiles whose texture was sliced into the standard 16-tile arrangement.
/// </summary>
public static class AutoDualGridRuleTileProvider
{
    private static readonly Vector3Int UpRightNeighbor = Vector3Int.up + Vector3Int.right;
    private static readonly Vector3Int UpLeftNeighbor = Vector3Int.up + Vector3Int.left;
    private static readonly Vector3Int DownRightNeighbor = Vector3Int.down + Vector3Int.right;
    private static readonly Vector3Int DownLeftNeighbor = Vector3Int.down + Vector3Int.left;

    /// <summary>
    /// One diagonal neighbor of a tiling rule and the filled state the rule requires there.
    /// </summary>
    private readonly struct NeighborPattern
    {
        public Vector3Int Position { get; }
        public int State { get; }

        /// <summary>
        /// Creates a pattern that requires <paramref name="state"/> at the neighbor offset <paramref name="position"/>.
        /// </summary>
        public NeighborPattern(Vector3Int position, int state)
        {
            Position = position;
            State = state;
        }
    }

    /// <summary>
    /// Builds the four diagonal neighbor patterns of one tiling rule from the state required at each corner.
    /// </summary>
    private static List<NeighborPattern> CreatePattern(int upLeft, int upRight, int downLeft, int downRight)
    {
        return new List<NeighborPattern>
        {
            new NeighborPattern(UpLeftNeighbor, upLeft),
            new NeighborPattern(UpRightNeighbor, upRight),
            new NeighborPattern(DownLeftNeighbor, downLeft),
            new NeighborPattern(DownRightNeighbor, downRight)
        };
    }

    private static readonly Dictionary<int, List<NeighborPattern>> NeighborConfigurationsByIndex = new()
    {
        { 0, CreatePattern(DualGridNeighbor.NotFilled, DualGridNeighbor.NotFilled, DualGridNeighbor.Filled, DualGridNeighbor.NotFilled) },
        { 1, CreatePattern(DualGridNeighbor.NotFilled, DualGridNeighbor.Filled, DualGridNeighbor.NotFilled, DualGridNeighbor.Filled) },
        { 2, CreatePattern(DualGridNeighbor.Filled, DualGridNeighbor.NotFilled, DualGridNeighbor.Filled, DualGridNeighbor.Filled) },
        { 3, CreatePattern(DualGridNeighbor.NotFilled, DualGridNeighbor.NotFilled, DualGridNeighbor.Filled, DualGridNeighbor.Filled) },
        { 4, CreatePattern(DualGridNeighbor.Filled, DualGridNeighbor.NotFilled, DualGridNeighbor.NotFilled, DualGridNeighbor.Filled) },
        { 5, CreatePattern(DualGridNeighbor.NotFilled, DualGridNeighbor.Filled, DualGridNeighbor.Filled, DualGridNeighbor.Filled) },
        { 6, CreatePattern(DualGridNeighbor.Filled, DualGridNeighbor.Filled, DualGridNeighbor.Filled, DualGridNeighbor.Filled) },
        { 7, CreatePattern(DualGridNeighbor.Filled, DualGridNeighbor.Filled, DualGridNeighbor.Filled, DualGridNeighbor.NotFilled) },
        { 8, CreatePattern(DualGridNeighbor.NotFilled, DualGridNeighbor.Filled, DualGridNeighbor.NotFilled, DualGridNeighbor.NotFilled) },
        { 9, CreatePattern(DualGridNeighbor.Filled, DualGridNeighbor.Filled, DualGridNeighbor.NotFilled, DualGridNeighbor.NotFilled) },
        { 10, CreatePattern(DualGridNeighbor.Filled, DualGridNeighbor.Filled, DualGridNeighbor.NotFilled, DualGridNeighbor.Filled) },
        { 11, CreatePattern(DualGridNeighbor.Filled, DualGridNeighbor.NotFilled, DualGridNeighbor.Filled, DualGridNeighbor.NotFilled) },
        { 12, CreatePattern(DualGridNeighbor.NotFilled, DualGridNeighbor.NotFilled, DualGridNeighbor.NotFilled, DualGridNeighbor.NotFilled) },
        { 13, CreatePattern(DualGridNeighbor.NotFilled, DualGridNeighbor.NotFilled, DualGridNeighbor.NotFilled, DualGridNeighbor.Filled) },
        { 14, CreatePattern(DualGridNeighbor.NotFilled, DualGridNeighbor.Filled, DualGridNeighbor.Filled, DualGridNeighbor.NotFilled) },
        { 15, CreatePattern(DualGridNeighbor.Filled, DualGridNeighbor.NotFilled, DualGridNeighbor.NotFilled, DualGridNeighbor.NotFilled) },
    };

    /// <summary>
    /// Applies the standard expected configuration into the <see langword="ref"/> <paramref name="dualGridRuleTile"/> for <see cref="Texture2D"/> assets 
    /// that have been automatically sliced by the standard 16x tiles arrangement.
    /// </summary>
    public static void ApplyConfigurationPreset(ref DualGridRuleTile dualGridRuleTile)
    {
        if (dualGridRuleTile.m_TilingRules.Count != 16)
        {
            Debug.LogWarning($"Could not apply configuration preset to {dualGridRuleTile.name} because the rule tile does not have exactly 16 sprites included.");
            return;
        }
        for (int i = 0; i < dualGridRuleTile.m_TilingRules.Count; i++)
        {
            var tilingRule = dualGridRuleTile.m_TilingRules[i];

            tilingRule.m_NeighborPositions = NeighborConfigurationsByIndex[i].Select(neightborPattern => neightborPattern.Position).ToList();
            tilingRule.m_Neighbors = NeighborConfigurationsByIndex[i].Select(neightborPattern => neightborPattern.State).ToList();
        }
    }
}
