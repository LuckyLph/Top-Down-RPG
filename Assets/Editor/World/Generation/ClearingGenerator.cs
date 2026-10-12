using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lays out a forest clearing: a grass blob with a wobbly outline, crossed west to east by a stone road with gravel
/// paths branching off it north and south, ringed by a dense band of trees, with props scattered inside, the party
/// spawn on the road and mobs spread over open ground away from it. Blocking props stay off the paths, a clearance
/// beside them and the ground around the spawn, and every mob can walk to the spawn.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Clearing Generator", fileName = "Generator_Clearing")]
public sealed class ClearingGenerator : AreaGenerator
{
    /// <summary>
    /// The part of the clearing a scatter layer fills: the band along its edge, or the ground inside that band.
    /// </summary>
    public enum Zone
    {
        Border,
        Interior
    }

    /// <summary>
    /// Props scattered over one zone of the clearing.
    /// </summary>
    [Serializable]
    public class Scatter : PropScatter
    {
        [SerializeField] private Zone zone = Zone.Interior;

        public Zone Zone => zone;
    }

    [Header("Terrain")]
    [SerializeField] private TerrainLayer grass = new();
    [Tooltip("Generated empty: painted by hand.")]
    [SerializeField] private TerrainLayer sand = new();
    [Tooltip("Generated empty: painted by hand.")]
    [SerializeField] private TerrainLayer mud = new();
    [SerializeField] private TerrainLayer gravel = new();
    [SerializeField] private TerrainLayer stone = new();
    [Tooltip("Generated empty: painted by hand. Drawn over every other layer, so it decides its cells: water cannot be walked.")]
    [SerializeField] private TerrainLayer water = new();

    [Header("Shape")]
    [Tooltip("Width and height of the clearing in cells, before its outline wobbles.")]
    [SerializeField] private Vector2Int size = new(46, 32);
    [Tooltip("Exponent of the outline's superellipse: 2 is an ellipse, higher is closer to a rectangle.")]
    [SerializeField, Range(2f, 6f)] private float squareness = 3f;
    [Tooltip("How far the outline wobbles in and out, as a fraction of its radius.")]
    [SerializeField, Range(0f, 0.3f)] private float edgeWobble = 0.12f;

    [Header("Paths")]
    [Tooltip("Width in cells of the stone road crossing the clearing from west to east.")]
    [SerializeField, Min(1)] private int roadWidth = 3;
    [Tooltip("How far the road wanders north and south, as a fraction of the clearing's half height.")]
    [SerializeField, Range(0f, 0.6f)] private float roadMeander = 0.3f;
    [Tooltip("Gravel paths branching off the road, alternately to the north and south edges.")]
    [SerializeField, Min(0)] private int gravelPathCount = 2;
    [SerializeField, Min(1)] private int gravelPathWidth = 2;
    [Tooltip("Cells beside every path kept free of blocking props.")]
    [SerializeField, Min(0)] private int pathClearance = 1;

    [Header("Spawn")]
    [Tooltip("Where along the road the party arrives, from -1 (west edge) to 1 (east edge).")]
    [SerializeField, Range(-0.9f, 0.9f)] private float spawnAlongRoad = -0.55f;
    [Tooltip("Radius in cells around the spawn that stays on grass and free of blocking props.")]
    [SerializeField, Min(0)] private int spawnClearRadius = 6;

    [Header("Props")]
    [Tooltip("Depth in cells of the band along the edge that Border layers fill.")]
    [SerializeField, Min(1)] private int borderDepth = 3;
    [Tooltip("Scatter layers, placed in this order: earlier layers take the room first.")]
    [SerializeField] private List<Scatter> scatters = new();

    [Header("Mobs")]
    [SerializeField] private MobController mobPrefab;
    [SerializeField, Min(0)] private int mobCount = 3;
    [Tooltip("Smallest distance in cells from the party spawn to a mob spawn.")]
    [SerializeField, Min(0f)] private float mobDistanceFromSpawn = 14f;
    [Tooltip("Smallest distance in cells between two mob spawns.")]
    [SerializeField, Min(0f)] private float mobSpacing = 10f;

    public int GravelPathCount => gravelPathCount;
    public int PathClearance => pathClearance;
    public int SpawnClearRadius => spawnClearRadius;
    public MobController MobPrefab => mobPrefab;
    public int MobCount => mobCount;
    public float MobDistanceFromSpawn => mobDistanceFromSpawn;
    public float MobSpacing => mobSpacing;

    /// <summary>
    /// Grass, sand, mud, gravel, stone, then water: each layer is drawn over the ones before it and decides its cells,
    /// so the paths cross sand and mud and water covers everything. Sand, mud and water are generated empty.
    /// </summary>
    public override IReadOnlyList<TerrainLayer> TerrainLayers => new[] { grass, sand, mud, gravel, stone, water };

    /// <summary>
    /// Lays out a clearing from <paramref name="layoutSeed"/>.
    /// </summary>
    public override AreaLayout Generate(int layoutSeed)
    {
        return new Run(this, layoutSeed).Build();
    }


    /// <summary>
    /// The state of one generation: the shared builder with the cells laid so far, and the road's samples.
    /// </summary>
    private sealed class Run
    {
        private readonly ClearingGenerator settings;
        private readonly AreaLayoutBuilder builder;
        private readonly List<Vector2> road = new();
        private readonly float halfWidth;
        private readonly float halfHeight;

