using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Water, for an animated material: a "breathing caustic net" (Docs/TerrainBlending.md, Phase 5 notes). A light net of
/// one-pixel lines over a mid-blue fill, the edges of Voronoi cells around jittered seeds on a stretched lattice, the
/// pixels warped by smooth waves and some edges dropped so neighbouring cells merge into larger blobs; the lines
/// thicken where they meet and glow either side, so they read as light on the water rather than as cracks; deep cells
/// where a smooth field is lowest, which drop more of their shared edges, so deep water shows an open net, and may have
/// a fill of their own, their edges with shallow cells never dropped so a change of tone falls under a line; a few
/// brighter knots where lines meet. Every seed has two positions a little apart, so each edge has two nearby courses: a pixel on both
/// is drawn as net, and a pixel on only one carries a glint phase, the edge's own offset, half a period apart between
/// the two courses, so with a glint duty of 0.5 each edge holds one course, crossfades and holds the other, out of step
/// with the rest. Some knots sparkle: a short horizontal dash whose sparkle phases step along it, so the sparkle appears,
/// slides a pixel or two and fades. The Voronoi search wraps around the texture, so the net tiles seamlessly. No cell
/// field; the base colour is the fill, the glint colour the net's and the sparkle colour the sparkles', and the
/// height's per-chip value is 0.5 everywhere.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Terrain Recipe/Water Caustics", fileName = "Recipe_Water")]
public sealed class WaterCausticsRecipe : TerrainMaterialRecipe
{
    private const int DropSalt = 23;
    private const int EdgePhaseSalt = 29;
    private const int KnotSalt = 31;
    private const int SparkleSalt = 37;
    private const int SparklePhaseSalt = 41;
    private const int SearchRadius = 2;

    /// <summary>
    /// The seeds' base places (before breathing) and whether each one's cell is deep, in a texture of
    /// <see cref="Size"/> pixels, for deciding which edges are drawn.
    /// </summary>
    private sealed class Lattice
    {
        public double[] X;
        public double[] Y;
        public bool[] Deep;
        public int Size;
        public double Flatten;
    }

    /// <summary>
    /// The Voronoi cell a pixel lies in for one course of the net: its nearest seed, the seeds across its nearest and
    /// second-nearest cell edges, and its distances in pixels to those edges.
    /// </summary>
    private struct Hit
    {
        public int Seed;
        public int Across;
        public int SecondAcross;
        public double Edge;
        public double SecondEdge;
    }

