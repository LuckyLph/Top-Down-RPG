using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lays out lava fields: scorched earth ringed by dead trees and dark rock, crossed from north to south by a river of
/// lava with pools of lava beside it, a stone road crossing the south from west to east over a causeway of earth where
/// the river stops short of it, a gravel trail from the road towards the lava that stops short of it, and a campsite
/// (a campfire and seats) on the open earth. The party spawns on the road. Lava cannot be walked: it keeps a cell of
/// earth from every path and from the spawn's ground, blocking props stay off it and the earth beside it, mobs stand
/// clear of it, and the trail and every mob can be walked to from the spawn around it. Dirt is the builder's ground,
/// the layer every other one lies on, as grass is in most other generators.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Lava Fields Generator", fileName = "Generator_LavaFields")]
public sealed class LavaFieldsGenerator : AreaGenerator
{
    private const int LavaGap = 2;
    private const int MinLavaPiece = 4;
    private const int IslandSize = 12;
    private const int CampsiteLavaMargin = 2;
    private const float TrailLeadIn = 3f;
    private const float TrailNorth = 6f;

    /// <summary>
    /// The part of the lava fields a scatter layer fills: the band along its edge, the earth near the lava, or the rest
    /// of the ground inside the band.
    /// </summary>
    public enum Zone
    {
        Border,
        Interior,
        Shore
    }

    /// <summary>
    /// Props scattered over one zone of the lava fields.
    /// </summary>
    [Serializable]
    public class Scatter : PropScatter
    {
        [SerializeField] private Zone zone = Zone.Interior;

        public Zone Zone => zone;
    }

    [Header("Terrain")]
    [Tooltip("The ground: the scorched earth every other layer lies on.")]
    [SerializeField] private TerrainLayer dirt = new();
    [SerializeField] private TerrainLayer gravel = new();
    [SerializeField] private TerrainLayer stone = new();
    [Tooltip("The river and pools of lava, drawn over everything under them so they decide their cells: lava cannot be walked.")]
    [SerializeField] private TerrainLayer lava = new();

    [Header("Shape")]
    [Tooltip("Width and height of the area in cells, before its outline wobbles.")]
    [SerializeField] private Vector2Int size = new(64, 46);
    [Tooltip("Exponent of the outline's superellipse: 2 is an ellipse, higher is closer to a rectangle.")]
    [SerializeField, Range(2f, 6f)] private float squareness = 3f;
    [Tooltip("How far the outline wobbles in and out, as a fraction of its radius.")]
    [SerializeField, Range(0f, 0.3f)] private float edgeWobble = 0.1f;

    [Header("Lava")]
    [Tooltip("Width in cells of the lava river running from the northern edge to the southern one.")]
    [SerializeField, Min(1)] private int riverWidth = 3;
    [Tooltip("How far east or west of the middle the river runs on average, at most, as a fraction of the area's half width.")]
    [SerializeField, Range(0f, 0.5f)] private float riverOffset = 0.15f;
    [Tooltip("How far the river wanders east and west of its course, as a fraction of the area's half width.")]
    [SerializeField, Range(0f, 0.5f)] private float riverMeander = 0.25f;
    [Tooltip("Pools of lava beside the river; 0 for none.")]
    [SerializeField, Min(0)] private int poolCount = 3;
    [Tooltip("Smallest and largest radius in cells of a pool.")]
    [SerializeField] private Vector2 poolRadius = new(2.5f, 4.5f);
    [Tooltip("Depth in cells of the band of earth around the lava that Shore layers fill.")]
    [SerializeField, Min(1)] private int shoreZoneDepth = 3;

    [Header("Paths")]
    [Tooltip("Width in cells of the stone road crossing the south of the area from west to east.")]
    [SerializeField, Min(1)] private int roadWidth = 3;
    [Tooltip("How far south of the middle the road runs, as a fraction of the area's half height.")]
    [SerializeField, Range(0f, 0.8f)] private float roadSouth = 0.5f;
    [Tooltip("How far the road wanders north and south, as a fraction of the area's half height.")]
    [SerializeField, Range(0f, 0.4f)] private float roadMeander = 0.12f;
    [Tooltip("Width in cells of the gravel trail from the road towards the lava.")]
    [SerializeField, Min(1)] private int trailWidth = 2;
    [Tooltip("Cells beside every path kept free of blocking props.")]
    [SerializeField, Min(0)] private int pathClearance = 1;

