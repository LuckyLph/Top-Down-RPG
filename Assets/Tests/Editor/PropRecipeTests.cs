using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for the prop baker (Docs/PropGeneration.md, Testing): each kind and form of recipe bakes the same pixels for
/// the same seed and different ones for different variants; every variant of every recipe asset keeps its drawing
/// inside its frame, stands on its base pixel and, when it blocks, has a footprint centred on its base inside its frame;
/// a bush's flowers only recolour its crown, in one
/// colour a variant; a crown drawing its lower clumps in front shows the lower of two overlapping clumps, one picking
/// from several places spreads its clumps and one tapered grows them from its top to its foot; a log lies across its base with its broken ends showing; a pine stands on a trunk that shows under
/// its tiers; a bare tree's twigs are unlined; a crag blocks only its own foot; a cover lies only where it is enabled;
/// and a recipe refuses names and frames it cannot bake.
/// </summary>
public class PropRecipeTests
{
    private const string RecipesFolder = "Assets/Settings/Props";
    private static readonly Vector2 ClumpSizes = new(0.28f, 0.5f);
    private static readonly Vector2 Shell = new(0.88f, 1.02f);
    private static readonly Vector3 CrownCentre = new(0f, 0f, 2.6f);
    private static readonly Vector3 CrownRadii = new(1.25f, 1.1f, 1.3f);

    private readonly List<Object> created = new();

