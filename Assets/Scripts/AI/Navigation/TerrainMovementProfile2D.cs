using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "AI/Navigation/Terrain Movement Profile", fileName = "TerrainMovementProfile2D")]
public class TerrainMovementProfile2D : ScriptableObject
{
    [Serializable]
    public class TerrainRule
    {
        public TerrainType2D terrainType;
        public bool isWalkable = true;
        [Min(1)] public int traversalCost = 10;
    }

    [SerializeField] private bool defaultIsWalkable = true;
    [SerializeField, Min(1)] private int defaultTraversalCost = 10;
    [SerializeField] private List<TerrainRule> terrainRules = new();

    private Dictionary<TerrainType2D, TerrainRule> ruleLookup;
    private int version;

    // Incremented whenever walkability or costs change so cached navigation data can be invalidated.
    public int Version => version;
    public bool DefaultIsWalkable => defaultIsWalkable;
    public int DefaultTraversalCost => Mathf.Max(1, defaultTraversalCost);
    public IReadOnlyList<TerrainRule> TerrainRules => terrainRules;

    public void Configure(bool isWalkableByDefault, int traversalCostByDefault)
    {
        defaultIsWalkable = isWalkableByDefault;
        defaultTraversalCost = Mathf.Max(1, traversalCostByDefault);
        InvalidateRules();
    }

    public void SetTerrainRule(TerrainType2D terrainType, bool isWalkable, int traversalCost)
    {
        if (terrainType == null)
        {
            return;
        }

        TerrainRule rule = FindRule(terrainType);
        if (rule == null)
        {
            rule = new TerrainRule { terrainType = terrainType };
            terrainRules.Add(rule);
        }

        rule.isWalkable = isWalkable;
        rule.traversalCost = Mathf.Max(1, traversalCost);
        InvalidateRules();
    }

    public bool TryGetTraversal(TerrainType2D terrainType, out bool isWalkable, out int traversalCost)
    {
        if (terrainType != null && TryGetRule(terrainType, out TerrainRule rule))
        {
            isWalkable = rule.isWalkable;
            traversalCost = Mathf.Max(1, rule.traversalCost);
            return true;
        }

        isWalkable = defaultIsWalkable;
        traversalCost = Mathf.Max(1, defaultTraversalCost);
        return true;
    }

    public int GetMinimumTraversalCost()
    {
        int minimum = int.MaxValue;
        if (defaultIsWalkable)
        {
            minimum = Mathf.Min(minimum, Mathf.Max(1, defaultTraversalCost));
        }

        for (int i = 0; i < terrainRules.Count; i++)
        {
            TerrainRule rule = terrainRules[i];
            if (rule == null || !rule.isWalkable)
            {
                continue;
            }

            minimum = Mathf.Min(minimum, Mathf.Max(1, rule.traversalCost));
        }

        return minimum == int.MaxValue ? Mathf.Max(1, defaultTraversalCost) : minimum;
    }

    private TerrainRule FindRule(TerrainType2D terrainType)
    {
        for (int i = 0; i < terrainRules.Count; i++)
        {
            TerrainRule rule = terrainRules[i];
            if (rule != null && rule.terrainType == terrainType)
            {
                return rule;
            }
        }

        return null;
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
            if (rule == null || rule.terrainType == null)
            {
                continue;
            }

            ruleLookup[rule.terrainType] = rule;
        }
    }

    private void InvalidateRules()
    {
        ruleLookup = null;
        version++;
    }

    private void OnEnable()
    {
        ruleLookup = null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        defaultTraversalCost = Mathf.Max(1, defaultTraversalCost);
        for (int i = 0; i < terrainRules.Count; i++)
        {
            TerrainRule rule = terrainRules[i];
            if (rule == null)
            {
                continue;
            }

            rule.traversalCost = Mathf.Max(1, rule.traversalCost);
        }

        InvalidateRules();
    }
#endif
}
