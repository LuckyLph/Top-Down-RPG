using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Planks: rows of boards running along x, each board a few pixels high over a one-pixel gap along its bottom row, and
/// cut at butt joints on the cell borders, at random lengths of whole cells, so the joints stagger from row to row and a
/// path of whole cells never shows a sliver of a board at its side. A board takes one of the palette's colours with a
/// little tint of its own, is lit along its top row and left end and shaded along its bottom row and right end (the light
/// comes from the top left), and carries short streaks of grain, now and then a knot, and a nail head over every joist it
/// crosses, the joists evenly spaced and centred in each cell. The boards' rows are whole fractions of a cell, so the
/// gaps and joints lie on the cell borders and boards turned a quarter by the shader (a material that turns to its runs)
/// meet unturned ones along a gap. The cell field (cut stones) points
/// every pixel straight across its board, at the board's middle row in the same column, since a board can be longer
/// than a dock is wide and its centre would lie off the dock; its alpha is the pixel's distance to its board's edge (the
/// gap rows and joints, at pixel centres), so the terrain shader cuts a board along the rim and bevels its cut end, and
/// leaves out a board whose middle row lies in the rim. Its blue channel is the gap's shade under every pixel; the base
/// colour is the gap colour.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Terrain Recipe/Plank Boards", fileName = "Recipe_Planks")]
public sealed class PlankBoardsRecipe : TerrainMaterialRecipe
{
    private const int GritSalt = 13;

    [Header("Boards")]
    [Tooltip("The gaps between the boards, and the material's base colour.")]
    [SerializeField] private Color32 gap = new(40, 29, 21, 255);
    [Tooltip("Boards across a cell, each with a one-pixel gap along its bottom row: 2, 4 or 8, so the gaps fall on the cell borders.")]
    [SerializeField, Range(2, 8)] private int boardsPerCell = 4;
    [Tooltip("Joists across a cell, evenly spaced and centred in it. Nail heads fall on them.")]
    [SerializeField, Range(1, 4)] private int joistsPerCell = 2;
    [Tooltip("Shortest and longest board, in cells. Butt joints fall on the cell borders.")]
    [SerializeField] private Vector2Int boardLength = new(2, 5);
    [Tooltip("Each board takes one of these colours at random.")]
    [SerializeField] private Color32[] palette =
    {
        new(148, 110, 72, 255), new(138, 101, 66, 255), new(158, 120, 80, 255), new(129, 95, 62, 255),
        new(144, 113, 82, 255), new(134, 104, 75, 255)
    };
    [Tooltip("How much each board's colour moves from its palette colour, either way, in 0-255 steps.")]
    [SerializeField, Range(0f, 24f)] private float tint = 6f;
    [Tooltip("Per-pixel noise of the boards and gaps, in 0-255 steps from darkest to lightest.")]
    [SerializeField, Range(0f, 32f)] private float grit = 5f;
    [Header("Light")]
    [Tooltip("How much the light changes a board along its edges, in 0-255 steps: lighter along the top row and left end, darker along the bottom row and right end.")]
    [SerializeField, Range(0f, 64f)] private float bevel = 16f;
    [Tooltip("Width in pixels of the bevel inside a board's edge.")]
    [SerializeField, Range(0.5f, 3f)] private float bevelWidth = 1f;
    [Tooltip("How much darker the bevel is on the sides that face away from the light, in 0-255 steps.")]
    [SerializeField, Range(0f, 32f)] private float shadedSide = 6f;
    [Header("Grain")]
    [Tooltip("Streaks of grain per board per cell of its length.")]
    [SerializeField, Range(0f, 8f)] private float grainPerCell = 2f;
    [Tooltip("Shortest and longest streak in pixels.")]
    [SerializeField] private Vector2Int grainLength = new(4, 14);
    [Tooltip("Chance a streak is darker than the board rather than lighter.")]
    [SerializeField, Range(0f, 1f)] private float darkGrainChance = 0.7f;
    [Tooltip("How much darker a dark streak is, in 0-255 steps.")]
    [SerializeField, Range(0f, 48f)] private float darkGrain = 13f;
    [Tooltip("How much lighter a light streak is, in 0-255 steps.")]
    [SerializeField, Range(0f, 48f)] private float lightGrain = 7f;
    [Tooltip("Chance a board has a knot: two dark pixels side by side on a middle row, off its joists and ends.")]
    [SerializeField, Range(0f, 1f)] private float knotChance = 0.15f;
    [SerializeField] private Color32 knot = new(86, 60, 40, 255);
    [Header("Nails")]
    [Tooltip("Chance a board has a nail head over each joist it crosses, on its middle row.")]
    [SerializeField, Range(0f, 1f)] private float nailChance = 0.9f;
    [SerializeField] private Color32 nail = new(68, 60, 54, 255);

