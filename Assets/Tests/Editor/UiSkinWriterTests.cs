using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for <see cref="UiSkinRecipe"/> and <see cref="UiSkinWriter"/> (Docs/DesignSystem.md, Phase 5): every piece is
/// greyscale, sized to slice around its corners with the border the writer sets, panels and bars have their corners
/// cut and a raised panel its darker lip; the three markers are different shapes on one canvas; the check mark a toggle
/// shows while on (Phase 6) is two arms meeting at the bottom; and the project's sprites are what its recipe draws,
/// imported at 32 pixels per unit with point filtering and their borders.
/// </summary>
public class UiSkinWriterTests
{
    private const string RecipePath = "Assets/Settings/UI/Recipe_UiSkin.asset";

    private UiSkinRecipe recipe;

    /// <summary>
    /// A recipe with the default settings, never saved.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        recipe = ScriptableObject.CreateInstance<UiSkinRecipe>();
        recipe.hideFlags = HideFlags.HideAndDontSave;
    }

    /// <summary>
    /// Destroys the recipe.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(recipe);
    }

    /// <summary>
    /// Every piece is greyscale (red, green and blue equal, so a tint gives it its role's colour), as many pixels as
    /// its size, and either sliced with at least 1 px left to stretch between its borders or, a marker, a pip or the
    /// check mark, not sliced at all.
    /// </summary>
    [Test]
    public void EveryPiece_IsGreyscale_AndSlicesAroundItsCorners()
    {
        foreach (UiSkinPiece piece in UiSkinRecipe.Pieces)
        {
            SkinSprite sprite = recipe.Generate(piece);
            Assert.That(sprite.Pixels.Length, Is.EqualTo(sprite.Width * sprite.Height), $"{piece} pixels.");
            Assert.That(sprite.Pixels.All(p => p.r == p.g && p.g == p.b), Is.True, $"{piece} is greyscale.");
            Assert.That(sprite.Pixels.Any(p => p.a == 255 && p.r == 255), Is.True, $"{piece} has white to tint.");
            if (IsWhole(piece))
            {
                Assert.That(sprite.Border, Is.EqualTo(Vector4.zero), $"{piece} is shown whole.");
                continue;
            }

            Assert.That(sprite.Border.x + sprite.Border.z, Is.LessThan(sprite.Width), $"{piece} keeps a column to stretch.");
            Assert.That(sprite.Border.y + sprite.Border.w, Is.LessThan(sprite.Height), $"{piece} keeps a row to stretch.");
        }
    }

    /// <summary>
    /// A panel's corner pixel is cut while the pixels beside it are filled, its frame is 1 px wide, and a raised
    /// panel's lip is darker than its face, inside the border its bottom slices at.
    /// </summary>
    [Test]
    public void Panels_HaveCutCorners_AndARaisedPanelALip()
    {
        SkinSprite panel = recipe.Generate(UiSkinPiece.Panel);
        Assert.That(At(panel, 0, 0).a, Is.Zero, "The corner is cut.");
        Assert.That(At(panel, 1, 0).a, Is.EqualTo(255));
        Assert.That(At(panel, 0, 1).a, Is.EqualTo(255));
        Assert.That(At(panel, panel.Width / 2, panel.Height / 2).a, Is.EqualTo(255), "A panel is filled.");

        SkinSprite frame = recipe.Generate(UiSkinPiece.PanelFrame);
        Assert.That(At(frame, 0, frame.Height / 2).a, Is.EqualTo(255), "The frame runs along the edge.");
        Assert.That(At(frame, 1, frame.Height / 2).a, Is.Zero, "1 px wide.");

        SkinSprite raised = recipe.Generate(UiSkinPiece.PanelRaised);
        int middle = raised.Width / 2;
        Assert.That(At(raised, middle, 0).r, Is.LessThan(At(raised, middle, raised.Height - 2).r), "The lip is darker than the face.");
        Assert.That(raised.Border.y, Is.GreaterThan(raised.Border.w), "The bottom border holds the lip.");
        Assert.That(raised.Height, Is.GreaterThan(recipe.Generate(UiSkinPiece.Panel).Height));
    }

    /// <summary>
    /// The move, attack move and hover markers share one canvas, so they share a centre, and each is a shape of its
    /// own: colour is never the only cue.
    /// </summary>
    [Test]
    public void TheMarkers_AreDifferentShapes_OnOneCanvas()
    {
        SkinSprite move = recipe.Generate(UiSkinPiece.MarkerMove);
        SkinSprite attack = recipe.Generate(UiSkinPiece.MarkerAttackMove);
        SkinSprite hover = recipe.Generate(UiSkinPiece.MarkerHover);
        foreach (SkinSprite marker in new[] { attack, hover })
        {
            Assert.That(marker.Width, Is.EqualTo(move.Width));
            Assert.That(marker.Height, Is.EqualTo(move.Height));
        }

        Assert.That(Shape(move), Is.Not.EqualTo(Shape(attack)));
        Assert.That(Shape(move), Is.Not.EqualTo(Shape(hover)));
        Assert.That(Shape(attack), Is.Not.EqualTo(Shape(hover)));
        Assert.That(move.Pixels.Any(p => p.a > 0 && p.r == 0), Is.True, "Outlined in black.");
    }

    /// <summary>
    /// The check mark is 8 x 5 px: its long arm rises from the bottom row to the top right corner and its short arm
    /// from the bottom to the left edge, two pixels across in every row, with clear pixels between them, so a toggle
    /// that is on shows a mark rather than a filled chip like a swatch row's.
    /// </summary>
    [Test]
    public void TheCheck_IsTwoArmsMeetingAtTheBottom()
    {
        SkinSprite check = recipe.Generate(UiSkinPiece.Check);
        Assert.That(check.Width, Is.EqualTo(8));
        Assert.That(check.Height, Is.EqualTo(5));
        string[] rows = { "......##", ".....##.", "##..##..", ".####...", "..##...." };
        for (int row = 0; row < rows.Length; row++)
        {
            for (int x = 0; x < check.Width; x++)
            {
                bool drawn = rows[row][x] == '#';
                Assert.That(At(check, x, check.Height - 1 - row).a, Is.EqualTo(drawn ? 255 : 0), $"Pixel ({x}, {row}) from the top left.");
            }
        }
    }

    /// <summary>
    /// The project's skin sprites hold exactly what its recipe draws, imported as single sprites at 32 pixels per unit
    /// with point filtering, a centred pivot and the piece's 9-slice border, so one sprite pixel is one UI px.
    /// </summary>
    [Test]
    public void TheProjectsSprites_AreWhatItsRecipeDraws()
    {
        UiSkinRecipe project = AssetDatabase.LoadAssetAtPath<UiSkinRecipe>(RecipePath);
        Assert.That(project, Is.Not.Null, $"The recipe at {RecipePath}.");
        List<string> problems = new();
        foreach (UiSkinPiece piece in UiSkinRecipe.Pieces)
        {
            string path = UiSkinRecipe.PathOf(piece);
            SkinSprite drawn = project.Generate(piece);
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                problems.Add($"{path} is missing.");
                continue;
            }

            byte[] expected = TerrainMaterialWriter.EncodePng(drawn.Width, drawn.Height, drawn.Pixels);
            if (!File.ReadAllBytes(path).SequenceEqual(expected))
            {
                problems.Add($"{path} differs from what the recipe draws; Generate it again.");
            }

            if (sprite.rect.size != new Vector2(drawn.Width, drawn.Height) || sprite.border != drawn.Border)
            {
                problems.Add($"{path} is {sprite.rect.size} with border {sprite.border}, not {drawn.Width} x {drawn.Height} with {drawn.Border}.");
            }

            if (!Mathf.Approximately(sprite.pixelsPerUnit, 32f) || sprite.texture.filterMode != FilterMode.Point || sprite.pivot != sprite.rect.size * 0.5f)
            {
                problems.Add($"{path} is not imported at 32 pixels per unit, point filtered, with a centred pivot.");
            }
        }

        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    /// <summary>
    /// Whether <paramref name="piece"/> is shown whole rather than sliced.
    /// </summary>
    private static bool IsWhole(UiSkinPiece piece)
    {
        return piece == UiSkinPiece.MarkerMove || piece == UiSkinPiece.MarkerAttackMove || piece == UiSkinPiece.MarkerHover || piece == UiSkinPiece.Pip
            || piece == UiSkinPiece.Check;
    }

    /// <summary>
    /// The pixel of <paramref name="sprite"/> at (<paramref name="x"/>, <paramref name="y"/>) from its bottom left.
    /// </summary>
    private static Color32 At(SkinSprite sprite, int x, int y)
    {
        return sprite.Pixels[y * sprite.Width + x];
    }

    /// <summary>
    /// Which pixels of <paramref name="sprite"/> are drawn, as a string of 0s and 1s.
    /// </summary>
    private static string Shape(SkinSprite sprite)
    {
        return new string(sprite.Pixels.Select(p => p.a > 0 ? '1' : '0').ToArray());
    }
}
