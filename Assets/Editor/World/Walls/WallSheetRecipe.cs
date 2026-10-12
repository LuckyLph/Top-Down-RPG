using System;
using UnityEngine;

/// <summary>
/// An editor-only recipe for the walls' 16-piece dual-grid sheet (Docs/DualGrid.md, Walls), made like the terrain
/// material recipes: the sheet texture it fills, the walls rule tile whose tiling rules say which quadrants each piece
/// fills, a seed and the look. Each piece is drawn from the exact signed distance, at each pixel's centre, to its
/// filled quadrants around the piece's centre (a corner of the data cells): outer corners rounded, inner corners
/// filleted and straight along the cell borders, so pieces that meet agree. From the edge inwards come a dark outline,
/// a darker front face along edges that face down, then the top: square blocks of stone in shades drawn at random,
/// laid out the same in every piece. <see cref="Generate(int)"/> returns the pixels without touching an asset, the
/// same seed and settings always giving the same pixels; <see cref="WallSheetWriter"/> writes and slices them as the
/// sheet, and the inspector previews them.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Wall Sheet Recipe", fileName = "Recipe_Walls")]
public sealed class WallSheetRecipe : ScriptableObject
{
    public const int PiecesPerSide = 4;
    public const int PieceCount = PiecesPerSide * PiecesPerSide;
    private const float Far = 1000f;

    [Tooltip("The sheet Generate writes and slices: a PNG whose pieces the walls rule tile draws.")]
    [SerializeField] private Texture2D sheet;
    [Tooltip("The walls rule tile: each tiling rule's sprite names its piece (the slice ending in _<index>) and its neighbours the quadrants that piece fills.")]
    [SerializeField] private DualGridRuleTile ruleTile;
    [Tooltip("The same seed always gives the same sheet.")]
    [SerializeField] private int seed = 1;
    [Tooltip("Pixels per piece side, which is also the sheet's pixels per unit: one piece covers one cell.")]
    [SerializeField, Range(8, 128)] private int pieceSize = 32;
    [Tooltip("Radius of outer corners, in pixels. Both radii stay under half a piece.")]
    [SerializeField, Min(0f)] private float outerRadius = 6f;
    [Tooltip("Radius of the fillet in inner corners, in pixels.")]
    [SerializeField, Min(0f)] private float innerRadius = 6.2f;
    [Tooltip("Width of the dark outline inside the edge, in pixels, measured to pixel centres.")]
    [SerializeField, Min(0f)] private float outlineWidth = 1.5f;
    [Tooltip("Height of the front face above the outline of an edge that faces down, in pixels: a pixel inside the outline is face where the point the outline's width plus this height below it lies outside the wall.")]
    [SerializeField, Min(0f)] private float faceHeight = 2.5f;
    [Tooltip("Side of the top's stone blocks, in pixels.")]
    [SerializeField, Min(1)] private int blockSize = 2;
    [SerializeField] private Color32 outlineColour = new(52, 47, 44, 255);
    [SerializeField] private Color32 faceColour = new(86, 81, 76, 255);
    [Tooltip("Shades of the top's blocks, each block one of them at random.")]
    [SerializeField] private Color32[] topColours = { new(113, 108, 101, 255), new(122, 116, 108, 255), new(131, 125, 116, 255) };

    public Texture2D Sheet => sheet;
    public DualGridRuleTile RuleTile => ruleTile;
    public int Seed => seed;
    public int PieceSize => pieceSize;
    public int SheetSize => pieceSize * PiecesPerSide;
    public float OuterRadius => outerRadius;
    public float InnerRadius => innerRadius;

    /// <summary>
    /// The sheet's pixels for <paramref name="sheetSeed"/>, rows from the bottom, with each piece's quadrants read from
    /// the rule tile (<see cref="PieceMasks"/>). Throws when the rule tile or the settings cannot make a sheet.
    /// </summary>
    public Color32[] Generate(int sheetSeed)
    {
        int[] masks = PieceMasks(ruleTile, out string problem);
        if (masks == null)
        {
            throw new InvalidOperationException($"{name}: {problem}");
        }

        return Generate(sheetSeed, masks);
    }

