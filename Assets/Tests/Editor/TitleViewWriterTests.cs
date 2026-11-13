using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Tests for the title view recipe and its writer (Docs/UiRevamp.md, Phase 11): the committed logo is exactly what the
/// recipe draws (the name in white, every font pixel doubled, outlined in black) and fits the main menu's panel; both
/// sprites import as single sprites at one UI px a pixel; the view lies on its world's chunks and covers every canvas in
/// the Scale table; and the view rendered now still matches the committed sprite, so a world changed under it is caught.
/// </summary>
public class TitleViewWriterTests
{
    private const string RecipePath = "Assets/Settings/UI/Recipe_TitleView.asset";
    private const float MostPixelsChanged = 0.001f;

    /// <summary>
    /// The committed logo is the recipe's drawing pixel for pixel: white glyphs, each font pixel two px square, with a
    /// black outline and nothing else, and narrow enough for the main menu's panel.
    /// </summary>
    [Test]
    public void TheLogo_IsTheRecipesDrawing()
    {
        TitleViewRecipe recipe = Recipe();
        LogoPicture logo = recipe.GenerateLogo();
        Texture2D committed = Load(TitleViewRecipe.LogoPath);
        try
        {
            Assert.That(committed.width, Is.EqualTo(logo.Width));
            Assert.That(committed.height, Is.EqualTo(logo.Height));
            Assert.That(committed.GetPixels32(), Is.EqualTo(logo.Pixels));
            int white = 0;
            int black = 0;
            foreach (Color32 pixel in logo.Pixels)
            {
                bool isWhite = pixel.r == 255 && pixel.g == 255 && pixel.b == 255 && pixel.a == 255;
                bool isBlack = pixel.r == 0 && pixel.g == 0 && pixel.b == 0 && pixel.a == 255;
                Assert.That(isWhite || isBlack || pixel.a == 0, Is.True, "Only white, black and clear.");
                white += isWhite ? 1 : 0;
                black += isBlack ? 1 : 0;
            }

            Assert.That(white, Is.GreaterThan(0));
            Assert.That(black, Is.GreaterThan(0));
            Assert.That(white % 4, Is.Zero, "Every font pixel is two px square.");
            Assert.That(logo.Width, Is.LessThanOrEqualTo(MainMenuController.PanelWidth - 16f), "It fits the panel's buttons' width.");
        }
        finally
        {
            Object.DestroyImmediate(committed);
        }
    }

    /// <summary>
    /// The title view and the logo import as single sprites at 32 pixels per unit, point filtered, their pivots centred.
    /// </summary>
    [Test]
    public void TheTitleSprites_ImportAtOneUiPxAPixel()
    {
        foreach (string path in new[] { TitleViewRecipe.ViewPath, TitleViewRecipe.LogoPath })
        {
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite), path);
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single), path);
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(32f), path);
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point), path);
            Assert.That(importer.spritePivot, Is.EqualTo(new Vector2(0.5f, 0.5f)), path);
        }
    }

    /// <summary>
    /// The view lies on its world's chunks, opens at least one chunk scene, and is as large as the recipe says, which
    /// covers the canvas of every display and size step in the Scale table.
    /// </summary>
    [Test]
    public void TheView_LiesOnItsWorld_AndCoversEveryCanvas()
    {
        TitleViewRecipe recipe = Recipe();
        Assert.That(recipe.CanGenerate(out string problem), Is.True, problem);
        Rect view = recipe.ViewRect();
        foreach (Vector2 corner in new[] { view.min, view.max - new Vector2(0.01f, 0.01f), new Vector2(view.xMin, view.yMax - 0.01f), new Vector2(view.xMax - 0.01f, view.yMin) })
        {
            Assert.That(recipe.World.HasChunkAt(corner), Is.True, $"The corner {corner} lies on a chunk.");
        }

        Assert.That(TitleViewRender.ChunkScenesUnder(recipe), Is.Not.Empty);
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TitleViewRecipe.ViewPath);
        Assert.That(sprite.rect.size, Is.EqualTo((Vector2)recipe.Size));
        UiScaleSettings settings = AssetDatabase.LoadAssetAtPath<UiScaleSettings>("Assets/Data/UI/UiScale.asset");
        List<string> uncovered = new();
        foreach (int[] display in UiScaleTests.Displays)
        {
            for (int step = 0; step <= UiScale.StepsFor(display[0], display[1], settings); step++)
            {
                int zoom = UiScale.ZoomFor(display[0], display[1], step, settings);
                if (display[0] / zoom > recipe.Size.x || display[1] / zoom > recipe.Size.y)
                {
                    uncovered.Add($"{display[0]} x {display[1]} at x{zoom}");
                }
            }
        }

        Assert.That(uncovered, Is.Empty, string.Join(", ", uncovered));
    }

    /// <summary>
    /// The view rendered now, its world's scenes opened and lit at its phase and given a few editor updates to draw,
    /// matches the committed sprite but for at most a tenth of a percent of its pixels (a caster at its top edge can
    /// shade a few differently from one render to the next): a world edited under it changes far more, and its recipe
    /// must be generated again. The open scenes are put back.
    /// </summary>
    [UnityTest]
    public IEnumerator TheView_MatchesItsWorld()
    {
        TitleViewRecipe recipe = Recipe();
        Color32[] now;
        int width;
        int height;
        using (TitleViewRender render = new(recipe))
        {
            for (int i = 0; i < TitleViewWriter.SettleUpdates; i++)
            {
                yield return null;
            }

            Texture2D rendered = render.Render();
            now = rendered.GetPixels32();
            width = rendered.width;
            height = rendered.height;
            Object.DestroyImmediate(rendered);
        }

        Texture2D committed = Load(TitleViewRecipe.ViewPath);
        try
        {
            Assert.That(width, Is.EqualTo(committed.width));
            Assert.That(height, Is.EqualTo(committed.height));
            Color32[] then = committed.GetPixels32();
            int changed = 0;
            for (int i = 0; i < now.Length; i++)
            {
                if (!now[i].Equals(then[i]))
                {
                    changed++;
                }
            }

            Assert.That(changed, Is.LessThanOrEqualTo(Mathf.CeilToInt(now.Length * MostPixelsChanged)), $"{changed} of {now.Length} pixels changed; generate {RecipePath} again.");
        }
        finally
        {
            Object.DestroyImmediate(committed);
        }
    }

    /// <summary>
    /// The project's title view recipe.
    /// </summary>
    private static TitleViewRecipe Recipe()
    {
        TitleViewRecipe recipe = AssetDatabase.LoadAssetAtPath<TitleViewRecipe>(RecipePath);
        Assert.That(recipe, Is.Not.Null, $"There is a title view recipe at {RecipePath}.");
        return recipe;
    }

    /// <summary>
    /// The PNG at <paramref name="path"/>, read from its bytes into a readable texture.
    /// </summary>
    private static Texture2D Load(string path)
    {
        Texture2D texture = new(2, 2, TextureFormat.RGBA32, false);
        texture.LoadImage(File.ReadAllBytes(path));
        return texture;
    }
}
