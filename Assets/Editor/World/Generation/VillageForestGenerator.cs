using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lays out an area that joins three environments inside a ring of forest. A village of cottages faces a stone street
/// that enters from the map edge, with a fountain square, lamp posts, fenced yards and town props. Gravel trails lead
/// on from the street's end across an open clearing with plants and a few trees, and wind into a dense forest, each
/// forest trail ending in a glade. The party spawns on the street; most mobs are in the forest, some in the clearing.
/// Blocking props stay off the paths, a clearance beside them and the ground around the spawn and the glades, and every
/// path, door and mob can be reached from the spawn.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Village Forest Generator", fileName = "Generator_VillageForest")]
public sealed class VillageForestGenerator : AreaGenerator
{
    /// <summary>
    /// The environment a grass cell belongs to.
    /// </summary>
    public enum Region
    {
        Village,
        Clearing,
        Forest
    }

    /// <summary>
    /// Props scattered over one region.
    /// </summary>
    [Serializable]
    public class Scatter : PropScatter
    {
        [SerializeField] private Region region = Region.Forest;

        public Region Region => region;
    }

    /// <summary>
    /// One generation: the layout and the region of each of its grass cells.
    /// </summary>
    public sealed class Generation
    {
        /// <summary>
        /// Pairs <paramref name="layout"/> with the <paramref name="regions"/> of its grass cells.
        /// </summary>
        internal Generation(AreaLayout layout, Dictionary<Vector3Int, Region> regions)
        {
            Layout = layout;
            Regions = regions;
        }

        public AreaLayout Layout { get; }
        public IReadOnlyDictionary<Vector3Int, Region> Regions { get; }
    }

    private const float VillageSquareness = 3f;
    private const float ClearingSquareness = 2f;
    private const float TrailBendSpacing = 9f;
    private const float MinTrailLength = 10f;
    private const int TrailPointStride = 5;
    private const int TrailTargetTries = 24;
    private const float RuledOutRadius = 8f;

    [Header("Terrain")]
    [SerializeField] private TerrainLayer grass = new();
    [Tooltip("Generated empty: painted by hand. Grass with swaying weeds, drawn over the grass without an edge.")]
    [SerializeField] private TerrainLayer weeds = new();
    [Tooltip("Generated empty: painted by hand.")]
    [SerializeField] private TerrainLayer sand = new();
    [Tooltip("Generated empty: painted by hand.")]
    [SerializeField] private TerrainLayer mud = new();
    [SerializeField] private TerrainLayer gravel = new();
    [SerializeField] private TerrainLayer stone = new();
    [Tooltip("Generated empty: painted by hand. Drawn over every other layer, so it decides its cells: water cannot be walked.")]
    [SerializeField] private TerrainLayer water = new();

    [Header("Shape")]
    [Tooltip("Width and height of the area in cells, before its outline wobbles.")]
    [SerializeField] private Vector2Int size = new(92, 62);
    [Tooltip("Exponent of the outline's superellipse: 2 is an ellipse, higher is closer to a rectangle.")]
    [SerializeField, Range(2f, 8f)] private float squareness = 4f;
    [Tooltip("How far the outline wobbles in and out, as a fraction of its radius.")]
    [SerializeField, Range(0f, 0.3f)] private float edgeWobble = 0.06f;
    [Tooltip("Depth in cells of the band of forest along the edge of the whole area.")]
    [SerializeField, Min(1)] private int borderDepth = 3;

    [Header("Regions")]
    [Tooltip("Width and height of the village in cells, before its outline wobbles. It lies against the west or east edge.")]
    [SerializeField] private Vector2Int villageSize = new(46, 28);
    [Tooltip("Width and height of the clearing in cells, before its outline wobbles. It lies past the village, off to the north or south.")]
    [SerializeField] private Vector2Int clearingSize = new(30, 22);
    [Tooltip("How far the village's and the clearing's outlines wobble in and out, as a fraction of their radius.")]
    [SerializeField, Range(0f, 0.4f)] private float regionWobble = 0.2f;

    [Header("Paths")]
    [Tooltip("Width in cells of the stone street that enters from the map edge and runs through the village.")]
    [SerializeField, Min(1)] private int streetWidth = 3;
    [Tooltip("How far in cells the street wanders north and south. Keep it small: the cottages line up along it.")]
    [SerializeField, Min(0f)] private float streetMeander = 1.5f;
    [SerializeField, Min(1)] private int trailWidth = 2;
    [Tooltip("Gravel trails winding from the trail network into the forest, each ending in a glade.")]
    [SerializeField, Min(0)] private int forestTrailCount = 3;
    [Tooltip("How far trails wander sideways between bends, as a fraction of the distance between bends.")]
    [SerializeField, Range(0f, 1f)] private float trailWinding = 0.45f;
    [Tooltip("Cells beside every path kept free of blocking props.")]
    [SerializeField, Min(0)] private int pathClearance = 1;
    [Tooltip("Radius in cells of the open glade at the end of each forest trail.")]
    [SerializeField, Min(0)] private int gladeRadius = 3;

