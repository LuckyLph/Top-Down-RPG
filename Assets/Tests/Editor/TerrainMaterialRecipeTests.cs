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
/// with one value per chip or stone, and glint and sparkle phases only in the water and lava recipes and sway weights
/// only in
/// weeds', which repeat the grass recipe's ground and weigh each weed's box by row inside one cell column, draw
/// flowers only when asked, in boxes and in drifts of one colour, and keep that contract with the project materials'
/// sway; the ocean's crests travel down the texture; lava's bubbles swell from their middle with a dark rim, each of
/// its hot patches is one orange of its own within the patches' shift, and the value noise it is drawn from runs on
/// across the texture's edges; and pond water's strokes glint from their middle out while its
/// sparkles sit on plain water, and it shows a bed with pebbles only when asked (shallows), without strokes writes
/// only sparkle phases, and with shines draws spots clear of its strokes and with a slant its strokes diagonally
/// (ice); tufts point up; sand's
/// crests lie over their troughs, and its clumps and sparkles (snow) appear only when asked, clumps lit from the top
/// left; mud's puddles are shaded under their top-left bank and lit on their bottom-right
/// edge, gather in clusters, its ridges cast their shade a row below and a pixel to the right and its cracks are grooves
/// with a lit lip below and right; stones on a regular grid moved half a pixel have one-pixel mortar; planks point
/// every pixel straight across its board, with butt joints on the cell borders staggered from row to row, and are lit
/// along their top rows and left ends; writing the
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
        typeof(GrassTuftsRecipe), typeof(GravelChipsRecipe), typeof(StoneCobblesRecipe), typeof(SandRipplesRecipe), typeof(MudPuddlesRecipe),
        typeof(WaterHighlightsRecipe), typeof(WeedsRecipe), typeof(PlankBoardsRecipe), typeof(OceanWavesRecipe), typeof(LavaBubblesRecipe)
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
        Assert.That(first.CellField != null, Is.EqualTo(recipe.CellMode != TerrainMaterial.CellMode.None || recipe is WeedsRecipe), "a cell field for chips, stones or the ground under weeds");
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
        foreach (string name in new[] { "Grass", "Gravel", "Stone", "DryGrass", "Meadow", "TallGrass", "Dirt", "Plaza", "Shallows", "Planks" })
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
    /// Planks lie in rows of boards 8 px apart (four to a cell) with butt joints on the cell borders: every pixel has
    /// alpha and points straight across its board (no offset along x) at the board's middle row in its own row of
    /// boards, which points at itself; its alpha / 16 is its distance to its board's edge, the nearer of the row's gap
    /// row, at the bottom, the next row's, and the row's joints, which are the columns where the middle row is gap and lie
    /// on cell borders, at least one per row; the gaps show the base colour shaded by the blue channel; and the joints of
    /// neighbouring rows seldom meet.
    /// </summary>
    [Test]
    public void PlankBoards_CellField_PointsAcrossEachBoard_WithJointsOnTheCellBorders()
    {
        TerrainTextures textures = CreateRecipe(typeof(PlankBoardsRecipe)).Generate(Seed);
        int size = textures.Size;
        Color32[] field = textures.CellField;
        const int pitch = 8;
        int rows = size / pitch;
        List<int>[] joints = new List<int>[rows];
        for (int row = 0; row < rows; row++)
        {
            joints[row] = new List<int>();
            for (int x = 0; x < size; x++)
            {
                if (field[(row * pitch + pitch / 2) * size + x].a < 8)
                {
                    Assert.That(x % TerrainStyle.PixelsPerCell, Is.Zero, $"row {row}: a joint at {x} off the cell borders");
                    joints[row].Add(x);
                }
            }

            Assert.That(joints[row], Is.Not.Empty, $"row {row}");
        }

        int gaps = 0;
        for (int y = 0; y < size; y++)
        {
            int row = y / pitch;
            int rowStart = row * pitch;
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                Color32 texel = field[i];
                Assert.That(texel.r, Is.EqualTo(128), $"({x}, {y}) points along its board.");
                Assert.That(texel.g - 128, Is.EqualTo(rowStart + pitch / 2 - y), $"({x}, {y}) points at its board's middle row.");
                Color32 centre = field[CentreOf(texel, x, y, size)];
                Assert.That(centre.r == 128 && centre.g == 128, Is.True, $"({x}, {y})");
                int along = int.MaxValue;
                foreach (int joint in joints[row])
                {
                    along = Math.Min(along, Math.Abs(TerrainArt.WrapDelta(x - joint, size)));
                }

                int border = Math.Min(Math.Min(y - rowStart, rowStart + pitch - y), along);
                Assert.That(texel.a, Is.EqualTo(Mathf.Clamp(border * 16, 1, 255)), $"({x}, {y})");
                if (texel.a < 8)
                {
                    gaps++;
                    Assert.That(SameColour(textures.Albedo[i], ShadedBase(textures.BaseColour, texel.b)), Is.True, $"({x}, {y}) is a gap");
                }
            }
        }

        Assert.That(gaps, Is.GreaterThan(size * rows));
        int meeting = 0;
        for (int row = 0; row < rows; row++)
        {
            foreach (int joint in joints[row])
            {
                if (joints[(row + 1) % rows].Contains(joint))
                {
                    meeting++;
                    break;
                }
            }
        }

        Assert.That(meeting, Is.LessThanOrEqualTo(rows / 4), "Joints of neighbouring rows stagger.");
    }

    /// <summary>
    /// Boards are lit from the top left, as the terrain shader lights the ground: on average a board's top row is
    /// lighter than its bottom row, and its first column after a joint lighter than its last before the next one.
    /// </summary>
    [Test]
    public void PlankBoards_AreLitAlongTheirTopAndLeftEdges()
    {
        TerrainTextures textures = CreateRecipe(typeof(PlankBoardsRecipe)).Generate(Seed);
        int size = textures.Size;
        double top = 0.0;
        double bottom = 0.0;
        double left = 0.0;
        double right = 0.0;
        int ends = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Color32 colour = textures.Albedo[y * size + x];
                double brightness = (colour.r + colour.g + colour.b) / 3.0;
                if (y % 8 == 7)
                {
                    top += brightness;
                }
                else if (y % 8 == 1)
                {
                    bottom += brightness;
                }

                if (y % 8 == 4 && textures.CellField[y * size + x].a < 8)
                {
                    Color32 after = textures.Albedo[y * size + TerrainArt.Wrap(x + 1, size)];
                    Color32 before = textures.Albedo[y * size + TerrainArt.Wrap(x - 1, size)];
                    left += (after.r + after.g + after.b) / 3.0;
                    right += (before.r + before.g + before.b) / 3.0;
                    ends++;
                }
            }
        }

        Assert.That(top, Is.GreaterThan(bottom + 10.0 * size * size / 8), "Top rows are lighter.");
        Assert.That(ends, Is.GreaterThan(5));
        Assert.That(left / ends, Is.GreaterThan(right / ends + 5.0), "Left ends are lighter.");
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
                if (recipeType == typeof(WeedsRecipe))
                {
                    Assert.That(height[i].g, Is.LessThan(32), $"({x}, {y}) a weed's row from under its foot, plus one, or 0");
                }
                else if (field == null || field[i].a == 0)
                {
                    Assert.That(height[i].g, Is.EqualTo(128), $"({x}, {y})");
                }
                else
                {
                    Assert.That(height[i].g, Is.EqualTo(height[CentreOf(field[i], x, y, size)].g), $"({x}, {y})");
                }

                if (recipeType != typeof(WaterHighlightsRecipe) && recipeType != typeof(WeedsRecipe) && recipeType != typeof(OceanWavesRecipe) && recipeType != typeof(LavaBubblesRecipe))
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
    /// Pond water shows no bed by default (deep water). With a bed share (the Shallows material's), about that share of
    /// the pixels off the strokes shows the bed tone, the strokes still glint over it, and pebbles lie only on plain
    /// water, never on a stroke or a sparkle; every pixel the bed and pebbles leave alone keeps its colour and phase.
    /// </summary>
    [Test]
    public void WaterHighlights_ShowTheirBed_OnlyWhenAsked()
    {
        WaterHighlightsRecipe recipe = (WaterHighlightsRecipe)CreateRecipe(typeof(WaterHighlightsRecipe));
        SerializedObject settings = new(recipe);
        Color32 bed = settings.FindProperty("bed").colorValue;
        Color32 pebble = settings.FindProperty("pebble").colorValue;
        Color32 resting = settings.FindProperty("resting").colorValue;
        TerrainTextures deep = recipe.Generate(Seed);

        settings.FindProperty("bedShare").floatValue = 0.3f;
        settings.FindProperty("pebblesPerCell").floatValue = 1f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        TerrainTextures shallow = recipe.Generate(Seed);

        int bedPixels = 0;
        int plain = 0;
        int pebbles = 0;
        int strokes = 0;
        for (int i = 0; i < deep.Albedo.Length; i++)
        {
            Assert.That(SameColour(deep.Albedo[i], bed) || SameColour(deep.Albedo[i], pebble), Is.False, $"pixel {i}: deep water shows a bed");
            Color32 colour = shallow.Albedo[i];
            int code = shallow.Height[i].b;
            if (SameColour(colour, resting))
            {
                strokes++;
                Assert.That(code > 0 && code < 128, Is.True, $"pixel {i}: a stroke over the bed glints");
                continue;
            }

            plain++;
            bedPixels += SameColour(colour, bed) ? 1 : 0;
            if (SameColour(colour, pebble))
            {
                pebbles++;
                Assert.That(code, Is.Zero, $"pixel {i}: a pebble on a stroke or a sparkle");
            }
        }

        Assert.That((double)bedPixels / plain, Is.InRange(0.22, 0.38), "bed share");
        Assert.That(strokes, Is.GreaterThan(5));
        Assert.That(pebbles, Is.GreaterThan(0));
    }

    /// <summary>
    /// Pond water with a stroke chance and density amount of 0 has no strokes, so its height holds only sparkle phases
    /// (or none): a material that sparkles without glints, as snow does, needs no other setting. Its sparkles sit where
    /// they sit with strokes, on the pixels no stroke took.
    /// </summary>
    [Test]
    public void WaterHighlights_WithoutStrokes_WriteOnlySparklePhases()
    {
        WaterHighlightsRecipe recipe = (WaterHighlightsRecipe)CreateRecipe(typeof(WaterHighlightsRecipe));
        SerializedObject settings = new(recipe);
        Color32 resting = settings.FindProperty("resting").colorValue;
        TerrainTextures withStrokes = recipe.Generate(Seed);
        settings.FindProperty("strokeChance").floatValue = 0f;
        settings.FindProperty("densityAmount").floatValue = 0f;
        settings.ApplyModifiedPropertiesWithoutUndo();

        TerrainTextures sparkling = recipe.Generate(Seed);

        int sparkles = 0;
        for (int i = 0; i < sparkling.Albedo.Length; i++)
        {
            int code = sparkling.Height[i].b;
            Assert.That(code > 0 && code < 128, Is.False, $"pixel {i} holds a glint phase");
            Assert.That(SameColour(sparkling.Albedo[i], resting), Is.False, $"pixel {i} is a stroke");
            if (withStrokes.Height[i].b >= 128)
            {
                Assert.That(code, Is.EqualTo(withStrokes.Height[i].b), $"pixel {i}: a sparkle moved");
            }

            sparkles += code >= 128 ? 1 : 0;
        }

        Assert.That(sparkles, Is.GreaterThan(0));
    }

    /// <summary>
    /// Pond water has no shines by default: the shine colours change no pixel. With shines (the Ice material's), every
    /// core pixel has its four neighbours in the shine, the cores lie towards the light, with more edge below and right
    /// of them than above and left, and no shine pixel glints, sparkles or lies within a pixel of a stroke.
    /// </summary>
    [Test]
    public void WaterHighlights_Shines_AreSpotsWithACoreTowardsTheLight_ClearOfTheStrokesAndSparkles()
    {
        WaterHighlightsRecipe recipe = (WaterHighlightsRecipe)CreateRecipe(typeof(WaterHighlightsRecipe));
        SerializedObject settings = new(recipe);
        Color32 resting = settings.FindProperty("resting").colorValue;
        TerrainTextures water = recipe.Generate(Seed);
        Color32 black = new(0, 0, 0, 255);
        Color32 white = new(255, 255, 255, 255);
        settings.FindProperty("shine").colorValue = black;
        settings.FindProperty("shineEdge").colorValue = white;
        settings.ApplyModifiedPropertiesWithoutUndo();
        Assert.That(Differences(recipe.Generate(Seed).Albedo, water.Albedo), Is.Zero, "the shine colours without shines");

        settings.FindProperty("shinesPerCell").floatValue = 1.5f;
        settings.FindProperty("shineChance").floatValue = 1f;
        settings.FindProperty("shineRadius").vector2Value = new Vector2(2.5f, 3.5f);
        settings.ApplyModifiedPropertiesWithoutUndo();
        TerrainTextures shiny = recipe.Generate(Seed);

        int size = shiny.Size;
        int cores = 0;
        int edges = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Color32 colour = shiny.Albedo[y * size + x];
                bool core = SameColour(colour, black);
                if (!core && !SameColour(colour, white))
                {
                    continue;
                }

                Assert.That(shiny.Height[y * size + x].b, Is.Zero, $"({x}, {y}) glints or sparkles");
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        Color32 near = shiny.Albedo[TerrainArt.Wrap(y + dy, size) * size + TerrainArt.Wrap(x + dx, size)];
                        Assert.That(SameColour(near, resting), Is.False, $"({x}, {y}) touches a stroke");
                        if (core && Math.Abs(dx) + Math.Abs(dy) == 1)
                        {
                            Assert.That(SameColour(near, black) || SameColour(near, white), Is.True, $"({x}, {y}) is a core inside its shine");
                        }
                    }
                }

                if (core)
                {
                    cores++;
                }
                else
                {
                    edges++;
                }
            }
        }

        Assert.That(cores, Is.GreaterThan(30));
        Assert.That(edges, Is.GreaterThan(cores));
        int edgesBelowRight = 0;
        int edgesAboveLeft = 0;
        for (int y = 1; y < size - 1; y++)
        {
            for (int x = 1; x < size - 1; x++)
            {
                if (!SameColour(shiny.Albedo[y * size + x], black))
                {
                    continue;
                }

                edgesAboveLeft += SameColour(shiny.Albedo[(y + 1) * size + x - 1], white) ? 1 : 0;
                edgesBelowRight += SameColour(shiny.Albedo[(y - 1) * size + x + 1], white) ? 1 : 0;
            }
        }

        Assert.That(edgesBelowRight, Is.GreaterThan(edgesAboveLeft), "more edge below and right of the cores than above and left");
    }

    /// <summary>
    /// With a slant of 1 (the Ice material's) and no companions, every stroke is a diagonal rising to the right: no
    /// stroke pixel has another beside it on its row, and each has one up and right or down and left of it.
    /// </summary>
    [Test]
    public void WaterHighlights_SlantedStrokes_RiseDiagonally()
    {
        WaterHighlightsRecipe recipe = (WaterHighlightsRecipe)CreateRecipe(typeof(WaterHighlightsRecipe));
        SerializedObject settings = new(recipe);
        Color32 resting = settings.FindProperty("resting").colorValue;
        settings.FindProperty("slant").floatValue = 1f;
        settings.FindProperty("companionChance").floatValue = 0f;
        settings.ApplyModifiedPropertiesWithoutUndo();

        TerrainTextures slanted = recipe.Generate(Seed);

        int size = slanted.Size;
        int strokePixels = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (!SameColour(slanted.Albedo[y * size + x], resting))
                {
                    continue;
                }

                strokePixels++;
                bool left = SameColour(slanted.Albedo[y * size + TerrainArt.Wrap(x - 1, size)], resting);
                bool right = SameColour(slanted.Albedo[y * size + TerrainArt.Wrap(x + 1, size)], resting);
                bool upRight = SameColour(slanted.Albedo[TerrainArt.Wrap(y + 1, size) * size + TerrainArt.Wrap(x + 1, size)], resting);
                bool downLeft = SameColour(slanted.Albedo[TerrainArt.Wrap(y - 1, size) * size + TerrainArt.Wrap(x - 1, size)], resting);
                Assert.That(left || right, Is.False, $"({x}, {y}) has a stroke pixel beside it on its row");
                Assert.That(upRight || downLeft, Is.True, $"({x}, {y}) is not on a diagonal");
            }
        }

        Assert.That(strokePixels, Is.GreaterThan(20));
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
    /// Sand has no clumps or sparkles by default: their colours change no pixel and its height holds no phase. With
    /// clumps and sparkles (the Snow material's), on flat sand: in every clump the lit pixels lie up and to the left of
    /// the shaded ones (their mean position against the shade's points towards the light), clumps are kept apart, the height holds
    /// only sparkle phases and none on a clump's shade, and the textures bring the sparkle colour.
    /// </summary>
    [Test]
    public void SandClumps_AreLitFromTheTopLeft_AndSparklesOnlyWhenAsked()
    {
        SandRipplesRecipe recipe = (SandRipplesRecipe)CreateRecipe(typeof(SandRipplesRecipe));
        SerializedObject settings = new(recipe);
        TerrainTextures sand = recipe.Generate(Seed);
        Color32 lit = new(255, 255, 255, 255);
        Color32 middle = new(128, 128, 128, 255);
        Color32 shade = new(0, 0, 0, 255);
        settings.FindProperty("clumpLit").colorValue = lit;
        settings.FindProperty("clump").colorValue = middle;
        settings.FindProperty("clumpShade").colorValue = shade;
        settings.ApplyModifiedPropertiesWithoutUndo();
        TerrainTextures unchanged = recipe.Generate(Seed);
        Assert.That(Differences(unchanged.Albedo, sand.Albedo), Is.Zero, "the clump colours without clumps");
        Assert.That(Differences(unchanged.Height, sand.Height), Is.Zero, "no sparkles by default");
        Assert.That(unchanged.SparkleColour.HasValue, Is.False);

        settings.FindProperty("grit").floatValue = 0f;
        settings.FindProperty("coverLevel").floatValue = 1f;
        settings.FindProperty("speckChance").floatValue = 0f;
        settings.FindProperty("clumpsPerCell").floatValue = 1.5f;
        settings.FindProperty("clumpChance").floatValue = 1f;
        settings.FindProperty("clumpStretch").floatValue = 2f;
        settings.FindProperty("sparklesPerCell").floatValue = 1f;
        settings.ApplyModifiedPropertiesWithoutUndo();

        TerrainTextures snow = recipe.Generate(Seed);

        int size = snow.Size;
        int litCount = 0;
        int shadeCount = 0;
        int sparkles = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Color32 colour = snow.Albedo[y * size + x];
                int code = snow.Height[y * size + x].b;
                Assert.That(code > 0 && code < 128, Is.False, $"({x}, {y}) holds a glint phase");
                sparkles += code >= 128 ? 1 : 0;
                if (SameColour(colour, lit))
                {
                    litCount++;
                }
                else if (SameColour(colour, shade))
                {
                    Assert.That(code, Is.Zero, $"({x}, {y}): a sparkle on a clump's shade");
                    shadeCount++;
                }
            }
        }

        Assert.That(litCount, Is.GreaterThan(20));
        Assert.That(shadeCount, Is.GreaterThan(20));
        Assert.That(sparkles, Is.GreaterThan(0));
        Assert.That(snow.SparkleColour.HasValue, Is.True);
        HashSet<Vector3Int> clumpPixels = new();
        for (int i = 0; i < snow.Albedo.Length; i++)
        {
            Color32 colour = snow.Albedo[i];
            if (SameColour(colour, lit) || SameColour(colour, middle) || SameColour(colour, shade))
            {
                clumpPixels.Add(new Vector3Int(i % size, i / size, 0));
            }
        }

        int clumps = 0;
        HashSet<Vector3Int> seen = new();
        foreach (Vector3Int pixel in clumpPixels)
        {
            if (!seen.Add(pixel))
            {
                continue;
            }

            clumps++;
            Queue<Vector3Int> queue = new();
            queue.Enqueue(pixel);
            Vector2 litInClump = Vector2.zero;
            Vector2 shadeInClump = Vector2.zero;
            int lits = 0;
            int shades = 0;
            while (queue.Count > 0)
            {
                Vector3Int cell = queue.Dequeue();
                Color32 colour = snow.Albedo[cell.y * size + cell.x];
                Vector2 offset = new(TerrainArt.WrapDelta(cell.x - pixel.x, size), TerrainArt.WrapDelta(cell.y - pixel.y, size));
                if (SameColour(colour, lit))
                {
                    litInClump += offset;
                    lits++;
                }
                else if (SameColour(colour, shade))
                {
                    shadeInClump += offset;
                    shades++;
                }

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        Vector3Int next = new(TerrainArt.Wrap(cell.x + dx, size), TerrainArt.Wrap(cell.y + dy, size), 0);
                        if (clumpPixels.Contains(next) && seen.Add(next))
                        {
                            queue.Enqueue(next);
                        }
                    }
                }
            }

            if (lits > 0 && shades > 0)
            {
                Vector2 towardsLight = litInClump / lits - shadeInClump / shades;
                Assert.That(towardsLight.x * (float)TerrainArt.LightX + towardsLight.y * (float)TerrainArt.LightY, Is.GreaterThan(0f), $"the clump at {pixel} is lit away from the light");
            }
        }

        Assert.That(clumps, Is.GreaterThan(10));
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
    /// grass textures', tiled, and the cell field is that ground everywhere, the weeds left out. Inside a box the sway
    /// weight is the same along each row and never falls going up, and every box lies inside one cell column, so the
    /// shader's gust is the same across it; the height's green channel counts a box's rows from the shadow row (1) up, a
    /// row up being one more, and is 0 outside every box, and the field's alpha is a box's height in rows above its base
    /// (255 outside); some pixels differ from the grass, the blades.
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

        (int weighted, int blades) = AssertClumpContract(textures, ground, 0f);

        Assert.That(weighted, Is.GreaterThan(textures.Size * textures.Size / 50), "the weeds' boxes");
        Assert.That(blades, Is.GreaterThan(textures.Size * textures.Size / 200), "the blades");
    }

    /// <summary>
    /// Flowers are off by default: flower colours and colour waves set without a flower chance change no pixel. With a
    /// chance, some kept spots become flower clusters whose petals and hearts all lie inside a box, above its base, at
    /// the full sway weight, so every head leans whole, and keep the clump contract; clusters take the colour of the colour waves' band where they stand,
    /// so with one wave once across the texture and two colours, each colour fills many columns on its own and only the
    /// columns around the two band borders hold both.
    /// </summary>
    [Test]
    public void Flowers_StandInBoxesInDriftsOfOneColour_AndAreOffByDefault()
    {
        GrassTuftsRecipe grass = (GrassTuftsRecipe)CreateRecipe(typeof(GrassTuftsRecipe));
        WeedsRecipe plain = (WeedsRecipe)CreateRecipe(typeof(WeedsRecipe));
        SetFlowerGround(plain, grass);
        TerrainTextures unflowered = plain.Generate(Seed);

        WeedsRecipe recipe = (WeedsRecipe)CreateRecipe(typeof(WeedsRecipe));
        SetFlowerGround(recipe, grass);
        Color32 red = new(255, 0, 0, 255);
        Color32 blue = new(0, 0, 255, 255);
        Color32 white = new(255, 255, 255, 255);
        SerializedObject settings = new(recipe);
        settings.FindProperty("flowerStems").vector2IntValue = new Vector2Int(1, 3);
        settings.FindProperty("stemSpread").intValue = 2;
        SerializedProperty colours = settings.FindProperty("flowerColours");
        colours.arraySize = 2;
        colours.GetArrayElementAtIndex(0).FindPropertyRelative("petal").colorValue = red;
        colours.GetArrayElementAtIndex(0).FindPropertyRelative("heart").colorValue = white;
        colours.GetArrayElementAtIndex(1).FindPropertyRelative("petal").colorValue = blue;
        colours.GetArrayElementAtIndex(1).FindPropertyRelative("heart").colorValue = white;
        SerializedProperty waves = settings.FindProperty("flowerColourWaves");
        waves.arraySize = 1;
        waves.GetArrayElementAtIndex(0).FindPropertyRelative("x").intValue = 1;
        waves.GetArrayElementAtIndex(0).FindPropertyRelative("y").intValue = 0;
        waves.GetArrayElementAtIndex(0).FindPropertyRelative("amplitude").floatValue = 1f;
        settings.FindProperty("flowerColourMix").floatValue = 0f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        TerrainTextures withoutChance = recipe.Generate(Seed);
        Assert.That(Differences(unflowered.Albedo, withoutChance.Albedo), Is.Zero, "No flower chance: the albedo of a recipe without flowers.");
        Assert.That(Differences(unflowered.Height, withoutChance.Height), Is.Zero, "No flower chance: the height of a recipe without flowers.");

        settings.FindProperty("flowerChance").floatValue = 0.8f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        TerrainTextures textures = recipe.Generate(Seed);
        AssertClumpContract(textures, grass.Generate(grass.Seed), 0f);

        int size = textures.Size;
        int[] redColumns = new int[size];
        int[] blueColumns = new int[size];
        int hearts = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Color32 pixel = textures.Albedo[y * size + x];
                bool isRed = SameColour(pixel, red);
                bool isBlue = SameColour(pixel, blue);
                bool heart = SameColour(pixel, white);
                if (!isRed && !isBlue && !heart)
                {
                    continue;
                }

                Assert.That(textures.Height[y * size + x].g, Is.GreaterThan(2), $"({x}, {y}) a flower stands above its box's base");
                Assert.That(textures.Height[y * size + x].b, Is.EqualTo(255), $"({x}, {y}) a head leans whole");
                hearts += heart ? 1 : 0;
                redColumns[x] += isRed ? 1 : 0;
                blueColumns[x] += isBlue ? 1 : 0;
            }
        }

        int mixed = 0;
        int redOnly = 0;
        int blueOnly = 0;
        for (int band = 0; band < size; band += 8)
        {
            int reds = 0;
            int blues = 0;
            for (int x = band; x < band + 8; x++)
            {
                reds += redColumns[x];
                blues += blueColumns[x];
            }

            mixed += reds > 0 && blues > 0 ? 1 : 0;
            redOnly += reds > 0 && blues == 0 ? 1 : 0;
            blueOnly += blues > 0 && reds == 0 ? 1 : 0;
        }

        Assert.That(hearts, Is.GreaterThan(20), "Flower hearts.");
        Assert.That(redOnly, Is.GreaterThan(8), "A drift of red flowers.");
        Assert.That(blueOnly, Is.GreaterThan(8), "A drift of blue flowers.");
        Assert.That(mixed, Is.LessThanOrEqualTo(6), "Only the bands around the two borders mix.");
    }

    /// <summary>
    /// Gives <paramref name="recipe"/> <paramref name="grass"/> as its ground, an 8-cell repeat and four weed spots per
    /// cell side.
    /// </summary>
    private static void SetFlowerGround(WeedsRecipe recipe, GrassTuftsRecipe grass)
    {
        SerializedObject settings = new(recipe);
        settings.FindProperty("grassRecipe").objectReferenceValue = grass;
        settings.FindProperty("repeatCells").intValue = 8;
        settings.FindProperty("weedsPerCell").floatValue = 4f;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// The project's swaying recipes (grass, dry grass, the meadow and tall grass, only read) generate the same textures
    /// twice and keep the clump contract with their materials' sway: a sway margin of at least the material's sway
    /// amplitude, which is at most <see cref="TerrainMaterial.MaxSwayAmplitude"/>, every box inside one cell column, and
    /// every weed pixel still inside its box's row when it leans by its row's lean at the full amplitude, so a leaning tip
    /// is never cut. All four sway in one wind (the grass's period and gust length); tall grass is taller than the grass
    /// and sways further.
    /// </summary>
    [TestCase("Recipe_Grass")]
    [TestCase("Recipe_DryGrass")]
    [TestCase("Recipe_Meadow")]
    [TestCase("Recipe_TallGrass")]
    public void TheProjectsSwayingRecipes_KeepTheClumpContract_WithTheirMaterialsSway(string recipeName)
    {
        WeedsRecipe recipe = AssetDatabase.LoadAssetAtPath<WeedsRecipe>($"Assets/Settings/Terrain/Recipes/{recipeName}.asset");
        TerrainMaterial material = recipe.Material;
        SerializedObject settings = new(recipe);
        GrassTuftsRecipe grass = (GrassTuftsRecipe)settings.FindProperty("grassRecipe").objectReferenceValue;
        Assert.That(material.Sways, Is.True, recipeName);
        Assert.That(material.SwayAmplitude, Is.LessThanOrEqualTo(TerrainMaterial.MaxSwayAmplitude));
        Assert.That(settings.FindProperty("swayMargin").intValue, Is.GreaterThanOrEqualTo(Mathf.CeilToInt(material.SwayAmplitude)), "The margin holds the lean.");

        TerrainTextures textures = recipe.Generate(recipe.Seed);
        TerrainTextures again = recipe.Generate(recipe.Seed);
        Assert.That(Differences(textures.Albedo, again.Albedo) + Differences(textures.Height, again.Height) + Differences(textures.CellField, again.CellField), Is.Zero, "The same seed, the same textures.");
        (int weighted, int blades) = AssertClumpContract(textures, grass.Generate(grass.Seed), material.SwayAmplitude);
        Assert.That(weighted, Is.GreaterThan(textures.Size * textures.Size / 50), "the boxes");
        Assert.That(blades, Is.GreaterThan(textures.Size * textures.Size / 200), "the blades");

        WeedsRecipe grassRecipe = AssetDatabase.LoadAssetAtPath<WeedsRecipe>("Assets/Settings/Terrain/Recipes/Recipe_Grass.asset");
        TerrainMaterial grassMaterial = grassRecipe.Material;
        Assert.That(material.SwayPeriod, Is.EqualTo(grassMaterial.SwayPeriod), "One wind.");
        Assert.That(material.GustLength, Is.EqualTo(grassMaterial.GustLength), "One wind.");
        Assert.That(material.FramesPerSecond, Is.EqualTo(grassMaterial.FramesPerSecond), "One wind.");
        if (recipeName == "Recipe_TallGrass")
        {
            Assert.That(material.SwayAmplitude, Is.GreaterThan(grassMaterial.SwayAmplitude));
            Assert.That(settings.FindProperty("bladeHeight").vector2IntValue.y, Is.GreaterThan(new SerializedObject(grassRecipe).FindProperty("bladeHeight").vector2IntValue.y));
        }
    }

    /// <summary>
    /// Asserts the clump contract a swaying recipe's <paramref name="textures"/> keep over their
    /// <paramref name="ground"/> recipe's: away from the boxes the albedo and the height's red channel are the ground's,
    /// tiled, and the cell field is that ground everywhere; inside a box the sway weight is the same along each row and
    /// never falls going up, and every box lies inside one cell column; the height's green channel counts a box's rows
    /// from the shadow row (1) up, a row up being one more, and is 0 outside every box, and the field's alpha is a box's
    /// height in rows above its base (255 outside). With an <paramref name="amplitude"/> above 0, every pixel a weed
    /// covers (differing from the ground) leans by its row's lean at that amplitude to a pixel of the same row of its
    /// box. Returns how many pixels are weighted and how many differ from the ground.
    /// </summary>
    private static (int Weighted, int Blades) AssertClumpContract(TerrainTextures textures, TerrainTextures ground, float amplitude)
    {
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
                Assert.That(SameColour(textures.CellField[i], ground.Albedo[from]), Is.True, $"({x}, {y}) the field is the ground");
                byte row = textures.Height[i].g;
                Assert.That(textures.CellField[i].a, row > 0 ? Is.InRange(2, 30) : Is.EqualTo(255), $"({x}, {y}) the field's alpha is a box's height");
                if (row > 2)
                {
                    Assert.That(row - 2, Is.LessThanOrEqualTo(textures.CellField[i].a), $"({x}, {y}) no row of a box lies above its top");
                }

                if (textures.Height[i].b > 0)
                {
                    Assert.That(row, Is.GreaterThan(2), $"({x}, {y}) a leaning pixel lies above its weed's foot");
                }

                if (row > 1)
                {
                    Assert.That(textures.Height[TerrainArt.Wrap(y - 1, size) * size + x].g, Is.EqualTo(row - 1), $"({x}, {y}) the row below is one less");
                }

                if (row == 0)
                {
                    Assert.That(SameColour(textures.Albedo[i], ground.Albedo[from]), Is.True, $"({x}, {y}) outside the boxes the albedo is the ground");
                }

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

                if (differs && amplitude > 0f)
                {
                    float weight = textures.Height[i].b / 255f;
                    int lean = Mathf.RoundToInt(Mathf.Round(amplitude) * weight * weight);
                    Assert.That(x + lean, Is.LessThan(size), $"({x}, {y}) leans inside the texture");
                    Assert.That(textures.Height[y * size + x + lean].b, Is.EqualTo(textures.Height[i].b), $"({x}, {y}) a leaning tip stays in its box");
                }
            }
        }

        return (weighted, blades);
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
    /// The ocean's crests travel down the texture: its albedo holds only its three tones, the textures bring the crest
    /// colour as their glint colour, and a fair share of the pixels carry glint phases, which, between a pixel and the
    /// one above it on the same crest family, grow by one period over the crest spacing, so the band of pixels lit at a
    /// time moves down by the spacing each glint period, the same way in every column. Sparkles are few.
    /// </summary>
    [Test]
    public void OceanWaves_CrestsTravelDownTheTexture()
    {
        OceanWavesRecipe recipe = (OceanWavesRecipe)CreateRecipe(typeof(OceanWavesRecipe));
        SerializedObject settings = new(recipe);
        Color32 water = settings.FindProperty("water").colorValue;
        Color32 darker = settings.FindProperty("darker").colorValue;
        Color32 lighter = settings.FindProperty("lighter").colorValue;
        Color32 crest = settings.FindProperty("crest").colorValue;
        int crestsAcross = settings.FindProperty("crestsAcross").intValue;

        TerrainTextures textures = recipe.Generate(Seed);

        int size = textures.Size;
        double step = (double)crestsAcross / size;
        Assert.That(textures.GlintColour.HasValue && SameColour(textures.GlintColour.Value, crest), Is.True);
        int crestPixels = 0;
        int sparkles = 0;
        int pairs = 0;
        int travelling = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                Color32 colour = textures.Albedo[i];
                Assert.That(SameColour(colour, water) || SameColour(colour, darker) || SameColour(colour, lighter), Is.True, $"({x}, {y}) is none of the tones");
                int code = textures.Height[i].b;
                if (code >= 128)
                {
                    sparkles++;
                    continue;
                }

                if (code == 0)
                {
                    continue;
                }

                crestPixels++;
                int above = textures.Height[TerrainArt.Wrap(y + 1, size) * size + x].b;
                if (above == 0 || above >= 128)
                {
                    continue;
                }

                pairs++;
                double grown = TerrainArt.Wrap((above - code) / 127.0, 1);
                travelling += Math.Abs(grown - step) <= 1.5 / 127.0 ? 1 : 0;
            }
        }

        Assert.That(crestPixels, Is.InRange(size * size / 5, size * size * 4 / 5), "crests cover a fair share of the water");
        Assert.That(travelling, Is.GreaterThan(pairs * 9 / 10), "a crest's phase grows by the spacing up the texture");
        Assert.That(sparkles, Is.LessThan(size * size / 100));
    }

    /// <summary>
    /// The ocean's arch and fade are off by default and change only its crests: with an arch, the albedo and the set of
    /// crest pixels stay as they were and some crest phases move (a crest's middle runs ahead); with a fade, the albedo
    /// stays as it was and fewer pixels carry crests, every one of them a crest pixel without it.
    /// </summary>
    [Test]
    public void OceanWaves_ArchAndFade_ChangeOnlyTheCrests()
    {
        OceanWavesRecipe plain = (OceanWavesRecipe)CreateRecipe(typeof(OceanWavesRecipe));
        OceanWavesRecipe arched = (OceanWavesRecipe)CreateRecipe(typeof(OceanWavesRecipe));
        OceanWavesRecipe faded = (OceanWavesRecipe)CreateRecipe(typeof(OceanWavesRecipe));
        SerializedObject defaults = new(plain);
        Assert.That(defaults.FindProperty("arch").floatValue, Is.Zero);
        Assert.That(defaults.FindProperty("fade").floatValue, Is.Zero);
        SerializedObject archSettings = new(arched);
        archSettings.FindProperty("arch").floatValue = 8f;
        archSettings.ApplyModifiedPropertiesWithoutUndo();
        SerializedObject fadeSettings = new(faded);
        fadeSettings.FindProperty("fade").floatValue = 0.3f;
        fadeSettings.ApplyModifiedPropertiesWithoutUndo();

        TerrainTextures before = plain.Generate(Seed);
        TerrainTextures withArch = arched.Generate(Seed);
        TerrainTextures withFade = faded.Generate(Seed);

        Assert.That(Differences(before.Albedo, withArch.Albedo), Is.Zero);
        Assert.That(Differences(before.Albedo, withFade.Albedo), Is.Zero);
        int moved = 0;
        int crests = 0;
        int fadedCrests = 0;
        for (int i = 0; i < before.Height.Length; i++)
        {
            bool crest = before.Height[i].b > 0 && before.Height[i].b < 128;
            bool archedCrest = withArch.Height[i].b > 0 && withArch.Height[i].b < 128;
            bool fadedCrest = withFade.Height[i].b > 0 && withFade.Height[i].b < 128;
            Assert.That(archedCrest, Is.EqualTo(crest), $"pixel {i}");
            Assert.That(!fadedCrest || crest, Is.True, $"pixel {i}");
            crests += crest ? 1 : 0;
            fadedCrests += fadedCrest ? 1 : 0;
            moved += crest && withArch.Height[i].b != before.Height[i].b ? 1 : 0;
        }

        Assert.That(moved, Is.GreaterThan(crests / 4), "the arch moves the crests' phases");
        Assert.That(fadedCrests, Is.LessThan(crests), "the fade breaks up the crests' ends");
        Assert.That(fadedCrests, Is.GreaterThan(crests / 4));
    }

    /// <summary>
    /// Lava's bubbles swell from their middle: each bubble (a group of pixels with phases, apart from every other) has
    /// its latest phase at its middle and phases that fall behind with the distance from it, by the swell delay per
    /// pixel, so it swells from a point; a large bubble has a rim of sparkle phases, on the same timing, along its
    /// outside only; the textures bring the bubble and rim colours as the glint and sparkle colours, and bubbles leave
    /// the albedo as it is.
    /// </summary>
    [Test]
    public void LavaBubbles_SwellFromTheirMiddle_WithADarkRim()
    {
        LavaBubblesRecipe recipe = (LavaBubblesRecipe)CreateRecipe(typeof(LavaBubblesRecipe));
        SerializedObject settings = new(recipe);
        double delay = settings.FindProperty("swellDelay").floatValue;
        Color32 bubbleColour = settings.FindProperty("bubble").colorValue;
        Color32 rimColour = settings.FindProperty("bubbleRim").colorValue;
        TerrainTextures textures = recipe.Generate(Seed);
        settings.FindProperty("bubblesPerCell").floatValue = 0f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        TerrainTextures still = recipe.Generate(Seed);

        Assert.That(SameColour(textures.GlintColour.Value, bubbleColour), Is.True);
        Assert.That(SameColour(textures.SparkleColour.Value, rimColour), Is.True);
        Assert.That(Differences(textures.Albedo, still.Albedo), Is.Zero, "bubbles leave the albedo as it is");
        int size = textures.Size;
        int[] bubble = new int[size * size];
        for (int i = 0; i < bubble.Length; i++)
        {
            bubble[i] = -1;
        }

        int bubbles = 0;
        int rimmed = 0;
        for (int start = 0; start < bubble.Length; start++)
        {
            if (textures.Height[start].b == 0 || bubble[start] >= 0)
            {
                continue;
            }

            List<int> pixels = Group(textures.Height, size, start, bubble, bubbles);
            double sx = 0.0;
            double sy = 0.0;
            int ox = pixels[0] % size;
            int oy = pixels[0] / size;
            foreach (int i in pixels)
            {
                sx += TerrainArt.WrapDelta(i % size - ox, size);
                sy += TerrainArt.WrapDelta(i / size - oy, size);
            }

            double cx = ox + sx / pixels.Count;
            double cy = oy + sy / pixels.Count;
            int middle = pixels[0];
            foreach (int i in pixels)
            {
                if (Distance(i, cx, cy, size) < Distance(middle, cx, cy, size))
                {
                    middle = i;
                }
            }

            double middlePhase = Phase(textures.Height[middle].b);
            bool hasRim = false;
            foreach (int i in pixels)
            {
                int code = textures.Height[i].b;
                double behind = TerrainArt.Wrap(middlePhase - Phase(code), 1);
                double expected = delay * (Distance(i, cx, cy, size) - Distance(middle, cx, cy, size));
                Assert.That(behind, Is.EqualTo(expected).Within(delay * 1.5 + 2.0 / 127.0), $"bubble {bubbles} pixel ({i % size}, {i / size}) falls behind with its distance");
                if (code < 128)
                {
                    continue;
                }

                hasRim = true;
                bool outside = false;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        outside |= textures.Height[TerrainArt.Wrap(i / size + dy, size) * size + TerrainArt.Wrap(i % size + dx, size)].b == 0;
                    }
                }

                Assert.That(outside, Is.True, $"bubble {bubbles}: rim pixel ({i % size}, {i / size}) lies on the outside");
            }

            if (pixels.Count > 16)
            {
                Assert.That(hasRim, Is.True, $"bubble {bubbles} of {pixels.Count} px has a rim");
            }

            rimmed += hasRim ? 1 : 0;
            bubbles++;
        }

        Assert.That(bubbles, Is.GreaterThan(2));
        Assert.That(rimmed, Is.GreaterThan(0));
    }

    /// <summary>
    /// Each of lava's hot patches is one orange of its own: off the reds and the specks, every group of touching pixels
    /// holds at most two colours, its orange and its core, each within the patch shift of the recipe's orange or core in
    /// hue and still orange; the patches do not all share one orange, and they cover about their share of the surface.
    /// </summary>
    [Test]
    public void LavaPatches_AreEachOneOrangeOfTheirOwn()
    {
        LavaBubblesRecipe recipe = (LavaBubblesRecipe)CreateRecipe(typeof(LavaBubblesRecipe));
        SerializedObject settings = new(recipe);
        Color32 red = settings.FindProperty("red").colorValue;
        Color32 darkerRed = settings.FindProperty("darkerRed").colorValue;
        Color32 speck = settings.FindProperty("speck").colorValue;
        Color.RGBToHSV(settings.FindProperty("orange").colorValue, out float orangeHue, out _, out _);
        Color.RGBToHSV(settings.FindProperty("core").colorValue, out float coreHue, out _, out _);
        float hueRange = settings.FindProperty("hueRange").floatValue;
        float hotShare = settings.FindProperty("hotShare").floatValue;

        TerrainTextures textures = recipe.Generate(Seed);

        int size = textures.Size;
        Color32[] albedo = textures.Albedo;
        int[] patch = new int[size * size];
        for (int i = 0; i < patch.Length; i++)
        {
            patch[i] = SameColour(albedo[i], red) || SameColour(albedo[i], darkerRed) || SameColour(albedo[i], speck) ? -2 : -1;
        }

        int patches = 0;
        int hot = 0;
        HashSet<int> oranges = new();
        for (int start = 0; start < patch.Length; start++)
        {
            if (patch[start] != -1)
            {
                continue;
            }

            HashSet<int> colours = new();
            Stack<int> open = new();
            patch[start] = patches;
            open.Push(start);
            while (open.Count > 0)
            {
                int i = open.Pop();
                hot++;
                colours.Add(albedo[i].r << 16 | albedo[i].g << 8 | albedo[i].b);
                foreach (int n in Neighbours(i, size))
                {
                    if (patch[n] == -1)
                    {
                        patch[n] = patches;
                        open.Push(n);
                    }
                }
            }

            Assert.That(colours.Count, Is.LessThanOrEqualTo(2), $"patch {patches} holds its orange and its core");
            foreach (int colour in colours)
            {
                Color.RGBToHSV(new Color32((byte)(colour >> 16), (byte)(colour >> 8), (byte)colour, 255), out float hue, out _, out _);
                float off = Mathf.Min(Mathf.Abs(Mathf.DeltaAngle(hue * 360f, orangeHue * 360f)), Mathf.Abs(Mathf.DeltaAngle(hue * 360f, coreHue * 360f)));
                Assert.That(off, Is.LessThanOrEqualTo(hueRange + 1.5f), $"patch {patches} stays within the shift");
                Assert.That(hue * 360f, Is.InRange(0f, 45f), $"patch {patches} is orange");
                oranges.Add(colour);
            }

            patches++;
        }

        Assert.That(patches, Is.GreaterThan(10));
        Assert.That(oranges.Count, Is.GreaterThan(patches), "patches differ in tone");
        Assert.That((double)hot / (size * size), Is.EqualTo(hotShare).Within(0.03));
    }

    /// <summary>
    /// The value noise lava is drawn from repeats with the texture without a seam: from the last column to the first
    /// and the top row to the bottom it changes no more than between neighbours inside, and it is not flat.
    /// </summary>
    [Test]
    public void PeriodicValueNoise_RunsOnAcrossTheTexturesEdges()
    {
        const int size = 128;
        double[] noise = TerrainArt.PeriodicValueNoise(new System.Random(Seed), 13, size);
        double inside = 0.0;
        double across = 0.0;
        double lowest = 1.0;
        double highest = 0.0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double value = noise[y * size + x];
                lowest = Math.Min(lowest, value);
                highest = Math.Max(highest, value);
                double right = Math.Abs(noise[y * size + TerrainArt.Wrap(x + 1, size)] - value);
                double up = Math.Abs(noise[TerrainArt.Wrap(y + 1, size) * size + x] - value);
                if (x == size - 1)
                {
                    across = Math.Max(across, right);
                }
                else
                {
                    inside = Math.Max(inside, right);
                }

                if (y == size - 1)
                {
                    across = Math.Max(across, up);
                }
                else
                {
                    inside = Math.Max(inside, up);
                }
            }
        }

        Assert.That(across, Is.LessThanOrEqualTo(inside));
        Assert.That(highest - lowest, Is.GreaterThan(0.3));
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

        Assert.That(checkedRecipes, Is.EqualTo(17));
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

        if (textures.CellField == null || recipe.CellMode == TerrainMaterial.CellMode.None)
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
    /// The pixels touching <paramref name="start"/> (four neighbours, wrapping) whose height holds a glint or sparkle
    /// phase, <paramref name="start"/> first, each marked <paramref name="index"/> in <paramref name="group"/>.
    /// </summary>
    private static List<int> Group(Color32[] height, int size, int start, int[] group, int index)
    {
        List<int> pixels = new() { start };
        group[start] = index;
        for (int k = 0; k < pixels.Count; k++)
        {
            foreach (int n in Neighbours(pixels[k], size))
            {
                if (height[n].b != 0 && group[n] < 0)
                {
                    group[n] = index;
                    pixels.Add(n);
                }
            }
        }

        return pixels;
    }

    /// <summary>
    /// The four neighbours of pixel <paramref name="i"/>, wrapping at the texture's edges.
    /// </summary>
    private static IEnumerable<int> Neighbours(int i, int size)
    {
        int x = i % size;
        int y = i / size;
        yield return y * size + TerrainArt.Wrap(x + 1, size);
        yield return y * size + TerrainArt.Wrap(x - 1, size);
        yield return TerrainArt.Wrap(y + 1, size) * size + x;
        yield return TerrainArt.Wrap(y - 1, size) * size + x;
    }

    /// <summary>
    /// The distance from pixel <paramref name="i"/>'s centre to (<paramref name="cx"/>, <paramref name="cy"/>),
    /// wrapping.
    /// </summary>
    private static double Distance(int i, double cx, double cy, int size)
    {
        double dx = TerrainArt.WrapDelta(i % size - cx, size);
        double dy = TerrainArt.WrapDelta(i / size - cy, size);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// The phase a height texture's blue channel holds, as a share of its period, for a glint or a sparkle code.
    /// </summary>
    private static double Phase(int code)
    {
        return code >= 128 ? (code - 128) / 128.0 : (code - 1) / 127.0;
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
