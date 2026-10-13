using System;
using UnityEngine;

/// <summary>
/// Clumps of taller blades (weeds) over a ground recipe's textures, for a swaying material (Docs/TerrainBlending.md,
/// Phase 6 notes): the Grass material's weeds over its blade hatch, the DryGrass material's stalks over its straw
/// (Docs/TerrainTypes.md, Phase 3 notes). The ground is the grass recipe's albedo and height repeated over this
/// recipe's repeat; without a grass recipe it is the grass colour with grit. A weed is a clump of blades on a jittered
/// grid of spots, kept at a chance plus density waves: each blade rises from the clump's base, curving to one side, dark
/// at the foot, in one of two leaf greens above (every other blade) and pale at the tip, some ending in a seed head,
/// over a one-pixel shadow under the base. Every pixel of a weed's box (its blades and the sway margin to their right)
/// gets the weight of its row in the height texture's blue channel, from 0 at the base to 255 at the top, which the
/// shader leans by; the box keeps inside one cell column and clear of the other boxes, so the gust is the same across a
/// box and leaning blades never meet another weed. Every pixel of a box, its shadow row included, also gets in the
/// height's green channel its row counted from the row under the base, plus one (1 for the shadow row, 2 for the base,
/// 0 outside every box), and the cell field holds the ground without the weeds, its alpha a box's height in rows above
/// the base (255 outside every box), so the shader can leave out a weed whose foot or top another layer covers or lies
/// outside its own layer, showing the ground there. The base colour is the grass colour.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Terrain Recipe/Weeds", fileName = "Recipe_Weeds")]
public sealed class WeedsRecipe : TerrainMaterialRecipe
{
    private const int GritSalt = 17;

    [Header("Ground")]
    [Tooltip("The grass recipe whose textures this material's ground repeats, so weeds over grass show no seam. The repeat must be a whole number of its repeats.")]
    [SerializeField] private GrassTuftsRecipe grassRecipe;
    [Tooltip("The ground's colour without a grass recipe, and the material's base colour either way.")]
    [SerializeField] private Color32 grass = new(35, 114, 40, 255);
    [Header("Weeds")]
    [Tooltip("Weed spots along each side of a cell, on a jittered grid.")]
    [SerializeField, Range(0.25f, 4f)] private float weedsPerCell = 1.4f;
    [Tooltip("How far a spot moves from its grid position, in grid steps.")]
    [SerializeField, Range(0f, 0.5f)] private float jitter = 0.45f;
    [Tooltip("Chance that a spot holds a weed, before the density waves; a weed whose box would leave its cell column or touch another is left out.")]
    [SerializeField, Range(0f, 1f)] private float weedChance = 0.75f;
    [Tooltip("Waves added to the weed chance, normalised into -1 to 1 and scaled by the density amount, so weeds gather in clumps.")]
    [SerializeField] private Wave[] densityWaves = { new(2, 1, 1f), new(-1, 3, 0.8f), new(3, -2, 0.6f) };
    [Tooltip("How much the density waves raise or lower the weed chance.")]
    [SerializeField, Range(0f, 1f)] private float densityAmount = 0.35f;
    [Tooltip("Fewest and most blades in a weed.")]
    [SerializeField] private Vector2Int blades = new(4, 8);
    [Tooltip("Shortest and tallest blade, in pixels.")]
    [SerializeField] private Vector2Int bladeHeight = new(8, 15);
    [Tooltip("How far apart a weed's blades start, either side of its middle, in pixels.")]
    [SerializeField, Range(0, 4)] private int footSpread = 3;
    [Tooltip("Most pixels a blade curves sideways by its tip.")]
    [SerializeField, Range(0, 4)] private int curve = 3;
    [Tooltip("Pixels of sway margin to the right of a weed's blades, inside its box: at least the material's sway amplitude, so a leaning tip stays in the box.")]
    [SerializeField, Range(0, 4)] private int swayMargin = 3;
    [Tooltip("Chance that a blade ends in a seed head, two pixels of the tip colour side by side.")]
    [SerializeField, Range(0f, 1f)] private float seedHeadChance = 0.3f;
    [Tooltip("A blade's lowest fifth.")]
    [SerializeField] private Color32 stem = new(26, 84, 30, 255);
    [Tooltip("Every other blade above its stem.")]
    [SerializeField] private Color32 leaf = new(52, 128, 46, 255);
    [Tooltip("The other blades above their stem.")]
    [SerializeField] private Color32 leafLight = new(72, 148, 56, 255);
    [Tooltip("A blade's top pixel, and seed heads.")]
    [SerializeField] private Color32 tip = new(132, 158, 70, 255);
    [Tooltip("The shadow under a weed's base.")]
    [SerializeField] private Color32 shadow = new(22, 72, 28, 255);

