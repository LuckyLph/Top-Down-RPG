using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lays out a lakeside: a grass blob ringed by trees, with a lake in its northern half, a sandy beach on the lake's
/// southern shore and a muddy marsh around the rest of it, a stone road crossing the south from west to east, a gravel
/// trail from the road down to the beach, patches of mud and of dry grass on the open grass, small shallow ponds
/// apart from the lake, a wooden dock from the trail's end across the beach into the lake, tall grass along the trees
/// and the marsh and flower meadows on the open grass. The party spawns on the road. Blocking props stay off the water,
/// the beach, the mud, the paths, a clearance beside them and the ground around the spawn; every mob, and the beach,
/// can be walked to from the spawn.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Lakeside Generator", fileName = "Generator_Lakeside")]
public sealed class LakesideGenerator : AreaGenerator
{
    private const int LakeSearchMargin = 2;
    private const int MinDryGrassFragment = 4;
    private const int MinPondPiece = 4;
    private const int MinPatchPiece = 4;
    private const int MeadowCentreClearance = 2;

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
    [Tooltip("Flower meadows on the open grass.")]
    [SerializeField] private TerrainLayer meadow = new();
    [Tooltip("Tall grass along the trees around the edge and along the marsh.")]
    [SerializeField] private TerrainLayer tallGrass = new();
    [SerializeField] private TerrainLayer sand = new();
    [SerializeField] private TerrainLayer mud = new();
    [SerializeField] private TerrainLayer gravel = new();
    [SerializeField] private TerrainLayer stone = new();
    [Tooltip("Light water over a visible bed, in small ponds on the open grass, walked at a cost.")]
    [SerializeField] private TerrainLayer shallows = new();
    [Tooltip("Drawn over every other layer but the planks, so it decides its cells: water cannot be walked.")]
    [SerializeField] private TerrainLayer water = new();
    [Tooltip("The dock's boards, drawn over everything, the water included, so a dock over the lake can be walked.")]
    [SerializeField] private TerrainLayer planks = new();

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

    [Header("Shallow ponds")]
    [Tooltip("Small ponds of shallows on the open grass, apart from the lake.")]
    [SerializeField, Min(0)] private int shallowPondCount = 3;
    [Tooltip("Smallest and largest radius in cells of a shallow pond.")]
    [SerializeField] private Vector2 shallowPondRadius = new(2f, 3f);

    [Header("Meadows and tall grass")]
    [Tooltip("Flower meadows laid on the open grass inside the band of trees, a cell from every other look.")]
    [SerializeField, Min(0)] private int meadowCount = 2;
    [Tooltip("Smallest and largest radius in cells of a meadow.")]
    [SerializeField] private Vector2 meadowRadius = new(3f, 5f);
    [Tooltip("Patches of tall grass laid along the inner edge of the band of trees and around the marsh's mud.")]
    [SerializeField, Min(0)] private int tallGrassCount = 7;
    [Tooltip("Smallest and largest radius in cells of a tall grass patch.")]
    [SerializeField] private Vector2 tallGrassRadius = new(2f, 3.5f);
    [Tooltip("How far in cells from the band of trees, or from the mud, tall grass may grow.")]
    [SerializeField, Min(1)] private int tallGrassReach = 3;

    [Header("Dock")]
    [Tooltip("Width in cells of the dock that runs on from the trail's end across the beach into the lake.")]
    [SerializeField, Min(1)] private int dockWidth = 3;
    [Tooltip("Fewest and most rows of the dock over open water, past the shore: it reaches as far as it fits up to the most, and is left out, with a warning, when not even the fewest fit. A most of 0 lays no dock.")]
    [SerializeField] private Vector2Int dockReach = new(4, 6);
    [Tooltip("Cells around the dock's part over the lake that must be water (or the beach it starts from), so its sides and end meet only water.")]
    [SerializeField, Min(1)] private int dockClearance = 2;

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
    public int ShallowPondCount => shallowPondCount;
    public int MeadowCount => meadowCount;
    public int TallGrassCount => tallGrassCount;
    public int TallGrassReach => tallGrassReach;
    public int DockWidth => dockWidth;
    public Vector2Int DockReach => dockReach;
    public int DockClearance => dockClearance;
    public int PathClearance => pathClearance;
    public int RoadMargin => roadMargin;
    public int SpawnClearRadius => spawnClearRadius;
    public MobController MobPrefab => mobPrefab;
    public int MobCount => mobCount;
    public float MobDistanceFromSpawn => mobDistanceFromSpawn;
    public float MobSpacing => mobSpacing;

