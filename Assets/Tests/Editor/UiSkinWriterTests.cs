using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for <see cref="UiSkinRecipe"/> and <see cref="UiSkinWriter"/> (Docs/DesignSystem.md, Phase 5): every piece is
/// greyscale, sized to slice around its corners with the border the writer sets, panels and frames have square corners
/// (Docs/UiRevamp.md, Phase 3), or cut ones at a recipe's cut, and a raised panel its darker lip; the three markers are different shapes on one canvas; the check mark a toggle
/// shows while on (Phase 6) is two arms meeting at the bottom; a slot's corner brackets and the hatch tile
/// (Docs/UiRevamp.md, Phase 1) and a bar's tick and a debuff's notch (Phase 2) are what they say; and the project's sprites are what its recipe draws, imported at 32
/// pixels per unit with point filtering and their borders.
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
    /// By default a panel's and a frame's corners are square (Docs/UiRevamp.md, Phase 3): the corner pixel is filled,
    /// so the 1 px frame closes; with a cut of 1 the corner pixel goes while the pixels beside it stay. The frame is 1 px
    /// wide, and a raised panel's lip is darker than its face, inside the border its bottom slices at.
    /// </summary>
    [Test]
    public void Panels_HaveSquareCorners_OrCutOnes_AndARaisedPanelALip()
    {
        Assert.That(recipe.CornerCut, Is.Zero);
        SkinSprite panel = recipe.Generate(UiSkinPiece.Panel);
        Assert.That(At(panel, 0, 0).a, Is.EqualTo(255), "The corner is square.");
        Assert.That(At(panel, panel.Width / 2, panel.Height / 2).a, Is.EqualTo(255), "A panel is filled.");

        SkinSprite frame = recipe.Generate(UiSkinPiece.PanelFrame);
        Assert.That(At(frame, 0, 0).a, Is.EqualTo(255), "The frame closes at its corner.");
        Assert.That(At(frame, 0, frame.Height / 2).a, Is.EqualTo(255), "The frame runs along the edge.");
        Assert.That(At(frame, 1, frame.Height / 2).a, Is.Zero, "1 px wide.");
        Assert.That(At(recipe.Generate(UiSkinPiece.BarFrame), 0, 0).a, Is.EqualTo(255), "A bar's corner is square too.");

        SerializedObject settings = new(recipe);
        settings.FindProperty("cornerCut").intValue = 1;
        settings.ApplyModifiedPropertiesWithoutUndo();
        SkinSprite cut = recipe.Generate(UiSkinPiece.Panel);
        Assert.That(At(cut, 0, 0).a, Is.Zero, "A cut corner.");
        Assert.That(At(cut, 1, 0).a, Is.EqualTo(255));
        Assert.That(At(cut, 0, 1).a, Is.EqualTo(255));
        settings.FindProperty("cornerCut").intValue = 0;
        settings.ApplyModifiedPropertiesWithoutUndo();

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
    /// A slot's corner brackets (Docs/UiRevamp.md, Phase 1) are an L of 5 px arms, 1 px thick, in each corner with
    /// nothing between them, sliced at the arm so they keep to the corners of any slot; the hatch is a 4 px tile with one
    /// diagonal rising to the right, so the tiles join into lines 4 px apart.
    /// </summary>
    [Test]
    public void TheCornersAndTheHatch_AreBracketsAndADiagonal()
    {
        SkinSprite corners = recipe.Generate(UiSkinPiece.SlotCorners);
        Assert.That(corners.Width, Is.EqualTo(11));
        Assert.That(corners.Height, Is.EqualTo(11));
        Assert.That(corners.Border, Is.EqualTo(new Vector4(5f, 5f, 5f, 5f)));
        string[] rows =
        {
            "#####.#####",
            "#.........#",
            "#.........#",
            "#.........#",
            "#.........#",
            "...........",
            "#.........#",
            "#.........#",
            "#.........#",
            "#.........#",
            "#####.#####",
        };
        for (int row = 0; row < rows.Length; row++)
        {
            for (int x = 0; x < corners.Width; x++)
            {
                Assert.That(At(corners, x, corners.Height - 1 - row).a, Is.EqualTo(rows[row][x] == '#' ? 255 : 0), $"Corner pixel ({x}, {row}) from the top left.");
            }
        }

        SkinSprite hatch = recipe.Generate(UiSkinPiece.Hatch);
        Assert.That(hatch.Width, Is.EqualTo(4));
        Assert.That(hatch.Height, Is.EqualTo(4));
        for (int y = 0; y < hatch.Height; y++)
        {
            for (int x = 0; x < hatch.Width; x++)
            {
                Assert.That(At(hatch, x, y).a, Is.EqualTo(x == y ? 255 : 0), $"Hatch pixel ({x}, {y}) from the bottom left.");
            }
        }
    }

    /// <summary>
    /// A bar's tick (Docs/UiRevamp.md, Phase 2) is a 1 x 3 px line, and a debuff's notch a 5 px right triangle filling
    /// its top row and right column, its hypotenuse a 1-1 diagonal.
    /// </summary>
    [Test]
    public void TheTickAndTheNotch_AreALineAndACornerTriangle()
    {
        SkinSprite tick = recipe.Generate(UiSkinPiece.BarTick);
        Assert.That(tick.Width, Is.EqualTo(1));
        Assert.That(tick.Height, Is.EqualTo(3));
        Assert.That(tick.Pixels.All(p => p.a == 255), Is.True);

        SkinSprite notch = recipe.Generate(UiSkinPiece.Notch);
        string[] rows = { "#####", ".####", "..###", "...##", "....#" };
        Assert.That(notch.Width, Is.EqualTo(5));
        Assert.That(notch.Height, Is.EqualTo(5));
        for (int row = 0; row < rows.Length; row++)
        {
            for (int x = 0; x < notch.Width; x++)
            {
                Assert.That(At(notch, x, notch.Height - 1 - row).a, Is.EqualTo(rows[row][x] == '#' ? 255 : 0), $"Notch pixel ({x}, {row}) from the top left.");
            }
        }
    }

    /// <summary>
    /// A window's grips (Docs/UiRevamp.md, Phase 12): the move grip six 2 px dots, three across and two down, 1 px
    /// apart; the resize grip three diagonal strokes in the bottom right half of a 7 px square, 3 px apart along its
    /// edges, the first the corner pixel.
    /// </summary>
    [Test]
    public void TheGrips_AreDotsAndDiagonalStrokes()
    {
        AssertRows(recipe.Generate(UiSkinPiece.GripMove), "##.##.##", "##.##.##", "........", "##.##.##", "##.##.##");
        AssertRows(recipe.Generate(UiSkinPiece.GripResize), "......#", ".....#.", "....#..", "...#..#", "..#..#.", ".#..#..", "#..#..#");
    }

    /// <summary>
    /// A player slot's badges (Docs/UiRevamp.md, Phase 5) are discs outlined in opaque black: the small one 5 px across
    /// in a 7 px sprite, its corners cut, and the large one 16 px across in an 18 px sprite, round and symmetric, white
    /// inside, black around and clear beyond.
    /// </summary>
    [Test]
    public void TheBadges_AreDiscsOutlinedInBlack()
    {
        SkinSprite badge = recipe.Generate(UiSkinPiece.Badge);
        string[] rows = { ".ooooo.", "oo###oo", "o#####o", "o#####o", "o#####o", "oo###oo", ".ooooo." };
        Assert.That(badge.Width, Is.EqualTo(7));
        Assert.That(badge.Height, Is.EqualTo(7));
        for (int row = 0; row < rows.Length; row++)
        {
            for (int x = 0; x < badge.Width; x++)
            {
                Color32 pixel = At(badge, x, badge.Height - 1 - row);
                char expected = rows[row][x];
                Assert.That(pixel.a, Is.EqualTo(expected == '.' ? 0 : 255), $"Badge pixel ({x}, {row}) from the top left.");
                Assert.That(pixel.r, Is.EqualTo(expected == '#' ? 255 : 0), $"Badge pixel ({x}, {row}) is {(expected == '#' ? "white" : "black or clear")}.");
            }
        }

        SkinSprite large = recipe.Generate(UiSkinPiece.BadgeLarge);
        Assert.That(large.Width, Is.EqualTo(18));
        Assert.That(large.Height, Is.EqualTo(18));
        int white = 0;
        for (int y = 0; y < large.Height; y++)
        {
            for (int x = 0; x < large.Width; x++)
            {
                Color32 pixel = At(large, x, y);
                Assert.That(pixel, Is.EqualTo(At(large, large.Width - 1 - x, y)), $"Large badge pixel ({x}, {y}) mirrors across.");
                Assert.That(pixel, Is.EqualTo(At(large, y, x)), $"Large badge pixel ({x}, {y}) mirrors along the diagonal.");
                white += pixel.r == 255 ? 1 : 0;
            }
        }

        Assert.That(At(large, 9, 9).r, Is.EqualTo(255), "White in the middle.");
        Assert.That(At(large, 1, 9).r, Is.EqualTo(255), "Reaching the edge of its 16 px.");
        Assert.That(At(large, 0, 9), Is.EqualTo(new Color32(0, 0, 0, 255)), "Outlined in black.");
        Assert.That(At(large, 1, 1).a, Is.Zero, "Round: its corners clear.");
        Assert.That(white, Is.InRange(190, 210), "About pi times 8 squared.");
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
            || piece == UiSkinPiece.Check || piece == UiSkinPiece.Hatch || piece == UiSkinPiece.BarTick || piece == UiSkinPiece.Notch
            || piece == UiSkinPiece.Badge || piece == UiSkinPiece.BadgeLarge || piece == UiSkinPiece.GripMove || piece == UiSkinPiece.GripResize;
    }

    /// <summary>
    /// Asserts <paramref name="sprite"/> is <paramref name="rows"/>, from the top, '#' opaque white and '.' clear.
    /// </summary>
    private static void AssertRows(SkinSprite sprite, params string[] rows)
    {
        Assert.That(sprite.Width, Is.EqualTo(rows[0].Length));
        Assert.That(sprite.Height, Is.EqualTo(rows.Length));
        for (int row = 0; row < rows.Length; row++)
        {
            for (int x = 0; x < sprite.Width; x++)
            {
                Color32 pixel = At(sprite, x, sprite.Height - 1 - row);
                Assert.That(pixel.a, Is.EqualTo(rows[row][x] == '#' ? 255 : 0), $"{sprite.Piece} pixel ({x}, {row}) from the top left.");
                Assert.That(pixel.r, Is.EqualTo(rows[row][x] == '#' ? 255 : pixel.r));
            }
        }
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
