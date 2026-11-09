using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

/// <summary>
/// Tests for drawing a character in a player's colours (Docs/PlayerCharacter.md, Phase 5): its palette draws it in a
/// copy of the renderer's material holding the picks' palette, and the default picks put the renderer's own material
/// back; the hit flash leaves the copy alone; and rendered through the lit sprite shader, the adventurer's sheets, which
/// carry palette maps, draw exactly what the same pixels draw without one, under the shared material and under a palette
/// of every choice's own swatch alike, while a picked tunic changes exactly the pixels the map names as the tunic, the
/// sword drawn in its own colours, and the Bandit, drawn in Aseprite without a palette map, keeps its colours under any
/// palette.
/// </summary>
public class CharacterPaletteTests
{
    private const string ColoursPath = "Assets/Data/Characters/CharacterColours.asset";
    private const string MaterialPath = "Assets/Materials/SpritePixelLit.mat";
    private const string SlashPath = "Assets/Sprites/Characters/Adventurer/Adventurer_SwordSlash.png";
    private const string BanditPath = "Assets/Sprites/Characters/Bandit/Bandit_Idle.png";
    private const int ContactFrame = 4;
    private const int ArtPixelsPerUnit = 32;
    private static readonly Rect World = new(0f, 0f, 4f, 3f);
    private static readonly Vector2 Feet = new(2f, 1f);

    private readonly List<Object> created = new();
    private CharacterColours colours;
    private Material shared;