    /// <summary>
    /// Grass, dry grass, meadow, tall grass, sand, mud, gravel, stone, shallows, water, then planks, in the style's layer
    /// order: each layer is drawn over the ones before it and decides its cells, so the road and the trail cross mud and
    /// sand, the lake covers everything under it and the dock lies over the lake and the beach.
    /// </summary>
    public override IReadOnlyList<TerrainLayer> TerrainLayers => new[] { grass, dryGrass, meadow, tallGrass, sand, mud, gravel, stone, shallows, water, planks };

    /// <summary>
    /// Lays out a lakeside from <paramref name="layoutSeed"/>.
    /// </summary>
    public override AreaLayout Generate(int layoutSeed)
    {
        return new Run(this, layoutSeed).Build();
    }

    /// <summary>
    /// The state of one generation: the shared builder with the cells laid so far, the road's samples, the lake's
    /// centre, the trail's end, and how many steps each dry grass cell lies from the water.
    /// </summary>
    private sealed class Run
    {
        private readonly LakesideGenerator settings;
        private readonly AreaLayoutBuilder builder;
        private readonly HashSet<Vector3Int> water;
        private readonly HashSet<Vector3Int> sand;
        private readonly HashSet<Vector3Int> mud;
        private readonly HashSet<Vector3Int> dryGrass;
        private readonly HashSet<Vector3Int> shallows;
        private readonly HashSet<Vector3Int> planks;
        private readonly HashSet<Vector3Int> meadow;
        private readonly HashSet<Vector3Int> tallGrass;
        private readonly List<Vector2> road = new();
        private readonly Dictionary<Vector3Int, int> waterDistance = new(CellComparer.Instance);
        private readonly float halfWidth;
        private readonly float halfHeight;
        private Vector2 lakeCentre;
        private Vector3Int? trailEnd;

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
            shallows = builder.Layout.TerrainCells(generator.shallows);
            planks = builder.Layout.TerrainCells(generator.planks);
            meadow = builder.Layout.TerrainCells(generator.meadow);
            tallGrass = builder.Layout.TerrainCells(generator.tallGrass);
            halfWidth = Mathf.Max(8, generator.size.x) * 0.5f;
            halfHeight = Mathf.Max(8, generator.size.y) * 0.5f;
        }

