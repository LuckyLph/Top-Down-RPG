using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A piece of the UI skin (Docs/DesignSystem.md, Foundations: Art), one sprite each in <see cref="UiSkinRecipe.Folder"/>.
/// Append-only: the writer names each sprite after its piece.
/// </summary>
public enum UiSkinPiece
{
    Panel,
    PanelFrame,
    PanelRaised,
    SlotFrame,
    BarFrame,
    BarFill,
    MarkerMove,
    MarkerAttackMove,
    MarkerHover,
    Pip,
    Check,
    SlotCorners,
    Hatch,
    BarTick,
    Notch,
    Badge,
    BadgeLarge,
    GripMove,
    GripResize,
}

/// <summary>
/// An editor-only recipe for the UI skin (Docs/DesignSystem.md, Foundations: Art), made like the prop and terrain
/// recipes: greyscale sprites that images and sprite renderers tint by colour role, at one pixel per UI px (or per art
/// px in the world). The panels, frames and bars are 9-sliced, their corners cut along a diagonal by
/// <see cref="CornerCut"/> px, none since Docs/UiRevamp.md's Phase 3, so square: a panel's fill, the 1 px frame around
/// it, a raised panel with a darker lip under its face, a slot frame with a dark line inside it, a
/// bar's frame (the track its fill sits in) and its fill, a pip, a small mark shown whole, a check mark, a toggle's
/// mark while it is on, a slot's corner brackets, shown around a slot whose cast waits, and a hatch of diagonal lines,
/// tiled over a slot that cannot be cast (Docs/UiRevamp.md, Decisions: Slot states), a bar's tick, a short line down from
/// the top of a health bar every so many HP, and a debuff's notch, a right triangle in an icon's top right corner
/// (Docs/UiRevamp.md, Phase 2), a player slot's badge, a disc outlined in black, small over a player in the world and
/// large in a party frame (Phase 5), and a window's two grips, a block of dots at its header's right end that moves it
/// and three diagonal strokes in its bottom right corner that resize it (Phase 12). The markers are drawn around a ground ellipse twice as wide as tall, each a different
/// shape so colour is never the only cue: four chevrons pointing in for a move, the ring crossed by four ticks for an
/// attack move, and four brackets on the ring's diagonals under a hovered enemy, each line outlined in translucent
/// black. <see cref="Generate(UiSkinPiece)"/> draws a piece without touching an asset, the same settings always giving
/// the same pixels; <see cref="UiSkinWriter"/> writes them and sets their 9-slice borders, and the inspector previews
/// them.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/UI/UI Skin Recipe", fileName = "Recipe_UiSkin")]
public sealed class UiSkinRecipe : ScriptableObject
{
    public const string Folder = "Assets/Sprites/UI/Skin";

    [Header("Frames")]
    [Tooltip("How many px each corner of a panel, frame and bar is cut by along a diagonal; 0 keeps them square.")]
    [SerializeField, Range(0, 3)] private int cornerCut;
    [Tooltip("How many px of a raised panel's bottom are its lip, under its face.")]
    [SerializeField, Range(0, 4)] private int lipHeight = 2;
    [Tooltip("The grey of a raised panel's lip, so its tint shows darker there by as much.")]
    [SerializeField, Range(0f, 1f)] private float lipGrey = 0.6f;
    [Tooltip("The opacity of the black line just inside a slot frame.")]
    [SerializeField, Range(0f, 1f)] private float slotShadow = 0.4f;

    [Header("Markers")]
    [Tooltip("The half-axes in px of the ground ellipse the markers are drawn around: twice as wide as tall, as the ground is seen.")]
    [SerializeField] private Vector2Int markerRadii = new(15, 7);
    [Tooltip("The thickness in px of a marker's ring and chevrons, grown inwards.")]
    [SerializeField, Range(1, 3)] private int markerLine = 2;
    [Tooltip("The length in px of a move marker's chevron arms and an attack move marker's ticks, halved upright.")]
    [SerializeField, Range(1, 6)] private int markerArm = 5;
    [Tooltip("The opacity of the black outline around a marker's lines.")]
    [SerializeField, Range(0f, 1f)] private float markerOutline = 0.5f;
    [Tooltip("The share of the ring each hover bracket keeps, around its diagonal.")]
    [SerializeField, Range(0.1f, 1f)] private float bracketShare = 0.75f;

