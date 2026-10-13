using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// An editor-only recipe for one kind of environment. <see cref="Generate"/> lays out an area's terrain, props, mob
/// spawns and party spawn from a seed, the same seed always giving the same <see cref="AreaLayout"/>;
/// <see cref="AreaLayoutWriter"/> writes the layout into an area scene and <see cref="SceneGeneratorWindow"/> drives
/// both. The scene then holds the result like any hand-made area, so every machine loads the same content. Its terrain
/// layers and terrain style also define how an area's ground is set up, which <see cref="GroundLayerMigration"/>
/// brings an existing area in line with without repainting it.
/// </summary>
public abstract class AreaGenerator : ScriptableObject
{
    /// <summary>
    /// One ground layer a generator paints: a plain data tilemap holding one plain tile in its filled cells, drawn by
    /// the area's <see cref="TerrainBlendRenderer"/> with a material. Its material's place in the style's layer order
    /// decides where it is drawn, and its place among the area's layers is its navigation priority, so it has neither
    /// of its own.
    /// </summary>
    [Serializable]
    public class TerrainLayer
    {
        [Tooltip("Name of the data tilemap under the area's Grid. The writer creates it when the scene has none.")]
        [SerializeField] private string tilemapName = "GroundTilemap";
        [Tooltip("The plain tile the layer's filled cells hold (a Tile_ asset of Assets/Tiles/Ground), the one its palette holds.")]
        [SerializeField] private Tile tile;
        [Tooltip("Terrain type the layer's navigation source gives its cells.")]
        [SerializeField] private TerrainType2D terrainType;
        [Tooltip("The material the area's Terrain Blend Renderer draws the layer with.")]
        [SerializeField] private TerrainMaterial material;

        /// <summary>
        /// Creates an empty layer, for Unity serialization and the inspector.
        /// </summary>
        public TerrainLayer()
        {
        }

        /// <summary>
        /// Creates a layer for the tilemap named <paramref name="tilemapName"/>, filled with <paramref name="tile"/>,
        /// of <paramref name="terrainType"/> and drawn with <paramref name="material"/>, for tests and tools.
        /// </summary>
        public TerrainLayer(string tilemapName, Tile tile, TerrainType2D terrainType, TerrainMaterial material)
        {
            this.tilemapName = tilemapName;
            this.tile = tile;
            this.terrainType = terrainType;
            this.material = material;
        }

        public string TilemapName => tilemapName;
        public Tile Tile => tile;
        public TerrainType2D TerrainType => terrainType;
        public TerrainMaterial Material => material;
    }

    /// <summary>
    /// Props a generator scatters over part of its area, picked at random from a list, at least a spacing apart. Each
    /// prefab is placed as a blocking or walk-through prop depending on whether it has a footprint collider. Each
    /// generator's own scatter type says which part of the area it fills.
    /// </summary>
    [Serializable]
    public class PropScatter
    {
        [SerializeField] private string label = "Props";
        [SerializeField] private List<GameObject> prefabs = new();
        [Tooltip("Most props of this layer placed. Fewer are placed when its part of the area runs out of room.")]
        [SerializeField, Min(0)] private int count = 10;
        [Tooltip("Smallest distance in cells between two props of this layer.")]
        [SerializeField, Min(0f)] private float spacing = 3f;

        public string Label => label;
        public IReadOnlyList<GameObject> Prefabs => prefabs;
        public int Count => count;
        public float Spacing => spacing;
    }

    [Tooltip("Seed the scene generator uses. The same seed always gives the same layout.")]
    [SerializeField] private int seed = 1;
    [Tooltip("The style the area's Terrain Blend Renderer draws the terrain layers with.")]
    [SerializeField] private TerrainStyle terrainStyle;

    public int Seed => seed;
    public TerrainStyle TerrainStyle => terrainStyle;

    /// <summary>
    /// The terrain layers this generator paints, bottom first: only the ones its areas use, at most
    /// <see cref="TerrainControlMap.MaxLayers"/>, their materials a subsequence of the style's
    /// <see cref="TerrainStyle.LayerOrder"/> (each listed there, in its order, none twice). A layer's index here is its
    /// place among the generator's layers, and in an area drawing nothing else its draw order in the area's
    /// <see cref="TerrainBlendRenderer"/> and its navigation priority. <see cref="AreaLayout.TerrainCells(int)"/> is
    /// indexed like this list.
    /// </summary>
    public abstract IReadOnlyList<TerrainLayer> TerrainLayers { get; }

    /// <summary>
    /// Lays out an area from <paramref name="layoutSeed"/>, in cells of one unit.
    /// </summary>
    public abstract AreaLayout Generate(int layoutSeed);
}