    /// <summary>
    /// Destroys every recipe instance the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Object instance in created)
        {
            if (instance != null)
            {
                Object.DestroyImmediate(instance);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// Every kind and form of recipe (its <paramref name="setting"/>, an enum or a flag, set to
    /// <paramref name="value"/>), baked twice from the same seed, gives the same pixels; another variant index gives
    /// other pixels.
    /// </summary>
    [TestCase(typeof(TreeRecipe), null, 0)]
    [TestCase(typeof(TreeRecipe), "form", (int)TreeForm.Pine)]
    [TestCase(typeof(TreeRecipe), "form", (int)TreeForm.Bare)]
    [TestCase(typeof(BushRecipe), null, 0)]
    [TestCase(typeof(RockRecipe), null, 0)]
    [TestCase(typeof(RockRecipe), "form", (int)RockForm.Scattered)]
    [TestCase(typeof(RockRecipe), "form", (int)RockForm.Crag)]
    [TestCase(typeof(WoodRecipe), null, 0)]
    [TestCase(typeof(WoodRecipe), "form", (int)WoodForm.Log)]
    [TestCase(typeof(PlantRecipe), "form", (int)PlantForm.Fern)]
    [TestCase(typeof(PlantRecipe), "form", (int)PlantForm.Blades)]
    [TestCase(typeof(PlantRecipe), "form", (int)PlantForm.Mushrooms)]
    public void Bake_SameSeed_GivesTheSamePixels_AndOtherVariantsOthers(System.Type kind, string setting, int value)
    {
        PropRecipe recipe = Track((PropRecipe)ScriptableObject.CreateInstance(kind));
        if (setting != null)
        {
            Set(recipe, setting, value);
        }

        PropVariant first = PropBaker.Bake(recipe, 0);
        PropVariant again = PropBaker.Bake(recipe, 0);
        PropVariant other = PropBaker.Bake(recipe, 1);

        Assert.That(again.Pixels.SequenceEqual(first.Pixels), Is.True);
        Assert.That(again.Footprint, Is.EqualTo(first.Footprint));
        Assert.That(other.Pixels.SequenceEqual(first.Pixels), Is.False);
    }

    /// <summary>
    /// Every variant of every recipe asset bakes without its drawing reaching its frame's edge, with its base pixel
    /// drawn; a blocking one has a footprint around its base, evenly about it so the root snaps onto whole pixels, and
    /// inside its frame.
    /// </summary>
    [Test]
    public void EveryRecipeAsset_KeepsItsVariantsInsideTheirFrame_StandingOnTheirBase()
    {
        List<PropRecipe> recipes = LoadRecipes();
        Assert.That(recipes, Is.Not.Empty);
        foreach (PropRecipe recipe in recipes)
        {
            Assert.That(recipe.CanGenerate(out string problem), Is.True, $"{recipe.name}: {problem}");
            Vector2Int frame = recipe.FrameSize;
            Vector2Int basePixel = recipe.BasePixel;
            for (int i = 0; i < recipe.VariantCount; i++)
            {
                PropVariant variant = PropBaker.Bake(recipe, i);
                string name = $"{recipe.name} {i}";
                Assert.That(variant.AtEdge, Is.False, $"{name} reaches its frame's edge.");
                Assert.That(variant.Pixels[basePixel.y * frame.x + basePixel.x].a, Is.EqualTo(255), $"{name} should stand on its base pixel.");
                if (!recipe.Blocking)
                {
                    continue;
                }

                RectInt footprint = variant.Footprint;
                Assert.That(variant.HasFootprint, Is.True, name);
                Assert.That(footprint.x * 2 + footprint.width, Is.EqualTo(basePixel.x * 2), $"{name}'s footprint should be centred on its base.");
                Assert.That(footprint.y * 2 + footprint.height, Is.EqualTo(basePixel.y * 2), $"{name}'s footprint should be centred on its base.");
                Assert.That(footprint.xMin >= 0 && footprint.yMin >= 0 && footprint.xMax <= frame.x && footprint.yMax <= frame.y, Is.True, $"{name}'s footprint should lie inside its frame.");
            }
        }
    }

    /// <summary>
    /// A bush's flowers only recolour pixels of its crown: every pixel of each variant is the flowerless bush's or one
    /// of at most two new colours (a petal and a heart), and some are.
    /// </summary>
    [Test]
    public void Flowers_RecolourOnlyTheCrown_InOneColourAVariant()
    {
        BushRecipe plain = Track(ScriptableObject.CreateInstance<BushRecipe>());
        BushRecipe flowering = Track(ScriptableObject.CreateInstance<BushRecipe>());
        Set(flowering, "flowers.count", 12);

        for (int i = 0; i < flowering.VariantCount; i++)
        {
            Color32[] without = PropBaker.Bake(plain, i).Pixels;
            Color32[] with = PropBaker.Bake(flowering, i).Pixels;
            HashSet<Color32> leaves = new(without);
            HashSet<Color32> added = new();
            for (int p = 0; p < with.Length; p++)
            {
                if (!with[p].Equals(without[p]))
                {
                    Assert.That(without[p].a, Is.EqualTo(255), $"variant {i}, pixel {p}: a flower outside the crown");
                    Assert.That(leaves.Contains(with[p]), Is.False, $"variant {i}, pixel {p}");
                    added.Add(with[p]);
                }
            }

            Assert.That(added.Count, Is.InRange(1, 2), $"variant {i}");
        }
    }

    /// <summary>
    /// A crown drawing each clump in front of the clumps above it shows the lower clump nearly everywhere two clumps
    /// more than a fifth of a cell apart on screen overlap, far more often than one sorting them by their depth.
    /// </summary>
    [Test]
    public void TiltedCrown_ShowsTheLowerOfTwoOverlappingClumps()
    {
        for (int seed = 0; seed < 4; seed++)
        {
            float sorted = LowerShown(new PropCrown(26, ClumpSizes, 0.45f, Shell), seed);
            float tilted = LowerShown(new PropCrown(26, ClumpSizes, 0.45f, Shell, lowerInFront: 2f), seed);
            Assert.That(tilted, Is.GreaterThan(0.85f), $"seed {seed}");
            Assert.That(tilted, Is.GreaterThan(sorted + 0.3f), $"seed {seed}");
        }
    }

    /// <summary>
    /// A crown picking each clump from several places sets its clumps farther from their nearest neighbours than one
    /// setting them at random.
    /// </summary>
    [Test]
    public void SpreadCrown_SetsItsClumpsFartherApart()
    {
        for (int seed = 0; seed < 4; seed++)
        {
            float random = MeanGap(DrawCrown(new PropCrown(26, ClumpSizes, 0.45f, Shell), seed, out _));
            float spread = MeanGap(DrawCrown(new PropCrown(26, ClumpSizes, 0.45f, Shell, spotChoices: 8), seed, out _));
            Assert.That(spread, Is.GreaterThan(random * 1.15f), $"seed {seed}");
        }
    }

    /// <summary>
    /// A crown tapered all the way sizes its clumps by how high they sit on screen, from the smallest at its top to the
    /// largest at its foot.
    /// </summary>
    [Test]
    public void TaperedCrown_GrowsItsClumpsFromItsTopToItsFoot()
    {
        for (int seed = 0; seed < 4; seed++)
        {
            List<PropClump> clumps = DrawCrown(new PropCrown(26, ClumpSizes, 0.45f, Shell, sizeTaper: 1f), seed, out ArtCanvas canvas);
            List<PropClump> downward = clumps.OrderByDescending(c => canvas.Project(c.Centre).y).ToList();
            for (int i = 1; i < downward.Count; i++)
            {
                Assert.That(downward[i].Radius, Is.GreaterThanOrEqualTo(downward[i - 1].Radius - 0.00001f), $"seed {seed}, clump {i}");
            }

            Assert.That(downward[downward.Count - 1].Radius - downward[0].Radius, Is.GreaterThan((ClumpSizes.y - ClumpSizes.x) * 0.5f), $"seed {seed}");
        }
    }

    /// <summary>
    /// A log lies across its base on the ground, its footprint wider than tall, and its broken ends show in the cut
    /// end's material on some of its variants.
    /// </summary>
    [Test]
    public void Log_LiesAcrossItsBase_WithItsBrokenEndsShowing()
    {
        WoodRecipe recipe = Track(ScriptableObject.CreateInstance<WoodRecipe>());
        Set(recipe, "form", (int)WoodForm.Log);

        int ends = 0;
        for (int i = 0; i < recipe.VariantCount; i++)
        {
            RectInt footprint = PropBaker.Bake(recipe, i).Footprint;
            Assert.That(footprint.width, Is.GreaterThan(footprint.height), $"variant {i}");

            ArtCanvas canvas = PropBaker.Render(recipe, i, out PropDrawing drawing);
            Assert.That(drawing.Materials.Count, Is.EqualTo(2));
            Assert.That(canvas.CountId(0), Is.GreaterThan(0), $"variant {i}'s bark");
            ends += canvas.CountId(1);
        }

        Assert.That(ends, Is.GreaterThan(0));
    }

    /// <summary>
    /// A pine stands on its trunk: the bark shows on its base pixel under the lowest tier, and its needles are drawn as
    /// one part a tier.
    /// </summary>
    [Test]
    public void Pine_StandsOnATrunkThatShows_UnderItsTiers()
    {
        TreeRecipe recipe = Track(ScriptableObject.CreateInstance<TreeRecipe>());
        Set(recipe, "form", (int)TreeForm.Pine);
        Set(recipe, "pine.clearance", 1f);
        Set(recipe, "crownWidth", 2f);
        Set(recipe, "frameSize", new Vector2Int(96, 152));
        Set(recipe, "basePixel", new Vector2Int(48, 12));

        ArtCanvas canvas = PropBaker.Render(recipe, 0, out PropDrawing _);
        Vector2Int basePixel = recipe.BasePixel;
        HashSet<int> tiers = new();
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                if (canvas.IsFilled(x, y) && canvas.MaterialIdAt(x, y) == 1)
                {
                    tiers.Add(canvas.PartIdAt(x, y));
                }
            }
        }

        Assert.That(canvas.MaterialIdAt(basePixel.x, basePixel.y), Is.EqualTo(0), "the trunk's bark on the base pixel");
        Assert.That(tiers.Count, Is.EqualTo(5));
        Assert.That(PropBaker.Bake(recipe, 0).AtEdge, Is.False);
    }

