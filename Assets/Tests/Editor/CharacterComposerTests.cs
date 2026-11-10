using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for composing a character's frames from its layers (Docs/PlayerCharacter.md, Phase 6), on layers built in
/// the test: each pixel takes the nearest layer's, a tie going to the earlier drawing step whichever layer comes first;
/// a material is coloured by its ramp in its tone with its dye's palette code; an empty pixel beside an outlined
/// material takes the nearest such neighbour's outline, and an outlined pixel beside a nearer part past the inner line
/// gap its own, an unlined material doing neither; a layer's file reads back exactly what was written, depths on the
/// quantized steps included, a material table per clip or none, and refuses what is not one; a clip is coloured with
/// the table of its last layer that has one (a weapon's gear, Docs/PlayerCharacter.md, Phase 7), else the body's; and
/// building an outfit packs every frame into one texture with a sprite per frame pivoted on its clip's feet, a few
/// sprites at a time if asked, the same as at once, and a build given up destroys what it made.
/// </summary>
public class CharacterComposerTests
{
    private const float Gap = 2.6f;
    private static readonly ColourRamp Red = new(new Color32(40, 0, 0, 255), new Color32(120, 0, 0, 255), new Color32(180, 0, 0, 255), new Color32(240, 0, 0, 255));
    private static readonly ColourRamp Blue = new(new Color32(0, 0, 40, 255), new Color32(0, 0, 120, 255), new Color32(0, 0, 180, 255), new Color32(0, 0, 240, 255));
    private static readonly ColourRamp Grey = new(new Color32(20, 20, 20, 255), new Color32(90, 90, 90, 255), new Color32(130, 130, 130, 255), new Color32(170, 170, 170, 255));

    private readonly List<Object> created = new();

    /// <summary>
    /// Destroys everything the test made.
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
    /// Over three pixels in a row, the nearer layer's pixel shows; at the same depth the one drawn in the earlier step,
    /// though its layer comes second; each in its ramp's tone, with its dye's palette code.
    /// </summary>
    [Test]
    public void Compose_TakesTheNearestPixel_ATieGoingToTheEarlierStep()
    {
        CharacterLayerFrame first = Frame(new RectInt(1, 1, 3, 1), new byte[] { 0, 0, 0 }, new byte[] { 0, 0, 0 }, new byte[] { 2, 2, 2 }, 5, new[] { 1f, 2f, 3f });
        CharacterLayerFrame second = Frame(new RectInt(1, 1, 3, 1), new byte[] { 1, 1, 1 }, new byte[] { 1, 1, 1 }, new byte[] { 0, 1, 0 }, 3, new[] { 2f, 2f, 2f });
        Color32[] pixels = new Color32[5 * 3];
        byte[] codes = new byte[pixels.Length];

        new CharacterComposer().ComposeFrame(5, 3, new[] { first, null, second }, Materials(false), Gap, pixels, codes);

        Assert.That(pixels[1 * 5 + 1], Is.EqualTo(Red.Light), "the first layer is nearer");
        Assert.That(codes[1 * 5 + 1], Is.EqualTo(CharacterPaletteMap.Code(CharacterDye.Skin, 3)));
        Assert.That(pixels[1 * 5 + 2], Is.EqualTo(Blue.Middle), "a tie goes to the earlier step");
        Assert.That(codes[1 * 5 + 2], Is.EqualTo(CharacterPaletteMap.Code(CharacterDye.Tunic, 2)));
        Assert.That(pixels[1 * 5 + 3], Is.EqualTo(Blue.Shadow), "the second layer is nearer");
        Assert.That(pixels[0], Is.EqualTo(new Color32(0, 0, 0, 0)));
    }

