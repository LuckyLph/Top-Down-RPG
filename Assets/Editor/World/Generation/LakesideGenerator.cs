using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lays out a lakeside: a grass blob ringed by trees, with a lake in its northern half, a sandy beach on the lake's
/// southern shore and a muddy marsh around the rest of it, a stone road crossing the south from west to east, a gravel
/// trail from the road down to the beach, and patches of mud and of dry grass on the open grass. The party spawns on
/// the road. Blocking props stay off the water, the beach, the mud, the paths, a clearance beside them and the ground
/// around the spawn; every mob, and the beach, can be walked to from the spawn.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Lakeside Generator", fileName = "Generator_Lakeside")]
public sealed class LakesideGenerator : AreaGenerator
{
    private const int LakeSearchMargin = 2;

    /// <summary>
    /// The part of the lakeside a scatter layer fills: the band along its edge, the grass near the lake's shore, or
    /// the rest of the ground inside the band.
    /// </summary>
    public enum Zone
    {
        Border,
        Interior,
        Shore
    }

    /// <summary>
    /// Props scattered over one zone of the lakeside.
    /// </summary>
    [Serializable]
    public class Scatter : PropScatter
    {
        [SerializeField] private Zone zone = Zone.Interior;

        public Zone Zone => zone;
    }

    [Header("Terrain")]
    [SerializeField] private TerrainLayer grass = new();
    [Tooltip("Straw-coloured grass in patches away from the lake, drawn over the grass without an edge.")]
    [SerializeField] private TerrainLayer dryGrass = new();
    [SerializeField] private TerrainLayer sand = new();
    [SerializeField] private TerrainLayer mud = new();
    [SerializeField] private TerrainLayer gravel = new();
    [SerializeField] private TerrainLayer stone = new();
    [Tooltip("Drawn over every other layer, so it decides its cells: water cannot be walked.")]
    [SerializeField] private TerrainLayer water = new();

    [Header("Shape")]
    [Tooltip("Width and height of the area in cells, before its outline wobbles.")]
    [SerializeField] private Vector2Int size = new(64, 46);
    [Tooltip("Exponent of the outline's superellipse: 2 is an ellipse, higher is closer to a rectangle.")]
    [SerializeField, Range(2f, 6f)] private float squareness = 3f;
    [Tooltip("How far the outline wobbles in and out, as a fraction of its radius.")]
    [SerializeField, Range(0f, 0.3f)] private float edgeWobble = 0.12f;

    [Header("Lake")]
    [Tooltip("Width and height of the lake in cells, before its outline wobbles.")]
    [SerializeField] private Vector2Int lakeSize = new(26, 13);
    [Tooltip("How far north of the middle the lake lies, as a fraction of the area's half height.")]
    [SerializeField, Range(0f, 0.6f)] private float lakeNorth = 0.35f;
    [Tooltip("How far the lake's outline wobbles in and out, as a fraction of its radius.")]
    [SerializeField, Range(0f, 0.4f)] private float lakeWobble = 0.2f;
    [Tooltip("Fewest water cells a lake must keep; fewer gives a warning.")]
    [SerializeField, Min(1)] private int minLakeCells = 120;
    [Tooltip("Smallest distance in cells from the water to the road.")]
    [SerializeField, Min(1)] private int roadMargin = 5;

    [Header("Shore")]
    [Tooltip("How far around the lake, as an angle from straight south in degrees, the beach reaches on either side; the rest of the shore is marsh.")]
    [SerializeField, Range(0f, 180f)] private float beachSpread = 70f;
    [Tooltip("Width in cells of the beach along the water.")]
    [SerializeField, Min(1)] private int beachWidth = 3;
    [Tooltip("Width in cells of the marsh's mud along the water, on average.")]
    [SerializeField, Min(1)] private int marshWidth = 2;
    [Tooltip("How much the beach's and the marsh's widths swell and thin around the lake, as a fraction of their widths.")]
    [SerializeField, Range(0f, 1f)] private float shoreWobble = 0.6f;
    [Tooltip("Mud patches spread around the marsh side of the lake, beyond the shore.")]
    [SerializeField, Min(0)] private int marshPatchCount = 4;
    [Tooltip("Mud patches on the open grass away from the lake.")]
    [SerializeField, Min(0)] private int mudPatchCount = 3;
    [Tooltip("Smallest and largest radius in cells of a mud patch.")]
    [SerializeField] private Vector2 mudPatchRadius = new(1.5f, 3.5f);
    [Tooltip("Dry grass patches on the open grass away from the lake.")]
    [SerializeField, Min(0)] private int dryGrassPatchCount = 5;
    [Tooltip("Smallest and largest radius in cells of a dry grass patch.")]
    [SerializeField] private Vector2 dryGrassPatchRadius = new(2.5f, 5f);
    [Tooltip("Depth in cells of the band of grass around the shore that Shore layers fill.")]
    [SerializeField, Min(1)] private int shoreZoneDepth = 3;