    [Header("Spawn")]
    [Tooltip("Where along the street the party arrives, from 0 (the map edge) to 1 (the village's inner end).")]
    [SerializeField, Range(0f, 1f)] private float spawnAlongStreet = 0.8f;
    [Tooltip("Radius in cells around the spawn that stays on grass and free of blocking props.")]
    [SerializeField, Min(0)] private int spawnClearRadius = 6;

    [Header("Village")]
    [Tooltip("Building placed along the north side of the street, facing it.")]
    [SerializeField] private GameObject buildingPrefab;
    [SerializeField, Min(0)] private int buildingCount = 3;
    [Tooltip("Column of the building's footprint, counted from its left, in front of which its door is.")]
    [SerializeField, Min(0)] private int doorColumn = 3;
    [Tooltip("Rows of yard between the fence along the street and the front of a building.")]
    [SerializeField, Min(1)] private int yardDepth = 3;
    [Tooltip("Columns of yard on each side of a building.")]
    [SerializeField, Min(0)] private int sideYard = 1;
    [Tooltip("Rows behind a building kept free of other props.")]
    [SerializeField, Min(0)] private int backYard = 2;
    [Tooltip("Smallest gap in cells between two yards.")]
    [SerializeField, Min(0)] private int lotGap = 1;
    [Tooltip("Fence placed along the front of every yard, with a gap for the path to the door.")]
    [SerializeField] private GameObject fencePrefab;
    [SerializeField] private GameObject lampPostPrefab;
    [Tooltip("Distance in cells between lamp posts along the street, which alternate sides.")]
    [SerializeField, Min(1f)] private float lampSpacing = 7f;
    [Tooltip("Centrepiece of the square, on a bed of grass inside the paving.")]
    [SerializeField] private GameObject fountainPrefab;
    [Tooltip("Where along the street the square opens to the south, from 0 (the map edge) to 1 (the village's inner end).")]
    [SerializeField, Range(0f, 1f)] private float squareAlongStreet = 0.45f;
    [Tooltip("Radius in cells of the paved square around the fountain.")]
    [SerializeField, Min(2f)] private float squareRadius = 4.5f;
    [Tooltip("Town props placed in the yards and around the square.")]
    [SerializeField] private List<GameObject> townProps = new();
    [SerializeField, Min(0)] private int townPropsPerBuilding = 4;
    [SerializeField, Min(0)] private int townPropsAroundSquare = 4;
    [Tooltip("Smallest distance in cells between two town props of one yard, or of the square.")]
    [SerializeField, Min(0f)] private float townPropSpacing = 2.5f;

    [Header("Props")]
    [Tooltip("Scatter layers, placed in this order after the village: earlier layers take the room first.")]
    [SerializeField] private List<Scatter> scatters = new();

    [Header("Mobs")]
    [SerializeField] private MobController mobPrefab;
    [SerializeField, Min(0)] private int villageMobCount;
    [SerializeField, Min(0)] private int clearingMobCount = 3;
    [SerializeField, Min(0)] private int forestMobCount = 6;
    [Tooltip("Smallest distance in cells from the party spawn to a mob spawn.")]
    [SerializeField, Min(0f)] private float mobDistanceFromSpawn = 14f;
    [Tooltip("Smallest distance in cells between two mob spawns.")]
    [SerializeField, Min(0f)] private float mobSpacing = 9f;

    public int PathClearance => pathClearance;
    public int SpawnClearRadius => spawnClearRadius;
    public int ForestTrailCount => forestTrailCount;
    public GameObject BuildingPrefab => buildingPrefab;
    public int BuildingCount => buildingCount;
    public int DoorColumn => doorColumn;
    public GameObject FencePrefab => fencePrefab;
    public GameObject LampPostPrefab => lampPostPrefab;
    public GameObject FountainPrefab => fountainPrefab;
    public MobController MobPrefab => mobPrefab;
    public float MobDistanceFromSpawn => mobDistanceFromSpawn;
    public float MobSpacing => mobSpacing;

    /// <summary>
    /// Grass, weeds, sand, mud, gravel, stone, then water: each layer is drawn over the ones before it and decides its
    /// cells, so the paths cross weeds, sand and mud and water covers everything. Weeds, sand, mud and water are
    /// generated empty.
    /// </summary>
    public override IReadOnlyList<TerrainLayer> TerrainLayers => new[] { grass, weeds, sand, mud, gravel, stone, water };

    /// <summary>
    /// How many mobs <paramref name="region"/> gets.
    /// </summary>
    public int MobCount(Region region)
    {
        return region switch
        {
            Region.Village => villageMobCount,
            Region.Clearing => clearingMobCount,
            _ => forestMobCount
        };
    }

