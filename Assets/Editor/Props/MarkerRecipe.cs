using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The shape of a marker prop: a waystone (a standing stone carved with runes on a plinth), a doorway (a stone frame
/// round a plank door), a signpost (a post with an arrow board) or a doormat (a woven mat lying on the ground).
/// </summary>
public enum MarkerForm
{
    Waystone,
    Doorway,
    Signpost,
    Doormat
}

/// <summary>
/// A marker prop (Docs/KnownGaps.md, Phase 3): what shows a world's waystones and exits, drawn on the props' canvas and
/// written by the prop writer like any prop, its prefabs placed by the World Editor's Place markers step
/// (<c>WorldMarkers</c>). A waystone is a standing stone on a plinth with a chain of diamond runes carved down its face;
/// its variants are its two states, drawn from one stream so they share their shape: <see cref="DormantVariant"/>, the
/// runes carved dark, and <see cref="AwakeVariant"/>, the runes and a halo round them glowing; it writes one prefab
/// (<see cref="PrefabCount"/>) whose <see cref="WaystoneMarker"/> switches between them, with a small cool light that
/// burns at night while it is awake. A doorway (an exit into an interior), a signpost (a road's exit to another world:
/// <see cref="SignpostWest"/> pointing west, <see cref="SignpostEast"/> east) and a doormat (an exit out of an interior:
/// <see cref="DoormatAcross"/> for an exit two cells wide, <see cref="DoormatAlong"/> two cells tall,
/// <see cref="DoormatSingle"/> one cell) write a prefab per variant.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Props/Marker Recipe", fileName = "Waystone")]
public sealed class MarkerRecipe : PropRecipe, IPropLightSettings
{
    public const int DormantVariant = 0;
    public const int AwakeVariant = 1;
    public const int SignpostWest = 0;
    public const int SignpostEast = 1;
    public const int DoormatAcross = 0;
    public const int DoormatAlong = 1;
    public const int DoormatSingle = 2;

    private const float FrontFacing = -0.7f;
    private const float PlinthSink = 0.04f;
    private const float Chamfer = 0.05f;
    private const float FacetReach = 0.9f;
    private const float RuneWidth = 0.075f;
    private const float RuneHeight = 0.08f;
    private const float RuneStep = 0.18f;
    private const float FirstRune = 0.35f;
    private const float LightHeight = 0.6f;

    [Tooltip("A waystone, a doorway, a signpost or a doormat.")]
    [SerializeField] private MarkerForm form = MarkerForm.Waystone;

    [Header("Waystone (cells)")]
    [Tooltip("How tall the stone stands above its plinth.")]
    [SerializeField, Min(0.5f)] private float stoneHeight = 1.5f;
    [Tooltip("The stone's half width and half depth at its foot.")]
    [SerializeField] private Vector2 stoneFoot = new(0.27f, 0.18f);
    [Tooltip("The stone's half width and half depth at its top, before its top is cut.")]
    [SerializeField] private Vector2 stoneTop = new(0.17f, 0.12f);
    [Tooltip("The octagonal plinth's radius to the middle of each side, and its height.")]
    [SerializeField] private Vector2 plinth = new(0.36f, 0.08f);
    [Tooltip("How far the stone leans either way, in radians.")]
    [SerializeField, Range(0f, 0.2f)] private float lean = 0.05f;
    [Tooltip("How many faces cut the stone's sides at random, so it reads as hewn rather than sawn.")]
    [SerializeField, Min(0)] private int facets = 2;
    [Tooltip("How many diamond runes are carved down its face.")]
    [SerializeField, Min(1)] private int runes = 3;

    [Header("Doormat (cells)")]
    [Tooltip("The mat across an exit two cells wide; one two cells tall takes it turned.")]
    [SerializeField] private Vector2 doormatSize = new(1.85f, 0.9f);
    [Tooltip("The mat of an exit of one cell.")]
    [SerializeField] private Vector2 singleDoormatSize = new(0.9f, 0.8f);

    [Header("Light")]
    [Tooltip("The light an awake waystone gives at night.")]
    [SerializeField] private Color32 lightColour = new(140, 205, 255, 255);
    [Tooltip("The light's strength at night, at its middle.")]
    [SerializeField, Min(0f)] private float lightIntensity = 0.9f;
    [Tooltip("How far the light reaches, in cells.")]
    [SerializeField, Min(0.5f)] private float lightRadius = 3f;
    [Tooltip("How far the light's strength flickers either way, as a share of it.")]
    [SerializeField, Range(0f, 0.5f)] private float lightFlicker;

    [Header("Colours")]
    [Tooltip("The standing stone.")]
    [SerializeField] private ColourRamp stone = Ramp(0x1a1d26, 0x434858, 0x666d80, 0x8f97a8);
    [Tooltip("Dressed stone: the plinth, the doorway's frame and threshold and the signpost's foot.")]
    [SerializeField] private ColourRamp dressed = Ramp(0x26282e, 0x5e626c, 0x868a92, 0xb4b6b4);
    [Tooltip("A sleeping waystone's runes, drawn flat in their middle tone.")]
    [SerializeField] private ColourRamp carved = Ramp(0x1a1d26, 0x2a2e3a, 0x2a2e3a, 0x2a2e3a);
    [Tooltip("An awake waystone's runes in their middle tone and the halo round them in their shadow tone, both glowing.")]
    [SerializeField] private ColourRamp rune = Ramp(0x1e5a6e, 0x4ec3dc, 0xb6fbff, 0xeaffff);
    [Tooltip("The door's planks and the signpost's post.")]
    [SerializeField] private ColourRamp wood = Ramp(0x24140c, 0x4a2a18, 0x6e4024, 0x94603a);
    [Tooltip("The signpost's board, its carved marks in its outline colour.")]
    [SerializeField] private ColourRamp board = Ramp(0x1e140e, 0x4e3420, 0x765236, 0x9e7650);
    [Tooltip("The door's bands, studs and ring.")]
    [SerializeField] private ColourRamp iron = Ramp(0x14141a, 0x2c2e36, 0x484c56, 0x6c707a);
    [Tooltip("The doormat's border and stripes.")]
    [SerializeField] private ColourRamp matBorder = Ramp(0x2a0e0c, 0x5a2016, 0x7e3422, 0x9a4a30);
    [Tooltip("The doormat's field, speckled where it is worn, and its fringe in its light tone.")]
    [SerializeField] private ColourRamp matField = Ramp(0x3a2610, 0x7a5a2a, 0xa07c40, 0xc49e5c);

    public MarkerForm Form => form;
    public Color32 LightColour => lightColour;
    public float LightIntensity => lightIntensity;
    public float LightRadius => lightRadius;
    public float LightFlicker => lightFlicker;

    /// <summary>
    /// A waystone's light casts no shadows: it is small, and its prop's own caster stops the other lights.
    /// </summary>
    public bool LightCastsShadows => false;

    /// <summary>
    /// A waystone writes one prefab, switching between its two variants; every other marker one per variant.
    /// </summary>
    public override int PrefabCount => form == MarkerForm.Waystone ? 1 : VariantCount;

    /// <summary>
    /// A waystone's states draw from the first variant's stream, so they share their shape and their stone's noise.
    /// </summary>
    public override int StreamIndex(int index)
    {
        return form == MarkerForm.Waystone ? 0 : index;
    }

    /// <inheritdoc />
    public override void Draw(PropDrawing drawing)
    {
        switch (form)
        {
            case MarkerForm.Doorway:
                DrawDoorway(drawing);
                break;
            case MarkerForm.Signpost:
                DrawSignpost(drawing, drawing.Variant == SignpostEast);
                break;
            case MarkerForm.Doormat:
                DrawDoormat(drawing, SizeOf(drawing.Variant));
                break;
            default:
                DrawWaystone(drawing, drawing.Variant != DormantVariant);
                break;
        }
    }

    /// <summary>
    /// A waystone needs both its states; a signpost both its directions; a doormat its three sizes.
    /// </summary>
    protected override string KindProblem()
    {
        int needed = form switch
        {
            MarkerForm.Waystone => 2,
            MarkerForm.Signpost => 2,
            MarkerForm.Doormat => 3,
            _ => 1
        };
        return VariantCount < needed ? $"a {form} marker needs at least {needed} variants." : null;
    }

    /// <summary>
    /// The size of doormat variant <paramref name="variant"/>: across two cells, along two cells or one cell.
    /// </summary>
    private Vector2 SizeOf(int variant)
    {
        return variant switch
        {
            DoormatAcross => doormatSize,
            DoormatAlong => new Vector2(doormatSize.y, doormatSize.x),
            _ => singleDoormatSize
        };
    }

    /// <summary>
    /// A tapering standing stone, its sides hewn by a few random faces, its top rounded by six more, leaning a little,
    /// on an octagonal plinth of dressed stone, with a chain of diamond runes carved down the middle of its front face:
    /// flat and dark while it sleeps, glowing with a glowing halo once <paramref name="awake"/>. Its light shines from
    /// the runes, in either state, so the prefab drawn from the sleeping variant carries it.
    /// </summary>
    private void DrawWaystone(PropDrawing drawing, bool awake)
    {
        int plinthId = drawing.AddMaterial(new ArtMaterial(dressed, pattern: ArtPattern.Speckle, patternScale: 0.12f), true);
        int stoneId = drawing.AddMaterial(new ArtMaterial(stone, pattern: ArtPattern.Speckle, patternScale: 0.08f), false);
        int runeId = awake
            ? drawing.AddMaterial(new ArtMaterial(Flat(rune.Middle), true, false, glow: 1f), false)
            : drawing.AddMaterial(new ArtMaterial(carved, true, false), false);
        int haloId = awake ? drawing.AddMaterial(new ArtMaterial(Flat(rune.Shadow), true, false, glow: 0.7f), false) : -1;
        ArtCanvas canvas = drawing.Canvas;

        List<Vector4> plinthPlanes = new();
        for (int k = 0; k < 8; k++)
        {
            float angle = (k + 0.5f) * Mathf.PI / 4f;
            plinthPlanes.Add(new Vector4(Mathf.Cos(angle), Mathf.Sin(angle), 0f, plinth.x));
        }

        plinthPlanes.Add(new Vector4(0f, 0f, 1f, plinth.y));
        plinthPlanes.Add(new Vector4(0f, 0f, -1f, PlinthSink));
        canvas.Convex(plinthPlanes, new Bounds(new Vector3(0f, 0f, plinth.y * 0.5f), new Vector3(plinth.x * 2.4f, plinth.x * 2.4f, plinth.y + 0.1f)), plinthId, drawing.NewPart());

        float z0 = plinth.y - 0.02f;
        float z1 = z0 + stoneHeight;
        Quaternion turn = Quaternion.AngleAxis(drawing.Range(-lean, lean) * Mathf.Rad2Deg, Vector3.up);
        List<Vector4> planes = new()
        {
            Plane(new Vector3(stoneHeight, 0f, stoneFoot.x - stoneTop.x), new Vector3(stoneFoot.x, 0f, z0)),
            Plane(new Vector3(-stoneHeight, 0f, stoneFoot.x - stoneTop.x), new Vector3(-stoneFoot.x, 0f, z0)),
            Plane(new Vector3(0f, stoneHeight, stoneFoot.y - stoneTop.y), new Vector3(0f, stoneFoot.y, z0)),
            Plane(new Vector3(0f, -stoneHeight, stoneFoot.y - stoneTop.y), new Vector3(0f, -stoneFoot.y, z0)),
            new Vector4(0f, 0f, -1f, -z0 + PlinthSink)
        };

        float edgeRise = -(stoneFoot.y * (stoneTop.x - stoneFoot.x) + stoneFoot.x * (stoneTop.y - stoneFoot.y)) / stoneHeight;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sy = -1; sy <= 1; sy += 2)
            {
                Vector3 n = new Vector3(sx * stoneFoot.y, sy * stoneFoot.x, edgeRise).normalized;
                planes.Add(new Vector4(n.x, n.y, n.z, Vector3.Dot(n, new Vector3(sx * stoneFoot.x, sy * stoneFoot.y, z0)) - Chamfer));
            }
        }

        Vector3 apex = new(drawing.Range(-0.05f, 0.05f), 0f, z1 + 0.1f);
        float spin = drawing.Range(0f, 2f * Mathf.PI);
        for (int k = 0; k < 6; k++)
        {
            float angle = spin + k * Mathf.PI / 3f + drawing.Range(-0.3f, 0.3f);
            float steep = drawing.Range(0.9f, 1.6f);
            planes.Add(Plane(new Vector3(Mathf.Cos(angle) * steep, Mathf.Sin(angle) * steep * 1.4f, 1f), apex - new Vector3(0f, 0f, drawing.Range(0.06f, 0.12f))));
        }

        planes.Add(new Vector4(0f, 0f, 1f, z1 + drawing.Range(0f, 0.03f)));
        for (int k = 0; k < facets; k++)
        {
            float angle = drawing.Range(0f, 2f * Mathf.PI);
            float z = z0 + stoneHeight * drawing.Range(0.15f, 0.85f);
            Vector3 n = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle) * 1.6f, drawing.Range(-0.3f, 0.5f)).normalized;
            Vector3 on = new(Mathf.Cos(angle) * stoneFoot.x * FacetReach, Mathf.Sin(angle) * stoneFoot.y * FacetReach, z);
            planes.Add(new Vector4(n.x, n.y, n.z, Vector3.Dot(n, on)));
        }

        for (int i = 0; i < planes.Count; i++)
        {
            Vector3 n = turn * (Vector3)planes[i];
            planes[i] = new Vector4(n.x, n.y, n.z, planes[i].w);
        }

        canvas.Convex(planes, new Bounds(new Vector3(0f, 0f, (z0 + z1) * 0.5f + 0.05f), new Vector3(stoneFoot.x * 3f, stoneFoot.y * 3f, stoneHeight + 0.4f)), stoneId, drawing.NewPart());

        Face face = new(canvas, turn, z0, stoneHeight, stoneFoot.y, stoneTop.y, stoneId, runeId);
        for (int k = 0; k < runes; k++)
        {
            float at = z0 + stoneHeight * (FirstRune + k * RuneStep);
            face.Stroke(new Vector2(0f, at + RuneHeight), new Vector2(RuneWidth, at));
            face.Stroke(new Vector2(RuneWidth, at), new Vector2(0f, at - RuneHeight));
            face.Stroke(new Vector2(0f, at - RuneHeight), new Vector2(-RuneWidth, at));
            face.Stroke(new Vector2(-RuneWidth, at), new Vector2(0f, at + RuneHeight));
        }

        if (haloId >= 0)
        {
            face.Surround(haloId);
        }

        float middle = z0 + stoneHeight * LightHeight;
        drawing.MarkLight(turn * new Vector3(0f, face.FrontAt(middle), middle), 1f);
    }

    /// <summary>
    /// A stone threshold, two jambs and a lintel with a keystone framing a plank door with two studded iron bands and
    /// a ring.
    /// </summary>
    private void DrawDoorway(PropDrawing drawing)
    {
        int stoneId = drawing.AddMaterial(new ArtMaterial(dressed, pattern: ArtPattern.Speckle, patternScale: 0.1f), true);
        int frameId = drawing.AddMaterial(new ArtMaterial(dressed, pattern: ArtPattern.Speckle, patternScale: 0.1f), false);
        int woodId = drawing.AddMaterial(new ArtMaterial(wood, pattern: ArtPattern.Bark, patternScale: 9f), false);
        int ironId = drawing.AddMaterial(new ArtMaterial(iron), false);
        ArtCanvas canvas = drawing.Canvas;
        Box(canvas, new Vector3(-0.46f, -0.2f, -0.03f), new Vector3(0.46f, 0.18f, 0.06f), stoneId, drawing.NewPart());
        Box(canvas, new Vector3(0.28f, -0.12f, 0.06f), new Vector3(0.42f, 0.12f, 1.36f), frameId, drawing.NewPart());
        Box(canvas, new Vector3(-0.42f, -0.12f, 0.06f), new Vector3(-0.28f, 0.12f, 1.36f), frameId, drawing.NewPart());
        Box(canvas, new Vector3(-0.5f, -0.14f, 1.36f), new Vector3(0.5f, 0.14f, 1.5f), frameId, drawing.NewPart());
        Box(canvas, new Vector3(-0.07f, -0.17f, 1.32f), new Vector3(0.07f, 0.16f, 1.56f), frameId, drawing.NewPart());
        Box(canvas, new Vector3(-0.28f, -0.02f, 0.06f), new Vector3(0.28f, 0.04f, 1.36f), woodId, drawing.NewPart());
        int bands = drawing.NewPart();
        foreach (float z in new[] { 0.3f, 1.06f })
        {
            Box(canvas, new Vector3(-0.27f, -0.045f, z), new Vector3(0.27f, -0.025f, z + 0.06f), ironId, bands);
            for (int k = 0; k < 4; k++)
            {
                canvas.Sphere(new Vector3(-0.2f + k * 0.133f, -0.05f, z + 0.03f), 0.012f, ironId, bands, 0.5f);
            }
        }

        int ring = drawing.NewPart();
        for (int k = 0; k < 10; k++)
        {
            float angle = k * 2f * Mathf.PI / 10f;
            canvas.Sphere(new Vector3(0.17f + Mathf.Cos(angle) * 0.04f, -0.05f, 0.7f + Mathf.Sin(angle) * 0.04f), 0.012f, ironId, ring, 0.5f);
        }
    }

    /// <summary>
    /// A square post with a cap, staked among three stones, with an arrow board near its top pointing west, or east
    /// when <paramref name="east"/>, three marks carved along it.
    /// </summary>
    private void DrawSignpost(PropDrawing drawing, bool east)
    {
        int stoneId = drawing.AddMaterial(new ArtMaterial(dressed, pattern: ArtPattern.Speckle, patternScale: 0.1f), true);
        int postId = drawing.AddMaterial(new ArtMaterial(wood, pattern: ArtPattern.Bark, patternScale: 24f), true);
        int boardId = drawing.AddMaterial(new ArtMaterial(board, pattern: ArtPattern.Bark, patternScale: 14f, patternAxis: Vector3.right), false);
        int markId = drawing.AddMaterial(new ArtMaterial(Flat(board.Outline), true, false), false);
        ArtCanvas canvas = drawing.Canvas;
        float turn = drawing.Range(0f, 2f * Mathf.PI);
        for (int k = 0; k < 3; k++)
        {
            float angle = turn + k * 2f * Mathf.PI / 3f + drawing.Range(-0.4f, 0.4f);
            float size = 0.07f * drawing.Range(0.8f, 1.2f);
            Vector3 at = new(Mathf.Cos(angle) * (0.06f + size), Mathf.Sin(angle) * (0.06f + size), size * 0.4f);
            canvas.Ellipsoid(at, Vector3.right * size, Vector3.up * size * 0.85f, Vector3.forward * size * 0.7f, stoneId, drawing.NewPart());
        }

        Box(canvas, new Vector3(-0.05f, -0.05f, -0.02f), new Vector3(0.05f, 0.05f, 1.36f), postId, drawing.NewPart());
        canvas.Convex(new List<Vector4>
        {
            Plane(new Vector3(1f, 0f, 1.2f), new Vector3(0f, 0f, 1.47f)), Plane(new Vector3(-1f, 0f, 1.2f), new Vector3(0f, 0f, 1.47f)),
            Plane(new Vector3(0f, 1f, 1.2f), new Vector3(0f, 0f, 1.47f)), Plane(new Vector3(0f, -1f, 1.2f), new Vector3(0f, 0f, 1.47f)),
            new Vector4(0f, 0f, -1f, -1.34f)
        }, new Bounds(new Vector3(0f, 0f, 1.4f), new Vector3(0.3f, 0.3f, 0.2f)), postId, drawing.NewPart());
        float s = east ? -1f : 1f;
        float tipX = -0.44f * s;
        float baseX = -0.3f * s;
        float tailX = 0.3f * s;
        float zMid = 1.1f;
        float half = 0.11f;
        Vector3 up = new Vector3(-half * s, 0f, baseX * s - tipX * s).normalized;
        Vector3 down = new Vector3(-half * s, 0f, -(baseX * s - tipX * s)).normalized;
        List<Vector4> planes = new()
        {
            new Vector4(0f, 1f, 0f, -0.05f), new Vector4(0f, -1f, 0f, 0.1f), new Vector4(0f, 0f, 1f, zMid + half), new Vector4(0f, 0f, -1f, -(zMid - half)),
            new Vector4(s, 0f, 0f, s * tailX), Plane(up, new Vector3(tipX, 0f, zMid)), Plane(down, new Vector3(tipX, 0f, zMid))
        };
        canvas.Convex(planes, new Bounds(new Vector3(0f, -0.075f, zMid), new Vector3(1f, 0.12f, 0.3f)), boardId, drawing.NewPart());
        for (int k = 0; k < 3; k++)
        {
            float x = Mathf.Lerp(baseX + 0.04f * s, tailX - 0.08f * s, k / 2f);
            Vector2Int pixel = canvas.PixelOf(new Vector3(x, -0.101f, zMid));
            for (int dx = -1; dx <= 1; dx++)
            {
                int px = pixel.x + dx;
                if (canvas.Contains(px, pixel.y) && canvas.MaterialIdAt(px, pixel.y) == boardId)
                {
                    canvas.SetMaterialId(px, pixel.y, markId);
                }
            }
        }
    }

    /// <summary>
    /// A woven mat of <paramref name="size"/> lying flat: a border round a field with a stripe along each long side, the
    /// field speckled where it is worn, and a fringe at its short ends.
    /// </summary>
    private void DrawDoormat(PropDrawing drawing, Vector2 size)
    {
        bool across = size.x >= size.y;
        int borderId = drawing.AddMaterial(new ArtMaterial(matBorder), false);
        int fieldId = drawing.AddMaterial(new ArtMaterial(matField, pattern: ArtPattern.Speckle, patternScale: 0.25f), false);
        int stripeId = drawing.AddMaterial(new ArtMaterial(matBorder), false);
        int fringeId = drawing.AddMaterial(new ArtMaterial(Flat(matField.Light), true, false), false);
        ArtCanvas canvas = drawing.Canvas;
        Vector2 half = size * 0.5f;
        Box(canvas, new Vector3(-half.x, -half.y, -0.01f), new Vector3(half.x, half.y, 0.015f), borderId, drawing.NewPart());
        Box(canvas, new Vector3(-half.x + 0.07f, -half.y + 0.07f, -0.01f), new Vector3(half.x - 0.07f, half.y - 0.07f, 0.02f), fieldId, drawing.NewPart());
        int stripes = drawing.NewPart();
        for (int side = -1; side <= 1; side += 2)
        {
            if (across)
            {
                float y = side * (half.y - 0.17f);
                Box(canvas, new Vector3(-half.x + 0.14f, y - 0.03f, -0.01f), new Vector3(half.x - 0.14f, y + 0.03f, 0.022f), stripeId, stripes);
            }
            else
            {
                float x = side * (half.x - 0.17f);
                Box(canvas, new Vector3(x - 0.03f, -half.y + 0.14f, -0.01f), new Vector3(x + 0.03f, half.y - 0.14f, 0.022f), stripeId, stripes);
            }
        }

        int fringe = drawing.NewPart();
        int count = Mathf.RoundToInt((across ? size.y : size.x) * 10f);
        for (int end = -1; end <= 1; end += 2)
        {
            for (int k = 0; k < count; k++)
            {
                float t = (k + 0.5f) / count - 0.5f;
                Vector3 from = across ? new Vector3(end * half.x, t * size.y * 0.9f, 0.01f) : new Vector3(t * size.x * 0.9f, end * half.y, 0.01f);
                Vector3 to = from + (across ? Vector3.right : Vector3.up) * (end * 0.07f);
                canvas.Dot(from + (to - from) * 0.5f, fringeId, fringe);
                canvas.Dot(to, fringeId, fringe);
            }
        }
    }

    /// <summary>
    /// Draws the box from <paramref name="min"/> to <paramref name="max"/> as a convex solid.
    /// </summary>
    private static void Box(ArtCanvas canvas, Vector3 min, Vector3 max, int material, int part)
    {
        canvas.Convex(new[]
        {
            new Vector4(1f, 0f, 0f, max.x), new Vector4(-1f, 0f, 0f, -min.x), new Vector4(0f, 1f, 0f, max.y), new Vector4(0f, -1f, 0f, -min.y),
            new Vector4(0f, 0f, 1f, max.z), new Vector4(0f, 0f, -1f, -min.z)
        }, new Bounds((min + max) * 0.5f, (max - min) * 1.1f + Vector3.one * 0.02f), material, part);
    }

    /// <summary>
    /// The plane through <paramref name="point"/> facing <paramref name="normal"/>.
    /// </summary>
    private static Vector4 Plane(Vector3 normal, Vector3 point)
    {
        Vector3 n = normal.normalized;
        return new Vector4(n.x, n.y, n.z, Vector3.Dot(n, point));
    }

    /// <summary>
    /// A ramp of one colour.
    /// </summary>
    private static ColourRamp Flat(Color32 colour)
    {
        return new ColourRamp(colour, colour, colour, colour);
    }

    /// <summary>
    /// The front face of a waystone being carved: strokes laid on it in its own across and up coordinates (cells from its
    /// middle, and height) turn the stone's front-facing pixels they cross into a rune's, and a halo turns the stone's
    /// front-facing pixels beside the carved ones into its own.
    /// </summary>
    private sealed class Face
    {
        private readonly ArtCanvas canvas;
        private readonly Quaternion turn;
        private readonly float foot;
        private readonly float height;
        private readonly float footDepth;
        private readonly float topDepth;
        private readonly int stoneId;
        private readonly int runeId;
        private readonly List<Vector2Int> carved = new();

        /// <summary>
        /// The face of a stone turned by <paramref name="lean"/>, rising <paramref name="rise"/> from
        /// <paramref name="from"/>, its half depth <paramref name="depthAtFoot"/> at its foot and
        /// <paramref name="depthAtTop"/> at its top, drawn in <paramref name="stone"/>, carved in <paramref name="rune"/>.
        /// </summary>
        public Face(ArtCanvas canvas, Quaternion lean, float from, float rise, float depthAtFoot, float depthAtTop, int stone, int rune)
        {
            this.canvas = canvas;
            turn = lean;
            foot = from;
            height = rise;
            footDepth = depthAtFoot;
            topDepth = depthAtTop;
            stoneId = stone;
            runeId = rune;
        }

        /// <summary>
        /// Where the front face lies along the north axis at height <paramref name="z"/>, a hair in front of it.
        /// </summary>
        public float FrontAt(float z)
        {
            return -(footDepth + (topDepth - footDepth) * (z - foot) / height) - 0.01f;
        }

        /// <summary>
        /// Carves a stroke from <paramref name="a"/> to <paramref name="b"/>, sampled four times a pixel.
        /// </summary>
        public void Stroke(Vector2 a, Vector2 b)
        {
            int steps = Mathf.CeilToInt((b - a).magnitude * PropBaker.PixelsPerCell * 4f) + 1;
            for (int s = 0; s <= steps; s++)
            {
                Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
                Vector2Int pixel = canvas.PixelOf(turn * new Vector3(p.x, FrontAt(p.y), p.y));
                if (IsFace(pixel))
                {
                    canvas.SetMaterialId(pixel.x, pixel.y, runeId);
                    carved.Add(pixel);
                }
            }
        }

        /// <summary>
        /// Turns the face's pixels beside the carved ones into <paramref name="haloId"/>.
        /// </summary>
        public void Surround(int haloId)
        {
            foreach (Vector2Int pixel in carved)
            {
                foreach (Vector2Int step in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down })
                {
                    Vector2Int beside = pixel + step;
                    if (IsFace(beside))
                    {
                        canvas.SetMaterialId(beside.x, beside.y, haloId);
                    }
                }
            }
        }

        /// <summary>
        /// Whether <paramref name="pixel"/> shows the stone facing the camera.
        /// </summary>
        private bool IsFace(Vector2Int pixel)
        {
            return canvas.Contains(pixel.x, pixel.y) && canvas.MaterialIdAt(pixel.x, pixel.y) == stoneId && canvas.WorldNormalAt(pixel.x, pixel.y).y < FrontFacing;
        }
    }
}
