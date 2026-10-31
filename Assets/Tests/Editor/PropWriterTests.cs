using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

/// <summary>
/// Tests for the prop writer (Docs/PropGeneration.md, Testing), writing a recipe built in the test into a temporary
/// folder: the sheet holds a slice per variant pivoted on the base pixel, and each variant is a prefab of the base it
/// should be, in the units' layout, casting the sun's shadow unless the recipe casts none, its footprint its collider;
/// generating again writes nothing
/// and, after a change, keeps every prefab's GUID and every slice's ID; a recipe that fades covers each variant's
/// drawing with its prefab's fade, and one that does not covers nothing; a recipe casting no shadow draws on the
/// default rendering layer alone, and one casting it again on the base's; a light prop writes its glow as its sheet's
/// secondary texture and a light on each prefab, and a prop that does not glow neither; lowering the variant count while
/// a dropped variant's prefab is left, or changing whether it blocks once its prefabs exist, is refused.
/// </summary>
public class PropWriterTests
{
    private const string Folder = "Assets/Tests/TestPropWriter";
    private const string Sprites = Folder + "/Sprites";
    private const string Prefabs = Folder + "/Prefabs";
    private const string Glows = Folder + "/Glow";
    private const string Name = "TestBush";
    private const string LightName = "TestFire";
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
    /// Generating writes the sheet, the variants in a row, and a prefab per variant: a variant of <c>BlockingProp</c>
    /// whose <c>Visuals</c>, on its root and its only child, draw the variant's slice pivoted on the base pixel on the
    /// base's rendering layers, which cast the sun's shadow, and whose collider is its footprint around the root.
    /// </summary>
    [Test]
    public void Generate_WritesTheSheetAndAPrefabPerVariant_InTheUnitsLayout()
    {
        IReadOnlyList<string> written = PropWriter.Generate(recipe, Sprites, Prefabs);

        string sheet = PropWriter.SheetPath(Sprites, Name);
        Assert.That(written, Is.EquivalentTo(new[] { sheet, PrefabPath(0), PrefabPath(1) }));
        Dictionary<string, Sprite> sprites = SpriteSheetFiles.SpritesByName(sheet);
        Assert.That(sprites.Keys, Is.EquivalentTo(new[] { "TestBush_0", "TestBush_1" }));
        Assert.That(((TextureImporter)AssetImporter.GetAtPath(sheet)).secondarySpriteTextures, Is.Empty, "A prop that does not glow has no glow.");
        Assert.That(sprites["TestBush_0"].texture.height, Is.EqualTo(52));
        Assert.That(sprites["TestBush_0"].pivot, Is.EqualTo(new Vector2(28f, 10f)));
        Assert.That(sprites["TestBush_0"].pixelsPerUnit, Is.EqualTo(Unit));

        GameObject blocking = AssetDatabase.LoadAssetAtPath<GameObject>(PropWriter.BlockingPropPath);
        uint casters = blocking.transform.Find(PropWriter.VisualsName).GetComponent<SpriteRenderer>().renderingLayerMask;
        for (int i = 0; i < 2; i++)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(i));
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(prefab), Is.EqualTo(blocking));
            Transform visuals = prefab.transform.Find(PropWriter.VisualsName);
            Assert.That(visuals.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(visuals.GetComponent<SpriteRenderer>().sprite, Is.EqualTo(sprites[PropWriter.SliceName(Name, i)]));
            Assert.That(visuals.GetComponent<SpriteRenderer>().renderingLayerMask, Is.EqualTo(casters));
            Assert.That(prefab.transform.childCount, Is.EqualTo(1));
            Assert.That(prefab.GetComponentInChildren<PropLight>(true), Is.Null, "A prop that does not glow has no light.");

            RectInt footprint = PropBaker.Bake(recipe, i).Footprint;
            BoxCollider2D box = prefab.GetComponent<BoxCollider2D>();
            Assert.That(box.size, Is.EqualTo(new Vector2(footprint.width / Unit, footprint.height / Unit)));
            Assert.That(box.offset, Is.EqualTo(Vector2.zero));
        }
    }

    /// <summary>
    /// A light prop's glow goes beside its sheet in the glow folder, laid out as the sheet, each pixel's alpha its glow
    /// and its colour the one it flickers to, as the sheet's secondary texture <c>_GlowTex</c>, imported as a plain
    /// point-filtered texture; each prefab carries a <c>Light</c> child where its light stands: a point Light 2D, off,
    /// in the recipe's colour, strength and radius on the lit sorting layers, and the <see cref="PropLight"/> that burns
    /// it. Generating again writes nothing.
    /// </summary>
    [Test]
    public void Generate_ALightProp_WritesItsGlowAsTheSheetsSecondaryTexture_AndALightOnEachPrefab()
    {
        LightRecipe campfire = ScriptableObject.CreateInstance<LightRecipe>();
        try
        {
            SerializedObject settings = new(campfire);
            settings.FindProperty("propName").stringValue = LightName;
            settings.FindProperty("category").enumValueIndex = (int)PropCategory.Lights;
            settings.FindProperty("variantCount").intValue = 2;
            settings.FindProperty("frameSize").vector2IntValue = new Vector2Int(48, 48);
            settings.FindProperty("basePixel").vector2IntValue = new Vector2Int(24, 14);
            settings.ApplyModifiedPropertiesWithoutUndo();

            IReadOnlyList<string> written = PropWriter.Generate(campfire, Sprites, Prefabs, Glows);

            string sheet = PropWriter.SheetPath(Sprites, LightName);
            string glow = PropWriter.GlowPath(Glows, LightName);
            Assert.That(written, Is.EquivalentTo(new[] { sheet, glow, PropWriter.PrefabPath(Prefabs, campfire, 0), PropWriter.PrefabPath(Prefabs, campfire, 1) }));
            TextureImporter sheetImporter = (TextureImporter)AssetImporter.GetAtPath(sheet);
            Texture2D glowTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(glow);
            Assert.That(sheetImporter.secondarySpriteTextures.Length, Is.EqualTo(1));
            Assert.That(sheetImporter.secondarySpriteTextures[0].name, Is.EqualTo(PropWriter.GlowTextureName));
            Assert.That(sheetImporter.secondarySpriteTextures[0].texture, Is.EqualTo(glowTexture));
            TextureImporter glowImporter = (TextureImporter)AssetImporter.GetAtPath(glow);
            Assert.That(glowImporter.textureType, Is.EqualTo(TextureImporterType.Default));
            Assert.That(glowImporter.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(glowImporter.mipmapEnabled, Is.False);
            Assert.That(glowImporter.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));

            Texture2D file = new(2, 2);
            file.LoadImage(System.IO.File.ReadAllBytes(glow));
            Color32[] glowPixels = file.GetPixels32();
            Assert.That(new Vector2Int(file.width, file.height), Is.EqualTo(new Vector2Int(96, 48)));
            Object.DestroyImmediate(file);
            for (int i = 0; i < 2; i++)
            {
                PropVariant variant = PropBaker.Bake(campfire, i);
                for (int y = 0; y < 48; y++)
                {
                    for (int x = 0; x < 48; x++)
                    {
                        Assert.That(glowPixels[y * 96 + i * 48 + x], Is.EqualTo(variant.Glow[y * 48 + x]), $"Variant {i}'s glow at ({x}, {y}).");
                    }
                }

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PropWriter.PrefabPath(Prefabs, campfire, i));
                Transform child = prefab.transform.Find(PropWriter.LightName);
                Assert.That(child, Is.Not.Null, $"Variant {i} has a light.");
                Assert.That((Vector2)child.localPosition, Is.EqualTo(variant.LightOffset));
                Light2D light = child.GetComponent<Light2D>();
                Assert.That(light.lightType, Is.EqualTo(Light2D.LightType.Point));
                Assert.That(light.enabled, Is.False, "Off until it burns.");
                Assert.That(light.color, Is.EqualTo((Color)campfire.LightColour));
                Assert.That(light.intensity, Is.EqualTo(campfire.LightIntensity));
                Assert.That(light.pointLightOuterRadius, Is.EqualTo(campfire.LightRadius));
                Assert.That(light.blendStyleIndex, Is.EqualTo(0));
                Assert.That(light.shadowsEnabled, Is.False);
                Assert.That(light.targetSortingLayers, Is.EqualTo(new[] { SortingLayer.NameToID("Default"), SortingLayer.NameToID("Terrain"), SortingLayer.NameToID("Entities") }));
                PropLight propLight = child.GetComponent<PropLight>();
                Assert.That(propLight.LightSource, Is.EqualTo(light));
                Assert.That(propLight.Intensity, Is.EqualTo(campfire.LightIntensity));
                Assert.That(propLight.Flicker, Is.EqualTo(campfire.LightFlicker));
            }

            Assert.That(PropWriter.Generate(campfire, Sprites, Prefabs, Glows), Is.Empty);
        }
        finally
        {
            Object.DestroyImmediate(campfire);
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
    /// A recipe casting no shadow, as props lying flat, puts each variant's <c>Visuals</c> on the default rendering
    /// layer alone, so the sun's shadows leave it out; once it casts one again, generating rewrites the prefabs onto the
    /// base's layers.
    /// </summary>
    [Test]
    public void Generate_ARecipeCastingNoShadow_DrawsOnTheDefaultLayerAlone_AndCastingOneOnTheBases()
    {
        SerializedObject settings = new(recipe);
        settings.FindProperty("castsShadow").boolValue = false;
        settings.ApplyModifiedPropertiesWithoutUndo();
        PropWriter.Generate(recipe, Sprites, Prefabs);
        SpriteRenderer visuals = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(0)).transform.Find(PropWriter.VisualsName).GetComponent<SpriteRenderer>();
        Assert.That(visuals.renderingLayerMask, Is.EqualTo(RenderingLayerMask.defaultRenderingLayerMask.value));

        settings.FindProperty("castsShadow").boolValue = true;
        settings.ApplyModifiedPropertiesWithoutUndo();
        Assert.That(PropWriter.Generate(recipe, Sprites, Prefabs), Is.EquivalentTo(new[] { PrefabPath(0), PrefabPath(1) }));
        GameObject blocking = AssetDatabase.LoadAssetAtPath<GameObject>(PropWriter.BlockingPropPath);
        visuals = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(0)).transform.Find(PropWriter.VisualsName).GetComponent<SpriteRenderer>();
        Assert.That(visuals.renderingLayerMask, Is.EqualTo(blocking.transform.Find(PropWriter.VisualsName).GetComponent<SpriteRenderer>().renderingLayerMask));
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
