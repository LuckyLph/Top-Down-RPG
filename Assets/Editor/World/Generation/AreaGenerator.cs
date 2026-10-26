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
    /// The weed patches a generator lays on its plain ground (<see cref="AreaLayoutBuilder.LayWeeds"/>): some along the
    /// band of trees at the area's edge and whatever borders the generator adds (the Lakeside's marsh), the rest
    /// anywhere on open ground, each a wobbly patch of a radius between the two values of its radius.
    /// </summary>
    [Serializable]
    public class WeedPatches
    {
        [Tooltip("Patches centred within the reach of the band of trees along the area's edge, or of the generator's other borders.")]
        [SerializeField, Min(0)] private int edgeCount = 6;
        [Tooltip("Patches centred anywhere on open ground.")]
        [SerializeField, Min(0)] private int openCount = 4;
        [Tooltip("Smallest and largest radius in cells of a patch.")]
        [SerializeField] private Vector2 radius = new(2f, 3.5f);
        [Tooltip("How many cells past the band of trees, or a border, an edge patch may lie.")]
        [SerializeField, Min(0)] private int reach = 3;

        public int EdgeCount => edgeCount;
        public int OpenCount => openCount;
        public Vector2 Radius => radius;
        public int Reach => reach;
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

    /// <summary>
    /// Props a generator swaps for others where terrain laid after them lies under them
    /// (<see cref="AreaLayoutBuilder.SwapProps"/>): each blocking prop of <see cref="From"/> whose footprint lies
    /// wholly on that terrain becomes one of <see cref="To"/> covering the same cells, so nothing else moves.
    /// </summary>
    [Serializable]
    public class PropSwap
    {
        [Tooltip("The blocking props swapped where they stand on the terrain.")]
        [SerializeField] private List<GameObject> from = new();
        [Tooltip("The props they become: one covering the same cells, picked by the prop's cell. A prop none of them fits stays.")]
        [SerializeField] private List<GameObject> to = new();

        public IReadOnlyList<GameObject> From => from;
        public IReadOnlyList<GameObject> To => to;
    }

    public const string RaisedTilemapName = "RaisedTilemap";

    [Tooltip("Seed the scene generator uses. The same seed always gives the same layout.")]
    [SerializeField] private int seed = 1;
    [Tooltip("The style the area's Terrain Blend Renderer draws the terrain layers with.")]
    [SerializeField] private TerrainStyle terrainStyle;

    [Header("Raised ground")]
    [Tooltip("The tile of raised ground the writer puts on the area's raised tilemap; none for a generator that lays no raised ground.")]
    [SerializeField] private RaisedGroundTile raisedTile;
    [Tooltip("The tile of a ramp the writer puts on the area's raised tilemap.")]
    [SerializeField] private RaisedGroundTile rampTile;

    public int Seed => seed;
    public TerrainStyle TerrainStyle => terrainStyle;
    public RaisedGroundTile RaisedTile => raisedTile;
    public RaisedGroundTile RampTile => rampTile;

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
    /// The scatter layers, in the order they are placed. Each prop a layer places records it
    /// (<see cref="AreaLayout.Prop.Scatter"/>).
    /// </summary>
    public abstract IReadOnlyList<PropScatter> Scatters { get; }

    /// <summary>
    /// Lays out an area from <paramref name="layoutSeed"/>, in cells of one unit.
    /// </summary>
    public AreaLayout Generate(int layoutSeed)
    {
        return Generate(layoutSeed, true);
    }

    /// <summary>
    /// Lays out an area from <paramref name="layoutSeed"/>, in cells of one unit. With <paramref name="scatter"/> false
    /// the scatter layers place nothing, as if they listed no prefabs, so the rest of the layout is what it was before
    /// they were filled: world composition lays its regions that way until it writes their props.
    /// </summary>
    public abstract AreaLayout Generate(int layoutSeed, bool scatter);
}
