using UnityEngine;

/// <summary>
/// The shape of a light prop: a campfire (a ring of stones round a bed of coals, logs leaning together over it and
/// tongues of flame rising between them) or a lamp post (a post on an iron foot with a lantern on its top).
/// </summary>
public enum LightForm
{
    Campfire,
    LampPost
}

/// <summary>
/// A light prop (Docs/Lighting.md, Phase 5): a campfire or a lamp post, drawn on the props' canvas, its flame, coals or
/// glass glowing and its flame's pixels flickering between its tones, with the place its light shines from marked and
/// the light's colour, strength, radius and flicker for the prop writer, which puts the light on its prefab.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Props/Light Recipe", fileName = "Campfire")]
public sealed class LightRecipe : PropRecipe
{
    private const float StoneJitter = 0.25f;
    private const float StoneSink = 0.45f;
    private const float CoalsShare = 0.78f;
    private const float CoalsHeight = 0.05f;
    private const float LogFoot = 0.85f;
    private const float LogMeet = 0.03f;
    private const float FlameSpread = 0.1f;
    private const float FlameLean = 0.05f;
    private const float FlameFoot = 0.05f;
    private const float FlameTip = 0.012f;
    private const float MiddleShare = 0.68f;
    private const float CoreShare = 0.4f;
    private const float FootRadius = 0.11f;
    private const float FootHeight = 0.12f;
    private const float PlateHeight = 0.03f;
    private const float BarRadius = 0.022f;
    private const float RoofHeight = 0.12f;
    private const float RoofOverhang = 0.035f;
    private const float FinialRadius = 0.03f;
    private const float WickRadius = 0.05f;

    [Tooltip("A campfire or a lamp post.")]
    [SerializeField] private LightForm form = LightForm.Campfire;

    [Header("Campfire (cells)")]
    [Tooltip("The radius of the ring of stones.")]
    [SerializeField, Min(0.1f)] private float ringRadius = 0.36f;
    [Tooltip("How many stones ring the fire.")]
    [SerializeField, Min(3)] private int stones = 8;
    [Tooltip("Each stone's radius: the least and most.")]
    [SerializeField] private Vector2 stoneSize = new(0.08f, 0.11f);
    [Tooltip("How many logs lean together over the coals.")]
    [SerializeField, Min(0)] private int logs = 4;
    [Tooltip("How high the logs meet above the coals.")]
    [SerializeField, Min(0f)] private float logHeight = 0.22f;
    [SerializeField, Min(0.01f)] private float logRadius = 0.045f;
    [Tooltip("How many tongues of flame rise from the coals.")]
    [SerializeField, Min(1)] private int tongues = 5;
    [Tooltip("How tall the tongues are: the shortest and the tallest, the first.")]
    [SerializeField] private Vector2 flameHeight = new(0.26f, 0.48f);
    [Tooltip("A tongue's radius at its foot.")]
    [SerializeField, Min(0.01f)] private float flameWidth = 0.12f;

    [Header("Lamp post (cells)")]
    [Tooltip("How high the post stands, its foot included, to the lantern's floor.")]
    [SerializeField, Min(0.2f)] private float postHeight = 1.7f;
    [SerializeField, Min(0.01f)] private float postRadius = 0.06f;
    [SerializeField, Min(0.05f)] private float lanternWidth = 0.24f;
    [SerializeField, Min(0.05f)] private float lanternHeight = 0.3f;

    [Header("Light")]
    [Tooltip("The light's colour.")]
    [SerializeField] private Color32 lightColour = new(255, 186, 104, 255);
    [Tooltip("The light's strength at night, at its middle.")]
    [SerializeField, Min(0f)] private float lightIntensity = 1.2f;
    [Tooltip("How far the light reaches, in cells.")]
    [SerializeField, Min(0.5f)] private float lightRadius = 4f;
    [Tooltip("How far the light's strength flickers either way, as a share of it.")]
    [SerializeField, Range(0f, 0.5f)] private float lightFlicker = 0.12f;

    [Header("Colours")]
    [SerializeField] private ColourRamp stone = Ramp(0x26282e, 0x5e626c, 0x868a92, 0xb4b6b4);
    [SerializeField] private ColourRamp wood = Ramp(0x1e140e, 0x3e2a1c, 0x604230, 0x8a6644);
    [SerializeField] private ColourRamp charred = Ramp(0x0e0a0a, 0x241a16, 0x3a2a22, 0x5a4434);
    [SerializeField] private ColourRamp coals = Ramp(0x1c0c08, 0x4a1a0e, 0x8a2e12, 0xd85a1a);
    [SerializeField] private ColourRamp iron = Ramp(0x14141a, 0x2c2e36, 0x484c56, 0x6c707a);
    [Tooltip("The flame's tones from its edge in: its outer, middle and core colours (the shadow, middle and light of the ramp).")]
    [SerializeField] private ColourRamp flame = Ramp(0x8a2a10, 0xe65c1e, 0xfa9c2c, 0xffe278);
    [Tooltip("The lantern's glass, and the wick's flame inside it as its light colour.")]
    [SerializeField] private ColourRamp glass = Ramp(0x6a3a14, 0xd88a3a, 0xffcc6e, 0xfff2c4);

