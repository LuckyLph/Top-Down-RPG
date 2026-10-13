using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lays out a forest clearing: a grass blob with a wobbly outline, crossed west to east by a stone road with gravel
/// paths branching off it north and south, ringed by a dense band of trees, with props scattered inside, the party
/// spawn on the road, mobs spread over open ground away from it, and a campsite (a campfire and seats on a patch of
/// dirt) on open ground inside. Blocking props stay off the paths, a clearance beside them and the ground around the
/// spawn, and every mob can walk to the spawn.
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
    [Tooltip("Packed earth around the campsite.")]
    [SerializeField] private TerrainLayer dirt = new();
    [SerializeField] private TerrainLayer gravel = new();
    [SerializeField] private TerrainLayer stone = new();

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

    [Header("Campsite")]
    [Tooltip("Stands in the middle of the campsite; none for no campsite.")]
    [SerializeField] private GameObject campfirePrefab;
    [Tooltip("Seats placed around the campfire, picked at random.")]
    [SerializeField] private List<GameObject> campsiteSeats = new();
    [SerializeField, Min(0)] private int seatCount = 3;
    [Tooltip("Smallest distance in cells between two seats.")]
    [SerializeField, Min(0f)] private float seatSpacing = 1.5f;
    [Tooltip("Radius in cells of the campsite's patch of dirt, before its outline wobbles.")]
    [SerializeField, Min(1f)] private float campsiteRadius = 3f;

    [Header("Mobs")]
    [SerializeField] private MobController mobPrefab;
    [SerializeField, Min(0)] private int mobCount = 3;
    [Tooltip("Smallest distance in cells from the party spawn to a mob spawn.")]
    [SerializeField, Min(0f)] private float mobDistanceFromSpawn = 14f;
    [Tooltip("Smallest distance in cells between two mob spawns.")]
    [SerializeField, Min(0f)] private float mobSpacing = 10f;

    public int GravelPathCount => gravelPathCount;
    public int BorderDepth => borderDepth;
    public GameObject CampfirePrefab => campfirePrefab;
    public IReadOnlyList<GameObject> CampsiteSeats => campsiteSeats;
    public int SeatCount => seatCount;
    public float CampsiteRadius => campsiteRadius;
    public int PathClearance => pathClearance;
    public int SpawnClearRadius => spawnClearRadius;
    public MobController MobPrefab => mobPrefab;
    public int MobCount => mobCount;
    public float MobDistanceFromSpawn => mobDistanceFromSpawn;
    public float MobSpacing => mobSpacing;

    /// <summary>
    /// Grass, dirt, gravel, then stone, in the style's layer order: each layer is drawn over the ones before it and
    /// decides its cells, so the road and the gravel paths are drawn over the grass and the stone over the gravel where
    /// they meet.
    /// </summary>
    public override IReadOnlyList<TerrainLayer> TerrainLayers => new[] { grass, dirt, gravel, stone };

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
            builder = new AreaLayoutBuilder(seed, generator.TerrainLayers, generator.grass, generator.gravel, generator.stone);
            halfWidth = Mathf.Max(4, generator.size.x) * 0.5f;
            halfHeight = Mathf.Max(4, generator.size.y) * 0.5f;
        }

        /// <summary>
        /// Lays the grass, the road and the gravel paths, picks the spawn, scatters the props layer by layer, spreads
        /// the mobs, then lays the campsite from the detail stream, so it moves nothing laid before it.
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
            LayCampsite();
            return builder.Layout;
        }

        /// <summary>
        /// Picks, from the detail stream, a campsite centre on interior grass: deeper than the edge band plus the
        /// campsite's radius, with no path within its radius plus the path clearance, and its radius clear of the
        /// spawn's disc and of every mob; among those, one of the cells with the fewest blocking-prop cells within its
        /// radius less half a cell, where the fire and the seats go. The props in that circle and the plants on or beside
        /// the campsite's wobbly patch of dirt (a plant's sprite stands above its cell, so one just below the patch would
        /// grow over it) are removed; a tree at the patch's rim stays and stands on the dirt, since grass goes on under
        /// it. The campfire stands in the middle and the seats on the dirt around it. Props are only
        /// removed, so nothing else moves and the spawn can still reach everything it could. Warns when no cell
        /// qualifies.
        /// </summary>
        private void LayCampsite()
        {
            if (settings.campfirePrefab == null)
            {
                return;
            }

            builder.StartDetailStream();
            float radius = settings.campsiteRadius;
            int reach = Mathf.CeilToInt(radius * 1.3f) + 1;
            float site = radius - 0.5f;
            List<Vector3Int> candidates = new();
            int fewest = int.MaxValue;
            foreach (Vector3Int cell in builder.OrderedGrass)
            {
                if (builder.EdgeDistance(cell) <= settings.borderDepth + radius || IsNear(cell, radius + settings.pathClearance, builder.IsPath)
                    || Vector3Int.Distance(cell, builder.Layout.SpawnCell) <= settings.spawnClearRadius + radius || !IsAwayFromMobs(cell, radius + 1f))
                {
                    continue;
                }

                int covered = CoveredCellsWithin(cell, site);
                if (covered < fewest)
                {
                    fewest = covered;
                    candidates.Clear();
                }

                if (covered == fewest)
                {
                    candidates.Add(cell);
                }
            }

            if (candidates.Count == 0)
            {
                builder.Layout.Warnings.Add("No open ground inside the clearing has room for the campsite.");
                return;
            }

            Vector3Int centre = candidates[builder.Next(candidates.Count)];
            HashSet<Vector3Int> dirt = builder.Layout.TerrainCells(settings.dirt);
            Vector2 middle = new(centre.x + 0.5f, centre.y + 0.5f);
            builder.RemoveProps(cell => Vector2.Distance(new Vector2(cell.x + 0.5f, cell.y + 0.5f), middle) <= site, false);
            Predicate<Vector2> contains = builder.WobblyShape(middle, new Vector2(radius, radius * 0.8f), 2f, 0.2f);
            List<Vector3Int> ring = new();
            for (int dy = -reach; dy <= reach; dy++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    Vector3Int cell = centre + new Vector3Int(dx, dy, 0);
                    Vector2 cellCentre = new(cell.x + 0.5f, cell.y + 0.5f);
                    if (builder.Grass.Contains(cell) && !builder.IsPath(cell) && contains(cellCentre))
                    {
                        dirt.Add(cell);
                        float distance = Vector2.Distance(cellCentre, middle);
                        if (distance >= 1.5f && distance <= radius - 0.5f)
                        {
                            ring.Add(cell);
                        }
                    }
                }
            }

            builder.RemoveProps(cell => IsNear(cell, 1f, dirt.Contains), true);
            if (!PropGridSnapping.TryGetFootprint(settings.campfirePrefab, out Rect footprint))
            {
                builder.Layout.Warnings.Add("The campfire prefab has no footprint; no campfire was placed.");
                return;
            }

            Vector2Int fire = PropGridSnapping.CellsCovered(footprint, Vector2.one);
            if (!builder.TryPlaceFootprint(settings.campfirePrefab, new Vector3Int(centre.x - (fire.x - 1) / 2, centre.y, 0)))
            {
                builder.Layout.Warnings.Add("The campfire did not fit on its campsite.");
                return;
            }

            builder.ScatterProps(settings.campsiteSeats, settings.seatCount, settings.seatSpacing, ring);
        }

        /// <summary>
        /// Whether a cell within <paramref name="steps"/> steps (8 neighbours) of <paramref name="cell"/> passes
        /// <paramref name="test"/>.
        /// </summary>
        private static bool IsNear(Vector3Int cell, float steps, Predicate<Vector3Int> test)
        {
            int reach = Mathf.CeilToInt(steps);
            for (int dy = -reach; dy <= reach; dy++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    if (test(cell + new Vector3Int(dx, dy, 0)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// How many cells covered by a blocking prop have their centre within <paramref name="distance"/> of
        /// <paramref name="cell"/>'s centre.
        /// </summary>
        private int CoveredCellsWithin(Vector3Int cell, float distance)
        {
            int reach = Mathf.CeilToInt(distance);
            int count = 0;
            for (int dy = -reach; dy <= reach; dy++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    if (dx * dx + dy * dy <= distance * distance && builder.Occupied.Contains(cell + new Vector3Int(dx, dy, 0)))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// Whether every mob spawn lies more than <paramref name="distance"/> from <paramref name="cell"/>.
        /// </summary>
        private bool IsAwayFromMobs(Vector3Int cell, float distance)
        {
            foreach (AreaLayout.Mob mob in builder.Layout.Mobs)
            {
                if (Vector3Int.Distance(cell, mob.Cell) <= distance)
                {
                    return false;
                }
            }

            return true;
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
