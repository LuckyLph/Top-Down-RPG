using UnityEngine;

/// <summary>
/// Open sea, for an animated material (Docs/TerrainTypes.md, Phase 8 notes): dark blue water in a few flat tones, as one
/// field wherever the material lies, since the texture repeats over the world; the waves rolling in are the shader's,
/// drawn from the coast map. A few pixels sparkle, but none inside the crest patches: smooth masks (2D noise), one per
/// family, where a family's mask is highest and above the crest level, its ends, where the mask is within the fade of
/// the level, breaking up into scattered pixels. The crests these patches carried once had glint phases, which the
/// material no longer lights (its glint duty is 0), so they were dropped with the settings that shaped only them
/// (Docs/KnownGaps.md, Phase 6); the random draws of each family's sideways bend are still made, so the masks, height
/// and sparkles drawn after them keep the sea as it was. No cell field; the base colour is the middle tone, the glint
/// colour the crests' and the sparkle colour the sparkles', and the height's per-chip value is 0.5 everywhere.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Terrain Recipe/Ocean Waves", fileName = "Recipe_Ocean")]
public sealed class OceanWavesRecipe : TerrainMaterialRecipe
{
    private const int FadeSalt = 43;

    /// <summary>
    /// The random draws a crest family's sideways bend took, one per wave of Recipe_Ocean's bend, before the crests'
    /// glint phases were dropped (Docs/KnownGaps.md, Phase 6): still drawn, so what is drawn after them stays the same.
    /// </summary>
    private const int BendDraws = 4;

    [Header("Water")]
    [Tooltip("The middle tone, and the material's base colour.")]
    [SerializeField] private Color32 water = new(22, 58, 110, 255);
    [Tooltip("The darker tone, where the tone field is lowest.")]
    [SerializeField] private Color32 darker = new(19, 52, 101, 255);
    [Tooltip("The lighter tone, where the tone field is highest.")]
    [SerializeField] private Color32 lighter = new(25, 64, 119, 255);
    [Tooltip("Waves of the tone field. X and Y are how many times a wave repeats across the texture; amplitudes only count relative to each other. Set for a texture of 16 cells.")]
    [SerializeField] private Wave[] toneWaves = { new(2, 3, 1f), new(3, -2, 0.8f), new(5, 4, 0.5f), new(-4, 7, 0.4f) };
    [Tooltip("Share of the water in the darker tone.")]
    [SerializeField, Range(0f, 1f)] private float darkerShare = 0.3f;
    [Tooltip("Share of the water in the lighter tone.")]
    [SerializeField, Range(0f, 1f)] private float lighterShare = 0.2f;
    [Header("Crest patches")]
    [Tooltip("Families of crest patches, each with its own mask.")]
    [SerializeField, Range(1, 4)] private int families = 3;
    [Tooltip("Waves of each family's mask: where a family's mask is highest and above the crest level, a pixel lies in its crest patches, where no sparkle shows. Amplitudes only count relative to each other. Set for a texture of 16 cells.")]
    [SerializeField] private Wave[] maskWaves = { new(7, 2, 1f), new(11, 3, 0.8f), new(-9, 5, 0.6f), new(13, -2, 0.4f) };
    [Tooltip("A pixel lies in crest patches where its strongest family's mask, normalised into -1 to 1, is above this: higher gives smaller, sparser patches.")]
    [SerializeField, Range(-1f, 1f)] private float crestLevel = 0.15f;
    [Tooltip("How far above the crest level the mask must rise before a pixel surely lies in crest patches: below that it does at a chance that grows with the mask, so a patch's ends break up into scattered pixels instead of stopping square; 0 for square ends.")]
    [SerializeField, Range(0f, 1f)] private float fade;
    [Tooltip("The material's glint colour, which it no longer lights (its glint duty is 0).")]
    [SerializeField] private Color32 crest = new(88, 142, 196, 255);
    [Header("Sparkles")]
    [Tooltip("Chance that a pixel off the crests sparkles.")]
    [SerializeField, Range(0f, 0.05f)] private float sparkleChance = 0.002f;
    [Tooltip("The sparkle colour, the material's sparkle colour.")]
    [SerializeField] private Color32 sparkle = new(200, 230, 245, 255);

    public override TerrainMaterial.CellMode CellMode => TerrainMaterial.CellMode.None;

    /// <summary>
    /// Draws the tones, each family's mask (after its bend's draws), the height, and the sparkles' chances and phases,
    /// then gives every pixel its tone and a sparkle phase to the pixels that sparkle outside the crest patches.
    /// </summary>
    protected override TerrainTextures Generate(System.Random random, int size)
    {
        double[] tone = TerrainArt.PeriodicNoise(random, toneWaves, size);
        double darkLevel = TerrainArt.Level(tone, darkerShare);
        double lightLevel = TerrainArt.Level(tone, 1.0 - lighterShare);
        double maskTotal = TerrainArt.TotalAmplitude(maskWaves);
        double[][] masks = new double[families][];
        for (int f = 0; f < families; f++)
        {
            for (int d = 0; d < BendDraws; d++)
            {
                random.NextDouble();
            }

            masks[f] = TerrainArt.PeriodicNoise(random, maskWaves, size);
        }

        double[] height = SmoothHeight(random, size);
        Color32[] albedo = new Color32[size * size];
        byte[] glints = new byte[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                albedo[i] = tone[i] <= darkLevel ? darker : tone[i] >= lightLevel ? lighter : water;
                int family = -1;
                double strongest = crestLevel;
                for (int f = 0; f < families; f++)
                {
                    double value = maskTotal > 0.0 ? masks[f][i] / maskTotal : 0.0;
                    if (value > strongest)
                    {
                        strongest = value;
                        family = f;
                    }
                }

                if (family >= 0 && fade > 0f && strongest - crestLevel < fade && TerrainArt.Hash01(x, y, FadeSalt) >= (strongest - crestLevel) / fade)
                {
                    family = -1;
                }

                double sparkleRoll = random.NextDouble();
                double sparklePhase = random.NextDouble();
                if (family < 0 && sparkleRoll < sparkleChance)
                {
                    glints[i] = TerrainArt.SparkleByte(sparklePhase);
                }
            }
        }

        return new TerrainTextures(size, water, CellMode, albedo, null, TerrainArt.HeightPixels(height, null, null, glints), crest, sparkle);
    }
}
