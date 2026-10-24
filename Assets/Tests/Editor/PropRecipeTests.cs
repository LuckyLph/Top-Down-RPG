using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for the prop baker (Docs/PropGeneration.md, Testing): each kind of recipe bakes the same pixels for the same
/// seed and different ones for different variants; every variant of every recipe asset keeps its drawing inside its
/// frame, stands on its base pixel and, when it blocks, has a footprint centred on its base inside its frame; its shadow
/// is an ellipse of the recipe's opacity inside its frame; and a recipe refuses names and frames it cannot bake.
/// </summary>
public class PropRecipeTests
{
    private const string RecipesFolder = "Assets/Settings/Props";

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
    /// Every kind of recipe, baked twice from the same seed, gives the same pixels and shadow; another variant index
    /// gives other pixels.
    /// </summary>
    [TestCase(typeof(TreeRecipe))]
    [TestCase(typeof(BushRecipe))]
    [TestCase(typeof(RockRecipe))]
    [TestCase(typeof(WoodRecipe))]
    public void Bake_SameSeed_GivesTheSamePixels_AndOtherVariantsOthers(System.Type kind)
    {
        PropRecipe recipe = Track((PropRecipe)ScriptableObject.CreateInstance(kind));

        PropVariant first = PropBaker.Bake(recipe, 0);
        PropVariant again = PropBaker.Bake(recipe, 0);
        PropVariant other = PropBaker.Bake(recipe, 1);

        Assert.That(again.Pixels.SequenceEqual(first.Pixels), Is.True);
        Assert.That(again.Shadow.SequenceEqual(first.Shadow), Is.True);
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
    /// A variant's shadow is an ellipse of black at the recipe's opacity, every pixel either that or clear, under the
    /// base pixel's column and away from its frame's edge.
    /// </summary>
    [Test]
    public void Shadow_IsAnEllipseOfTheRecipesOpacity_InsideItsFrame()
    {
        PropRecipe recipe = Track(ScriptableObject.CreateInstance<TreeRecipe>());
        PropVariant variant = PropBaker.Bake(recipe, 0);
        Vector2Int size = PropBaker.ShadowFrameSize(recipe);
        Vector2Int pivot = PropBaker.ShadowBasePixel(recipe);
        byte alpha = (byte)Mathf.RoundToInt(recipe.ShadowOpacity * 255f);

        Assert.That(variant.Shadow.Length, Is.EqualTo(size.x * size.y));
        Assert.That(variant.Shadow[(pivot.y + recipe.ShadowRise) * size.x + pivot.x].a, Is.EqualTo(alpha));
        for (int y = 0; y < size.y; y++)
        {
            for (int x = 0; x < size.x; x++)
            {
                Color32 pixel = variant.Shadow[y * size.x + x];
                Assert.That(pixel.a == 0 || (pixel.a == alpha && pixel.r == 0 && pixel.g == 0 && pixel.b == 0), Is.True, $"pixel {x}, {y}");
                if (x == 0 || y == 0 || x == size.x - 1 || y == size.y - 1)
                {
                    Assert.That(pixel.a, Is.Zero, $"pixel {x}, {y} on the frame's edge");
                }
            }
        }
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
    /// Records <paramref name="instance"/> for destruction in <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}
