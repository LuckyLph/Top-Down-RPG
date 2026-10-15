using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lays out a coast: grass ringed by trees, with a sandy beach along its northern edge giving onto the ocean, which
/// runs out to the map's edge. A stone road crosses the south from west to east, a
/// gravel trail runs from it to the beach, and a campsite (a campfire and seats on a patch of trodden dirt) stands on
/// the open grass. The party spawns on the road. Blocking props stay off the beach, the sea, the paths, a clearance
/// beside them and the ground around the spawn; the beach, the trail and every mob can be walked to from the spawn, and
/// the ocean, which cannot be walked, keeps every unit off the map's northern edge.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Coast Generator", fileName = "Generator_Coast")]
public sealed class CoastGenerator : AreaGenerator
{
    private const int CoastHarmonics = 3;
    private const int SeaGap = 2;
    private const int CampsiteCoastMargin = 2;
    private const float TrailLeadIn = 3f;

    /// <summary>
    /// The part of the coast a scatter layer fills: the band along its edge, the grass near the beach, or the rest of
    /// the ground inside the band.
    /// </summary>
    public enum Zone
    {
        Border,
        Interior,
        Shore
    }

    /// <summary>
    /// Props scattered over one zone of the coast.
    /// </summary>
    [Serializable]
    public class Scatter : PropScatter
    {
        [SerializeField] private Zone zone = Zone.Interior;

        public Zone Zone => zone;
    }

    [Header("Terrain")]
    [Tooltip("The ground every other layer lies on.")]
    [SerializeField] private TerrainLayer grass = new();
    [Tooltip("Trodden earth around the campsite.")]
    [SerializeField] private TerrainLayer dirt = new();
    [Tooltip("The beach, drawn over the grass.")]
    [SerializeField] private TerrainLayer sand = new();
    [SerializeField] private TerrainLayer gravel = new();
    [SerializeField] private TerrainLayer stone = new();
    [Tooltip("The sea beyond the beach, out to the map's edge, drawn over everything under it so it decides its cells: the ocean cannot be walked.")]
    [SerializeField] private TerrainLayer ocean = new();

    [Header("Shape")]
    [Tooltip("Width and height of the area in cells, before its outline wobbles.")]
    [SerializeField] private Vector2Int size = new(64, 46);
    [Tooltip("Exponent of the outline's superellipse: 2 is an ellipse, higher is closer to a rectangle, so the sea runs further along the northern edge before the corners cut it.")]
    [SerializeField, Range(2f, 8f)] private float squareness = 6f;
    [Tooltip("How far the outline wobbles in and out, as a fraction of its radius.")]
    [SerializeField, Range(0f, 0.3f)] private float edgeWobble = 0.05f;

    [Header("Coast")]
    [Tooltip("Rows of ocean below the area's nominal northern edge, on average; the outline's wobble adds to them or takes from them.")]
    [SerializeField, Min(2f)] private float oceanDepth = 9f;
    [Tooltip("How far the ocean's edge swings north and south of the ocean depth, in rows.")]
    [SerializeField, Min(0f)] private float oceanWobble = 2f;
    [Tooltip("Smallest and largest width in rows of the beach; at least two rows.")]
    [SerializeField] private Vector2 beachWidth = new(4f, 7f);
    [Tooltip("Shortest and longest wave, in cells, of the smooth noise that swings the ocean's and the beach's edges along the coast.")]
    [SerializeField] private Vector2 coastWavelength = new(12f, 36f);
    [Tooltip("Smallest distance in cells from the beach or the sea to the road; a road any closer gives a warning.")]
    [SerializeField, Min(1)] private int roadMargin = 5;
    [Tooltip("Depth in cells of the band of grass along the beach that Shore layers fill.")]
    [SerializeField, Min(1)] private int shoreZoneDepth = 3;

