using UnityEngine;

/// <summary>
/// Open sea, for an animated material (Docs/TerrainTypes.md, Phase 8 notes): dark blue water in a few flat tones with
/// lighter wave crests that travel across it down the texture, towards a shore to the south, as one moving field
/// wherever the material lies, since the texture repeats over the world. Every pixel a crest can pass over carries a
/// glint phase that grows by one period per crest spacing up the texture, so the band of pixels lit at any moment (the
/// material's glint duty times the spacing, a crest's thickness) moves down by one spacing per glint period. The crests
/// come in families, each with its own phase offset, its own sideways bend (smooth noise along x that pushes a crest up
/// or down, so it runs wavy rather than straight) and its own smooth mask (2D noise; where a family's mask is highest
/// and above the crest level, its crests pass). A crest is drawn only across its family's patches, which change slowly
/// down the texture, so each crest grows and shrinks as it travels and gives way to another family's crests further
/// on; its middle, where the mask is highest, runs ahead towards the shore by up to the arch, so the crest curves like
/// a wave rolling in, and its ends, where the mask is within the fade of the crest level, break up into scattered
/// pixels. Between crests the pixels keep their tone. A few pixels off the crests sparkle. No cell field;
/// the base colour is the middle tone, the glint colour the crests' and the sparkle colour the sparkles', and the
/// height's per-chip value is 0.5 everywhere.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Terrain Recipe/Ocean Waves", fileName = "Recipe_Ocean")]
public sealed class OceanWavesRecipe : TerrainMaterialRecipe
{
    private const int FadeSalt = 43;

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
    [Header("Waves")]
    [Tooltip("Crests of one family along the texture's height: a family's crests are the texture's height over this apart, and travel that far in one glint period. A crest is as thick as the material's glint duty times that spacing.")]
    [SerializeField, Range(1, 64)] private int crestsAcross = 16;
    [Tooltip("Families of crests, each with its own phase, bend and patches, so neighbouring crests differ.")]
    [SerializeField, Range(1, 4)] private int families = 3;
    [Tooltip("Waves of each family's sideways bend; only their X counts (whole periods across the texture), so the bend runs along the crests. Amplitudes only count relative to each other.")]
    [SerializeField] private Wave[] bendWaves = { new(3, 0, 1f), new(5, 0, 0.7f), new(9, 0, 0.4f) };
    [Tooltip("How far up or down the bend pushes a crest at most, in pixels.")]
    [SerializeField, Range(0f, 16f)] private float bend = 3f;
    [Tooltip("How far towards the shore a crest's middle runs ahead of its ends, in pixels: the crest is pushed down by this times how far its family's mask rises above the crest level, so each crest arches like a wave rolling in; 0 for none.")]
    [SerializeField, Range(0f, 16f)] private float arch;
    [Tooltip("Waves of each family's mask: where a family's mask is highest and above the crest level, its crests pass. X counts set the length of a crest, Y counts how fast it grows and shrinks as it travels. Amplitudes only count relative to each other. Set for a texture of 16 cells.")]
    [SerializeField] private Wave[] maskWaves = { new(7, 2, 1f), new(11, 3, 0.8f), new(-9, 5, 0.6f), new(13, -2, 0.4f) };
    [Tooltip("A pixel carries crests where its strongest family's mask, normalised into -1 to 1, is above this: higher gives shorter, sparser crests.")]
    [SerializeField, Range(-1f, 1f)] private float crestLevel = 0.15f;
    [Tooltip("How far above the crest level the mask must rise before a pixel surely carries crests: below that a pixel carries them at a chance that grows with the mask, so a crest's ends break up into scattered pixels instead of stopping square; 0 for square ends.")]
    [SerializeField, Range(0f, 1f)] private float fade;
    [Tooltip("The colour a crest lights up with, the material's glint colour.")]
    [SerializeField] private Color32 crest = new(88, 142, 196, 255);
    [Header("Sparkles")]
    [Tooltip("Chance that a pixel off the crests sparkles.")]
    [SerializeField, Range(0f, 0.05f)] private float sparkleChance = 0.002f;
    [Tooltip("The sparkle colour, the material's sparkle colour.")]
    [SerializeField] private Color32 sparkle = new(200, 230, 245, 255);

    public override TerrainMaterial.CellMode CellMode => TerrainMaterial.CellMode.None;

    /// <summary>
    /// Draws the tones, each family's bend and mask, the height, and the sparkles' chances and phases, then gives every
    /// pixel on a crest family's patches that family's travelling glint phase and the others their tone.
    /// </summary>
    protected override TerrainTextures Generate(System.Random random, int size)
    {
        double[] tone = TerrainArt.PeriodicNoise(random, toneWaves, size);
        double darkLevel = TerrainArt.Level(tone, darkerShare);
        double lightLevel = TerrainArt.Level(tone, 1.0 - lighterShare);
        double bendTotal = TerrainArt.TotalAmplitude(bendWaves);
        double maskTotal = TerrainArt.TotalAmplitude(maskWaves);
        double[][] bends = new double[families][];
        double[][] masks = new double[families][];
        for (int f = 0; f < families; f++)
        {
            bends[f] = TerrainArt.PeriodicNoise(random, bendWaves, size);
            masks[f] = TerrainArt.PeriodicNoise(random, maskWaves, size);
        }

        double[] height = SmoothHeight(random, size);
        double spacing = (double)size / crestsAcross;
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
                if (family >= 0)
                {
                    double push = bendTotal > 0.0 ? bend * bends[family][x] / bendTotal : 0.0;
                    push += crestLevel < 1f ? arch * (strongest - crestLevel) / (1.0 - crestLevel) : 0.0;
                    glints[i] = TerrainArt.GlintByte((y + push) / spacing + (double)family / families);
                }
                else if (sparkleRoll < sparkleChance)
                {
                    glints[i] = TerrainArt.SparkleByte(sparklePhase);
                }
            }
        }

        return new TerrainTextures(size, water, CellMode, albedo, null, TerrainArt.HeightPixels(height, null, null, glints), crest, sparkle);
    }
}
