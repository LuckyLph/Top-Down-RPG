using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A world's generation settings (Docs/WorldGeneration.md): the <see cref="WorldDefinition"/> it composes, a world seed,
/// the materials the world lays itself, the sea around the land and its beach, the regions (<see cref="RegionPlacement"/>)
/// and the gaps between them, the road links between their ports (<see cref="RoadLink"/>), the waystones' plazas and the
/// gates, the wilds' patches and mobs per chunk, a record per chunk (<see cref="ChunkRecord"/>) and the names of the
/// entries the writer last set on the world.
/// <see cref="Compose"/> composes the world in memory, the same settings always giving the same
/// <see cref="WorldComposition"/>. Editor-only data, edited by editor tools only.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/World Generator", fileName = "WorldGenerator")]
public sealed class WorldGenerator : ScriptableObject
{
    /// <summary>
    /// The ground materials the world lays itself, outside the regions: the wilds' grass and their patches, the sea and
    /// its beach, the roads and the waystones' plazas. Each must be listed in the world's terrain style's layer order.
    /// </summary>
    [Serializable]
    public sealed class GroundMaterials
    {
        [Tooltip("The wilds' ground, under every land cell outside the regions, the beach included.")]
        [SerializeField] private TerrainMaterial grass;
        [Tooltip("The beach between the land and the ocean.")]
        [SerializeField] private TerrainMaterial sand;
        [Tooltip("The sea between the land and the world's edge.")]
        [SerializeField] private TerrainMaterial ocean;
        [Tooltip("Stone roads; also a path material, which marks a region's ports.")]
        [SerializeField] private TerrainMaterial stone;
        [Tooltip("Gravel roads; also a path material, which marks a region's ports.")]
        [SerializeField] private TerrainMaterial gravel;
        [Tooltip("The waystones' discs; also a path material, which marks a region's ports.")]
        [SerializeField] private TerrainMaterial plaza;
        [Tooltip("Weed patches on the wilds' grass.")]
        [SerializeField] private TerrainMaterial weeds;
        [Tooltip("Meadow patches on the wilds' grass.")]
        [SerializeField] private TerrainMaterial meadow;
        [Tooltip("Tall grass patches on the wilds' grass.")]
        [SerializeField] private TerrainMaterial tallGrass;

        /// <summary>
        /// Creates an empty set, for Unity serialization and the inspector.
        /// </summary>
        public GroundMaterials()
        {
        }

        /// <summary>
        /// Creates the set of the given materials, for tests and tools.
        /// </summary>
        public GroundMaterials(TerrainMaterial grass, TerrainMaterial sand, TerrainMaterial ocean, TerrainMaterial stone, TerrainMaterial gravel, TerrainMaterial plaza, TerrainMaterial weeds, TerrainMaterial meadow, TerrainMaterial tallGrass)
        {
            this.grass = grass;
            this.sand = sand;
            this.ocean = ocean;
            this.stone = stone;
            this.gravel = gravel;
            this.plaza = plaza;
            this.weeds = weeds;
            this.meadow = meadow;
            this.tallGrass = tallGrass;
        }

        public TerrainMaterial Grass => grass;
        public TerrainMaterial Sand => sand;
        public TerrainMaterial Ocean => ocean;
        public TerrainMaterial Stone => stone;
        public TerrainMaterial Gravel => gravel;
        public TerrainMaterial Plaza => plaza;
        public TerrainMaterial Weeds => weeds;
        public TerrainMaterial Meadow => meadow;
        public TerrainMaterial TallGrass => tallGrass;
    }

    /// <summary>
    /// The patches of one material the wilds lay per chunk: some centred near a region's outline, the rest anywhere on
    /// the chunk's open wilds grass, each a wobbly patch of a radius between the two values of its radius.
    /// </summary>
    [Serializable]
    public sealed class Patches
    {
        [Tooltip("Patches per chunk centred within the reach of a region's outline.")]
        [SerializeField, Min(0)] private int edgeCount;
        [Tooltip("Patches per chunk centred anywhere on its open wilds grass.")]
        [SerializeField, Min(0)] private int openCount;
        [Tooltip("Smallest and largest radius in cells of a patch.")]
        [SerializeField] private Vector2 radius = new(2f, 3.5f);
        [Tooltip("How many cells past the two cells patches keep from an outline an edge patch may lie.")]
        [SerializeField, Min(0)] private int reach = 4;