    [Header("Spawn")]
    [Tooltip("Where along the road the party arrives, from -1 (west edge) to 1 (east edge).")]
    [SerializeField, Range(-0.9f, 0.9f)] private float spawnAlongRoad = -0.6f;
    [Tooltip("Radius in cells around the spawn that stays on earth, free of lava and blocking props.")]
    [SerializeField, Min(0)] private int spawnClearRadius = 6;

    [Header("Props")]
    [Tooltip("Depth in cells of the band along the edge that Border layers fill; the pools keep out of it.")]
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
    [Tooltip("Radius in cells of the campsite, before its outline wobbles: the earth there is cleared of props and plants.")]
    [SerializeField, Min(1f)] private float campsiteRadius = 3f;

    [Header("Mobs")]
    [SerializeField] private MobController mobPrefab;
    [SerializeField, Min(0)] private int mobCount = 4;
    [Tooltip("Smallest distance in cells from the party spawn to a mob spawn.")]
    [SerializeField, Min(0f)] private float mobDistanceFromSpawn = 14f;
    [Tooltip("Smallest distance in cells between two mob spawns.")]
    [SerializeField, Min(0f)] private float mobSpacing = 10f;

    public int BorderDepth => borderDepth;
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
    /// Dirt, gravel, stone, then lava, in the style's layer order: each layer is drawn over the ones before it and
    /// decides its cells, so the paths cross the earth and the lava lies over it.
    /// </summary>
    public override IReadOnlyList<TerrainLayer> TerrainLayers => new[] { dirt, gravel, stone, lava };

    /// <summary>
    /// Lays out lava fields from <paramref name="layoutSeed"/>.
    /// </summary>
    public override AreaLayout Generate(int layoutSeed)
    {
        return new Run(this, layoutSeed).Build();
    }

    /// <summary>
    /// The state of one generation: the shared builder with the cells laid so far, the road's samples and how many
    /// steps each ground cell lies from the lava.
    /// </summary>
    private sealed class Run
    {
        private readonly LavaFieldsGenerator settings;
        private readonly AreaLayoutBuilder builder;
        private readonly HashSet<Vector3Int> lava;
        private readonly List<Vector2> road = new();
        private Dictionary<Vector3Int, int> lavaDistance = new();
        private readonly float halfWidth;
        private readonly float halfHeight;

        /// <summary>
        /// Prepares a generation of <paramref name="generator"/>'s settings from <paramref name="seed"/>.
        /// </summary>
        public Run(LavaFieldsGenerator generator, int seed)
        {
            settings = generator;
            builder = new AreaLayoutBuilder(seed, generator.TerrainLayers, generator.dirt, generator.gravel, generator.stone);
            lava = builder.Layout.TerrainCells(generator.lava);
            halfWidth = Mathf.Max(8, generator.size.x) * 0.5f;
            halfHeight = Mathf.Max(8, generator.size.y) * 0.5f;
        }

        /// <summary>
        /// Lays the earth and the road, picks the spawn, lays the lava river and pools clear of the road and the spawn,
        /// then the trail towards the lava, keeps blocking props off the lava and the earth beside it, scatters the props
        /// layer by layer, spreads the mobs, checks that the trail can be walked to, then lays the campsite from the
        /// detail stream, so it moves nothing laid before it.
        /// </summary>
        public AreaLayout Build()
        {
            builder.LayGround(new Vector2(halfWidth, halfHeight), settings.squareness, settings.edgeWobble);
            LayRoad();
            PlaceSpawn();
            LayLava();
            lavaDistance = builder.StepsFrom(lava.Contains);
            LayTrail();
            builder.ReservePathClearance(settings.pathClearance);
            builder.ReserveDisc(builder.Layout.SpawnCell, settings.spawnClearRadius);
            ReserveLava();
            foreach (Scatter scatter in settings.scatters)
            {
                if (scatter != null)
                {
                    ScatterProps(scatter);
                }
            }

            HashSet<Vector3Int> reachable = builder.ReachableFromSpawn(lava.Contains);
            PlaceMobs(reachable);
            CheckTrail(reachable);
            builder.StartDetailStream();
            LayCampsite();
            return builder.Layout;
        }