    /// <summary>
    /// A lone outlined pixel is ringed by its outline on its four sides, not its corners, each with its dye's outline
    /// code; a pixel of another part nearer than its neighbour past the gap lines that neighbour, nearer by less does not,
    /// and an unlined material neither lines nor is lined.
    /// </summary>
    [Test]
    public void Compose_DrawsTheOutline_AndInnerLinesPastTheGap()
    {
        Color32[] pixels = new Color32[5 * 5];
        byte[] codes = new byte[pixels.Length];
        CharacterComposer composer = new();
        CharacterLayerFrame dot = Frame(new RectInt(2, 2, 1, 1), new byte[] { 0 }, new byte[] { 0 }, new byte[] { 1 }, 1, new[] { 0f });
        composer.ComposeFrame(5, 5, new[] { dot }, Materials(false), Gap, pixels, codes);
        foreach (int i in new[] { 2 * 5 + 1, 2 * 5 + 3, 1 * 5 + 2, 3 * 5 + 2 })
        {
            Assert.That(pixels[i], Is.EqualTo(Red.Outline), $"side {i}");
            Assert.That(codes[i], Is.EqualTo(CharacterPaletteMap.Code(CharacterDye.Skin, 0)));
        }

        Assert.That(pixels[1 * 5 + 1].a, Is.Zero, "no corner");

        CharacterLayerFrame row = Frame(new RectInt(1, 2, 3, 1), new byte[] { 0, 1, 2 }, new byte[] { 0, 1, 2 }, new byte[] { 1, 1, 1 }, 1, new[] { 10f, 5f, 3f });
        composer.ComposeFrame(5, 5, new[] { row }, Materials(true), Gap, pixels, codes);
        Assert.That(pixels[2 * 5 + 1], Is.EqualTo(Red.Outline), "5 nearer than 10, past the gap: lined");
        Assert.That(pixels[2 * 5 + 2], Is.EqualTo(Blue.Middle), "3 nearer than 5 but within the gap, and grey is unlined");
        Assert.That(pixels[2 * 5 + 3], Is.EqualTo(Grey.Middle), "an unlined material is not lined");
        Assert.That(pixels[3 * 5 + 3].a, Is.Zero, "an unlined material draws no outline around it");
    }

    /// <summary>
    /// A layer written and read back holds the same clips, frames, materials, parts, tones, steps and depths (on the
    /// quantized steps), and the body's material table; bytes that are not a layer are refused.
    /// </summary>
    [Test]
    public void ALayersFile_ReadsBackExactly()
    {
        byte[] material = { 0, CharacterLayer.Empty, 1, 1 };
        float[] depth = { -12.3456f, float.PositiveInfinity, -3.21f, 7.777f };
        CharacterLayer.Quantize(material, depth);
        CharacterLayerFrame frame = Frame(new RectInt(4, 5, 2, 2), material, new byte[] { 3, 0, 4, 4 }, new byte[] { 0, 0, 1, 2 }, 9, depth);
        CharacterLayerFrame empty = Frame(new RectInt(0, 0, 0, 0), new byte[0], new byte[0], new byte[0], 0, new float[0]);
        CharacterLayer layer = new(new[] { new CharacterLayerClip(new Vector2Int(64, 64), new Vector2Int(32, 12), new[] { frame, empty }, Materials(true)) });

        CharacterLayer read = CharacterLayer.Read(layer.Write());

        CharacterLayerClip clip = read.Clips[0];
        Assert.That(clip.FrameSize, Is.EqualTo(new Vector2Int(64, 64)));
        Assert.That(clip.Feet, Is.EqualTo(new Vector2Int(32, 12)));
        Assert.That(clip.Frames.Length, Is.EqualTo(2));
        CharacterLayerFrame back = clip.Frames[0];
        Assert.That(back.Box, Is.EqualTo(frame.Box));
        Assert.That(back.Material, Is.EqualTo(frame.Material));
        Assert.That(back.Part, Is.EqualTo(frame.Part));
        Assert.That(back.Tone, Is.EqualTo(frame.Tone));
        Assert.That(back.Step, Is.EqualTo(frame.Step));
        Assert.That(back.Depth, Is.EqualTo(frame.Depth));
        Assert.That(depth[0], Is.EqualTo(-12.3456f).Within(1f / CharacterLayer.DepthSteps));
        Assert.That(clip.Frames[1].Box.width, Is.Zero);
        Assert.That(clip.Materials.Length, Is.EqualTo(3));
        Assert.That(clip.Materials[1].Dye, Is.EqualTo(CharacterDye.Tunic));
        Assert.That(clip.Materials[1].Ramp, Is.EqualTo(Blue));
        Assert.That(clip.Materials[2].Outlined, Is.False);
        Assert.Throws<InvalidDataException>(() => CharacterLayer.Read(Gzipped(new byte[16])));
    }

