using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Tests for effects and projectiles drawn by hand in Aseprite (Docs/AsepriteCharacters.md, Phase 3), on files written
/// by <see cref="AsepriteFileWriter"/>: an effect's file gives its frames in eight tagged directions or as one row of
/// every frame, a projectile's one frame per direction; every validation rule of the effect and projectile files and
/// recipes reports what is broken, naming the file, tag and frame; the baked impact, death puff and arrow written as
/// files and generated give sheets byte for byte the baked ones and sprite sets equal to the baked ones, and generating
/// again writes nothing; generating after a file is edited keeps the ID of every slice still drawn, and a broken edit
/// writes nothing; saving files regenerates the recipes using them; the inspectors read the files as Generate does.
/// Imported files, recipes and generated sheets live in folders and under names of their own, deleted after each test.
/// </summary>
public class AsepriteEffectTests
{
    private const string Effect = "AsepriteTestEffect";
    private const string Projectile = "AsepriteTestArrow";
    private const string EffectFolder = AsepriteEffectRecipe.ArtRoot + "/AsepriteTest";
    private const string ProjectileFolder = AsepriteProjectileRecipe.ArtRoot + "/AsepriteTest";
    private const string OutsideFolder = "Assets/Tests/AsepriteOutside";
    private const string BakedArrowSheet = "Assets/Sprites/Weapons/Arrow.png";
    private const string BakedArrowDirections = "Assets/Sprites/Weapons/Arrow_Directions.asset";
    private static readonly Vector2Int SmallPivot = new(4, 4);

    private readonly List<string> tempFolders = new();
    private readonly List<string> createdFolders = new();
    private readonly List<Object> created = new();

    /// <summary>
    /// Destroys the objects the test made and deletes its files, imported files, recipes, sheets and sets.
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
        foreach (string folder in tempFolders)
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        tempFolders.Clear();
        foreach (string folder in createdFolders)
        {
            AssetDatabase.DeleteAsset(folder);
        }

        createdFolders.Clear();
        AssetDatabase.DeleteAsset(OutsideFolder);
        foreach (string sheet in new[] { Effect, "AsepriteTestImpact_Spark", "AsepriteTestDeathPuff_Smoke" })
        {
            AssetDatabase.DeleteAsset(EffectSheetWriter.SheetPath(sheet));
            AssetDatabase.DeleteAsset(EffectSheetWriter.SpritesPath(sheet));
        }

