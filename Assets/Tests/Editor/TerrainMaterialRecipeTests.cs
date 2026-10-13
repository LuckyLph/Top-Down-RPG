using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

/// <summary>
/// Tests for the terrain material recipes and <see cref="TerrainMaterialWriter"/>: each recipe gives the same pixels
/// for the same seed and others for another seed, and the same PNG bytes on every run; a failed write leaves every file
/// as it was; texture files keep the paths a material already uses and are otherwise named after it, and another
/// material's files are found; the cell fields keep the terrain shader's contract (every chip pixel points at its
/// chip's centre, a gravel chip's reach covers its whole footprint and alpha is 0 exactly where no chip is, a stone
/// pixel's alpha is its distance to its stone's edge); chips and stones are lit from the top left; the height is smooth
/// with one value per chip or stone, and glint and sparkle phases only in the water recipes and sway weights only in
/// weeds', which repeat the grass recipe's ground and weigh each weed's box by row inside one cell column; caustic
/// water's net breathes between two courses, runs sideways when flattened and breaks into ripples with a junction gap,
/// and pond water's strokes glint from their middle out while its sparkles sit on plain water; tufts point up; sand's
/// crests lie over their troughs; mud's puddles are shaded under their top-left bank and lit on their bottom-right
/// edge, gather in clusters, its ridges cast their shade a row below and a pixel to the right and its cracks are grooves
/// with a lit lip below and right; stones on a regular grid moved half a pixel have one-pixel mortar; writing the
/// textures to a material sets the base colour the textures' base is shaded from and has the renderers drawing it
/// rebuild; and every project recipe still generates its material's textures. Recipes and materials are built in memory
/// with their default settings (the project's materials and recipes are only read), and files go to a temporary folder
/// outside Assets.
/// </summary>
public class TerrainMaterialRecipeTests
{
    private const int Seed = 20261013;

    private static readonly Type[] RecipeTypes =
    {
        typeof(GrassTuftsRecipe), typeof(GravelChipsRecipe), typeof(StoneCobblesRecipe), typeof(SandRipplesRecipe), typeof(MudPuddlesRecipe), typeof(WaterCausticsRecipe),
        typeof(WaterHighlightsRecipe), typeof(WeedsRecipe)
    };

    private readonly List<Object> created = new();
    private string tempFolder;

    /// <summary>
    /// Destroys what the test created and deletes its temporary folder.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Object createdObject in created)
        {
            if (createdObject != null)
            {
                Object.DestroyImmediate(createdObject);
            }
        }

        created.Clear();
        if (tempFolder != null && Directory.Exists(tempFolder))
        {
            Directory.Delete(tempFolder, true);
        }

