using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Tests for the prop writer (Docs/PropGeneration.md, Testing), writing a recipe built in the test into a temporary
/// folder: the sheet holds a slice per variant pivoted on the base pixel and one per shadow, and each variant is a
/// prefab of the base it should be, in the units' layout, its footprint its collider; generating again writes nothing
/// and, after a change, keeps every prefab's GUID and every slice's ID; a recipe that fades covers each variant's
/// drawing with its prefab's fade, and one that does not covers nothing; lowering the variant count while a dropped
/// variant's prefab is left, or changing whether it blocks once its prefabs exist, is refused.
/// </summary>
public class PropWriterTests
{
    private const string Folder = "Assets/Tests/TestPropWriter";
    private const string Sprites = Folder + "/Sprites";
    private const string Prefabs = Folder + "/Prefabs";
    private const string Name = "TestBush";
    private const float Unit = PropBaker.PixelsPerCell;

    private BushRecipe recipe;

    /// <summary>
    /// Makes an empty temporary folder and a two-variant bush recipe writing into it.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        DeleteFolder();
        AssetDatabase.CreateFolder("Assets/Tests", "TestPropWriter");
        recipe = ScriptableObject.CreateInstance<BushRecipe>();
        SerializedObject settings = new(recipe);
        settings.FindProperty("propName").stringValue = Name;
        settings.FindProperty("category").enumValueIndex = (int)PropCategory.Bushes;
        settings.FindProperty("variantCount").intValue = 2;
        settings.FindProperty("frameSize").vector2IntValue = new Vector2Int(56, 52);
        settings.FindProperty("basePixel").vector2IntValue = new Vector2Int(28, 10);
        settings.FindProperty("contactHeight").floatValue = 0.3f;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Destroys the recipe and removes the temporary folder with everything written in it.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(recipe);
        DeleteFolder();
    }

    /// <summary>
    /// Generating writes the sheet and a prefab per variant: a variant of <c>BlockingProp</c> whose <c>Visuals</c>, on
    /// its root, draw the variant's slice pivoted on the base pixel, whose <c>Shadow</c> draws its shadow slice, and
    /// whose collider is its footprint around the root.
    /// </summary>
    [Test]
    public void Generate_WritesTheSheetAndAPrefabPerVariant_InTheUnitsLayout()
    {
        IReadOnlyList<string> written = PropWriter.Generate(recipe, Sprites, Prefabs);

        string sheet = PropWriter.SheetPath(Sprites, Name);
        Assert.That(written, Is.EquivalentTo(new[] { sheet, PrefabPath(0), PrefabPath(1) }));
        Dictionary<string, Sprite> sprites = SpriteSheetFiles.SpritesByName(sheet);
        Assert.That(sprites.Keys, Is.EquivalentTo(new[] { "TestBush_0", "TestBush_1", "TestBush_0_Shadow", "TestBush_1_Shadow" }));
        Assert.That(sprites["TestBush_0"].pivot, Is.EqualTo(new Vector2(28f, 10f)));
        Assert.That(sprites["TestBush_0"].pixelsPerUnit, Is.EqualTo(Unit));

        GameObject blocking = AssetDatabase.LoadAssetAtPath<GameObject>(PropWriter.BlockingPropPath);
        for (int i = 0; i < 2; i++)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(i));
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(prefab), Is.EqualTo(blocking));
            Transform visuals = prefab.transform.Find(PropWriter.VisualsName);
            Assert.That(visuals.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(visuals.GetComponent<SpriteRenderer>().sprite, Is.EqualTo(sprites[PropWriter.SliceName(Name, i)]));
            Assert.That(prefab.transform.Find(PropWriter.ShadowName).GetComponent<SpriteRenderer>().sprite, Is.EqualTo(sprites[PropWriter.ShadowSliceName(Name, i)]));

            RectInt footprint = PropBaker.Bake(recipe, i).Footprint;
            BoxCollider2D box = prefab.GetComponent<BoxCollider2D>();
            Assert.That(box.size, Is.EqualTo(new Vector2(footprint.width / Unit, footprint.height / Unit)));
            Assert.That(box.offset, Is.EqualTo(Vector2.zero));
        }
    }

    /// <summary>
    /// Generating again writes nothing; after the seed changes it rewrites the sheet, and every prefab and slice keeps
    /// its GUID and ID.
    /// </summary>
    [Test]
    public void Generate_Again_WritesNothing_AndAfterAChangeKeepsEveryGuidAndSliceId()
    {
        PropWriter.Generate(recipe, Sprites, Prefabs);
        string sheet = PropWriter.SheetPath(Sprites, Name);
        Dictionary<string, string> guids = Guids(sheet);
        Dictionary<string, long> sliceIds = SliceIds(sheet);

        Assert.That(PropWriter.Generate(recipe, Sprites, Prefabs), Is.Empty);

        SerializedObject settings = new(recipe);
        settings.FindProperty("seed").intValue = 99;
        settings.ApplyModifiedPropertiesWithoutUndo();
        IReadOnlyList<string> written = PropWriter.Generate(recipe, Sprites, Prefabs);

        Assert.That(written, Does.Contain(sheet));
        Assert.That(Guids(sheet), Is.EqualTo(guids));
        Assert.That(SliceIds(sheet), Is.EqualTo(sliceIds));
    }

    /// <summary>
    /// A recipe that fades gives each variant's prefab a fade covering the variant's drawing around its root, in units;
    /// once it no longer fades, generating again empties the cover.
    /// </summary>
    [Test]
    public void Generate_AFadingRecipe_CoversEachDrawing_AndAnotherNothing()
    {
        SerializedObject settings = new(recipe);
        settings.FindProperty("fades").boolValue = true;
        settings.ApplyModifiedPropertiesWithoutUndo();
        PropWriter.Generate(recipe, Sprites, Prefabs);

        for (int i = 0; i < 2; i++)
        {
            RectInt drawn = PropBaker.Bake(recipe, i).Drawn;
            PropFade fade = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(i)).GetComponent<PropFade>();
            Assert.That(fade.Fades, Is.True);
            Assert.That(fade.Cover, Is.EqualTo(new Rect((drawn.x - 28) / Unit, (drawn.y - 10) / Unit, drawn.width / Unit, drawn.height / Unit)));
        }

        settings.FindProperty("fades").boolValue = false;
        settings.ApplyModifiedPropertiesWithoutUndo();
        Assert.That(PropWriter.Generate(recipe, Sprites, Prefabs), Is.EquivalentTo(new[] { PrefabPath(0), PrefabPath(1) }));
        Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(0)).GetComponent<PropFade>().Fades, Is.False);
    }

    /// <summary>
    /// Lowering the variant count below a variant whose prefab is left is refused, writing nothing and keeping that
    /// prefab.
    /// </summary>
    [Test]
    public void Generate_WithFewerVariantsThanPrefabsLeft_IsRefused()
    {
        PropWriter.Generate(recipe, Sprites, Prefabs);
        SerializedObject settings = new(recipe);
        settings.FindProperty("variantCount").intValue = 1;
        settings.ApplyModifiedPropertiesWithoutUndo();

        LogAssert.Expect(LogType.Error, new Regex("past the count"));
        Assert.That(PropWriter.Generate(recipe, Sprites, Prefabs), Is.Null);
        Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(1)), Is.Not.Null);
    }

    /// <summary>
    /// A walk-through recipe makes variants of <c>Prop</c> without a collider, and once they exist, making it block is
    /// refused.
    /// </summary>
    [Test]
    public void Generate_AWalkThroughRecipe_MakesPropVariantsWithoutACollider_AndCannotThenBlock()
    {
        SerializedObject settings = new(recipe);
        settings.FindProperty("blocking").boolValue = false;
        settings.ApplyModifiedPropertiesWithoutUndo();
        PropWriter.Generate(recipe, Sprites, Prefabs);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(0));
        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(prefab), Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(PropWriter.PropPath)));
        Assert.That(prefab.GetComponentsInChildren<Collider2D>(true), Is.Empty);

        settings.FindProperty("blocking").boolValue = true;
        settings.ApplyModifiedPropertiesWithoutUndo();
        LogAssert.Expect(LogType.Error, new Regex("not a variant of BlockingProp"));
        Assert.That(PropWriter.Generate(recipe, Sprites, Prefabs), Is.Null);
    }

    /// <summary>
    /// The path of variant <paramref name="index"/>'s prefab in the temporary folder.
    /// </summary>
    private string PrefabPath(int index)
    {
        return PropWriter.PrefabPath(Prefabs, recipe, index);
    }

    /// <summary>
    /// The GUIDs of the sheet at <paramref name="sheet"/> and of every variant's prefab, by path.
    /// </summary>
    private Dictionary<string, string> Guids(string sheet)
    {
        Dictionary<string, string> guids = new() { [sheet] = AssetDatabase.AssetPathToGUID(sheet) };
        for (int i = 0; i < recipe.VariantCount; i++)
        {
            guids[PrefabPath(i)] = AssetDatabase.AssetPathToGUID(PrefabPath(i));
        }

        return guids;
    }

    /// <summary>
    /// The local file IDs of the slices of the sheet at <paramref name="sheet"/>, by name.
    /// </summary>
    private static Dictionary<string, long> SliceIds(string sheet)
    {
        Dictionary<string, long> ids = new();
        foreach (KeyValuePair<string, Sprite> slice in SpriteSheetFiles.SpritesByName(sheet))
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(slice.Value, out string _, out long id);
            ids[slice.Key] = id;
        }

        return ids;
    }

    /// <summary>
    /// Removes the temporary folder when it is there.
    /// </summary>
    private static void DeleteFolder()
    {
        if (AssetDatabase.IsValidFolder(Folder))
        {
            AssetDatabase.DeleteAsset(Folder);
        }
    }
}