    [Header("Paths")]
    [Tooltip("Width in cells of the stone road crossing the south of the area from west to east.")]
    [SerializeField, Min(1)] private int roadWidth = 3;
    [Tooltip("How far south of the middle the road runs, as a fraction of the area's half height.")]
    [SerializeField, Range(0f, 0.8f)] private float roadSouth = 0.55f;
    [Tooltip("How far the road wanders north and south, as a fraction of the area's half height.")]
    [SerializeField, Range(0f, 0.4f)] private float roadMeander = 0.12f;
    [Tooltip("Width in cells of the gravel trail from the road to the beach.")]
    [SerializeField, Min(1)] private int trailWidth = 2;
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
    [SerializeField, Min(0)] private int mobCount = 4;
    [Tooltip("Smallest distance in cells from the party spawn to a mob spawn.")]
    [SerializeField, Min(0f)] private float mobDistanceFromSpawn = 14f;
    [Tooltip("Smallest distance in cells between two mob spawns.")]
    [SerializeField, Min(0f)] private float mobSpacing = 10f;

    public int BorderDepth => borderDepth;
    public Vector2 BeachWidth => beachWidth;
    public int RoadMargin => roadMargin;
    public int PathClearance => pathClearance;
    public int SpawnClearRadius => spawnClearRadius;
    public GameObject CampfirePrefab => campfirePrefab;
    public IReadOnlyList<GameObject> CampsiteSeats => campsiteSeats;
    public float CampsiteRadius => campsiteRadius;
    public MobController MobPrefab => mobPrefab;
    public int MobCount => mobCount;
    public float MobDistanceFromSpawn => mobDistanceFromSpawn;
    public float MobSpacing => mobSpacing;

    /// <summary>
    /// Grass, dirt, sand, gravel, stone, then the ocean, in the style's layer order: each layer is drawn over the ones
    /// before it and decides its cells, so the beach lies on the grass, the trail runs onto the beach and the ocean lies
    /// over the sand.
    /// </summary>
    public override IReadOnlyList<TerrainLayer> TerrainLayers => new[] { grass, dirt, sand, gravel, stone, ocean };

    /// <summary>
    /// Lays out a coast from <paramref name="layoutSeed"/>.
    /// </summary>
    public override AreaLayout Generate(int layoutSeed)
    {
        return new Run(this, layoutSeed).Build();
    }

    /// <summary>
    /// The state of one generation: the shared builder with the cells laid so far, the road's samples and how many
    /// steps each ground cell lies from the coast (the beach and the ocean) and from the ocean alone.
    /// </summary>
    private sealed class Run
    {
        private readonly CoastGenerator settings;
        private readonly AreaLayoutBuilder builder;
        private readonly HashSet<Vector3Int> sand;
        private readonly HashSet<Vector3Int> ocean;
        private readonly HashSet<Vector3Int> dirt;
        private readonly List<Vector2> road = new();
        private readonly Dictionary<Vector3Int, int> coastDistance = new();
        private readonly Dictionary<Vector3Int, int> seaDistance = new();
        private readonly float halfWidth;
        private readonly float halfHeight;

        /// <summary>
        /// Prepares a generation of <paramref name="generator"/>'s settings from <paramref name="seed"/>.
        /// </summary>
        public Run(CoastGenerator generator, int seed)
        {
            settings = generator;
            builder = new AreaLayoutBuilder(seed, generator.TerrainLayers, generator.grass, generator.gravel, generator.stone);
            sand = builder.Layout.TerrainCells(generator.sand);
            ocean = builder.Layout.TerrainCells(generator.ocean);
            dirt = builder.Layout.TerrainCells(generator.dirt);
            halfWidth = Mathf.Max(8, generator.size.x) * 0.5f;
            halfHeight = Mathf.Max(8, generator.size.y) * 0.5f;
        }

        /// <summary>
        /// Lays the grass, the coast's bands, the road and the trail to the beach, picks the spawn, keeps blocking props
        /// off the coast, scatters the props layer by layer, spreads the mobs, checks that the beach and the trail can be
        /// walked to, then lays the campsite from the detail stream, so it moves nothing laid before it.
        /// </summary>
        public AreaLayout Build()
        {
            builder.LayGround(new Vector2(halfWidth, halfHeight), settings.squareness, settings.edgeWobble);
            LayCoast();
            MeasureDistances();
            LayRoad();
            LayTrail();
            PlaceSpawn();
            builder.ReservePathClearance(settings.pathClearance);
            builder.ReserveDisc(builder.Layout.SpawnCell, settings.spawnClearRadius);
            ReserveCoast();
            foreach (Scatter scatter in settings.scatters)
            {
                if (scatter != null)
                {
                    ScatterProps(scatter);
                }
            }

            HashSet<Vector3Int> reachable = Reachable();
            PlaceMobs(reachable);
            CheckBeach(reachable);
            builder.StartDetailStream();
            LayCampsite();
            return builder.Layout;
        }