        /// <summary>
        /// Runs the stone road through five points from beyond the west edge to beyond the east edge, south of the
        /// middle, each at a random height within the meander, and paints it on the earth.
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
        /// Puts the party spawn on the road where it passes the configured point from west to east, or at the
        /// nearest road cell with earth all around it for the clear radius.
        /// </summary>
        private void PlaceSpawn()
        {
            float targetX = settings.spawnAlongRoad * halfWidth;
            List<Vector2> byDistance = new(road);
            byDistance.Sort((a, b) => Mathf.Abs(a.x - targetX).CompareTo(Mathf.Abs(b.x - targetX)));
            builder.PlaceSpawn(byDistance, settings.spawnClearRadius);
        }

        /// <summary>
        /// Runs the lava river through five points from beyond the northern edge to beyond the southern one, around a
        /// course east or west of the middle by up to the river offset, each point wandering by up to the meander, then
        /// paints the pools, each centred on earth beside the river or near it, inside the border band. Lava keeps the
        /// lava gap from every path, so the river stops a cell of earth short of the road on each side and the road
        /// crosses it on a causeway of earth, and stays off the spawn's ground; pieces of fewer than four cells are
        /// dropped. The earth goes on under the lava.
        /// </summary>
        private void LayLava()
        {
            Dictionary<Vector3Int, int> pathDistance = builder.StepsFrom(builder.IsPath);
            Vector3Int spawn = builder.Layout.SpawnCell;
            float keepFromSpawn = settings.spawnClearRadius + 1f;
            Predicate<Vector3Int> allowed = cell => AreaLayoutBuilder.StepsAt(pathDistance, cell) >= LavaGap && Vector3Int.Distance(cell, spawn) > keepFromSpawn;
            float course = builder.Range(-1f, 1f) * settings.riverOffset * halfWidth;
            float meander = settings.riverMeander * halfWidth;
            Vector2[] points =
            {
                new(course + builder.Range(-1f, 1f) * meander, halfHeight + 3f),
                new(course + builder.Range(-1f, 1f) * meander, halfHeight * 0.5f),
                new(course + builder.Range(-1f, 1f) * meander, 0f),
                new(course + builder.Range(-1f, 1f) * meander, -halfHeight * 0.5f),
                new(course + builder.Range(-1f, 1f) * meander, -halfHeight - 3f)
            };
            List<Vector2> samples = new();
            AreaLayoutBuilder.SampleSpline(points, samples);
            HashSet<Vector3Int> river = new();
            builder.Paint(samples, settings.riverWidth, river);
            foreach (Vector3Int cell in builder.OrderedGround)
            {
                if (river.Contains(cell) && allowed(cell))
                {
                    lava.Add(cell);
                }
            }

            Dictionary<Vector3Int, int> riverDistance = builder.StepsFrom(lava.Contains);
            Predicate<Vector3Int> poolAllowed = cell => allowed(cell) && builder.EdgeDistance(cell) > settings.borderDepth + 1
                && AreaLayoutBuilder.StepsAt(riverDistance, cell) <= Mathf.CeilToInt(settings.poolRadius.y) + 2;
            builder.PaintPatches(lava, settings.poolCount, settings.poolRadius, poolAllowed, 1);
            builder.RemoveFragments(lava, MinLavaPiece);
            FillIslands(allowed);
        }