    /// <summary>
    /// Whether the settings can make a sheet: a top colour, and corner radii and an outline and face under half a piece,
    /// so every edge stays inside its piece. Returns the reason in <paramref name="problem"/> when they cannot.
    /// </summary>
    public bool CanGenerate(out string problem)
    {
        float half = pieceSize * 0.5f;
        problem = topColours == null || topColours.Length == 0 || outerRadius >= half || innerRadius >= half || outlineWidth + faceHeight >= half
            ? $"a wall sheet needs a top colour, and corner radii and an outline and face under half a piece ({half} px)."
            : null;
        return problem == null;
    }

    /// <summary>
    /// The sheet's pixels for <paramref name="sheetSeed"/>, rows from the bottom as <see cref="Texture2D.SetPixels32"/>
    /// takes them, piece i filling the quadrants of <paramref name="pieceMasks"/>[i] (see <see cref="PieceMasks"/>).
    /// Every random value comes from one <see cref="System.Random"/> seeded with <paramref name="sheetSeed"/>. Throws
    /// when there are not 16 masks or when the settings cannot make a sheet (<see cref="CanGenerate"/>).
    /// </summary>
    public Color32[] Generate(int sheetSeed, int[] pieceMasks)
    {
        if (pieceMasks == null || pieceMasks.Length != PieceCount)
        {
            throw new ArgumentException($"A wall sheet needs {PieceCount} piece masks.", nameof(pieceMasks));
        }

        if (!CanGenerate(out string problem))
        {
            throw new InvalidOperationException($"{name}: {problem}");
        }

        float half = pieceSize * 0.5f;

        System.Random random = new(sheetSeed);
        int blocksAcross = (pieceSize + blockSize - 1) / blockSize;
        int[] shades = new int[blocksAcross * blocksAcross];
        for (int i = 0; i < shades.Length; i++)
        {
            shades[i] = random.Next(topColours.Length);
        }

        int size = SheetSize;
        Color32[] pixels = new Color32[size * size];
        for (int piece = 0; piece < PieceCount; piece++)
        {
            RectInt rect = PieceRect(piece, pieceSize);
            int mask = pieceMasks[piece];
            for (int y = 0; y < pieceSize; y++)
            {
                for (int x = 0; x < pieceSize; x++)
                {
                    Color32 colour = PiecePixel(mask, x + 0.5f - half, y + 0.5f - half, shades[y / blockSize * blocksAcross + x / blockSize]);
                    pixels[(rect.y + y) * size + rect.x + x] = colour;
                }
            }
        }

        return pixels;
    }

    /// <summary>
    /// The quadrants each piece of the 16 fills, read from <paramref name="tile"/>'s tiling rules: rule r's first
    /// sprite names its piece by the index after its last underscore, and its four diagonal neighbours the quadrants,
    /// as bits in the terrain shader's corner order (1 down-left, 2 down-right, 4 up-left, 8 up-right). Returns null
    /// with the reason in <paramref name="problem"/> unless the tile has 16 rules naming each piece once, each with all
    /// four neighbours set.
    /// </summary>
    public static int[] PieceMasks(DualGridRuleTile tile, out string problem)
    {
        problem = null;
        if (tile == null || tile.m_TilingRules == null || tile.m_TilingRules.Count != PieceCount)
        {
            problem = $"the walls rule tile needs exactly {PieceCount} tiling rules.";
            return null;
        }

        int[] masks = new int[PieceCount];
        bool[] seen = new bool[PieceCount];
        foreach (RuleTile.TilingRule rule in tile.m_TilingRules)
        {
            Sprite sprite = rule.m_Sprites != null && rule.m_Sprites.Length > 0 ? rule.m_Sprites[0] : null;
            int piece = sprite != null ? PieceIndex(sprite.name) : -1;
            if (piece < 0 || piece >= PieceCount || seen[piece])
            {
                problem = $"each tiling rule's sprite must name a different piece from 0 to {PieceCount - 1} ({(sprite != null ? sprite.name : "no sprite")}).";
                return null;
            }

            int mask = 0;
            int set = 0;
            for (int i = 0; i < rule.m_NeighborPositions.Count && i < rule.m_Neighbors.Count; i++)
            {
                int bit = CornerBit(rule.m_NeighborPositions[i]);
                if (bit < 0)
                {
                    continue;
                }

                set |= bit;
                mask |= rule.m_Neighbors[i] == DualGridRuleTile.DualGridNeighbor.Filled ? bit : 0;
            }

            if (set != 15)
            {
                problem = $"the tiling rule of {sprite.name} must set all four diagonal neighbours.";
                return null;
            }

            seen[piece] = true;
            masks[piece] = mask;
        }

        return masks;
    }