    public override TerrainMaterial.CellMode CellMode => TerrainMaterial.CellMode.CutStones;

    /// <summary>
    /// Lays the joints of every row of boards, draws each board's colour, tint and height value, then the height waves,
    /// the grain, knots and nails, and paints the boards, the gaps, the cell field and the height.
    /// </summary>
    protected override TerrainTextures Generate(System.Random random, int size)
    {
        int pitch = Pitch();
        int rows = Math.Max(1, size / pitch);
        int joistStep = Math.Max(1, TerrainStyle.PixelsPerCell / joistsPerCell);
        int cells = Math.Max(1, size / TerrainStyle.PixelsPerCell);
        List<int>[] joints = LayJoints(random, rows, cells);
        int boards = 0;
        int[] firstBoard = new int[rows];
        for (int row = 0; row < rows; row++)
        {
            firstBoard[row] = boards;
            boards += joints[row].Count;
        }

        Color32[] colours = new Color32[boards];
        double[] values = new double[boards];
        for (int b = 0; b < boards; b++)
        {
            Color32 colour = palette.Length > 0 ? palette[random.Next(palette.Length)] : gap;
            colours[b] = TerrainArt.Shade(colour, TerrainArt.Uniform(random, -tint, tint));
            values[b] = random.NextDouble();
        }

        double[] height = SmoothHeight(random, size);
        int[] owner = new int[size * size];
        int[] segmentStart = new int[size * size];
        int[] segmentEnd = new int[size * size];
        for (int y = 0; y < size; y++)
        {
            int row = Math.Min(y / pitch, rows - 1);
            List<int> rowJoints = joints[row];
            for (int x = 0; x < size; x++)
            {
                int segment = SegmentOf(rowJoints, x);
                int i = y * size + x;
                owner[i] = firstBoard[row] + segment;
                segmentStart[i] = rowJoints[segment] * TerrainStyle.PixelsPerCell;
                segmentEnd[i] = rowJoints[(segment + 1) % rowJoints.Count] * TerrainStyle.PixelsPerCell;
            }
        }

        double[] marks = new double[size * size];
        Color32?[] solid = new Color32?[size * size];
        DrawMarks(random, size, pitch, rows, joistStep, cells, joints, marks, solid);

        Color32[] albedo = new Color32[size * size];
        Color32[] field = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            int rowStart = Math.Min(y / pitch, rows - 1) * pitch;
            int centreRow = rowStart + pitch / 2;
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                double pixelGrit = (TerrainArt.Hash01(x, y, GritSalt) - 0.5) * grit;
                int amount = Mathf.Clamp(TerrainArt.Round(pixelGrit), -128, 127);
                int up = y - rowStart;
                int down = rowStart + pitch - y;
                int fromStart = TerrainArt.Wrap(x - segmentStart[i], size);
                int toEnd = TerrainArt.Wrap(segmentEnd[i] - x, size);

                int across = Math.Min(up, down);
                int along = Math.Min(fromStart, toEnd);
                int border = Math.Min(across, along);
                if (border == 0)
                {
                    albedo[i] = TerrainArt.Shade(gap, amount);
                }
                else if (solid[i].HasValue)
                {
                    albedo[i] = TerrainArt.Shade(solid[i].Value, pixelGrit);
                }
                else
                {
                    double lit = across <= along ? (up > down ? TerrainArt.LightY : -TerrainArt.LightY) : (fromStart < toEnd ? -TerrainArt.LightX : TerrainArt.LightX);
                    double bevelAmount = TerrainArt.Clamp01(1.0 - (border - 1) / bevelWidth);
                    double shade = lit * bevel * bevelAmount + marks[i] + pixelGrit;
                    if (lit < 0.0)
                    {
                        shade -= shadedSide * bevelAmount;
                    }

                    albedo[i] = TerrainArt.Shade(colours[owner[i]], shade);
                }

                byte distance = (byte)Mathf.Clamp(TerrainArt.Round(border * 16.0), 1, 255);
                field[i] = new Color32(128, (byte)(128 + centreRow - y), (byte)(128 + amount), distance);
            }
        }

        return new TerrainTextures(size, gap, CellMode, albedo, field, TerrainArt.HeightPixels(height, owner, values));
    }

    /// <summary>
    /// The height of a row of boards in pixels, gap included: a whole fraction of a cell (16, 8 or 4).
    /// </summary>
    private int Pitch()
    {
        int boards = boardsPerCell >= 8 ? 8 : boardsPerCell >= 4 ? 4 : 2;
        return TerrainStyle.PixelsPerCell / boards;
    }

    /// <summary>
    /// For every row of boards, the cell borders its joints lie on (a cell's index for its left border), in order along x
    /// from a random first one: boards of a random length between the shortest and longest, the last one taking what is
    /// left (never under the shortest, so at times over the longest), and each joint kept off the joints of the two rows
    /// below where another draw allows.
    /// </summary>
    private List<int>[] LayJoints(System.Random random, int rows, int cells)
    {
        int shortest = Mathf.Clamp(Mathf.Min(boardLength.x, boardLength.y), 1, cells);
        int longest = Mathf.Clamp(Mathf.Max(boardLength.x, boardLength.y), shortest, cells);
        List<int>[] joints = new List<int>[rows];
        for (int row = 0; row < rows; row++)
        {
            HashSet<int> below = new();
            for (int back = 1; back <= 2 && row - back >= 0; back++)
            {
                below.UnionWith(joints[row - back]);
            }
            int first = random.Next(cells);
            for (int attempt = 0; attempt < 6 && below.Contains(first); attempt++)
            {
                first = random.Next(cells);
            }

            List<int> rowJoints = new() { first };
            int laid = 0;
            while (true)
            {
                int length = shortest + random.Next(longest - shortest + 1);
                for (int attempt = 0; attempt < 6 && below.Contains((first + laid + length) % cells); attempt++)
                {
                    length = shortest + random.Next(longest - shortest + 1);
                }

                if (laid + length > cells - shortest)
                {
                    break;
                }

                laid += length;
                rowJoints.Add((first + laid) % cells);
            }

            rowJoints.Sort();
            joints[row] = rowJoints;
        }

        return joints;
    }

    /// <summary>
    /// The board of a row that pixel column <paramref name="x"/> lies on: the index of the last joint at or before it,
    /// or the last joint's board, which runs on round the texture's edge, before the first joint.
    /// </summary>
    private static int SegmentOf(List<int> rowJoints, int x)
    {
        int segment = rowJoints.Count - 1;
        for (int j = 0; j < rowJoints.Count; j++)
        {
            if (rowJoints[j] * TerrainStyle.PixelsPerCell <= x)
            {
                segment = j;
            }
        }

        return segment;
    }

    /// <summary>
    /// Draws the grain streaks (a shade added to the board) and the knots and nail heads (colours of their own) of every
    /// board, from <paramref name="random"/> in board order: streaks along a row inside the board's bevel, clipped short
    /// of its ends; a knot on a middle row, off the joists and ends; a nail on the middle row over each joist the board
    /// crosses.
    /// </summary>
    private void DrawMarks(System.Random random, int size, int pitch, int rows, int joistStep, int cells, List<int>[] joints, double[] marks, Color32?[] solid)
    {
        int middle = pitch / 2;
        for (int row = 0; row < rows; row++)
        {
            int rowStart = row * pitch;
            List<int> rowJoints = joints[row];
            for (int b = 0; b < rowJoints.Count; b++)
            {
                int lengthCells = TerrainArt.Wrap(rowJoints[(b + 1) % rowJoints.Count] - rowJoints[b], cells);
                if (lengthCells == 0)
                {
                    lengthCells = cells;
                }

                int start = rowJoints[b] * TerrainStyle.PixelsPerCell;
                int length = lengthCells * TerrainStyle.PixelsPerCell;
                int streaks = TerrainArt.Round(grainPerCell * lengthCells);
                for (int s = 0; s < streaks; s++)
                {
                    int streakRow = rowStart + 2 + random.Next(Math.Max(1, pitch - 3));
                    int streakLength = Mathf.Min(grainLength.x, grainLength.y) + random.Next(Mathf.Abs(grainLength.y - grainLength.x) + 1);
                    int from = 2 + random.Next(Math.Max(1, length - 3));
                    double amount = random.NextDouble() < darkGrainChance ? -darkGrain : lightGrain;
                    for (int k = 0; k < streakLength && from + k < length - 1; k++)
                    {
                        marks[streakRow * size + TerrainArt.Wrap(start + from + k, size)] += amount;
                    }
                }

                if (random.NextDouble() < knotChance)
                {
                    int along = 3 + random.Next(Math.Max(1, length - 7));
                    if (TerrainArt.Wrap(along - joistStep / 2, joistStep) == 0 || TerrainArt.Wrap(along + 1 - joistStep / 2, joistStep) == 0)
                    {
                        along += 2;
                    }

                    int knotRow = rowStart + middle - 1 + random.Next(2);
                    solid[knotRow * size + TerrainArt.Wrap(start + along, size)] = knot;
                    solid[knotRow * size + TerrainArt.Wrap(start + along + 1, size)] = knot;
                }

                int nailRow = (rowStart + middle) * size;
                for (int at = joistStep / 2; at < length; at += joistStep)
                {
                    if (random.NextDouble() < nailChance)
                    {
                        solid[nailRow + TerrainArt.Wrap(start + at, size)] = nail;
                    }
                }
            }
        }
    }
}