    /// <summary>
    /// Lays out the area from <paramref name="layoutSeed"/>.
    /// </summary>
    public override AreaLayout Generate(int layoutSeed)
    {
        return GenerateWithRegions(layoutSeed).Layout;
    }

    /// <summary>
    /// Lays out the area from <paramref name="layoutSeed"/> and tells which region each grass cell belongs to.
    /// </summary>
    public Generation GenerateWithRegions(int layoutSeed)
    {
        return new Run(this, layoutSeed).Build();
    }

    /// <summary>
    /// The state of one generation: the shared builder with the cells laid so far, the region of every grass cell,
    /// the street, trail and square geometry, and the village's lots and doors.
    /// </summary>
    private sealed class Run
    {
        private readonly VillageForestGenerator settings;
        private readonly AreaLayoutBuilder builder;
        private readonly Dictionary<Vector3Int, Region> regions = new();
        private readonly List<Vector2> street = new();
        private readonly Dictionary<int, int> streetTop = new();
        private readonly List<Vector2> trailPoints = new();
        private readonly List<Vector2> pathPoints = new();
        private readonly List<Vector3Int> glades = new();
        private readonly List<RectInt> yards = new();
        private readonly List<RectInt> lots = new();
        private readonly List<Vector3Int> doorsteps = new();
        private readonly Vector2 halfSize;
        private readonly Vector2 villageHalf;
        private readonly Vector2 clearingHalf;
        private float side;
        private Vector2 villageCentre;
        private Vector2 clearingCentre;
        private Vector2 squareCentre;
        private Vector3Int fountainCorner;
        private bool hasSquare;

        /// <summary>
        /// Prepares a generation of <paramref name="generator"/>'s settings from <paramref name="seed"/>.
        /// </summary>
        public Run(VillageForestGenerator generator, int seed)
        {
            settings = generator;
            builder = new AreaLayoutBuilder(seed);
            halfSize = new Vector2(Mathf.Max(24, generator.size.x), Mathf.Max(16, generator.size.y)) * 0.5f;
            villageHalf = new Vector2(Mathf.Max(8, generator.villageSize.x), Mathf.Max(8, generator.villageSize.y)) * 0.5f;
            clearingHalf = new Vector2(Mathf.Max(8, generator.clearingSize.x), Mathf.Max(8, generator.clearingSize.y)) * 0.5f;
        }

        /// <summary>
        /// How many cells a trail's centre line keeps from a lot, so that neither the trail nor its clearance reaches
        /// into it.
        /// </summary>
        private int LotMargin => settings.trailWidth / 2 + settings.pathClearance + 1;

        /// <summary>
        /// Lays the ground and its regions, the street, the spawn, the square, the buildings with their paths and the
        /// trails; furnishes the village; scatters the props layer by layer; spreads the mobs; and checks what the spawn
        /// can reach.
        /// </summary>
        public Generation Build()
        {
            builder.LayGrass(halfSize, settings.squareness, settings.edgeWobble);
            AssignRegions();
            LayStreet();
            PlaceSpawn();
            builder.ReserveDisc(builder.Layout.SpawnCell, settings.spawnClearRadius);
            LaySquare();
            PlaceBuildings();
            LayTrails();
            KeepConnectedPaths();
            builder.ReservePathClearance(settings.pathClearance);
            foreach (Vector3Int glade in glades)
            {
                builder.ReserveDisc(glade, settings.gladeRadius);
            }

            PlaceFountain();
            PlaceLampPosts();
            PlaceFences();
            PlaceTownProps();
            foreach (RectInt lot in lots)
            {
                foreach (Vector2Int cell in lot.allPositionsWithin)
                {
                    builder.Reserved.Add(new Vector3Int(cell.x, cell.y, 0));
                }
            }

            foreach (Scatter scatter in settings.scatters)
            {
                if (scatter != null)
                {
                    ScatterProps(scatter);
                }
            }

            PlaceMobs();
            CheckReachability();
            return new Generation(builder.Layout, regions);
        }

        /// <summary>
        /// Puts the village against the west or east edge and the clearing past it to the north or south, each a
        /// wobbly blob, and gives every grass cell its region: the band along the edge and everything outside both
        /// blobs is forest.
        /// </summary>
        private void AssignRegions()
        {
            side = builder.Next(2) == 0 ? -1f : 1f;
            villageCentre = new Vector2(side * (halfSize.x - villageHalf.x - builder.Range(0f, 3f)), builder.Range(-0.12f, 0.12f) * halfSize.y);
            float north = builder.Next(2) == 0 ? 1f : -1f;
            clearingCentre = new Vector2(-side * builder.Range(0.25f, 0.4f) * halfSize.x, north * builder.Range(0.05f, 0.3f) * halfSize.y);
            Predicate<Vector2> inVillage = builder.WobblyShape(villageCentre, villageHalf, VillageSquareness, settings.regionWobble);
            Predicate<Vector2> inClearing = builder.WobblyShape(clearingCentre, clearingHalf, ClearingSquareness, settings.regionWobble);
            foreach (Vector3Int cell in builder.OrderedGrass)
            {
                Vector2 centre = CellCentre(cell);
                Region region = Region.Forest;
                if (builder.EdgeDistance(cell) > settings.borderDepth)
                {
                    region = inVillage(centre) ? Region.Village : inClearing(centre) ? Region.Clearing : Region.Forest;
                }

                regions[cell] = region;
            }
        }