        /// <summary>
        /// Prepares a generation of <paramref name="generator"/>'s settings from <paramref name="seed"/>.
        /// </summary>
        public Run(ClearingGenerator generator, int seed)
        {
            settings = generator;
            builder = new AreaLayoutBuilder(seed);
            halfWidth = Mathf.Max(4, generator.size.x) * 0.5f;
            halfHeight = Mathf.Max(4, generator.size.y) * 0.5f;
        }

        /// <summary>
        /// Lays the grass, the road and the gravel paths, picks the spawn, scatters the props layer by layer and
        /// spreads the mobs.
        /// </summary>
        public AreaLayout Build()
        {
            builder.LayGrass(new Vector2(halfWidth, halfHeight), settings.squareness, settings.edgeWobble);
            LayRoad();
            LayGravelPaths();
            PlaceSpawn();
            builder.ReservePathClearance(settings.pathClearance);
            builder.ReserveDisc(builder.Layout.SpawnCell, settings.spawnClearRadius);
            foreach (Scatter scatter in settings.scatters)
            {
                if (scatter != null)
                {
                    ScatterProps(scatter);
                }
            }

            PlaceMobs();
            return builder.Layout;
        }

        /// <summary>
        /// Runs the stone road through five points from beyond the west edge to beyond the east edge, each at a
        /// random height within the meander, and paints it on the grass.
        /// </summary>
        private void LayRoad()
        {
            float meander = settings.roadMeander * halfHeight;
            Vector2[] points =
            {
                new(-halfWidth - 3f, builder.Range(-1f, 1f) * meander),
                new(-halfWidth * 0.5f, builder.Range(-1f, 1f) * meander),
                new(0f, builder.Range(-1f, 1f) * meander),
                new(halfWidth * 0.5f, builder.Range(-1f, 1f) * meander),
                new(halfWidth + 3f, builder.Range(-1f, 1f) * meander)
            };
            AreaLayoutBuilder.SampleSpline(points, road);
            builder.Paint(road, settings.roadWidth, builder.Stone);
        }

        /// <summary>
        /// Branches the gravel paths off the road, spread along its middle half, each heading alternately for the
        /// north or south edge through a point that bends it sideways.
        /// </summary>
        private void LayGravelPaths()
        {
            int count = settings.gravelPathCount;
            bool north = builder.Next(2) == 0;
            List<Vector2> branch = new();
            for (int i = 0; i < count; i++)
            {
                float along = Mathf.Lerp(0.25f, 0.75f, Mathf.Clamp01((i + 0.5f + builder.Range(-0.3f, 0.3f)) / count));
                Vector2 start = road[Mathf.RoundToInt(along * (road.Count - 1))];
                float side = north ? 1f : -1f;
                north = !north;
                Vector2 end = new(start.x + builder.Range(-0.35f, 0.35f) * halfWidth, side * (halfHeight + 3f));
                Vector2 bend = Vector2.Lerp(start, end, 0.5f) + new Vector2(builder.Range(-0.2f, 0.2f) * halfWidth, 0f);
                branch.Clear();
                AreaLayoutBuilder.SampleSpline(new[] { start, bend, end }, branch);
                builder.Paint(branch, settings.gravelPathWidth, builder.Gravel);
            }
        }

        /// <summary>
        /// Puts the party spawn on the road where it passes the configured point from west to east, or at the
        /// nearest road cell with grass all around it for the clear radius.
        /// </summary>
        private void PlaceSpawn()
        {
            float targetX = settings.spawnAlongRoad * halfWidth;
            List<Vector2> byDistance = new(road);
            byDistance.Sort((a, b) => Mathf.Abs(a.x - targetX).CompareTo(Mathf.Abs(b.x - targetX)));
            builder.PlaceSpawn(byDistance, settings.spawnClearRadius);
        }

        /// <summary>
        /// Scatters <paramref name="scatter"/>'s props over the grass cells of its zone.
        /// </summary>
        private void ScatterProps(Scatter scatter)
        {
            List<Vector3Int> candidates = new();
            foreach (Vector3Int cell in builder.OrderedGrass)
            {
                bool inBorder = builder.EdgeDistance(cell) <= settings.borderDepth;
                if (inBorder == (scatter.Zone == Zone.Border))
                {
                    candidates.Add(cell);
                }
            }

            builder.ScatterProps(scatter.Prefabs, scatter.Count, scatter.Spacing, candidates);
        }

        /// <summary>
        /// Spreads the mobs over cells inside the border band that the spawn can walk to, away from props, at least
        /// the configured distance from the spawn and from each other. Warns when fewer fit than asked for.
        /// </summary>
        private void PlaceMobs()
        {
            int wanted = settings.mobCount;
            if (wanted == 0)
            {
                return;
            }

            if (settings.mobPrefab == null)
            {
                builder.Layout.Warnings.Add("No mob prefab is set; no mobs were placed.");
                return;
            }

            List<Vector3Int> candidates = builder.MobCells(builder.ReachableFromSpawn(), settings.borderDepth, settings.mobDistanceFromSpawn);
            int placed = builder.PlaceMobs(settings.mobPrefab, wanted, candidates, settings.mobSpacing, new List<Vector3Int>());
            if (placed < wanted)
            {
                builder.Layout.Warnings.Add($"Placed {placed} of {wanted} mobs: not enough open ground {settings.mobDistanceFromSpawn} cells from the spawn and {settings.mobSpacing} apart.");
            }
        }
    }
}