    [Header("Water")]
    [Tooltip("The water's fill, and the material's base colour.")]
    [SerializeField] private Color32 water = new(40, 104, 160, 255);
    [Tooltip("The fill of deep cells; the water colour shows depth by the sparser net alone.")]
    [SerializeField] private Color32 deep = new(40, 104, 160, 255);
    [Tooltip("Waves of the depth field, read at each cell's seed. Avoid single periods across the texture, which make one big patch per repeat, and waves along one axis only, which repeat within it. Set for a texture of 8 cells.")]
    [SerializeField] private Wave[] depthWaves = { new(2, 1, 1f), new(1, -2, 0.9f), new(3, 2, 0.6f), new(-2, 3, 0.5f) };
    [Tooltip("Share of the cells that are deep: those where the depth field is lowest.")]
    [SerializeField, Range(0f, 1f)] private float deepShare = 0.35f;
    [Header("Net")]
    [Tooltip("Cells of the net along x per world cell.")]
    [SerializeField, Range(0.5f, 6f)] private float cellsAlongXPerCell = 1.75f;
    [Tooltip("Cells of the net along y per world cell; more than along x stretches the cells sideways.")]
    [SerializeField, Range(0.5f, 6f)] private float cellsAlongYPerCell = 2.5f;
    [Tooltip("How much flatter the cell edges run: the cells are found with up-and-down distances counted this many times over, so every edge leans towards level and the net runs sideways like ripples, without changing the cells' size; 1 for none.")]
    [SerializeField, Range(1f, 4f)] private float flatten = 1f;
    [Tooltip("How much of its lattice cell a seed may lie anywhere in; low values give a honeycomb.")]
    [SerializeField, Range(0f, 1f)] private float jitter = 0.85f;
    [Tooltip("Distance in pixels between a seed's two positions, so between an edge's two courses.")]
    [SerializeField, Range(0f, 3f)] private float breathe = 1f;
    [Tooltip("Waves that move the pixels sideways before the cells are found, their amplitude in pixels; about one period per two cells bends the edges. Set for a texture of 8 cells.")]
    [SerializeField] private Wave[] warpWaves = { new(8, 3, 2.5f), new(-5, 9, 2f), new(11, -6, 1f) };
    [Tooltip("Waves that move the pixels up and down before the cells are found, their amplitude in pixels. Waves that repeat often along X and rarely along Y make level lines undulate like ripples. Set for a texture of 8 cells.")]
    [SerializeField] private Wave[] verticalWarpWaves = { new(8, 3, 2.5f), new(-5, 9, 2f), new(11, -6, 1f) };
    [Tooltip("A pixel is on a line when it lies this many pixels or less from a cell edge, on average.")]
    [SerializeField, Range(0.3f, 1.5f)] private float lineWidth = 0.55f;
    [Tooltip("Waves that vary the line width along the net, normalised into -1 to 1 by their total amplitude, so lines swell and thin out. Set for a texture of 8 cells.")]
    [SerializeField] private Wave[] widthWaves = { new(4, 7, 1f), new(9, -3, 0.7f), new(-6, 5, 0.5f) };
    [Tooltip("How much the width waves change the line width either way, as a fraction of it.")]
    [SerializeField, Range(0f, 0.9f)] private float widthVariation = 0.5f;
    [Tooltip("Chance that a cell edge is left out, merging its two cells.")]
    [SerializeField, Range(0f, 0.9f)] private float dropChance = 0.25f;
    [Tooltip("Chance that an edge between two deep cells is left out, so deep water shows a sparser net.")]
    [SerializeField, Range(0f, 0.9f)] private float deepDropChance = 0.7f;
    [Tooltip("Chance that an upright edge (between cells side by side) is left out; slanted edges are left out by less, level ones by the drop chances above. Raise it to make the net run sideways, in level ripples; 0 treats every edge alike.")]
    [SerializeField, Range(0f, 1f)] private float uprightDropChance;
    [Tooltip("A pixel within this many pixels of two drawn cell edges is on the net too, so the lines thicken into small blobs where they meet and the cells' corners round off; 0 keeps thin junctions.")]
    [SerializeField, Range(0f, 4f)] private float junctionReach = 1.6f;
    [Tooltip("Pieces of line smaller than this many pixels are removed.")]
    [SerializeField, Range(1, 16)] private int minPiece = 5;
    [Tooltip("The net's lines, and the glint colour their breathing lights up with.")]
    [SerializeField] private Color32 net = new(76, 148, 200, 255);
    [Tooltip("Width in pixels of a glow either side of the net's cell edges, wider where edges meet, so the lines read as light on the water rather than as cracks; 0 for none.")]
    [SerializeField, Range(0f, 4f)] private float haloWidth = 2f;
    [Tooltip("The glow's colour, between the fill and the net's.")]
    [SerializeField] private Color32 halo = new(52, 120, 176, 255);
    [Header("Knots")]
    [Tooltip("A line pixel lies at a knot where its second-nearest cell edge is this many pixels away or less.")]
    [SerializeField, Range(0.3f, 3f)] private float knotReach = 1f;
    [Tooltip("Share of the knots drawn brighter.")]
    [SerializeField, Range(0f, 1f)] private float knotShare = 0.2f;
    [SerializeField] private Color32 knot = new(150, 214, 236, 255);
    [Header("Sparkles")]
    [Tooltip("Share of the bright knots that sparkle.")]
    [SerializeField, Range(0f, 1f)] private float sparkleShare = 0.4f;
    [Tooltip("Length in pixels of a sparkle's dash, from its knot's leftmost pixel to the right.")]
    [SerializeField, Range(1, 6)] private int sparkleLength = 3;
    [Tooltip("How much of the sparkle period each pixel of the dash lights up after the one before it.")]
    [SerializeField, Range(0f, 0.25f)] private float sparkleStep = 0.03125f;
    [Tooltip("The sparkle colour.")]
    [SerializeField] private Color32 sparkle = new(226, 246, 242, 255);