        /// <summary>
        /// Runs the stone street through four points from beyond the village's edge of the map to the village's inner
        /// end, each within the meander of the village's middle, paints it on the grass and records the top row of
        /// every column it crosses.
        /// </summary>
        private void LayStreet()
        {
            float meander = settings.streetMeander;
            float y = villageCentre.y;
            Vector2[] points =
            {
                new(side * (halfSize.x * (1f + settings.edgeWobble) + 2f), y + builder.Range(-1f, 1f) * meander),
                new(villageCentre.x + side * villageHalf.x * 0.5f, y + builder.Range(-1f, 1f) * meander),
                new(villageCentre.x - side * villageHalf.x * 0.25f, y + builder.Range(-1f, 1f) * meander),
                new(villageCentre.x - side * villageHalf.x, y + builder.Range(-1f, 1f) * meander)
            };
            AreaLayoutBuilder.SampleSpline(points, street);
            HashSet<Vector3Int> cells = new();
            builder.Paint(street, settings.streetWidth, cells);
            foreach (Vector3Int cell in builder.OrderedGrass)
            {
                if (cells.Contains(cell))
                {
                    builder.Stone.Add(cell);
                    streetTop[cell.x] = streetTop.TryGetValue(cell.x, out int top) ? Mathf.Max(top, cell.y) : cell.y;
                }
            }

            for (int i = 0; i < street.Count; i += TrailPointStride)
            {
                pathPoints.Add(street[i]);
            }
        }

        /// <summary>
        /// Puts the party spawn on the street at the configured point, or at the nearest point along it with grass all
        /// around it for the clear radius.
        /// </summary>
        private void PlaceSpawn()
        {
            int target = Mathf.RoundToInt(settings.spawnAlongStreet * (street.Count - 1));
            List<Vector2> preferred = new();
            for (int offset = 0; preferred.Count < street.Count; offset++)
            {
                if (target + offset < street.Count)
                {
                    preferred.Add(street[target + offset]);
                }

                if (offset > 0 && target - offset >= 0)
                {
                    preferred.Add(street[target - offset]);
                }
            }

            builder.PlaceSpawn(preferred, settings.spawnClearRadius);
        }

        /// <summary>
        /// Lays a gravel trail from the street's end across the clearing, then the forest trails.
        /// </summary>
        private void LayTrails()
        {
            Vector2 junction = street[street.Count - 1];
            Vector2 across = new(clearingCentre.x - side * clearingHalf.x * 0.6f, clearingCentre.y + builder.Range(-0.4f, 0.4f) * clearingHalf.y);
            List<Vector2> points = Winding(junction, clearingCentre);
            points.RemoveAt(points.Count - 1);
            points.AddRange(Winding(clearingCentre, across));
            List<Vector2> samples = new();
            AreaLayoutBuilder.SampleSpline(points, samples);
            PaintTrail(samples);
            LayForestTrails();
        }

        /// <summary>
        /// Lays the forest trails one after another, each aimed at forest far from the paths laid before it. Warns when
        /// fewer fit than asked for.
        /// </summary>
        private void LayForestTrails()
        {
            int depth = settings.borderDepth + settings.gladeRadius + 1;
            List<Vector3Int> cells = new();
            List<float> distances = new();
            foreach (Vector3Int cell in builder.OrderedGrass)
            {
                if (regions[cell] == Region.Forest && builder.EdgeDistance(cell) >= depth)
                {
                    cells.Add(cell);
                    distances.Add(SquaredDistanceTo(pathPoints, 0, CellCentre(cell)));
                }
            }

            int laid = 0;
            for (int i = 0; i < settings.forestTrailCount && cells.Count > 0; i++)
            {
                int known = pathPoints.Count;
                if (!LayForestTrail(cells, distances))
                {
                    continue;
                }

                laid++;
                for (int j = 0; j < cells.Count; j++)
                {
                    distances[j] = Mathf.Min(distances[j], SquaredDistanceTo(pathPoints, known, CellCentre(cells[j])));
                }
            }

            if (laid < settings.forestTrailCount)
            {
                builder.Layout.Warnings.Add($"Laid {laid} of {settings.forestTrailCount} forest trails: no more forest far enough from the paths could be reached without crossing the village.");
            }
        }