    [Header("Slot states")]
    [Tooltip("The length in px of each arm of a slot's corner brackets, the corner pixel included.")]
    [SerializeField, Range(2, 8)] private int cornerArm = 5;
    [Tooltip("The thickness in px of a slot's corner brackets.")]
    [SerializeField, Range(1, 2)] private int cornerLine = 1;
    [Tooltip("The px between the hatch's diagonal lines, which are 1 px wide: the size of the tile it repeats.")]
    [SerializeField, Range(2, 8)] private int hatchSpacing = 4;

    [Header("Health and statuses")]
    [Tooltip("The length in px of a health bar's tick, a 1 px line down from the top of its fill.")]
    [SerializeField, Range(1, 8)] private int tickLength = 3;
    [Tooltip("The legs in px of a debuff's notch, the right triangle in its top right corner.")]
    [SerializeField, Range(2, 10)] private int notchSize = 5;

    [Header("Player slots")]
    [Tooltip("The diameter in px of a player slot's badge beside its world bar, inside its outline.")]
    [SerializeField, Range(3, 9)] private int badgeSize = 5;
    [Tooltip("The diameter in px of a player slot's badge in a party frame, inside its outline.")]
    [SerializeField, Range(8, 24)] private int badgeLargeSize = 16;
    [Tooltip("The opacity of the 1 px black outline around a badge.")]
    [SerializeField, Range(0f, 1f)] private float badgeOutline = 1f;

    [Header("Windows")]
    [Tooltip("The side in px of each of the six dots, three across and two down, of a window's move grip.")]
    [SerializeField, Range(1, 3)] private int gripDot = 2;
    [Tooltip("The px between the dots of a window's move grip.")]
    [SerializeField, Range(1, 3)] private int gripGap = 1;
    [Tooltip("The side in px of a window's resize grip, the square its strokes fill from its bottom right corner.")]
    [SerializeField, Range(3, 12)] private int resizeGripSize = 7;
    [Tooltip("The px between the resize grip's diagonal strokes, counted along its edges.")]
    [SerializeField, Range(2, 6)] private int resizeGripSpacing = 3;

    public int CornerCut => cornerCut;
    public int LipHeight => lipHeight;
    public Vector2Int MarkerRadii => markerRadii;

    /// <summary>
    /// Every piece, in the order of <see cref="UiSkinPiece"/>.
    /// </summary>
    public static IReadOnlyList<UiSkinPiece> Pieces { get; } = (UiSkinPiece[])Enum.GetValues(typeof(UiSkinPiece));

    /// <summary>
    /// The path of the sprite written for <paramref name="piece"/>.
    /// </summary>
    public static string PathOf(UiSkinPiece piece)
    {
        return $"{Folder}/{piece}.png";
    }

    /// <summary>
    /// Whether the settings can draw every piece: marker radii of at least 4 by 2 px. Returns the reason in
    /// <paramref name="problem"/> when they cannot.
    /// </summary>
    public bool CanGenerate(out string problem)
    {
        problem = markerRadii.x < 4 || markerRadii.y < 2 ? "the marker radii must be at least 4 by 2 px." : null;
        return problem == null;
    }