    /// <summary>
    /// A bare tree draws its thick limbs in lined bark and its twigs, thinner than the twig radius, in unlined bark.
    /// </summary>
    [Test]
    public void Bare_LinesItsLimbs_AndNotItsTwigs()
    {
        TreeRecipe recipe = Track(ScriptableObject.CreateInstance<TreeRecipe>());
        Set(recipe, "form", (int)TreeForm.Bare);

        ArtCanvas canvas = PropBaker.Render(recipe, 0, out PropDrawing drawing);

        Assert.That(drawing.Materials[0].Outlined, Is.True);
        Assert.That(drawing.Materials[1].Outlined, Is.False);
        Assert.That(canvas.CountId(0), Is.GreaterThan(0), "lined limbs");
        Assert.That(canvas.CountId(1), Is.GreaterThan(0), "unlined twigs");
    }

    /// <summary>
    /// A crag is taller than wide, and its footprint is its own foot's: narrower than the drawing, which its smaller
    /// spire beside it widens without blocking.
    /// </summary>
    [Test]
    public void Crag_BlocksOnlyItsOwnFoot()
    {
        RockRecipe recipe = Track(ScriptableObject.CreateInstance<RockRecipe>());
        Set(recipe, "form", (int)RockForm.Crag);
        Set(recipe, "width", 1.1f);
        Set(recipe, "height", 2.9f);
        Set(recipe, "frameSize", new Vector2Int(56, 104));
        Set(recipe, "basePixel", new Vector2Int(28, 12));

        for (int i = 0; i < recipe.VariantCount; i++)
        {
            PropVariant variant = PropBaker.Bake(recipe, i);
            Assert.That(variant.AtEdge, Is.False, $"variant {i}");
            Assert.That(variant.Drawn.height, Is.GreaterThan(variant.Drawn.width * 2), $"variant {i}");
            Assert.That(variant.Footprint.width, Is.LessThan(variant.Drawn.width * 0.75f), $"variant {i}");
        }
    }

