using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lays out a snowfield: a field of snow ringed by pines, with a frozen lake of ice in its northern half and a hole of
/// open water in the ice, a stone road crossing the south from west to east, a gravel trail from the road towards the
/// lake that stops short of the ice, and a campsite (a campfire and seats on a patch of trodden dirt) on the open snow.
/// The party spawns on the road. Blocking props stay off the ice and the snow beside it, the paths, a clearance beside
/// them and the ground around the spawn; every mob, and the trail, can be walked to from the spawn. Snow is the
/// builder's ground, the layer every other one lies on, as grass is in the other generators.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Snowfield Generator", fileName = "Generator_Snowfield")]
public sealed class SnowfieldGenerator : AreaGenerator
{
    private const int LakeSearchMargin = 2;
    private const int MinHolePiece = 4;
    private const int IceGap = 2;
    private const int CampsiteIceMargin = 2;

    /// <summary>
    /// The part of the snowfield a scatter layer fills: the band along its edge, the snow near the frozen lake, or the
    /// rest of the ground inside the band.
    /// </summary>
    public enum Zone
    {
        Border,
        Interior,
        Shore
    }

    /// <summary>
    /// Props scattered over one zone of the snowfield.
    /// </summary>
    [Serializable]
    public class Scatter : PropScatter
    {
        [SerializeField] private Zone zone = Zone.Interior;

        public Zone Zone => zone;
    }

    [Header("Terrain")]
    [Tooltip("The ground: the snow every other layer lies on.")]
    [SerializeField] private TerrainLayer snow = new();
    [Tooltip("Trodden earth around the campsite.")]
    [SerializeField] private TerrainLayer dirt = new();
    [Tooltip("The frozen lake, drawn over the snow.")]
    [SerializeField] private TerrainLayer ice = new();
    [SerializeField] private TerrainLayer gravel = new();
    [SerializeField] private TerrainLayer stone = new();
    [Tooltip("Open water in holes in the ice, drawn over it, so it decides its cells: water cannot be walked.")]
    [SerializeField] private TerrainLayer water = new();

    [Header("Shape")]
    [Tooltip("Width and height of the area in cells, before its outline wobbles.")]
    [SerializeField] private Vector2Int size = new(60, 44);
    [Tooltip("Exponent of the outline's superellipse: 2 is an ellipse, higher is closer to a rectangle.")]
    [SerializeField, Range(2f, 6f)] private float squareness = 3f;
    [Tooltip("How far the outline wobbles in and out, as a fraction of its radius.")]
    [SerializeField, Range(0f, 0.3f)] private float edgeWobble = 0.12f;

    [Header("Frozen lake")]
    [Tooltip("Width and height of the frozen lake in cells, before its outline wobbles.")]
    [SerializeField] private Vector2Int lakeSize = new(24, 12);
    [Tooltip("How far north of the middle the lake lies, as a fraction of the area's half height.")]
    [SerializeField, Range(0f, 0.6f)] private float lakeNorth = 0.35f;
    [Tooltip("How far the lake's outline wobbles in and out, as a fraction of its radius.")]
    [SerializeField, Range(0f, 0.4f)] private float lakeWobble = 0.2f;
    [Tooltip("Fewest ice cells the lake must keep; fewer gives a warning.")]
    [SerializeField, Min(1)] private int minLakeCells = 100;
    [Tooltip("Smallest distance in cells from the ice to the road.")]
    [SerializeField, Min(1)] private int roadMargin = 5;
    [Tooltip("Depth in cells of the band of snow around the lake that Shore layers fill.")]
    [SerializeField, Min(1)] private int shoreZoneDepth = 3;

    [Header("Holes")]
    [Tooltip("Holes of open water in the ice; 0 for none.")]
    [SerializeField, Min(0)] private int holeCount = 1;
    [Tooltip("Smallest and largest radius in cells of a hole.")]
    [SerializeField] private Vector2 holeRadius = new(1.5f, 2.2f);
    [Tooltip("Cells of ice kept all round a hole, so it meets only ice.")]
    [SerializeField, Min(1)] private int holeMargin = 2;

    [Header("Paths")]
    [Tooltip("Width in cells of the stone road crossing the south of the area from west to east.")]
    [SerializeField, Min(1)] private int roadWidth = 3;
    [Tooltip("How far south of the middle the road runs, as a fraction of the area's half height.")]
    [SerializeField, Range(0f, 0.8f)] private float roadSouth = 0.5f;
    [Tooltip("How far the road wanders north and south, as a fraction of the area's half height.")]
    [SerializeField, Range(0f, 0.4f)] private float roadMeander = 0.12f;
    [Tooltip("Width in cells of the gravel trail from the road towards the lake.")]
    [SerializeField, Min(1)] private int trailWidth = 2;
    [Tooltip("Cells beside every path kept free of blocking props.")]
    [SerializeField, Min(0)] private int pathClearance = 1;