        tempFolder = null;
    }

    /// <summary>
    /// Generating twice from one seed gives the same albedo, cell field and height; another seed gives other ones.
    /// Textures are 4 cells of 32 px, with the recipe's cell mode.
    /// </summary>
    [TestCaseSource(nameof(RecipeTypes))]
    public void TheSameSeed_GivesTheSamePixels_AndAnotherSeedOthers(Type recipeType)
    {
        TerrainMaterialRecipe recipe = CreateRecipe(recipeType);

        TerrainTextures first = recipe.Generate(Seed);
        TerrainTextures again = recipe.Generate(Seed);
        TerrainTextures other = recipe.Generate(Seed + 1);

        Assert.That(first.Size, Is.EqualTo(128));
        Assert.That(first.CellMode, Is.EqualTo(recipe.CellMode));
        Assert.That(first.CellField != null, Is.EqualTo(recipe.CellMode != TerrainMaterial.CellMode.None));
        Assert.That(Differences(again.Albedo, first.Albedo), Is.Zero);
        Assert.That(Differences(again.CellField, first.CellField), Is.Zero);
        Assert.That(Differences(again.Height, first.Height), Is.Zero);
        Assert.That(Differences(other.Albedo, first.Albedo), Is.GreaterThan(0));
        Assert.That(Differences(other.Height, first.Height), Is.GreaterThan(0));
        if (first.CellField != null)
        {
            Assert.That(Differences(other.CellField, first.CellField), Is.GreaterThan(0));
        }
    }

    /// <summary>
    /// The PNG files written for one seed are byte-identical on a second run, with a cell field only for chips and
    /// stones, and decode to exactly the generated pixels, rows from the bottom.
    /// </summary>
    [TestCaseSource(nameof(RecipeTypes))]
    public void TheWrittenPngs_AreByteIdenticalAcrossRuns(Type recipeType)
    {
        TerrainMaterialRecipe recipe = CreateRecipe(recipeType);
        TerrainTextures textures = recipe.Generate(Seed);
        bool hasField = textures.CellField != null;
        (string Albedo, string CellField, string Height) first = TerrainMaterialWriter.PathsIn(TempFolder("first"), "sample", hasField);
        (string Albedo, string CellField, string Height) second = TerrainMaterialWriter.PathsIn(TempFolder("second"), "sample", hasField);

        TerrainMaterialWriter.WriteFiles(textures, first);
        TerrainMaterialWriter.WriteFiles(recipe.Generate(Seed), second);

        Assert.That(File.Exists(first.Albedo) && File.Exists(first.Height), Is.True);
        Assert.That(first.CellField != null && File.Exists(first.CellField), Is.EqualTo(hasField));
        Assert.That(File.ReadAllBytes(second.Albedo), Is.EqualTo(File.ReadAllBytes(first.Albedo)));
        Assert.That(File.ReadAllBytes(second.Height), Is.EqualTo(File.ReadAllBytes(first.Height)));
        Assert.That(Differences(Decode(first.Albedo), textures.Albedo), Is.Zero);
        Assert.That(Differences(Decode(first.Height), textures.Height), Is.Zero);
        if (textures.CellField != null)
        {
            Assert.That(File.ReadAllBytes(second.CellField), Is.EqualTo(File.ReadAllBytes(first.CellField)));
            Assert.That(Differences(Decode(first.CellField), textures.CellField), Is.Zero);
        }
    }

    /// <summary>
    /// When one of a material's files cannot be written (here the height, held open by another reader), the files
    /// written before it get their previous bytes back, so the material's textures on disk never mix two runs.
    /// </summary>
    [Test]
    public void AFailedWrite_LeavesEveryFileAsItWas()
    {
        TerrainMaterialRecipe recipe = CreateRecipe(typeof(GravelChipsRecipe));
        (string Albedo, string CellField, string Height) paths = TerrainMaterialWriter.PathsIn(TempFolder("files"), "sample", true);
        TerrainMaterialWriter.WriteFiles(recipe.Generate(Seed), paths);
        byte[] albedo = File.ReadAllBytes(paths.Albedo);
        byte[] field = File.ReadAllBytes(paths.CellField);
        byte[] height = File.ReadAllBytes(paths.Height);

        using (new FileStream(paths.Height, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Throws<IOException>(() => TerrainMaterialWriter.WriteFiles(recipe.Generate(Seed + 1), paths));
        }

        Assert.That(File.ReadAllBytes(paths.Albedo), Is.EqualTo(albedo));
        Assert.That(File.ReadAllBytes(paths.CellField), Is.EqualTo(field));
        Assert.That(File.ReadAllBytes(paths.Height), Is.EqualTo(height));
    }

    /// <summary>
    /// Generate writes a new material's textures into a folder named after it in the terrain texture folder, each file
    /// named after the material in lower case, with a cell field only when the recipe makes one.
    /// </summary>
    [Test]
    public void TargetPaths_AreNamedAfterTheMaterial_InItsOwnFolder()
    {
        TerrainMaterial material = Track(TerrainMaterial.Create("RoadGravel", null, null, 4, TerrainMaterial.CellMode.None, Color.gray, new TerrainStyle.EdgeSettings()));

        (string Albedo, string CellField, string Height) withField = TerrainMaterialWriter.TargetPaths(material, true);
        (string Albedo, string CellField, string Height) withoutField = TerrainMaterialWriter.TargetPaths(material, false);

        Assert.That(TerrainMaterialWriter.FolderOf(material), Is.EqualTo("Assets/Textures/Terrain/RoadGravel"));
        Assert.That(withField.Albedo, Is.EqualTo("Assets/Textures/Terrain/RoadGravel/roadgravel_albedo.png"));
        Assert.That(withField.CellField, Is.EqualTo("Assets/Textures/Terrain/RoadGravel/roadgravel_field.png"));
        Assert.That(withField.Height, Is.EqualTo("Assets/Textures/Terrain/RoadGravel/roadgravel_height.png"));
        Assert.That(withoutField.CellField, Is.Null);
    }

    /// <summary>
    /// The project's materials (only read) each keep their textures in a folder of their own, named after them, under
    /// the terrain texture folder, and that is the folder Generate writes them to.
    /// </summary>
    [Test]
    public void TheProjectsMaterials_KeepTheirTexturesInAFolderOfTheirOwn()
    {
        foreach (string name in new[] { "Grass", "Gravel", "Stone", "DryGrass", "Dirt", "Plaza" })
        {
            TerrainMaterial material = AssetDatabase.LoadAssetAtPath<TerrainMaterial>($"Assets/Settings/Terrain/Materials/{name}.asset");
            string folder = $"{TerrainMaterialWriter.TextureFolder}/{name}";

            Assert.That(TerrainMaterialWriter.FolderOf(material), Is.EqualTo(folder), name);
            foreach (Texture2D texture in new[] { material.Albedo, material.CellField, material.Height })
            {
                if (texture != null)
                {
                    Assert.That(Path.GetDirectoryName(AssetDatabase.GetAssetPath(texture)).Replace('\\', '/'), Is.EqualTo(folder), texture.name);
                }
            }
        }
    }

    /// <summary>
    /// A material that already uses PNGs under the terrain texture folder keeps them whatever its name, and puts a
    /// texture it does not have yet beside them, so renaming it keeps its textures' folder, files and GUIDs; those files
    /// are another material's (the project's Gravel, only read here), which Generate refuses to overwrite, while the
    /// Gravel material itself may.
    /// </summary>
    [Test]
    public void TargetPaths_KeepTheFilesAMaterialAlreadyUses_AndOtherMaterialsFilesAreFound()
    {
        TerrainMaterial gravel = AssetDatabase.LoadAssetAtPath<TerrainMaterial>("Assets/Settings/Terrain/Materials/Gravel.asset");
        Assert.That(gravel, Is.Not.Null);
        TerrainMaterial renamed = Track(TerrainMaterial.Create("RoadGravel", gravel.Albedo, gravel.CellField, 4, TerrainMaterial.CellMode.WholeChips, Color.gray, new TerrainStyle.EdgeSettings()));

        (string Albedo, string CellField, string Height) paths = TerrainMaterialWriter.TargetPaths(renamed, true);

        Assert.That(paths.Albedo, Is.EqualTo(AssetDatabase.GetAssetPath(gravel.Albedo)));
        Assert.That(paths.CellField, Is.EqualTo(AssetDatabase.GetAssetPath(gravel.CellField)));
        Assert.That(paths.Height, Is.EqualTo("Assets/Textures/Terrain/Gravel/roadgravel_height.png"), "A texture the material does not use yet is named after it, beside its other textures.");
        Assert.That(TerrainMaterialWriter.MaterialUsingTexture(paths.Albedo, renamed), Is.SameAs(gravel));
        Assert.That(TerrainMaterialWriter.MaterialUsingTexture(paths.Albedo, gravel), Is.Null);
        Assert.That(TerrainMaterialWriter.MaterialUsingTexture(paths.Height, renamed), Is.Null);
    }

    /// <summary>
    /// Every gravel pixel with alpha points at a pixel with alpha that points at itself: its chip's centre, which is
    /// what identifies a chip in the field. That centre has the same alpha (the chip's reach, alpha / 16), which is at
    /// least the distance to the farthest pixel pointing at it and at most a sixteenth more; alpha is 0, with no offset,
    /// exactly where the albedo shows the bare dirt, the base colour shaded by the blue channel.
    /// </summary>
    [Test]
    public void GravelChips_CellField_KeepsTheShadersContract()
    {
        TerrainTextures textures = CreateRecipe(typeof(GravelChipsRecipe)).Generate(Seed);
        int size = textures.Size;
        Color32[] field = textures.CellField;
        double[] farthest = new double[field.Length];
        int bare = 0;
        int chipPixels = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                Color32 texel = field[i];
                bool showsDirt = SameColour(textures.Albedo[i], ShadedBase(textures.BaseColour, texel.b));
                Assert.That(texel.a == 0, Is.EqualTo(showsDirt), $"({x}, {y}): alpha {texel.a}");
                if (texel.a == 0)
                {
                    Assert.That(texel.r == 128 && texel.g == 128, Is.True, $"({x}, {y}) has no chip but an offset.");
                    bare++;
                    continue;
                }

                int centre = CentreOf(texel, x, y, size);
                Color32 centreTexel = field[centre];
                Assert.That(centreTexel.a > 0 && centreTexel.r == 128 && centreTexel.g == 128, Is.True, $"({x}, {y}) points at a pixel that is not its chip's centre.");
                Assert.That(texel.a, Is.EqualTo(centreTexel.a), $"({x}, {y}): a chip's reach is the same over the whole chip.");
                farthest[centre] = Math.Max(farthest[centre], Math.Sqrt((texel.r - 128) * (texel.r - 128) + (texel.g - 128) * (texel.g - 128)));
                chipPixels++;
            }
        }

        int chips = 0;
        for (int i = 0; i < field.Length; i++)
        {
            if (field[i].a > 0 && field[i].r == 128 && field[i].g == 128)
            {
                chips++;
                double reach = field[i].a / 16.0;
                Assert.That(reach, Is.GreaterThanOrEqualTo(farthest[i] - 1e-9), $"chip at {i % size}, {i / size}");
                Assert.That(reach, Is.LessThanOrEqualTo(farthest[i] + 1.0 / 16.0 + 1e-9), $"chip at {i % size}, {i / size}");
            }
        }

        Assert.That(chips, Is.GreaterThan(200));
        Assert.That(chipPixels, Is.GreaterThan(1000));
        Assert.That(bare, Is.GreaterThan(1000));
    }

    /// <summary>
    /// Every stone pixel has alpha and points at its stone's centre pixel, which points at itself; its alpha / 16 is its
    /// distance to its stone's edge: never more than the distance to the nearest pixel of another stone nor less than
    /// that distance minus a pixel's diagonal, summing to at most a pixel across any two neighbours of different stones,
    /// and changing by at most a pixel between neighbours of the same stone (each within the encoding's rounding). The
    /// stones are as deep as their size gives: half of them have a pixel over 2.5 px from their edge.
    /// </summary>
    [Test]
    public void StoneCobbles_CellField_KeepsTheShadersContract()
    {
        TerrainTextures textures = CreateRecipe(typeof(StoneCobblesRecipe)).Generate(Seed);
        int size = textures.Size;
        Color32[] field = textures.CellField;
        int[] stone = new int[field.Length];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                Assert.That(field[i].a, Is.GreaterThan(0), $"({x}, {y}) belongs to no stone.");
                stone[i] = CentreOf(field[i], x, y, size);
                Color32 centreTexel = field[stone[i]];
                Assert.That(centreTexel.r == 128 && centreTexel.g == 128, Is.True, $"({x}, {y}) points at a pixel that is not its stone's centre.");
            }
        }

        int stones = 0;
        int mortar = 0;
        double[] deepest = new double[field.Length];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                double border = field[i].a / 16.0;
                double nearestOther = NearestOtherStone(stone, x, y, size);
                stones += stone[i] == i ? 1 : 0;
                mortar += field[i].a < 8 ? 1 : 0;
                deepest[stone[i]] = Math.Max(deepest[stone[i]], border);
                Assert.That(border, Is.LessThanOrEqualTo(nearestOther + 1.0 / 32.0 + 1e-9), $"({x}, {y})");
                Assert.That(border, Is.GreaterThanOrEqualTo(nearestOther - Math.Sqrt(2.0) - 1.0 / 32.0 - 1e-9), $"({x}, {y})");
                foreach (int neighbour in new[] { y * size + TerrainArt.Wrap(x + 1, size), TerrainArt.Wrap(y + 1, size) * size + x })
                {
                    double other = field[neighbour].a / 16.0;
                    if (stone[neighbour] != stone[i])
                    {
                        Assert.That(border + other, Is.LessThanOrEqualTo(1.0 + 1.0 / 8.0 + 1e-9), $"({x}, {y}) beside another stone");
                    }
                    else
                    {
                        Assert.That(Math.Abs(border - other), Is.LessThanOrEqualTo(1.0 + 1.0 / 16.0 + 1e-9), $"({x}, {y}) beside its own stone");
                    }
                }
            }
        }

        Assert.That(stones, Is.GreaterThan(200));
        Assert.That(mortar, Is.GreaterThan(1000));
        List<double> depths = new();
        for (int i = 0; i < field.Length; i++)
        {
            if (stone[i] == i)
            {
                depths.Add(deepest[i]);
            }
        }

        depths.Sort();
        Assert.That(depths[depths.Count / 2], Is.GreaterThan(2.5));
    }

    /// <summary>
    /// Chips and stones are lit from the top left, as the terrain shader lights the ground: their pixels up and to the
    /// left of their centre are brighter on average than those down and to the right (gravel shadows left out).
    /// </summary>
    [TestCase(typeof(GravelChipsRecipe))]
    [TestCase(typeof(StoneCobblesRecipe))]
    public void ChipsAndStones_AreLitFromTheTopLeft(Type recipeType)
    {
        TerrainTextures textures = CreateRecipe(recipeType).Generate(Seed);
        double topLeft = 0.0;
        double bottomRight = 0.0;
        int topLeftCount = 0;
        int bottomRightCount = 0;
        for (int i = 0; i < textures.Albedo.Length; i++)
        {
            Color32 texel = textures.CellField[i];
            Color32 colour = textures.Albedo[i];
            Color32 shadow = ShadedBase(textures.BaseColour, (byte)Math.Max(0, texel.b - 16));
            if (texel.a == 0 || SameColour(colour, shadow))
            {
                continue;
            }

            int towardsCentreX = texel.r - 128;
            int towardsCentreY = texel.g - 128;
            double brightness = (colour.r + colour.g + colour.b) / 3.0;
            if (towardsCentreX > 0 && towardsCentreY < 0)
            {
                topLeft += brightness;
                topLeftCount++;
            }
            else if (towardsCentreX < 0 && towardsCentreY > 0)
            {
                bottomRight += brightness;
                bottomRightCount++;
            }
        }

        Assert.That(topLeftCount, Is.GreaterThan(200));
        Assert.That(bottomRightCount, Is.GreaterThan(200));
        Assert.That(topLeft / topLeftCount, Is.GreaterThan(bottomRight / bottomRightCount + 10.0));
    }

    /// <summary>
    /// The height's red channel changes by at most 0.1 per pixel (within rounding; the shader's contract allows 0.2) and
    /// is not flat; its green channel is one value per chip or stone, and 0.5 where there is none (grass, bare gravel
    /// dirt); its blue channel holds no glint phase but in water.
    /// </summary>
    [TestCaseSource(nameof(RecipeTypes))]
    public void TheHeight_IsSmooth_WithOneValuePerChipOrStone(Type recipeType)
    {
        TerrainTextures textures = CreateRecipe(recipeType).Generate(Seed);
        int size = textures.Size;
        Color32[] height = textures.Height;
        Color32[] field = textures.CellField;
        int lowest = 255;
        int highest = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                lowest = Math.Min(lowest, height[i].r);
                highest = Math.Max(highest, height[i].r);
                Assert.That(Math.Abs(height[i].r - height[y * size + TerrainArt.Wrap(x + 1, size)].r) / 255.0, Is.LessThanOrEqualTo(0.1 + 1.0 / 255.0), $"({x}, {y})");
                Assert.That(Math.Abs(height[i].r - height[TerrainArt.Wrap(y + 1, size) * size + x].r) / 255.0, Is.LessThanOrEqualTo(0.1 + 1.0 / 255.0), $"({x}, {y})");
                if (field == null || field[i].a == 0)
                {
                    Assert.That(height[i].g, Is.EqualTo(128), $"({x}, {y})");
                }
                else
                {
                    Assert.That(height[i].g, Is.EqualTo(height[CentreOf(field[i], x, y, size)].g), $"({x}, {y})");
                }

                if (recipeType != typeof(WaterCausticsRecipe) && recipeType != typeof(WaterHighlightsRecipe) && recipeType != typeof(WeedsRecipe))
                {
                    Assert.That(height[i].b, Is.Zero, $"({x}, {y})");
                }
            }
        }

        Assert.That(highest - lowest, Is.GreaterThan(64));
    }

    /// <summary>
    /// A tuft glyph's top row is drawn above its bottom row: with one glyph of a tip over a blade, every tip has its
    /// blade right under it.
    /// </summary>
    [Test]
    public void GrassTufts_PointUp()
    {
        GrassTuftsRecipe recipe = (GrassTuftsRecipe)CreateRecipe(typeof(GrassTuftsRecipe));
        SerializedObject settings = new(recipe);
        settings.FindProperty("blade").colorValue = Color.black;
        settings.FindProperty("tip").colorValue = Color.white;
        settings.FindProperty("grit").floatValue = 0f;
        settings.FindProperty("tuftChance").floatValue = 1f;
        settings.FindProperty("densityWaves").arraySize = 0;
        SerializedProperty glyphs = settings.FindProperty("glyphs");
        glyphs.arraySize = 1;
        glyphs.GetArrayElementAtIndex(0).stringValue = "+\n#";
        settings.ApplyModifiedPropertiesWithoutUndo();

        TerrainTextures textures = recipe.Generate(Seed);

        int size = textures.Size;
        int tips = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (SameColour(textures.Albedo[y * size + x], new Color32(255, 255, 255, 255)))
                {
                    tips++;
                    Assert.That(SameColour(textures.Albedo[TerrainArt.Wrap(y - 1, size) * size + x], new Color32(0, 0, 0, 255)), Is.True, $"no blade under the tip at ({x}, {y})");
                }
            }
        }

        Assert.That(tips, Is.GreaterThan(100));
    }

    /// <summary>
    /// Water's net breathes between two courses and sparkles at some knots. A pixel with a glint phase shows the plain
    /// fill (water, deep or the lines' glow) until it lights up with the net's colour, which the textures bring as their
    /// glint colour, and most of them have a pixel of the other course nearby whose phase is half a period away, so each
    /// edge always shows one course. Pixels drawn as net or knot carry no glint. Sparkles are short horizontal runs
    /// starting on the net whose phases step along them. The glow and the net's two courses each take a fair share of
    /// the texture.
    /// </summary>
    [Test]
    public void WaterCaustics_BreatheBetweenTwoCourses_AndSparkleAtKnots()
    {
        WaterCausticsRecipe recipe = (WaterCausticsRecipe)CreateRecipe(typeof(WaterCausticsRecipe));
        SerializedObject settings = new(recipe);
        Color32 water = settings.FindProperty("water").colorValue;
        Color32 deep = settings.FindProperty("deep").colorValue;
        Color32 halo = settings.FindProperty("halo").colorValue;
        Color32 net = settings.FindProperty("net").colorValue;
        Color32 knot = settings.FindProperty("knot").colorValue;
        int sparkleLength = settings.FindProperty("sparkleLength").intValue;
        int sparkleStep = (int)Math.Round(settings.FindProperty("sparkleStep").floatValue * 128.0);

        TerrainTextures textures = recipe.Generate(Seed);

        int size = textures.Size;
        Assert.That(textures.GlintColour.HasValue && SameColour(textures.GlintColour.Value, net), Is.True);
        Assert.That(textures.SparkleColour.HasValue, Is.True);
        int breathing = 0;
        int paired = 0;
        int glowFill = 0;
        int fill = 0;
        int lines = 0;
        int sparkles = 0;
        int sparkleSteps = 0;
        int stepsInTime = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                Color32 colour = textures.Albedo[i];
                int code = textures.Height[i].b;
                bool isFill = SameColour(colour, water) || SameColour(colour, deep) || SameColour(colour, halo);
                bool isLine = SameColour(colour, net) || SameColour(colour, knot);
                Assert.That(isFill || isLine, Is.True, $"({x}, {y}) is neither fill nor net");
                fill += isFill ? 1 : 0;
                glowFill += SameColour(colour, halo) ? 1 : 0;
                lines += isLine ? 1 : 0;
                if (code >= 128)
                {
                    sparkles++;
                    int left = textures.Height[y * size + TerrainArt.Wrap(x - 1, size)].b;
                    Assert.That(isLine || left >= 128, Is.True, $"({x}, {y}) sparkles neither on the net nor after a sparkle");
                    if (left >= 128)
                    {
                        sparkleSteps++;
                        stepsInTime += ((code - left) % 128 + 128) % 128 == sparkleStep ? 1 : 0;
                    }

                    Assert.That(RunLength(textures.Height, x, y, size), Is.LessThanOrEqualTo(sparkleLength * 2), $"({x}, {y})");
                }
                else if (code > 0)
                {
                    breathing++;
                    Assert.That(isFill, Is.True, $"({x}, {y}) breathes but is drawn as net");
                    paired += HasOppositeCourseNearby(textures.Height, x, y, size, code) ? 1 : 0;
                }
            }
        }

        Assert.That(breathing, Is.GreaterThan(size * size / 20));
        Assert.That(paired, Is.GreaterThan(breathing * 6 / 10), "most breathing pixels have the other course nearby");
        Assert.That(glowFill, Is.InRange(fill / 10, fill * 6 / 10), "the glow around the lines");
        Assert.That(lines, Is.GreaterThan(0), "some of the net lies on both courses");
        Assert.That(lines + breathing, Is.InRange(size * size / 20, size * size * 4 / 10), "the net, both courses together");
        Assert.That(sparkles, Is.GreaterThan(0).And.LessThan(size * size / 100));
        Assert.That(stepsInTime, Is.GreaterThan(sparkleSteps * 8 / 10), "a sparkle's phases step along its dash");
    }

    /// <summary>
    /// Flattening water's net and leaving out its upright edges makes it run sideways: its lines' runs of pixels along x
    /// grow longer against their runs along y than in the default net, both courses counted.
    /// </summary>
    [Test]
    public void WaterCaustics_Flattened_RunSideways()
    {
        WaterCausticsRecipe plain = (WaterCausticsRecipe)CreateRecipe(typeof(WaterCausticsRecipe));
        WaterCausticsRecipe flat = (WaterCausticsRecipe)CreateRecipe(typeof(WaterCausticsRecipe));
        SerializedObject settings = new(flat);
        settings.FindProperty("flatten").floatValue = 3f;
        settings.FindProperty("uprightDropChance").floatValue = 0.9f;
        settings.ApplyModifiedPropertiesWithoutUndo();

        double plainRatio = SidewaysRunRatio(plain);
        double flatRatio = SidewaysRunRatio(flat);

        Assert.That(flatRatio, Is.GreaterThan(plainRatio + 0.15), $"plain {plainRatio:F2}, flattened {flatRatio:F2}");
        Assert.That(flatRatio, Is.GreaterThan(1.1));
    }

    /// <summary>
    /// A junction gap breaks water's net into separate ripples: the largest 8-connected piece of its lines (both courses)
    /// holds well under half the share of all line pixels it holds in the connected default net, and sparkles still
    /// show, since they start from the edges' middles.
    /// </summary>
    [Test]
    public void WaterCaustics_JunctionGap_BreaksTheNetIntoRipples()
    {
        WaterCausticsRecipe connected = (WaterCausticsRecipe)CreateRecipe(typeof(WaterCausticsRecipe));
        WaterCausticsRecipe broken = (WaterCausticsRecipe)CreateRecipe(typeof(WaterCausticsRecipe));
        SerializedObject settings = new(broken);
        settings.FindProperty("junctionGap").floatValue = 3f;
        settings.ApplyModifiedPropertiesWithoutUndo();

        double connectedShare = LargestPieceShare(connected, out _);
        double brokenShare = LargestPieceShare(broken, out int sparkles);

        Assert.That(brokenShare, Is.LessThan(connectedShare / 2.0), $"connected {connectedShare:P1}, broken {brokenShare:P1}");
        Assert.That(sparkles, Is.GreaterThan(0));
    }

    /// <summary>
    /// The share of the line pixels (net, knots and both breathing courses) of <paramref name="recipe"/>'s textures for
    /// the test seed that the largest of their 8-connected pieces holds, wrapping around the texture, and in
    /// <paramref name="sparkles"/> how many pixels sparkle.
    /// </summary>
    private static double LargestPieceShare(WaterCausticsRecipe recipe, out int sparkles)
    {
        SerializedObject settings = new(recipe);
        Color32 net = settings.FindProperty("net").colorValue;
        Color32 knot = settings.FindProperty("knot").colorValue;
        TerrainTextures textures = recipe.Generate(Seed);
        int size = textures.Size;
        bool[] line = new bool[size * size];
        sparkles = 0;
        int total = 0;
        for (int i = 0; i < line.Length; i++)
        {
            int code = textures.Height[i].b;
            line[i] = SameColour(textures.Albedo[i], net) || SameColour(textures.Albedo[i], knot) || (code > 0 && code < 128);
            sparkles += code >= 128 ? 1 : 0;
            total += line[i] ? 1 : 0;
        }

        bool[] seen = new bool[line.Length];
        Stack<int> stack = new();
        int largest = 0;
        for (int start = 0; start < line.Length; start++)
        {
            if (!line[start] || seen[start])
            {
                continue;
            }

            int piece = 0;
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int index = stack.Pop();
                piece++;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int next = TerrainArt.Wrap(index / size + dy, size) * size + TerrainArt.Wrap(index % size + dx, size);
                        if (line[next] && !seen[next])
                        {
                            seen[next] = true;
                            stack.Push(next);
                        }
                    }
                }
            }

            largest = Math.Max(largest, piece);
        }

        return (double)largest / total;
    }

    /// <summary>
    /// The mean length of the runs of line pixels (net, knots and both breathing courses) along x over that along y, in
    /// <paramref name="recipe"/>'s textures for the test seed, wrapping around the texture.
    /// </summary>
    private static double SidewaysRunRatio(WaterCausticsRecipe recipe)
    {
        SerializedObject settings = new(recipe);
        Color32 net = settings.FindProperty("net").colorValue;
        Color32 knot = settings.FindProperty("knot").colorValue;
        TerrainTextures textures = recipe.Generate(Seed);
        int size = textures.Size;
        bool[] line = new bool[size * size];
        for (int i = 0; i < line.Length; i++)
        {
            int code = textures.Height[i].b;
            line[i] = SameColour(textures.Albedo[i], net) || SameColour(textures.Albedo[i], knot) || (code > 0 && code < 128);
        }

        int pixels = 0;
        int sidewaysRuns = 0;
        int uprightRuns = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (!line[y * size + x])
                {
                    continue;
                }

                pixels++;
                sidewaysRuns += line[y * size + TerrainArt.Wrap(x - 1, size)] ? 0 : 1;
                uprightRuns += line[TerrainArt.Wrap(y - 1, size) * size + x] ? 0 : 1;
            }
        }

        return ((double)pixels / sidewaysRuns) / ((double)pixels / uprightRuns);
    }

    /// <summary>
    /// Pond water is its three flat tones, near their shares, and strokes in the resting colour. Exactly the stroke
    /// pixels carry a glint phase; a stroke is a level dash of at least two pixels and at most the longest stroke, both
    /// ends with one phase, and on a dash with a middle pixel the ends glint a spread after it. Sparkles sit on plain
    /// water. The textures bring the highlight and sparkle colours as their glint and sparkle colours.
    /// </summary>
    [Test]
    public void WaterHighlights_StrokesGlintFromTheirMiddle_AndSparklesSitOnPlainWater()
    {
        WaterHighlightsRecipe recipe = (WaterHighlightsRecipe)CreateRecipe(typeof(WaterHighlightsRecipe));
        SerializedObject settings = new(recipe);
        Color32 water = settings.FindProperty("water").colorValue;
        Color32 darker = settings.FindProperty("darker").colorValue;
        Color32 lighter = settings.FindProperty("lighter").colorValue;
        Color32 resting = settings.FindProperty("resting").colorValue;
        double darkerShare = settings.FindProperty("darkerShare").floatValue;
        double lighterShare = settings.FindProperty("lighterShare").floatValue;
        int maxLength = settings.FindProperty("maxLength").intValue;
        int spread = (int)Math.Round(settings.FindProperty("spread").floatValue * 127.0);

        TerrainTextures textures = recipe.Generate(Seed);

        int size = textures.Size;
        Assert.That(textures.GlintColour.HasValue && SameColour(textures.GlintColour.Value, settings.FindProperty("highlight").colorValue), Is.True);
        Assert.That(textures.SparkleColour.HasValue && SameColour(textures.SparkleColour.Value, settings.FindProperty("sparkle").colorValue), Is.True);
        int plain = 0;
        int darkerPixels = 0;
        int lighterPixels = 0;
        int strokes = 0;
        int sparkles = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                Color32 colour = textures.Albedo[i];
                int code = textures.Height[i].b;
                bool stroke = SameColour(colour, resting);
                Assert.That(stroke || SameColour(colour, water) || SameColour(colour, darker) || SameColour(colour, lighter), Is.True, $"({x}, {y}) is neither a tone nor a stroke");
                Assert.That(code > 0 && code < 128, Is.EqualTo(stroke), $"({x}, {y}) glints off a stroke, or a stroke does not");
                if (!stroke)
                {
                    plain++;
                    darkerPixels += SameColour(colour, darker) ? 1 : 0;
                    lighterPixels += SameColour(colour, lighter) ? 1 : 0;
                    sparkles += code >= 128 ? 1 : 0;
                    continue;
                }

                if (SameColour(textures.Albedo[y * size + TerrainArt.Wrap(x - 1, size)], resting))
                {
                    continue;
                }

                strokes++;
                int length = 1;
                while (length < size && SameColour(textures.Albedo[y * size + TerrainArt.Wrap(x + length, size)], resting))
                {
                    length++;
                }

                Assert.That(length, Is.InRange(2, maxLength), $"the stroke at ({x}, {y})");
                int first = textures.Height[i].b;
                int last = textures.Height[y * size + TerrainArt.Wrap(x + length - 1, size)].b;
                Assert.That(last, Is.EqualTo(first), $"the stroke at ({x}, {y}) glints at its ends together");
                if (length % 2 == 1 && length >= 3)
                {
                    int middle = textures.Height[y * size + TerrainArt.Wrap(x + length / 2, size)].b;
                    Assert.That(((middle - first) % 127 + 127) % 127, Is.InRange(spread - 1, spread + 1), $"the stroke at ({x}, {y}) glints from its middle out");
                }
            }
        }

        Assert.That((double)darkerPixels / plain, Is.InRange(darkerShare - 0.08, darkerShare + 0.08), "darker share");
        Assert.That((double)lighterPixels / plain, Is.InRange(lighterShare - 0.08, lighterShare + 0.08), "lighter share");
        Assert.That(strokes, Is.GreaterThan(5));
        Assert.That(sparkles, Is.GreaterThan(0));
    }

    /// <summary>
    /// How many sparkle pixels in a row lie at and to the left of (<paramref name="x"/>, <paramref name="y"/>).
    /// </summary>
    private static int RunLength(Color32[] height, int x, int y, int size)
    {
        int run = 0;
        while (run < size && height[y * size + TerrainArt.Wrap(x - run, size)].b >= 128)
        {
            run++;
        }

        return run;
    }

    /// <summary>
    /// Whether a breathing pixel within two pixels of (<paramref name="x"/>, <paramref name="y"/>) has a glint phase
    /// half a period away from <paramref name="code"/>'s, within a step.
    /// </summary>
    private static bool HasOppositeCourseNearby(Color32[] height, int x, int y, int size, int code)
    {
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                int other = height[TerrainArt.Wrap(y + dy, size) * size + TerrainArt.Wrap(x + dx, size)].b;
                if (other > 0 && other < 128)
                {
                    int difference = ((other - code) % 127 + 127) % 127;
                    if (Math.Abs(difference - 63.5) <= 1.5)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// With straight ripples across the y axis and nothing else drawn, every lit crest pixel lies right over a shaded
    /// trough pixel, so a ripple reads as a ridge lit from above.
    /// </summary>
    [Test]
    public void SandRipples_LayTheirLitCrestsOverTheirShadedTroughs()
    {
        SandRipplesRecipe recipe = (SandRipplesRecipe)CreateRecipe(typeof(SandRipplesRecipe));
        SerializedObject settings = new(recipe);
        settings.FindProperty("grit").floatValue = 0f;
        settings.FindProperty("ripplesAlongX").intValue = 0;
        settings.FindProperty("ripplesAlongY").intValue = 8;
        settings.FindProperty("wobbleWaves").arraySize = 0;
        settings.FindProperty("coverWaves").arraySize = 0;
        settings.FindProperty("speckChance").floatValue = 0f;
        settings.FindProperty("trough").colorValue = Color.black;
        settings.FindProperty("crest").colorValue = Color.white;
        settings.ApplyModifiedPropertiesWithoutUndo();

        TerrainTextures textures = recipe.Generate(Seed);

        int size = textures.Size;
        int crests = 0;
        int troughs = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Color32 colour = textures.Albedo[y * size + x];
                troughs += SameColour(colour, new Color32(0, 0, 0, 255)) ? 1 : 0;
                if (SameColour(colour, new Color32(255, 255, 255, 255)))
                {
                    crests++;
                    Assert.That(SameColour(textures.Albedo[TerrainArt.Wrap(y - 1, size) * size + x], new Color32(0, 0, 0, 255)), Is.True, $"no trough under the crest at ({x}, {y})");
                }
            }
        }

        Assert.That(crests, Is.EqualTo(8 * size));
        Assert.That(troughs, Is.EqualTo(crests));
    }

    /// <summary>
    /// A puddle sinks into the mud: the pixels of its edge in the bank's shade have dry mud above or to their left, and
    /// those catching the light have the puddle above and to their left and dry mud below or to their right.
    /// </summary>
    [Test]
    public void MudPuddles_AreShadedUnderTheirTopLeftBank_AndLitOnTheirBottomRightEdge()
    {
        MudPuddlesRecipe recipe = (MudPuddlesRecipe)CreateRecipe(typeof(MudPuddlesRecipe));
        SerializedObject settings = new(recipe);
        settings.FindProperty("grit").floatValue = 0f;
        settings.FindProperty("reflectionChance").floatValue = 0f;
        settings.FindProperty("pool").colorValue = new Color32(0, 0, 255, 255);
        settings.FindProperty("bankShade").colorValue = new Color32(0, 0, 0, 255);
        settings.FindProperty("wetRim").colorValue = new Color32(255, 255, 255, 255);
        settings.ApplyModifiedPropertiesWithoutUndo();

        TerrainTextures textures = recipe.Generate(Seed);

        int size = textures.Size;
        int shaded = 0;
        int lit = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Color32 colour = textures.Albedo[y * size + x];
                bool puddleAbove = IsPuddle(textures.Albedo[TerrainArt.Wrap(y + 1, size) * size + x]);
                bool puddleLeft = IsPuddle(textures.Albedo[y * size + TerrainArt.Wrap(x - 1, size)]);
                bool puddleBelow = IsPuddle(textures.Albedo[TerrainArt.Wrap(y - 1, size) * size + x]);
                bool puddleRight = IsPuddle(textures.Albedo[y * size + TerrainArt.Wrap(x + 1, size)]);
                if (SameColour(colour, new Color32(0, 0, 0, 255)))
                {
                    shaded++;
                    Assert.That(!puddleAbove || !puddleLeft, Is.True, $"({x}, {y})");
                }
                else if (SameColour(colour, new Color32(255, 255, 255, 255)))
                {
                    lit++;
                    Assert.That(puddleAbove && puddleLeft && (!puddleBelow || !puddleRight), Is.True, $"({x}, {y})");
                }
            }
        }

        Assert.That(shaded, Is.GreaterThan(20));
        Assert.That(lit, Is.GreaterThan(20));

        static bool IsPuddle(Color32 c) => c.g == 0 || (c.r == 255 && c.g == 255 && c.b == 255);
    }

    /// <summary>
    /// Mud's cluster waves gather its puddles: over blocks of two cells square, the share of puddle pixels varies at
    /// least half as much again as with no clustering, and some blocks hold none.
    /// </summary>
    [Test]
    public void MudClusters_GatherThePuddles()
    {
        double even = PuddleShareSpread(0f, out _);
        double clustered = PuddleShareSpread(0.8f, out int bareBlocks);

        Assert.That(clustered, Is.GreaterThan(even * 1.5), $"even {even:F4}, clustered {clustered:F4}");
        Assert.That(bareBlocks, Is.GreaterThan(0));
    }

    /// <summary>
    /// The standard deviation of the share of puddle pixels over blocks of two cells square, in a mud texture of 16
    /// cells for the test seed with <paramref name="clusterAmount"/>, and in <paramref name="bareBlocks"/> how many
    /// blocks hold no puddle.
    /// </summary>
    private double PuddleShareSpread(float clusterAmount, out int bareBlocks)
    {
        MudPuddlesRecipe recipe = (MudPuddlesRecipe)CreateRecipe(typeof(MudPuddlesRecipe));
        SerializedObject settings = new(recipe);
        settings.FindProperty("repeatCells").intValue = 16;
        settings.FindProperty("clusterAmount").floatValue = clusterAmount;
        settings.FindProperty("reflectionChance").floatValue = 0f;
        settings.FindProperty("pool").colorValue = new Color32(0, 0, 255, 255);
        settings.FindProperty("bankShade").colorValue = new Color32(0, 255, 0, 255);
        settings.FindProperty("wetRim").colorValue = new Color32(255, 0, 255, 255);
        settings.ApplyModifiedPropertiesWithoutUndo();

        TerrainTextures textures = recipe.Generate(Seed);

        int size = textures.Size;
        int block = 2 * TerrainStyle.PixelsPerCell;
        int blocks = size / block;
        List<double> shares = new();
        bareBlocks = 0;
        for (int by = 0; by < blocks; by++)
        {
            for (int bx = 0; bx < blocks; bx++)
            {
                int puddles = 0;
                for (int y = by * block; y < (by + 1) * block; y++)
                {
                    for (int x = bx * block; x < (bx + 1) * block; x++)
                    {
                        Color32 colour = textures.Albedo[y * size + x];
                        bool puddle = SameColour(colour, new Color32(0, 0, 255, 255)) || SameColour(colour, new Color32(0, 255, 0, 255)) || SameColour(colour, new Color32(255, 0, 255, 255));
                        puddles += puddle ? 1 : 0;
                    }
                }

                shares.Add((double)puddles / (block * block));
                bareBlocks += puddles == 0 ? 1 : 0;
            }
        }

        double mean = 0.0;
        foreach (double share in shares)
        {
            mean += share / shares.Count;
        }

        double variance = 0.0;
        foreach (double share in shares)
        {
            variance += (share - mean) * (share - mean) / shares.Count;
        }

        return Math.Sqrt(variance);
    }

    /// <summary>
    /// Weeds repeat the grass recipe's ground: away from the weeds' boxes the albedo and the height's red channel are the
    /// grass textures', tiled. Inside a box the sway weight is the same along each row and never falls going up, and
    /// every box lies inside one cell column, so the shader's gust is the same across it; some pixels differ from the
    /// grass, the blades.
    /// </summary>
    [Test]
    public void Weeds_RepeatTheGrass_AndWeighTheirBoxesByRowInsideOneCellColumn()
    {
        GrassTuftsRecipe grass = (GrassTuftsRecipe)CreateRecipe(typeof(GrassTuftsRecipe));
        WeedsRecipe recipe = (WeedsRecipe)CreateRecipe(typeof(WeedsRecipe));
        SerializedObject settings = new(recipe);
        settings.FindProperty("grassRecipe").objectReferenceValue = grass;
        settings.FindProperty("repeatCells").intValue = 8;
        settings.ApplyModifiedPropertiesWithoutUndo();

        TerrainTextures ground = grass.Generate(grass.Seed);
        TerrainTextures textures = recipe.Generate(Seed);

        int size = textures.Size;
        int weighted = 0;
        int blades = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                int from = (y % ground.Size) * ground.Size + x % ground.Size;
                Assert.That(textures.Height[i].r, Is.EqualTo(ground.Height[from].r), $"({x}, {y}) height");
                bool inBox = textures.Height[i].b > 0;
                weighted += inBox ? 1 : 0;
                bool differs = !SameColour(textures.Albedo[i], ground.Albedo[from]);
                blades += differs ? 1 : 0;
                if (!inBox)
                {
                    continue;
                }

                int right = x + 1;
                if (right < size && textures.Height[y * size + right].b > 0)
                {
                    Assert.That(textures.Height[y * size + right].b, Is.EqualTo(textures.Height[i].b), $"({x}, {y}) a row of a box leans together");
                    Assert.That(right / TerrainStyle.PixelsPerCell, Is.EqualTo(x / TerrainStyle.PixelsPerCell), $"({x}, {y}) a box keeps to one cell column");
                }

                int above = TerrainArt.Wrap(y + 1, size) * size + x;
                if (textures.Height[above].b > 0)
                {
                    Assert.That(textures.Height[above].b, Is.GreaterThanOrEqualTo(textures.Height[i].b), $"({x}, {y}) the weight grows going up");
                }
            }
        }

        Assert.That(weighted, Is.GreaterThan(size * size / 50), "the weeds' boxes");
        Assert.That(blades, Is.GreaterThan(size * size / 200), "the blades");
    }

    /// <summary>
    /// A mud ridge is lit from the top left: with no puddles or lumps, every lit ridge pixel has a shaded pixel a row
    /// below and a pixel to its right, and every shaded pixel a lit one a row above and a pixel to its left.
    /// </summary>
    [Test]
    public void MudRidges_CastTheirShadeBelowAndToTheRight()
    {
        MudPuddlesRecipe recipe = (MudPuddlesRecipe)CreateRecipe(typeof(MudPuddlesRecipe));
        SerializedObject settings = new(recipe);
        settings.FindProperty("grit").floatValue = 0f;
        settings.FindProperty("puddleLevel").floatValue = 1f;
        settings.FindProperty("lumpsPerCell").floatValue = 0f;
        settings.FindProperty("ridgeLit").colorValue = new Color32(255, 255, 255, 255);
        settings.FindProperty("ridgeShade").colorValue = new Color32(0, 0, 0, 255);
        settings.ApplyModifiedPropertiesWithoutUndo();

        TerrainTextures textures = recipe.Generate(Seed);

        int size = textures.Size;
        int lit = 0;
        int shaded = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Color32 colour = textures.Albedo[y * size + x];
                if (SameColour(colour, new Color32(255, 255, 255, 255)))
                {
                    lit++;
                    Color32 below = textures.Albedo[TerrainArt.Wrap(y - 1, size) * size + TerrainArt.Wrap(x + 1, size)];
                    Assert.That(SameColour(below, new Color32(0, 0, 0, 255)), Is.True, $"({x}, {y})");
                }
                else if (SameColour(colour, new Color32(0, 0, 0, 255)))
                {
                    shaded++;
                    Color32 above = textures.Albedo[TerrainArt.Wrap(y + 1, size) * size + TerrainArt.Wrap(x - 1, size)];
                    Assert.That(SameColour(above, new Color32(255, 255, 255, 255)), Is.True, $"({x}, {y})");
                }
            }
        }

        Assert.That(lit, Is.EqualTo(shaded));
        Assert.That(lit, Is.GreaterThan(20));
    }

    /// <summary>
    /// The grass recipe's colour patches (the DryGrass material's) shift the hue, not only the lightness: with no grit,
    /// tint or tufts, the ground's red minus green is one value without them, even with patch colours set but no amount,
    /// and spreads over more than 20 steps with them.
    /// </summary>
    [Test]
    public void GrassPatches_ShiftTheHue_AndAreOffByDefault()
    {
        GrassTuftsRecipe recipe = (GrassTuftsRecipe)CreateRecipe(typeof(GrassTuftsRecipe));
        SerializedObject settings = new(recipe);
        settings.FindProperty("grit").floatValue = 0f;
        settings.FindProperty("tuftChance").floatValue = 0f;
        settings.FindProperty("densityWaves").arraySize = 0;
        settings.FindProperty("tintWaves").arraySize = 0;
        settings.FindProperty("patchLow").colorValue = new Color32(20, 140, 40, 255);
        settings.FindProperty("patchHigh").colorValue = new Color32(90, 100, 40, 255);
        settings.ApplyModifiedPropertiesWithoutUndo();
        TerrainTextures plain = recipe.Generate(Seed);

        settings.FindProperty("patchAmount").floatValue = 1f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        TerrainTextures patched = recipe.Generate(Seed);

        Assert.That(HueSpread(plain.Albedo), Is.Zero, "No patches: one hue.");
        Assert.That(HueSpread(patched.Albedo), Is.GreaterThan(20), "Patches move red against green.");
    }

    /// <summary>
    /// The range of red minus green over <paramref name="pixels"/>.
    /// </summary>
    private static int HueSpread(Color32[] pixels)
    {
        int lowest = int.MaxValue;
        int highest = int.MinValue;
        foreach (Color32 pixel in pixels)
        {
            lowest = Math.Min(lowest, pixel.r - pixel.g);
            highest = Math.Max(highest, pixel.r - pixel.g);
        }

        return highest - lowest;
    }

    /// <summary>
    /// Mud's cracks (the Dirt material's) are grooves lit from the top left: with no puddles, ridges or lumps, every
    /// crack pixel touches another (a crack is at least two pixels long) and the pixel below and right of it is crack or
    /// lip, and every lip pixel has a crack pixel above and left of it.
    /// </summary>
    [Test]
    public void MudCracks_AreGroovesWithALitLipBelowAndRight()
    {
        MudPuddlesRecipe recipe = (MudPuddlesRecipe)CreateRecipe(typeof(MudPuddlesRecipe));
        SerializedObject settings = new(recipe);
        settings.FindProperty("grit").floatValue = 0f;
        settings.FindProperty("puddleLevel").floatValue = 1f;
        settings.FindProperty("wetLevel").floatValue = 1f;
        settings.FindProperty("clusterAmount").floatValue = 0f;
        settings.FindProperty("ridgesPerCell").floatValue = 0f;
        settings.FindProperty("lumpsPerCell").floatValue = 0f;
        settings.FindProperty("cracksPerCell").floatValue = 1.5f;
        settings.FindProperty("crackChance").floatValue = 1f;
        settings.FindProperty("crack").colorValue = new Color32(0, 0, 0, 255);
        settings.FindProperty("crackLip").colorValue = new Color32(255, 255, 255, 255);
        settings.ApplyModifiedPropertiesWithoutUndo();

        TerrainTextures textures = recipe.Generate(Seed);

        int size = textures.Size;
        Color32 black = new(0, 0, 0, 255);
        Color32 white = new(255, 255, 255, 255);
        int cracks = 0;
        int lips = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Color32 colour = textures.Albedo[y * size + x];
                if (SameColour(colour, black))
                {
                    cracks++;
                    Color32 below = textures.Albedo[TerrainArt.Wrap(y - 1, size) * size + TerrainArt.Wrap(x + 1, size)];
                    Assert.That(SameColour(below, black) || SameColour(below, white), Is.True, $"({x}, {y}) has a lip");
                    bool joined = false;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            joined |= (dx != 0 || dy != 0) && SameColour(textures.Albedo[TerrainArt.Wrap(y + dy, size) * size + TerrainArt.Wrap(x + dx, size)], black);
                        }
                    }

                    Assert.That(joined, Is.True, $"({x}, {y}) is part of a line");
                }
                else if (SameColour(colour, white))
                {
                    lips++;
                    Color32 above = textures.Albedo[TerrainArt.Wrap(y + 1, size) * size + TerrainArt.Wrap(x - 1, size)];
                    Assert.That(SameColour(above, black), Is.True, $"({x}, {y}) lies below a crack");
                }
            }
        }

        Assert.That(cracks, Is.GreaterThan(100));
        Assert.That(lips, Is.GreaterThan(50));
    }

    /// <summary>
    /// Stones with no jitter lie on a regular grid with straight cell edges: two per cell side (16 px apart) and moved
    /// half a pixel by the seed offset, the mortar is exactly the one-pixel rows and columns every 16 px; without the
    /// offset the edges fall between two pixels and a mortar width of half a pixel leaves no mortar at all.
    /// </summary>
    [Test]
    public void StoneSlabs_OnARegularGrid_HaveOnePixelMortar_WithHalfAPixelSeedOffset()
    {
        StoneCobblesRecipe recipe = (StoneCobblesRecipe)CreateRecipe(typeof(StoneCobblesRecipe));
        Color32 mortar = new(0, 0, 255, 255);
        SerializedObject settings = new(recipe);
        settings.FindProperty("grit").floatValue = 0f;
        settings.FindProperty("jitter").floatValue = 0f;
        settings.FindProperty("stonesPerCell").floatValue = 2f;
        settings.FindProperty("mortarWidth").floatValue = 0.5f;
        settings.FindProperty("mortar").colorValue = mortar;
        settings.FindProperty("seedOffset").floatValue = 0.5f;
        settings.ApplyModifiedPropertiesWithoutUndo();

        TerrainTextures slabs = recipe.Generate(Seed);

        int size = slabs.Size;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool onGrid = x % 16 == 0 || y % 16 == 0;
                Assert.That(SameColour(slabs.Albedo[y * size + x], mortar), Is.EqualTo(onGrid), $"({x}, {y})");
            }
        }

        settings.FindProperty("seedOffset").floatValue = 0f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        TerrainTextures unmoved = recipe.Generate(Seed);
        foreach (Color32 pixel in unmoved.Albedo)
        {
            Assert.That(SameColour(pixel, mortar), Is.False, "No mortar between two pixels.");
        }
    }

    /// <summary>
    /// Every recipe asset of the project that fills a material (only read) still generates, for its seed, exactly the
    /// albedo, cell field and height its material uses, so settings added to a recipe kind (mud's cracks, stone's seed
    /// offset) leave the materials made before them as they were.
    /// </summary>
    [Test]
    public void TheProjectsRecipes_StillGenerateTheirMaterialsTextures()
    {
        int checkedRecipes = 0;
        foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(TerrainMaterialRecipe)}", new[] { "Assets/Settings/Terrain/Recipes" }))
        {
            TerrainMaterialRecipe recipe = AssetDatabase.LoadAssetAtPath<TerrainMaterialRecipe>(AssetDatabase.GUIDToAssetPath(guid));
            if (recipe == null || recipe.Material == null)
            {
                continue;
            }

            TerrainTextures textures = recipe.Generate(recipe.Seed);
            TerrainMaterial material = recipe.Material;
            Assert.That(Differences(Decode(AssetDatabase.GetAssetPath(material.Albedo)), textures.Albedo), Is.Zero, $"{recipe.name} albedo");
            Assert.That(Differences(Decode(AssetDatabase.GetAssetPath(material.Height)), textures.Height), Is.Zero, $"{recipe.name} height");
            Assert.That(material.CellField != null, Is.EqualTo(textures.CellField != null), $"{recipe.name} cell field");
            if (textures.CellField != null)
            {
                Assert.That(Differences(Decode(AssetDatabase.GetAssetPath(material.CellField)), textures.CellField), Is.Zero, $"{recipe.name} cell field");
            }

            checkedRecipes++;
        }

        Assert.That(checkedRecipes, Is.EqualTo(9));
    }

    /// <summary>
    /// Writing the textures to a material points it at them and sets the repeat, cell mode and base colour they were
    /// made for; wherever the albedo shows the base (bare gravel dirt, stone mortar less than half a pixel from a
    /// stone's edge), it is exactly the material's base colour shaded by the cell field's blue channel, as the shader
    /// draws a chip or stone it leaves out.
    /// </summary>
    [TestCaseSource(nameof(RecipeTypes))]
    public void Apply_SetsTheBaseColourTheTexturesBaseIsShadedFrom(Type recipeType)
    {
        TerrainMaterialRecipe recipe = CreateRecipe(recipeType);
        TerrainTextures textures = recipe.Generate(Seed);
        TerrainMaterial material = Track(TerrainMaterial.Create("Sample", null, null, 1, TerrainMaterial.CellMode.None, Color.black, new TerrainStyle.EdgeSettings()));
        Texture2D albedo = ToTexture(textures.Albedo, textures.Size);
        Texture2D field = textures.CellField != null ? ToTexture(textures.CellField, textures.Size) : null;
        Texture2D height = ToTexture(textures.Height, textures.Size);

        TerrainMaterialWriter.Apply(material, textures, albedo, field, height);

        Assert.That(material.Albedo == albedo, Is.True);
        Assert.That(material.CellField == field, Is.True, "A material without chips or stones has no cell field.");
        Assert.That(material.Height == height, Is.True);
        Assert.That(material.RepeatCells, Is.EqualTo(4));
        Assert.That(material.Cells, Is.EqualTo(recipe.CellMode));
        Color32 baseColour = material.BaseColour;
        Assert.That(SameColour(baseColour, textures.BaseColour), Is.True);
        if (textures.GlintColour.HasValue)
        {
            Assert.That(SameColour(material.GlintColour, textures.GlintColour.Value), Is.True, "The glint colour is written too.");
        }

        if (textures.SparkleColour.HasValue)
        {
            Assert.That(SameColour(material.SparkleColour, textures.SparkleColour.Value), Is.True, "The sparkle colour is written too.");
        }

        if (textures.CellField == null)
        {
            return;
        }

        int checkedTexels = 0;
        for (int i = 0; i < textures.Albedo.Length; i++)
        {
            Color32 texel = textures.CellField[i];
            bool showsBase = recipe.CellMode == TerrainMaterial.CellMode.WholeChips ? texel.a == 0 : texel.a < 8;
            if (showsBase)
            {
                checkedTexels++;
                Assert.That(SameColour(textures.Albedo[i], ShadedBase(baseColour, texel.b)), Is.True, $"texel {i % textures.Size}, {i / textures.Size}");
            }
        }

        Assert.That(checkedTexels, Is.GreaterThan(500));
    }

    /// <summary>
    /// Writing textures to a material schedules a rebuild of a renderer drawing it even when nothing on the material
    /// changes, since the textures' pixels may have, and the rebuild succeeds.
    /// </summary>
    [Test]
    public void Apply_HasTheRenderersDrawingTheMaterialRebuild_EvenWhenTheMaterialIsUnchanged()
    {
        GameObject root = Track(new GameObject("TerrainRecipeGrid", typeof(Grid)));
        root.SetActive(false);
        Tilemap tilemap = new GameObject("Gravel", typeof(Tilemap)).GetComponent<Tilemap>();
        tilemap.transform.SetParent(root.transform);
        tilemap.SetTile(Vector3Int.zero, Track(ScriptableObject.CreateInstance<Tile>()));
        TerrainTextures textures = CreateRecipe(typeof(GravelChipsRecipe)).Generate(Seed);
        Texture2D albedo = ToTexture(textures.Albedo, textures.Size);
        Texture2D field = ToTexture(textures.CellField, textures.Size);
        Texture2D height = ToTexture(textures.Height, textures.Size);
        TerrainMaterial material = Track(TerrainMaterial.Create("Gravel", null, null, 4, TerrainMaterial.CellMode.None, Color.gray, new TerrainStyle.EdgeSettings()));
        TerrainMaterialWriter.Apply(material, textures, albedo, field, height);
        TerrainBlendRenderer renderer = root.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(TestTerrainStyle.Create(created), new TerrainBlendRenderer.Layer(tilemap, material));
        root.SetActive(true);
        Assert.That(renderer.ControlMap, Is.Not.Null);
        Assert.That(renderer.IsRebuildScheduled, Is.False);

        TerrainMaterialWriter.Apply(material, textures, albedo, field, height);

        Assert.That(renderer.IsRebuildScheduled, Is.True);
        renderer.RunScheduledRebuild();
        Assert.That(renderer.ControlMap, Is.Not.Null);
    }

    /// <summary>
    /// A recipe of <paramref name="recipeType"/> with its default settings, in memory.
    /// </summary>
    private TerrainMaterialRecipe CreateRecipe(Type recipeType)
    {
        return Track((TerrainMaterialRecipe)ScriptableObject.CreateInstance(recipeType));
    }

    /// <summary>
    /// A new folder <paramref name="name"/> under this test's temporary folder outside the project.
    /// </summary>
    private string TempFolder(string name)
    {
        tempFolder ??= Path.Combine(Path.GetTempPath(), $"TerrainMaterialRecipeTests_{Guid.NewGuid():N}");
        string folder = Path.Combine(tempFolder, name).Replace('\\', '/');
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// A readable RGBA32 texture with sRGB off holding <paramref name="pixels"/>.
    /// </summary>
    private Texture2D ToTexture(Color32[] pixels, int size)
    {
        Texture2D texture = Track(new Texture2D(size, size, TextureFormat.RGBA32, false, true));
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    /// <summary>
    /// The pixels of the PNG at <paramref name="path"/>, rows from the bottom.
    /// </summary>
    private Color32[] Decode(string path)
    {
        Texture2D texture = Track(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
        Assert.That(texture.LoadImage(File.ReadAllBytes(path)), Is.True, path);
        return texture.GetPixels32();
    }

    /// <summary>
    /// The index of the pixel a cell field texel at (<paramref name="x"/>, <paramref name="y"/>) points at.
    /// </summary>
    private static int CentreOf(Color32 texel, int x, int y, int size)
    {
        return TerrainArt.Wrap(y + texel.g - 128, size) * size + TerrainArt.Wrap(x + texel.r - 128, size);
    }

    /// <summary>
    /// The distance from pixel (<paramref name="x"/>, <paramref name="y"/>) to the nearest pixel of another stone,
    /// looking up to 10 pixels away.
    /// </summary>
    private static double NearestOtherStone(int[] stone, int x, int y, int size)
    {
        int own = stone[y * size + x];
        double nearest = double.MaxValue;
        for (int dy = -10; dy <= 10; dy++)
        {
            for (int dx = -10; dx <= 10; dx++)
            {
                if (stone[TerrainArt.Wrap(y + dy, size) * size + TerrainArt.Wrap(x + dx, size)] != own)
                {
                    nearest = Math.Min(nearest, Math.Sqrt(dx * dx + dy * dy));
                }
            }
        }

        return nearest;
    }

    /// <summary>
    /// <paramref name="baseColour"/> shaded by a cell field's blue channel, as the shader shades it.
    /// </summary>
    private static Color32 ShadedBase(Color32 baseColour, byte blue)
    {
        int amount = blue - 128;
        return new Color32((byte)Mathf.Clamp(baseColour.r + amount, 0, 255), (byte)Mathf.Clamp(baseColour.g + amount, 0, 255), (byte)Mathf.Clamp(baseColour.b + amount, 0, 255), 255);
    }

    /// <summary>
    /// Whether two colours have the same red, green and blue.
    /// </summary>
    private static bool SameColour(Color32 a, Color32 b)
    {
        return a.r == b.r && a.g == b.g && a.b == b.b;
    }

    /// <summary>
    /// How many pixels differ between <paramref name="a"/> and <paramref name="b"/> (both null counts as none).
    /// </summary>
    private static int Differences(Color32[] a, Color32[] b)
    {
        if (a == null || b == null)
        {
            return a == b ? 0 : int.MaxValue;
        }

        Assert.That(a.Length, Is.EqualTo(b.Length));
        int count = 0;
        for (int i = 0; i < a.Length; i++)
        {
            count += a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a ? 1 : 0;
        }

        return count;
    }

    /// <summary>
    /// Records <paramref name="createdObject"/> for destruction in <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T createdObject) where T : Object
    {
        created.Add(createdObject);
        return createdObject;
    }
}
