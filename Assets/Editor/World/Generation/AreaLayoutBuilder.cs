using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

/// <summary>
/// The shared state and steps of one generation, used by the generators' runs: the seeded random source, the layout
/// being filled with its ground (the base layer every other one lies on: grass, or the snowfield's snow), gravel and
/// stone cells (its other layers are the generators' own to fill, through
/// <see cref="AreaLayout.TerrainCells(AreaGenerator.TerrainLayer)"/>), how deep each ground cell lies inside the edge,
/// and which cells are kept free of blocking props, covered by one, or hold a walk-through prop. Layers are found by the
/// generator's own layer objects, never by a fixed index, since each generator lists only the layers it uses.
/// Iteration always goes through ordered lists, never hash sets, so a seed gives the same layout every time. Terrain
/// laid after everything else draws from a second stream seeded from the same seed (<see cref="StartDetailStream"/>),
/// so adding such a step leaves every earlier draw, and so every path, prop and mob, where it was.
/// </summary>
internal sealed class AreaLayoutBuilder
{
    private const float SampleStep = 0.2f;
    private const float PathEdgeAllowance = 0.05f;
    private const int OutlineHarmonics = 4;
    private const int DetailStreamFactor = 486187739;
    private const int DetailStreamSalt = 1013904223;

    private static readonly Vector3Int[] Sides = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down };

    /// <summary>
    /// A campsite's settings for <see cref="LayCampsite"/>: the campfire in its middle, the seats around it, how many
    /// and how far apart, and the radius of its patch of dirt.
    /// </summary>
    public sealed class Campsite
    {
        /// <summary>
        /// A campsite of <paramref name="campfire"/> and <paramref name="seatCount"/> of <paramref name="seats"/>,
        /// <paramref name="seatSpacing"/> apart, on dirt of <paramref name="radius"/>.
        /// </summary>
        public Campsite(GameObject campfire, IReadOnlyList<GameObject> seats, int seatCount, float seatSpacing, float radius)
        {
            Campfire = campfire;
            Seats = seats;
            SeatCount = seatCount;
            SeatSpacing = seatSpacing;
            Radius = radius;
        }

        public GameObject Campfire { get; }
        public IReadOnlyList<GameObject> Seats { get; }
        public int SeatCount { get; }
        public float SeatSpacing { get; }
        public float Radius { get; }
    }

    private readonly int seed;
    private readonly List<HashSet<Vector3Int>> otherPaths = new();
    private Random random;
    private readonly List<Vector3Int> orderedGround = new();
    private readonly Dictionary<Vector3Int, int> edgeDistance = new();

    /// <summary>
    /// Prepares an empty layout of <paramref name="layers"/>, a generator's terrain layers, whose
    /// <paramref name="ground"/>, <paramref name="gravel"/> and <paramref name="stone"/> layers the shared steps lay, and
    /// a random source seeded with <paramref name="seed"/>. The cells of <paramref name="otherPaths"/> (the Village
    /// Forest's plaza) count as paths too, like gravel and stone.
    /// </summary>
    public AreaLayoutBuilder(int seed, IReadOnlyList<AreaGenerator.TerrainLayer> layers, AreaGenerator.TerrainLayer ground, AreaGenerator.TerrainLayer gravel, AreaGenerator.TerrainLayer stone, params AreaGenerator.TerrainLayer[] otherPaths)
    {
        this.seed = seed;
        random = new Random(seed);
        Layout = new AreaLayout(layers);
        Ground = Layout.TerrainCells(ground);
        Gravel = Layout.TerrainCells(gravel);
        Stone = Layout.TerrainCells(stone);
        foreach (AreaGenerator.TerrainLayer layer in otherPaths)
        {
            this.otherPaths.Add(Layout.TerrainCells(layer));
        }
    }

    public AreaLayout Layout { get; }
    /// <summary>
    /// The ground: the base layer the area's outline is laid in, which every other layer lies on.
    /// </summary>
    public HashSet<Vector3Int> Ground { get; }
    public HashSet<Vector3Int> Gravel { get; }
    public HashSet<Vector3Int> Stone { get; }

    /// <summary>
    /// The ground cells in the order they were laid, rows from the bottom and cells from the left.
    /// </summary>
    public IReadOnlyList<Vector3Int> OrderedGround => orderedGround;

    /// <summary>
    /// Cells kept free of blocking props.
    /// </summary>
    public HashSet<Vector3Int> Reserved { get; } = new();

    /// <summary>
    /// Cells covered by a blocking prop's footprint.
    /// </summary>
    public HashSet<Vector3Int> Occupied { get; } = new();

    /// <summary>
    /// Cells holding a walk-through prop.
    /// </summary>
    public HashSet<Vector3Int> Decorated { get; } = new();

    /// <summary>
    /// From here on every random draw (<see cref="Range"/>, <see cref="Next"/>, <see cref="Shuffle{T}"/>,
    /// <see cref="WobblyShape"/>, <see cref="ScatterProps"/>) comes from a second stream seeded from the layout's seed,
    /// for terrain a generator lays after its props and mobs: what it lays stays deterministic, and adding it changes
    /// nothing drawn before.
    /// </summary>
    public void StartDetailStream()
    {
        random = new Random(unchecked(seed * DetailStreamFactor + DetailStreamSalt));
    }

    /// <summary>
    /// Lays the ground inside a superellipse of <paramref name="halfSize"/> around the origin whose radius wobbles by up
    /// to <paramref name="edgeWobble"/> (<see cref="WobblyShape"/>), keeps only its largest 4-connected region, and
    /// records how deep every ground cell lies inside the edge.
    /// </summary>
    public void LayGround(Vector2 halfSize, float squareness, float edgeWobble)
    {
        Predicate<Vector2> contains = WobblyShape(Vector2.zero, halfSize, squareness, edgeWobble);
        int reachX = Mathf.CeilToInt(halfSize.x * (1f + edgeWobble)) + 1;
        int reachY = Mathf.CeilToInt(halfSize.y * (1f + edgeWobble)) + 1;
        List<Vector3Int> inside = new();
        for (int y = -reachY; y < reachY; y++)
        {
            for (int x = -reachX; x < reachX; x++)
            {
                if (contains(new Vector2(x + 0.5f, y + 0.5f)))
                {
                    inside.Add(new Vector3Int(x, y, 0));
                }
            }
        }

        KeepLargestRegion(inside);
        MeasureEdgeDistance();
    }

    /// <summary>
    /// A random superellipse of <paramref name="halfSize"/> around <paramref name="centre"/> (<paramref name="squareness"/>
    /// 2 is an ellipse, higher is squarer) whose radius wobbles in and out by up to <paramref name="wobble"/> of itself
    /// with a few random harmonics around it, as a test of whether a point lies inside.
    /// </summary>
    public Predicate<Vector2> WobblyShape(Vector2 centre, Vector2 halfSize, float squareness, float wobble)
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

        return point =>
        {
            float nx = (point.x - centre.x) / halfSize.x;
            float ny = (point.y - centre.y) / halfSize.y;
            float radius = Mathf.Pow(Mathf.Pow(Mathf.Abs(nx), squareness) + Mathf.Pow(Mathf.Abs(ny), squareness), 1f / squareness);
            float angle = Mathf.Atan2(ny, nx);
            float offset = 0f;
            for (int i = 0; i < OutlineHarmonics; i++)
            {
                offset += amplitudes[i] * Mathf.Sin((i + 2) * angle + phases[i]);
            }

            return radius <= 1f + wobble * offset / total;
        };
    }

    /// <summary>
    /// How many steps (8 neighbours) <paramref name="cell"/> is from the nearest cell without ground: 1 on the edge,
    /// growing inwards, and 0 off the ground.
    /// </summary>
    public int EdgeDistance(Vector3Int cell)
    {
        return edgeDistance.TryGetValue(cell, out int distance) ? distance : 0;
    }

    /// <summary>
    /// Adds to <paramref name="target"/> every ground cell whose centre lies within half of <paramref name="width"/> of a
    /// sample, so the band is never narrower than the width.
    /// </summary>
    public void Paint(List<Vector2> samples, int width, HashSet<Vector3Int> target)
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
                    if ((cellCentre - sample).sqrMagnitude <= radius * radius && Ground.Contains(cell))
                    {
                        target.Add(cell);
                    }
                }
            }
        }
    }

    /// <summary>
    /// The sample of <paramref name="road"/> a trail of <paramref name="trailWidth"/> leaves from towards
    /// <paramref name="x"/>: the one nearest <paramref name="x"/> where the road's northern edge (its stone) runs level
    /// across the trail's columns and a cell beyond each side, so the trail meets it at no step (a step there would leave
    /// the road's edge running on under the trail's rounded corner, Docs/TerrainTypes.md, Phase 3's Corners note); the
    /// nearest sample when the road is level nowhere.
    /// </summary>
    public Vector2 TrailStart(IReadOnlyList<Vector2> road, float x, int trailWidth)
    {
        Dictionary<int, int> northEdge = new();
        foreach (Vector3Int cell in Stone)
        {
            northEdge[cell.x] = northEdge.TryGetValue(cell.x, out int y) ? Mathf.Max(y, cell.y) : cell.y;
        }

        List<Vector2> byDistance = new(road);
        byDistance.Sort((a, b) => Mathf.Abs(a.x - x).CompareTo(Mathf.Abs(b.x - x)));
        int reach = trailWidth / 2 + 2;
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
    /// Puts the party spawn on the first of <paramref name="preferred"/> whose cell has ground all around it for
    /// <paramref name="clearRadius"/> cells; falls back to the ground cell furthest from the edge, with a warning, when
    /// none has.
    /// </summary>
    public void PlaceSpawn(IReadOnlyList<Vector2> preferred, int clearRadius)
    {
        int needed = clearRadius + 1;
        foreach (Vector2 point in preferred)
        {
            Vector3Int cell = CellAt(point);
            if (EdgeDistance(cell) >= needed)
            {
                Layout.SpawnCell = cell;
                return;
            }
        }

        Vector3Int deepest = orderedGround.Count > 0 ? orderedGround[0] : Vector3Int.zero;
        foreach (Vector3Int cell in orderedGround)
        {
            if (edgeDistance[cell] > edgeDistance[deepest])
            {
                deepest = cell;
            }
        }

        Layout.SpawnCell = deepest;
        Layout.Warnings.Add($"No path cell has {clearRadius} cells of ground around it; the spawn is at the deepest ground cell.");
    }

    /// <summary>
    /// Keeps blocking props off every path cell and the <paramref name="clearance"/> cells beside it.
    /// </summary>
    public void ReservePathClearance(int clearance)
    {
        foreach (Vector3Int cell in orderedGround)
        {
            if (IsPath(cell))
            {
                ReserveSquare(cell, clearance);
            }
        }
    }

    /// <summary>
    /// Keeps blocking props off <paramref name="cell"/> and the cells within <paramref name="clearance"/> steps of it
    /// (8 neighbours).
    /// </summary>
    public void ReserveSquare(Vector3Int cell, int clearance)
    {
        for (int dy = -clearance; dy <= clearance; dy++)
        {
            for (int dx = -clearance; dx <= clearance; dx++)
            {
                Reserved.Add(cell + new Vector3Int(dx, dy, 0));
            }
        }
    }

    /// <summary>
    /// Keeps blocking props off the cells within <paramref name="radius"/> of <paramref name="centre"/>.
    /// </summary>
    public void ReserveDisc(Vector3Int centre, int radius)
    {
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy <= radius * radius)
                {
                    Reserved.Add(centre + new Vector3Int(dx, dy, 0));
                }
            }
        }
    }

    /// <summary>
    /// Tries <paramref name="candidates"/> in random order, each with a random prefab from <paramref name="prefabs"/>,
    /// until <paramref name="count"/> props are placed or the cells run out. A prefab with a footprint is placed as a
    /// blocking prop, any other as a walk-through prop, each at least <paramref name="spacing"/> from the others
    /// placed by this call. Returns how many were placed.
    /// </summary>
    public int ScatterProps(IReadOnlyList<GameObject> prefabs, int count, float spacing, List<Vector3Int> candidates)
    {
        List<GameObject> usable = new();
        foreach (GameObject prefab in prefabs)
        {
            if (prefab != null)
            {
                usable.Add(prefab);
            }
        }

        if (usable.Count == 0 || count == 0)
        {
            return 0;
        }

        Shuffle(candidates);
        List<Vector2> placed = new();
        foreach (Vector3Int cell in candidates)
        {
            if (placed.Count >= count)
            {
                break;
            }

            GameObject prefab = usable[random.Next(usable.Count)];
            if (PropGridSnapping.TryGetFootprint(prefab, out Rect footprint))
            {
                TryPlaceBlocking(prefab, footprint, cell, spacing, placed);
            }
            else
            {
                TryPlaceWalkThrough(prefab, cell, spacing, placed);
            }
        }

        return placed.Count;
    }

    /// <summary>
    /// Places the blocking <paramref name="prefab"/> with its footprint's bottom-left cell on <paramref name="corner"/>
    /// when every cell it covers is free ground. Returns whether it was placed.
    /// </summary>
    public bool TryPlaceFootprint(GameObject prefab, Vector3Int corner)
    {
        if (!PropGridSnapping.TryGetFootprint(prefab, out Rect footprint))
        {
            return false;
        }

        Vector2Int covered = PropGridSnapping.CellsCovered(footprint, Vector2.one);
        if (!IsFree(corner, covered))
        {
            return false;
        }

        Cover(prefab, corner, covered);
        return true;
    }

    /// <summary>
    /// Whether every cell of the <paramref name="covered"/> block from <paramref name="corner"/> is ground that is not
    /// reserved, covered or decorated.
    /// </summary>
    public bool IsFree(Vector3Int corner, Vector2Int covered)
    {
        for (int dy = 0; dy < covered.y; dy++)
        {
            for (int dx = 0; dx < covered.x; dx++)
            {
                Vector3Int part = corner + new Vector3Int(dx, dy, 0);
                if (!Ground.Contains(part) || Reserved.Contains(part) || Occupied.Contains(part) || Decorated.Contains(part))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// The cells 4-connected to the spawn around the blocking footprints, like the navigation grid's regions.
    /// </summary>
    public HashSet<Vector3Int> ReachableFromSpawn()
    {
        return FloodFill(Layout.SpawnCell, cell => Ground.Contains(cell) && !Occupied.Contains(cell));
    }

    /// <summary>
    /// The cells 4-connected to the spawn around the blocking footprints and the ground cells that pass
    /// <paramref name="blocked"/> (a layer that cannot be walked, such as the ocean or lava).
    /// </summary>
    public HashSet<Vector3Int> ReachableFromSpawn(Predicate<Vector3Int> blocked)
    {
        return FloodFill(Layout.SpawnCell, cell => Ground.Contains(cell) && !blocked(cell) && !Occupied.Contains(cell));
    }

    /// <summary>
    /// Each ground cell's steps (8 neighbours, over the ground) from the nearest ground cell that passes
    /// <paramref name="source"/>: 0 on such a cell; a cell no step reaches is left out.
    /// </summary>
    public Dictionary<Vector3Int, int> StepsFrom(Predicate<Vector3Int> source)
    {
        Dictionary<Vector3Int, int> distances = new();
        Queue<Vector3Int> queue = new();
        foreach (Vector3Int cell in orderedGround)
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
                    if (Ground.Contains(neighbour) && !distances.ContainsKey(neighbour))
                    {
                        distances[neighbour] = next;
                        queue.Enqueue(neighbour);
                    }
                }
            }
        }

        return distances;
    }

    /// <summary>
    /// The steps <paramref name="steps"/> (from <see cref="StepsFrom"/>) holds for <paramref name="cell"/>, and
    /// int.MaxValue for a cell it does not hold.
    /// </summary>
    public static int StepsAt(Dictionary<Vector3Int, int> steps, Vector3Int cell)
    {
        return steps.TryGetValue(cell, out int distance) ? distance : int.MaxValue;
    }

    /// <summary>
    /// The path cells (<see cref="IsPath"/>), in ground order.
    /// </summary>
    public List<Vector3Int> PathCells()
    {
        List<Vector3Int> cells = new();
        foreach (Vector3Int cell in orderedGround)
        {
            if (IsPath(cell))
            {
                cells.Add(cell);
            }
        }

        return cells;
    }

    /// <summary>
    /// The cells of <paramref name="reachable"/>, in ground order, where a mob may spawn: deeper inside the edge than
    /// <paramref name="borderDepth"/>, not next to a blocking prop, and at least <paramref name="distanceFromSpawn"/>
    /// from the spawn.
    /// </summary>
    public List<Vector3Int> MobCells(HashSet<Vector3Int> reachable, int borderDepth, float distanceFromSpawn)
    {
        List<Vector3Int> cells = new();
        foreach (Vector3Int cell in orderedGround)
        {
            if (reachable.Contains(cell) && edgeDistance[cell] > borderDepth && !TouchesProp(cell)
                && Vector3Int.Distance(cell, Layout.SpawnCell) >= distanceFromSpawn)
            {
                cells.Add(cell);
            }
        }

        return cells;
    }

    /// <summary>
    /// Adds spawns of <paramref name="prefab"/> on <paramref name="candidates"/>, tried in random order, until
    /// <paramref name="wanted"/> are placed, each at least <paramref name="spacing"/> from every spawn in
    /// <paramref name="placed"/>, which collects them. Returns how many were added.
    /// </summary>
    public int PlaceMobs(MobController prefab, int wanted, List<Vector3Int> candidates, float spacing, List<Vector3Int> placed)
    {
        Shuffle(candidates);
        int added = 0;
        foreach (Vector3Int cell in candidates)
        {
            if (added >= wanted)
            {
                break;
            }

            bool farEnough = true;
            foreach (Vector3Int other in placed)
            {
                farEnough &= Vector3Int.Distance(cell, other) >= spacing;
            }

            if (farEnough)
            {
                placed.Add(cell);
                Layout.Mobs.Add(new AreaLayout.Mob(prefab, cell));
                added++;
            }
        }

        return added;
    }

    /// <summary>
    /// Whether <paramref name="cell"/> is a gravel or stone cell, or a cell of one of the other path layers.
    /// </summary>
    public bool IsPath(Vector3Int cell)
    {
        if (Stone.Contains(cell) || Gravel.Contains(cell))
        {
            return true;
        }

        foreach (HashSet<Vector3Int> paths in otherPaths)
        {
            if (paths.Contains(cell))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Removes every walk-through prop standing on a cell that passes <paramref name="covered"/> and, unless
    /// <paramref name="walkThroughOnly"/>, every blocking prop whose footprint covers such a cell, freeing their cells.
    /// </summary>
    public void RemoveProps(Predicate<Vector3Int> covered, bool walkThroughOnly)
    {
        for (int i = Layout.Props.Count - 1; i >= 0; i--)
        {
            AreaLayout.Prop prop = Layout.Props[i];
            if (!PropGridSnapping.TryGetFootprint(prop.Prefab, out Rect footprint))
            {
                if (covered(prop.Cell))
                {
                    Decorated.Remove(prop.Cell);
                    Layout.Props.RemoveAt(i);
                }

                continue;
            }

            if (walkThroughOnly)
            {
                continue;
            }

            Vector2Int size = PropGridSnapping.CellsCovered(footprint, Vector2.one);
            bool hit = false;
            for (int dy = 0; dy < size.y; dy++)
            {
                for (int dx = 0; dx < size.x; dx++)
                {
                    hit |= covered(prop.Cell + new Vector3Int(dx, dy, 0));
                }
            }

            if (!hit)
            {
                continue;
            }

            for (int dy = 0; dy < size.y; dy++)
            {
                for (int dx = 0; dx < size.x; dx++)
                {
                    Occupied.Remove(prop.Cell + new Vector3Int(dx, dy, 0));
                }
            }

            Layout.Props.RemoveAt(i);
        }
    }

    /// <summary>
    /// Adds to <paramref name="target"/> the ground cells that pass <paramref name="allowed"/> inside a wobbly disc
    /// around <paramref name="centre"/>, of a random radius between the two values of <paramref name="radii"/> and four
    /// fifths as tall as wide.
    /// </summary>
    public void PaintPatch(Vector3Int centre, Vector2 radii, HashSet<Vector3Int> target, Predicate<Vector3Int> allowed)
    {
        float radius = Range(radii.x, Mathf.Max(radii.x, radii.y));
        Vector2 middle = new(centre.x + 0.5f, centre.y + 0.5f);
        Predicate<Vector2> contains = WobblyShape(middle, new Vector2(radius, radius * 0.8f), 2f, 0.25f);
        int reach = Mathf.CeilToInt(radius * 1.3f) + 1;
        for (int dy = -reach; dy <= reach; dy++)
        {
            for (int dx = -reach; dx <= reach; dx++)
            {
                Vector3Int cell = centre + new Vector3Int(dx, dy, 0);
                if (Ground.Contains(cell) && allowed(cell) && contains(new Vector2(cell.x + 0.5f, cell.y + 0.5f)))
                {
                    target.Add(cell);
                }
            }
        }
    }

    /// <summary>
    /// Paints up to <paramref name="count"/> patches (<see cref="PaintPatch"/>) of a radius between the two values of
    /// <paramref name="radii"/> into <paramref name="target"/>, on the ground cells that pass <paramref name="allowed"/>,
    /// each centred on a random ground cell, in ground order, that is not in <paramref name="target"/> yet and whose cells
    /// within <paramref name="centreClearance"/> steps (8 neighbours) all pass <paramref name="allowed"/>, so a patch is
    /// not cut down to a sliver at once. Stops early when no such cell is left.
    /// </summary>
    public void PaintPatches(HashSet<Vector3Int> target, int count, Vector2 radii, Predicate<Vector3Int> allowed, int centreClearance)
    {
        List<Vector3Int> centres = new();
        foreach (Vector3Int cell in orderedGround)
        {
            if (AllWithin(cell, centreClearance, allowed))
            {
                centres.Add(cell);
            }
        }

        for (int i = 0; i < count && centres.Count > 0; i++)
        {
            PaintPatch(centres[Next(centres.Count)], radii, target, allowed);
            centres.RemoveAll(target.Contains);
        }
    }

    /// <summary>
    /// Whether every cell within <paramref name="steps"/> steps (8 neighbours) of <paramref name="cell"/>, the cell
    /// included, passes <paramref name="test"/>.
    /// </summary>
    public static bool AllWithin(Vector3Int cell, int steps, Predicate<Vector3Int> test)
    {
        for (int dy = -steps; dy <= steps; dy++)
        {
            for (int dx = -steps; dx <= steps; dx++)
            {
                if (!test(cell + new Vector3Int(dx, dy, 0)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Removes from <paramref name="cells"/> every 4-connected piece of fewer than <paramref name="smallest"/> cells,
    /// the scraps a patch leaves where the cells it may not cover cut it short.
    /// </summary>
    public void RemoveFragments(HashSet<Vector3Int> cells, int smallest)
    {
        HashSet<Vector3Int> seen = new();
        foreach (Vector3Int cell in orderedGround)
        {
            if (!cells.Contains(cell) || seen.Contains(cell))
            {
                continue;
            }

            HashSet<Vector3Int> piece = FloodFill(cell, cells.Contains);
            seen.UnionWith(piece);
            if (piece.Count < smallest)
            {
                cells.ExceptWith(piece);
            }
        }
    }

    /// <summary>
    /// Every cell within <paramref name="steps"/> steps (8 neighbours) of a cell of <paramref name="cells"/>.
    /// </summary>
    public static HashSet<Vector3Int> CellsNear(IEnumerable<Vector3Int> cells, int steps)
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

    /// <summary>
    /// Whether <paramref name="cell"/> or one of its 8 neighbours is a path cell.
    /// </summary>
    public bool IsNearPath(Vector3Int cell)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (IsPath(cell + new Vector3Int(dx, dy, 0)))
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
    public bool TouchesProp(Vector3Int cell)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (Occupied.Contains(cell + new Vector3Int(dx, dy, 0)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Picks a campsite centre on interior ground that passes <paramref name="allowed"/>: deeper than
    /// <paramref name="borderDepth"/> plus the campsite's radius, with no path within its radius plus
    /// <paramref name="pathClearance"/>, and its radius clear of the spawn's disc of <paramref name="spawnClearRadius"/>
    /// and of every mob; among those, one of the cells with the fewest blocking-prop cells within its radius less half a
    /// cell, where the fire and the seats go. The props in that circle and the plants on or beside the campsite's wobbly
    /// patch of dirt, added to <paramref name="dirt"/> (a plant's sprite stands above its cell, so one just below the
    /// patch would grow over it), are removed; a tree at the patch's rim stays and stands on the dirt, since the ground
    /// goes on under it. The campfire stands in the middle and the seats on the dirt around it. Props are only removed,
    /// so nothing else moves and the spawn can still reach everything it could. Draws from the current stream (a
    /// generator calls <see cref="StartDetailStream"/> first). Warns and returns false when no cell qualifies or the
    /// campfire does not fit.
    /// </summary>
    public bool LayCampsite(Campsite campsite, HashSet<Vector3Int> dirt, int borderDepth, int pathClearance, int spawnClearRadius, Predicate<Vector3Int> allowed)
    {
        float radius = campsite.Radius;
        int reach = Mathf.CeilToInt(radius * 1.3f) + 1;
        float site = radius - 0.5f;
        List<Vector3Int> candidates = new();
        int fewest = int.MaxValue;
        foreach (Vector3Int cell in orderedGround)
        {
            if (EdgeDistance(cell) <= borderDepth + radius || IsWithin(cell, radius + pathClearance, IsPath)
                || Vector3Int.Distance(cell, Layout.SpawnCell) <= spawnClearRadius + radius || !IsAwayFromMobs(cell, radius + 1f) || !allowed(cell))
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
            Layout.Warnings.Add("No open ground has room for the campsite.");
            return false;
        }

        Vector3Int centre = candidates[Next(candidates.Count)];
        Vector2 middle = new(centre.x + 0.5f, centre.y + 0.5f);
        RemoveProps(cell => Vector2.Distance(new Vector2(cell.x + 0.5f, cell.y + 0.5f), middle) <= site, false);
        Predicate<Vector2> contains = WobblyShape(middle, new Vector2(radius, radius * 0.8f), 2f, 0.2f);
        List<Vector3Int> ring = new();
        for (int dy = -reach; dy <= reach; dy++)
        {
            for (int dx = -reach; dx <= reach; dx++)
            {
                Vector3Int cell = centre + new Vector3Int(dx, dy, 0);
                Vector2 cellCentre = new(cell.x + 0.5f, cell.y + 0.5f);
                if (Ground.Contains(cell) && !IsPath(cell) && contains(cellCentre))
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

        RemoveProps(cell => IsWithin(cell, 1f, dirt.Contains), true);
        if (!PropGridSnapping.TryGetFootprint(campsite.Campfire, out Rect footprint))
        {
            Layout.Warnings.Add("The campfire prefab has no footprint; no campfire was placed.");
            return false;
        }

        Vector2Int fire = PropGridSnapping.CellsCovered(footprint, Vector2.one);
        if (!TryPlaceFootprint(campsite.Campfire, new Vector3Int(centre.x - (fire.x - 1) / 2, centre.y, 0)))
        {
            Layout.Warnings.Add("The campfire did not fit on its campsite.");
            return false;
        }

        ScatterProps(campsite.Seats, campsite.SeatCount, campsite.SeatSpacing, ring);
        return true;
    }

    /// <summary>
    /// A uniformly distributed value between <paramref name="min"/> and <paramref name="max"/>.
    /// </summary>
    public float Range(float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }

    /// <summary>
    /// A uniformly distributed integer from 0 up to, not including, <paramref name="max"/>.
    /// </summary>
    public int Next(int max)
    {
        return random.Next(max);
    }

    /// <summary>
    /// Shuffles <paramref name="items"/> in place (Fisher-Yates).
    /// </summary>
    public void Shuffle<T>(List<T> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>
    /// Every cell 4-connected to <paramref name="start"/> through cells that pass <paramref name="isOpen"/>,
    /// <paramref name="start"/> included when it passes.
    /// </summary>
    public static HashSet<Vector3Int> FloodFill(Vector3Int start, Predicate<Vector3Int> isOpen)
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
    /// Samples a Catmull-Rom spline through <paramref name="points"/> about every <see cref="SampleStep"/> units into
    /// <paramref name="samples"/>.
    /// </summary>
    public static void SampleSpline(IReadOnlyList<Vector2> points, List<Vector2> samples)
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
    /// The cell that contains <paramref name="point"/>.
    /// </summary>
    public static Vector3Int CellAt(Vector2 point)
    {
        return new Vector3Int(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y), 0);
    }

    /// <summary>
    /// Whether <paramref name="point"/> is at least <paramref name="spacing"/> from every point in
    /// <paramref name="placed"/>.
    /// </summary>
    public static bool IsFarFrom(List<Vector2> placed, Vector2 point, float spacing)
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
    /// Whether a cell within <paramref name="steps"/> steps (8 neighbours) of <paramref name="cell"/> passes
    /// <paramref name="test"/>.
    /// </summary>
    private static bool IsWithin(Vector3Int cell, float steps, Predicate<Vector3Int> test)
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
                if (dx * dx + dy * dy <= distance * distance && Occupied.Contains(cell + new Vector3Int(dx, dy, 0)))
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
        foreach (AreaLayout.Mob mob in Layout.Mobs)
        {
            if (Vector3Int.Distance(cell, mob.Cell) <= distance)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Adds the largest 4-connected region of <paramref name="cells"/> to the ground, in the order the cells were
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
                Ground.Add(cell);
                orderedGround.Add(cell);
            }
        }
    }

    /// <summary>
    /// Records for every ground cell how many steps (8 neighbours) it is from the nearest cell without ground.
    /// </summary>
    private void MeasureEdgeDistance()
    {
        Queue<Vector3Int> queue = new();
        foreach (Vector3Int cell in orderedGround)
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
                    if (Ground.Contains(neighbour) && !edgeDistance.ContainsKey(neighbour))
                    {
                        edgeDistance[neighbour] = next;
                        queue.Enqueue(neighbour);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Whether one of the 8 neighbours of <paramref name="cell"/> has no ground.
    /// </summary>
    private bool TouchesOutside(Vector3Int cell)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!Ground.Contains(cell + new Vector3Int(dx, dy, 0)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Places a blocking prop whose footprint stands on <paramref name="cell"/>, centred on it as far as its width
    /// allows, when every cell it covers is free ground and it is far enough from the props in
    /// <paramref name="placed"/>.
    /// </summary>
    private void TryPlaceBlocking(GameObject prefab, Rect footprint, Vector3Int cell, float spacing, List<Vector2> placed)
    {
        Vector2Int covered = PropGridSnapping.CellsCovered(footprint, Vector2.one);
        Vector3Int corner = new(cell.x - (covered.x - 1) / 2, cell.y, 0);
        Vector2 centre = new(corner.x + covered.x * 0.5f, corner.y + covered.y * 0.5f);
        if (!IsFarFrom(placed, centre, spacing) || !IsFree(corner, covered))
        {
            return;
        }

        Cover(prefab, corner, covered);
        placed.Add(centre);
    }

    /// <summary>
    /// Places a walk-through prop at a random point in <paramref name="cell"/> when the cell and its neighbours are
    /// off the paths, nothing else stands in it, and it is far enough from the props in <paramref name="placed"/>.
    /// </summary>
    private void TryPlaceWalkThrough(GameObject prefab, Vector3Int cell, float spacing, List<Vector2> placed)
    {
        Vector2 offset = new(Range(0.2f, 0.8f), Range(0.15f, 0.6f));
        Vector2 point = new(cell.x + offset.x, cell.y + offset.y);
        if (IsNearPath(cell) || Occupied.Contains(cell) || Decorated.Contains(cell) || !IsFarFrom(placed, point, spacing))
        {
            return;
        }

        Decorated.Add(cell);
        placed.Add(point);
        Layout.Props.Add(new AreaLayout.Prop(prefab, cell, offset));
    }

    /// <summary>
    /// Marks the <paramref name="covered"/> block from <paramref name="corner"/> as covered and adds the prop.
    /// </summary>
    private void Cover(GameObject prefab, Vector3Int corner, Vector2Int covered)
    {
        for (int dy = 0; dy < covered.y; dy++)
        {
            for (int dx = 0; dx < covered.x; dx++)
            {
                Occupied.Add(corner + new Vector3Int(dx, dy, 0));
            }
        }

        Layout.Props.Add(new AreaLayout.Prop(prefab, corner, Vector2.zero));
    }
}