        /// <summary>
        /// Winds a gravel trail from the nearest point of the trails laid so far to one of <paramref name="cells"/>,
        /// picked at random among those whose <paramref name="distances"/> to the paths are greatest, keeping it out
        /// of the village and on the grass, and opens a glade at its end. A target that fails rules out the cells
        /// around it, and the next try picks among the furthest of the rest. Returns whether a trail was laid.
        /// </summary>
        private bool LayForestTrail(List<Vector3Int> cells, List<float> distances)
        {
            List<int> order = new();
            for (int i = 0; i < cells.Count; i++)
            {
                order.Add(i);
            }

            order.Sort((a, b) => distances[a] != distances[b] ? distances[b].CompareTo(distances[a]) : a.CompareTo(b));
            int poolSize = Mathf.Max(1, order.Count / 8);
            bool[] ruledOut = new bool[cells.Count];
            List<int> pool = new();
            List<Vector2> samples = new();
            int next = 0;
            for (int attempt = 0; attempt < TrailTargetTries; attempt++)
            {
                pool.RemoveAll(index => ruledOut[index]);
                for (; pool.Count < poolSize && next < order.Count; next++)
                {
                    if (!ruledOut[order[next]])
                    {
                        pool.Add(order[next]);
                    }
                }

                if (pool.Count == 0)
                {
                    return false;
                }

                int pick = pool[builder.Next(pool.Count)];
                Vector2 target = CellCentre(cells[pick]);
                Vector2 start = Nearest(trailPoints, target);
                samples.Clear();
                if (Vector2.Distance(start, target) >= MinTrailLength)
                {
                    AreaLayoutBuilder.SampleSpline(Winding(start, target), samples);
                }

                if (samples.Count > 0 && StaysOutOfVillage(samples))
                {
                    PaintTrail(samples);
                    glades.Add(cells[pick]);
                    return true;
                }

                for (int i = 0; i < cells.Count; i++)
                {
                    ruledOut[i] |= (CellCentre(cells[i]) - target).sqrMagnitude <= RuledOutRadius * RuledOutRadius;
                }
            }

            return false;
        }

        /// <summary>
        /// Points from <paramref name="from"/> to <paramref name="to"/> for a spline that bends about every
        /// <see cref="TrailBendSpacing"/> cells, each bend pushed sideways by up to the winding share of the distance
        /// between bends, or left on the straight line where the push would take it into the band along the edge.
        /// </summary>
        private List<Vector2> Winding(Vector2 from, Vector2 to)
        {
            List<Vector2> points = new() { from };
            float length = Vector2.Distance(from, to);
            int bends = Mathf.Max(1, Mathf.RoundToInt(length / TrailBendSpacing));
            Vector2 across = Vector2.Perpendicular((to - from) / Mathf.Max(length, 0.001f));
            float reach = settings.trailWinding * length / (bends + 1);
            for (int i = 1; i <= bends; i++)
            {
                Vector2 onLine = Vector2.Lerp(from, to, i / (float)(bends + 1));
                Vector2 bent = onLine + across * (builder.Range(-1f, 1f) * reach);
                points.Add(builder.EdgeDistance(AreaLayoutBuilder.CellAt(bent)) > settings.borderDepth + 1 ? bent : onLine);
            }

            points.Add(to);
            return points;
        }