        AssetDatabase.DeleteAsset(WeaponSpriteWriter.SheetPath(Projectile));
        AssetDatabase.DeleteAsset(WeaponSpriteWriter.DirectionsPath(Projectile));
    }

    /// <summary>
    /// A directional effect's file gives its eight tags' frames a row each, east on top, and its frame duration; a file
    /// facing one way gives every frame in one row, its tags ignored; a projectile's file gives a frame per direction in
    /// one row, its tags and durations ignored.
    /// </summary>
    [Test]
    public void Read_WellFormedFiles_GiveTheirRows()
    {
        AsepriteClipFrames directional = AsepriteClipReader.ReadEffect(WriteTemp(EffectFile(), "Impact.aseprite"), true, SmallPivot);
        Assert.That(directional.Problems, Is.Empty);
        Assert.That((directional.DirectionCount, directional.FrameCount, directional.FrameSize), Is.EqualTo((8, 2, new Vector2Int(8, 8))));
        Assert.That(directional.FrameSeconds, Is.EqualTo(new[] { 0.04f, 0.04f }));
        Assert.That(directional.ContactFrame, Is.EqualTo(-1));
        for (int direction = 0; direction < CharacterDirections.Count; direction++)
        {
            for (int frame = 0; frame < 2; frame++)
            {
                Assert.That(Pixel(directional, direction, frame, 3, 2), Is.EqualTo(Colour(0, direction, frame)), $"{direction} {frame}");
                Assert.That(Pixel(directional, direction, frame, 0, 0), Is.EqualTo(new Color32(0, 0, 0, 0)), $"{direction} {frame}");
            }
        }

        AsepriteFileWriter tagged = EffectFile(directions: 1, framesIn: d => 3);
        tagged.AddTag("Pop", 0, 0);
        AsepriteClipFrames oneWay = AsepriteClipReader.ReadEffect(WriteTemp(tagged, "Puff.aseprite"), false, SmallPivot);
        Assert.That(oneWay.Problems, Is.Empty);
        Assert.That((oneWay.DirectionCount, oneWay.FrameCount, oneWay.SheetWidth, oneWay.SheetHeight), Is.EqualTo((1, 3, 24, 8)));
        Assert.That(Pixel(oneWay, 0, 2, 3, 2), Is.EqualTo(Colour(0, 0, 2)));

        AsepriteFileWriter arrow = ProjectileFile(milliseconds: d => 100 + d);
        arrow.AddTag("E", 0, 0);
        AsepriteClipFrames directions = AsepriteClipReader.ReadDirections(WriteTemp(arrow, "Arrow.aseprite"), SmallPivot);
        Assert.That(directions.Problems, Is.Empty);
        Assert.That((directions.DirectionCount, directions.FrameCount), Is.EqualTo((1, 8)));
        for (int direction = 0; direction < 8; direction++)
        {
            Assert.That(Pixel(directions, 0, direction, 4, 3), Is.EqualTo(Colour(0, direction, 0)), $"{direction}");
        }
    }

    /// <summary>
    /// Each validation rule of an effect's file reports a broken file, naming the file and, where it has them, the tag
    /// and the frame, and gives no sheet: the rules it shares with a character's clip (direction tags, lengths,
    /// durations between directions, empty frames, drawing past the canvas, layers, the pivot inside the canvas, the
    /// header) and its own (no mark, every frame as long as the first).
    /// </summary>
    [TestCase("marked", "Impact.aseprite, tag E, frame 2 (Aseprite frame 2): marked contact, but an effect has no contact frame.")]
    [TestCase("uneven", "Impact.aseprite, tag E, frame 2 (Aseprite frame 2): 60 ms, but 40 ms on the first frame; every frame of an effect shows as long.")]
    [TestCase("uneven one way", "Impact.aseprite, frame 3: 60 ms, but 40 ms on the first frame; every frame of an effect shows as long.")]
    [TestCase("durations differ", "Impact.aseprite, tag NE, frame 1 (Aseprite frame 3): 60 ms, but 40 ms in tag E.")]
    [TestCase("no tag", "Impact.aseprite: no tag N;")]
    [TestCase("lengths differ", "Impact.aseprite, tag NW: 3 frames, but 2 in tag E.")]
    [TestCase("frame empty", "Impact.aseprite, frame 2: empty.")]
    [TestCase("past the canvas", "Impact.aseprite, frame 1: layer Body draws past the canvas, at (8, 1).")]
    [TestCase("blend mode", "Impact.aseprite, layer Shade: blends by Multiply; only normal layers are drawn.")]
    [TestCase("pivot outside", "Impact.aseprite: the pivot pixel (8, 0) lies outside its 8 x 8 canvas.")]
    [TestCase("not a file", "Impact.aseprite: could not be read as an Aseprite file.")]
    public void ReadEffect_ABrokenFile_ReportsWhereItIsBroken(string broken, string expected)
    {
        bool directional = true;
        Vector2Int pivot = SmallPivot;
        AsepriteFileWriter writer = null;
        switch (broken)
        {
            case "marked":
                writer = EffectFile(marked: (d, f) => f == 1);
                break;
            case "uneven":
                writer = EffectFile(millisecondsOf: (d, f) => f == 1 ? 60 : 40);
                break;
            case "uneven one way":
                directional = false;
                writer = EffectFile(directions: 1, framesIn: d => 4, millisecondsOf: (d, f) => f == 2 ? 60 : 40);
                break;
            case "durations differ":
                writer = EffectFile(millisecondsOf: (d, f) => d == 1 && f == 0 ? 60 : 40);
                break;
            case "no tag":
                writer = EffectFile(tagOf: d => d == 2 ? null : CharacterDirections.Name(d));
                break;
            case "lengths differ":
                writer = EffectFile(framesIn: d => d == 3 ? 3 : 2);
                break;
            case "frame empty":
                directional = false;
                writer = EffectFile(directions: 1, framesIn: d => 3, empty: (d, f) => f == 1);
                break;
            case "past the canvas":
                directional = false;
                writer = EffectFile(directions: 1);
                writer.SetCel(0, 0, new RectInt(6, 1, 3, 2), Enumerable.Repeat(Colour(0, 0, 0), 6).ToArray());
                break;
            case "blend mode":
                writer = EffectFile();
                writer.SetCel(writer.AddLayer("Shade", blendMode: AsepriteFileWriter.BlendMultiply), 0, new RectInt(2, 2, 1, 1), new[] { Colour(0, 1, 1) });
                break;
            case "pivot outside":
                writer = EffectFile();
                pivot = new Vector2Int(8, 0);
                break;
        }

        string path = writer != null ? WriteTemp(writer, "Impact.aseprite") : WriteJunk("Impact.aseprite");

        AsepriteClipFrames frames = AsepriteClipReader.ReadEffect(path, directional, pivot);

        Assert.That(frames.IsValid, Is.False);
        Assert.That(frames.Pixels, Is.Null);
        Assert.That(frames.Problems.Any(p => p.StartsWith(expected, StringComparison.Ordinal)), Is.True, $"{expected}\nnot in:\n{string.Join("\n", frames.Problems)}");
    }

    /// <summary>
    /// Each validation rule of a projectile's file reports a broken file, naming the file and the frame where it has
    /// one, and gives no frames: a frame count other than the directions', a mark, an empty frame, the tip outside the
    /// canvas, a group not blending normally at full opacity.
    /// </summary>
    [TestCase("count", "Arrow.aseprite: 7 frames, but the projectile is drawn in 8 directions, a frame each: frame 1 faces east and each next one 45 degrees further counterclockwise.")]
    [TestCase("marked", "Arrow.aseprite, frame 3: marked contact, but a projectile has no contact frame.")]
    [TestCase("frame empty", "Arrow.aseprite, frame 5: empty.")]
    [TestCase("tip outside", "Arrow.aseprite: the tip pixel (8, 3) lies outside its 8 x 8 canvas.")]
    [TestCase("group opacity", "Arrow.aseprite, group Gear: blends by Normal at opacity 128; a group must blend normally at full opacity.")]
    public void ReadProjectile_ABrokenFile_ReportsWhereItIsBroken(string broken, string expected)
    {
        Vector2Int tip = SmallPivot;
        AsepriteFileWriter writer = broken switch
        {
            "count" => ProjectileFile(count: 7),
            "marked" => ProjectileFile(marked: d => d == 2),
            "frame empty" => ProjectileFile(empty: d => d == 4),
            _ => ProjectileFile()
        };

        if (broken == "tip outside")
        {
            tip = new Vector2Int(8, 3);
        }
        else if (broken == "group opacity")
        {
            writer.SetCel(writer.AddLayer("Belt", group: writer.AddGroup("Gear", opacity: 128)), 0, new RectInt(2, 2, 1, 1), new[] { Colour(0, 1, 1) });
        }

        AsepriteReport report = new();

        AsepriteClipFrames frames = AsepriteProjectileWriter.Read(WriteTemp(writer, "Arrow.aseprite"), 8, tip, report);

        Assert.That(frames, Is.Null);
        Assert.That(report.Problems.Any(p => p.StartsWith(expected, StringComparison.Ordinal)), Is.True, $"{expected}\nnot in:\n{string.Join("\n", report.Problems)}");
    }

    /// <summary>
    /// An effect's variants must match the first: its canvas size, its frames a direction and its frame duration, each
    /// difference named; variants that match give one sheet, a block of rows per variant, the first at the top.
    /// </summary>
    [Test]
    public void ReadEffectVariants_ThatDiffer_ReportEachDifference()
    {
        string[] paths =
        {
            WriteTemp(EffectFile(variant: 0), "Impact_Physical.aseprite"),
            WriteTemp(EffectFile(variant: 1, size: 10), "Impact_Fire.aseprite"),
            WriteTemp(EffectFile(variant: 2, framesIn: d => 3), "Impact_Frost.aseprite"),
            WriteTemp(EffectFile(variant: 3, millisecondsOf: (d, f) => 50), "Impact_Lightning.aseprite")
        };
        List<string> problems = new();

        Assert.That(AsepriteEffectWriter.Read(paths, true, SmallPivot, problems, out AsepriteClipFrames _), Is.Null);

        Assert.That(problems, Is.EqualTo(new[]
        {
            "Impact_Fire.aseprite: a 10 x 10 canvas, but 8 x 8 in Impact_Physical.aseprite; every variant is the same size.",
            "Impact_Frost.aseprite: 3 frames a direction, but 2 in Impact_Physical.aseprite; every variant has as many.",
            "Impact_Lightning.aseprite: frames of 50 ms, but 40 ms in Impact_Physical.aseprite; every variant's frames show as long."
        }));

        problems.Clear();
        Color32[] sheet = AsepriteEffectWriter.Read(new[] { paths[0], WriteTemp(EffectFile(variant: 1), "Impact_Fire.aseprite") }, true, SmallPivot, problems, out AsepriteClipFrames first);
        Assert.That(problems, Is.Empty);
        Assert.That(sheet.Length, Is.EqualTo(16 * 8 * 16));
        Assert.That(sheet[(16 * 8 - 1 - 2) * 16 + 3], Is.EqualTo(Colour(0, 0, 0)), "variant 0, east, frame 0 at the top");
        Assert.That(sheet[(8 * 8 - 1 - 2) * 16 + 3], Is.EqualTo(Colour(1, 0, 0)), "variant 1, east, frame 0 below variant 0's rows");
        Assert.That(first.FrameSeconds[0], Is.EqualTo(0.04f));
    }

    /// <summary>
    /// The effect recipe generates only with a sheet name of letters, digits and underscores, a file per variant
    /// (one per damage type, in their order, each named after the sheet and its type) under Assets/Art/Effects, and a
    /// pivot that is not negative; the projectile recipe only with such a name, its file named after the sheet under
    /// Assets/Art/Weapons, at least one direction and a tip that is not negative.
    /// </summary>
    [Test]
    public void CanGenerate_RefusesRecipesItCannotWrite()
    {
        string[] types = Enum.GetNames(typeof(DamageType));
        Object[] impacts = types.Select((t, i) => Import(EffectFile(variant: i), EffectFolder, $"{Effect}_{t}")).ToArray();
        Object puff = Import(EffectFile(directions: 1), EffectFolder, Effect);
        Object outside = Import(EffectFile(directions: 1), OutsideFolder, Effect);

        AssertRefused(EffectRecipe("Bad Name", AsepriteEffectVariants.One, puff), "the sheet's name must be letters, digits and underscores");
        AssertRefused(EffectRecipe(Effect, AsepriteEffectVariants.One), $"the recipe needs one file, {Effect}.aseprite, but lists 0.");
        AssertRefused(EffectRecipe(Effect, AsepriteEffectVariants.DamageTypes, impacts[0]), "the recipe needs 6 files, one per damage type in their order (Physical, Fire, Frost, Lightning, Poison, True), but lists 1.");
        AssertRefused(EffectRecipe(Effect, AsepriteEffectVariants.DamageTypes, impacts.Reverse().ToArray()), $"file 1 must be {Effect}_Physical.aseprite.");
        AssertRefused(EffectRecipe(Effect, AsepriteEffectVariants.One, impacts[0]), $"file 1 must be {Effect}.aseprite.");
        AssertRefused(EffectRecipe(Effect, AsepriteEffectVariants.One, outside), $"{Effect}.aseprite must be under Assets/Art/Effects");
        AsepriteEffectRecipe negative = Track(AsepriteEffectRecipe.Create(Effect, AsepriteEffectVariants.One, false, new Vector2Int(-1, 2), new[] { puff }));
        AssertRefused(negative, "the pivot pixel cannot be negative.");
        AsepriteEffectRecipe impact = EffectRecipe(Effect, AsepriteEffectVariants.DamageTypes, impacts);
        Assert.That(impact.CanGenerate(out string problem), Is.True, problem);
        Assert.That(EffectRecipe(Effect, AsepriteEffectVariants.One, puff).CanGenerate(out problem), Is.True, problem);
        Assert.That(impact.FilePaths, Is.EqualTo(impacts.Select(AssetDatabase.GetAssetPath)));

        Object arrow = Import(ProjectileFile(), ProjectileFolder, Projectile);
        Object arrowOutside = Import(ProjectileFile(), OutsideFolder, Projectile);
        AssertRefused(ProjectileRecipe("Bad Name", arrow), "the sheet's name must be letters, digits and underscores");
        AssertRefused(ProjectileRecipe(Projectile, null), $"the recipe needs its file, {Projectile}.aseprite.");
        AssertRefused(ProjectileRecipe(Projectile, puff), $"the recipe needs its file, {Projectile}.aseprite.");
        AssertRefused(ProjectileRecipe(Projectile, arrowOutside), $"{Projectile}.aseprite must be under Assets/Art/Weapons");
        AssertRefused(ProjectileRecipe(Projectile, arrow, 0), "the projectile needs at least one direction.");
        AssertRefused(ProjectileRecipe(Projectile, arrow, 8, new Vector2Int(2, -1)), "the tip pixel cannot be negative.");
        AsepriteProjectileRecipe valid = ProjectileRecipe(Projectile, arrow);
        Assert.That(valid.CanGenerate(out problem), Is.True, problem);
        Assert.That(valid.FilePaths, Is.EqualTo(new[] { AssetDatabase.GetAssetPath(arrow) }));

        AsepriteReport refused = AsepriteEffectWriter.Generate(EffectRecipe(Effect, AsepriteEffectVariants.One));
        Assert.That(refused.Problems.Single(), Does.Contain("needs one file"));
        Assert.That(refused.Written, Is.Empty);
        Assert.That(File.Exists(EffectSheetWriter.SheetPath(Effect)), Is.False);
    }

    /// <summary>
    /// The baked impact (a variant per damage type, 8 directions, 4 frames of 40 ms, 40 px) and death puff (one
    /// direction, 10 frames of 70 ms, 48 x 56 px pivoted on the feet), read from their committed sheets and written as
    /// files by <see cref="AsepriteFileWriter"/>, a file per variant under Assets/Art/Effects, give sheets byte for byte
    /// the baked ones and sprite sets equal to the baked ones (counts, frame duration, every sprite's rect, pivot and
    /// pixels per unit); the files get the enforced import settings, and generating again writes nothing.
    /// </summary>
    [TestCase("Impact_Spark")]
    [TestCase("DeathPuff_Smoke")]
    public void Generate_TheBakedEffectAsFiles_GivesTheBakedSheetAndSprites(string baked)
    {
        EffectSprites bakedSprites = AssetDatabase.LoadAssetAtPath<EffectSprites>(EffectSheetWriter.SpritesPath(baked));
        Sprite firstSprite = bakedSprites.Get(0, 0, 0);
        Vector2Int frameSize = Vector2Int.RoundToInt(firstSprite.rect.size);
        Vector2Int pivot = Vector2Int.RoundToInt(firstSprite.pivot);
        bool directional = bakedSprites.DirectionCount == CharacterDirections.Count;
        AsepriteEffectVariants variants = bakedSprites.VariantCount > 1 ? AsepriteEffectVariants.DamageTypes : AsepriteEffectVariants.One;
        Assert.That(variants == AsepriteEffectVariants.One || bakedSprites.VariantCount == Enum.GetValues(typeof(DamageType)).Length, Is.True);
        Assert.That(directional || bakedSprites.DirectionCount == 1, Is.True);
        string sheet = $"AsepriteTest{baked}";
        Color32[] pixels = ReadPng(EffectSheetWriter.SheetPath(baked), out int width);
        int milliseconds = Mathf.RoundToInt(bakedSprites.FrameSeconds * 1000f);
        List<Object> files = new();
        for (int variant = 0; variant < bakedSprites.VariantCount; variant++)
        {
            string name = variants == AsepriteEffectVariants.One ? sheet : $"{sheet}_{(DamageType)variant}";
            int rows = bakedSprites.DirectionCount;
            AsepriteFileWriter writer = AsepriteFileWriter.FromSheetRows(pixels, width, frameSize, variant * rows, rows, bakedSprites.FrameCount, milliseconds, directional ? (Func<int, string>)CharacterDirections.Name : null);
            files.Add(Import(writer, EffectFolder, name));
        }

        Assert.That(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(files[0])).OfType<GameObject>(), Is.Empty, "The enforced import settings make no prefab.");
        AsepriteEffectRecipe recipe = Track(AsepriteEffectRecipe.Create(sheet, variants, directional, pivot, files));

        AsepriteReport report = AsepriteEffectWriter.Generate(recipe);

        Assert.That(report.Problems, Is.Empty);
        Assert.That(report.Written, Is.EqualTo(new[] { EffectSheetWriter.SheetPath(sheet), EffectSheetWriter.SpritesPath(sheet) }));
        Assert.That(File.ReadAllBytes(EffectSheetWriter.SheetPath(sheet)), Is.EqualTo(File.ReadAllBytes(EffectSheetWriter.SheetPath(baked))));
        EffectSprites sprites = AssetDatabase.LoadAssetAtPath<EffectSprites>(EffectSheetWriter.SpritesPath(sheet));
        Assert.That((sprites.VariantCount, sprites.DirectionCount, sprites.FrameCount), Is.EqualTo((bakedSprites.VariantCount, bakedSprites.DirectionCount, bakedSprites.FrameCount)));
        Assert.That(sprites.FrameSeconds, Is.EqualTo(bakedSprites.FrameSeconds));
        for (int variant = 0; variant < sprites.VariantCount; variant++)
        {
            for (int direction = 0; direction < sprites.DirectionCount; direction++)
            {
                for (int frame = 0; frame < sprites.FrameCount; frame++)
                {
                    Sprite sprite = sprites.Get(variant, direction, frame);
                    Sprite bakedSprite = bakedSprites.Get(variant, direction, frame);
                    string where = $"{variant} {direction} {frame}";
                    Assert.That(sprite.name, Is.EqualTo(EffectSheetWriter.SliceName(sheet, variant, direction, frame)), where);
                    Assert.That(sprite.rect, Is.EqualTo(bakedSprite.rect), where);
                    Assert.That(sprite.pivot, Is.EqualTo(bakedSprite.pivot), where);
                    Assert.That(sprite.pixelsPerUnit, Is.EqualTo(bakedSprite.pixelsPerUnit), where);
                }
            }
        }

        AsepriteReport again = AsepriteEffectWriter.Generate(recipe);
        Assert.That(again.Written, Is.Empty);
        Assert.That(again.Problems, Is.Empty);
    }

    /// <summary>
    /// The longbow's baked arrow (32 directions, one 56 px frame each pivoted on the tip at its middle), read from its
    /// committed sheet and written as a file, a frame per direction counterclockwise from east under
    /// Assets/Art/Weapons, gives a sheet byte for byte the baked one and a direction set equal to the baked one (the
    /// count, every sprite's rect, pivot and pixels per unit); generating again writes nothing.
    /// </summary>
    [Test]
    public void Generate_TheBakedArrowAsAFile_GivesTheBakedSheetAndDirections()
    {
        DirectionalSprites baked = AssetDatabase.LoadAssetAtPath<DirectionalSprites>(BakedArrowDirections);
        Sprite firstSprite = baked.Get(0);
        Vector2Int frameSize = Vector2Int.RoundToInt(firstSprite.rect.size);
        Color32[] pixels = ReadPng(BakedArrowSheet, out int width);
        Object file = Import(AsepriteFileWriter.FromSheetRows(pixels, width, frameSize, 0, 1, baked.Count, 100), ProjectileFolder, Projectile);
        AsepriteProjectileRecipe recipe = ProjectileRecipe(Projectile, file, baked.Count, Vector2Int.RoundToInt(firstSprite.pivot));

        AsepriteReport report = AsepriteProjectileWriter.Generate(recipe);

        Assert.That(report.Problems, Is.Empty);
        Assert.That(report.Written, Is.EqualTo(new[] { WeaponSpriteWriter.SheetPath(Projectile), WeaponSpriteWriter.DirectionsPath(Projectile) }));
        Assert.That(File.ReadAllBytes(WeaponSpriteWriter.SheetPath(Projectile)), Is.EqualTo(File.ReadAllBytes(BakedArrowSheet)));
        DirectionalSprites directions = AssetDatabase.LoadAssetAtPath<DirectionalSprites>(WeaponSpriteWriter.DirectionsPath(Projectile));
        Assert.That(baked.Count, Is.EqualTo(32));
        Assert.That(directions.Count, Is.EqualTo(baked.Count));
        for (int direction = 0; direction < baked.Count; direction++)
        {
            Sprite sprite = directions.Get(direction);
            Assert.That(sprite.name, Is.EqualTo(WeaponSpriteWriter.DirectionSliceName(Projectile, direction)), $"{direction}");
            Assert.That(sprite.rect, Is.EqualTo(baked.Get(direction).rect), $"{direction}");
            Assert.That(sprite.pivot, Is.EqualTo(baked.Get(direction).pivot), $"{direction}");
            Assert.That(sprite.pixelsPerUnit, Is.EqualTo(baked.Get(direction).pixelsPerUnit), $"{direction}");
        }

        AsepriteReport again = AsepriteProjectileWriter.Generate(recipe);
        Assert.That(again.Written, Is.Empty);
        Assert.That(again.Problems, Is.Empty);
    }

    /// <summary>
    /// An effect generated from its file, then generated again after a frame is added, a layer is added, the canvas is
    /// grown 2 px on every side (the pivot moved with it) and the frame is removed again, keeps the sprite ID of every
    /// slice still drawn, and its sprite set points at the sheet's slices; a broken edit (an empty frame) writes
    /// nothing. The same for a projectile whose canvas grows and gains a layer.
    /// </summary>
    [Test]
    public void Generate_AfterTheFilesAreEdited_KeepsEverySliceStillDrawn()
    {
        Object file = Import(EffectFile(directions: 1, framesIn: d => 3), EffectFolder, Effect);
        string sheetPath = EffectSheetWriter.SheetPath(Effect);
        AsepriteEffectRecipe recipe = EffectRecipe(Effect, AsepriteEffectVariants.One, file);
        Assert.That(AsepriteEffectWriter.Generate(recipe).Problems, Is.Empty);
        Dictionary<string, long> ids = SpriteIds(sheetPath);
        Assert.That(ids.Count, Is.EqualTo(3));

        Import(EffectFile(directions: 1, framesIn: d => 4), EffectFolder, Effect);
        AssertEffectKeepsIds(AsepriteEffectWriter.Generate(recipe), ids, sheetPath, 4, "frame added");

        Import(EffectFile(directions: 1, framesIn: d => 4, layer: true), EffectFolder, Effect);
        AssertEffectKeepsIds(AsepriteEffectWriter.Generate(recipe), ids, sheetPath, 4, "layer added");

        Import(EffectFile(directions: 1, framesIn: d => 4, layer: true, pad: 2), EffectFolder, Effect);
        AsepriteEffectRecipe grown = Track(AsepriteEffectRecipe.Create(Effect, AsepriteEffectVariants.One, false, SmallPivot + new Vector2Int(2, 2), new[] { file }));
        AssertEffectKeepsIds(AsepriteEffectWriter.Generate(grown), ids, sheetPath, 4, "canvas grown");
        Sprite grownSprite = SpriteSheetFiles.SpritesByName(sheetPath)[EffectSheetWriter.SliceName(Effect, 0, 0, 0)];
        Assert.That(grownSprite.rect.size, Is.EqualTo(new Vector2(12f, 12f)));
        Assert.That(grownSprite.pivot, Is.EqualTo(new Vector2(6f, 6f)));

        Import(EffectFile(directions: 1, framesIn: d => 3, pad: 2), EffectFolder, Effect);
        AssertEffectKeepsIds(AsepriteEffectWriter.Generate(grown), ids, sheetPath, 3, "frame removed");

        byte[] before = File.ReadAllBytes(sheetPath);
        Import(EffectFile(directions: 1, framesIn: d => 3, pad: 2, empty: (d, f) => f == 1), EffectFolder, Effect);
        AsepriteReport broken = AsepriteEffectWriter.Generate(grown);
        Assert.That(broken.Problems, Is.EqualTo(new[] { $"{Effect}.aseprite, frame 2: empty." }));
        Assert.That(broken.Written, Is.Empty);
        Assert.That(File.ReadAllBytes(sheetPath), Is.EqualTo(before));
        Assert.That(SpriteIds(sheetPath).Count, Is.EqualTo(3));

        Object arrow = Import(ProjectileFile(), ProjectileFolder, Projectile);
        string arrowPath = WeaponSpriteWriter.SheetPath(Projectile);
        Assert.That(AsepriteProjectileWriter.Generate(ProjectileRecipe(Projectile, arrow)).Problems, Is.Empty);
        Dictionary<string, long> arrowIds = SpriteIds(arrowPath);
        Assert.That(arrowIds.Count, Is.EqualTo(8));
        Import(ProjectileFile(pad: 2, layer: true), ProjectileFolder, Projectile);
        AsepriteReport moved = AsepriteProjectileWriter.Generate(ProjectileRecipe(Projectile, arrow, 8, SmallPivot + new Vector2Int(2, 2)));
        Assert.That(moved.Problems, Is.Empty);
        Assert.That(moved.Written, Is.EqualTo(new[] { arrowPath }));
        Assert.That(SpriteIds(arrowPath), Is.EqualTo(arrowIds));
        DirectionalSprites directions = AssetDatabase.LoadAssetAtPath<DirectionalSprites>(WeaponSpriteWriter.DirectionsPath(Projectile));
        for (int direction = 0; direction < 8; direction++)
        {
            Sprite sprite = directions.Get(direction);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string _, out long fileId);
            Assert.That(fileId, Is.EqualTo(arrowIds[WeaponSpriteWriter.DirectionSliceName(Projectile, direction)]), $"{direction}");
            Assert.That(sprite.rect.size, Is.EqualTo(new Vector2(12f, 12f)), $"{direction}");
            Assert.That(sprite.pivot, Is.EqualTo(new Vector2(6f, 6f)), $"{direction}");
        }
    }

    /// <summary>
    /// Saving an effect's and a projectile's files in one import queues the recipes using them, once each, and running
    /// the queue regenerates them with the files' new durations and pixels; another effect's recipe, whose file was
    /// not saved, is neither queued nor rewritten.
    /// </summary>
    [Test]
    public void SavingFiles_RegeneratesTheEffectAndProjectileRecipesUsingThem()
    {
        AsepriteRegeneration.Flush();
        Object puff = Import(EffectFile(directions: 1), EffectFolder, Effect);
        Object other = Import(EffectFile(directions: 1), EffectFolder, "AsepriteTestImpact_Spark");
        Object arrow = Import(ProjectileFile(), ProjectileFolder, Projectile);
        string effectRecipePath = $"{EffectFolder}/{Effect}_Recipe.asset";
        string otherRecipePath = $"{EffectFolder}/Other_Recipe.asset";
        string projectileRecipePath = $"{ProjectileFolder}/{Projectile}_Recipe.asset";
        AssetDatabase.CreateAsset(AsepriteEffectRecipe.Create(Effect, AsepriteEffectVariants.One, false, SmallPivot, new[] { puff }), effectRecipePath);
        AssetDatabase.CreateAsset(AsepriteEffectRecipe.Create("AsepriteTestImpact_Spark", AsepriteEffectVariants.One, false, SmallPivot, new[] { other }), otherRecipePath);
        AssetDatabase.CreateAsset(AsepriteProjectileRecipe.Create(Projectile, arrow, 8, SmallPivot), projectileRecipePath);
        foreach (string path in new[] { effectRecipePath, otherRecipePath, projectileRecipePath })
        {
            Assert.That(((IAsepriteRecipe)AssetDatabase.LoadAssetAtPath<ScriptableObject>(path)).Generate().Problems, Is.Empty, path);
        }

        AsepriteRegeneration.Flush();
        string otherSheet = EffectSheetWriter.SheetPath("AsepriteTestImpact_Spark");
        DateTime otherWritten = File.GetLastWriteTimeUtc(otherSheet);
        byte[] arrowBefore = File.ReadAllBytes(WeaponSpriteWriter.SheetPath(Projectile));

        AssetDatabase.StartAssetEditing();
        try
        {
            EffectFile(directions: 1, millisecondsOf: (d, f) => 90).Write(AssetDatabase.GetAssetPath(puff));
            ProjectileFile(layer: true).Write(AssetDatabase.GetAssetPath(arrow));
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(puff), ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(arrow), ImportAssetOptions.ForceUpdate);
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        string[] expected = new[] { effectRecipePath, projectileRecipePath }.OrderBy(p => p, StringComparer.Ordinal).ToArray();
        Assert.That(AsepriteRegeneration.Queued, Is.EquivalentTo(expected));
        Assert.That(AsepriteRegeneration.Flush(), Is.EqualTo(expected));
        Assert.That(AsepriteRegeneration.Queued, Is.Empty);
        Assert.That(AssetDatabase.LoadAssetAtPath<EffectSprites>(EffectSheetWriter.SpritesPath(Effect)).FrameSeconds, Is.EqualTo(0.09f));
        Assert.That(File.ReadAllBytes(WeaponSpriteWriter.SheetPath(Projectile)), Is.Not.EqualTo(arrowBefore));
        Assert.That(File.GetLastWriteTimeUtc(otherSheet), Is.EqualTo(otherWritten));
    }

    /// <summary>
    /// The effect inspector reads the picked variant as Generate does, and again when its file changes (then a
    /// problem); the projectile inspector reads its file's directions, and names the file it needs without one.
    /// </summary>
    [Test]
    public void ThePreviews_ReadTheFilesAsGenerateDoes()
    {
        Object puff = Import(EffectFile(directions: 1, framesIn: d => 3), EffectFolder, Effect);
        string path = AssetDatabase.GetAssetPath(puff);
        AsepriteEffectRecipe recipe = EffectRecipe(Effect, AsepriteEffectVariants.One, puff);
        AsepriteEffectRecipeEditor inspector = Track((AsepriteEffectRecipeEditor)Editor.CreateEditor(recipe));

        inspector.RebuildIfChanged(recipe);
        Assert.That(inspector.PreviewProblem, Is.Null);
        Assert.That((inspector.PreviewFrames.FrameCount, inspector.PreviewFrames.FrameSeconds[0]), Is.EqualTo((3, 0.04f)));

        EffectFile(directions: 1, framesIn: d => 3, marked: (d, f) => f == 0).Write(path);
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(1));
        inspector.RebuildIfChanged(recipe);
        StringAssert.Contains("marked contact, but an effect has no contact frame", inspector.PreviewProblem);

        Object arrow = Import(ProjectileFile(), ProjectileFolder, Projectile);
        AsepriteProjectileRecipe projectile = ProjectileRecipe(Projectile, arrow);
        AsepriteProjectileRecipeEditor arrowInspector = Track((AsepriteProjectileRecipeEditor)Editor.CreateEditor(projectile));
        arrowInspector.RebuildIfChanged(projectile);
        Assert.That(arrowInspector.PreviewProblem, Is.Null);
        Assert.That(arrowInspector.PreviewFrames.FrameCount, Is.EqualTo(8));

        AsepriteProjectileRecipe empty = ProjectileRecipe(Projectile, null);
        AsepriteProjectileRecipeEditor emptyInspector = Track((AsepriteProjectileRecipeEditor)Editor.CreateEditor(empty));
        emptyInspector.RebuildIfChanged(empty);
        Assert.That(emptyInspector.PreviewProblem, Is.EqualTo($"Pick {Projectile}.aseprite to preview it."));
    }

    /// <summary>
    /// Checks a generation of the one-direction effect after an edit: no problem, the sheet's slices are its
    /// <paramref name="frames"/> frames, each slice drawn before keeps its sprite ID, and the sprite set points at the
    /// sheet's slices by frame.
    /// </summary>
    private static void AssertEffectKeepsIds(AsepriteReport report, Dictionary<string, long> ids, string sheetPath, int frames, string edit)
    {
        Assert.That(report.Problems, Is.Empty, edit);
        Dictionary<string, long> now = SpriteIds(sheetPath);
        Assert.That(now.Count, Is.EqualTo(frames), edit);
        foreach (KeyValuePair<string, long> slice in ids.Where(s => now.ContainsKey(s.Key)))
        {
            Assert.That(now[slice.Key], Is.EqualTo(slice.Value), $"{edit}: {slice.Key}");
        }

        Assert.That(ids.Keys.Count(now.ContainsKey), Is.EqualTo(Mathf.Min(frames, ids.Count)), edit);
        EffectSprites sprites = AssetDatabase.LoadAssetAtPath<EffectSprites>(EffectSheetWriter.SpritesPath(Effect));
        Assert.That(sprites.FrameCount, Is.EqualTo(frames), edit);
        for (int frame = 0; frame < frames; frame++)
        {
            string name = EffectSheetWriter.SliceName(Effect, 0, 0, frame);
            Sprite sprite = sprites.Get(0, 0, frame);
            Assert.That(sprite.name, Is.EqualTo(name), edit);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string _, out long fileId);
            Assert.That(fileId, Is.EqualTo(now[name]), $"{edit}: {name}");
        }
    }

    /// <summary>
    /// Each sprite of the sheet at <paramref name="path"/> by name, with its local file ID.
    /// </summary>
    private static Dictionary<string, long> SpriteIds(string path)
    {
        Dictionary<string, long> ids = new();
        foreach (Sprite sprite in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>())
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string _, out long fileId);
            ids[sprite.name] = fileId;
        }

        return ids;
    }

    /// <summary>
    /// Asserts that <paramref name="recipe"/> cannot generate, for a reason starting with <paramref name="expected"/>.
    /// </summary>
    private static void AssertRefused(ScriptableObject recipe, string expected)
    {
        string problem = null;
        bool can = recipe is AsepriteEffectRecipe effect ? effect.CanGenerate(out problem) : ((AsepriteProjectileRecipe)recipe).CanGenerate(out problem);
        Assert.That(can, Is.False, expected);
        Assert.That(problem, Does.StartWith(expected));
    }

    /// <summary>
    /// The pixels of the committed PNG at <paramref name="path"/>, rows from the bottom, and its width.
    /// </summary>
    private static Color32[] ReadPng(string path, out int width)
    {
        Texture2D texture = new(2, 2, TextureFormat.RGBA32, false);
        try
        {
            texture.LoadImage(File.ReadAllBytes(path));
            width = texture.width;
            return texture.GetPixels32();
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }
    }

    /// <summary>
    /// An effect of 8 x 8 px frames (<paramref name="size"/> wide and high), two a direction by default at 40 ms, each a
    /// 3 x 4 px block of its own colour at (2, 1) from the top left: eight directions tagged E to SE, or for
    /// <paramref name="directions"/> 1 every frame untagged; <paramref name="framesIn"/> a direction,
    /// <paramref name="millisecondsOf"/> each, <paramref name="marked"/> frames with the contact mark,
    /// <paramref name="empty"/> frames without a cel, each direction's tag named <paramref name="tagOf"/> (none for
    /// null), a second visible layer drawing a mark on every frame with <paramref name="layer"/>, and the canvas grown
    /// by <paramref name="pad"/> on every side around the drawing.
    /// </summary>
    private static AsepriteFileWriter EffectFile(int directions = 8, int variant = 0, int size = 8, Func<int, int> framesIn = null, Func<int, int, int> millisecondsOf = null, Func<int, int, bool> marked = null, Func<int, string> tagOf = null, Func<int, int, bool> empty = null, bool layer = false, int pad = 0)
    {
        AsepriteFileWriter writer = new(size + 2 * pad, size + 2 * pad);
        int body = writer.AddLayer("Body");
        int mark = layer ? writer.AddLayer("Mark") : -1;
        for (int direction = 0; direction < directions; direction++)
        {
            int first = writer.FrameCount;
            int frames = framesIn?.Invoke(direction) ?? 2;
            for (int f = 0; f < frames; f++)
            {
                int frame = writer.AddFrame(millisecondsOf?.Invoke(direction, f) ?? 40);
                if (empty?.Invoke(direction, f) != true)
                {
                    string contact = marked?.Invoke(direction, f) == true ? AsepriteFileWriter.ContactMarker : null;
                    writer.SetCel(body, frame, new RectInt(2 + pad, 1 + pad, 3, 4), Enumerable.Repeat(Colour(variant, direction, f), 12).ToArray(), contact);
                }

                if (mark >= 0)
                {
                    writer.SetCel(mark, frame, new RectInt(pad, pad, 1, 1), new[] { new Color32(255, 0, 255, 255) });
                }
            }

            string tag = tagOf != null ? tagOf(direction) : directions == 1 ? null : CharacterDirections.Name(direction);
            if (tag != null)
            {
                writer.AddTag(tag, first, writer.FrameCount - 1);
            }
        }

        return writer;
    }

    /// <summary>
    /// A projectile of 8 x 8 px frames, one per direction (<paramref name="count"/>, 8 by default), each a 2 x 2 px
    /// block of its direction's colour at (3, 2) from the top left, shown <paramref name="milliseconds"/> each (100
    /// by default); <paramref name="marked"/> frames with the contact mark, <paramref name="empty"/> frames without a
    /// cel, a second visible layer drawing a mark on every frame with <paramref name="layer"/>, and the canvas grown by
    /// <paramref name="pad"/> on every side around the drawing.
    /// </summary>
    private static AsepriteFileWriter ProjectileFile(int count = 8, Func<int, int> milliseconds = null, Func<int, bool> marked = null, Func<int, bool> empty = null, bool layer = false, int pad = 0)
    {
        AsepriteFileWriter writer = new(8 + 2 * pad, 8 + 2 * pad);
        int body = writer.AddLayer("Body");
        int mark = layer ? writer.AddLayer("Mark") : -1;
        for (int direction = 0; direction < count; direction++)
        {
            int frame = writer.AddFrame(milliseconds?.Invoke(direction) ?? 100);
            if (empty?.Invoke(direction) != true)
            {
                string contact = marked?.Invoke(direction) == true ? AsepriteFileWriter.ContactMarker : null;
                writer.SetCel(body, frame, new RectInt(3 + pad, 2 + pad, 2, 2), Enumerable.Repeat(Colour(0, direction, 0), 4).ToArray(), contact);
            }

            if (mark >= 0)
            {
                writer.SetCel(mark, frame, new RectInt(pad, pad, 1, 1), new[] { new Color32(255, 0, 255, 255) });
            }
        }

        return writer;
    }

    /// <summary>
    /// The colour of frame <paramref name="frame"/> of <paramref name="direction"/> of <paramref name="variant"/> in
    /// the test files.
    /// </summary>
    private static Color32 Colour(int variant, int direction, int frame)
    {
        return new Color32((byte)(20 + direction * 7), (byte)(40 + frame * 50), (byte)(90 + variant * 25), 255);
    }

    /// <summary>
    /// The pixel of a read file's sheet at (<paramref name="x"/>, <paramref name="y"/>) from the top left of frame
    /// <paramref name="frame"/> of <paramref name="direction"/>.
    /// </summary>
    private static Color32 Pixel(AsepriteClipFrames frames, int direction, int frame, int x, int y)
    {
        RectInt rect = frames.FrameRect(direction, frame);
        return frames.Pixels[(rect.y + frames.FrameSize.y - 1 - y) * frames.SheetWidth + rect.x + x];
    }

    /// <summary>
    /// An unsaved effect recipe for the sheet <paramref name="name"/> of <paramref name="files"/>, drawn facing one way
    /// pivoted on the small pivot unless it is one per damage type (then in eight directions).
    /// </summary>
    private AsepriteEffectRecipe EffectRecipe(string name, AsepriteEffectVariants variants, params Object[] files)
    {
        return Track(AsepriteEffectRecipe.Create(name, variants, variants == AsepriteEffectVariants.DamageTypes, SmallPivot, files));
    }

    /// <summary>
    /// An unsaved projectile recipe for the sheet <paramref name="name"/> of <paramref name="file"/> in
    /// <paramref name="directions"/> directions, its tip on <paramref name="tip"/> (the small pivot by default).
    /// </summary>
    private AsepriteProjectileRecipe ProjectileRecipe(string name, Object file, int directions = 8, Vector2Int? tip = null)
    {
        return Track(AsepriteProjectileRecipe.Create(name, file, directions, tip ?? SmallPivot));
    }

    /// <summary>
    /// Keeps <paramref name="made"/> for <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T made) where T : Object
    {
        created.Add(made);
        return made;
    }

    /// <summary>
    /// Writes <paramref name="writer"/>'s file as <paramref name="fileName"/> in a folder of its own under the
    /// project's Temp folder, deleted after the test (nothing there is imported), and returns its path.
    /// </summary>
    private string WriteTemp(AsepriteFileWriter writer, string fileName)
    {
        string path = TempPath(fileName);
        writer.Write(path);
        return path;
    }

    /// <summary>
    /// Writes a file that is not an Aseprite file as <paramref name="fileName"/> under the project's Temp folder.
    /// </summary>
    private string WriteJunk(string fileName)
    {
        string path = TempPath(fileName);
        File.WriteAllBytes(path, Enumerable.Repeat((byte)7, 200).ToArray());
        return path;
    }

    /// <summary>
    /// A path for <paramref name="fileName"/> in a new folder of its own under the project's Temp folder.
    /// </summary>
    private string TempPath(string fileName)
    {
        string folder = FileUtil.GetUniqueTempPathInProject();
        Directory.CreateDirectory(folder);
        tempFolders.Add(folder);
        return Path.Combine(folder, fileName);
    }

    /// <summary>
    /// Writes <paramref name="writer"/>'s file as <paramref name="name"/>.aseprite in <paramref name="folder"/>, created
    /// when missing (the first folder on its way that did not exist is deleted after the test), imports it and returns
    /// its main asset.
    /// </summary>
    private Object Import(AsepriteFileWriter writer, string folder, string name)
    {
        string missing = null;
        for (string parent = folder; !AssetDatabase.IsValidFolder(parent); parent = Path.GetDirectoryName(parent).Replace('\\', '/'))
        {
            missing = parent;
        }

        if (missing != null)
        {
            createdFolders.Add(missing);
            TerrainMaterialWriter.EnsureFolder(folder);
        }

        string path = $"{folder}/{name}.aseprite";
        writer.Write(path);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        return AssetDatabase.LoadMainAssetAtPath(path);
    }
}
