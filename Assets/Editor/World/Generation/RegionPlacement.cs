using System;
using UnityEngine;

/// <summary>
/// One region of a world (Docs/WorldGeneration.md: Terms): an area generator's layout from a seed, laid around the
/// origin as in a scratch scene and moved cell for cell to the world-local cell its origin goes to, named in the world
/// by its key.
/// </summary>
[Serializable]
public sealed class RegionPlacement
{
    [Tooltip("The region's name in the world: its waystone's name, the start of its gates' names and the scope of its mobs' ids. Keep it once the world is written, or its mobs get new ids.")]
    [SerializeField] private string key = "region";
    [Tooltip("The area generator that lays the region.")]
    [SerializeField] private AreaGenerator generator;
    [Tooltip("Lay the region from its generator asset's own seed rather than the seed below.")]
    [SerializeField] private bool useGeneratorSeed = true;
    [Tooltip("The seed the region is laid from when it does not use its generator's.")]
    [SerializeField] private int seed = 1;
    [Tooltip("The world-local cell the layout's origin goes to.")]
    [SerializeField] private Vector2Int cell;

    /// <summary>
    /// Creates an empty placement, for Unity serialization and the inspector.
    /// </summary>
    public RegionPlacement()
    {
    }

    /// <summary>
    /// Creates the placement <paramref name="regionKey"/> of <paramref name="regionGenerator"/>'s layout at
    /// <paramref name="originCell"/>, from the generator's own seed, for tests and tools.
    /// </summary>
    public RegionPlacement(string regionKey, AreaGenerator regionGenerator, Vector2Int originCell)
    {
        key = regionKey;
        generator = regionGenerator;
        cell = originCell;
    }

    /// <summary>
    /// Creates the placement <paramref name="regionKey"/> of <paramref name="regionGenerator"/>'s layout from
    /// <paramref name="layoutSeed"/> at <paramref name="originCell"/>, for tests and tools.
    /// </summary>
    public RegionPlacement(string regionKey, AreaGenerator regionGenerator, int layoutSeed, Vector2Int originCell)
    {
        key = regionKey;
        generator = regionGenerator;
        useGeneratorSeed = false;
        seed = layoutSeed;
        cell = originCell;
    }

    public string Key => key;
    public AreaGenerator Generator => generator;
    public Vector2Int Cell => cell;

    /// <summary>
    /// The seed the region is laid from: its generator asset's own, or the placement's.
    /// </summary>
    public int Seed => useGeneratorSeed && generator != null ? generator.Seed : seed;
}