        /// <summary>
        /// Lays the grass, the road, the lake, its beach and marsh, the trail to the beach and the mud patches, picks
        /// the spawn, keeps blocking props off the open ground, scatters the props layer by layer, spreads the mobs,
        /// checks that the beach can be walked to, then lays the dry grass, the shallow ponds, the dock, the tall grass and
        /// the meadows from the detail stream, so they move nothing laid before them.
        /// </summary>
        public AreaLayout Build()
        {
            builder.LayGround(new Vector2(halfWidth, halfHeight), settings.squareness, settings.edgeWobble);
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
            LayShallowPonds();
            LayDock();
            LayTallGrass();
            LayMeadows();
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
            foreach (Vector3Int cell in builder.OrderedGround)
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
                        if (builder.Ground.Contains(neighbour) && !waterDistance.ContainsKey(neighbour))
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
            foreach (Vector3Int cell in builder.OrderedGround)
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
            foreach (Vector3Int cell in builder.OrderedGround)
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

            trailEnd = target;
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
            foreach (Vector3Int cell in builder.OrderedGround)
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
            foreach (Vector3Int cell in builder.OrderedGround)
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
        /// rounded corners where the two meet grass. It also keeps a cell of grass inside the map's edge, where its own
        /// edge would otherwise meet the grass's fringe. Pieces of fewer than four cells, left where those cells cut a patch
        /// short, are removed. Props stand on it as on grass.
        /// </summary>
        private void LayDryGrass()
        {
            builder.StartDetailStream();
            HashSet<Vector3Int> nearMud = AreaLayoutBuilder.CellsNear(mud, 1);
            List<Vector3Int> open = new();
            int away = settings.shoreZoneDepth + Mathf.Max(settings.beachWidth, settings.marshWidth) + 2;
            foreach (Vector3Int cell in builder.OrderedGround)
            {
                if (IsOpenGrass(cell) && !nearMud.Contains(cell) && builder.EdgeDistance(cell) > settings.borderDepth && WaterDistance(cell) > away && !builder.IsNearPath(cell))
                {
                    open.Add(cell);
                }
            }

            for (int i = 0; i < settings.dryGrassPatchCount && open.Count > 0; i++)
            {
                PaintPatch(open[builder.Next(open.Count)], settings.dryGrassPatchRadius, dryGrass, cell => !nearMud.Contains(cell) && !builder.IsNearPath(cell) && builder.EdgeDistance(cell) > 2);
            }

            builder.RemoveFragments(dryGrass, MinDryGrassFragment);
        }

        /// <summary>
        /// Lays the shallow ponds, after the dry grass on the same detail stream, so nothing laid before them moves:
        /// the shallow pond count of wobbly patches of shallows, of a radius between the two values of the shallow pond
        /// radius, centred on random cells where a pond fits. A pond cell and its 8 neighbours are plain grass (no dry
        /// grass, beach, mud, path or water, and two cells inside the map's edge) with no blocking prop or mob on them,
        /// outside the spawn's clear disc, and the ponds keep two cells apart. A pond so meets only grass, and no third
        /// look shows under its rounded corners (Docs/TerrainTypes.md, Phase 3's Corners note). Pieces of fewer than four
        /// cells, left where those rules cut a pond short, are removed, and so are the walk-through props (plants) on or
        /// beside a pond or up to three rows below it, as the Clearing's campsite removes them, since a plant's sprite
        /// stands above its cell and a tall one would reach over the water. Props are only removed, so nothing else moves.
        /// </summary>
        private void LayShallowPonds()
        {
            HashSet<Vector3Int> blocked = new(CellComparer.Instance);
            foreach (Vector3Int cell in builder.OrderedGround)
            {
                if (!IsPlainGrass(cell) || builder.EdgeDistance(cell) <= 2 || builder.Occupied.Contains(cell))
                {
                    blocked.Add(cell);
                }
            }

            foreach (AreaLayout.Mob mob in builder.Layout.Mobs)
            {
                blocked.Add(mob.Cell);
            }

            HashSet<Vector3Int> near = AreaLayoutBuilder.CellsNear(blocked, 1);
            Vector3Int spawn = builder.Layout.SpawnCell;
            int clear = settings.spawnClearRadius;
            HashSet<Vector3Int> fits = new(CellComparer.Instance);
            List<Vector3Int> centres = new();
            foreach (Vector3Int cell in builder.OrderedGround)
            {
                Vector3Int offset = cell - spawn;
                if (!near.Contains(cell) && offset.x * offset.x + offset.y * offset.y > clear * clear)
                {
                    fits.Add(cell);
                    centres.Add(cell);
                }
            }

            for (int i = 0; i < settings.shallowPondCount && centres.Count > 0; i++)
            {
                HashSet<Vector3Int> apart = AreaLayoutBuilder.CellsNear(shallows, 2);
                PaintPatch(centres[builder.Next(centres.Count)], settings.shallowPondRadius, shallows, cell => fits.Contains(cell) && !apart.Contains(cell));
                HashSet<Vector3Int> taken = AreaLayoutBuilder.CellsNear(shallows, 2);
                centres.RemoveAll(taken.Contains);
            }

            builder.RemoveFragments(shallows, MinPondPiece);
            HashSet<Vector3Int> wet = AreaLayoutBuilder.CellsNear(shallows, 1);
            foreach (Vector3Int cell in shallows)
            {
                for (int dy = -3; dy <= -2; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        wet.Add(cell + new Vector3Int(dx, dy, 0));
                    }
                }
            }

            builder.RemoveProps(wet.Contains, true);
        }

