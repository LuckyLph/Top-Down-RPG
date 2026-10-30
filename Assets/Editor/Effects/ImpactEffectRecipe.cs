using System;
using UnityEngine;

/// <summary>
/// The spark star impact played where a direct hit lands (Docs/Animation.md, Phase 6): a hot core and rays bursting
/// out, one along the hit, drawn by <see cref="EffectPainter"/> frame by frame from <see cref="Frames"/>, outlined in
/// its darkest colour darkened further so it still reads over a white flash. One variant per damage type, in
/// <see cref="DamageType"/>'s order, each in its palette, and baked facing each of <see cref="DirectionCount"/>
/// directions. Frames are square and pivot on their middle. The elemental types' impacts (Fire, Frost, Lightning and
/// Poison) cast light in their middle colour (Docs/Lighting.md, Phase 2); Physical and True do not.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Effects/Impact Effect Recipe", fileName = "Impact")]
public sealed class ImpactEffectRecipe : EffectRecipe
{
    [Header("Frames")]
    [Tooltip("Each frame's width and height in pixels.")]
    [SerializeField, Range(8, 96)] private int frameSize = 40;
    [Tooltip("How many directions it is baked facing, evenly spaced counterclockwise from east.")]
    [SerializeField, Range(1, 32)] private int directions = 8;
    [Tooltip("How many rays burst out, evenly spaced from the one along the hit; every other one is long.")]
    [SerializeField, Range(2, 16)] private int rays = 6;
    [Tooltip("How much larger than the frames' lengths it is drawn.")]
    [SerializeField, Range(0.25f, 3f)] private float scale = 1f;
    [Tooltip("How dark the outline is, as a share of the darkest colour.")]
    [SerializeField, Range(0f, 1f)] private float outlineShade = 0.55f;
    [SerializeField] private ImpactStarFrame[] frames = DefaultFrames();

    [Header("Palettes")]
    [Tooltip("One palette per damage type, in the damage types' order.")]
    [SerializeField] private ImpactPalette[] palettes = DefaultPalettes();

    public int Rays => rays;
    public float Scale => scale;
    public float OutlineShade => outlineShade;
    public ImpactStarFrame[] Frames => frames;
    public ImpactPalette[] Palettes => palettes;
    public override int FrameWidth => frameSize;
    public override int FrameHeight => frameSize;
    public override int FrameCount => frames.Length;
    public override int VariantCount => palettes.Length;
    public override int DirectionCount => directions;
    public override Vector2 Pivot => new(0.5f, 0.5f);

    /// <summary>
    /// Creates an unsaved recipe of the shipped spark star, for tests and the first asset.
    /// </summary>
    public static ImpactEffectRecipe Create(string name)
    {
        ImpactEffectRecipe recipe = CreateInstance<ImpactEffectRecipe>();
        recipe.SetSheet(name, 0.04f);
        recipe.hideFlags = HideFlags.HideAndDontSave;
        return recipe;
    }

    /// <summary>
    /// Draws frame <paramref name="frame"/> of the palette of damage type <paramref name="variant"/> facing
    /// <paramref name="direction"/>.
    /// </summary>
    public override Color32[] BakeFrame(int variant, int direction, int frame)
    {
        float angle = 2f * Mathf.PI * direction / directions;
        return EffectPainter.Star(this, palettes[variant], new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), frames[frame]);
    }

    /// <summary>
    /// The middle colour of a damage type whose palette casts light; clear for one that does not.
    /// </summary>
    public override Color LightColour(int variant)
    {
        ImpactPalette palette = palettes[variant];
        return palette.CastsLight ? (Color)palette.Get(ImpactShade.Middle) : Color.clear;
    }

    /// <summary>
    /// Whether the recipe can bake: a name, frames, and one palette per damage type in their order.
    /// </summary>
    public override bool CanGenerate(out string problem)
    {
        if (!base.CanGenerate(out problem))
        {
            return false;
        }

        if (frames == null || frames.Length == 0)
        {
            problem = "Add the frames.";
            return false;
        }

        Array types = Enum.GetValues(typeof(DamageType));
        if (palettes == null || palettes.Length != types.Length)
        {
            problem = $"Give one palette per damage type ({types.Length}).";
            return false;
        }

        for (int i = 0; i < palettes.Length; i++)
        {
            if (palettes[i].Type != (DamageType)types.GetValue(i))
            {
                problem = $"Palette {i} should be {types.GetValue(i)}'s.";
                return false;
            }
        }

        problem = null;
        return true;
    }

    /// <summary>
    /// The shipped star's four frames: a core with short rays, rays shooting out with hot middles, their tips
    /// breaking away, then a dot where each tip was.
    /// </summary>
    private static ImpactStarFrame[] DefaultFrames()
    {
        return new[]
        {
            new ImpactStarFrame(2.2f, 2f, 4.5f, 3.5f, 99f, 0f, ImpactShade.Light),
            new ImpactStarFrame(1.4f, 2.5f, 8.5f, 6f, 99f, 3f, ImpactShade.Light),
            new ImpactStarFrame(0f, 0f, 10.5f, 7.5f, 3.5f, 0f, ImpactShade.Middle),
            new ImpactStarFrame(0f, 0f, 11.5f, 8.5f, 0f, 0f, ImpactShade.Dark)
        };
    }

    /// <summary>
    /// The shipped palettes, one per damage type: white-hot cores fading through the type's light, middle and dark
    /// colours (Physical a warm spark), the elemental types casting light.
    /// </summary>
    private static ImpactPalette[] DefaultPalettes()
    {
        return new[]
        {
            new ImpactPalette(DamageType.Physical, new Color32(255, 255, 255, 255), new Color32(255, 244, 186, 255), new Color32(255, 200, 96, 255), new Color32(196, 108, 40, 255)),
            new ImpactPalette(DamageType.Fire, new Color32(255, 255, 214, 255), new Color32(255, 206, 84, 255), new Color32(255, 124, 36, 255), new Color32(184, 48, 24, 255), true),
            new ImpactPalette(DamageType.Frost, new Color32(255, 255, 255, 255), new Color32(206, 244, 255, 255), new Color32(124, 204, 255, 255), new Color32(56, 116, 208, 255), true),
            new ImpactPalette(DamageType.Lightning, new Color32(255, 255, 255, 255), new Color32(255, 250, 164, 255), new Color32(240, 220, 64, 255), new Color32(148, 104, 228, 255), true),
            new ImpactPalette(DamageType.Poison, new Color32(240, 255, 220, 255), new Color32(184, 240, 120, 255), new Color32(112, 200, 72, 255), new Color32(48, 118, 40, 255), true),
            new ImpactPalette(DamageType.True, new Color32(255, 255, 255, 255), new Color32(236, 236, 255, 255), new Color32(200, 200, 236, 255), new Color32(138, 138, 192, 255))
        };
    }
}
