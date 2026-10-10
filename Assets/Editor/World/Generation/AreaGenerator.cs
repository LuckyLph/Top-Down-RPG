using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An editor-only recipe for one kind of environment. <see cref="Generate"/> lays out an area's terrain, props, mob
/// spawns and party spawn from a seed, the same seed always giving the same <see cref="AreaLayout"/>;
/// <see cref="AreaLayoutWriter"/> writes the layout into an area scene and <see cref="SceneGeneratorWindow"/> drives
/// both. The scene then holds the result like any hand-made area, so every machine loads the same content.
/// </summary>
public abstract class AreaGenerator : ScriptableObject
{
    /// <summary>
    /// One dual-grid terrain layer a generator paints, and how <see cref="AreaLayoutWriter"/> sets up its tilemap.
    /// </summary>
    [Serializable]
    public class TerrainLayer
    {
        [Tooltip("Name of the data tilemap under the area's Grid. The writer creates it when the scene has none.")]
        [SerializeField] private string tilemapName = "GroundTilemap";
        [SerializeField] private DualGridRuleTile ruleTile;
        [Tooltip("Terrain type the layer's navigation source gives its cells.")]
        [SerializeField] private TerrainType2D terrainType;
        [Tooltip("Where terrain layers overlap, the highest priority decides walkability and cost.")]
        [SerializeField] private int navigationPriority;
        [Tooltip("Order of the render tilemap on the Terrain sorting layer. A layer drawn over others goes higher.")]
        [SerializeField] private int sortingOrder;

        public string TilemapName => tilemapName;
        public DualGridRuleTile RuleTile => ruleTile;
        public TerrainType2D TerrainType => terrainType;
        public int NavigationPriority => navigationPriority;
        public int SortingOrder => sortingOrder;
    }

    [Tooltip("Seed the scene generator uses. The same seed always gives the same layout.")]
    [SerializeField] private int seed = 1;

    public int Seed => seed;

    /// <summary>
    /// The terrain layers this generator paints, bottom first. <see cref="AreaLayout.TerrainCells"/> is indexed like
    /// this list.
    /// </summary>
    public abstract IReadOnlyList<TerrainLayer> TerrainLayers { get; }

    /// <summary>
    /// Lays out an area from <paramref name="layoutSeed"/>, in cells of one unit.
    /// </summary>
    public abstract AreaLayout Generate(int layoutSeed);
}