    /// <summary>
    /// A rock's cover shows its colours on the rock when enabled, and none of them when not.
    /// </summary>
    [Test]
    public void Cover_ShowsOnlyWhenEnabled()
    {
        RockRecipe covered = Track(ScriptableObject.CreateInstance<RockRecipe>());
        RockRecipe bare = Track(ScriptableObject.CreateInstance<RockRecipe>());
        Set(bare, "cover.enabled", 0);
        PropBaker.Render(covered, 0, out PropDrawing drawing);
        ColourRamp cover = drawing.Materials[0].Ramp;
        HashSet<Color32> colours = new() { cover.Shadow, cover.Middle, cover.Light };

        int withCover = 0, without = 0;
        for (int i = 0; i < covered.VariantCount; i++)
        {
            withCover += PropBaker.Bake(covered, i).Pixels.Count(colours.Contains);
            without += PropBaker.Bake(bare, i).Pixels.Count(colours.Contains);
        }

        Assert.That(withCover, Is.GreaterThan(0));
        Assert.That(without, Is.Zero);
    }

    /// <summary>
    /// A recipe refuses a name that is not letters, digits and underscores starting with a letter, and a frame too
    /// small or with its base pixel outside it.
    /// </summary>
    [Test]
    public void CanGenerate_RefusesBadNamesAndFrames()
    {
        PropRecipe recipe = Track(ScriptableObject.CreateInstance<RockRecipe>());
        Assert.That(recipe.CanGenerate(out _), Is.True);

        SerializedObject settings = new(recipe);
        settings.FindProperty("propName").stringValue = "Rock Grey";
        settings.ApplyModifiedPropertiesWithoutUndo();
        Assert.That(recipe.CanGenerate(out _), Is.False);

        settings.FindProperty("propName").stringValue = "Rock_Grey";
        settings.FindProperty("basePixel").vector2IntValue = new Vector2Int(200, 10);
        settings.ApplyModifiedPropertiesWithoutUndo();
        Assert.That(recipe.CanGenerate(out _), Is.False);

        settings.FindProperty("basePixel").vector2IntValue = new Vector2Int(2, 2);
        settings.FindProperty("frameSize").vector2IntValue = new Vector2Int(6, 6);
        settings.ApplyModifiedPropertiesWithoutUndo();
        Assert.That(recipe.CanGenerate(out _), Is.False);
    }