        /// <summary>
        /// Creates an empty set, for Unity serialization and the inspector.
        /// </summary>
        public Patches()
        {
        }

        /// <summary>
        /// Creates <paramref name="edge"/> edge and <paramref name="open"/> open patches per chunk of a radius between
        /// the two values of <paramref name="radii"/>, edge patches within <paramref name="edgeReach"/>.
        /// </summary>
        public Patches(int edge, int open, Vector2 radii, int edgeReach)
        {
            edgeCount = edge;
            openCount = open;
            radius = radii;
            reach = edgeReach;
        }

        public int EdgeCount => edgeCount;
        public int OpenCount => openCount;
        public Vector2 Radius => radius;
        public int Reach => reach;
    }

    /// <summary>
    /// Where in a chunk's wilds a <see cref="WildsScatter"/> places its props.
    /// </summary>
    public enum WildsZone
    {
        Edge,
        Grove,
        Open
    }

    /// <summary>
    /// Props the wilds scatter per chunk (<see cref="AreaGenerator.PropScatter"/>: a label, the prefabs, the most placed
    /// in a chunk and their spacing) over one part of the chunk's wilds: along the outlines of the regions whose ground is
    /// the wilds' grass, in its groves, or on the rest of its open ground.
    /// </summary>
    [Serializable]
    public sealed class WildsScatter : AreaGenerator.PropScatter
    {
        [Tooltip("Edge: the wilds within the edge reach of the outline of a region whose ground is the wilds' grass. Grove: the chunk's grove patches. Open: the rest.")]
        [SerializeField] private WildsZone zone;

        public WildsZone Zone => zone;
    }

    [Header("World")]
    [Tooltip("The world this generator composes, which gives its size in chunks, the chunks it has and its id.")]
    [SerializeField] private WorldDefinition world;
    [Tooltip("Seed of the world's own streams: the coast's swing, and with each chunk's coordinates the chunk's wilds.")]
    [SerializeField] private int seed = 1;
    [Tooltip("The style the world's chunks draw with: its layer order is the order of every chunk's layers.")]
    [SerializeField] private TerrainStyle terrainStyle;
    [Tooltip("Which ground can be walked, for the reachability check.")]
    [SerializeField] private TerrainMovementProfile2D movementProfile;
    [Tooltip("The materials the world lays itself, outside the regions.")]
    [SerializeField] private GroundMaterials materials = new();

    [Header("Sea")]
    [Tooltip("Cells from the world's edge to the coast, on average. The view is 20 x 11.25 cells, so keep 16 or more of sea in front of the land.")]
    [SerializeField, Min(4)] private float seaDepth = 20f;
    [Tooltip("How far the coast swings in and out of the sea depth, in cells.")]
    [SerializeField, Min(0f)] private float coastWobble = 3f;
    [Tooltip("Shortest and longest wave, in cells, of the smooth noise the coast and the beach's width swing with (the Coast generator's).")]
    [SerializeField] private Vector2 coastWavelength = new(12f, 36f);
    [Tooltip("Smallest and largest width of the beach in cells (the Coast generator's).")]
    [SerializeField] private Vector2 beachWidth = new(4f, 7f);
    [Tooltip("Radius in cells of the island's rounded corners.")]
    [SerializeField, Min(0f)] private float coastCornerRadius = 24f;
    [Tooltip("Cells over which the coast and the beach ease back from a sea region's own rows, beside it, to the sea depth and the beach's width.")]
    [SerializeField, Min(1f)] private float coastEase = 40f;

    [Header("Regions")]
    [Tooltip("The regions, in order: the first one's waystone is the world's default entry.")]
    [SerializeField] private List<RegionPlacement> regions = new();
    [Tooltip("Cells of wilds kept between two regions.")]
    [SerializeField, Min(0)] private int regionGap = 12;
    [Tooltip("Cells of wilds kept between a region and the beach, a sea region's sea side aside.")]
    [SerializeField, Min(0)] private int regionShoreGap = 6;
    [Tooltip("Radius in cells of the disc of plaza under each region's waystone, at its spawn cell.")]
    [SerializeField, Min(0f)] private float plazaRadius = 3f;