    public override TerrainMaterial.CellMode CellMode => TerrainMaterial.CellMode.None;

    /// <summary>
    /// Builds the ground from the grass recipe (or the grass colour, the grit and the height waves), draws the density
    /// waves, the weed spots and each spot's weed (kept or not, blades, their feet, heights, curves and seed heads), and
    /// paints the weeds over the ground with their sway weights and rows; the cell field is the ground before the weeds,
    /// with each box's height in rows above its base as alpha.
    /// </summary>
    protected override TerrainTextures Generate(System.Random random, int size)
    {
        Color32[] albedo = new Color32[size * size];
        Color32[] height = new Color32[size * size];
        LayGround(random, size, albedo, height);
        Color32[] ground = (Color32[])albedo.Clone();

        double[] density = TerrainArt.PeriodicNoise(random, densityWaves, size);
        double densityTotal = TerrainArt.TotalAmplitude(densityWaves);
        byte[] weights = new byte[size * size];
        byte[] rows = new byte[size * size];
        byte[] tops = new byte[size * size];
        bool[] taken = new bool[size * size];
        int fewest = Math.Max(1, Math.Min(blades.x, blades.y));
        int most = Math.Max(fewest, Math.Max(blades.x, blades.y));
        int shortest = Math.Max(2, Math.Min(bladeHeight.x, bladeHeight.y));
        int tallest = Math.Max(shortest, Math.Max(bladeHeight.x, bladeHeight.y));
        foreach ((double spotX, double spotY) in TerrainArt.JitteredSeeds(random, SeedsAcross(weedsPerCell), jitter, size))
        {
            int x = (int)spotX;
            int y = (int)spotY;
            double chance = weedChance + (densityTotal > 0.0 ? densityAmount * density[y * size + x] / densityTotal : 0.0);
            bool keep = random.NextDouble() < chance;
            Blade[] weed = new Blade[random.Next(fewest, most + 1)];
            for (int i = 0; i < weed.Length; i++)
            {
                weed[i] = new Blade(x + random.Next(-footSpread, footSpread + 1), random.Next(shortest, tallest + 1), random.Next(-curve, curve + 1), random.NextDouble() < seedHeadChance);
            }

            if (keep)
            {
                StampWeed(albedo, weights, rows, tops, taken, size, y, weed);
            }
        }

        for (int i = 0; i < height.Length; i++)
        {
            height[i].g = rows[i];
            height[i].b = weights[i];
            ground[i].a = rows[i] > 0 ? tops[i] : (byte)255;
        }

        return new TerrainTextures(size, grass, CellMode, albedo, ground, height);
    }

    /// <summary>
    /// Fills <paramref name="albedo"/> and <paramref name="height"/> with the ground: the grass recipe's textures
    /// repeated when it is set and its texture size divides <paramref name="size"/>, else the grass colour with grit
    /// over the smooth height. The blue channel is cleared and the green one is 0.5 until the weeds' rows replace it.
    /// </summary>
    private void LayGround(System.Random random, int size, Color32[] albedo, Color32[] height)
    {
        TerrainTextures ground = grassRecipe != null ? grassRecipe.Generate(grassRecipe.Seed) : null;
        if (ground != null && size % ground.Size == 0)
        {
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int from = (y % ground.Size) * ground.Size + x % ground.Size;
                    albedo[y * size + x] = ground.Albedo[from];
                    height[y * size + x] = new Color32(ground.Height[from].r, 128, 0, 255);
                }
            }