    /// <summary>
    /// The piece index a slice name ends with, after its last underscore, or -1 when it ends with none.
    /// </summary>
    public static int PieceIndex(string sliceName)
    {
        int underscore = sliceName.LastIndexOf('_');
        return underscore >= 0 && int.TryParse(sliceName.Substring(underscore + 1), out int index) ? index : -1;
    }

    /// <summary>
    /// Where piece <paramref name="index"/> lies on a sheet of <paramref name="size"/> px pieces, in pixels from the
    /// bottom left: four pieces a row, piece 0 at the top left, as Grid By Cell Count 4 x 4 slices a sheet.
    /// </summary>
    public static RectInt PieceRect(int index, int size)
    {
        int column = index % PiecesPerSide;
        int rowFromTop = index / PiecesPerSide;
        return new RectInt(column * size, (PiecesPerSide - 1 - rowFromTop) * size, size, size);
    }

    /// <summary>
    /// The colour of a piece's pixel at (<paramref name="x"/>, <paramref name="y"/>) pixels from its centre: clear
    /// outside the shape, the outline within its width of the edge, the face where the point the outline's width plus
    /// the face's height below lies outside the shape, else the top colour <paramref name="shade"/>.
    /// </summary>
    private Color32 PiecePixel(int mask, float x, float y, int shade)
    {
        float distance = ShapeDistance(mask, x, y);
        if (distance > 0f)
        {
            return new Color32(0, 0, 0, 0);
        }

        if (distance > -outlineWidth)
        {
            return outlineColour;
        }

        return ShapeDistance(mask, x, y - outlineWidth - faceHeight) > 0f ? faceColour : topColours[shade];
    }

    /// <summary>
    /// The exact signed distance, positive outside, from (<paramref name="x"/>, <paramref name="y"/>) pixels from a
    /// corner to the filled quadrants of <paramref name="mask"/> around it, outer corners rounded and inner ones
    /// filleted; the C# twin of RoundedCorners.hlsl's CornerShapeDistance.
    /// </summary>
    private float ShapeDistance(int mask, float x, float y)
    {
        switch (mask)
        {
            case 0:
                return Far;
            case 15:
                return -Far;
            case 3:
                return y;
            case 12:
                return -y;
            case 5:
                return x;
            case 10:
                return -x;
            case 6:
                return Mathf.Min(RoundedQuarter(1, x, y, outerRadius), RoundedQuarter(2, x, y, outerRadius));
            case 9:
                return Mathf.Min(RoundedQuarter(0, x, y, outerRadius), RoundedQuarter(3, x, y, outerRadius));
            case 1:
            case 2:
            case 4:
            case 8:
                return RoundedQuarter(LowestBit(mask), x, y, outerRadius);
            default:
                return -RoundedQuarter(LowestBit(~mask & 15), x, y, innerRadius);
        }
    }

    /// <summary>
    /// The signed distance to the quadrant of cell <paramref name="cell"/> around a corner (0 down-left, 1 down-right,
    /// 2 up-left, 3 up-right) with its corner rounded by <paramref name="radius"/>.
    /// </summary>
    private static float RoundedQuarter(int cell, float x, float y, float radius)
    {
        float u = ((cell & 1) != 0 ? x : -x) - radius;
        float v = ((cell & 2) != 0 ? y : -y) - radius;
        float quarter = u >= 0f && v >= 0f ? -Mathf.Min(u, v) : Mathf.Sqrt(Mathf.Min(u, 0f) * Mathf.Min(u, 0f) + Mathf.Min(v, 0f) * Mathf.Min(v, 0f));
        return quarter - radius;
    }

    /// <summary>
    /// The index of the lowest set bit of the low four of <paramref name="mask"/>.
    /// </summary>
    private static int LowestBit(int mask)
    {
        return (mask & 1) != 0 ? 0 : (mask & 2) != 0 ? 1 : (mask & 4) != 0 ? 2 : 3;
    }

    /// <summary>
    /// The corner bit of a diagonal neighbour position of a dual-grid tiling rule, or -1 for any other position.
    /// </summary>
    private static int CornerBit(Vector3Int position)
    {
        if (Mathf.Abs(position.x) != 1 || Mathf.Abs(position.y) != 1)
        {
            return -1;
        }

        return 1 << ((position.x > 0 ? 1 : 0) + (position.y > 0 ? 2 : 0));
    }
}