    [Header("Roads")]
    [Tooltip("The road links, laid in this order.")]
    [SerializeField] private List<RoadLink> links = new();
    [Tooltip("Cells a road runs straight out of its port, on its region road's rows, before it turns.")]
    [SerializeField, Min(2)] private int portLeadIn = 6;
    [Tooltip("Cells between the points of a road's course its spline runs through.")]
    [SerializeField, Min(2)] private int splineSpacing = 8;
    [Tooltip("How far a road wanders to either side of its course, in cells, on a stream of its own from the world's seed.")]
    [SerializeField, Min(0f)] private float roadMeander = 1.5f;
    [Tooltip("Shortest and longest wave, in cells along the road, of its wandering.")]
    [SerializeField] private Vector2 roadMeanderWavelength = new(24f, 64f);
    [Tooltip("Cells beyond its half width a road keeps from a region's outline and the beach, its ports aside.")]
    [SerializeField, Min(0)] private int roadMargin = 2;
    [Tooltip("Cells past that margin over which a road's course grows dearer towards an outline, the beach or another road.")]
    [SerializeField, Min(0)] private int roadAvoidance = 5;
    [Tooltip("Cells outside its port's outline a gate stands on its road.")]
    [SerializeField, Min(1)] private int gateDistance = 2;

    [Header("Wilds")]
    [Tooltip("Tall grass patches per chunk, laid first.")]
    [SerializeField] private Patches tallGrass = new(2, 0, new Vector2(2f, 3.5f), 4);
    [Tooltip("Meadow patches per chunk, laid after the tall grass.")]
    [SerializeField] private Patches meadows = new(0, 1, new Vector2(3f, 5f), 4);
    [Tooltip("Weed patches per chunk, laid last.")]
    [SerializeField] private Patches weeds = new(2, 2, new Vector2(2f, 3.5f), 4);
    [Tooltip("The mob the wilds spawn; none for no wilds mobs.")]
    [SerializeField] private MobController wildsMob;
    [Tooltip("Most mobs per chunk on its wilds grass.")]
    [SerializeField, Min(0)] private int mobsPerChunk = 1;
    [Tooltip("Smallest distance in cells from a waystone or a gate to a wilds mob.")]
    [SerializeField, Min(0f)] private float mobDistanceFromEntries = 14f;
    [Tooltip("Smallest distance in cells between a wilds mob and any other mob, the regions' included.")]
    [SerializeField, Min(0f)] private float mobSpacing = 10f;

    [Header("Wilds props")]
    [Tooltip("Grove patches per chunk, laid after the mobs: where Grove layers place their props.")]
    [SerializeField] private Patches groves = new(1, 1, new Vector2(4f, 7f), 6);
    [Tooltip("How many cells out from the outline of a region whose ground is the wilds' grass Edge layers place their props.")]
    [SerializeField, Min(1)] private int edgeReach = 3;
    [Tooltip("Cells a blocking prop of the wilds keeps from a road, a plaza and the beach.")]
    [SerializeField, Min(0)] private int propPathClearance = 2;
    [Tooltip("Cells every prop of the wilds keeps from a waystone or a gate.")]
    [SerializeField, Min(0f)] private float propEntryClearance = 4f;
    [Tooltip("The wilds' scatter layers per chunk, placed in this order: earlier layers take the room first.")]
    [SerializeField] private List<WildsScatter> wildsProps = new();

    [Header("Chunks")]
    [Tooltip("What the generator keeps per chunk: the written hash, locks, wide allowances and wilds seeds.")]
    [SerializeField] private List<ChunkRecord> chunks = new();
    [Tooltip("The names of the entries the writer last set first in the world's list; set by the writer, so the next write replaces them and keeps the entries made by hand.")]
    [SerializeField] private List<string> writtenEntries = new();