    /// <summary>
    /// Every prop recipe asset under <see cref="RecipesFolder"/>.
    /// </summary>
    private static List<PropRecipe> LoadRecipes()
    {
        return AssetDatabase.FindAssets("t:PropRecipe", new[] { RecipesFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<PropRecipe>)
            .Where(r => r != null)
            .ToList();
    }

    /// <summary>
    /// Draws <paramref name="crown"/> from <paramref name="seed"/> around a broadleaf's middle on a broadleaf's frame,
    /// returning its clumps.
    /// </summary>
    private static List<PropClump> DrawCrown(PropCrown crown, int seed, out ArtCanvas canvas)
    {
        canvas = new ArtCanvas(104, 136, new Vector2Int(52, 10), PropBaker.PixelsPerCell) { HidesBelowGround = true };
        List<PropClump> clumps = new();
        crown.Draw(new PropDrawing(canvas, new System.Random(seed)), CrownCentre, CrownRadii, 0, 1, clumps);
        return clumps;
    }

    /// <summary>
    /// The share of overlaps where <paramref name="crown"/>, drawn from <paramref name="seed"/>, shows the lower clump:
    /// each pixel showing a clump counts once for every other clump covering it whose middle lies more than a fifth of a
    /// cell higher or lower on screen.
    /// </summary>
    private static float LowerShown(PropCrown crown, int seed)
    {
        List<PropClump> clumps = DrawCrown(crown, seed, out ArtCanvas canvas);
        float apart = PropBaker.PixelsPerCell * 0.2f;
        int overlaps = 0;
        int lower = 0;
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                int shown = canvas.PartIdAt(x, y);
                int index = clumps.FindIndex(c => c.Part == shown);
                if (index < 0)
                {
                    continue;
                }

                float shownHeight = canvas.Project(clumps[index].Centre).y;
                foreach (PropClump other in clumps)
                {
                    Vector3 at = canvas.Project(other.Centre);
                    float dx = x + 0.5f - canvas.Pivot.x - at.x;
                    float dy = y + 0.5f - canvas.Pivot.y - at.y;
                    float radius = other.Radius * canvas.Scale;
                    if (dx * dx + dy * dy > radius * radius || Mathf.Abs(at.y - shownHeight) <= apart)
                    {
                        continue;
                    }

                    overlaps++;
                    if (shownHeight < at.y)
                    {
                        lower++;
                    }
                }
            }
        }

        return lower / (float)overlaps;
    }

    /// <summary>
    /// The mean distance from each of <paramref name="clumps"/>' middles to its nearest neighbour's.
    /// </summary>
    private static float MeanGap(List<PropClump> clumps)
    {
        float sum = 0f;
        foreach (PropClump clump in clumps)
        {
            sum += clumps.Where(other => other.Part != clump.Part).Min(other => (other.Centre - clump.Centre).magnitude);
        }

        return sum / clumps.Count;
    }

    /// <summary>
    /// Sets <paramref name="recipe"/>'s float <paramref name="setting"/> to <paramref name="value"/>.
    /// </summary>
    private static void Set(PropRecipe recipe, string setting, float value)
    {
        SerializedObject settings = new(recipe);
        settings.FindProperty(setting).floatValue = value;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Sets <paramref name="recipe"/>'s <see cref="Vector2Int"/> <paramref name="setting"/> to <paramref name="value"/>.
    /// </summary>
    private static void Set(PropRecipe recipe, string setting, Vector2Int value)
    {
        SerializedObject settings = new(recipe);
        settings.FindProperty(setting).vector2IntValue = value;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Sets <paramref name="recipe"/>'s integer, enum or flag <paramref name="setting"/> to <paramref name="value"/>.
    /// </summary>
    private static void Set(PropRecipe recipe, string setting, int value)
    {
        SerializedObject settings = new(recipe);
        SerializedProperty property = settings.FindProperty(setting);
        switch (property.propertyType)
        {
            case SerializedPropertyType.Boolean:
                property.boolValue = value != 0;
                break;
            case SerializedPropertyType.Enum:
                property.enumValueIndex = value;
                break;
            default:
                property.intValue = value;
                break;
        }

        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Records <paramref name="instance"/> for destruction in <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}