    /// <summary>
    /// Composing a body of one clip of two frames and building it packs both frames into one texture, a sprite per
    /// frame over the box around what it draws, pivoted on the clip's feet, in a set of the look's clip over them that
    /// keeps its timing and names the look's and the parts' dyes.
    /// </summary>
    [Test]
    public void Build_MakesASpritePerFrame_PivotedOnTheFeet()
    {
        CharacterLayerFrame a = Frame(new RectInt(30, 12, 4, 6), Filled(24, 0), new byte[24], new byte[24], 1, Depths(24));
        CharacterLayerFrame b = Frame(new RectInt(28, 10, 3, 3), Filled(9, 1), new byte[9], new byte[9], 1, Depths(9));
        CharacterLayer body = new(new[] { new CharacterLayerClip(new Vector2Int(64, 64), new Vector2Int(32, 12), new[] { a, b }, Materials(false)) });
        CharacterAnimationSet look = Track(CharacterAnimationSet.Create(new[] { CharacterAnimationClip.Create("Idle", CharacterGait.Idle, false, new Sprite[2], new[] { 0.5f, 0.25f }, 0f) }, 1 << (int)CharacterDye.Skin));

        CharacterOutfitFrames frames = new CharacterComposer().ComposeAll(body, new CharacterLayer[] { null }, Gap);
        CharacterOutfit outfit = CharacterComposer.Build(frames, look, 1 << (int)CharacterDye.Tunic, "Test");
        Track(outfit.Set);
        Track(outfit.Sheet);
        Track(outfit.PaletteMap);

        Assert.That(frames.Clips[0][0].Box, Is.EqualTo(new RectInt(29, 11, 6, 8)), "the frame's pixels and their outline");
        CharacterAnimationClip clip = outfit.Set.Clips[0];
        Assert.That(clip.Name, Is.EqualTo("Idle"));
        Assert.That(clip.FrameSeconds(1), Is.EqualTo(0.25f));
        Assert.That(outfit.Set.Dyes, Is.EqualTo((1 << (int)CharacterDye.Skin) | (1 << (int)CharacterDye.Tunic)));
        Sprite sprite = clip.SpriteAt(0, 0);
        Track(sprite);
        Track(clip.SpriteAt(0, 1));
        Assert.That(sprite.texture, Is.EqualTo(outfit.Sheet));
        Assert.That(sprite.rect.size, Is.EqualTo(new Vector2(6f, 8f)));
        ExpectPivot(sprite, new Vector2(32 - 29, 12 - 11));
        Assert.That(sprite.pixelsPerUnit, Is.EqualTo(CharacterOutfit.PixelsPerUnit));
        ExpectPivot(clip.SpriteAt(0, 1), new Vector2(32 - 27, 12 - 9));
        Assert.That(sprite.GetSecondaryTextureCount(), Is.EqualTo(1));
    }

    /// <summary>
    /// A layer's file keeps a material table per clip, or none (Docs/PlayerCharacter.md, Phase 7): a weapon's gear holds
    /// one only for the clips it draws the weapon stowed in, and they read back so.
    /// </summary>
    [Test]
    public void ALayersFile_KeepsATablePerClip()
    {
        CharacterLayerFrame frame = Frame(new RectInt(0, 0, 1, 1), Filled(1, 2), new byte[1], new byte[1], 1, Depths(1));
        CharacterLayer layer = new(new[]
        {
            new CharacterLayerClip(new Vector2Int(64, 64), new Vector2Int(32, 12), new[] { frame }, Materials(true)),
            new CharacterLayerClip(new Vector2Int(80, 80), new Vector2Int(40, 22), new[] { frame }, null)
        });

        CharacterLayer read = CharacterLayer.Read(layer.Write());

        Assert.That(read.Clips[0].Materials, Is.Not.Null);
        Assert.That(read.Clips[0].Materials[2].Ramp, Is.EqualTo(Grey));
        Assert.That(read.Clips[1].Materials, Is.Null);
        Assert.That(read.Clips[1].FrameSize, Is.EqualTo(new Vector2Int(80, 80)));
    }