        /// <summary>
        /// Lays the coast across the north of the area, each edge a smooth line swinging north and south along the
        /// coast: the ocean from the map's edge down to the ocean depth below the area's nominal northern edge, then the
        /// beach, between its two widths. Where the lines step past each other between two columns, the beach takes any
        /// grass cell beside the ocean (8 neighbours), so the ocean meets only the beach and no third look shows under a
        /// rounded corner; a piece of ocean cut off from the rest (4 neighbours), where the outline steps at a corner of
        /// the map, is beach. The grass goes on under both.
        /// </summary>
        private void LayCoast()
        {
            Func<float, float> oceanSwing = CoastNoise();
            Func<float, float> beachSwing = CoastNoise();
            float beachLow = Mathf.Max(2f, settings.beachWidth.x);
            float beachHigh = Mathf.Max(beachLow, settings.beachWidth.y);
            foreach (Vector3Int cell in builder.OrderedGround)
            {
                float x = cell.x + 0.5f;
                float oceanEdge = halfHeight - settings.oceanDepth + settings.oceanWobble * oceanSwing(x);
                float beachEdge = oceanEdge - Mathf.Lerp(beachLow, beachHigh, 0.5f + 0.5f * beachSwing(x));
                float y = cell.y + 0.5f;
                if (y >= oceanEdge)
                {
                    ocean.Add(cell);
                }
                else if (y >= beachEdge)
                {
                    sand.Add(cell);
                }
            }

            HashSet<Vector3Int> largest = new();
            HashSet<Vector3Int> unseen = new(ocean);
            foreach (Vector3Int cell in builder.OrderedGround)
            {
                if (unseen.Contains(cell))
                {
                    HashSet<Vector3Int> region = AreaLayoutBuilder.FloodFill(cell, unseen.Contains);
                    unseen.ExceptWith(region);
                    if (region.Count > largest.Count)
                    {
                        largest = region;
                    }
                }
            }

            foreach (Vector3Int cell in builder.OrderedGround)
            {
                if (ocean.Contains(cell) && !largest.Contains(cell))
                {
                    ocean.Remove(cell);
                    sand.Add(cell);
                }
            }

            List<Vector3Int> beach = new();
            foreach (Vector3Int cell in builder.OrderedGround)
            {
                if (!ocean.Contains(cell) && !sand.Contains(cell) && Touches(cell, ocean.Contains))
                {
                    beach.Add(cell);
                }
            }

            sand.UnionWith(beach);
        }