        /// <summary>
        /// Whether every sample lies on a grass cell outside the village, further than <see cref="LotMargin"/> from
        /// every lot.
        /// </summary>
        private bool StaysOutOfVillage(List<Vector2> samples)
        {
            int margin = LotMargin;
            foreach (Vector2 sample in samples)
            {
                Vector3Int cell = AreaLayoutBuilder.CellAt(sample);
                if (!regions.TryGetValue(cell, out Region region) || region == Region.Village)
                {
                    return false;
                }

                foreach (RectInt lot in lots)
                {
                    if (cell.x >= lot.xMin - margin && cell.x < lot.xMax + margin && cell.y >= lot.yMin - margin && cell.y < lot.yMax + margin)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Paints <paramref name="samples"/> as gravel and keeps every few of them as points later trails may branch
        /// from.
        /// </summary>
        private void PaintTrail(List<Vector2> samples)
        {
            builder.Paint(samples, settings.trailWidth, builder.Gravel);
            for (int i = 0; i < samples.Count; i += TrailPointStride)
            {
                trailPoints.Add(samples[i]);
                pathPoints.Add(samples[i]);
            }
        }

        /// <summary>
        /// Drops every path cell that is not 4-connected through path cells to the spawn on the street: a stray cell
        /// left where the edge of the grass clipped a path, touching the rest only at a corner. Does nothing when the
        /// spawn had to leave the street.
        /// </summary>
        private void KeepConnectedPaths()
        {
            if (!builder.IsPath(builder.Layout.SpawnCell))
            {
                return;
            }

            HashSet<Vector3Int> network = AreaLayoutBuilder.FloodFill(builder.Layout.SpawnCell, builder.IsPath);
            foreach (Vector3Int cell in builder.OrderedGrass)
            {
                if (!network.Contains(cell))
                {
                    builder.Stone.Remove(cell);
                    builder.Gravel.Remove(cell);
                }
            }
        }

        /// <summary>
        /// Paves a disc of stone opening south off the street at the configured point, around a bed of grass that
        /// holds the fountain's footprint with the path clearance around it.
        /// </summary>
        private void LaySquare()
        {
            if (settings.fountainPrefab == null || !PropGridSnapping.TryGetFootprint(settings.fountainPrefab, out Rect footprint))
            {
                builder.Layout.Warnings.Add("No fountain prefab with a footprint is set; the village has no square.");
                return;
            }

            Vector2Int covered = PropGridSnapping.CellsCovered(footprint, Vector2.one);
            Vector2 onStreet = street[Mathf.RoundToInt(settings.squareAlongStreet * (street.Count - 1))];
            Vector2 centre = onStreet + new Vector2(0f, -(settings.streetWidth * 0.5f + settings.squareRadius - 0.5f));
            fountainCorner = new Vector3Int(Mathf.RoundToInt(centre.x - covered.x * 0.5f), Mathf.RoundToInt(centre.y - covered.y * 0.5f), 0);
            squareCentre = new Vector2(fountainCorner.x + covered.x * 0.5f, fountainCorner.y + covered.y * 0.5f);
            int bed = settings.pathClearance;
            RectInt bedCells = new(fountainCorner.x - bed, fountainCorner.y - bed, covered.x + 2 * bed, covered.y + 2 * bed);
            float radius = settings.squareRadius;
            foreach (Vector3Int cell in builder.OrderedGrass)
            {
                if ((CellCentre(cell) - squareCentre).sqrMagnitude <= radius * radius && !bedCells.Contains(new Vector2Int(cell.x, cell.y)))
                {
                    builder.Stone.Add(cell);
                }
            }

            hasSquare = true;
        }

        /// <summary>
        /// Stands the fountain in the middle of the square.
        /// </summary>
        private void PlaceFountain()
        {
            if (hasSquare && !builder.TryPlaceFootprint(settings.fountainPrefab, fountainCorner))
            {
                builder.Layout.Warnings.Add("The fountain did not fit in its square.");
            }
        }

        /// <summary>
        /// Walks the street from the map edge towards its inner end, where the trails start, and puts a building on
        /// every lot that fits along its north side, until the configured number stand. Warns when fewer fit.
        /// </summary>
        private void PlaceBuildings()
        {
            int wanted = settings.buildingCount;
            if (wanted == 0)
            {
                return;
            }

            GameObject prefab = settings.buildingPrefab;
            if (prefab == null || !PropGridSnapping.TryGetFootprint(prefab, out Rect footprint))
            {
                builder.Layout.Warnings.Add("No building prefab with a footprint is set; no buildings were placed.");
                return;
            }

            Vector2Int covered = PropGridSnapping.CellsCovered(footprint, Vector2.one);
            int width = covered.x + 2 * settings.sideYard;
            int step = side < 0f ? 1 : -1;
            int last = AreaLayoutBuilder.CellAt(street[street.Count - 1]).x - step * LotMargin;
            int placed = 0;
            for (int column = AreaLayoutBuilder.CellAt(street[0]).x; placed < wanted && (column + step * (width - 1) - last) * step <= 0;)
            {
                int left = step > 0 ? column : column - width + 1;
                if (TryPlaceLot(prefab, covered, left, width))
                {
                    placed++;
                    column += step * (width + settings.lotGap);
                }
                else
                {
                    column += step;
                }
            }

            if (placed < wanted)
            {
                builder.Layout.Warnings.Add($"Placed {placed} of {wanted} buildings: the street through the village has no room for more lots.");
            }
        }

        /// <summary>
        /// Puts a building on the lot of <paramref name="width"/> columns from <paramref name="left"/> north of the
        /// street, when every column of it borders the street: a row of clearance, the fence row, the yard, then the
        /// building facing the street, then the back yard. The lot must be free grass off the paths, the edge band and
        /// the clearing; once built, all of it belongs to the village. A stone path runs from the street to the
        /// doorstep, the cell in front of the door. Returns whether it was placed.
        /// </summary>
        private bool TryPlaceLot(GameObject prefab, Vector2Int covered, int left, int width)
        {
            int top = int.MinValue;
            for (int x = left; x < left + width; x++)
            {
                if (!streetTop.TryGetValue(x, out int row))
                {
                    return false;
                }

                top = Mathf.Max(top, row);
            }

            int front = top + settings.pathClearance + 1;
            int bottom = front + 1 + settings.yardDepth;
            Vector3Int corner = new(left + settings.sideYard, bottom, 0);
            RectInt lot = new(left, front, width, bottom + covered.y + settings.backYard - front);
            foreach (Vector2Int position in lot.allPositionsWithin)
            {
                Vector3Int cell = new(position.x, position.y, 0);
                if (!builder.Grass.Contains(cell) || builder.IsPath(cell) || builder.Occupied.Contains(cell)
                    || builder.EdgeDistance(cell) <= settings.borderDepth || regions[cell] == Region.Clearing)
                {
                    return false;
                }
            }

            int door = corner.x + Mathf.Clamp(settings.doorColumn, 0, covered.x - 1);
            int pathEnd = streetTop[door] + 1;
            for (int y = bottom - 1; y >= pathEnd; y--)
            {
                Vector3Int cell = new(door, y, 0);
                if (!builder.Grass.Contains(cell) || builder.Occupied.Contains(cell))
                {
                    return false;
                }
            }

            if (!builder.TryPlaceFootprint(prefab, corner))
            {
                return false;
            }

            for (int y = bottom - 1; y >= pathEnd; y--)
            {
                Vector3Int cell = new(door, y, 0);
                builder.Stone.Add(cell);
                builder.ReserveSquare(cell, settings.pathClearance);
            }

            foreach (Vector2Int position in lot.allPositionsWithin)
            {
                regions[new Vector3Int(position.x, position.y, 0)] = Region.Village;
            }

            doorsteps.Add(new Vector3Int(door, bottom - 1, 0));
            yards.Add(new RectInt(left, front + 1, width, bottom + covered.y - front - 1));
            lots.Add(lot);
            return true;
        }

        /// <summary>
        /// Stands lamp posts along the street through the village, about the configured spacing apart, alternately
        /// north and south of it just beyond its clearance, and outside the lots, so those north of it stand between
        /// the yards rather than in their fences.
        /// </summary>
        private void PlaceLampPosts()
        {
            GameObject lamp = settings.lampPostPrefab;
            if (lamp == null)
            {
                return;
            }

            bool north = builder.Next(2) == 0;
            float travelled = settings.lampSpacing * 0.5f;
            float offset = settings.streetWidth * 0.5f + settings.pathClearance + 0.5f;
            for (int i = 1; i < street.Count - 1; i++)
            {
                travelled += Vector2.Distance(street[i], street[i - 1]);
                if (travelled < settings.lampSpacing)
                {
                    continue;
                }

                Vector2 tangent = (street[i + 1] - street[i - 1]).normalized;
                Vector2 normal = new(-tangent.y, tangent.x);
                if (normal.y < 0f)
                {
                    normal = -normal;
                }

                Vector3Int cell = AreaLayoutBuilder.CellAt(street[i] + normal * (north ? offset : -offset));
                if (regions.TryGetValue(cell, out Region region) && region == Region.Village && !IsInLot(cell) && builder.TryPlaceFootprint(lamp, cell))
                {
                    travelled = 0f;
                    north = !north;
                }
            }
        }

        /// <summary>
        /// Runs a fence along the front row of every lot, from either side of the gate the path to the door leaves
        /// outwards, segment after segment wherever one fits.
        /// </summary>
        private void PlaceFences()
        {
            GameObject fence = settings.fencePrefab;
            if (fence == null || !PropGridSnapping.TryGetFootprint(fence, out Rect footprint))
            {
                return;
            }

            int length = PropGridSnapping.CellsCovered(footprint, Vector2.one).x;
            for (int i = 0; i < lots.Count; i++)
            {
                RectInt lot = lots[i];
                int gate = doorsteps[i].x;
                for (int x = gate - settings.pathClearance - length; x >= lot.xMin;)
                {
                    x -= builder.TryPlaceFootprint(fence, new Vector3Int(x, lot.yMin, 0)) ? length : 1;
                }

                for (int x = gate + settings.pathClearance + 1; x + length <= lot.xMax;)
                {
                    x += builder.TryPlaceFootprint(fence, new Vector3Int(x, lot.yMin, 0)) ? length : 1;
                }
            }
        }

        /// <summary>
        /// Scatters town props over each yard, in front of and beside its building, and around the square.
        /// </summary>
        private void PlaceTownProps()
        {
            foreach (RectInt yard in yards)
            {
                List<Vector3Int> cells = new();
                foreach (Vector2Int position in yard.allPositionsWithin)
                {
                    cells.Add(new Vector3Int(position.x, position.y, 0));
                }

                builder.ScatterProps(settings.townProps, settings.townPropsPerBuilding, settings.townPropSpacing, cells);
            }

            if (!hasSquare)
            {
                return;
            }

            float inner = settings.squareRadius + settings.pathClearance + 0.5f;
            float outer = inner + 2f;
            List<Vector3Int> ring = new();
            foreach (Vector3Int cell in builder.OrderedGrass)
            {
                float distance = Vector2.Distance(CellCentre(cell), squareCentre);
                if (distance > inner && distance <= outer && cell.y <= squareCentre.y)
                {
                    ring.Add(cell);
                }
            }

            builder.ScatterProps(settings.townProps, settings.townPropsAroundSquare, settings.townPropSpacing, ring);
        }

        /// <summary>
        /// Scatters <paramref name="scatter"/>'s props over the grass cells of its region.
        /// </summary>
        private void ScatterProps(Scatter scatter)
        {
            List<Vector3Int> candidates = new();
            foreach (Vector3Int cell in builder.OrderedGrass)
            {
                if (regions[cell] == scatter.Region)
                {
                    candidates.Add(cell);
                }
            }

            builder.ScatterProps(scatter.Prefabs, scatter.Count, scatter.Spacing, candidates);
        }

        /// <summary>
        /// Spreads each region's mobs over its open cells inside the edge band that the spawn can walk to, away from
        /// props, at least the configured distance from the spawn and from every other mob: the clearing first, then
        /// the forest, then the village. Warns for a region where fewer fit than asked for.
        /// </summary>
        private void PlaceMobs()
        {
            if (settings.villageMobCount + settings.clearingMobCount + settings.forestMobCount == 0)
            {
                return;
            }

            if (settings.mobPrefab == null)
            {
                builder.Layout.Warnings.Add("No mob prefab is set; no mobs were placed.");
                return;
            }

            List<Vector3Int> open = builder.MobCells(builder.ReachableFromSpawn(), settings.borderDepth, settings.mobDistanceFromSpawn);
            List<Vector3Int> placed = new();
            PlaceMobsIn(Region.Clearing, open, placed);
            PlaceMobsIn(Region.Forest, open, placed);
            PlaceMobsIn(Region.Village, open, placed);
        }

        /// <summary>
        /// Places <paramref name="region"/>'s mobs on the cells of <paramref name="open"/> in it, apart from every
        /// spawn in <paramref name="placed"/>.
        /// </summary>
        private void PlaceMobsIn(Region region, List<Vector3Int> open, List<Vector3Int> placed)
        {
            int wanted = settings.MobCount(region);
            if (wanted == 0)
            {
                return;
            }

            List<Vector3Int> candidates = new();
            foreach (Vector3Int cell in open)
            {
                if (regions[cell] == region)
                {
                    candidates.Add(cell);
                }
            }

            int added = builder.PlaceMobs(settings.mobPrefab, wanted, candidates, settings.mobSpacing, placed);
            if (added < wanted)
            {
                builder.Layout.Warnings.Add($"Placed {added} of {wanted} mobs in the {region.ToString().ToLowerInvariant()}: not enough open ground {settings.mobDistanceFromSpawn} cells from the spawn and {settings.mobSpacing} from other mobs.");
            }
        }

        /// <summary>
        /// Warns about path cells, doorsteps and regions the spawn cannot walk to once every prop stands.
        /// </summary>
        private void CheckReachability()
        {
            HashSet<Vector3Int> reachable = builder.ReachableFromSpawn();
            int stranded = 0;
            bool[] reached = new bool[3];
            foreach (Vector3Int cell in builder.OrderedGrass)
            {
                bool isReachable = reachable.Contains(cell);
                stranded += builder.IsPath(cell) && !isReachable ? 1 : 0;
                reached[(int)regions[cell]] |= isReachable;
            }

            if (stranded > 0)
            {
                builder.Layout.Warnings.Add($"{stranded} path cells cannot be reached from the spawn.");
            }

            foreach (Vector3Int doorstep in doorsteps)
            {
                if (!reachable.Contains(doorstep))
                {
                    builder.Layout.Warnings.Add($"The door at {doorstep} cannot be reached from the spawn.");
                }
            }

            for (int i = 0; i < reached.Length; i++)
            {
                if (!reached[i])
                {
                    builder.Layout.Warnings.Add($"No cell of the {((Region)i).ToString().ToLowerInvariant()} can be reached from the spawn.");
                }
            }
        }

        /// <summary>
        /// Whether <paramref name="cell"/> lies in one of the lots.
        /// </summary>
        private bool IsInLot(Vector3Int cell)
        {
            foreach (RectInt lot in lots)
            {
                if (lot.Contains(new Vector2Int(cell.x, cell.y)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The centre of <paramref name="cell"/>.
        /// </summary>
        private static Vector2 CellCentre(Vector3Int cell)
        {
            return new Vector2(cell.x + 0.5f, cell.y + 0.5f);
        }

        /// <summary>
        /// The point of <paramref name="points"/> nearest to <paramref name="target"/>, the first on a tie.
        /// </summary>
        private static Vector2 Nearest(List<Vector2> points, Vector2 target)
        {
            Vector2 nearest = points[0];
            foreach (Vector2 point in points)
            {
                if ((point - target).sqrMagnitude < (nearest - target).sqrMagnitude)
                {
                    nearest = point;
                }
            }

            return nearest;
        }

        /// <summary>
        /// The squared distance from <paramref name="target"/> to the nearest of <paramref name="points"/> from index
        /// <paramref name="first"/> on.
        /// </summary>
        private static float SquaredDistanceTo(List<Vector2> points, int first, Vector2 target)
        {
            float nearest = float.MaxValue;
            for (int i = first; i < points.Count; i++)
            {
                nearest = Mathf.Min(nearest, (points[i] - target).sqrMagnitude);
            }

            return nearest;
        }
    }
}