    /// <summary>
    /// A clip's frames are coloured with the table of the last layer that has one for it, as a weapon's gear has where it
    /// draws the weapon stowed, else the body's: composed, the gear's grey takes its table's red.
    /// </summary>
    [Test]
    public void MaterialsOf_TakesTheLastLayersTable_ElseTheBodys()
    {
        CharacterLayerMaterial[] body = Materials(false);
        CharacterLayerMaterial[] gear = { body[0], body[1], new CharacterLayerMaterial(true, CharacterDye.None, Red) };
        CharacterLayerFrame frame = Frame(new RectInt(2, 2, 1, 1), Filled(1, 2), new byte[1], new byte[1], 1, Depths(1));
        CharacterLayerClip bodyClip = new(new Vector2Int(5, 5), Vector2Int.zero, new[] { frame }, body);
        CharacterLayerClip withTable = new(new Vector2Int(5, 5), Vector2Int.zero, new[] { frame }, gear);
        CharacterLayerClip without = new(new Vector2Int(5, 5), Vector2Int.zero, new[] { frame }, null);

        Assert.That(CharacterComposer.MaterialsOf(new[] { bodyClip, without }), Is.SameAs(body));
        Assert.That(CharacterComposer.MaterialsOf(new[] { bodyClip, withTable, null }), Is.SameAs(gear));

        CharacterOutfitFrames frames = new CharacterComposer().ComposeAll(new CharacterLayer(new[] { bodyClip }), new[] { new CharacterLayer(new[] { withTable }) }, Gap);
        CharacterOutfitFrame composed = frames.Clips[0][0];
        Assert.That(composed.Pixels[(2 - composed.Box.y) * composed.Box.width + 2 - composed.Box.x], Is.EqualTo(Red.Tone(1)));
    }

    /// <summary>
    /// An outfit built a few sprites at a time (Docs/PlayerCharacter.md, Phase 7) has its texture from the start, makes
    /// at most as many sprites a step as asked, skips a clip without frames, and finishes with the same frames as one
    /// built at once; a build given up destroys what it made.
    /// </summary>
    [Test]
    public void BuildingInSteps_MakesTheSameOutfit_AndAnAbortDestroysIt()
    {
        CharacterLayerFrame a = Frame(new RectInt(30, 12, 4, 6), Filled(24, 0), new byte[24], new byte[24], 1, Depths(24));
        CharacterLayerFrame b = Frame(new RectInt(28, 10, 3, 3), Filled(9, 1), new byte[9], new byte[9], 1, Depths(9));
        CharacterLayer body = new(new[]
        {
            new CharacterLayerClip(new Vector2Int(64, 64), new Vector2Int(32, 12), new[] { a, b, a }, Materials(false)),
            new CharacterLayerClip(new Vector2Int(64, 64), new Vector2Int(32, 12), new CharacterLayerFrame[0], Materials(false)),
            new CharacterLayerClip(new Vector2Int(64, 64), new Vector2Int(32, 12), new[] { b }, Materials(false))
        });
        CharacterAnimationSet look = Track(CharacterAnimationSet.Create(new[]
        {
            CharacterAnimationClip.Create("Walk", CharacterGait.Walk, false, new Sprite[3], new[] { 0.1f, 0.1f, 0.1f }, 1f),
            CharacterAnimationClip.Create("Empty", CharacterGait.None, false, new Sprite[0], new float[0], 0f),
            CharacterAnimationClip.Create("Idle", CharacterGait.Idle, false, new Sprite[1], new[] { 0.5f }, 0f)
        }));
        CharacterOutfitFrames frames = new CharacterComposer().ComposeAll(body, new CharacterLayer[0], Gap);

        CharacterOutfitBuild build = CharacterComposer.BeginBuild(CharacterComposer.Pack(frames), look, 0, "Stepped");
        Assert.That(build.Step(2), Is.False);
        Assert.That(build.Step(1), Is.False, "the third sprite, the walk's last");
        Assert.That(build.Step(5), Is.True, "the empty clip skipped, the idle's sprite made");
        CharacterOutfit stepped = build.Finish();
        CharacterOutfit whole = CharacterComposer.Build(frames, look, 0, "Whole");
        Assert.That(stepped.Sheet.width, Is.EqualTo(whole.Sheet.width));
        Assert.That(stepped.Sheet.height, Is.EqualTo(whole.Sheet.height));
        for (int c = 0; c < look.Clips.Count; c++)
        {
            for (int f = 0; f < look.Clips[c].FrameCount; f++)
            {
                Assert.That(stepped.Set.Clips[c].SpriteAt(0, f).rect, Is.EqualTo(whole.Set.Clips[c].SpriteAt(0, f).rect), $"clip {c} frame {f}");
            }
        }

        Assert.That(stepped.Set.SameClipsAs(look), Is.True);
        stepped.Destroy();
        whole.Destroy();

        CharacterOutfitBuild abandoned = CharacterComposer.BeginBuild(CharacterComposer.Pack(frames), look, 0, "Abandoned");
        abandoned.Step(2);
        Sprite made = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(sprite => sprite.name.StartsWith("Abandoned"));
        Assert.That(made, Is.Not.Null);
        abandoned.Abort();
        Assert.That(made == null, Is.True, "the sprites made are destroyed");
    }