    /// <summary>
    /// The pixels of <paramref name="piece"/>, rows from the bottom, and its 9-slice border (left, bottom, right, top;
    /// zero for a marker).
    /// </summary>
    public SkinSprite Generate(UiSkinPiece piece)
    {
        switch (piece)
        {
            case UiSkinPiece.Panel:
                return Panel(piece, false);
            case UiSkinPiece.PanelFrame:
                return Panel(piece, true);
            case UiSkinPiece.PanelRaised:
                return Raised();
            case UiSkinPiece.SlotFrame:
                return Slot();
            case UiSkinPiece.BarFrame:
                return Bar();
            case UiSkinPiece.BarFill:
                return Fill();
            case UiSkinPiece.Pip:
                return Pip();
            case UiSkinPiece.Check:
                return Check();
            case UiSkinPiece.SlotCorners:
                return Corners();
            case UiSkinPiece.Hatch:
                return Hatch();
            case UiSkinPiece.BarTick:
                return Tick();
            case UiSkinPiece.Notch:
                return Notch();
            case UiSkinPiece.Badge:
                return Badge(piece, badgeSize);
            case UiSkinPiece.BadgeLarge:
                return Badge(piece, badgeLargeSize);
            case UiSkinPiece.GripMove:
                return GripMove();
            case UiSkinPiece.GripResize:
                return GripResize();
            default:
                return Marker(piece);
        }
    }

    /// <summary>
    /// A panel's fill, or the 1 px frame along its edge: <see cref="cornerCut"/> + 2 px corners around 2 px that stretch.
    /// </summary>
    private SkinSprite Panel(UiSkinPiece piece, bool frame)
    {
        int corner = cornerCut + 2;
        int size = 2 * corner + 2;
        bool[] shape = Shape(size, size);
        bool[] drawn = frame ? Ring(shape, size, size) : shape;
        return new SkinSprite(piece, size, size, Paint(drawn, Grey(1f)), new Vector4(corner, corner, corner, corner));
    }