            return;
        }

        if (ground != null)
        {
            Debug.LogWarning($"{name}: the repeat must be a whole number of {grassRecipe.name}'s repeats, so the ground is plain grass.", this);
        }

        double[] smooth = SmoothHeight(random, size);
        Color32[] pixels = TerrainArt.HeightPixels(smooth, null, null);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                albedo[i] = TerrainArt.Shade(grass, (TerrainArt.Hash01(x, y, GritSalt) - 0.5) * 3.0);
                height[i] = pixels[i];
            }
        }
    }

    /// <summary>
    /// Paints <paramref name="weed"/> with its base on row <paramref name="baseRow"/>, when its box (from the shadow
    /// row under it to its tallest tip, and from its leftmost pixel to the sway margin past its rightmost) lies inside one
    /// cell column and clear of every other box; marks the box, a pixel around it included, taken, and gives every
    /// pixel of it the weight of its row, its row from the shadow row (1 there, 2 at the base) and the box's height in
    /// rows above its base.
    /// </summary>
    private void StampWeed(Color32[] albedo, byte[] weights, byte[] rows, byte[] tops, bool[] taken, int size, int baseRow, Blade[] weed)
    {
        int left = int.MaxValue;
        int right = int.MinValue;
        int tallest = 0;
        foreach (Blade blade in weed)
        {
            left = Math.Min(left, Math.Min(blade.Foot, blade.Foot + blade.Curve));
            right = Math.Max(right, Math.Max(blade.Foot, blade.Foot + blade.Curve) + (blade.SeedHead ? 1 : 0));
            tallest = Math.Max(tallest, blade.Height);
        }

        right += swayMargin;
        int column = left / TerrainStyle.PixelsPerCell;
        if (left < 0 || right >= size || right / TerrainStyle.PixelsPerCell != column)
        {
            return;
        }

        for (int dy = -2; dy <= tallest + 1; dy++)
        {
            for (int x = Math.Max(0, left - 1); x <= Math.Min(size - 1, right + 1); x++)
            {
                if (taken[TerrainArt.Wrap(baseRow + dy, size) * size + x])
                {
                    return;
                }
            }
        }

        for (int dy = -1; dy <= tallest; dy++)
        {
            byte weight = (byte)(dy <= 0 ? 0 : Math.Min(255, (int)Math.Round(255.0 * dy / tallest)));
            for (int x = left; x <= right; x++)
            {
                int i = TerrainArt.Wrap(baseRow + dy, size) * size + x;
                taken[i] = true;
                weights[i] = weight;
                rows[i] = (byte)(dy + 2);
                tops[i] = (byte)tallest;
            }
        }

        foreach (Blade blade in weed)
        {
            albedo[TerrainArt.Wrap(baseRow - 1, size) * size + blade.Foot] = shadow;
        }

        for (int b = 0; b < weed.Length; b++)
        {
            Blade blade = weed[b];
            for (int k = 0; k < blade.Height; k++)
            {
                double along = blade.Height > 1 ? (double)k / (blade.Height - 1) : 1.0;
                int x = blade.Foot + (int)Math.Round(blade.Curve * along * along);
                Color32 colour = k == blade.Height - 1 ? tip : along < 0.2 ? stem : b % 2 == 0 ? leaf : leafLight;
                albedo[TerrainArt.Wrap(baseRow + k, size) * size + x] = colour;
                if (k == blade.Height - 1 && blade.SeedHead)
                {
                    albedo[TerrainArt.Wrap(baseRow + k, size) * size + x + 1] = tip;
                }
            }
        }
    }

    /// <summary>
    /// One blade of a weed: the column its foot stands in, its height in pixels, how far its tip curves sideways and
    /// whether it ends in a seed head.
    /// </summary>
    private readonly struct Blade
    {
        public Blade(int foot, int height, int curve, bool seedHead)
        {
            Foot = foot;
            Height = height;
            Curve = curve;
            SeedHead = seedHead;
        }

        public int Foot { get; }
        public int Height { get; }
        public int Curve { get; }
        public bool SeedHead { get; }
    }
}
