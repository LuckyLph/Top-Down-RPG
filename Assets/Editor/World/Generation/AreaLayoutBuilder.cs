using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

/// <summary>
/// The shared state and steps of one generation, used by the generators' runs: the seeded random source, the layout
/// being filled with its grass, gravel and stone cells (its other layers are the generators' own to fill, through
/// <see cref="AreaLayout.TerrainCells(AreaGenerator.TerrainLayer)"/>), how deep each grass cell lies inside the edge,
/// and which cells are kept free of blocking props, covered by one, or hold a walk-through prop. Layers are found by the
/// generator's own layer objects, never by a fixed index, since each generator lists only the layers it uses.
/// Iteration always goes through ordered lists, never hash sets, so a seed gives the same layout every time.
/// </summary>
internal sealed class AreaLayoutBuilder
{
    private const float SampleStep = 0.2f;
    private const float PathEdgeAllowance = 0.05f;
    private const int OutlineHarmonics = 4;

    private static readonly Vector3Int[] Sides = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down };

    private readonly Random random;
    private readonly List<Vector3Int> orderedGrass = new();
    private readonly Dictionary<Vector3Int, int> edgeDistance = new();

    /// <summary>
    /// Prepares an empty layout of <paramref name="layers"/>, a generator's terrain layers, whose
    /// <paramref name="grass"/>, <paramref name="gravel"/> and <paramref name="stone"/> layers the shared steps lay, and
    /// a random source seeded with <paramref name="seed"/>.
    /// </summary>
    public AreaLayoutBuilder(int seed, IReadOnlyList<AreaGenerator.TerrainLayer> layers, AreaGenerator.TerrainLayer grass, AreaGenerator.TerrainLayer gravel, AreaGenerator.TerrainLayer stone)
    {
        random = new Random(seed);
        Layout = new AreaLayout(layers);
        Grass = Layout.TerrainCells(grass);
        Gravel = Layout.TerrainCells(gravel);
        Stone = Layout.TerrainCells(stone);
    }

    public AreaLayout Layout { get; }
    public HashSet<Vector3Int> Grass { get; }
    public HashSet<Vector3Int> Gravel { get; }
    public HashSet<Vector3Int> Stone { get; }

    /// <summary>
    /// The grass cells in the order they were laid, rows from the bottom and cells from the left.
    /// </summary>
    public IReadOnlyList<Vector3Int> OrderedGrass => orderedGrass;

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
    /// Lays the grass inside a superellipse of <paramref name="halfSize"/> around the origin whose radius wobbles by up
    /// to <paramref name="edgeWobble"/> (<see cref="WobblyShape"/>), keeps only its largest 4-connected region, and
    /// records how deep every grass cell lies inside the edge.
    /// </summary>
    public void LayGrass(Vector2 halfSize, float squareness, float edgeWobble)
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
    /// How many steps (8 neighbours) <paramref name="cell"/> is from the nearest cell without grass: 1 on the edge,
    /// growing inwards, and 0 off the grass.
    /// </summary>
    public int EdgeDistance(Vector3Int cell)
    {
        return edgeDistance.TryGetValue(cell, out int distance) ? distance : 0;
    }

    /// <summary>
    /// Adds to <paramref name="target"/> every grass cell whose centre lies within half of <paramref name="width"/> of a
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
                    if ((cellCentre - sample).sqrMagnitude <= radius * radius && Grass.Contains(cell))
                    {
                        target.Add(cell);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Puts the party spawn on the first of <paramref name="preferred"/> whose cell has grass all around it for
    /// <paramref name="clearRadius"/> cells; falls back to the grass cell furthest from the edge, with a warning, when
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

        Vector3Int deepest = orderedGrass.Count > 0 ? orderedGrass[0] : Vector3Int.zero;
        foreach (Vector3Int cell in orderedGrass)
        {
            if (edgeDistance[cell] > edgeDistance[deepest])
            {
                deepest = cell;
            }
        }

        Layout.SpawnCell = deepest;
        Layout.Warnings.Add($"No path cell has {clearRadius} cells of grass around it; the spawn is at the deepest grass cell.");
    }

    /// <summary>
    /// Keeps blocking props off every path cell and the <paramref name="clearance"/> cells beside it.
    /// </summary>
    public void ReservePathClearance(int clearance)
    {
        foreach (Vector3Int cell in orderedGrass)
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
    /// when every cell it covers is free grass. Returns whether it was placed.
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
    /// Whether every cell of the <paramref name="covered"/> block from <paramref name="corner"/> is grass that is not
    /// reserved, covered or decorated.
    /// </summary>
    public bool IsFree(Vector3Int corner, Vector2Int covered)
    {
        for (int dy = 0; dy < covered.y; dy++)
        {
            for (int dx = 0; dx < covered.x; dx++)
            {
                Vector3Int part = corner + new Vector3Int(dx, dy, 0);
                if (!Grass.Contains(part) || Reserved.Contains(part) || Occupied.Contains(part) || Decorated.Contains(part))
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
        return FloodFill(Layout.SpawnCell, cell => Grass.Contains(cell) && !Occupied.Contains(cell));
    }

    /// <summary>
    /// The cells of <paramref name="reachable"/>, in grass order, where a mob may spawn: deeper inside the edge than
    /// <paramref name="borderDepth"/>, not next to a blocking prop, and at least <paramref name="distanceFromSpawn"/>
    /// from the spawn.
    /// </summary>
    public List<Vector3Int> MobCells(HashSet<Vector3Int> reachable, int borderDepth, float distanceFromSpawn)
    {
        List<Vector3Int> cells = new();
        foreach (Vector3Int cell in orderedGrass)
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
    /// Whether <paramref name="cell"/> is a gravel or stone cell.
    /// </summary>
    public bool IsPath(Vector3Int cell)
    {
        return Stone.Contains(cell) || Gravel.Contains(cell);
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
                Grass.Add(cell);
                orderedGrass.Add(cell);
            }
        }
    }

    /// <summary>
    /// Records for every grass cell how many steps (8 neighbours) it is from the nearest cell without grass.
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
                    if (Grass.Contains(neighbour) && !edgeDistance.ContainsKey(neighbour))
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
                if (!Grass.Contains(cell + new Vector3Int(dx, dy, 0)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Places a blocking prop whose footprint stands on <paramref name="cell"/>, centred on it as far as its width
    /// allows, when every cell it covers is free grass and it is far enough from the props in
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
