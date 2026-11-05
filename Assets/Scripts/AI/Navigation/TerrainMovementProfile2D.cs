using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A mob's movement rules per terrain type: whether it can walk there and at what cost, with defaults for terrain it
/// has no rule for. Read-only at runtime; tests build their own with <see cref="Create"/>. <see cref="Version"/> changes
/// when the asset is edited, so region caches refresh.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/AI/Navigation/Terrain Movement Profile", fileName = "TerrainMovementProfile2D")]
public class TerrainMovementProfile2D : ScriptableObject
{
    /// <summary>Whether one terrain type is walkable, and its traversal cost.</summary>
    [Serializable]
    public class TerrainRule
    {
        [SerializeField] private TerrainType2D terrainType;
        [SerializeField] private bool isWalkable = true;
        [SerializeField, Min(1)] private int traversalCost = 10;

        /// <summary>
        /// Parameterless constructor for serialization: a walkable rule with no terrain type at cost 10.
        /// </summary>
        public TerrainRule()
        {
        }

        /// <summary>
        /// Creates a rule for <paramref name="terrainType"/>, raising <paramref name="traversalCost"/> to at least 1.
        /// </summary>
        public TerrainRule(TerrainType2D terrainType, bool isWalkable, int traversalCost)
        {
            this.terrainType = terrainType;
            this.isWalkable = isWalkable;
            this.traversalCost = Mathf.Max(1, traversalCost);
        }

        public TerrainType2D TerrainType => terrainType;
        public bool IsWalkable => isWalkable;
        public int TraversalCost => Mathf.Max(1, traversalCost);
    }

    [SerializeField] private bool defaultIsWalkable = true;
    [SerializeField, Min(1)] private int defaultTraversalCost = 10;
    [SerializeField] private List<TerrainRule> terrainRules = new();
    [Tooltip("Extra cost for entering a walkable cell that touches (8 neighbours) a cell this profile cannot walk, " +
        "so paths keep off ground edges and wall corners. 0 turns it off.")]
    [SerializeField, Min(0)] private int edgeCost;

    private Dictionary<TerrainType2D, TerrainRule> ruleLookup;
    private int minimumTraversalCost = -1;
    private int version;
    public int Version => version;
    public bool DefaultIsWalkable => defaultIsWalkable;
    public int DefaultTraversalCost => Mathf.Max(1, defaultTraversalCost);
    public int EdgeCost => Mathf.Max(0, edgeCost);

    /// <summary>
    /// Whether <paramref name="terrainType"/> is walkable and at what cost: its rule's values, or the profile defaults
    /// when it has no rule or is null. Always returns true.
    /// </summary>
    public bool TryGetTraversal(TerrainType2D terrainType, out bool isWalkable, out int traversalCost)
    {
        if (terrainType != null && TryGetRule(terrainType, out TerrainRule rule))
        {
            isWalkable = rule.IsWalkable;
            traversalCost = rule.TraversalCost;
            return true;
        }

        isWalkable = defaultIsWalkable;
        traversalCost = Mathf.Max(1, defaultTraversalCost);
        return true;
    }

    /// <summary>
    /// The cheapest cost of any walkable terrain under this profile, cached until the asset is edited. The pathfinding
    /// heuristic uses it as its step cost.
    /// </summary>
    public int GetMinimumTraversalCost()
    {
        if (minimumTraversalCost < 0)
        {
            minimumTraversalCost = CalculateMinimumTraversalCost();
        }

        return minimumTraversalCost;
    }

    /// <summary>
    /// Builds a profile in code, for tests. A later rule for the same terrain type wins.
    /// </summary>
    public static TerrainMovementProfile2D Create(bool isWalkableByDefault, int traversalCostByDefault, params TerrainRule[] rules)
    {
        return Create(isWalkableByDefault, traversalCostByDefault, 0, rules);
    }

    /// <summary>
    /// Builds a profile with an <see cref="EdgeCost"/> in code, for tests. A later rule for the same terrain type wins.
    /// </summary>
    public static TerrainMovementProfile2D Create(bool isWalkableByDefault, int traversalCostByDefault, int edgeCost, params TerrainRule[] rules)
    {
        TerrainMovementProfile2D profile = CreateInstance<TerrainMovementProfile2D>();
        profile.defaultIsWalkable = isWalkableByDefault;
        profile.defaultTraversalCost = Mathf.Max(1, traversalCostByDefault);
        profile.edgeCost = Mathf.Max(0, edgeCost);
        if (rules != null)
        {
            profile.terrainRules.AddRange(rules);
        }

        return profile;
    }

    /// <summary>
    /// The lowest cost among the default (when walkable) and the walkable rules, or the default cost when nothing is
    /// walkable.
    /// </summary>
    private int CalculateMinimumTraversalCost()
    {
        int minimum = int.MaxValue;
        if (defaultIsWalkable)
        {
            minimum = Mathf.Min(minimum, Mathf.Max(1, defaultTraversalCost));
        }

        for (int i = 0; i < terrainRules.Count; i++)
        {
            TerrainRule rule = terrainRules[i];
            if (rule == null || !rule.IsWalkable)
            {
                continue;
            }

            minimum = Mathf.Min(minimum, rule.TraversalCost);
        }

        return minimum == int.MaxValue ? Mathf.Max(1, defaultTraversalCost) : minimum;
    }

    /// <summary>
    /// Finds the rule for <paramref name="terrainType"/>, building the lookup on first use.
    /// </summary>
    private bool TryGetRule(TerrainType2D terrainType, out TerrainRule rule)
    {
        EnsureLookup();
        return ruleLookup.TryGetValue(terrainType, out rule);
    }

    /// <summary>
    /// Builds the terrain-to-rule lookup from the rules list when missing, skipping empty rules; a later rule for the
    /// same terrain type wins.
    /// </summary>
    private void EnsureLookup()
    {
        if (ruleLookup != null)
        {
            return;
        }

        ruleLookup = new Dictionary<TerrainType2D, TerrainRule>();
        for (int i = 0; i < terrainRules.Count; i++)
        {
            TerrainRule rule = terrainRules[i];
            if (rule == null || rule.TerrainType == null)
            {
                continue;
            }

            ruleLookup[rule.TerrainType] = rule;
        }
    }

    /// <summary>
    /// Drops the cached lookup and minimum cost so they are rebuilt from the serialized data.
    /// </summary>
    private void OnEnable()
    {
        ruleLookup = null;
        minimumTraversalCost = -1;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Clamps the inspector values, drops the caches and bumps <see cref="Version"/> so grids rebake their cost fields.
    /// </summary>
    private void OnValidate()
    {
        defaultTraversalCost = Mathf.Max(1, defaultTraversalCost);
        edgeCost = Mathf.Max(0, edgeCost);
        ruleLookup = null;
        minimumTraversalCost = -1;
        version++;
    }
#endif
}