        /// <summary>
        /// Whether one of the 8 neighbours of <paramref name="cell"/> passes <paramref name="test"/>.
        /// </summary>
        private static bool Touches(Vector3Int cell, Predicate<Vector3Int> test)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if ((dx != 0 || dy != 0) && test(cell + new Vector3Int(dx, dy, 0)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// A smooth noise along the coast, from -1 to 1: a few sine waves of random wavelengths between the coast's
        /// shortest and longest and random phases, weighted at random and scaled by their total weight.
        /// </summary>
        private Func<float, float> CoastNoise()
        {
            float[] frequencies = new float[CoastHarmonics];
            float[] phases = new float[CoastHarmonics];
            float[] weights = new float[CoastHarmonics];
            float total = 0f;
            float shortest = Mathf.Max(2f, settings.coastWavelength.x);
            float longest = Mathf.Max(shortest, settings.coastWavelength.y);
            for (int i = 0; i < CoastHarmonics; i++)
            {
                frequencies[i] = 2f * Mathf.PI / builder.Range(shortest, longest);
                phases[i] = builder.Range(0f, 2f * Mathf.PI);
                weights[i] = builder.Range(0.4f, 1f);
                total += weights[i];
            }

            return x =>
            {
                float sum = 0f;
                for (int i = 0; i < CoastHarmonics; i++)
                {
                    sum += weights[i] * Mathf.Sin(frequencies[i] * x + phases[i]);
                }

                return sum / total;
            };
        }

        /// <summary>
        /// Records for every ground cell how many steps (8 neighbours) it lies from the nearest cell of the coast (the
        /// beach or the ocean) and from the nearest cell of the ocean.
        /// </summary>
        private void MeasureDistances()
        {
            Measure(coastDistance, cell => sand.Contains(cell) || ocean.Contains(cell));
            Measure(seaDistance, ocean.Contains);
        }

        /// <summary>
        /// Fills <paramref name="distances"/> with each ground cell's steps (8 neighbours) from the nearest ground cell
        /// that passes <paramref name="source"/>.
        /// </summary>
        private void Measure(Dictionary<Vector3Int, int> distances, Predicate<Vector3Int> source)
        {
            Queue<Vector3Int> queue = new();
            foreach (Vector3Int cell in builder.OrderedGround)
            {
                if (source(cell))
                {
                    distances[cell] = 0;
                    queue.Enqueue(cell);
                }
            }

            while (queue.Count > 0)
            {
                Vector3Int cell = queue.Dequeue();
                int next = distances[cell] + 1;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        Vector3Int neighbour = cell + new Vector3Int(dx, dy, 0);
                        if (builder.Ground.Contains(neighbour) && !distances.ContainsKey(neighbour))
                        {
                            distances[neighbour] = next;
                            queue.Enqueue(neighbour);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Runs the stone road through five points from beyond the west edge to beyond the east edge, south of the
        /// middle, each at a random height within the meander, and paints it on the grass off the coast. Warns when a
        /// road cell lies within the road margin of the coast.
        /// </summary>
        private void LayRoad()
        {
            float middle = -settings.roadSouth * halfHeight;
            float meander = settings.roadMeander * halfHeight;
            Vector2[] points =
            {
                new(-halfWidth - 3f, middle + builder.Range(-1f, 1f) * meander),
                new(-halfWidth * 0.5f, middle + builder.Range(-1f, 1f) * meander),
                new(0f, middle + builder.Range(-1f, 1f) * meander),
                new(halfWidth * 0.5f, middle + builder.Range(-1f, 1f) * meander),
                new(halfWidth + 3f, middle + builder.Range(-1f, 1f) * meander)
            };
            AreaLayoutBuilder.SampleSpline(points, road);
            HashSet<Vector3Int> painted = new();
            builder.Paint(road, settings.roadWidth, painted);
            int close = 0;
            foreach (Vector3Int cell in builder.OrderedGround)
            {
                if (!painted.Contains(cell) || CoastDistance(cell) == 0)
                {
                    continue;
                }

                builder.Stone.Add(cell);
                if (CoastDistance(cell) <= settings.roadMargin)
                {
                    close++;
                }
            }

            if (close > 0)
            {
                builder.Layout.Warnings.Add($"{close} road cells lie within {settings.roadMargin} cells of the beach: make the area taller or the coast narrower.");
            }
        }

        /// <summary>
        /// Runs the gravel trail from the road to the beach: to the beach cell furthest north, in the column nearest a
        /// random point of the middle third, that keeps the sea gap from the sea. It leaves the road straight north for
        /// a few cells, bends once on the way, and crosses the beach's landward edge straight north too, so where it
        /// meets the road and the beach it steps nowhere: a step there would leave the road's or the beach's edge
        /// running on under the trail's rounded corner (Docs/TerrainTypes.md, Phase 3's Corners note). It is painted
        /// over the grass and the beach, keeping every trail cell that gap from the sea, so the trail ends on the sand
        /// with a row of beach between it and the water, since gravel beside the ocean would run on under it and show
        /// under its corners too. Warns when no beach cell is that far from the sea.
        /// </summary>
        private void LayTrail()
        {
            float targetX = builder.Range(-1f, 1f) * halfWidth / 3f;
            Vector3Int target = default;
            bool found = false;
            foreach (Vector3Int cell in builder.OrderedGround)
            {
                if (!sand.Contains(cell) || SeaDistance(cell) < SeaGap)
                {
                    continue;
                }

                float distance = Mathf.Abs(cell.x + 0.5f - targetX);
                float best = Mathf.Abs(target.x + 0.5f - targetX);
                if (!found || distance < best || (Mathf.Approximately(distance, best) && cell.y > target.y))
                {
                    target = cell;
                    found = true;
                }
            }

            if (!found || road.Count == 0)
            {
                builder.Layout.Warnings.Add("No beach cell lies far enough from the sea for the trail to end on it.");
                return;
            }

            Vector2 end = new(target.x + 0.5f, target.y + 0.5f);
            Vector2 start = TrailStart(end.x);

            int landward = target.y;
            foreach (Vector3Int cell in sand)
            {
                if (Mathf.Abs(cell.x - target.x) <= settings.trailWidth)
                {
                    landward = Mathf.Min(landward, cell.y);
                }
            }

            Vector2 leave = start + new Vector2(0f, TrailLeadIn);
            Vector2 approach = new(end.x, landward - TrailLeadIn + 0.5f);
            Vector2 bend = Vector2.Lerp(leave, approach, 0.5f) + new Vector2(builder.Range(-0.15f, 0.15f) * halfWidth, 0f);
            List<Vector2> samples = new();
            AreaLayoutBuilder.SampleSpline(new[] { start, leave }, samples);
            AreaLayoutBuilder.SampleSpline(new[] { leave, bend, approach }, samples);
            AreaLayoutBuilder.SampleSpline(new[] { approach, end }, samples);
            HashSet<Vector3Int> trail = new();
            builder.Paint(samples, settings.trailWidth, trail);
            foreach (Vector3Int cell in builder.OrderedGround)
            {
                if (trail.Contains(cell) && SeaDistance(cell) >= SeaGap && !builder.Stone.Contains(cell))
                {
                    builder.Gravel.Add(cell);
                }
            }
        }

        /// <summary>
        /// The road sample the trail leaves from: the one nearest <paramref name="x"/> where the road's northern edge
        /// runs level across the trail's columns and a cell beyond each side, so the trail meets it at no step; the
        /// nearest sample when the road is level nowhere.
        /// </summary>
        private Vector2 TrailStart(float x)
        {
            Dictionary<int, int> northEdge = new();
            foreach (Vector3Int cell in builder.Stone)
            {
                northEdge[cell.x] = northEdge.TryGetValue(cell.x, out int y) ? Mathf.Max(y, cell.y) : cell.y;
            }

            List<Vector2> byDistance = new(road);
            byDistance.Sort((a, b) => Mathf.Abs(a.x - x).CompareTo(Mathf.Abs(b.x - x)));
            int reach = settings.trailWidth / 2 + 2;
            foreach (Vector2 sample in byDistance)
            {
                int column = Mathf.FloorToInt(sample.x);
                bool level = northEdge.TryGetValue(column, out int edge);
                for (int dx = -reach; dx <= reach && level; dx++)
                {
                    level = northEdge.TryGetValue(column + dx, out int other) && other == edge;
                }

                if (level)
                {
                    return sample;
                }
            }

            return byDistance[0];
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
        /// Keeps blocking props off the beach and the sea.
        /// </summary>
        private void ReserveCoast()
        {
            foreach (Vector3Int cell in builder.OrderedGround)
            {
                if (CoastDistance(cell) == 0)
                {
                    builder.Reserved.Add(cell);
                }
            }
        }

        /// <summary>
        /// Scatters <paramref name="scatter"/>'s props over the grass cells of its zone off the beach and the sea.
        /// </summary>
        private void ScatterProps(Scatter scatter)
        {
            List<Vector3Int> candidates = new();
            foreach (Vector3Int cell in builder.OrderedGround)
            {
                if (CoastDistance(cell) > 0 && ZoneOf(cell) == scatter.Zone)
                {
                    candidates.Add(cell);
                }
            }

            builder.ScatterProps(scatter.Prefabs, scatter.Count, scatter.Spacing, candidates);
        }

        /// <summary>
        /// The zone of <paramref name="cell"/>: the border band along the edge, else the band of grass along the beach,
        /// else the interior.
        /// </summary>
        private Zone ZoneOf(Vector3Int cell)
        {
            if (builder.EdgeDistance(cell) <= settings.borderDepth)
            {
                return Zone.Border;
            }

            int distance = CoastDistance(cell);
            return distance > 0 && distance <= settings.shoreZoneDepth ? Zone.Shore : Zone.Interior;
        }

        /// <summary>
        /// The cells 4-connected to the spawn around the blocking footprints and the ocean, which cannot be walked.
        /// </summary>
        private HashSet<Vector3Int> Reachable()
        {
            return AreaLayoutBuilder.FloodFill(builder.Layout.SpawnCell, cell => builder.Ground.Contains(cell) && !ocean.Contains(cell) && !builder.Occupied.Contains(cell));
        }

        /// <summary>
        /// Spreads the mobs over the land (off the ocean) inside the border band that the spawn can walk to, away from props, at least the configured distance from the spawn and from each other. Warns when
        /// fewer fit than asked for.
        /// </summary>
        private void PlaceMobs(HashSet<Vector3Int> reachable)
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

            List<Vector3Int> candidates = builder.MobCells(reachable, settings.borderDepth, settings.mobDistanceFromSpawn);
            candidates.RemoveAll(ocean.Contains);
            int placed = builder.PlaceMobs(settings.mobPrefab, wanted, candidates, settings.mobSpacing, new List<Vector3Int>());
            if (placed < wanted)
            {
                builder.Layout.Warnings.Add($"Placed {placed} of {wanted} mobs: not enough open ground {settings.mobDistanceFromSpawn} cells from the spawn and {settings.mobSpacing} apart.");
            }
        }

        /// <summary>
        /// Warns when a beach or trail cell cannot be walked to from the spawn.
        /// </summary>
        private void CheckBeach(HashSet<Vector3Int> reachable)
        {
            int cut = 0;
            foreach (Vector3Int cell in builder.OrderedGround)
            {
                if ((sand.Contains(cell) || builder.Gravel.Contains(cell)) && !reachable.Contains(cell))
                {
                    cut++;
                }
            }

            if (cut > 0)
            {
                builder.Layout.Warnings.Add($"{cut} beach or trail cells cannot be walked to from the spawn.");
            }
        }

        /// <summary>
        /// Lays the campsite on the detail stream: a campfire and seats on a wobbly patch of dirt on interior grass,
        /// away from the paths, the spawn and the mobs (<see cref="AreaLayoutBuilder.LayCampsite"/>), its centre further
        /// from the beach and the paths than its radius plus two cells, so its dirt meets only grass and no third look
        /// shows under a rounded corner.
        /// </summary>
        private void LayCampsite()
        {
            if (settings.campfirePrefab == null)
            {
                return;
            }

            float clear = settings.campsiteRadius + CampsiteCoastMargin;
            HashSet<Vector3Int> nearPaths = AreaLayoutBuilder.CellsNear(PathCells(), Mathf.CeilToInt(clear));
            AreaLayoutBuilder.Campsite campsite = new(settings.campfirePrefab, settings.campsiteSeats, settings.seatCount, settings.seatSpacing, settings.campsiteRadius);
            builder.LayCampsite(campsite, dirt, settings.borderDepth, settings.pathClearance, settings.spawnClearRadius, cell => CoastDistance(cell) > clear && !nearPaths.Contains(cell));
        }

        /// <summary>
        /// The road's and the trail's cells, in ground order.
        /// </summary>
        private List<Vector3Int> PathCells()
        {
            List<Vector3Int> cells = new();
            foreach (Vector3Int cell in builder.OrderedGround)
            {
                if (builder.IsPath(cell))
                {
                    cells.Add(cell);
                }
            }

            return cells;
        }

        /// <summary>
        /// The steps <paramref name="distances"/> holds for <paramref name="cell"/>, and int.MaxValue for a cell it does
        /// not hold.
        /// </summary>
        private static int Steps(Dictionary<Vector3Int, int> distances, Vector3Int cell)
        {
            return distances.TryGetValue(cell, out int distance) ? distance : int.MaxValue;
        }

        /// <summary>
        /// How many steps (8 neighbours) <paramref name="cell"/> lies from the beach or the sea: 0 on them, and
        /// int.MaxValue off the ground.
        /// </summary>
        private int CoastDistance(Vector3Int cell)
        {
            return Steps(coastDistance, cell);
        }

        /// <summary>
        /// How many steps (8 neighbours) <paramref name="cell"/> lies from the ocean: 0 on it, and int.MaxValue off the
        /// ground.
        /// </summary>
        private int SeaDistance(Vector3Int cell)
        {
            return Steps(seaDistance, cell);
        }
    }
}