    /// <summary>
    /// Loads the project's colours and the lit sprite material.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        colours = AssetDatabase.LoadAssetAtPath<CharacterColours>(ColoursPath);
        shared = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Assert.That(colours, Is.Not.Null);
        Assert.That(shared, Is.Not.Null);
    }

    /// <summary>
    /// Destroys everything the test made, the palettes' copies and textures included.
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
    /// Picks other than the default draw the renderer in a copy of its material holding their palette, the copy kept for
    /// the next picks; the default picks put the renderer's own material back, and so does clearing.
    /// </summary>
    [Test]
    public void Show_DrawsInACopyHoldingThePalette_AndTheDefaultPutsTheOwnBack()
    {
        CharacterPalette palette = AddPalette(out SpriteRenderer body);
        CharacterColourPicks red = colours.Pick(default, colours.IndexOf("Tunic"), "Red");

        palette.Show(colours, default);
        Assert.That(palette.IsShown, Is.False);
        Assert.That(body.sharedMaterial, Is.EqualTo(shared));

        palette.Show(colours, red);
        Track(palette.Copy);
        Track(palette.Palette);
        Assert.That(palette.IsShown, Is.True);
        Assert.That(body.sharedMaterial, Is.EqualTo(palette.Copy));
        Assert.That(palette.Copy.shader, Is.EqualTo(shared.shader));
        Assert.That(palette.Copy.GetTexture(CharacterPaletteMap.PaletteTextureName), Is.EqualTo(palette.Palette));
        Assert.That(palette.Palette.width, Is.EqualTo(ColourRamp.ToneCount));
        Assert.That(palette.Palette.height, Is.EqualTo(CharacterPaletteMap.Rows));
        Color32[] expected = new Color32[CharacterPaletteMap.PaletteSize];
        colours.WritePalette(red, expected);
        Assert.That(palette.Palette.GetPixels32(), Is.EqualTo(expected));

        Material copy = palette.Copy;
        palette.Show(colours, colours.Pick(default, colours.IndexOf("Hair"), "Blonde"));
        Assert.That(palette.Copy, Is.EqualTo(copy), "The copy is kept.");

        palette.Show(colours, default);
        Assert.That(palette.IsShown, Is.False);
        Assert.That(body.sharedMaterial, Is.EqualTo(shared));
        palette.Show(colours, red);
        palette.Clear();
        Assert.That(body.sharedMaterial, Is.EqualTo(shared));
    }

    /// <summary>
    /// A hit's flash, a property block on the body, ends without taking the palette's copy off the renderer.
    /// </summary>
    [Test]
    public void TheHitFlash_LeavesThePaletteOnTheRenderer()
    {
        CharacterPalette palette = AddPalette(out SpriteRenderer body);
        palette.Show(colours, colours.Pick(default, colours.IndexOf("Tunic"), "Red"));
        Track(palette.Copy);
        Track(palette.Palette);
        HitFeedback feedback = body.gameObject.AddComponent<HitFeedback>();
        feedback.Configure(body, body.transform, null, null, 0.6f);

        feedback.Flash(Color.white, 0.8f, 0.1f);
        feedback.Tick(0f);
        feedback.Tick(0.2f);

        Assert.That(feedback.FlashCount, Is.EqualTo(1));
        Assert.That(body.HasPropertyBlock(), Is.False, "The flash is over.");
        Assert.That(body.sharedMaterial, Is.EqualTo(palette.Copy));
    }

    /// <summary>
    /// The sword slash's contact frame facing east, rendered through the lit sprite shader under the day's light: with
    /// its palette map under the shared material, and under a palette of every choice's own swatch, it draws exactly what
    /// the same pixels draw without a map; a red tunic changes exactly the pixels its palette map names as the tunic,
    /// so the sword keeps its colours; and the Bandit's idle draws the same under the red tunic's palette as under its own
    /// material.
    /// </summary>
    [UnityTest]
    public IEnumerator TheAdventurer_DrawsItsBakedColours_UntilAPickRecoloursItsDyes()
    {
        Sprite mapped = SpriteSheetFiles.SpritesByName(SlashPath)[CharacterSheetWriter.SliceName("Adventurer", "SwordSlash", 0, ContactFrame)];
        Sprite plain = Track(Sprite.Create(mapped.texture, mapped.rect, new Vector2(mapped.pivot.x / mapped.rect.width, mapped.pivot.y / mapped.rect.height), ArtPixelsPerUnit, 0, SpriteMeshType.FullRect));
        CharacterColourPicks forcedOwn = default;
        for (int choice = 0; choice < colours.Count; choice++)
        {
            forcedOwn = forcedOwn.With(choice, colours.OwnIndex(choice));
        }

        CharacterColourPicks red = colours.Pick(default, colours.IndexOf("Tunic"), "Red");
        Assert.That(forcedOwn.IsDefault, Is.False);
        Light2D sky = Track(new GameObject("Global Light 2D")).AddComponent<Light2D>();
        sky.lightType = Light2D.LightType.Global;
        sky.intensity = 0.85f;

        Color32[] reference = null;
        Color32[] sharedMapped = null;
        Color32[] own = null;
        Color32[] recoloured = null;
        yield return Render(plain, null, pixels => reference = pixels);
        yield return Render(mapped, null, pixels => sharedMapped = pixels);
        yield return Render(mapped, forcedOwn, pixels => own = pixels);
        yield return Render(mapped, red, pixels => recoloured = pixels);
        Assert.That(sharedMapped, Is.EqualTo(reference), "The shared material draws the baked colours.");
        Assert.That(own, Is.EqualTo(reference), "Every choice's own swatch draws the baked colours.");

        byte[] map = PaletteMapOf(SlashPath, mapped.rect);
        int width = Mathf.RoundToInt(World.width * ArtPixelsPerUnit);
        Vector2Int origin = Vector2Int.RoundToInt(Feet * ArtPixelsPerUnit - mapped.pivot);
        int tunic = 0;
        int changed = 0;
        int mismatched = 0;
        int kept = 0;
        for (int y = 0; y < (int)mapped.rect.height; y++)
        {
            for (int x = 0; x < (int)mapped.rect.width; x++)
            {
                int screen = (origin.y + y) * width + origin.x + x;
                byte code = map[y * (int)mapped.rect.width + x];
                bool isTunic = code > 0 && CharacterPaletteMap.DyeOf(code) == CharacterDye.Tunic;
                bool differs = !recoloured[screen].Equals(reference[screen]);
                tunic += isTunic ? 1 : 0;
                changed += differs ? 1 : 0;
                mismatched += isTunic != differs ? 1 : 0;
                kept += code == 0 && reference[screen].r + reference[screen].g + reference[screen].b > 0 && !differs ? 1 : 0;
            }
        }

        Assert.That(tunic, Is.GreaterThan(20));
        Assert.That(mismatched, Is.Zero, $"{changed} pixels changed, {tunic} are the tunic's.");
        Assert.That(kept, Is.GreaterThan(0), "The sword keeps its colours.");

        Sprite bandit = SpriteSheetFiles.SpritesByName(BanditPath)[CharacterSheetWriter.SliceName("Bandit", "Idle", 6, 0)];
        Color32[] banditOwn = null;
        Color32[] banditPicked = null;
        yield return Render(bandit, null, pixels => banditOwn = pixels);
        yield return Render(bandit, red, pixels => banditPicked = pixels);
        Assert.That(banditPicked, Is.EqualTo(banditOwn), "A look without a palette map keeps its colours.");
    }

    /// <summary>
    /// Renders <paramref name="sprite"/> standing at <see cref="Feet"/> under the shared material, or under a palette of
    /// <paramref name="picks"/> when given, over the <see cref="World"/> rectangle at one screen pixel an art pixel, and
    /// hands its pixels to <paramref name="read"/>; waits for frames until something is drawn.
    /// </summary>
    private IEnumerator Render(Sprite sprite, CharacterColourPicks? picks, System.Action<Color32[]> read)
    {
        CharacterPalette palette = AddPalette(out SpriteRenderer body);
        body.transform.position = Feet;
        body.sprite = sprite;
        body.sortingLayerName = "Entities";
        if (picks.HasValue)
        {
            palette.Show(colours, picks.Value);
            Track(palette.Copy);
            Track(palette.Palette);
        }

        Color32[] pixels = null;
        bool drawn = false;
        for (int frame = 0; frame < 30 && !drawn; frame++)
        {
            yield return null;
            Texture2D texture = TerrainBlendProbe.Render(null, World, 1, false, false);
            pixels = texture.GetPixels32();
            Object.DestroyImmediate(texture);
            foreach (Color32 pixel in pixels)
            {
                drawn |= pixel.r + pixel.g + pixel.b > 0;
            }
        }

        Assert.That(drawn, Is.True, "Something is drawn.");
        Object.DestroyImmediate(body.gameObject);
        read(pixels);
    }

    /// <summary>
    /// The bytes of the palette map beside the sheet at <paramref name="sheetPath"/> under <paramref name="rect"/>, rows
    /// from the bottom, read from its file.
    /// </summary>
    private byte[] PaletteMapOf(string sheetPath, Rect rect)
    {
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(sheetPath);
        Texture2D map = System.Array.Find(importer.secondarySpriteTextures, secondary => secondary.name == CharacterPaletteMap.MapTextureName).texture;
        Assert.That(map, Is.Not.Null);
        Texture2D file = Track(new Texture2D(2, 2));
        file.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(map)));
        Color32[] pixels = file.GetPixels32();
        byte[] codes = new byte[(int)rect.width * (int)rect.height];
        for (int y = 0; y < (int)rect.height; y++)
        {
            for (int x = 0; x < (int)rect.width; x++)
            {
                codes[y * (int)rect.width + x] = pixels[((int)rect.y + y) * file.width + (int)rect.x + x].r;
            }
        }

        return codes;
    }

    /// <summary>
    /// A sprite renderer drawn with the lit sprite material, with a palette beside it.
    /// </summary>
    private CharacterPalette AddPalette(out SpriteRenderer body)
    {
        GameObject unit = Track(new GameObject("Visuals"));
        body = unit.AddComponent<SpriteRenderer>();
        body.sharedMaterial = shared;
        return unit.AddComponent<CharacterPalette>();
    }

    /// <summary>
    /// Remembers <paramref name="instance"/> for destruction after the test.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}