        /// <summary>
        /// Lays the dock, last, after the shallow ponds, so nothing laid before it moves; it draws nothing from the
        /// random streams, so it does not depend on the dry grass or the ponds either: planks the dock width wide over
        /// the trail's end, from the row of the trail's last cell (the tip its brush leaves above its end) north across
        /// the beach and into the lake, as many rows over open water as fit up to the most of the dock reach
        /// (<see cref="TryDock"/>). Its cells on the beach are sand or the trail's gravel, beside sand, water or gravel; its
        /// rows in the lake are all water, and every cell within the dock clearance of them is water or the beach. It
        /// is centred on the trail's end where it fits, else moved sideways a column at a time while it still covers
        /// the end, first looking for a place where the shore runs level across the dock and a cell beyond each side,
        /// so the water's edge meets the dock's sides straight. The planks are a bridge, so the sand and the trail's
        /// gravel under its beach end and the water under its lake end stay as they are and only the planks are added.
        /// The walk-through props on or beside the dock or up to three rows below it are removed, as the ponds remove
        /// them. Warns when no dock fits.
        /// </summary>
        private void LayDock()
        {
            int longest = Mathf.Max(settings.dockReach.x, settings.dockReach.y);
            if (trailEnd == null || longest <= 0)
            {
                return;
            }

            int width = Mathf.Max(1, settings.dockWidth);
            int shortest = Mathf.Clamp(Mathf.Min(settings.dockReach.x, settings.dockReach.y), 1, longest);
            int centred = trailEnd.Value.x - (width - 1) / 2;
            List<Vector3Int> dock = null;
            for (int pass = 0; pass < 2 && dock == null; pass++)
            {
                for (int shift = 0; shift < width && dock == null; shift++)
                {
                    foreach (int left in new[] { centred - shift, centred + shift })
                    {
                        if (dock == null && left <= trailEnd.Value.x && left + width > trailEnd.Value.x)
                        {
                            dock = TryDock(left, width, shortest, longest, pass == 0);
                        }
                    }
                }
            }

            if (dock == null)
            {
                builder.Layout.Warnings.Add($"No dock of {shortest} rows over the water fits from the trail's end at {trailEnd.Value} across the beach.");
                return;
            }

            planks.UnionWith(dock);
            HashSet<Vector3Int> near = AreaLayoutBuilder.CellsNear(planks, 1);
            foreach (Vector3Int cell in planks)
            {
                for (int dy = -3; dy <= -2; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        near.Add(cell + new Vector3Int(dx, dy, 0));
                    }
                }
            }