    public LightForm Form => form;
    public Color32 LightColour => lightColour;
    public float LightIntensity => lightIntensity;
    public float LightRadius => lightRadius;
    public float LightFlicker => lightFlicker;

    /// <inheritdoc />
    public override void Draw(PropDrawing drawing)
    {
        if (form == LightForm.LampPost)
        {
            DrawLampPost(drawing);
        }
        else
        {
            DrawCampfire(drawing);
        }
    }

    /// <summary>
    /// A ring of stones round a bed of coals, logs leaning together over it, charred where they meet, and tongues of
    /// flame in three layers, each nearer the camera than the last, each layer's pixels flickering to the next layer's
    /// tone; the light shines from the flame's middle.
    /// </summary>
    private void DrawCampfire(PropDrawing drawing)
    {
        int stoneId = drawing.AddMaterial(new ArtMaterial(stone, pattern: ArtPattern.Speckle, patternScale: 0.1f), true);
        int coalsId = drawing.AddMaterial(new ArtMaterial(coals, pattern: ArtPattern.Speckle, patternScale: 0.3f, glow: 1f), false);
        int woodId = drawing.AddMaterial(new ArtMaterial(wood, pattern: ArtPattern.Bark, patternScale: 24f), false);
        int charredId = drawing.AddMaterial(new ArtMaterial(charred), false);
        int outerId = drawing.AddMaterial(new ArtMaterial(Flat(flame.Shadow), true, false, glow: 1f, flicker: new[] { flame.Middle }), false);
        int middleId = drawing.AddMaterial(new ArtMaterial(Flat(flame.Middle), true, false, glow: 1f, flicker: new[] { flame.Shadow, flame.Light }), false);
        int coreId = drawing.AddMaterial(new ArtMaterial(Flat(flame.Light), true, false, glow: 1f, flicker: new[] { flame.Middle }), false);
        ArtCanvas canvas = drawing.Canvas;

        float turn = drawing.Range(0f, 2f * Mathf.PI);
        for (int k = 0; k < stones; k++)
        {
            float angle = turn + (k + drawing.Range(-StoneJitter, StoneJitter)) * 2f * Mathf.PI / stones;
            float size = drawing.Range(stoneSize);
            float spin = drawing.Range(0f, Mathf.PI);
            Vector3 ex = new(Mathf.Cos(spin), Mathf.Sin(spin), 0f);
            Vector3 ey = new(-ex.y, ex.x, 0f);
            Vector3 at = new(Mathf.Cos(angle) * ringRadius, Mathf.Sin(angle) * ringRadius, size * StoneSink);
            canvas.Ellipsoid(at, ex * size, ey * size * drawing.Range(0.75f, 0.95f), Vector3.forward * size * drawing.Range(0.65f, 0.85f), stoneId, drawing.NewPart());
        }

        float bed = ringRadius * CoalsShare;
        canvas.Ellipsoid(Vector3.zero, Vector3.right * bed, Vector3.up * bed, Vector3.forward * CoalsHeight, coalsId, drawing.NewPart());

        float logTurn = drawing.Range(0f, 2f * Mathf.PI);
        for (int k = 0; k < logs; k++)
        {
            float angle = logTurn + k * 2f * Mathf.PI / logs;
            Vector3 foot = new(Mathf.Cos(angle) * ringRadius * LogFoot, Mathf.Sin(angle) * ringRadius * LogFoot, logRadius);
            Vector3 top = new(Mathf.Cos(angle) * LogMeet, Mathf.Sin(angle) * LogMeet, logHeight);
            Vector3 middle = Vector3.Lerp(foot, top, 0.5f);
            int part = drawing.NewPart();
            canvas.Capsule(foot, middle, logRadius, logRadius, woodId, part);
            canvas.Capsule(middle, top, logRadius, logRadius * 0.85f, charredId, part);
        }

        int flamePart = drawing.NewPart();
        for (int k = 0; k < tongues; k++)
        {
            float height = k == 0 ? flameHeight.y : drawing.Range(flameHeight);
            float spread = k == 0 ? 0f : FlameSpread;
            Vector3 foot = new(drawing.Range(-spread, spread), drawing.Range(-spread, spread) * 0.5f, FlameFoot);
            Vector3 tip = foot + new Vector3(drawing.Range(-FlameLean, FlameLean), 0f, height);
            float width = flameWidth * (k == 0 ? 1f : drawing.Range(0.7f, 0.9f));
            Tongue(canvas, foot, tip, width, outerId, flamePart, 0f);
            Tongue(canvas, foot, Vector3.Lerp(foot, tip, MiddleShare), width * MiddleShare, middleId, flamePart, -width * PropBaker.PixelsPerCell);
            Tongue(canvas, foot, Vector3.Lerp(foot, tip, CoreShare), width * CoreShare, coreId, flamePart, -2f * width * PropBaker.PixelsPerCell);
        }

        drawing.MarkLight(new Vector3(0f, 0f, FlameFoot + flameHeight.x * 0.5f), 1f);
    }