    public WorldDefinition World => world;
    public int Seed => seed;
    public TerrainStyle TerrainStyle => terrainStyle;
    public TerrainMovementProfile2D MovementProfile => movementProfile;
    public GroundMaterials Materials => materials;
    public float SeaDepth => seaDepth;
    public float CoastWobble => coastWobble;
    public Vector2 CoastWavelength => coastWavelength;
    public Vector2 BeachWidth => beachWidth;
    public float CoastCornerRadius => coastCornerRadius;
    public float CoastEase => coastEase;
    public IReadOnlyList<RegionPlacement> Regions => regions;
    public int RegionGap => regionGap;
    public int RegionShoreGap => regionShoreGap;
    public float PlazaRadius => plazaRadius;
    public IReadOnlyList<RoadLink> Links => links;
    public int PortLeadIn => portLeadIn;
    public int SplineSpacing => splineSpacing;
    public float RoadMeander => roadMeander;
    public Vector2 RoadMeanderWavelength => roadMeanderWavelength;
    public int RoadMargin => roadMargin;
    public int RoadAvoidance => roadAvoidance;
    public int GateDistance => gateDistance;
    public Patches TallGrass => tallGrass;
    public Patches Meadows => meadows;
    public Patches Weeds => weeds;
    public MobController WildsMob => wildsMob;
    public int MobsPerChunk => mobsPerChunk;
    public float MobDistanceFromEntries => mobDistanceFromEntries;
    public float MobSpacing => mobSpacing;
    public Patches Groves => groves;
    public int EdgeReach => edgeReach;
    public int PropPathClearance => propPathClearance;
    public float PropEntryClearance => propEntryClearance;
    public IReadOnlyList<WildsScatter> WildsProps => wildsProps;
    public IReadOnlyList<ChunkRecord> Chunks => chunks;
    public IReadOnlyList<string> WrittenEntries => writtenEntries;

    /// <summary>
    /// Composes the world from these settings, in memory (Docs/WorldGeneration.md: Architecture: Composition).
    /// </summary>
    public WorldComposition Compose()
    {
        return WorldComposer.Compose(this);
    }

    /// <summary>
    /// The record of the chunk at <paramref name="coords"/>, or null when the generator keeps none.
    /// </summary>
    public ChunkRecord RecordOf(Vector2Int coords)
    {
        foreach (ChunkRecord record in chunks)
        {
            if (record != null && record.Coords == coords)
            {
                return record;
            }
        }

        return null;
    }

    /// <summary>
    /// Creates an unsaved generator for <paramref name="target"/> with the given style, profile, materials and wilds
    /// mob and every other setting at its default, for tests and tools.
    /// </summary>
    internal static WorldGenerator Create(WorldDefinition target, TerrainStyle style, TerrainMovementProfile2D profile, GroundMaterials groundMaterials, MobController mob)
    {
        WorldGenerator generator = CreateInstance<WorldGenerator>();
        generator.hideFlags = HideFlags.DontSave;
        generator.world = target;
        generator.terrainStyle = style;
        generator.movementProfile = profile;
        generator.materials = groundMaterials;
        generator.wildsMob = mob;
        return generator;
    }

    /// <summary>
    /// Sets the world seed, for tests and tools.
    /// </summary>
    internal void SetSeed(int worldSeed)
    {
        seed = worldSeed;
    }

    /// <summary>
    /// Replaces the region placements, for tests and tools.
    /// </summary>
    internal void SetRegions(params RegionPlacement[] placements)
    {
        regions = new List<RegionPlacement>(placements);
    }

    /// <summary>
    /// Replaces the road links, for tests and tools.
    /// </summary>
    internal void SetLinks(params RoadLink[] roadLinks)
    {
        links = new List<RoadLink>(roadLinks);
    }

    /// <summary>
    /// Sets <paramref name="record"/> as the record of its chunk, replacing any there, for tests and tools.
    /// </summary>
    internal void SetRecord(ChunkRecord record)
    {
        chunks.RemoveAll(other => other == null || other.Coords == record.Coords);
        chunks.Add(record);
    }

    /// <summary>
    /// Records <paramref name="hash"/> as the written hash of the chunk at <paramref name="coords"/>, and
    /// <paramref name="mobIds"/> (none when null) as the ids of the generated spawn points written there, adding a
    /// record in row order when it has none; the writer calls it once it has saved the chunk.
    /// </summary>
    internal void SetWrittenHash(Vector2Int coords, string hash, IEnumerable<string> mobIds = null)
    {
        ChunkRecord record = RecordOf(coords);
        if (record == null)
        {
            record = new ChunkRecord(coords, false, false, 0);
            int at = chunks.FindIndex(other => other != null && (other.Coords.y > coords.y || (other.Coords.y == coords.y && other.Coords.x > coords.x)));
            chunks.Insert(at < 0 ? chunks.Count : at, record);
        }

        record.SetWrittenHash(hash, mobIds);
    }

    /// <summary>
    /// Records <paramref name="names"/> as the entries the writer set on the world.
    /// </summary>
    internal void SetWrittenEntries(IEnumerable<string> names)
    {
        writtenEntries = new List<string>(names);
    }
}
