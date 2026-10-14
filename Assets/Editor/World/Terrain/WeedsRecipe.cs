using System;
using UnityEngine;

/// <summary>
/// Clumps of taller blades (weeds) over a ground recipe's textures, for a swaying material (Docs/TerrainBlending.md,
/// Phase 6 notes): the Grass material's weeds over its blade hatch, the DryGrass material's stalks over its straw
/// (Docs/TerrainTypes.md, Phase 3 notes), the TallGrass material's tall blades over its own denser hatch and the Meadow
/// material's weeds and flower clusters over the grass's hatch (Phase 6 notes). The ground is the grass recipe's albedo
/// and height repeated over this recipe's repeat; without a grass recipe it is the grass colour with grit. A weed is a
/// clump of blades on a jittered grid of spots, kept at a chance plus density waves: each blade rises from the clump's
/// base, curving to one side, dark at the foot, in one of two leaf greens above (every other blade) and pale at the
/// tip, some ending in a seed head, over a one-pixel shadow under the base. With flowers (none by default), a kept spot
/// is at a chance a flower cluster instead: short stems each topped by a flower head of one glyph, in one petal and
/// heart colour picked by slow colour waves where the cluster stands, so clusters of one colour gather in drifts. Every
/// pixel of a weed's box (its blades or stems and heads, and the sway margin to their right) gets the weight of its row
/// in the height texture's blue channel, from 0 at the base to 255 at the top (a flower cluster's from its lowest head
/// up, so its heads lean whole), which the shader leans by; the box keeps inside one cell column and clear of the
/// other boxes, so the gust is the same across a box and leaning blades never meet another weed. Every pixel of a box,
/// its shadow row included, also gets in the height's green channel its row counted from the row under the base, plus
/// one (1 for the shadow row, 2 for the base, 0 outside every box), and the cell field holds the ground without the
/// weeds, its alpha a box's height in rows above the base (255 outside every box), so the shader can leave out a weed
/// whose foot or top another layer covers or lies outside its own layer, showing the ground there. The base colour is
/// the grass colour.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Terrain Recipe/Weeds", fileName = "Recipe_Weeds")]
public sealed class WeedsRecipe : TerrainMaterialRecipe
{
    private const int GritSalt = 17;

    /// <summary>
    /// The colours of one kind of flower: its petals and its heart.
    /// </summary>
    [Serializable]
    public struct FlowerColour
    {
        [Tooltip("The petals ('o' in a head glyph).")]
        [SerializeField] private Color32 petal;
        [Tooltip("The heart ('c' in a head glyph).")]
        [SerializeField] private Color32 heart;

        /// <summary>
        /// A flower of <paramref name="petal"/> petals around a <paramref name="heart"/> heart.
        /// </summary>
        public FlowerColour(Color32 petal, Color32 heart)
        {
            this.petal = petal;
            this.heart = heart;
        }

        public Color32 Petal => petal;
        public Color32 Heart => heart;
    }

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
    [Tooltip("A blade's lowest fifth, and a flower stem's.")]
    [SerializeField] private Color32 stem = new(26, 84, 30, 255);
    [Tooltip("Every other blade above its stem, and flower stems.")]
    [SerializeField] private Color32 leaf = new(52, 128, 46, 255);
    [Tooltip("The other blades above their stem.")]
    [SerializeField] private Color32 leafLight = new(72, 148, 56, 255);
    [Tooltip("A blade's top pixel, and seed heads.")]
    [SerializeField] private Color32 tip = new(132, 158, 70, 255);
    [Tooltip("The shadow under a weed's base.")]
    [SerializeField] private Color32 shadow = new(22, 72, 28, 255);
    [Header("Flowers")]
    [Tooltip("Chance that a kept spot holds a flower cluster instead of a weed; 0 for no flowers. Its random draws are made only above 0, so a recipe without flowers is unchanged by them.")]
    [SerializeField, Range(0f, 1f)] private float flowerChance;
    [Tooltip("How much the density waves raise or lower the flower chance, so flower clusters gather in drifts where the weeds are densest and give way to weeds between them.")]
    [SerializeField, Range(0f, 1f)] private float flowerDrift;
    [Tooltip("Fewest and most stems in a flower cluster, each topped by a head.")]
    [SerializeField] private Vector2Int flowerStems = new(2, 5);
    [Tooltip("Shortest and tallest stem under a head, in pixels.")]
    [SerializeField] private Vector2Int stemHeight = new(3, 7);
    [Tooltip("How far apart a cluster's stems start, either side of its middle, in pixels.")]
    [SerializeField, Range(0, 6)] private int stemSpread = 4;
    [Tooltip("Most pixels a stem curves sideways by its head.")]
    [SerializeField, Range(0, 3)] private int stemCurve = 1;
    [Tooltip("Flower heads, top row first: 'o' a petal, 'c' the heart, anything else leaves what is under it. A cluster takes one at random for all its stems; its bottom row sits on the row above a stem's top, centred on it.")]
    [SerializeField, TextArea(2, 4)] private string[] flowerHeads = { ".o.\noco\n.o." };
    [Tooltip("The kinds of flowers. A cluster takes the one the colour waves pick where it stands (or, at the mix chance, any), so clusters of one colour gather in drifts.")]
    [SerializeField] private FlowerColour[] flowerColours = new FlowerColour[0];
    [Tooltip("Slow waves, normalised into -1 to 1, whose middle half (-0.5 to 0.5) is cut into as many even bands as there are flower colours, the first and last reaching on to -1 and 1: they pick a cluster's colour where it stands.")]
    [SerializeField] private Wave[] flowerColourWaves = { new(1, 1, 1f), new(1, -1, 0.8f), new(2, 1, 0.4f) };
    [Tooltip("Chance that a cluster takes a colour at random instead of its drift's.")]
    [SerializeField, Range(0f, 1f)] private float flowerColourMix = 0.15f;