        /// <summary>
        /// Turns into lava every island of earth the lava encloses (a 4-connected piece of earth of fewer than the
        /// island size that touches the map's edge nowhere), where a pool meets the river, when all of it passes
        /// <paramref name="allowed"/>: no unit could reach it, and a speck of earth in the lava reads as a mistake.
        /// </summary>
        private void FillIslands(Predicate<Vector3Int> allowed)
        {
            HashSet<Vector3Int> seen = new();
            foreach (Vector3Int cell in builder.OrderedGround)
            {
                if (lava.Contains(cell) || seen.Contains(cell))
                {
                    continue;
                }

                HashSet<Vector3Int> piece = AreaLayoutBuilder.FloodFill(cell, other => builder.Ground.Contains(other) && !lava.Contains(other) && !seen.Contains(other));
                seen.UnionWith(piece);
                if (piece.Count >= IslandSize)
                {
                    continue;
                }

                bool fill = true;
                foreach (Vector3Int island in piece)
                {
                    fill &= allowed(island) && builder.EdgeDistance(island) > 1;
                }

                if (fill)
                {
                    lava.UnionWith(piece);
                }
            }
        }

        /// <summary>
        /// Runs the gravel trail from the road towards the lava, ending on earth the lava gap from it, at least a few rows
        /// north of the road and inside the border band. The ends are tried nearest a random point of the northern half
        /// first; the trail leaves the road straight north where the road's edge runs level
        /// (<see cref="AreaLayoutBuilder.TrailStart"/>) and bends once, by a sideways offset drawn once, and the first end
        /// whose whole trail keeps the lava gap from the lava is taken, so the trail runs unbroken and stops with a cell
        /// of earth between it and the lava: gravel beside the lava with only earth under it would show the lava under
        /// its rounded corners (Docs/TerrainTypes.md, Phase 3's Corners note). Warns when no end fits.
        /// </summary>
        private void LayTrail()
        {
            if (road.Count == 0 || lava.Count == 0)
            {
                builder.Layout.Warnings.Add("There is no lava, so no trail leads to it.");
                return;
            }

            Vector2 goal = new(builder.Range(-0.6f, 0.6f) * halfWidth, builder.Range(0.1f, 0.5f) * halfHeight);
            float sway = builder.Range(-0.1f, 0.1f) * halfWidth;
            float roadTop = float.MinValue;
            foreach (Vector3Int cell in builder.Stone)
            {
                roadTop = Mathf.Max(roadTop, cell.y);
            }

            List<Vector3Int> ends = new();
            foreach (Vector3Int cell in builder.OrderedGround)
            {
                if (LavaDistance(cell) == LavaGap && !builder.IsPath(cell) && cell.y >= roadTop + TrailNorth && builder.EdgeDistance(cell) > settings.borderDepth)
                {
                    ends.Add(cell);
                }
            }

            ends.Sort((a, b) => (new Vector2(a.x + 0.5f, a.y + 0.5f) - goal).sqrMagnitude.CompareTo((new Vector2(b.x + 0.5f, b.y + 0.5f) - goal).sqrMagnitude));
            foreach (Vector3Int end in ends)
            {
                HashSet<Vector3Int> trail = Trail(end, sway);
                if (trail.Count > 0)
                {
                    builder.Gravel.UnionWith(trail);
                    return;
                }
            }

            builder.Layout.Warnings.Add("No trail from the road reaches the lava without running beside it.");
        }

        /// <summary>
        /// The trail's cells to <paramref name="end"/>, off the road, bending by <paramref name="sway"/>; empty when one
        /// of them lies closer to the lava than the lava gap.
        /// </summary>
        private HashSet<Vector3Int> Trail(Vector3Int end, float sway)
        {
            Vector2 target = new(end.x + 0.5f, end.y + 0.5f);
            Vector2 start = builder.TrailStart(road, target.x, settings.trailWidth);
            Vector2 leave = start + new Vector2(0f, TrailLeadIn);
            Vector2 bend = Vector2.Lerp(leave, target, 0.5f) + new Vector2(sway, 0f);
            List<Vector2> samples = new();
            AreaLayoutBuilder.SampleSpline(new[] { start, leave }, samples);
            AreaLayoutBuilder.SampleSpline(new[] { leave, bend, target }, samples);
            HashSet<Vector3Int> painted = new();
            builder.Paint(samples, settings.trailWidth, painted);
            HashSet<Vector3Int> trail = new();
            foreach (Vector3Int cell in painted)
            {
                if (builder.Stone.Contains(cell))
                {
                    continue;
                }

                if (LavaDistance(cell) < LavaGap)
                {
                    return new HashSet<Vector3Int>();
                }

                trail.Add(cell);
            }

            return trail;
        }