    [Header("Spawn")]
    [Tooltip("Where along the road the party arrives, from -1 (west edge) to 1 (east edge).")]
    [SerializeField, Range(-0.9f, 0.9f)] private float spawnAlongRoad = -0.55f;
    [Tooltip("Radius in cells around the spawn that stays on snow and free of blocking props.")]
    [SerializeField, Min(0)] private int spawnClearRadius = 6;

    [Header("Props")]
    [Tooltip("Depth in cells of the band along the edge that Border layers fill; the lake keeps out of it.")]
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
    public int RoadMargin => roadMargin;
    public int HoleCount => holeCount;
    public int HoleMargin => holeMargin;
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
    /// Snow, dirt, ice, gravel, stone, then water, in the style's layer order: each layer is drawn over the ones before
    /// it and decides its cells, so the lake lies on the snow, the paths cross the snow, and a hole of water is drawn
    /// over the ice around it.
    /// </summary>
    public override IReadOnlyList<TerrainLayer> TerrainLayers => new[] { snow, dirt, ice, gravel, stone, water };

    public override IReadOnlyList<PropScatter> Scatters => scatters;

    /// <summary>
    /// Lays out a snowfield from <paramref name="layoutSeed"/>, its scatter layers placing props unless
    /// <paramref name="scatter"/> is false.
    /// </summary>
    public override AreaLayout Generate(int layoutSeed, bool scatter)
    {
        return new Run(this, layoutSeed, scatter).Build();
    }

    /// <summary>
    /// The state of one generation: the shared builder with the cells laid so far, the road's samples, the lake's
    /// centre and how many steps each snow cell lies from the ice.
    /// </summary>
    private sealed class Run
    {
        private readonly SnowfieldGenerator settings;
        private readonly AreaLayoutBuilder builder;
        private readonly HashSet<Vector3Int> ice;
        private readonly HashSet<Vector3Int> water;
        private readonly HashSet<Vector3Int> dirt;
        private readonly List<Vector2> road = new();
        private Dictionary<Vector3Int, int> iceDistance = new(CellComparer.Instance);
        private readonly float halfWidth;
        private readonly float halfHeight;
        private Vector2 lakeCentre;

        /// <summary>
        /// Prepares a generation of <paramref name="generator"/>'s settings from <paramref name="seed"/>, its scatter
        /// layers placing props unless <paramref name="scatter"/> is false.
        /// </summary>
        public Run(SnowfieldGenerator generator, int seed, bool scatter)
        {
            settings = generator;
            builder = new AreaLayoutBuilder(seed, generator.TerrainLayers, generator.snow, generator.gravel, generator.stone);
            builder.ScattersProps = scatter;
            ice = builder.Layout.TerrainCells(generator.ice);
            water = builder.Layout.TerrainCells(generator.water);
            dirt = builder.Layout.TerrainCells(generator.dirt);
            halfWidth = Mathf.Max(8, generator.size.x) * 0.5f;
            halfHeight = Mathf.Max(8, generator.size.y) * 0.5f;
        }

        /// <summary>
        /// Lays the snow, the road, the frozen lake and the trail towards it, picks the spawn, keeps blocking props off
        /// the ice and its shore, scatters the props layer by layer, spreads the mobs, checks that the trail can be
        /// walked to, then lays the holes in the ice and the campsite from the detail stream, so they move nothing laid
        /// before them.
        /// </summary>
        public AreaLayout Build()
        {
            builder.LayGround(new Vector2(halfWidth, halfHeight), settings.squareness, settings.edgeWobble);
            LayRoad();
            LayLake();
            iceDistance = builder.StepsFrom(ice.Contains);
            LayTrail();
            PlaceSpawn();
            builder.ReservePathClearance(settings.pathClearance);
            builder.ReserveDisc(builder.Layout.SpawnCell, settings.spawnClearRadius);
            ReserveLake();
            foreach (Scatter scatter in settings.scatters)
            {
                if (scatter != null)
                {
                    ScatterProps(scatter);
                }
            }

            HashSet<Vector3Int> reachable = builder.ReachableFromSpawn();
            PlaceMobs(reachable);
            CheckTrail(reachable);
            builder.StartDetailStream();
            LayHoles();
            LayCampsite();
            return builder.Layout;
        }

