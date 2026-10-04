using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A mob's movement rules per terrain type: whether it can walk there and at what cost, with defaults for terrain it
/// has no rule for. Read-only at runtime; tests build their own with <see cref="Create"/>. <see cref="Version"/> changes
/// when the asset is edited, so region caches refresh.
/// </summary>
[CreateAssetMenu(menuName = "AI/Navigation/Terrain Movement Profile", fileName = "TerrainMovementProfile2D")]
public class TerrainMovementProfile2D : ScriptableObject
{
    /// <summary>Whether one terrain type is walkable, and its traversal cost.</summary>
    [Serializable]
    public class TerrainRule
    {
        [SerializeField] private TerrainType2D terrainType;
        [SerializeField] private bool isWalkable = true;
        [SerializeField, Min(1)] private int traversalCost = 10;

        public TerrainRule()
        {
        }

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

    private bool TryGetRule(TerrainType2D terrainType, out TerrainRule rule)
    {
        EnsureLookup();
        return ruleLookup.TryGetValue(terrainType, out rule);
    }

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

    private void OnEnable()
    {
        ruleLookup = null;
        minimumTraversalCost = -1;
    }

#if UNITY_EDITOR
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