    public override TerrainMaterial.CellMode CellMode => TerrainMaterial.CellMode.None;

    /// <summary>
    /// Builds the ground from the grass recipe (or the grass colour, the grit and the height waves), draws the density
    /// waves (and with flowers the colour waves), the weed spots and each spot's weed (kept or not, blades, their feet,
    /// heights, curves and seed heads, and with flowers whether it is a flower cluster, its stems, head and colour), and
    /// paints the weeds and clusters over the ground with their sway weights and rows; the cell field is the ground
    /// before the weeds, with each box's height in rows above its base as alpha.
    /// </summary>
    protected override TerrainTextures Generate(System.Random random, int size)
    {
        Color32[] albedo = new Color32[size * size];
        Color32[] height = new Color32[size * size];
        LayGround(random, size, albedo, height);
        Color32[] ground = (Color32[])albedo.Clone();

        double[] density = TerrainArt.PeriodicNoise(random, densityWaves, size);
        double densityTotal = TerrainArt.TotalAmplitude(densityWaves);
        bool flowers = flowerChance > 0f && flowerHeads.Length > 0 && flowerColours.Length > 0;
        double[] colourField = flowers ? TerrainArt.PeriodicNoise(random, flowerColourWaves, size) : null;
        double colourTotal = TerrainArt.TotalAmplitude(flowerColourWaves);
        string[][] heads = flowers ? ParseHeads() : null;
        byte[] weights = new byte[size * size];
        byte[] rows = new byte[size * size];
        byte[] tops = new byte[size * size];
        bool[] taken = new bool[size * size];
        int fewest = Math.Max(1, Math.Min(blades.x, blades.y));
        int most = Math.Max(fewest, Math.Max(blades.x, blades.y));
        int shortest = Math.Max(2, Math.Min(bladeHeight.x, bladeHeight.y));
        int tallest = Math.Max(shortest, Math.Max(bladeHeight.x, bladeHeight.y));
        int fewestStems = Math.Max(1, Math.Min(flowerStems.x, flowerStems.y));
        int mostStems = Math.Max(fewestStems, Math.Max(flowerStems.x, flowerStems.y));
        int shortestStem = Math.Max(1, Math.Min(stemHeight.x, stemHeight.y));
        int tallestStem = Math.Max(shortestStem, Math.Max(stemHeight.x, stemHeight.y));
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

            if (flowers)
            {
                double drift = densityTotal > 0.0 ? flowerDrift * density[y * size + x] / densityTotal : 0.0;
                bool isFlower = random.NextDouble() < flowerChance + drift;
                Blade[] stems = new Blade[random.Next(fewestStems, mostStems + 1)];
                for (int i = 0; i < stems.Length; i++)
                {
                    stems[i] = new Blade(x + random.Next(-stemSpread, stemSpread + 1), random.Next(shortestStem, tallestStem + 1), random.Next(-stemCurve, stemCurve + 1), false);
                }

                string[] head = heads[random.Next(heads.Length)];
                bool mixed = random.NextDouble() < flowerColourMix;
                int anyColour = random.Next(flowerColours.Length);
                FlowerColour colour = flowerColours[mixed ? anyColour : ColourBand(colourField[y * size + x], colourTotal)];
                if (keep && isFlower)
                {
                    Stamp(albedo, weights, rows, tops, taken, size, y, stems, head, colour);
                    continue;
                }
            }

            if (keep)
            {
                Stamp(albedo, weights, rows, tops, taken, size, y, weed, null, default);
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
    /// The flower colour whose even band of -0.5 to 0.5 holds the colour waves' <paramref name="value"/> over their
    /// <paramref name="total"/> amplitude (the first band below it, the last above): waves seldom add up to their whole
    /// amplitude, so bands over all of -1 to 1 would leave the first and last colours rare.
    /// </summary>
    private int ColourBand(double value, double total)
    {
        double t = total > 0.0 ? 0.5 + value / total : 0.5;
        return Math.Max(0, Math.Min(flowerColours.Length - 1, (int)(t * flowerColours.Length)));
    }

    /// <summary>
    /// The flower heads as rows, top first, without blank lines at either end.
    /// </summary>
    private string[][] ParseHeads()
    {
        string[][] shapes = new string[flowerHeads.Length][];
        for (int i = 0; i < flowerHeads.Length; i++)
        {
            string[] lines = (flowerHeads[i] ?? string.Empty).Trim('\r', '\n').Split('\n');
            for (int row = 0; row < lines.Length; row++)
            {
                lines[row] = lines[row].TrimEnd('\r');
            }

            shapes[i] = lines;
        }

        return shapes;
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
    /// Paints a weed's <paramref name="weed"/> blades, or with a <paramref name="head"/> a flower cluster's stems each
    /// topped by that head in <paramref name="colour"/>, with its base on row <paramref name="baseRow"/>, when its box
    /// (from the shadow row under it to its highest pixel, and from its leftmost pixel to the sway margin past its
    /// rightmost) lies inside one cell column and clear of every other box; marks the box, a pixel around it included,
    /// taken, and gives every pixel of it the weight of its row, its row from the shadow row (1 there, 2 at the base)
    /// and the box's height in rows above its base. A cluster's weight reaches its top at the lowest head and stays
    /// there above it, so every head leans whole while the stems bend under it.
    /// </summary>
    private void Stamp(Color32[] albedo, byte[] weights, byte[] rows, byte[] tops, bool[] taken, int size, int baseRow, Blade[] weed, string[] head, FlowerColour colour)
    {
        int headWidth = 0;
        if (head != null)
        {
            foreach (string line in head)
            {
                headWidth = Math.Max(headWidth, line.Length);
            }
        }

        int left = int.MaxValue;
        int right = int.MinValue;
        int tallest = 0;
        int rigidFrom = int.MaxValue;
        foreach (Blade blade in weed)
        {
            if (head != null)
            {
                rigidFrom = Math.Min(rigidFrom, blade.Height);
                int headLeft = blade.Foot + blade.Curve - headWidth / 2;
                left = Math.Min(left, Math.Min(Math.Min(blade.Foot, blade.Foot + blade.Curve), headLeft));
                right = Math.Max(right, Math.Max(Math.Max(blade.Foot, blade.Foot + blade.Curve), headLeft + headWidth - 1));
                tallest = Math.Max(tallest, blade.Height + head.Length);
                continue;
            }

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

        int bend = Math.Min(tallest, rigidFrom);
        for (int dy = -1; dy <= tallest; dy++)
        {
            byte weight = (byte)(dy <= 0 ? 0 : Math.Min(255, (int)Math.Round(255.0 * Math.Min(dy, bend) / bend)));
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
                Color32 pixel = head != null ? along < 0.2 ? stem : leaf
                    : k == blade.Height - 1 ? tip : along < 0.2 ? stem : b % 2 == 0 ? leaf : leafLight;
                albedo[TerrainArt.Wrap(baseRow + k, size) * size + x] = pixel;
                if (head == null && k == blade.Height - 1 && blade.SeedHead)
                {
                    albedo[TerrainArt.Wrap(baseRow + k, size) * size + x + 1] = tip;
                }
            }

            if (head != null)
            {
                StampHead(albedo, size, head, headWidth, blade.Foot + blade.Curve - headWidth / 2, baseRow + blade.Height, colour);
            }
        }
    }

    /// <summary>
    /// Paints the petals and heart of <paramref name="head"/> in <paramref name="colour"/> with its bottom row on row
    /// <paramref name="bottom"/> and its left column on <paramref name="left"/>.
    /// </summary>
    private static void StampHead(Color32[] albedo, int size, string[] head, int width, int left, int bottom, FlowerColour colour)
    {
        for (int r = 0; r < head.Length; r++)
        {
            int row = TerrainArt.Wrap(bottom + head.Length - 1 - r, size);
            for (int c = 0; c < head[r].Length && c < width; c++)
            {
                char mark = head[r][c];
                if (mark == 'o' || mark == 'c')
                {
                    albedo[row * size + left + c] = mark == 'o' ? colour.Petal : colour.Heart;
                }
            }
        }
    }

    /// <summary>
    /// One blade of a weed, or one stem of a flower cluster: the column its foot stands in, its height in pixels, how
    /// far its tip curves sideways and whether it ends in a seed head.
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