    [Header("Paths")]
    [Tooltip("Width in cells of the stone road crossing the south of the area from west to east.")]
    [SerializeField, Min(1)] private int roadWidth = 3;
    [Tooltip("How far south of the middle the road runs, as a fraction of the area's half height.")]
    [SerializeField, Range(0f, 0.8f)] private float roadSouth = 0.5f;
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
    [Tooltip("Depth in cells of the band along the edge that Border layers fill; the lake keeps out of it.")]
    [SerializeField, Min(1)] private int borderDepth = 3;
    [Tooltip("Scatter layers, placed in this order: earlier layers take the room first.")]
    [SerializeField] private List<Scatter> scatters = new();

    [Header("Mobs")]
    [SerializeField] private MobController mobPrefab;
    [SerializeField, Min(0)] private int mobCount = 4;
    [Tooltip("Smallest distance in cells from the party spawn to a mob spawn.")]
    [SerializeField, Min(0f)] private float mobDistanceFromSpawn = 14f;
    [Tooltip("Smallest distance in cells between two mob spawns.")]
    [SerializeField, Min(0f)] private float mobSpacing = 10f;

    public int BorderDepth => borderDepth;
    public int DryGrassPatchCount => dryGrassPatchCount;
    public int PathClearance => pathClearance;
    public int RoadMargin => roadMargin;
    public int SpawnClearRadius => spawnClearRadius;
    public MobController MobPrefab => mobPrefab;
    public int MobCount => mobCount;
    public float MobDistanceFromSpawn => mobDistanceFromSpawn;
    public float MobSpacing => mobSpacing;

    /// <summary>
    /// Grass, dry grass, sand, mud, gravel, stone, then water, in the style's layer order: each layer is drawn over the
    /// ones before it and decides its cells, so the road and the trail cross mud and sand and the lake covers everything
    /// under it.
    /// </summary>
    public override IReadOnlyList<TerrainLayer> TerrainLayers => new[] { grass, dryGrass, sand, mud, gravel, stone, water };

    /// <summary>
    /// Lays out a lakeside from <paramref name="layoutSeed"/>.
    /// </summary>
    public override AreaLayout Generate(int layoutSeed)
    {
        return new Run(this, layoutSeed).Build();
    }

    /// <summary>
    /// The state of one generation: the shared builder with the cells laid so far, the road's samples, the lake's
    /// centre, and how many steps each dry grass cell lies from the water.
    /// </summary>
    private sealed class Run
    {
        private readonly LakesideGenerator settings;
        private readonly AreaLayoutBuilder builder;
        private readonly HashSet<Vector3Int> water;
        private readonly HashSet<Vector3Int> sand;
        private readonly HashSet<Vector3Int> mud;
        private readonly HashSet<Vector3Int> dryGrass;
        private readonly List<Vector2> road = new();
        private readonly Dictionary<Vector3Int, int> waterDistance = new();
        private readonly float halfWidth;
        private readonly float halfHeight;
        private Vector2 lakeCentre;

        /// <summary>
        /// Prepares a generation of <paramref name="generator"/>'s settings from <paramref name="seed"/>.
        /// </summary>
        public Run(LakesideGenerator generator, int seed)
        {
            settings = generator;
            builder = new AreaLayoutBuilder(seed, generator.TerrainLayers, generator.grass, generator.gravel, generator.stone);
            water = builder.Layout.TerrainCells(generator.water);
            sand = builder.Layout.TerrainCells(generator.sand);
            mud = builder.Layout.TerrainCells(generator.mud);
            dryGrass = builder.Layout.TerrainCells(generator.dryGrass);
            halfWidth = Mathf.Max(8, generator.size.x) * 0.5f;
            halfHeight = Mathf.Max(8, generator.size.y) * 0.5f;
        }