        /// <summary>
        /// Runs the stone road through five points from beyond the west edge to beyond the east edge, south of the
        /// middle, each at a random height within the meander, and paints it on the snow.
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
        /// Fills a wobbly blob of the lake's size north of the middle with ice, keeping it inside the border band and
        /// the road margin, then keeps only its largest 4-connected region. The snow goes on under the ice. Warns when
        /// it keeps too few cells.
        /// </summary>
        private void LayLake()
        {
            lakeCentre = new Vector2(builder.Range(-0.2f, 0.2f) * halfWidth, settings.lakeNorth * halfHeight);
            Vector2 halfLake = new(Mathf.Max(3, settings.lakeSize.x) * 0.5f, Mathf.Max(3, settings.lakeSize.y) * 0.5f);
            Predicate<Vector2> contains = builder.WobblyShape(lakeCentre, halfLake, 2.5f, settings.lakeWobble);
            HashSet<Vector3Int> nearRoad = AreaLayoutBuilder.CellsNear(builder.Stone, settings.roadMargin);
            List<Vector3Int> inside = new();
            foreach (Vector3Int cell in builder.OrderedGround)
            {
                if (builder.EdgeDistance(cell) > settings.borderDepth + LakeSearchMargin && !nearRoad.Contains(cell)
                    && contains(new Vector2(cell.x + 0.5f, cell.y + 0.5f)))
                {
                    inside.Add(cell);
                }
            }

            HashSet<Vector3Int> candidates = new(inside, CellComparer.Instance);
            HashSet<Vector3Int> largest = new(CellComparer.Instance);
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

            ice.UnionWith(largest);
            if (ice.Count < settings.minLakeCells)
            {
                builder.Layout.Warnings.Add($"The frozen lake kept {ice.Count} cells, fewer than {settings.minLakeCells}: make the area larger or the lake smaller.");
            }
        }

        /// <summary>
        /// Runs the gravel trail from the road towards the lake's south point, bending once on the way, to the snow
        /// cell nearest that point lying the ice gap plus a cell from the ice, and paints it on the snow off the road,
        /// keeping every trail cell the ice gap from the ice: the trail stops short of the lake with a cell of snow
        /// between, since gravel beside the ice with only snow under it would show the ice under its rounded corners
        /// (Docs/TerrainTypes.md, Phase 3's Corners note). Warns when no cell is that far from the ice.
        /// </summary>
        private void LayTrail()
        {
            if (ice.Count == 0 || road.Count == 0)
            {
                builder.Layout.Warnings.Add("There is no frozen lake, so no trail leads to it.");
                return;
            }

            Vector3Int target = default;
            float best = float.MaxValue;
            Vector2 south = lakeCentre + new Vector2(0f, -settings.lakeSize.y * 0.5f);
            foreach (Vector3Int cell in builder.OrderedGround)
            {
                if (IceDistance(cell) == IceGap + 1 && !builder.IsPath(cell))
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
                builder.Layout.Warnings.Add("No snow lies far enough from the ice for the trail to end on it.");
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
            HashSet<Vector3Int> trail = new(CellComparer.Instance);
            builder.Paint(samples, settings.trailWidth, trail);
            foreach (Vector3Int cell in builder.OrderedGround)
            {
                if (trail.Contains(cell) && IceDistance(cell) >= IceGap && !builder.Stone.Contains(cell))
                {
                    builder.Gravel.Add(cell);
                }
            }
        }

        /// <summary>
        /// Puts the party spawn on the road where it passes the configured point from west to east, or at the
        /// nearest road cell with snow all around it for the clear radius.
        /// </summary>
        private void PlaceSpawn()
        {
            float targetX = settings.spawnAlongRoad * halfWidth;
            List<Vector2> byDistance = new(road);
            byDistance.Sort((a, b) => Mathf.Abs(a.x - targetX).CompareTo(Mathf.Abs(b.x - targetX)));
            builder.PlaceSpawn(byDistance, settings.spawnClearRadius);
        }

        /// <summary>
        /// Keeps blocking props off the ice and the snow beside it.
        /// </summary>
        private void ReserveLake()
        {
            foreach (Vector3Int cell in builder.OrderedGround)
            {
                if (IceDistance(cell) <= 1)
                {
                    builder.Reserved.Add(cell);
                }
            }
        }

        /// <summary>
        /// Scatters <paramref name="scatter"/>'s props over the snow cells of its zone that are not on or beside the
        /// ice.
        /// </summary>
        private void ScatterProps(Scatter scatter)
        {
            List<Vector3Int> candidates = new();
            foreach (Vector3Int cell in builder.OrderedGround)
            {
                if (IceDistance(cell) > 1 && ZoneOf(cell) == scatter.Zone)
                {
                    candidates.Add(cell);
                }
            }

            builder.ScatterProps(scatter, candidates);
        }

        /// <summary>
        /// The zone of <paramref name="cell"/>: the border band along the edge, else the band of snow around the lake,
        /// else the interior.
        /// </summary>
        private Zone ZoneOf(Vector3Int cell)
        {
            if (builder.EdgeDistance(cell) <= settings.borderDepth)
            {
                return Zone.Border;
            }

            int distance = IceDistance(cell);
            return distance > 0 && distance <= settings.shoreZoneDepth ? Zone.Shore : Zone.Interior;
        }

        /// <summary>
        /// Spreads the mobs over snow off the ice inside the border band that the spawn can walk to, away from props,
        /// at least the configured distance from the spawn and from each other. Warns when fewer fit than asked for.
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
            candidates.RemoveAll(ice.Contains);
            int placed = builder.PlaceMobs(settings.mobPrefab, wanted, candidates, settings.mobSpacing, new List<Vector3Int>());
            if (placed < wanted)
            {
                builder.Layout.Warnings.Add($"Placed {placed} of {wanted} mobs: not enough open ground {settings.mobDistanceFromSpawn} cells from the spawn and {settings.mobSpacing} apart.");
            }
        }