        /// <summary>
        /// Keeps blocking props off the lava and the earth beside it.
        /// </summary>
        private void ReserveLava()
        {
            foreach (Vector3Int cell in builder.OrderedGround)
            {
                if (LavaDistance(cell) <= 1)
                {
                    builder.Reserved.Add(cell);
                }
            }
        }

        /// <summary>
        /// Scatters <paramref name="scatter"/>'s props over the earth cells of its zone that are not on or beside the
        /// lava.
        /// </summary>
        private void ScatterProps(Scatter scatter)
        {
            List<Vector3Int> candidates = new();
            foreach (Vector3Int cell in builder.OrderedGround)
            {
                if (LavaDistance(cell) > 1 && ZoneOf(cell) == scatter.Zone)
                {
                    candidates.Add(cell);
                }
            }

            builder.ScatterProps(scatter.Prefabs, scatter.Count, scatter.Spacing, candidates);
        }

        /// <summary>
        /// The zone of <paramref name="cell"/>: the border band along the edge, else the band of earth around the lava,
        /// else the interior.
        /// </summary>
        private Zone ZoneOf(Vector3Int cell)
        {
            if (builder.EdgeDistance(cell) <= settings.borderDepth)
            {
                return Zone.Border;
            }

            int distance = LavaDistance(cell);
            return distance > 0 && distance <= settings.shoreZoneDepth ? Zone.Shore : Zone.Interior;
        }

        /// <summary>
        /// Spreads the mobs over earth the lava gap from the lava, inside the border band, that the spawn can walk to
        /// around the lava, away from props, at least the configured distance from the spawn and from each other. Warns
        /// when fewer fit than asked for.
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
            candidates.RemoveAll(cell => LavaDistance(cell) < LavaGap);
            int placed = builder.PlaceMobs(settings.mobPrefab, wanted, candidates, settings.mobSpacing, new List<Vector3Int>());
            if (placed < wanted)
            {
                builder.Layout.Warnings.Add($"Placed {placed} of {wanted} mobs: not enough open ground {settings.mobDistanceFromSpawn} cells from the spawn and {settings.mobSpacing} apart.");
            }
        }

        /// <summary>
        /// Warns when a trail cell cannot be walked to from the spawn around the lava.
        /// </summary>
        private void CheckTrail(HashSet<Vector3Int> reachable)
        {
            int cut = 0;
            foreach (Vector3Int cell in builder.Gravel)
            {
                if (!reachable.Contains(cell))
                {
                    cut++;
                }
            }

            if (cut > 0)
            {
                builder.Layout.Warnings.Add($"{cut} trail cells cannot be walked to from the spawn.");
            }
        }

        /// <summary>
        /// Lays the campsite on the detail stream: a campfire and seats on interior earth, away from the paths, the
        /// spawn and the mobs (<see cref="AreaLayoutBuilder.LayCampsite"/>), its centre further from the lava and the
        /// paths than its radius plus two cells. The earth is its own trodden ground, so its patch is cleared of props
        /// and plants but not painted.
        /// </summary>
        private void LayCampsite()
        {
            if (settings.campfirePrefab == null)
            {
                return;
            }

            float clear = settings.campsiteRadius + CampsiteLavaMargin;
            HashSet<Vector3Int> nearPaths = AreaLayoutBuilder.CellsNear(builder.PathCells(), Mathf.CeilToInt(clear));
            AreaLayoutBuilder.Campsite campsite = new(settings.campfirePrefab, settings.campsiteSeats, settings.seatCount, settings.seatSpacing, settings.campsiteRadius);
            builder.LayCampsite(campsite, new HashSet<Vector3Int>(), settings.borderDepth, settings.pathClearance, settings.spawnClearRadius, cell => LavaDistance(cell) > clear && !nearPaths.Contains(cell));
        }

        /// <summary>
        /// How many steps (8 neighbours) <paramref name="cell"/> lies from the lava: 0 on it, and int.MaxValue off the
        /// earth or with no lava.
        /// </summary>
        private int LavaDistance(Vector3Int cell)
        {
            return AreaLayoutBuilder.StepsAt(lavaDistance, cell);
        }

    }
}