        /// <summary>
        /// Lays the grass, the road, the lake, its beach and marsh, the trail to the beach and the mud patches, picks
        /// the spawn, keeps blocking props off the open ground, scatters the props layer by layer, spreads the mobs,
        /// checks that the beach can be walked to, then lays the dry grass from the detail stream, so it moves nothing
        /// laid before it.
        /// </summary>
        public AreaLayout Build()
        {
            builder.LayGrass(new Vector2(halfWidth, halfHeight), settings.squareness, settings.edgeWobble);
            LayRoad();
            LayLake();
            MeasureWaterDistance();
            LayShore();
            LayTrail();
            LayMarshPatches();
            LayMudPatches();
            PlaceSpawn();
            builder.ReservePathClearance(settings.pathClearance);
            builder.ReserveDisc(builder.Layout.SpawnCell, settings.spawnClearRadius);
            ReserveOpenGround();
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
            LayDryGrass();
            return builder.Layout;
        }

        /// <summary>
        /// Runs the stone road through five points from beyond the west edge to beyond the east edge, south of the
        /// middle, each at a random height within the meander, and paints it on the grass.
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
            builder.Paint(road, settings.roadWidth, builder.Stone);
        }

        /// <summary>
        /// Fills a wobbly blob of the lake's size north of the middle with water, keeping it inside the border band
        /// and the road margin, then keeps only its largest 4-connected region. Warns when it keeps too few cells.
        /// </summary>
        private void LayLake()
        {
            lakeCentre = new Vector2(builder.Range(-0.2f, 0.2f) * halfWidth, settings.lakeNorth * halfHeight);
            Vector2 halfLake = new(Mathf.Max(3, settings.lakeSize.x) * 0.5f, Mathf.Max(3, settings.lakeSize.y) * 0.5f);
            Predicate<Vector2> contains = builder.WobblyShape(lakeCentre, halfLake, 2.5f, settings.lakeWobble);
            HashSet<Vector3Int> nearRoad = CellsNear(builder.Stone, settings.roadMargin);
            List<Vector3Int> inside = new();
            foreach (Vector3Int cell in builder.OrderedGrass)
            {
                if (builder.EdgeDistance(cell) > settings.borderDepth + LakeSearchMargin && !nearRoad.Contains(cell)
                    && contains(new Vector2(cell.x + 0.5f, cell.y + 0.5f)))
                {
                    inside.Add(cell);
                }
            }

            HashSet<Vector3Int> candidates = new(inside);
            HashSet<Vector3Int> largest = new();
            foreach (Vector3Int start in inside)
            {
                if (!candidates.Contains(start))
                {
                    continue;
                }

                HashSet<Vector3Int> region = AreaLayoutBuilder.FloodFill(start, candidates.Contains);
                candidates.ExceptWith(region);
                if (region.Count > largest.Count)
                {
                    largest = region;
                }
            }

            water.UnionWith(largest);
            if (water.Count < settings.minLakeCells)
            {
                builder.Layout.Warnings.Add($"The lake kept {water.Count} cells, fewer than {settings.minLakeCells}: make the area larger or the lake smaller.");
            }
        }