    /// <summary>
    /// A post of wood on an iron foot, an iron plate, a box of glass between iron corner bars, an iron roof with a knob,
    /// and the wick's flame showing through the glass; the light shines from the lantern's middle.
    /// </summary>
    private void DrawLampPost(PropDrawing drawing)
    {
        int ironId = drawing.AddMaterial(new ArtMaterial(iron), true);
        int woodId = drawing.AddMaterial(new ArtMaterial(wood, pattern: ArtPattern.Bark, patternScale: 24f), true);
        int glassId = drawing.AddMaterial(new ArtMaterial(Flat(glass.Middle), true, false, glow: 1f), false);
        int wickId = drawing.AddMaterial(new ArtMaterial(Flat(glass.Light), true, false, glow: 1f), false);
        int topIron = drawing.AddMaterial(new ArtMaterial(iron), false);
        ArtCanvas canvas = drawing.Canvas;

        canvas.Capsule(Vector3.zero, new Vector3(0f, 0f, FootHeight), FootRadius, postRadius * 1.3f, ironId, drawing.NewPart());
        canvas.Capsule(new Vector3(0f, 0f, FootHeight), new Vector3(0f, 0f, postHeight), postRadius, postRadius, woodId, drawing.NewPart());

        float half = lanternWidth * 0.5f;
        float floor = postHeight + PlateHeight;
        float ceiling = floor + lanternHeight;
        canvas.Ellipsoid(new Vector3(0f, 0f, postHeight), Vector3.right * (half + BarRadius), Vector3.up * (half + BarRadius), Vector3.forward * PlateHeight, topIron, drawing.NewPart());
        Bounds box = new(new Vector3(0f, 0f, (floor + ceiling) * 0.5f), new Vector3(lanternWidth, lanternWidth, lanternHeight));
        canvas.Convex(new[]
        {
            new Vector4(1f, 0f, 0f, half), new Vector4(-1f, 0f, 0f, half), new Vector4(0f, 1f, 0f, half), new Vector4(0f, -1f, 0f, half),
            new Vector4(0f, 0f, 1f, ceiling), new Vector4(0f, 0f, -1f, -floor)
        }, box, glassId, drawing.NewPart());
        int bars = drawing.NewPart();
        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                canvas.Capsule(new Vector3(x * half, y * half, floor), new Vector3(x * half, y * half, ceiling), BarRadius, BarRadius, topIron, bars);
            }
        }

        Vector3 middle = new(0f, 0f, (floor + ceiling) * 0.5f);
        canvas.Sphere(middle, WickRadius, wickId, drawing.NewPart(), 0f, null, 0f, -lanternWidth * PropBaker.PixelsPerCell);

        float eave = half + RoofOverhang;
        Vector3 apex = new(0f, 0f, ceiling + RoofHeight);
        Vector4[] roof = new Vector4[5];
        for (int k = 0; k < 4; k++)
        {
            Vector3 outward = k == 0 ? Vector3.right : k == 1 ? Vector3.left : k == 2 ? Vector3.up : Vector3.down;
            Vector3 n = (outward * RoofHeight + Vector3.forward * eave).normalized;
            roof[k] = new Vector4(n.x, n.y, n.z, Vector3.Dot(n, apex));
        }

        roof[4] = new Vector4(0f, 0f, -1f, -ceiling);
        canvas.Convex(roof, new Bounds(new Vector3(0f, 0f, ceiling + RoofHeight * 0.5f), new Vector3(eave * 2.4f, eave * 2.4f, RoofHeight * 1.2f)), topIron, drawing.NewPart());
        canvas.Sphere(apex, FinialRadius, topIron, drawing.NewPart());

        drawing.MarkLight(middle, 1f);
    }

    /// <summary>
    /// A tongue of flame from <paramref name="foot"/> to <paramref name="tip"/>, a sphere every half pixel, its radius
    /// shrinking from <paramref name="radius"/> to a point, moved by <paramref name="depthOffset"/> pixels in the depth
    /// test only, so a layer drawn nearer shows over the one around it.
    /// </summary>
    private static void Tongue(ArtCanvas canvas, Vector3 foot, Vector3 tip, float radius, int material, int part, float depthOffset)
    {
        int count = Mathf.Max(2, Mathf.CeilToInt((tip - foot).magnitude * canvas.Scale * 2f) + 1);
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            canvas.Sphere(Vector3.Lerp(foot, tip, t), Mathf.Lerp(radius, FlameTip, Mathf.Pow(t, 0.8f)), material, part, 0f, null, 0f, depthOffset);
        }
    }

    /// <summary>
    /// A ramp of one colour, for a flat material drawn in it whatever its tone.
    /// </summary>
    private static ColourRamp Flat(Color32 colour)
    {
        return new ColourRamp(colour, colour, colour, colour);
    }
}