        /// <summary>
        /// Warns when a trail cell or an ice cell cannot be walked to from the spawn.
        /// </summary>
        private void CheckTrail(HashSet<Vector3Int> reachable)
        {
            int cut = 0;
            foreach (Vector3Int cell in builder.OrderedGround)
            {
                if ((builder.Gravel.Contains(cell) || ice.Contains(cell)) && !reachable.Contains(cell))
                {
                    cut++;
                }
            }

            if (cut > 0)
            {
                builder.Layout.Warnings.Add($"{cut} trail or ice cells cannot be walked to from the spawn.");
            }
        }

        /// <summary>
        /// Lays the holes, first on the detail stream: the hole count of wobbly patches of open water, of a radius
        /// between the two values of the hole radius, on ice cells with ice all round them for the hole margin and no
        /// mob within a cell, so a hole meets only ice and no third look shows under its rounded corners. Pieces of
        /// fewer than four cells are dropped. Warns when holes were asked for and none fit.
        /// </summary>
        private void LayHoles()
        {
            if (settings.holeCount == 0)
            {
                return;
            }

            HashSet<Vector3Int> nearMobs = new(CellComparer.Instance);
            foreach (AreaLayout.Mob mob in builder.Layout.Mobs)
            {
                nearMobs.UnionWith(AreaLayoutBuilder.CellsNear(new[] { mob.Cell }, 1));
            }

            Predicate<Vector3Int> allowed = cell => !nearMobs.Contains(cell) && AreaLayoutBuilder.AllWithin(cell, settings.holeMargin, ice.Contains);
            builder.PaintPatches(water, settings.holeCount, settings.holeRadius, allowed, 1);
            builder.RemoveFragments(water, MinHolePiece);
            if (water.Count == 0)
            {
                builder.Layout.Warnings.Add("No hole fits in the ice: make the lake larger or the holes smaller.");
            }
        }

        /// <summary>
        /// Lays the campsite, after the holes on the detail stream: a campfire and seats on a wobbly patch of dirt on
        /// interior snow, away from the paths, the spawn and the mobs (<see cref="AreaLayoutBuilder.LayCampsite"/>), its
        /// centre further from the ice and the paths than its radius plus two cells, so its dirt meets only snow and no
        /// third look shows under a rounded corner.
        /// </summary>
        private void LayCampsite()
        {
            if (settings.campfirePrefab == null)
            {
                return;
            }

            float clear = settings.campsiteRadius + CampsiteIceMargin;
            HashSet<Vector3Int> nearPaths = AreaLayoutBuilder.CellsNear(builder.PathCells(), Mathf.CeilToInt(clear));
            AreaLayoutBuilder.Campsite campsite = new(settings.campfirePrefab, settings.campsiteSeats, settings.seatCount, settings.seatSpacing, settings.campsiteRadius);
            builder.LayCampsite(campsite, dirt, settings.borderDepth, settings.pathClearance, settings.spawnClearRadius, cell => IceDistance(cell) > clear && !nearPaths.Contains(cell));
        }

        /// <summary>
        /// How many steps (8 neighbours) <paramref name="cell"/> lies from the ice: 0 on it, and int.MaxValue off the
        /// snow or with no lake.
        /// </summary>
        private int IceDistance(Vector3Int cell)
        {
            return AreaLayoutBuilder.StepsAt(iceDistance, cell);
        }
    }
}