    /// <summary>
    /// A raised panel: the panel's shape, taller by <see cref="lipHeight"/>, whose bottom rows are the lip in
    /// <see cref="lipGrey"/> under a white face.
    /// </summary>
    private SkinSprite Raised()
    {
        int corner = cornerCut + 2;
        int width = 2 * corner + 2;
        int height = width + lipHeight;
        bool[] shape = Shape(width, height);
        Color32[] pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                pixels[i] = shape[i] ? Grey(y < lipHeight ? lipGrey : 1f) : Clear;
            }
        }

        return new SkinSprite(UiSkinPiece.PanelRaised, width, height, pixels, new Vector4(corner, corner + lipHeight, corner, corner));
    }

    /// <summary>
    /// A slot frame: the panel's 1 px frame in white with a line of black at <see cref="slotShadow"/> just inside it.
    /// </summary>
    private SkinSprite Slot()
    {
        int corner = cornerCut + 2;
        int size = 2 * corner + 2;
        bool[] shape = Shape(size, size);
        bool[] outer = Ring(shape, size, size);
        bool[] inside = new bool[shape.Length];
        for (int i = 0; i < shape.Length; i++)
        {
            inside[i] = shape[i] && !outer[i];
        }

        bool[] inner = Ring(inside, size, size);
        Color32[] pixels = new Color32[size * size];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = outer[i] ? Grey(1f) : inner[i] ? Black(slotShadow) : Clear;
        }

        return new SkinSprite(UiSkinPiece.SlotFrame, size, size, pixels, new Vector4(corner, corner, corner, corner));
    }

    /// <summary>
    /// A bar's frame: the track its fill sits in, 1 px inside it, so the frame shows as a 1 px edge around the fill and
    /// as the empty part of the bar. Small enough to slice down to a 4 px bar: <see cref="cornerCut"/> + 1 px corners
    /// around 1 px that stretches.
    /// </summary>
    private SkinSprite Bar()
    {
        int corner = cornerCut + 1;
        int size = 2 * corner + 1;
        return new SkinSprite(UiSkinPiece.BarFrame, size, size, Paint(Shape(size, size), Grey(1f)), new Vector4(corner, corner, corner, corner));
    }

    /// <summary>
    /// A bar's fill and the plain fill of any image that is filled or stretched: 3 px of white, square, so a filled
    /// image keeps its edge wherever it stops.
    /// </summary>
    private static SkinSprite Fill()
    {
        bool[] all = new bool[9];
        Array.Fill(all, true);
        return new SkinSprite(UiSkinPiece.BarFill, 3, 3, Paint(all, Grey(1f)), new Vector4(1f, 1f, 1f, 1f));
    }

    /// <summary>
    /// A small mark shown whole rather than sliced (a status over a unit): 5 px with the panel's corners.
    /// </summary>
    private SkinSprite Pip()
    {
        return new SkinSprite(UiSkinPiece.Pip, 5, 5, Paint(Shape(5, 5), Grey(1f)), Vector4.zero);
    }

    /// <summary>
    /// A toggle's mark while it is on, shown whole: a check mark 8 px wide and 5 tall in strokes 2 px across at 45
    /// degrees, its short arm three rows tall and its long arm five, meeting in the bottom row.
    /// </summary>
    private static SkinSprite Check()
    {
        const int width = 8;
        const int height = 5;
        bool[] drawn = new bool[width * height];
        for (int row = 0; row < height; row++)
        {
            int y = height - 1 - row;
            int longArm = width - 2 - row;
            drawn[y * width + longArm] = true;
            drawn[y * width + longArm + 1] = true;
            int shortArm = row - 2;
            if (shortArm >= 0)
            {
                drawn[y * width + shortArm] = true;
                drawn[y * width + shortArm + 1] = true;
            }
        }

        return new SkinSprite(UiSkinPiece.Check, width, height, Paint(drawn, Grey(1f)), Vector4.zero);
    }

    /// <summary>
    /// A slot's corner brackets, 9-sliced so they keep to the corners of whatever they frame: an L of
    /// <see cref="cornerArm"/> px arms, <see cref="cornerLine"/> px thick, in each corner, and nothing between them.
    /// </summary>
    private SkinSprite Corners()
    {
        int size = 2 * cornerArm + 1;
        bool[] drawn = new bool[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int fromX = Math.Min(x, size - 1 - x);
                int fromY = Math.Min(y, size - 1 - y);
                bool inCorner = fromX < cornerArm && fromY < cornerArm;
                drawn[y * size + x] = inCorner && (fromX < cornerLine || fromY < cornerLine);
            }
        }

        return new SkinSprite(UiSkinPiece.SlotCorners, size, size, Paint(drawn, Grey(1f)), new Vector4(cornerArm, cornerArm, cornerArm, cornerArm));
    }

    /// <summary>
    /// A hatch tile, shown tiled rather than sliced: a 1 px diagonal rising to the right across a square of
    /// <see cref="hatchSpacing"/> px, so the tiles join into lines that far apart.
    /// </summary>
    private SkinSprite Hatch()
    {
        int size = hatchSpacing;
        bool[] drawn = new bool[size * size];
        for (int i = 0; i < size; i++)
        {
            drawn[i * size + i] = true;
        }

        return new SkinSprite(UiSkinPiece.Hatch, size, size, Paint(drawn, Grey(1f)), Vector4.zero);
    }

    /// <summary>
    /// A health bar's tick, shown whole: a 1 px line <see cref="tickLength"/> px long.
    /// </summary>
    private SkinSprite Tick()
    {
        bool[] drawn = new bool[tickLength];
        Array.Fill(drawn, true);
        return new SkinSprite(UiSkinPiece.BarTick, 1, tickLength, Paint(drawn, Grey(1f)), Vector4.zero);
    }

    /// <summary>
    /// A debuff's notch, shown whole: a right triangle with <see cref="notchSize"/> px legs along the top and right
    /// edges, its hypotenuse a 1-1 diagonal, to sit in an icon's top right corner.
    /// </summary>
    private SkinSprite Notch()
    {
        int size = notchSize;
        bool[] drawn = new bool[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                drawn[y * size + x] = x >= size - 1 - y;
            }
        }

        return new SkinSprite(UiSkinPiece.Notch, size, size, Paint(drawn, Grey(1f)), Vector4.zero);
    }

    /// <summary>
    /// A player slot's badge, shown whole: a disc <paramref name="diameter"/> px across (the pixels whose centres lie
    /// within half of it of its centre) in white, with a 1 px outline around it in black at <see cref="badgeOutline"/>,
    /// so the sprite is 2 px wider and taller than the disc.
    /// </summary>
    private SkinSprite Badge(UiSkinPiece piece, int diameter)
    {
        int size = diameter + 2;
        bool[] disc = new bool[size * size];
        double radius = diameter * 0.5;
        for (int y = 0; y < diameter; y++)
        {
            for (int x = 0; x < diameter; x++)
            {
                disc[(y + 1) * size + x + 1] = Sq(x + 0.5 - radius) + Sq(y + 0.5 - radius) <= radius * radius;
            }
        }

        Color32[] pixels = new Color32[disc.Length];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                pixels[i] = disc[i] ? Grey(1f) : Touches(disc, size, size, x, y) ? Black(badgeOutline) : Clear;
            }
        }

        return new SkinSprite(piece, size, size, pixels, Vector4.zero);
    }

    /// <summary>
    /// A window's move grip, shown whole: six <see cref="gripDot"/> px square dots, three across and two down,
    /// <see cref="gripGap"/> px apart.
    /// </summary>
    private SkinSprite GripMove()
    {
        int width = 3 * gripDot + 2 * gripGap;
        int height = 2 * gripDot + gripGap;
        bool[] drawn = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                drawn[y * width + x] = x % (gripDot + gripGap) < gripDot && y % (gripDot + gripGap) < gripDot;
            }
        }

        return new SkinSprite(UiSkinPiece.GripMove, width, height, Paint(drawn, Grey(1f)), Vector4.zero);
    }

    /// <summary>
    /// A window's resize grip, shown whole in its bottom right corner: 1 px diagonal strokes across the half of a
    /// square of <see cref="resizeGripSize"/> px below its diagonal, the first the corner pixel itself and each next one
    /// <see cref="resizeGripSpacing"/> px further along the edges.
    /// </summary>
    private SkinSprite GripResize()
    {
        int size = resizeGripSize;
        bool[] drawn = new bool[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int along = size - 1 - x + y;
                drawn[y * size + x] = along < size && along % resizeGripSpacing == 0;
            }
        }

        return new SkinSprite(UiSkinPiece.GripResize, size, size, Paint(drawn, Grey(1f)), Vector4.zero);
    }

    /// <summary>
    /// One of the three markers, every one on the same canvas around the same centre: the ground ellipse with room
    /// for the ticks and the outline around it.
    /// </summary>
    private SkinSprite Marker(UiSkinPiece piece)
    {
        int rx = markerRadii.x;
        int ry = markerRadii.y;
        int armX = markerArm;
        int armY = Mathf.Max(1, (markerArm + 1) / 2);
        int width = 2 * (rx + armX + 1) + 1;
        int height = 2 * (ry + armY + 1) + 1;
        int cx = width / 2;
        int cy = height / 2;
        bool[] core = new bool[width * height];
        void Plot(int x, int y)
        {
            if (x >= 0 && y >= 0 && x < width && y < height)
            {
                core[y * width + x] = true;
            }
        }

        void Line(int x0, int y0, int x1, int y1)
        {
            int dx = Math.Abs(x1 - x0);
            int dy = -Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int error = dx + dy;
            while (true)
            {
                Plot(x0, y0);
                if (x0 == x1 && y0 == y1)
                {
                    break;
                }

                int doubled = 2 * error;
                if (doubled >= dy)
                {
                    error += dy;
                    x0 += sx;
                }

                if (doubled <= dx)
                {
                    error += dx;
                    y0 += sy;
                }
            }
        }

        if (piece == UiSkinPiece.MarkerMove)
        {
            for (int i = 0; i < markerLine; i++)
            {
                Line(cx - rx + i, cy + armX, cx - rx + armX + i, cy);
                Line(cx - rx + armX + i, cy, cx - rx + i, cy - armX);
                Line(cx + rx - i, cy + armX, cx + rx - armX - i, cy);
                Line(cx + rx - armX - i, cy, cx + rx - i, cy - armX);
                Line(cx - armX, cy + ry - i, cx, cy + ry - armY - i);
                Line(cx, cy + ry - armY - i, cx + armX, cy + ry - i);
                Line(cx - armX, cy - ry + i, cx, cy - ry + armY + i);
                Line(cx, cy - ry + armY + i, cx + armX, cy - ry + i);
            }
        }
        else
        {
            List<Vector2Int> ring = new();
            for (int i = 0; i < markerLine && i < ry; i++)
            {
                ring.AddRange(Ellipse(rx - i, ry - i));
            }

            for (int y = -ry; y <= ry; y++)
            {
                for (int x = -rx; x <= rx; x++)
                {
                    if (Band(x, y, rx, ry, markerLine))
                    {
                        ring.Add(new Vector2Int(x, y));
                    }
                }
            }

            foreach (Vector2Int point in ring)
            {
                bool keep = true;
                if (piece == UiSkinPiece.MarkerHover)
                {
                    float angle = Mathf.Atan2(Mathf.Abs(point.y) / (float)ry, Mathf.Abs(point.x) / (float)rx) * Mathf.Rad2Deg;
                    keep = Mathf.Abs(angle - 45f) <= 45f * bracketShare;
                }

                if (keep)
                {
                    Plot(cx + point.x, cy + point.y);
                }
            }

            if (piece == UiSkinPiece.MarkerAttackMove)
            {
                Line(cx - rx - armX, cy, cx - rx + armX, cy);
                Line(cx + rx - armX, cy, cx + rx + armX, cy);
                Line(cx, cy - ry - armY, cx, cy - ry + armY);
                Line(cx, cy + ry - armY, cx, cy + ry + armY);
            }
        }

        Color32[] pixels = new Color32[core.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                pixels[i] = core[i] ? Grey(1f) : Touches(core, width, height, x, y) ? Black(markerOutline) : Clear;
            }
        }

        return new SkinSprite(piece, width, height, pixels, Vector4.zero);
    }

    /// <summary>
    /// Whether the pixel (<paramref name="x"/>, <paramref name="y"/>) from the centre lies strictly between the ellipse
    /// of half-axes <paramref name="rx"/> and <paramref name="ry"/> and the one <paramref name="thickness"/> - 1 px
    /// inside it, which fills the gaps between their drawn pixels.
    /// </summary>
    private static bool Band(int x, int y, int rx, int ry, int thickness)
    {
        if (thickness < 2)
        {
            return false;
        }

        double outer = Sq(x / (double)rx) + Sq(y / (double)ry);
        double innerX = rx - thickness + 1;
        double innerY = ry - thickness + 1;
        double inner = innerX > 0 && innerY > 0 ? Sq(x / innerX) + Sq(y / innerY) : double.MaxValue;
        return outer < 1.0 && inner > 1.0;
    }

    /// <summary>
    /// <paramref name="value"/> squared.
    /// </summary>
    private static double Sq(double value)
    {
        return value * value;
    }

    /// <summary>
    /// The pixels of an ellipse of half-axes <paramref name="rx"/> and <paramref name="ry"/> around the origin, one px
    /// wide and joined at their corners (the midpoint ellipse algorithm).
    /// </summary>
    private static List<Vector2Int> Ellipse(int rx, int ry)
    {
        List<Vector2Int> points = new();
        void Add(int x, int y)
        {
            points.Add(new Vector2Int(x, y));
            points.Add(new Vector2Int(-x, y));
            points.Add(new Vector2Int(x, -y));
            points.Add(new Vector2Int(-x, -y));
        }

        long rx2 = (long)rx * rx;
        long ry2 = (long)ry * ry;
        int px = 0;
        int py = ry;
        long dx = 0;
        long dy = 2 * rx2 * py;
        double decision = ry2 - rx2 * ry + 0.25 * rx2;
        while (dx < dy)
        {
            Add(px, py);
            px++;
            dx += 2 * ry2;
            if (decision < 0)
            {
                decision += ry2 + dx;
            }
            else
            {
                py--;
                dy -= 2 * rx2;
                decision += ry2 + dx - dy;
            }
        }

        decision = ry2 * (px + 0.5) * (px + 0.5) + rx2 * (py - 1.0) * (py - 1.0) - rx2 * ry2;
        while (py >= 0)
        {
            Add(px, py);
            py--;
            dy -= 2 * rx2;
            if (decision > 0)
            {
                decision += rx2 - dy;
            }
            else
            {
                px++;
                dx += 2 * ry2;
                decision += rx2 - dy + dx;
            }
        }

        return points;
    }

    /// <summary>
    /// A <paramref name="width"/> x <paramref name="height"/> rectangle with each corner cut along a diagonal by
    /// <see cref="cornerCut"/> px: a pixel is outside when its distances to a corner's two edges add up to less than
    /// the cut.
    /// </summary>
    private bool[] Shape(int width, int height)
    {
        bool[] shape = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int fromX = Math.Min(x, width - 1 - x);
                int fromY = Math.Min(y, height - 1 - y);
                shape[y * width + x] = fromX + fromY >= cornerCut;
            }
        }

        return shape;
    }

    /// <summary>
    /// The pixels of <paramref name="shape"/> beside a pixel outside it or the sprite's edge, left, right, above or
    /// below: a 1 px line along its edge whose corners step along the diagonal.
    /// </summary>
    private static bool[] Ring(bool[] shape, int width, int height)
    {
        bool[] ring = new bool[shape.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!shape[y * width + x])
                {
                    continue;
                }

                ring[y * width + x] = !Inside(shape, width, height, x - 1, y) || !Inside(shape, width, height, x + 1, y)
                    || !Inside(shape, width, height, x, y - 1) || !Inside(shape, width, height, x, y + 1);
            }
        }

        return ring;
    }

    /// <summary>
    /// Whether pixel (<paramref name="x"/>, <paramref name="y"/>) lies in the sprite and in <paramref name="shape"/>.
    /// </summary>
    private static bool Inside(bool[] shape, int width, int height, int x, int y)
    {
        return x >= 0 && y >= 0 && x < width && y < height && shape[y * width + x];
    }

    /// <summary>
    /// Whether a pixel of <paramref name="core"/> lies among the eight around (<paramref name="x"/>,
    /// <paramref name="y"/>).
    /// </summary>
    private static bool Touches(bool[] core, int width, int height, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if ((dx != 0 || dy != 0) && Inside(core, width, height, x + dx, y + dy))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// <paramref name="colour"/> where <paramref name="drawn"/> is set, clear elsewhere.
    /// </summary>
    private static Color32[] Paint(bool[] drawn, Color32 colour)
    {
        Color32[] pixels = new Color32[drawn.Length];
        for (int i = 0; i < drawn.Length; i++)
        {
            pixels[i] = drawn[i] ? colour : Clear;
        }

        return pixels;
    }

    private static Color32 Clear => new(0, 0, 0, 0);

    /// <summary>
    /// An opaque grey of <paramref name="value"/>, 0 black to 1 white.
    /// </summary>
    private static Color32 Grey(float value)
    {
        byte level = (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
        return new Color32(level, level, level, 255);
    }

    /// <summary>
    /// Black at <paramref name="opacity"/>.
    /// </summary>
    private static Color32 Black(float opacity)
    {
        return new Color32(0, 0, 0, (byte)Mathf.RoundToInt(Mathf.Clamp01(opacity) * 255f));
    }
}

/// <summary>
/// One sprite of the UI skin as <see cref="UiSkinRecipe"/> draws it: its piece, size, pixels (rows from the bottom) and
/// 9-slice border in px (left, bottom, right, top).
/// </summary>
public readonly struct SkinSprite
{
    /// <summary>
    /// Records a drawn piece.
    /// </summary>
    public SkinSprite(UiSkinPiece piece, int width, int height, Color32[] pixels, Vector4 border)
    {
        Piece = piece;
        Width = width;
        Height = height;
        Pixels = pixels;
        Border = border;
    }

    public UiSkinPiece Piece { get; }
    public int Width { get; }
    public int Height { get; }
    public Color32[] Pixels { get; }
    public Vector4 Border { get; }
}
