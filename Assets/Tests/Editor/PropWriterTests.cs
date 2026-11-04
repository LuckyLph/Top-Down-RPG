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
/// secondary texture and a light on each prefab, and a prop that does not glow neither; a moving flame writes its frames
/// in rows, imported as full rectangles, its prefabs drawing with the flames material, and a still one one row on the
/// lit material; every sheet gets its variants' normals as a secondary texture beside its glow, imported linear
/// (Docs/Lighting.md, Phase 11); a sheet that starts or stops glowing moves between the props' folder and the glowing one with its
/// slices; lowering the variant count while a dropped variant's prefab is left, or changing whether it blocks once its
/// prefabs exist, is refused. A blocking prop's <c>Visuals</c> carry a Shadow Caster 2D over its footprint, off, casting
/// onto the ground's sorting layers, and its light props' lights cast hard shadows at full, while a walk-through prop and
/// a light prop carry no caster (Docs/Lighting.md, Phase 7). A waystone writes one prefab switching between its two
/// states, its light asleep (Docs/KnownGaps.md, Phase 3).
/// </summary>
public class PropWriterTests
{
    private const string Folder = "Assets/Tests/TestPropWriter";
    private const string Sprites = Folder + "/Sprites";
    private const string Prefabs = Folder + "/Prefabs";
    private const string Glows = Folder + "/Glow";
    private const string GlowingSprites = Folder + "/GlowingSprites";
    private const string Name = "TestBush";
    private const string LightName = "TestFire";
    private const string StoneName = "TestStone";
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
    /// Generating writes the sheet, the variants in a row, its normals beside it, and a prefab per variant: a variant of
    /// <c>BlockingProp</c> whose <c>Visuals</c>, on its root and its only child, draw the variant's slice pivoted on the
    /// base pixel on the base's rendering layers, which cast the sun's shadow, and whose collider is its footprint around
    /// the root.
    /// </summary>
    [Test]
    public void Generate_WritesTheSheetAndAPrefabPerVariant_InTheUnitsLayout()
    {
        IReadOnlyList<string> written = PropWriter.Generate(recipe, Sprites, Prefabs);

        string sheet = PropWriter.SheetPath(Sprites, Name);
        string normals = PropWriter.NormalsPath(Sprites, Name);
        Assert.That(written, Is.EquivalentTo(new[] { sheet, normals, PrefabPath(0), PrefabPath(1) }));
        Dictionary<string, Sprite> sprites = SpriteSheetFiles.SpritesByName(sheet);
        Assert.That(sprites.Keys, Is.EquivalentTo(new[] { "TestBush_0", "TestBush_1" }));
        SecondarySpriteTexture[] secondary = ((TextureImporter)AssetImporter.GetAtPath(sheet)).secondarySpriteTextures;
        Assert.That(secondary.Length, Is.EqualTo(1), "A prop that does not glow has no glow, only its normals.");
        Assert.That(secondary[0].name, Is.EqualTo(PropWriter.NormalTextureName));
        Assert.That(secondary[0].texture, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Texture2D>(normals)));
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
    /// A light prop's glow goes beside its sheet in the glow folder, laid out as the sheet with every frame of its moving
    /// flame, each pixel's alpha its glow and its colour the one it flickers to, as the sheet's secondary texture
    /// <c>_GlowTex</c>, imported as a plain point-filtered texture; each prefab carries a <c>Light</c> child where its
    /// light stands: a point Light 2D, off, in the recipe's colour, strength and radius on the lit sorting layers, and the
    /// <see cref="PropLight"/> that burns it. Generating again writes nothing.
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
            string normals = PropWriter.NormalsPath(Glows, LightName);
            Assert.That(written, Is.EquivalentTo(new[] { sheet, glow, normals, PropWriter.PrefabPath(Prefabs, campfire, 0), PropWriter.PrefabPath(Prefabs, campfire, 1) }));
            TextureImporter sheetImporter = (TextureImporter)AssetImporter.GetAtPath(sheet);
            Texture2D glowTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(glow);
            Assert.That(sheetImporter.secondarySpriteTextures.Length, Is.EqualTo(2));
            Assert.That(sheetImporter.secondarySpriteTextures[0].name, Is.EqualTo(PropWriter.GlowTextureName));
            Assert.That(sheetImporter.secondarySpriteTextures[0].texture, Is.EqualTo(glowTexture));
            Assert.That(sheetImporter.secondarySpriteTextures[1].name, Is.EqualTo(PropWriter.NormalTextureName));
            Assert.That(sheetImporter.secondarySpriteTextures[1].texture, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Texture2D>(normals)));
            TextureImporter glowImporter = (TextureImporter)AssetImporter.GetAtPath(glow);
            Assert.That(glowImporter.textureType, Is.EqualTo(TextureImporterType.Default));
            Assert.That(glowImporter.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(glowImporter.mipmapEnabled, Is.False);
            Assert.That(glowImporter.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));

            Texture2D file = new(2, 2);
            file.LoadImage(System.IO.File.ReadAllBytes(glow));
            Color32[] glowPixels = file.GetPixels32();
            int frames = LightRecipe.FlameFrames;
            Assert.That(new Vector2Int(file.width, file.height), Is.EqualTo(new Vector2Int(96, 48 * frames)));
            Object.DestroyImmediate(file);
            for (int i = 0; i < 2; i++)
            {
                PropVariant variant = PropBaker.Bake(campfire, i);
                for (int f = 0; f < frames; f++)
                {
                    for (int y = 0; y < 48; y++)
                    {
                        for (int x = 0; x < 48; x++)
                        {
                            Assert.That(glowPixels[(f * 48 + y) * 96 + i * 48 + x], Is.EqualTo(variant.FrameGlow[f][y * 48 + x]), $"Variant {i}'s glow in frame {f} at ({x}, {y}).");
                        }
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
                Assert.That(light.shadowsEnabled, Is.True, "A prop light casts shadows.");
                Assert.That(light.shadowIntensity, Is.EqualTo(1f), "Its shadows take all its light.");
                Assert.That(light.shadowSoftness, Is.Zero, "Its shadows have hard edges.");
                Assert.That(Visuals(prefab).GetComponent<ShadowCaster2D>(), Is.Null, "A light's own prop casts nothing: its light stands inside it.");
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
    /// A waystone writes one prefab for its two states (Docs/KnownGaps.md, Phase 3): a blocking prop drawing its sleeping
    /// slice, its <see cref="WaystoneMarker"/> holding both slices of its one sheet, so switching keeps the sheet's glow
    /// and normals, and its <c>Light</c> child, inactive, a point light that casts no shadows, while its visuals carry a
    /// caster over its footprint for the other lights; no prefab of its second variant. Woken, it shows its awake slice
    /// and lets its light burn; put to sleep again, neither. Generating again writes nothing.
    /// </summary>
    [Test]
    public void Generate_AWaystone_WritesOnePrefabSwitchingBetweenItsStates_ItsLightAsleep()
    {
        MarkerRecipe stone = ScriptableObject.CreateInstance<MarkerRecipe>();
        GameObject instance = null;
        try
        {
            SerializedObject settings = new(stone);
            settings.FindProperty("propName").stringValue = StoneName;
            settings.FindProperty("category").enumValueIndex = (int)PropCategory.Markers;
            settings.FindProperty("variantCount").intValue = 2;
            settings.FindProperty("frameSize").vector2IntValue = new Vector2Int(40, 72);
            settings.FindProperty("basePixel").vector2IntValue = new Vector2Int(20, 12);
            settings.ApplyModifiedPropertiesWithoutUndo();

            IReadOnlyList<string> written = PropWriter.Generate(stone, Sprites, Prefabs, Glows);

            string sheet = PropWriter.SheetPath(Sprites, StoneName);
            string prefabPath = PropWriter.PrefabPath(Prefabs, stone, 0);
            Assert.That(written, Is.EquivalentTo(new[] { sheet, PropWriter.GlowPath(Glows, StoneName), PropWriter.NormalsPath(Glows, StoneName), prefabPath }));
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(PropWriter.PrefabPath(Prefabs, stone, 1)), Is.Null, "Its states share one prefab.");
            Dictionary<string, Sprite> sprites = SpriteSheetFiles.SpritesByName(sheet);
            Sprite dormant = sprites[PropWriter.SliceName(StoneName, MarkerRecipe.DormantVariant)];
            Sprite awake = sprites[PropWriter.SliceName(StoneName, MarkerRecipe.AwakeVariant)];
            Assert.That(awake.texture, Is.EqualTo(dormant.texture), "Both states are slices of one sheet.");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(prefab), Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(PropWriter.BlockingPropPath)));
            WaystoneMarker marker = prefab.GetComponent<WaystoneMarker>();
            Assert.That(marker, Is.Not.Null);
            Assert.That(marker.Visuals, Is.EqualTo(Visuals(prefab)));
            Assert.That(marker.DormantSprite, Is.EqualTo(dormant));
            Assert.That(marker.AwakeSprite, Is.EqualTo(awake));
            Assert.That(Visuals(prefab).sprite, Is.EqualTo(dormant), "It sleeps as written.");
            Transform light = prefab.transform.Find(PropWriter.LightName);
            Assert.That(marker.Glow, Is.EqualTo(light.gameObject));
            Assert.That(light.gameObject.activeSelf, Is.False, "Its light is out until it is discovered.");
            Assert.That(light.GetComponent<Light2D>().shadowsEnabled, Is.False, "Its small light casts no shadows.");
            Assert.That(light.GetComponent<PropLight>().LightSource, Is.EqualTo(light.GetComponent<Light2D>()));
            Assert.That(Visuals(prefab).GetComponent<ShadowCaster2D>(), Is.Not.Null, "The stone stops the other lights.");

            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            WaystoneMarker placed = instance.GetComponent<WaystoneMarker>();
            placed.SetAwake(true);
            Assert.That(placed.IsAwake, Is.True);
            Assert.That(Visuals(instance).sprite, Is.EqualTo(awake));
            Assert.That(placed.Glow.activeSelf, Is.True);
            placed.SetAwake(false);
            Assert.That(Visuals(instance).sprite, Is.EqualTo(dormant));
            Assert.That(placed.Glow.activeSelf, Is.False);

            Assert.That(PropWriter.Generate(stone, Sprites, Prefabs, Glows), Is.Empty);
        }
        finally
        {
            if (instance != null)
            {
                Object.DestroyImmediate(instance);
            }

            Object.DestroyImmediate(stone);
        }
    }

    /// <summary>
    /// A moving flame's sheet holds each variant's frames in rows over the first, which alone is sliced, imported as full
    /// rectangles so a frame reaching past the first's outline draws, and its prefabs draw with the flames material,
    /// stepping through as many rows; set still, the same recipe writes one row of tight sprites drawn with the lit
    /// material, its prefabs and slices kept (Docs/Lighting.md, Phase 6).
    /// </summary>
    [Test]
    public void Generate_AMovingFlame_WritesItsFramesInRows_OnTheFlamesMaterial_AndAStillOneOneRow()
    {
        LightRecipe torch = ScriptableObject.CreateInstance<LightRecipe>();
        try
        {
            SerializedObject settings = new(torch);
            settings.FindProperty("propName").stringValue = LightName;
            settings.FindProperty("category").enumValueIndex = (int)PropCategory.Lights;
            settings.FindProperty("form").enumValueIndex = (int)LightForm.Torch;
            settings.FindProperty("variantCount").intValue = 2;
            settings.FindProperty("frameSize").vector2IntValue = new Vector2Int(24, 64);
            settings.FindProperty("basePixel").vector2IntValue = new Vector2Int(12, 6);
            settings.ApplyModifiedPropertiesWithoutUndo();
            PropWriter.Generate(torch, Sprites, Prefabs, Glows);

            string sheet = PropWriter.SheetPath(Sprites, LightName);
            Dictionary<string, long> ids = SliceIds(sheet);
            Dictionary<string, Sprite> sprites = SpriteSheetFiles.SpritesByName(sheet);
            Assert.That(sprites.Keys, Is.EquivalentTo(new[] { $"{LightName}_0", $"{LightName}_1" }), "Only the first frame is sliced.");
            Assert.That(sprites[$"{LightName}_1"].rect, Is.EqualTo(new Rect(24f, 0f, 24f, 64f)));
            Assert.That(sprites[$"{LightName}_0"].texture.height, Is.EqualTo(64 * LightRecipe.FlameFrames));
            Assert.That(MeshType(sheet), Is.EqualTo(SpriteMeshType.FullRect));
            Texture2D file = new(2, 2);
            file.LoadImage(System.IO.File.ReadAllBytes(sheet));
            Color32[] pixels = file.GetPixels32();
            Object.DestroyImmediate(file);
            PropVariant variant = PropBaker.Bake(torch, 1);
            for (int f = 0; f < LightRecipe.FlameFrames; f++)
            {
                for (int y = 0; y < 64; y++)
                {
                    for (int x = 0; x < 24; x++)
                    {
                        Assert.That(pixels[(f * 64 + y) * 48 + 24 + x], Is.EqualTo(variant.FramePixels[f][y * 24 + x]), $"Frame {f} at ({x}, {y}).");
                    }
                }
            }

            Material flames = AssetDatabase.LoadAssetAtPath<Material>(PropWriter.FlamesMaterialPath);
            Assert.That(flames.GetFloat(PropWriter.FlameFramesName), Is.EqualTo(LightRecipe.FlameFrames));
            Assert.That(flames.shader, Is.EqualTo(Visuals(AssetDatabase.LoadAssetAtPath<GameObject>(PropWriter.PropPath)).sharedMaterial.shader));
            Assert.That(Visuals(AssetDatabase.LoadAssetAtPath<GameObject>(PropWriter.PrefabPath(Prefabs, torch, 0))).sharedMaterial, Is.EqualTo(flames));

            settings.FindProperty("flameMoves").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            PropWriter.Generate(torch, Sprites, Prefabs, Glows);
            Assert.That(SpriteSheetFiles.SpritesByName(sheet)[$"{LightName}_0"].texture.height, Is.EqualTo(64));
            Assert.That(MeshType(sheet), Is.EqualTo(SpriteMeshType.Tight));
            Assert.That(SliceIds(sheet), Is.EqualTo(ids), "Every slice keeps its ID.");
            GameObject still = AssetDatabase.LoadAssetAtPath<GameObject>(PropWriter.PrefabPath(Prefabs, torch, 0));
            Assert.That(Visuals(still).sharedMaterial, Is.EqualTo(Visuals(AssetDatabase.LoadAssetAtPath<GameObject>(PropWriter.PropPath)).sharedMaterial));
        }
        finally
        {
            Object.DestroyImmediate(torch);
        }
    }

    /// <summary>
    /// A sheet that starts glowing (a charred tree given embers, Docs/Lighting.md, Phase 6) moves from the props' sprites
    /// folder to the glowing one, outside the props' atlas, keeping its GUID and its slices' IDs, with its glow beside it;
    /// one that stops glowing moves back without a glow.
    /// </summary>
    [Test]
    public void Generate_ASheetThatStartsOrStopsGlowing_MovesWithItsSlices()
    {
        TreeRecipe tree = Object.Instantiate(AssetDatabase.LoadAssetAtPath<TreeRecipe>("Assets/Settings/Props/Tree_Charred.asset"));
        try
        {
            SerializedObject settings = new(tree);
            settings.FindProperty("propName").stringValue = Name;
            settings.FindProperty("variantCount").intValue = 1;
            settings.FindProperty("embers").FindPropertyRelative("enabled").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            PropWriter.Generate(tree, Sprites, GlowingSprites, Prefabs, Glows, out bool glows);
            string plain = PropWriter.SheetPath(Sprites, Name);
            string glowing = PropWriter.SheetPath(GlowingSprites, Name);
            Assert.That(glows, Is.False);
            string guid = AssetDatabase.AssetPathToGUID(plain);
            Dictionary<string, long> ids = SliceIds(plain);

            settings.FindProperty("embers").FindPropertyRelative("enabled").boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();
            IReadOnlyList<string> written = PropWriter.Generate(tree, Sprites, GlowingSprites, Prefabs, Glows, out glows);
            Assert.That(glows, Is.True);
            Assert.That(written, Does.Contain(glowing).And.Contain(PropWriter.GlowPath(Glows, Name)));
            Assert.That(AssetImporter.GetAtPath(plain), Is.Null, "The sheet left the props' folder.");
            Assert.That(AssetDatabase.AssetPathToGUID(glowing), Is.EqualTo(guid), "The sheet kept its GUID.");
            Assert.That(SliceIds(glowing), Is.EqualTo(ids), "Every slice keeps its ID.");
            Assert.That(((TextureImporter)AssetImporter.GetAtPath(glowing)).secondarySpriteTextures.Length, Is.EqualTo(2));

            settings.FindProperty("embers").FindPropertyRelative("enabled").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            PropWriter.Generate(tree, Sprites, GlowingSprites, Prefabs, Glows, out glows);
            Assert.That(glows, Is.False);
            Assert.That(AssetDatabase.AssetPathToGUID(plain), Is.EqualTo(guid), "The sheet moved back.");
            Assert.That(((TextureImporter)AssetImporter.GetAtPath(plain)).secondarySpriteTextures.Length, Is.EqualTo(1), "Only its normals.");
            Assert.That(AssetDatabase.LoadAssetAtPath<Texture2D>(PropWriter.GlowPath(Glows, Name)), Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(tree);
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
    /// Each blocking variant's <c>Visuals</c> carry a Shadow Caster 2D, off until a burning light wakes it: shaped by
    /// URP's shape editor as the box of its footprint, its collider's, without URP's edge clipping or trim, casting a
    /// light's shadow onto the <c>Default</c> and <c>Terrain</c> sorting layers only and leaving its own sprite lit, with
    /// no shape provider of URP's left on it. Generating again writes nothing.
    /// </summary>
    [Test]
    public void Generate_ABlockingProp_GivesItsVisualsAShadowCasterOverItsFootprint_OffUntilALightWakesIt()
    {
        PropWriter.Generate(recipe, Sprites, Prefabs);

        int[] ground = { SortingLayer.NameToID("Default"), SortingLayer.NameToID("Terrain") };
        for (int i = 0; i < 2; i++)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(i));
            ShadowCaster2D caster = Visuals(prefab).GetComponent<ShadowCaster2D>();
            Assert.That(caster, Is.Not.Null, $"Variant {i} casts.");
            Assert.That(caster.enabled, Is.False, "Off until a burning light wakes it.");
            Assert.That(caster.castingOption, Is.EqualTo(ShadowCaster2D.ShadowCastingOptions.CastShadow));
            BoxCollider2D box = prefab.GetComponent<BoxCollider2D>();
            Vector2 min = box.offset - box.size * 0.5f;
            Vector2 max = box.offset + box.size * 0.5f;
            Assert.That(caster.shapePath, Is.EqualTo(new[] { new Vector3(min.x, min.y), new Vector3(max.x, min.y), new Vector3(max.x, max.y), new Vector3(min.x, max.y) }));
            SerializedObject serialized = new(caster);
            Assert.That(serialized.FindProperty("m_ShadowCastingSource").intValue, Is.EqualTo(1), "Shaped by URP's shape editor.");
            Assert.That(serialized.FindProperty("m_ShapePathHash").intValue, Is.Not.Zero, "It builds its shadow from the shape as it first wakes.");
            Assert.That(serialized.FindProperty("m_ShadowMesh.m_EdgeProcessing").intValue, Is.Zero, "No edge clipping.");
            Assert.That(serialized.FindProperty("m_ShadowMesh.m_TrimEdge").floatValue, Is.Zero, "No trim.");
            Assert.That(serialized.FindProperty("m_ShadowShape2DComponent").objectReferenceValue, Is.Null, "No shape provider of URP's.");
            SerializedProperty layers = serialized.FindProperty("m_ApplyToSortingLayers");
            Assert.That(layers.arraySize, Is.EqualTo(ground.Length));
            for (int l = 0; l < ground.Length; l++)
            {
                Assert.That(layers.GetArrayElementAtIndex(l).intValue, Is.EqualTo(ground[l]), "It shadows the ground's sorting layers only.");
            }
        }

        Assert.That(PropWriter.Generate(recipe, Sprites, Prefabs), Is.Empty);
    }

    /// <summary>
    /// A walk-through recipe makes variants of <c>Prop</c> without a collider or a shadow caster, and once they exist,
    /// making it block is refused.
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
        Assert.That(prefab.GetComponentsInChildren<ShadowCaster2D>(true), Is.Empty, "A walk-through prop stops no light.");

        settings.FindProperty("blocking").boolValue = true;
        settings.ApplyModifiedPropertiesWithoutUndo();
        LogAssert.Expect(LogType.Error, new Regex("not a variant of BlockingProp"));
        Assert.That(PropWriter.Generate(recipe, Sprites, Prefabs), Is.Null);
    }

    /// <summary>
    /// How the sprites of the sheet at <paramref name="sheet"/> are meshed.
    /// </summary>
    private static SpriteMeshType MeshType(string sheet)
    {
        TextureImporterSettings settings = new();
        ((TextureImporter)AssetImporter.GetAtPath(sheet)).ReadTextureSettings(settings);
        return settings.spriteMeshType;
    }

    /// <summary>
    /// The renderer of <paramref name="prefab"/>'s <c>Visuals</c>.
    /// </summary>
    private static SpriteRenderer Visuals(GameObject prefab)
    {
        return prefab.transform.Find(PropWriter.VisualsName).GetComponent<SpriteRenderer>();
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