            builder.RemoveProps(near.Contains, true);
        }

        /// <summary>
        /// The cells of a dock <paramref name="width"/> wide from column <paramref name="left"/>, starting on the row of
        /// the trail's last cell: its rows on the beach up to the first row it finds all water, each on the beach
        /// (<see cref="IsDockRowOnBeach"/>), then as many of <paramref name="reach"/> rows over open water as keep the
        /// dock clearance around them water or beach; null when it crosses more than the beach or fewer than
        /// <paramref name="shortest"/> rows fit, or, with <paramref name="level"/>, when the shore steps across the dock
        /// or beside it.
        /// </summary>
        private List<Vector3Int> TryDock(int left, int width, int shortest, int reach, bool level)
        {
            List<Vector3Int> dock = new();
            int row = trailEnd.Value.y + 1;
            for (int beach = 0; !IsDockRowInWater(left, width, row); beach++, row++)
            {
                if (beach > settings.beachWidth * 2 || !IsDockRowOnBeach(left, width, row))
                {
                    return null;
                }

                AddDockRow(dock, left, width, row);
            }

            int shore = row;
            if (level && !IsLevelShore(left, width, shore))
            {
                return null;
            }

            int fits = 0;
            for (int rows = 1; rows <= reach && IsDockRowInWater(left, width, shore + rows - 1); rows++)
            {
                if (IsClearAround(left, width, shore, rows))
                {
                    fits = rows;
                }
            }

            if (fits < shortest)
            {
                return null;
            }

            for (int k = 0; k < fits; k++)
            {
                AddDockRow(dock, left, width, shore + k);
            }

            return dock;
        }

        /// <summary>
        /// Whether the shore runs level across the dock and a cell beyond each side: on row <paramref name="shore"/>
        /// those cells are all water and on the row below it none is.
        /// </summary>
        private bool IsLevelShore(int left, int width, int shore)
        {
            for (int x = left - 1; x <= left + width; x++)
            {
                if (!water.Contains(new Vector3Int(x, shore, 0)) || water.Contains(new Vector3Int(x, shore - 1, 0)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Adds the dock's <paramref name="width"/> cells from column <paramref name="left"/> on <paramref name="row"/>.
        /// </summary>
        private static void AddDockRow(List<Vector3Int> dock, int left, int width, int row)
        {
            for (int x = left; x < left + width; x++)
            {
                dock.Add(new Vector3Int(x, row, 0));
            }
        }

        /// <summary>
        /// Whether every cell of the dock's row <paramref name="row"/> is water.
        /// </summary>
        private bool IsDockRowInWater(int left, int width, int row)
        {
            for (int x = left; x < left + width; x++)
            {
                if (!water.Contains(new Vector3Int(x, row, 0)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Whether the dock can cross <paramref name="row"/> on the beach: each of its cells is sand, the trail's gravel
        /// or water (where the shore steps across the dock), and the cells beside them are too.
        /// </summary>
        private bool IsDockRowOnBeach(int left, int width, int row)
        {
            for (int x = left - 1; x <= left + width; x++)
            {
                Vector3Int cell = new(x, row, 0);
                bool shore = sand.Contains(cell) || water.Contains(cell) || builder.Gravel.Contains(cell);
                if (!shore || mud.Contains(cell) || builder.Stone.Contains(cell) || dryGrass.Contains(cell) || shallows.Contains(cell))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Whether every cell within the dock clearance of the dock's first <paramref name="rows"/> rows over the water,
        /// from row <paramref name="shore"/>, is water or the beach (sand), so the dock's sides and end meet only water.
        /// </summary>
        private bool IsClearAround(int left, int width, int shore, int rows)
        {
            int clearance = Mathf.Max(1, settings.dockClearance);
            for (int y = shore; y < shore + rows + clearance; y++)
            {
                for (int x = left - clearance; x < left + width + clearance; x++)
                {
                    Vector3Int cell = new(x, y, 0);
                    if (!water.Contains(cell) && !sand.Contains(cell))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Lays the tall grass, last but for the meadows, on the detail stream after the shallow ponds (the dock draws
        /// nothing from it), so nothing laid before it moves: the tall grass count of wobbly patches of a radius between
        /// the two values of the tall grass radius, each centred on a random cell where tall grass may grow: within the
        /// tall grass reach (8 neighbours) of the band of trees along the edge or of the mud, two cells inside the map's
        /// edge, and a cell from every other look (water, beach, mud, paths, dry grass, the ponds and the dock), so it meets
        /// only grass and no third look shows under a rounded corner (Docs/TerrainTypes.md, Phase 3's Corners note).
        /// Pieces of fewer than four cells are dropped. Trees stand in it; the walk-through props (plants) on it are
        /// removed, since a mushroom or a fern drawn over the blades would stand on top of them.
        /// </summary>
        private void LayTallGrass()
        {
            if (settings.tallGrassCount == 0)
            {
                return;
            }

            HashSet<Vector3Int> others = OtherLooks();
            HashSet<Vector3Int> nearOthers = AreaLayoutBuilder.CellsNear(others, 1);
            HashSet<Vector3Int> nearMud = AreaLayoutBuilder.CellsNear(mud, settings.tallGrassReach);
            int edge = settings.borderDepth + settings.tallGrassReach;
            Predicate<Vector3Int> allowed = cell => builder.Ground.Contains(cell) && builder.EdgeDistance(cell) > 2 && !nearOthers.Contains(cell)
                && (builder.EdgeDistance(cell) <= edge || nearMud.Contains(cell));
            builder.PaintPatches(tallGrass, settings.tallGrassCount, settings.tallGrassRadius, allowed, 0);
            builder.RemoveFragments(tallGrass, MinPatchPiece);
            builder.RemoveProps(tallGrass.Contains, true);
        }

        /// <summary>
        /// Lays the meadows, last, on the detail stream after the tall grass: the meadow count of wobbly patches of a
        /// radius between the two values of the meadow radius, on open grass inside the band of trees a cell from every
        /// other look (the tall grass included), each centred on a random cell with every cell within two steps such a
        /// cell, so a patch is not cut down to a sliver at once. A meadow so meets only grass. Pieces of fewer than four
        /// cells are dropped. Props stand on it as on grass, the plants among the flowers.
        /// </summary>
        private void LayMeadows()
        {
            HashSet<Vector3Int> others = OtherLooks();
            others.UnionWith(tallGrass);
            HashSet<Vector3Int> nearOthers = AreaLayoutBuilder.CellsNear(others, 1);
            Predicate<Vector3Int> allowed = cell => builder.Ground.Contains(cell) && builder.EdgeDistance(cell) > settings.borderDepth && !nearOthers.Contains(cell);
            builder.PaintPatches(meadow, settings.meadowCount, settings.meadowRadius, allowed, MeadowCentreClearance);
            builder.RemoveFragments(meadow, MinPatchPiece);
        }

        /// <summary>
        /// Every cell of a look other than the grass laid before the tall grass and the meadows: the water, the beach,
        /// the mud, the paths, the dry grass, the ponds and the dock.
        /// </summary>
        private HashSet<Vector3Int> OtherLooks()
        {
            HashSet<Vector3Int> cells = new(water, CellComparer.Instance);
            cells.UnionWith(sand);
            cells.UnionWith(mud);
            cells.UnionWith(builder.Gravel);
            cells.UnionWith(builder.Stone);
            cells.UnionWith(dryGrass);
            cells.UnionWith(shallows);
            cells.UnionWith(planks);
            return cells;
        }

        /// <summary>
        /// Whether <paramref name="cell"/> is grass with no dry grass, water, beach, mud or path on it.
        /// </summary>
        private bool IsPlainGrass(Vector3Int cell)
        {
            return IsOpenGrass(cell) && !dryGrass.Contains(cell);
        }

        /// <summary>
        /// Whether <paramref name="cell"/> is grass with no water, beach, mud or path on it.
        /// </summary>
        private bool IsOpenGrass(Vector3Int cell)
        {
            return builder.Ground.Contains(cell) && !water.Contains(cell) && !sand.Contains(cell) && !mud.Contains(cell) && !builder.IsPath(cell);
        }

        /// <summary>
        /// Paints <paramref name="target"/> in a wobbly disc of a random radius between the two values of
        /// <paramref name="radii"/> around <paramref name="centre"/>, on dry grass that is neither beach nor path and that
        /// passes <paramref name="allowed"/>.
        /// </summary>
        private void PaintPatch(Vector3Int centre, Vector2 radii, HashSet<Vector3Int> target, Predicate<Vector3Int> allowed)
        {
            builder.PaintPatch(centre, radii, target, cell => !water.Contains(cell) && !sand.Contains(cell) && !builder.IsPath(cell) && allowed(cell));
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
            foreach (Vector3Int cell in builder.OrderedGround)
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
            foreach (Vector3Int cell in builder.OrderedGround)
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
            return AreaLayoutBuilder.FloodFill(builder.Layout.SpawnCell, cell => builder.Ground.Contains(cell) && !water.Contains(cell) && !builder.Occupied.Contains(cell));
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
    }
}