        /// <summary>
        /// Records for every dry grass cell how many steps (8 neighbours) it lies from the nearest water cell.
        /// </summary>
        private void MeasureWaterDistance()
        {
            Queue<Vector3Int> queue = new();
            foreach (Vector3Int cell in builder.OrderedGrass)
            {
                if (water.Contains(cell))
                {
                    waterDistance[cell] = 0;
                    queue.Enqueue(cell);
                }
            }

            while (queue.Count > 0)
            {
                Vector3Int cell = queue.Dequeue();
                int next = waterDistance[cell] + 1;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        Vector3Int neighbour = cell + new Vector3Int(dx, dy, 0);
                        if (builder.Grass.Contains(neighbour) && !waterDistance.ContainsKey(neighbour))
                        {
                            waterDistance[neighbour] = next;
                            queue.Enqueue(neighbour);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Lays the shore: the dry cells within the beach width of the water, facing south within the beach spread,
        /// are sand; the dry cells within the marsh width of the water elsewhere are mud. Both widths swell and thin
        /// around the lake with two random harmonics of the angle from its centre, by up to the shore wobble, and are
        /// at least a cell. Path cells stay as they are.
        /// </summary>
        private void LayShore()
        {
            float cosSpread = Mathf.Cos(settings.beachSpread * Mathf.Deg2Rad);
            float firstPhase = builder.Range(0f, 2f * Mathf.PI);
            float secondPhase = builder.Range(0f, 2f * Mathf.PI);
            foreach (Vector3Int cell in builder.OrderedGrass)
            {
                int distance = WaterDistance(cell);
                if (distance <= 0 || builder.IsPath(cell))
                {
                    continue;
                }

                Vector2 fromCentre = new Vector2(cell.x + 0.5f, cell.y + 0.5f) - lakeCentre;
                bool facesSouth = fromCentre.sqrMagnitude > 0f && Vector2.Dot(fromCentre.normalized, Vector2.down) >= cosSpread;
                float angle = Mathf.Atan2(fromCentre.y, fromCentre.x);
                float swell = 1f + settings.shoreWobble * (0.6f * Mathf.Sin(3f * angle + firstPhase) + 0.4f * Mathf.Sin(5f * angle + secondPhase));
                if (facesSouth && distance <= Mathf.Max(1, Mathf.RoundToInt(settings.beachWidth * swell)))
                {
                    sand.Add(cell);
                }
                else if (!facesSouth && distance <= Mathf.Max(1, Mathf.RoundToInt(settings.marshWidth * swell)))
                {
                    mud.Add(cell);
                }
            }
        }

        /// <summary>
        /// Runs the gravel trail from the road to the beach cell nearest the lake's south point that does not touch
        /// the water, bending once on the way, and paints it on the dry grass.
        /// </summary>
        private void LayTrail()
        {
            if (sand.Count == 0 || road.Count == 0)
            {
                builder.Layout.Warnings.Add("The lake has no beach, so no trail leads to it.");
                return;
            }

            Vector3Int target = default;
            float best = float.MaxValue;
            Vector2 south = lakeCentre + new Vector2(0f, -settings.lakeSize.y * 0.5f);
            foreach (Vector3Int cell in builder.OrderedGrass)
            {
                if (sand.Contains(cell) && !IsBesideWater(cell))
                {
                    float distance = (new Vector2(cell.x + 0.5f, cell.y + 0.5f) - south).sqrMagnitude;
                    if (distance < best)
                    {
                        best = distance;
                        target = cell;
                    }
                }
            }

            if (best == float.MaxValue)
            {
                builder.Layout.Warnings.Add("The beach is too narrow for the trail to end on it.");
                return;
            }

            Vector2 end = new(target.x + 0.5f, target.y + 0.5f);
            Vector2 start = road[0];
            foreach (Vector2 sample in road)
            {
                if (Mathf.Abs(sample.x - end.x) < Mathf.Abs(start.x - end.x))
                {
                    start = sample;
                }
            }

            Vector2 bend = Vector2.Lerp(start, end, 0.5f) + new Vector2(builder.Range(-0.15f, 0.15f) * halfWidth, 0f);
            List<Vector2> samples = new();
            AreaLayoutBuilder.SampleSpline(new[] { start, bend, end }, samples);
            HashSet<Vector3Int> trail = new();
            builder.Paint(samples, settings.trailWidth, trail);
            foreach (Vector3Int cell in builder.OrderedGrass)
            {
                if (trail.Contains(cell) && !water.Contains(cell) && !builder.Stone.Contains(cell))
                {
                    builder.Gravel.Add(cell);
                }
            }
        }

        /// <summary>
        /// Adds the marsh patches: wobbly discs of mud centred on random marsh cells, so the marsh spreads beyond the
        /// shore in places.
        /// </summary>
        private void LayMarshPatches()
        {
            List<Vector3Int> marsh = new();
            foreach (Vector3Int cell in builder.OrderedGrass)
            {
                if (mud.Contains(cell))
                {
                    marsh.Add(cell);
                }
            }

            for (int i = 0; i < settings.marshPatchCount && marsh.Count > 0; i++)
            {
                PaintMudPatch(marsh[builder.Next(marsh.Count)]);
            }
        }

        /// <summary>
        /// Adds the mud patches on the open grass: wobbly discs centred on random interior cells away from the lake,
        /// the beach and the spawn's surroundings on the road.
        /// </summary>
        private void LayMudPatches()
        {
            List<Vector3Int> open = new();
            int away = settings.shoreZoneDepth + settings.marshWidth + 2;
            foreach (Vector3Int cell in builder.OrderedGrass)
            {
                if (builder.EdgeDistance(cell) > settings.borderDepth + 2 && WaterDistance(cell) > away && !builder.IsNearPath(cell))
                {
                    open.Add(cell);
                }
            }

            for (int i = 0; i < settings.mudPatchCount && open.Count > 0; i++)
            {
                PaintMudPatch(open[builder.Next(open.Count)]);
            }
        }

        /// <summary>
        /// Paints mud in a wobbly disc of a random radius around <paramref name="centre"/>, on dry grass inside the
        /// border band that is neither beach nor path.
        /// </summary>
        private void PaintMudPatch(Vector3Int centre)
        {
            PaintPatch(centre, settings.mudPatchRadius, mud, cell => builder.EdgeDistance(cell) > settings.borderDepth);
        }

        /// <summary>
        /// Lays the dry grass patches from the detail stream: wobbly discs centred on random open grass cells inside
        /// the edge band, away from the lake and its shore and off the paths, on grass that is not water or beach and
        /// not on or beside mud or a path. Mud and the paths are drawn over dry grass, so the renderer takes dry grass to
        /// go on under them; beside a mud or path cell that has only grass under it, the dry grass would show under its
        /// rounded corners where the two meet grass. Props stand on it as on grass.
        /// </summary>
        private void LayDryGrass()
        {
            builder.StartDetailStream();
            HashSet<Vector3Int> nearMud = CellsNear(mud, 1);
            List<Vector3Int> open = new();
            int away = settings.shoreZoneDepth + Mathf.Max(settings.beachWidth, settings.marshWidth) + 2;
            foreach (Vector3Int cell in builder.OrderedGrass)
            {
                if (IsOpenGrass(cell) && !nearMud.Contains(cell) && builder.EdgeDistance(cell) > settings.borderDepth && WaterDistance(cell) > away && !builder.IsNearPath(cell))
                {
                    open.Add(cell);
                }
            }

            for (int i = 0; i < settings.dryGrassPatchCount && open.Count > 0; i++)
            {
                PaintPatch(open[builder.Next(open.Count)], settings.dryGrassPatchRadius, dryGrass, cell => !nearMud.Contains(cell) && !builder.IsNearPath(cell));
            }
        }

        /// <summary>
        /// Whether <paramref name="cell"/> is grass with no water, beach, mud or path on it.
        /// </summary>
        private bool IsOpenGrass(Vector3Int cell)
        {
            return builder.Grass.Contains(cell) && !water.Contains(cell) && !sand.Contains(cell) && !mud.Contains(cell) && !builder.IsPath(cell);
        }

        /// <summary>
        /// Paints <paramref name="target"/> in a wobbly disc of a random radius between the two values of
        /// <paramref name="radii"/> around <paramref name="centre"/>, on dry grass that is neither beach nor path and that
        /// passes <paramref name="allowed"/>.
        /// </summary>
        private void PaintPatch(Vector3Int centre, Vector2 radii, HashSet<Vector3Int> target, Predicate<Vector3Int> allowed)
        {
            float radius = builder.Range(radii.x, Mathf.Max(radii.x, radii.y));
            Vector2 middle = new(centre.x + 0.5f, centre.y + 0.5f);
            Predicate<Vector2> contains = builder.WobblyShape(middle, new Vector2(radius, radius * 0.8f), 2f, 0.25f);
            int reach = Mathf.CeilToInt(radius * 1.3f) + 1;
            for (int dy = -reach; dy <= reach; dy++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    Vector3Int cell = centre + new Vector3Int(dx, dy, 0);
                    bool free = builder.Grass.Contains(cell) && !water.Contains(cell) && !sand.Contains(cell) && !builder.IsPath(cell);
                    if (free && allowed(cell) && contains(new Vector2(cell.x + 0.5f, cell.y + 0.5f)))
                    {
                        target.Add(cell);
                    }
                }
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
        /// Keeps blocking props off the water, the beach and the mud.
        /// </summary>
        private void ReserveOpenGround()
        {
            foreach (Vector3Int cell in builder.OrderedGrass)
            {
                if (water.Contains(cell) || sand.Contains(cell) || mud.Contains(cell))
                {
                    builder.Reserved.Add(cell);
                }
            }
        }

        /// <summary>
        /// Scatters <paramref name="scatter"/>'s props over the dry grass cells of its zone that are neither beach nor
        /// mud and do not touch the water.
        /// </summary>
        private void ScatterProps(Scatter scatter)
        {
            List<Vector3Int> candidates = new();
            foreach (Vector3Int cell in builder.OrderedGrass)
            {
                int distance = WaterDistance(cell);
                if (IsBesideWater(cell) || sand.Contains(cell) || mud.Contains(cell) || ZoneOf(cell) != scatter.Zone)
                {
                    continue;
                }

                candidates.Add(cell);
            }

            builder.ScatterProps(scatter.Prefabs, scatter.Count, scatter.Spacing, candidates);
        }

        /// <summary>
        /// The zone of <paramref name="cell"/>: the border band along the edge, else the shore band around the lake's
        /// water, beach and marsh, else the interior.
        /// </summary>
        private Zone ZoneOf(Vector3Int cell)
        {
            if (builder.EdgeDistance(cell) <= settings.borderDepth)
            {
                return Zone.Border;
            }

            int distance = WaterDistance(cell);
            int shore = Mathf.Max(settings.beachWidth, settings.marshWidth) + settings.shoreZoneDepth;
            return distance > 0 && distance <= shore ? Zone.Shore : Zone.Interior;
        }

        /// <summary>
        /// The dry cells 4-connected to the spawn around the blocking footprints, like the navigation grid's regions:
        /// the water cannot be walked.
        /// </summary>
        private HashSet<Vector3Int> Reachable()
        {
            return AreaLayoutBuilder.FloodFill(builder.Layout.SpawnCell, cell => builder.Grass.Contains(cell) && !water.Contains(cell) && !builder.Occupied.Contains(cell));
        }

        /// <summary>
        /// Spreads the mobs over dry cells inside the border band that the spawn can walk to, away from props, at
        /// least the configured distance from the spawn and from each other. Warns when fewer fit than asked for.
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
            int placed = builder.PlaceMobs(settings.mobPrefab, wanted, candidates, settings.mobSpacing, new List<Vector3Int>());
            if (placed < wanted)
            {
                builder.Layout.Warnings.Add($"Placed {placed} of {wanted} mobs: not enough open ground {settings.mobDistanceFromSpawn} cells from the spawn and {settings.mobSpacing} apart.");
            }
        }

        /// <summary>
        /// Warns when a beach cell or a trail cell cannot be walked to from the spawn.
        /// </summary>
        private void CheckBeach(HashSet<Vector3Int> reachable)
        {
            int cut = 0;
            foreach (Vector3Int cell in builder.OrderedGrass)
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
        /// Whether <paramref name="cell"/> or one of its 8 neighbours is water.
        /// </summary>
        private bool IsBesideWater(Vector3Int cell)
        {
            return WaterDistance(cell) <= 1;
        }

        /// <summary>
        /// How many steps (8 neighbours) <paramref name="cell"/> lies from the water: 0 on it, and int.MaxValue off
        /// the grass or with no lake.
        /// </summary>
        private int WaterDistance(Vector3Int cell)
        {
            return waterDistance.TryGetValue(cell, out int distance) ? distance : int.MaxValue;
        }

        /// <summary>
        /// Every cell within <paramref name="steps"/> steps (8 neighbours) of a cell of <paramref name="cells"/>.
        /// </summary>
        private static HashSet<Vector3Int> CellsNear(HashSet<Vector3Int> cells, int steps)
        {
            HashSet<Vector3Int> near = new();
            foreach (Vector3Int cell in cells)
            {
                for (int dy = -steps; dy <= steps; dy++)
                {
                    for (int dx = -steps; dx <= steps; dx++)
                    {
                        near.Add(cell + new Vector3Int(dx, dy, 0));
                    }
                }
            }

            return near;
        }
    }
}