    public override TerrainMaterial.CellMode CellMode => TerrainMaterial.CellMode.None;

    /// <summary>
    /// Draws the seeds (place and breathing direction), the warp, depth and height waves, finds both courses of the net,
    /// cleans them up, and paints the fill, the net and the knots with the breathing and sparkle phases.
    /// </summary>
    protected override TerrainTextures Generate(System.Random random, int size)
    {
        int across = SeedsAcross(cellsAlongXPerCell);
        int down = SeedsAcross(cellsAlongYPerCell);
        int seeds = across * down;
        double pitchX = (double)size / across;
        double pitchY = (double)size / down;
        double[] seedX = new double[seeds];
        double[] seedY = new double[seeds];
        double[] shiftX = new double[seeds];
        double[] shiftY = new double[seeds];
        for (int j = 0; j < down; j++)
        {
            for (int i = 0; i < across; i++)
            {
                int k = j * across + i;
                seedX[k] = (i + 0.5 + (random.NextDouble() - 0.5) * jitter) * pitchX;
                seedY[k] = (j + 0.5 + (random.NextDouble() - 0.5) * jitter) * pitchY;
                double angle = random.NextDouble() * 2.0 * Math.PI;
                shiftX[k] = 0.5 * breathe * Math.Cos(angle);
                shiftY[k] = 0.5 * breathe * Math.Sin(angle);
            }
        }

        double[] warpX = TerrainArt.PeriodicNoise(random, warpWaves, size);
        double[] warpY = TerrainArt.PeriodicNoise(random, verticalWarpWaves, size);
        double[] depth = TerrainArt.PeriodicNoise(random, depthWaves, size);
        double[] widthNoise = TerrainArt.PeriodicNoise(random, widthWaves, size);
        double widthTotal = TerrainArt.TotalAmplitude(widthWaves);
        double[] height = SmoothHeight(random, size);

        bool[] deepCell = DeepCells(depth, seedX, seedY, size);
        Lattice lattice = new() { X = seedX, Y = seedY, Deep = deepCell, Size = size, Flatten = flatten };
        Hit[] first = new Hit[size * size];
        Hit[] second = new Hit[size * size];
        bool[] firstLine = new bool[size * size];
        bool[] secondLine = new bool[size * size];
        bool[] glow = new bool[size * size];
        for (int course = 0; course < 2; course++)
        {
            Hit[] hits = course == 0 ? first : second;
            bool[] line = course == 0 ? firstLine : secondLine;
            double sign = course == 0 ? -1.0 : 1.0;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int i = y * size + x;
                    Hit hit = FindCell(x + 0.5 + warpX[i], y + 0.5 + warpY[i], sign, seedX, seedY, shiftX, shiftY, across, down, pitchX, pitchY, size, flatten);
                    hits[i] = hit;
                    double swell = 1.0 + (widthTotal > 0.0 ? widthVariation * widthNoise[i] / widthTotal : 0.0);
                    line[i] = OnLine(hit, lattice, lineWidth * swell) || AtJunction(hit, lattice, junctionReach * swell);
                    glow[i] |= haloWidth > 0f && OnLine(hit, lattice, (lineWidth + haloWidth) * swell);
                }
            }