    /// <summary>
    /// Asserts that <paramref name="sprite"/>'s pivot is <paramref name="pixels"/> from its rect's bottom left.
    /// </summary>
    private static void ExpectPivot(Sprite sprite, Vector2 pixels)
    {
        Assert.That(sprite.pivot.x, Is.EqualTo(pixels.x).Within(1e-4f), sprite.name);
        Assert.That(sprite.pivot.y, Is.EqualTo(pixels.y).Within(1e-4f), sprite.name);
    }

    /// <summary>
    /// <paramref name="bytes"/> gzipped.
    /// </summary>
    private static byte[] Gzipped(byte[] bytes)
    {
        using MemoryStream output = new();
        using (System.IO.Compression.GZipStream zip = new(output, System.IO.Compression.CompressionLevel.Fastest, true))
        {
            zip.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }

    /// <summary>
    /// A frame of <paramref name="box"/> drawn in step <paramref name="step"/>.
    /// </summary>
    private static CharacterLayerFrame Frame(RectInt box, byte[] material, byte[] part, byte[] tone, byte step, float[] depth)
    {
        byte[] steps = new byte[material.Length];
        for (int i = 0; i < steps.Length; i++)
        {
            steps[i] = step;
        }

        return new CharacterLayerFrame(box, material, part, tone, steps, depth);
    }

    /// <summary>
    /// <paramref name="count"/> pixels of <paramref name="material"/>.
    /// </summary>
    private static byte[] Filled(int count, byte material)
    {
        byte[] materials = new byte[count];
        for (int i = 0; i < count; i++)
        {
            materials[i] = material;
        }

        return materials;
    }

    /// <summary>
    /// <paramref name="count"/> depths of 0.
    /// </summary>
    private static float[] Depths(int count)
    {
        return new float[count];
    }

    /// <summary>
    /// A material table: red skin and blue tunic, both outlined, and grey, outlined only when
    /// <paramref name="greyUnlined"/> is false.
    /// </summary>
    private static CharacterLayerMaterial[] Materials(bool greyUnlined)
    {
        return new[]
        {
            new CharacterLayerMaterial(true, CharacterDye.Skin, Red),
            new CharacterLayerMaterial(true, CharacterDye.Tunic, Blue),
            new CharacterLayerMaterial(!greyUnlined, CharacterDye.None, Grey)
        };
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
