using UnityEngine;

/// <summary>
/// Water, for an animated material: deep water with slow patches of lighter and darker water, and ripple marks on a
/// jittered grid, each a short light dash, its ends raised a pixel when it is long enough to curve, some with a dark
/// trough under its middle. Every pixel of a dash glints in turn (Docs/TerrainBlending.md, Phase 5): its glint phase,
/// in the height's blue channel, starts at the mark's own random phase and runs along the dash by the sweep, so each
/// glint sweeps across its mark from left to right. No cell field; the base colour is the water colour, and the
/// height's per-chip value is 0.5 everywhere.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Terrain Recipe/Water Ripples", fileName = "Recipe_Water")]
public sealed class WaterRipplesRecipe : TerrainMaterialRecipe
{
    private const int GritSalt = 19;

    [Header("Water")]
    [Tooltip("The deep water, and the material's base colour.")]
    [SerializeField] private Color32 water = new(44, 96, 150, 255);
    [Tooltip("Slow patches of lighter and darker water, in 0-255 steps.")]
    [SerializeField] private Wave[] tintWaves = { new(1, 0, 4f), new(0, 1, 3.5f), new(1, -1, 3f), new(2, 1, 2f) };
    [Tooltip("Per-pixel noise, in 0-255 steps from darkest to lightest.")]
    [SerializeField, Range(0f, 16f)] private float grit = 2f;
    [Header("Ripples")]
    [Tooltip("Ripple spots along each side of a cell, on a jittered grid.")]
    [SerializeField, Range(0.5f, 8f)] private float marksPerCell = 2.25f;
    [Tooltip("How far a spot moves from its grid position, in grid steps.")]
    [SerializeField, Range(0f, 0.5f)] private float jitter = 0.45f;
    [Tooltip("Chance that a spot holds a ripple mark.")]
    [SerializeField, Range(0f, 1f)] private float markChance = 0.8f;
    [Tooltip("Shortest dash, in pixels.")]
    [SerializeField, Range(1, 8)] private int minLength = 2;
    [Tooltip("Longest dash, in pixels.")]
    [SerializeField, Range(1, 8)] private int maxLength = 5;
    [Tooltip("Dashes at least this long raise their end pixels one row, so they curve like a ripple.")]
    [SerializeField, Range(2, 9)] private int curveLength = 4;
    [Tooltip("A ripple mark's dash, which glints.")]
    [SerializeField] private Color32 ripple = new(84, 140, 192, 255);
    [Tooltip("The trough under a mark's middle.")]
    [SerializeField] private Color32 trough = new(34, 80, 132, 255);
    [Tooltip("Chance that a mark has a trough.")]
    [SerializeField, Range(0f, 1f)] private float troughChance = 0.5f;
    [Tooltip("How much of the glint period a glint takes to sweep from a dash's first pixel to its last.")]
    [SerializeField, Range(0f, 0.5f)] private float sweep = 0.12f;

    public override TerrainMaterial.CellMode CellMode => TerrainMaterial.CellMode.None;

    /// <summary>
    /// Draws the tint waves, the ripple spots and each spot's mark (kept or not, length, trough, phase), then the height
    /// waves, and paints the water, the marks and their troughs, with the marks' glint phases.
    /// </summary>
    protected override TerrainTextures Generate(System.Random random, int size)
    {
        double[] tint = TerrainArt.PeriodicNoise(random, tintWaves, size);
        (double X, double Y)[] spots = TerrainArt.JitteredSeeds(random, SeedsAcross(marksPerCell), jitter, size);
        Color32?[] marks = new Color32?[size * size];
        byte[] glints = new byte[size * size];
        int shortest = Mathf.Min(minLength, maxLength);
        int longest = Mathf.Max(minLength, maxLength);
        foreach ((double spotX, double spotY) in spots)
        {
            bool keep = random.NextDouble() < markChance;
            int length = random.Next(shortest, longest + 1);
            bool withTrough = random.NextDouble() < troughChance;
            double phase = random.NextDouble();
            if (keep)
            {
                Stamp(marks, glints, size, (int)spotX, (int)spotY, length, withTrough, phase);
            }
        }

        double[] height = SmoothHeight(random, size);
        Color32[] albedo = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                double pixelGrit = (TerrainArt.Hash01(x, y, GritSalt) - 0.5) * grit;
                albedo[i] = marks[i] ?? TerrainArt.Shade(water, tint[i] + pixelGrit);
            }
        }

        return new TerrainTextures(size, water, CellMode, albedo, null, TerrainArt.HeightPixels(height, null, null, glints));
    }

    /// <summary>
    /// Marks a dash of <paramref name="length"/> pixels centred on (<paramref name="x"/>, <paramref name="y"/>), its
    /// end pixels a row higher when it is long enough to curve, with its glint phases running from
    /// <paramref name="phase"/> along it, and with a trough a row under its inner pixels, wrapping across the texture's
    /// edges. A dash's pixels take over earlier troughs, never the other way round.
    /// </summary>
    private void Stamp(Color32?[] marks, byte[] glints, int size, int x, int y, int length, bool withTrough, double phase)
    {
        int left = x - length / 2;
        bool curved = length >= curveLength;
        for (int k = 0; k < length; k++)
        {
            bool end = k == 0 || k == length - 1;
            int row = TerrainArt.Wrap(curved && end ? y + 1 : y, size);
            int column = TerrainArt.Wrap(left + k, size);
            int pixel = row * size + column;
            marks[pixel] = ripple;
            glints[pixel] = TerrainArt.GlintByte(phase + (length > 1 ? sweep * k / (length - 1) : 0.0));
            int under = TerrainArt.Wrap(y - 1, size) * size + column;
            if (withTrough && !end && glints[under] == 0)
            {
                marks[under] = trough;
            }
        }
    }
}