            TidyCorners(line, size);
            RemoveSmallPieces(line, size);
        }

        KeepGlowNearLines(glow, firstLine, secondLine, size);
        Color32[] albedo = new Color32[size * size];
        byte[] glints = new byte[size * size];
        List<long> knots = new();
        Dictionary<long, int> knotStart = new();
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                Hit hit = first[i];
                Color32 fill = glow[i] ? halo : deepCell[hit.Seed] ? deep : water;
                if (firstLine[i] && secondLine[i])
                {
                    albedo[i] = net;
                    long key = hit.SecondEdge <= knotReach ? KnotKey(hit, seeds) : -1;
                    if (key >= 0 && Chance(key, seeds, KnotSalt) < knotShare)
                    {
                        albedo[i] = knot;
                        if (!knotStart.TryGetValue(key, out int start))
                        {
                            knots.Add(key);
                            knotStart[key] = i;
                        }
                        else if (x < start % size)
                        {
                            knotStart[key] = i;
                        }
                    }
                }
                else if (firstLine[i] || secondLine[i])
                {
                    albedo[i] = fill;
                    Hit course = firstLine[i] ? first[i] : second[i];
                    int other = LineEdgeAcross(course, lattice);
                    double offset = TerrainArt.Hash01(Math.Min(course.Seed, other), Math.Max(course.Seed, other), EdgePhaseSalt);
                    glints[i] = TerrainArt.GlintByte(offset + (secondLine[i] ? 0.5 : 0.0));
                }
                else
                {
                    albedo[i] = fill;
                }
            }
        }

        foreach (long key in knots)
        {
            if (Chance(key, seeds, SparkleSalt) >= sparkleShare)
            {
                continue;
            }

            int start = knotStart[key];
            double phase = Chance(key, seeds, SparklePhaseSalt);
            for (int k = 0; k < sparkleLength; k++)
            {
                int pixel = start / size * size + TerrainArt.Wrap(start % size + k, size);
                glints[pixel] = TerrainArt.SparkleByte(phase + k * sparkleStep);
            }
        }

        return new TerrainTextures(size, water, CellMode, albedo, null, TerrainArt.HeightPixels(height, null, null, glints), net, sparkle);
    }

    /// <summary>
    /// Keeps the glow only within <see cref="haloWidth"/> and a pixel and a half of a line of either course as drawn,
    /// wrapping around the texture, so no glow is left where the clean-up removed its line.
    /// </summary>
    private void KeepGlowNearLines(bool[] glow, bool[] firstLine, bool[] secondLine, int size)
    {
        bool[] near = new bool[size * size];
        double radius = haloWidth + 1.5;
        int reach = (int)Math.Floor(radius);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                if (!firstLine[i] && !secondLine[i])
                {
                    continue;
                }

                for (int dy = -reach; dy <= reach; dy++)
                {
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        if (dx * dx + dy * dy <= radius * radius)
                        {
                            near[TerrainArt.Wrap(y + dy, size) * size + TerrainArt.Wrap(x + dx, size)] = true;
                        }
                    }
                }
            }
        }

        for (int i = 0; i < glow.Length; i++)
        {
            glow[i] &= near[i];
        }
    }

    /// <summary>
    /// Whether each seed's cell is deep: the <see cref="deepShare"/> of the seeds where the depth field, read at the
    /// seed's pixel, is lowest.
    /// </summary>
    private bool[] DeepCells(double[] depth, double[] seedX, double[] seedY, int size)
    {
        double[] values = new double[seedX.Length];
        for (int k = 0; k < values.Length; k++)
        {
            values[k] = depth[TerrainArt.Wrap((int)Math.Floor(seedY[k]), size) * size + TerrainArt.Wrap((int)Math.Floor(seedX[k]), size)];
        }

        double[] sorted = (double[])values.Clone();
        Array.Sort(sorted);
        int count = TerrainArt.Round(deepShare * values.Length);
        bool[] deepCells = new bool[values.Length];
        for (int k = 0; k < values.Length && count > 0; k++)
        {
            deepCells[k] = values[k] <= sorted[count - 1];
        }

        return deepCells;
    }

    /// <summary>
    /// The Voronoi cell of the point (<paramref name="qx"/>, <paramref name="qy"/>) for the course where every seed is
    /// moved by <paramref name="sign"/> times its shift: the nearest seed among the lattice cells within
    /// <see cref="SearchRadius"/>, wrapping around the texture, and the exact distances to the two nearest edges of its
    /// cell, each the distance to the bisector between it and another seed. Up-and-down distances count
    /// <paramref name="flatten"/> times over, so the bisectors lean towards level; edge distances are still in pixels.
    /// </summary>
    private static Hit FindCell(double qx, double qy, double sign, double[] seedX, double[] seedY, double[] shiftX, double[] shiftY, int across, int down, double pitchX, double pitchY, int size, double flatten)
    {
        int column = (int)Math.Floor(qx / pitchX);
        int row = (int)Math.Floor(qy / pitchY);
        int nearest = -1;
        double nearestX = 0.0;
        double nearestY = 0.0;
        double nearestDistance = double.MaxValue;
        for (int dj = -SearchRadius; dj <= SearchRadius; dj++)
        {
            for (int di = -SearchRadius; di <= SearchRadius; di++)
            {
                SeedAt(column + di, row + dj, sign, seedX, seedY, shiftX, shiftY, across, down, size, out int k, out double sx, out double sy);
                double distance = (qx - sx) * (qx - sx) + flatten * flatten * (qy - sy) * (qy - sy);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = k;
                    nearestX = sx;
                    nearestY = sy;
                }
            }
        }

        Hit hit = new() { Seed = nearest, Across = -1, SecondAcross = -1, Edge = double.MaxValue, SecondEdge = double.MaxValue };
        for (int dj = -SearchRadius; dj <= SearchRadius; dj++)
        {
            for (int di = -SearchRadius; di <= SearchRadius; di++)
            {
                SeedAt(column + di, row + dj, sign, seedX, seedY, shiftX, shiftY, across, down, size, out int k, out double sx, out double sy);
                double nx = sx - nearestX;
                double ny = flatten * (sy - nearestY);
                double scale = Math.Sqrt(nx * nx + flatten * flatten * ny * ny);
                if (scale < 1e-9)
                {
                    continue;
                }

                double edge = Math.Abs((qx - 0.5 * (sx + nearestX)) * nx + flatten * (qy - 0.5 * (sy + nearestY)) * ny) / scale;
                if (edge < hit.Edge)
                {
                    hit.SecondEdge = hit.Edge;
                    hit.SecondAcross = hit.Across;
                    hit.Edge = edge;
                    hit.Across = k;
                }
                else if (edge < hit.SecondEdge)
                {
                    hit.SecondEdge = edge;
                    hit.SecondAcross = k;
                }
            }
        }

        return hit;
    }

    /// <summary>
    /// The seed of lattice cell (<paramref name="i"/>, <paramref name="j"/>), which may lie outside the texture: its
    /// index in the wrapped lattice, and its position moved by the whole textures it lies away and by
    /// <paramref name="sign"/> times its shift.
    /// </summary>
    private static void SeedAt(int i, int j, double sign, double[] seedX, double[] seedY, double[] shiftX, double[] shiftY, int across, int down, int size, out int k, out double x, out double y)
    {
        int wi = TerrainArt.Wrap(i, across);
        int wj = TerrainArt.Wrap(j, down);
        k = wj * across + wi;
        x = seedX[k] + sign * shiftX[k] + (double)(i - wi) / across * size;
        y = seedY[k] + sign * shiftY[k] + (double)(j - wj) / down * size;
    }

    /// <summary>
    /// Whether a pixel lies within <paramref name="width"/> pixels of one of its cell's two nearest edges that is not left
    /// out.
    /// </summary>
    private bool OnLine(Hit hit, Lattice lattice, double width)
    {
        return (hit.Edge < width && Kept(hit.Seed, hit.Across, lattice)) || (hit.SecondEdge < width && Kept(hit.Seed, hit.SecondAcross, lattice));
    }

    /// <summary>
    /// Whether a pixel lies within <paramref name="reach"/> pixels of both its cell's nearest edges, both drawn.
    /// </summary>
    private bool AtJunction(Hit hit, Lattice lattice, double reach)
    {
        return hit.SecondEdge < reach && Kept(hit.Seed, hit.Across, lattice) && Kept(hit.Seed, hit.SecondAcross, lattice);
    }

    /// <summary>
    /// The seed across the edge a line pixel lies on: its nearest edge when that one is kept, else its second.
    /// </summary>
    private int LineEdgeAcross(Hit hit, Lattice lattice)
    {
        return Kept(hit.Seed, hit.Across, lattice) ? hit.Across : hit.SecondAcross;
    }

    /// <summary>
    /// Whether the edge between the cells of seeds <paramref name="a"/> and <paramref name="b"/> is drawn: always where
    /// a deep cell meets a shallow one, so the change of tone lies under a line, else unless it is left out. Its chance
    /// to be left out is the drop chance, the deep one between two deep cells, raised towards the upright one by the
    /// square of how upright the edge stands, from the two seeds' base places (across the texture's edges by the
    /// shortest way) and the flattening.
    /// </summary>
    private bool Kept(int a, int b, Lattice lattice)
    {
        if (b < 0)
        {
            return false;
        }

        if (lattice.Deep[a] != lattice.Deep[b])
        {
            return true;
        }

        double dx = TerrainArt.WrapDelta(lattice.X[b] - lattice.X[a], lattice.Size);
        double dy = TerrainArt.WrapDelta(lattice.Y[b] - lattice.Y[a], lattice.Size);
        double alongX = lattice.Flatten * dy;
        double alongY = dx / lattice.Flatten;
        double length = Math.Sqrt(alongX * alongX + alongY * alongY);
        double upright = length > 0.0 ? alongY / length : 0.0;
        double level = lattice.Deep[a] ? deepDropChance : dropChance;
        double chance = level + (Math.Max(level, uprightDropChance) - level) * upright * upright;
        return TerrainArt.Hash01(Math.Min(a, b), Math.Max(a, b), DropSalt) >= chance;
    }

    /// <summary>
    /// Removes the corner pixel of every L turn of a line, in scan order and wrapping around the texture, so lines run
    /// one pixel wide and step diagonally: a line pixel whose only line neighbours along the axes are one beside it and
    /// one above or below it.
    /// </summary>
    private static void TidyCorners(bool[] line, int size)
    {
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (!line[y * size + x])
                {
                    continue;
                }

                bool left = line[y * size + TerrainArt.Wrap(x - 1, size)];
                bool right = line[y * size + TerrainArt.Wrap(x + 1, size)];
                bool up = line[TerrainArt.Wrap(y + 1, size) * size + x];
                bool down = line[TerrainArt.Wrap(y - 1, size) * size + x];
                int count = (left ? 1 : 0) + (right ? 1 : 0) + (up ? 1 : 0) + (down ? 1 : 0);
                if (count == 2 && (left || right) && (up || down))
                {
                    line[y * size + x] = false;
                }
            }
        }
    }

    /// <summary>
    /// Removes the pieces of line, 8-connected and wrapping around the texture, smaller than <see cref="minPiece"/>
    /// pixels.
    /// </summary>
    private void RemoveSmallPieces(bool[] line, int size)
    {
        bool[] seen = new bool[line.Length];
        List<int> piece = new();
        Stack<int> stack = new();
        for (int start = 0; start < line.Length; start++)
        {
            if (!line[start] || seen[start])
            {
                continue;
            }

            piece.Clear();
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int index = stack.Pop();
                piece.Add(index);
                int x = index % size;
                int y = index / size;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int next = TerrainArt.Wrap(y + dy, size) * size + TerrainArt.Wrap(x + dx, size);
                        if (line[next] && !seen[next])
                        {
                            seen[next] = true;
                            stack.Push(next);
                        }
                    }
                }
            }

            if (piece.Count < minPiece)
            {
                foreach (int index in piece)
                {
                    line[index] = false;
                }
            }
        }
    }

    /// <summary>
    /// The key of the knot a pixel lies at: the three seeds whose cells meet there, in order.
    /// </summary>
    private static long KnotKey(Hit hit, int seeds)
    {
        int a = hit.Seed;
        int b = hit.Across;
        int c = hit.SecondAcross;
        if (b < 0 || c < 0)
        {
            return -1;
        }

        int low = Math.Min(a, Math.Min(b, c));
        int high = Math.Max(a, Math.Max(b, c));
        int middle = a + b + c - low - high;
        return ((long)low * seeds + middle) * seeds + high;
    }

    /// <summary>
    /// A value in [0, 1] that depends only on a knot and <paramref name="salt"/>.
    /// </summary>
    private static double Chance(long key, int seeds, int salt)
    {
        return TerrainArt.Hash01((int)(key / seeds), (int)(key % seeds), salt);
    }
}
