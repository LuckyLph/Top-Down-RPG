using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Tests for the walls' sheet: <see cref="WallSheetRecipe"/> built in memory with its default settings and the
/// project's walls rule tile (only read) gives the same bytes for the same seed, reads each piece's quadrants from the
/// tiling rules and fills exactly them away from the rounded corner; the project's walls sheet (only read) is 16 pieces
/// of 32 px at 32 pixels per unit, named walls_0 to walls_15, filling the quadrants their rules mark as filled, and the
/// rule tile's sprites still point at those slices.
/// </summary>
public class WallSheetRecipeTests
{
    private const string SheetPath = "Assets/Tiles/tilesets/walls.png";
    private const string RuleTilePath = "Assets/Tiles/tilesets/walls_DualGridRuleTile.asset";
    private const string RecipePath = "Assets/Settings/Terrain/Recipes/Recipe_Walls.asset";
    private const int PieceSize = 32;

    private readonly List<Object> created = new();
    private DualGridRuleTile ruleTile;

    /// <summary>
    /// Loads the project's walls rule tile, which the tests only read.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        ruleTile = AssetDatabase.LoadAssetAtPath<DualGridRuleTile>(RuleTilePath);
        Assert.That(ruleTile, Is.Not.Null);
    }

    /// <summary>
    /// Destroys every object the test created.
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
    /// The same seed and settings give the same pixels and PNG bytes, and another seed other pixels.
    /// </summary>
    [Test]
    public void Generate_SameParameters_GiveTheSameBytes()
    {
        WallSheetRecipe recipe = CreateRecipe();
        int[] masks = WallSheetRecipe.PieceMasks(ruleTile, out _);

        Color32[] first = recipe.Generate(7, masks);
        Color32[] again = recipe.Generate(7, masks);
        Color32[] other = recipe.Generate(8, masks);

        Assert.That(first.Length, Is.EqualTo(recipe.SheetSize * recipe.SheetSize));
        Assert.That(TerrainMaterialWriter.EncodePng(recipe.SheetSize, again), Is.EqualTo(TerrainMaterialWriter.EncodePng(recipe.SheetSize, first)));
        Assert.That(other.SequenceEqual(first), Is.False);
    }

    /// <summary>
    /// The pieces' quadrants come from the tiling rules: piece 6 fills all four, piece 12 none, piece 0 only the
    /// bottom left and piece 1 the right half, as the standard 16-piece layout has them, and every piece is named once.
    /// </summary>
    [Test]
    public void PieceMasks_ReadEachPiecesQuadrantsFromItsTilingRule()
    {
        int[] masks = WallSheetRecipe.PieceMasks(ruleTile, out string problem);

        Assert.That(masks, Is.Not.Null, problem);
        Assert.That(masks[6], Is.EqualTo(15));
        Assert.That(masks[12], Is.EqualTo(0));
        Assert.That(masks[0], Is.EqualTo(1));
        Assert.That(masks[1], Is.EqualTo(2 | 8));
        Assert.That(masks.Distinct().Count(), Is.EqualTo(WallSheetRecipe.PieceCount), "Each piece covers a different corner pattern.");
    }

    /// <summary>
    /// Every generated piece fills the quadrants its tiling rule marks as filled and leaves the others clear, wherever
    /// a pixel lies farther from the piece's centre than a corner radius, so the rounding cannot reach it.
    /// </summary>
    [Test]
    public void EachGeneratedPiece_FillsTheQuadrantsItsTilingRuleMarksAsFilled()
    {
        WallSheetRecipe recipe = CreateRecipe();
        int[] masks = WallSheetRecipe.PieceMasks(ruleTile, out _);

        AssertPiecesFillTheirQuadrants(recipe.Generate(recipe.Seed, masks), recipe.PieceSize, masks, Mathf.Max(recipe.OuterRadius, recipe.InnerRadius));
    }

    /// <summary>
    /// The project's sheet is a 128 px PNG sliced into 16 pieces of 32 px at 32 pixels per unit, named walls_0 to
    /// walls_15 at their places in the 4 x 4 layout, each filling the quadrants its rule marks as filled; it is what the
    /// project's recipe generates for its seed; and the walls rule tile's sprites (every rule's, the default and the
    /// data tile's full piece) still point at those slices.
    /// </summary>
    [Test]
    public void TheProjectsSheet_IsSixteen32PxPiecesNamedWalls0To15_ThatTheRuleTileStillDraws()
    {
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(SheetPath);
        Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(PieceSize));
        Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Multiple));
        Texture2D sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(SheetPath);
        Assert.That(sheet.width, Is.EqualTo(PieceSize * WallSheetRecipe.PiecesPerSide));
        Assert.That(sheet.height, Is.EqualTo(PieceSize * WallSheetRecipe.PiecesPerSide));

        Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(SheetPath).OfType<Sprite>().ToArray();
        Assert.That(sprites.Select(s => s.name), Is.EquivalentTo(Enumerable.Range(0, WallSheetRecipe.PieceCount).Select(i => $"walls_{i}")));
        foreach (Sprite sprite in sprites)
        {
            RectInt piece = WallSheetRecipe.PieceRect(WallSheetRecipe.PieceIndex(sprite.name), PieceSize);
            Assert.That(sprite.rect, Is.EqualTo(new Rect(piece.x, piece.y, piece.width, piece.height)), sprite.name);
            Assert.That(sprite.pixelsPerUnit, Is.EqualTo(PieceSize), sprite.name);
            Assert.That(sprite.pivot, Is.EqualTo(new Vector2(PieceSize * 0.5f, PieceSize * 0.5f)), sprite.name);
        }

        foreach (RuleTile.TilingRule rule in ruleTile.m_TilingRules)
        {
            Assert.That(sprites, Does.Contain(rule.m_Sprites[0]), "Every rule's sprite is a slice of the sheet.");
        }

        Assert.That(sprites, Does.Contain(ruleTile.m_DefaultSprite));
        Assert.That(ruleTile.DataTile.sprite, Is.Not.Null.And.Property("name").EqualTo("walls_6"));
        Assert.That(sprites, Does.Contain(ruleTile.DataTile.sprite));

        Texture2D decoded = Track(new Texture2D(2, 2, TextureFormat.RGBA32, false));
        Assert.That(decoded.LoadImage(File.ReadAllBytes(SheetPath)), Is.True);
        int[] masks = WallSheetRecipe.PieceMasks(ruleTile, out _);

        WallSheetRecipe recipe = AssetDatabase.LoadAssetAtPath<WallSheetRecipe>(RecipePath);
        Assert.That(recipe, Is.Not.Null);
        AssertPiecesFillTheirQuadrants(decoded.GetPixels32(), PieceSize, masks, Mathf.Max(recipe.OuterRadius, recipe.InnerRadius));
        Assert.That(recipe.Sheet, Is.SameAs(sheet));
        Assert.That(recipe.RuleTile, Is.SameAs(ruleTile));
        Assert.That(File.ReadAllBytes(SheetPath), Is.EqualTo(TerrainMaterialWriter.EncodePng(recipe.SheetSize, recipe.Generate(recipe.Seed))), "The sheet is what its recipe generates.");
    }

    /// <summary>
    /// Settings that cannot make a sheet (a corner radius reaching half a piece) are reported by
    /// <see cref="WallSheetRecipe.CanGenerate"/>, make <see cref="WallSheetRecipe.Generate(int, int[])"/> throw, and make
    /// the writer log the reason and write nothing instead of throwing.
    /// </summary>
    [Test]
    public void SettingsThatCannotMakeASheet_AreReported_AndTheWriterRefusesThem()
    {
        WallSheetRecipe recipe = CreateRecipe();
        recipe.name = "Recipe";
        SerializedObject settings = new(recipe);
        settings.FindProperty("ruleTile").objectReferenceValue = ruleTile;
        settings.FindProperty("outerRadius").floatValue = 16f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        int[] masks = WallSheetRecipe.PieceMasks(ruleTile, out _);

        Assert.That(recipe.CanGenerate(out string problem), Is.False);
        Assert.That(() => recipe.Generate(1, masks), Throws.InvalidOperationException);
        LogAssert.Expect(LogType.Error, $"Recipe: {problem}");
        Assert.That(WallSheetWriter.Generate(recipe), Is.False);
    }

    /// <summary>
    /// The inspector's preview follows the rule tile: after an edit to one of its tiling rules elsewhere, the preview
    /// asks to be rebuilt although the recipe itself did not change, and a rebuild settles it again.
    /// </summary>
    [Test]
    public void ThePreview_FollowsAnEditToTheRuleTile()
    {
        DualGridRuleTile copy = Track(Object.Instantiate(ruleTile));
        WallSheetRecipe recipe = CreateRecipe();
        SerializedObject settings = new(recipe);
        settings.FindProperty("ruleTile").objectReferenceValue = copy;
        settings.ApplyModifiedPropertiesWithoutUndo();
        WallSheetRecipeEditor inspector = Track((WallSheetRecipeEditor)Editor.CreateEditor(recipe));
        inspector.RebuildPreview(recipe);
        Assert.That(inspector.PreviewNeedsRebuild(recipe), Is.False);

        List<int> neighbours = copy.m_TilingRules[12].m_Neighbors;
        neighbours[0] = neighbours[0] == DualGridRuleTile.DualGridNeighbor.Filled ? DualGridRuleTile.DualGridNeighbor.NotFilled : DualGridRuleTile.DualGridNeighbor.Filled;

        Assert.That(inspector.PreviewNeedsRebuild(recipe), Is.True);
        inspector.RebuildPreview(recipe);
        Assert.That(inspector.PreviewNeedsRebuild(recipe), Is.False);
    }

    /// <summary>
    /// Asserts that in every piece of <paramref name="pixels"/> (a sheet of <paramref name="size"/> px pieces, rows
    /// from the bottom) each pixel farther than <paramref name="radius"/> from the piece's centre along x or y is opaque
    /// exactly when its quadrant's bit is set in the piece's mask.
    /// </summary>
    private static void AssertPiecesFillTheirQuadrants(Color32[] pixels, int size, int[] masks, float radius)
    {
        int sheetSize = size * WallSheetRecipe.PiecesPerSide;
        float half = size * 0.5f;
        for (int piece = 0; piece < WallSheetRecipe.PieceCount; piece++)
        {
            RectInt rect = WallSheetRecipe.PieceRect(piece, size);
            int wrong = 0;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) <= radius)
                    {
                        continue;
                    }

                    int bit = 1 << ((dx > 0f ? 1 : 0) + (dy > 0f ? 2 : 0));
                    bool opaque = pixels[(rect.y + y) * sheetSize + rect.x + x].a > 0;
                    wrong += opaque == ((masks[piece] & bit) != 0) ? 0 : 1;
                }
            }

            Assert.That(wrong, Is.Zero, $"piece {piece} (mask {masks[piece]})");
        }
    }

    /// <summary>
    /// A recipe with its default settings, built in memory.
    /// </summary>
    private WallSheetRecipe CreateRecipe()
    {
        return Track(ScriptableObject.CreateInstance<WallSheetRecipe>());
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
