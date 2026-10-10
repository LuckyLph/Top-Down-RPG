using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

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
    /// Props scattered over one zone, picked at random from a list, at least a spacing apart. Each prefab is placed
    /// as a blocking or walk-through prop depending on whether it has a footprint collider.
    /// </summary>
    [Serializable]
    public class Scatter
    {
        [SerializeField] private string label = "Props";
        [SerializeField] private List<GameObject> prefabs = new();
        [SerializeField] private Zone zone = Zone.Interior;
        [Tooltip("Most props of this layer placed. Fewer are placed when the zone runs out of room.")]
        [SerializeField, Min(0)] private int count = 10;
        [Tooltip("Smallest distance in cells between two props of this layer.")]
        [SerializeField, Min(0f)] private float spacing = 3f;

        public string Label => label;
        public IReadOnlyList<GameObject> Prefabs => prefabs;
        public Zone Zone => zone;
        public int Count => count;
        public float Spacing => spacing;
    }

    private const int GrassLayer = 0;
    private const int GravelLayer = 1;
    private const int StoneLayer = 2;

    [Header("Terrain")]
    [SerializeField] private TerrainLayer grass = new();
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
    /// Grass, then gravel, then stone: each path layer is drawn over the ones before it and decides its cells.
    /// </summary>
    public override IReadOnlyList<TerrainLayer> TerrainLayers => new[] { grass, gravel, stone };

    /// <summary>
    /// Lays out a clearing from <paramref name="layoutSeed"/>.
    /// </summary>
    public override AreaLayout Generate(int layoutSeed)
    {
        return new Run(this, layoutSeed).Build();
    }

    /// <summary>
    /// The state of one generation: the seeded random source, the cells laid so far and what each cell is used for.
    /// Iteration always goes through ordered lists, never hash sets, so a seed gives the same layout every time.
    /// </summary>
    private sealed class Run
    {
        private const float SampleStep = 0.2f;
        private const float PathEdgeAllowance = 0.05f;
        private const int OutlineHarmonics = 4;

        private static readonly Vector3Int[] Sides = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down };

        private readonly ClearingGenerator settings;
        private readonly Random random;
        private readonly AreaLayout layout;
        private readonly HashSet<Vector3Int> grass;
        private readonly HashSet<Vector3Int> gravel;
        private readonly HashSet<Vector3Int> stone;
        private readonly List<Vector3Int> orderedGrass = new();
        private readonly Dictionary<Vector3Int, int> edgeDistance = new();
        private readonly HashSet<Vector3Int> reserved = new();
        private readonly HashSet<Vector3Int> occupied = new();
        private readonly HashSet<Vector3Int> decorated = new();
        private readonly List<Vector2> road = new();
        private readonly float halfWidth;
        private readonly float halfHeight;

        /// <summary>
        /// Prepares a generation of <paramref name="generator"/>'s settings from <paramref name="seed"/>.
        /// </summary>
        public Run(ClearingGenerator generator, int seed)
        {
            settings = generator;
            random = new Random(seed);
            layout = new AreaLayout(3);
            grass = layout.TerrainCells(GrassLayer);
            gravel = layout.TerrainCells(GravelLayer);
            stone = layout.TerrainCells(StoneLayer);
            halfWidth = Mathf.Max(4, generator.size.x) * 0.5f;
            halfHeight = Mathf.Max(4, generator.size.y) * 0.5f;
        }

        /// <summary>
        /// Lays the grass, the road and the gravel paths, picks the spawn, scatters the props layer by layer and
        /// spreads the mobs.
        /// </summary>
        public AreaLayout Build()
        {
            LayGrass();
            MeasureEdgeDistance();
            LayRoad();
            LayGravelPaths();
            PlaceSpawn();
            ReserveOpenGround();
            foreach (Scatter scatter in settings.scatters)
            {
                if (scatter != null)
                {
                    ScatterProps(scatter);
                }
            }

            PlaceMobs();
            return layout;
        }

        /// <summary>
        /// Fills the cells whose centres fall inside a superellipse whose radius wobbles with a few random
        /// harmonics around it, then keeps only the largest 4-connected region.
        /// </summary>
        private void LayGrass()
        {
            float[] amplitudes = new float[OutlineHarmonics];
            float[] phases = new float[OutlineHarmonics];
            float total = 0f;
            for (int i = 0; i < OutlineHarmonics; i++)
            {
                amplitudes[i] = Range(0.4f, 1f);
                phases[i] = Range(0f, 2f * Mathf.PI);
                total += amplitudes[i];
            }

            int reachX = Mathf.CeilToInt(halfWidth * (1f + settings.edgeWobble)) + 1;
            int reachY = Mathf.CeilToInt(halfHeight * (1f + settings.edgeWobble)) + 1;
            List<Vector3Int> inside = new();
            for (int y = -reachY; y < reachY; y++)
            {
                for (int x = -reachX; x < reachX; x++)
                {
                    float nx = (x + 0.5f) / halfWidth;
                    float ny = (y + 0.5f) / halfHeight;
                    float radius = Mathf.Pow(Mathf.Pow(Mathf.Abs(nx), settings.squareness) + Mathf.Pow(Mathf.Abs(ny), settings.squareness), 1f / settings.squareness);
                    float angle = Mathf.Atan2(ny, nx);
                    float wobble = 0f;
                    for (int i = 0; i < OutlineHarmonics; i++)
                    {
                        wobble += amplitudes[i] * Mathf.Sin((i + 2) * angle + phases[i]);
                    }

                    if (radius <= 1f + settings.edgeWobble * wobble / total)
                    {
                        inside.Add(new Vector3Int(x, y, 0));
                    }
                }
            }

            KeepLargestRegion(inside);
        }

        /// <summary>
        /// Adds the largest 4-connected region of <paramref name="cells"/> to the grass, in the order the cells were
        /// listed.
        /// </summary>
        private void KeepLargestRegion(List<Vector3Int> cells)
        {
            HashSet<Vector3Int> remaining = new(cells);
            HashSet<Vector3Int> largest = new();
            foreach (Vector3Int start in cells)
            {
                if (!remaining.Contains(start))
                {
                    continue;
                }

                HashSet<Vector3Int> region = FloodFill(start, remaining.Contains);
                remaining.ExceptWith(region);
                if (region.Count > largest.Count)
                {
                    largest = region;
                }
            }

            foreach (Vector3Int cell in cells)
            {
                if (largest.Contains(cell))
                {
                    grass.Add(cell);
                    orderedGrass.Add(cell);
                }
            }
        }

        /// <summary>
        /// Records for every grass cell how many steps (8 neighbours) it is from the nearest cell without grass: 1 on
        /// the edge, growing inwards.
        /// </summary>
        private void MeasureEdgeDistance()
        {
            Queue<Vector3Int> queue = new();
            foreach (Vector3Int cell in orderedGrass)
            {
                if (TouchesOutside(cell))
                {
                    edgeDistance[cell] = 1;
                    queue.Enqueue(cell);
                }
            }

            while (queue.Count > 0)
            {
                Vector3Int cell = queue.Dequeue();
                int next = edgeDistance[cell] + 1;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        Vector3Int neighbour = cell + new Vector3Int(dx, dy, 0);
                        if (grass.Contains(neighbour) && !edgeDistance.ContainsKey(neighbour))
                        {
                            edgeDistance[neighbour] = next;
                            queue.Enqueue(neighbour);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Whether one of the 8 neighbours of <paramref name="cell"/> has no grass.
        /// </summary>
        private bool TouchesOutside(Vector3Int cell)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!grass.Contains(cell + new Vector3Int(dx, dy, 0)))
                    {
                        return true;
                    }
                }
            }

            return false;
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
                new(-halfWidth - 3f, Range(-1f, 1f) * meander),
                new(-halfWidth * 0.5f, Range(-1f, 1f) * meander),
                new(0f, Range(-1f, 1f) * meander),
                new(halfWidth * 0.5f, Range(-1f, 1f) * meander),
                new(halfWidth + 3f, Range(-1f, 1f) * meander)
            };
            SampleSpline(points, road);
            Paint(road, settings.roadWidth, stone);
        }

        /// <summary>
        /// Branches the gravel paths off the road, spread along its middle half, each heading alternately for the
        /// north or south edge through a point that bends it sideways.
        /// </summary>
        private void LayGravelPaths()
        {
            int count = settings.gravelPathCount;
            bool north = random.Next(2) == 0;
            List<Vector2> branch = new();
            for (int i = 0; i < count; i++)
            {
                float along = Mathf.Lerp(0.25f, 0.75f, Mathf.Clamp01((i + 0.5f + Range(-0.3f, 0.3f)) / count));
                Vector2 start = road[Mathf.RoundToInt(along * (road.Count - 1))];
                float side = north ? 1f : -1f;
                north = !north;
                Vector2 end = new(start.x + Range(-0.35f, 0.35f) * halfWidth, side * (halfHeight + 3f));
                Vector2 bend = Vector2.Lerp(start, end, 0.5f) + new Vector2(Range(-0.2f, 0.2f) * halfWidth, 0f);
                branch.Clear();
                SampleSpline(new[] { start, bend, end }, branch);
                Paint(branch, settings.gravelPathWidth, gravel);
            }
        }

        /// <summary>
        /// Puts the party spawn on the road where it passes the configured point from west to east, or at the
        /// nearest road cell with grass all around it for the clear radius; falls back to the grass cell furthest
        /// from the edge, with a warning, when the road has none.
        /// </summary>
        private void PlaceSpawn()
        {
            int needed = settings.spawnClearRadius + 1;
            float targetX = settings.spawnAlongRoad * halfWidth;
            List<Vector2> byDistance = new(road);
            byDistance.Sort((a, b) => Mathf.Abs(a.x - targetX).CompareTo(Mathf.Abs(b.x - targetX)));
            foreach (Vector2 point in byDistance)
            {
                Vector3Int cell = CellAt(point);
                if (edgeDistance.TryGetValue(cell, out int distance) && distance >= needed)
                {
                    layout.SpawnCell = cell;
                    return;
                }
            }

            Vector3Int deepest = orderedGrass.Count > 0 ? orderedGrass[0] : Vector3Int.zero;
            foreach (Vector3Int cell in orderedGrass)
            {
                if (edgeDistance[cell] > edgeDistance[deepest])
                {
                    deepest = cell;
                }
            }

            layout.SpawnCell = deepest;
            layout.Warnings.Add($"No road cell has {settings.spawnClearRadius} cells of grass around it; the spawn is at the clearing's deepest cell.");
        }

        /// <summary>
        /// Keeps blocking props off the paths and their clearance, and off the cells within the clear radius of the
        /// spawn.
        /// </summary>
        private void ReserveOpenGround()
        {
            int clearance = settings.pathClearance;
            foreach (Vector3Int cell in orderedGrass)
            {
                if (!stone.Contains(cell) && !gravel.Contains(cell))
                {
                    continue;
                }

                for (int dy = -clearance; dy <= clearance; dy++)
                {
                    for (int dx = -clearance; dx <= clearance; dx++)
                    {
                        reserved.Add(cell + new Vector3Int(dx, dy, 0));
                    }
                }
            }

            int radius = settings.spawnClearRadius;
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy <= radius * radius)
                    {
                        reserved.Add(layout.SpawnCell + new Vector3Int(dx, dy, 0));
                    }
                }
            }
        }

        /// <summary>
        /// Tries the cells of <paramref name="scatter"/>'s zone in random order, each with a random prefab from its
        /// list, until its count is placed or the cells run out.
        /// </summary>
        private void ScatterProps(Scatter scatter)
        {
            List<GameObject> prefabs = new();
            foreach (GameObject prefab in scatter.Prefabs)
            {
                if (prefab != null)
                {
                    prefabs.Add(prefab);
                }
            }

            if (prefabs.Count == 0 || scatter.Count == 0)
            {
                return;
            }

            List<Vector3Int> candidates = new();
            foreach (Vector3Int cell in orderedGrass)
            {
                if (IsInZone(cell, scatter.Zone))
                {
                    candidates.Add(cell);
                }
            }

            Shuffle(candidates);
            List<Vector2> placed = new();
            foreach (Vector3Int cell in candidates)
            {
                if (placed.Count >= scatter.Count)
                {
                    break;
                }

                GameObject prefab = prefabs[random.Next(prefabs.Count)];
                if (PropGridSnapping.TryGetFootprint(prefab, out Rect footprint))
                {
                    TryPlaceBlocking(prefab, footprint, cell, scatter.Spacing, placed);
                }
                else
                {
                    TryPlaceWalkThrough(prefab, cell, scatter.Spacing, placed);
                }
            }
        }

        /// <summary>
        /// Places a blocking prop whose footprint stands on <paramref name="cell"/>, centred on it as far as its
        /// width allows, when every cell it covers is free grass and it is far enough from the layer's other props.
        /// </summary>
        private void TryPlaceBlocking(GameObject prefab, Rect footprint, Vector3Int cell, float spacing, List<Vector2> placed)
        {
            Vector2Int covered = PropGridSnapping.CellsCovered(footprint, Vector2.one);
            Vector3Int corner = new(cell.x - (covered.x - 1) / 2, cell.y, 0);
            Vector2 centre = new(corner.x + covered.x * 0.5f, corner.y + covered.y * 0.5f);
            if (!IsFarFrom(placed, centre, spacing))
            {
                return;
            }

            for (int dy = 0; dy < covered.y; dy++)
            {
                for (int dx = 0; dx < covered.x; dx++)
                {
                    Vector3Int part = corner + new Vector3Int(dx, dy, 0);
                    if (!grass.Contains(part) || reserved.Contains(part) || occupied.Contains(part) || decorated.Contains(part))
                    {
                        return;
                    }
                }
            }

            for (int dy = 0; dy < covered.y; dy++)
            {
                for (int dx = 0; dx < covered.x; dx++)
                {
                    occupied.Add(corner + new Vector3Int(dx, dy, 0));
                }
            }

            placed.Add(centre);
            layout.Props.Add(new AreaLayout.Prop(prefab, corner, Vector2.zero));
        }

        /// <summary>
        /// Places a walk-through prop at a random point in <paramref name="cell"/> when the cell and its neighbours
        /// are off the paths, nothing else stands in it, and it is far enough from the layer's other props.
        /// </summary>
        private void TryPlaceWalkThrough(GameObject prefab, Vector3Int cell, float spacing, List<Vector2> placed)
        {
            Vector2 offset = new(Range(0.2f, 0.8f), Range(0.15f, 0.6f));
            Vector2 point = new(cell.x + offset.x, cell.y + offset.y);
            if (IsNearPath(cell) || occupied.Contains(cell) || decorated.Contains(cell) || !IsFarFrom(placed, point, spacing))
            {
                return;
            }

            decorated.Add(cell);
            placed.Add(point);
            layout.Props.Add(new AreaLayout.Prop(prefab, cell, offset));
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
                layout.Warnings.Add("No mob prefab is set; no mobs were placed.");
                return;
            }

            HashSet<Vector3Int> reachable = FloodFill(layout.SpawnCell, cell => grass.Contains(cell) && !occupied.Contains(cell));
            List<Vector3Int> candidates = new();
            foreach (Vector3Int cell in orderedGrass)
            {
                if (reachable.Contains(cell) && edgeDistance[cell] > settings.borderDepth && !TouchesProp(cell)
                    && Vector3Int.Distance(cell, layout.SpawnCell) >= settings.mobDistanceFromSpawn)
                {
                    candidates.Add(cell);
                }
            }

            Shuffle(candidates);
            List<Vector3Int> placed = new();
            foreach (Vector3Int cell in candidates)
            {
                if (placed.Count >= wanted)
                {
                    break;
                }

                bool farEnough = true;
                foreach (Vector3Int other in placed)
                {
                    farEnough &= Vector3Int.Distance(cell, other) >= settings.mobSpacing;
                }

                if (farEnough)
                {
                    placed.Add(cell);
                    layout.Mobs.Add(new AreaLayout.Mob(settings.mobPrefab, cell));
                }
            }

            if (placed.Count < wanted)
            {
                layout.Warnings.Add($"Placed {placed.Count} of {wanted} mobs: not enough open ground {settings.mobDistanceFromSpawn} cells from the spawn and {settings.mobSpacing} apart.");
            }
        }

        /// <summary>
        /// Every cell 4-connected to <paramref name="start"/> through cells that pass <paramref name="isOpen"/>,
        /// <paramref name="start"/> included when it passes.
        /// </summary>
        private static HashSet<Vector3Int> FloodFill(Vector3Int start, Predicate<Vector3Int> isOpen)
        {
            HashSet<Vector3Int> region = new();
            if (!isOpen(start))
            {
                return region;
            }

            Queue<Vector3Int> queue = new();
            region.Add(start);
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                Vector3Int cell = queue.Dequeue();
                foreach (Vector3Int side in Sides)
                {
                    Vector3Int neighbour = cell + side;
                    if (isOpen(neighbour) && region.Add(neighbour))
                    {
                        queue.Enqueue(neighbour);
                    }
                }
            }

            return region;
        }

        /// <summary>
        /// Samples a Catmull-Rom spline through <paramref name="points"/> about every <see cref="SampleStep"/> units
        /// into <paramref name="samples"/>.
        /// </summary>
        private static void SampleSpline(IReadOnlyList<Vector2> points, List<Vector2> samples)
        {
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 p0 = points[Mathf.Max(0, i - 1)];
                Vector2 p1 = points[i];
                Vector2 p2 = points[i + 1];
                Vector2 p3 = points[Mathf.Min(points.Count - 1, i + 2)];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(p1, p2) / SampleStep));
                for (int step = 0; step < steps; step++)
                {
                    float t = step / (float)steps;
                    samples.Add(0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * (t * t)
                        + (3f * p1 - p0 - 3f * p2 + p3) * (t * t * t)));
                }
            }

            samples.Add(points[points.Count - 1]);
        }

        /// <summary>
        /// Adds to <paramref name="target"/> every grass cell whose centre lies within half of
        /// <paramref name="width"/> of a sample, so the band is never narrower than the width.
        /// </summary>
        private void Paint(List<Vector2> samples, int width, HashSet<Vector3Int> target)
        {
            float radius = width * 0.5f + PathEdgeAllowance;
            int reach = Mathf.CeilToInt(radius);
            foreach (Vector2 sample in samples)
            {
                Vector3Int centre = CellAt(sample);
                for (int dy = -reach; dy <= reach; dy++)
                {
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        Vector3Int cell = centre + new Vector3Int(dx, dy, 0);
                        Vector2 cellCentre = new(cell.x + 0.5f, cell.y + 0.5f);
                        if ((cellCentre - sample).sqrMagnitude <= radius * radius && grass.Contains(cell))
                        {
                            target.Add(cell);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Whether <paramref name="cell"/> lies in <paramref name="zone"/>.
        /// </summary>
        private bool IsInZone(Vector3Int cell, Zone zone)
        {
            bool inBorder = edgeDistance[cell] <= settings.borderDepth;
            return zone == Zone.Border ? inBorder : !inBorder;
        }

        /// <summary>
        /// Whether <paramref name="cell"/> or one of its 8 neighbours is a path cell.
        /// </summary>
        private bool IsNearPath(Vector3Int cell)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    Vector3Int neighbour = cell + new Vector3Int(dx, dy, 0);
                    if (stone.Contains(neighbour) || gravel.Contains(neighbour))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Whether <paramref name="cell"/> or one of its 8 neighbours is covered by a blocking prop.
        /// </summary>
        private bool TouchesProp(Vector3Int cell)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (occupied.Contains(cell + new Vector3Int(dx, dy, 0)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Whether <paramref name="point"/> is at least <paramref name="spacing"/> from every point in
        /// <paramref name="placed"/>.
        /// </summary>
        private static bool IsFarFrom(List<Vector2> placed, Vector2 point, float spacing)
        {
            float squared = spacing * spacing;
            foreach (Vector2 other in placed)
            {
                if ((other - point).sqrMagnitude < squared)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The cell that contains <paramref name="point"/>.
        /// </summary>
        private static Vector3Int CellAt(Vector2 point)
        {
            return new Vector3Int(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y), 0);
        }

        /// <summary>
        /// A uniformly distributed value between <paramref name="min"/> and <paramref name="max"/>.
        /// </summary>
        private float Range(float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }

        /// <summary>
        /// Shuffles <paramref name="cells"/> in place (Fisher-Yates).
        /// </summary>
        private void Shuffle(List<Vector3Int> cells)
        {
            for (int i = cells.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (cells[i], cells[j]) = (cells[j], cells[i]);
            }
        }
    }
}
