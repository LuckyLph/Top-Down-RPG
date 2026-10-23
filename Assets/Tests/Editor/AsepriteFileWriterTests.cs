using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.U2D.Aseprite;
using UnityEngine;

/// <summary>
/// Tests for <see cref="AsepriteFileWriter"/> (Docs/AsepriteCharacters.md, Phase 0), read back through the Aseprite
/// importer's own reader (<see cref="AsepriteReader"/>): the header, layers, frame durations, tags, cels and their user
/// data come back exactly as written; a whole canvas is cropped to its opaque pixels as Aseprite saves it, and an empty
/// one leaves no cel; the rogue's baked Run and SwordSlash written in the plan's conventions read back as their sheets:
/// the canvas the size of a frame, eight direction tags in order, each frame's duration in whole milliseconds, the
/// contact marker on the contact frame in every direction and nowhere else, and every frame's pixels.
/// </summary>
public class AsepriteFileWriterTests
{
    private const string RecipePath = "Assets/Settings/Characters/Recipe_Rogue.asset";

    private readonly List<string> paths = new();

    /// <summary>
    /// Deletes every file the test wrote.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (string path in paths)
        {
            File.Delete(path);
        }

        paths.Clear();
    }

    /// <summary>
    /// A file of two layers (one hidden), three frames of their own durations, two tags and cels with and without user
    /// data reads back with every value and pixel as written.
    /// </summary>
    [Test]
    public void ReadFile_WrittenFile_ReadsBackAsWritten()
    {
        AsepriteFileWriter writer = new(6, 5);
        int body = writer.AddLayer("Body");
        int guide = writer.AddLayer("Guide", false);
        writer.AddFrame(100);
        writer.AddFrame(40);
        writer.AddFrame(250);
        writer.AddTag("E", 0, 1);
        writer.AddTag("W", 2, 2);
        Color32[] first = Pixels(3, 2, 1);
        Color32[] second = Pixels(2, 4, 50);
        Color32[] third = Pixels(6, 5, 100);
        writer.SetCel(body, 0, new RectInt(1, 2, 3, 2), first);
        writer.SetCel(body, 1, new RectInt(4, 0, 2, 4), second, AsepriteFileWriter.ContactMarker);
        writer.SetCel(guide, 1, new RectInt(-2, 3, 6, 5), third, "guide");

        string path = Write(writer);
        using AsepriteFile file = AsepriteReader.ReadFile(path);

        Assert.That(file, Is.Not.Null);
        Assert.That(file.fileSize, Is.EqualTo(new FileInfo(path).Length));
        Assert.That(file.width, Is.EqualTo(6));
        Assert.That(file.height, Is.EqualTo(5));
        Assert.That(file.colorDepth, Is.EqualTo(32));
        Assert.That(file.noOfFrames, Is.EqualTo(3));
        Assert.That(file.frameData.Select(f => (int)f.frameDuration), Is.EqualTo(new[] { 100, 40, 250 }));

        LayerChunk[] layers = file.frameData[0].chunks.OfType<LayerChunk>().ToArray();
        Assert.That(layers.Select(l => l.name), Is.EqualTo(new[] { "Body", "Guide" }));
        Assert.That(layers.Select(l => (l.flags & LayerFlags.Visible) != 0), Is.EqualTo(new[] { true, false }));
        Assert.That(layers.All(l => l.layerType == LayerTypes.Normal && l.blendMode == BlendModes.Normal && l.opacity == 255 && l.childLevel == 0), Is.True);

        TagData[] tags = file.frameData.SelectMany(f => f.chunks.OfType<TagsChunk>()).SelectMany(t => t.tagData).ToArray();
        Assert.That(tags.Select(t => (t.name, (int)t.fromFrame, (int)t.toFrame, t.loopDirection)), Is.EqualTo(new[] { ("E", 0, 1, LoopAnimationDirection.Forward), ("W", 2, 2, LoopAnimationDirection.Forward) }));

        Assert.That(file.frameData[2].chunks.OfType<CellChunk>(), Is.Empty);
        CellChunk firstCel = file.frameData[0].chunks.OfType<CellChunk>().Single();
        AssertCel(firstCel, body, new RectInt(1, 2, 3, 2), first, null);
        CellChunk[] secondCels = file.frameData[1].chunks.OfType<CellChunk>().ToArray();
        Assert.That(secondCels.Length, Is.EqualTo(2));
        AssertCel(secondCels[0], body, new RectInt(4, 0, 2, 4), second, AsepriteFileWriter.ContactMarker);
        AssertCel(secondCels[1], guide, new RectInt(-2, 3, 6, 5), third, "guide");
    }

    /// <summary>
    /// A whole canvas becomes a cel over its pixels that are not fully transparent; a canvas with none leaves the frame
    /// without a cel.
    /// </summary>
    [Test]
    public void SetImage_CropsToOpaquePixels_AndLeavesNoCelForAnEmptyCanvas()
    {
        AsepriteFileWriter writer = new(5, 4);
        int layer = writer.AddLayer("Body");
        writer.AddFrame(100);
        writer.AddFrame(100);
        Color32[] canvas = new Color32[5 * 4];
        canvas[1 * 5 + 1] = new Color32(10, 20, 30, 255);
        canvas[2 * 5 + 3] = new Color32(40, 50, 60, 1);

        Assert.That(writer.SetImage(layer, 0, canvas), Is.True);
        Assert.That(writer.SetImage(layer, 1, new Color32[5 * 4]), Is.False);

        using AsepriteFile file = AsepriteReader.ReadFile(Write(writer));
        CellChunk cel = file.frameData[0].chunks.OfType<CellChunk>().Single();
        Color32[] expected = { canvas[1 * 5 + 1], default, default, default, default, canvas[2 * 5 + 3] };
        AssertCel(cel, layer, new RectInt(1, 1, 3, 2), expected, null);
        Assert.That(file.frameData[1].chunks.OfType<CellChunk>(), Is.Empty);
    }

    /// <summary>
    /// The rogue's baked Run and SwordSlash, written from their sheets, read back in the plan's conventions: a canvas the
    /// size of a frame, one visible layer, eight forward tags named after the directions in order over each direction's
    /// frames, each frame's duration in whole milliseconds, the contact marker only on the contact frame's cel in every
    /// direction, and each frame's cel holding exactly that frame's pixels.
    /// </summary>
    [Test]
    public void FromSheet_RoguesRunAndSwordSlash_FollowTheConventions()
    {
        CharacterRecipe recipe = AssetDatabase.LoadAssetAtPath<CharacterRecipe>(RecipePath);
        Assert.That(recipe, Is.Not.Null);
        CharacterBaker baker = new(recipe);
        foreach (string clipName in new[] { "Run", "SwordSlash" })
        {
            CharacterClip clip = recipe.Clips.Single(c => c.Name == clipName);
            CharacterSheet sheet = baker.Bake(clip);
            Vector2Int size = clip.FrameSize;
            int frames = sheet.FrameCount;

            using AsepriteFile file = AsepriteReader.ReadFile(Write(AsepriteFileWriter.FromSheet(sheet)));

            Assert.That((file.width, file.height), Is.EqualTo(((ushort)size.x, (ushort)size.y)), clipName);
            Assert.That(file.noOfFrames, Is.EqualTo(CharacterDirections.Count * frames), clipName);
            LayerChunk layer = file.frameData[0].chunks.OfType<LayerChunk>().Single();
            Assert.That((layer.flags & LayerFlags.Visible) != 0, Is.True, clipName);
            TagData[] tags = file.frameData[0].chunks.OfType<TagsChunk>().Single().tagData.ToArray();
            Assert.That(tags.Select(t => t.name), Is.EqualTo(Enumerable.Range(0, CharacterDirections.Count).Select(CharacterDirections.Name)), clipName);
            for (int direction = 0; direction < CharacterDirections.Count; direction++)
            {
                TagData tag = tags[direction];
                Assert.That((tag.fromFrame, tag.toFrame, tag.loopDirection), Is.EqualTo(((ushort)(direction * frames), (ushort)(direction * frames + frames - 1), LoopAnimationDirection.Forward)), clipName);
                for (int frame = 0; frame < frames; frame++)
                {
                    string where = $"{clipName} {CharacterDirections.Name(direction)} {frame}";
                    FrameData data = file.frameData[direction * frames + frame];
                    Assert.That(data.frameDuration, Is.EqualTo(Mathf.RoundToInt(clip.Motion.FrameSeconds(frame) * 1000f)), where);
                    CellChunk cel = data.chunks.OfType<CellChunk>().Single();
                    string marker = cel.dataChunk?.text;
                    Assert.That(marker, Is.EqualTo(frame == clip.Motion.ContactFrame ? AsepriteFileWriter.ContactMarker : null), where);
                    Assert.That(Canvas(cel, size).SequenceEqual(SheetFrame(sheet, direction, frame)), Is.True, where);
                }
            }
        }
    }

    /// <summary>
    /// Writes <paramref name="writer"/>'s file to a new path under the project's Temp folder, deleted after the test.
    /// </summary>
    private string Write(AsepriteFileWriter writer)
    {
        string path = FileUtil.GetUniqueTempPathInProject() + ".aseprite";
        paths.Add(path);
        writer.Write(path);
        return path;
    }

    /// <summary>
    /// Checks a cel read back: its layer, place, size, compressed type, full opacity, pixels and user data text.
    /// </summary>
    private static void AssertCel(CellChunk cel, int layer, RectInt rect, Color32[] pixels, string userData)
    {
        Assert.That(cel.layerIndex, Is.EqualTo(layer));
        Assert.That((cel.posX, cel.posY, cel.width, cel.height), Is.EqualTo(((short)rect.x, (short)rect.y, (ushort)rect.width, (ushort)rect.height)));
        Assert.That(cel.cellType, Is.EqualTo(CellTypes.CompressedImage));
        Assert.That(cel.opacity, Is.EqualTo(255));
        Assert.That(Packed(cel.image.ToArray()), Is.EqualTo(Packed(pixels)));
        Assert.That(cel.dataChunk?.text, Is.EqualTo(userData));
    }

    /// <summary>
    /// <paramref name="width"/> by <paramref name="height"/> pixels of distinct colours and alphas from
    /// <paramref name="seed"/>.
    /// </summary>
    private static Color32[] Pixels(int width, int height, int seed)
    {
        return Enumerable.Range(0, width * height).Select(i => new Color32((byte)(seed + i), (byte)(seed * 3 + i * 7), (byte)(255 - i), (byte)(i % 3 == 0 ? 255 : 30 + i))).ToArray();
    }

    /// <summary>
    /// The canvas a single cel covers, row by row from the top, transparent outside it.
    /// </summary>
    private static int[] Canvas(CellChunk cel, Vector2Int size)
    {
        int[] canvas = new int[size.x * size.y];
        int[] image = Packed(cel.image.ToArray());
        for (int y = 0; y < cel.height; y++)
        {
            for (int x = 0; x < cel.width; x++)
            {
                canvas[(cel.posY + y) * size.x + cel.posX + x] = image[y * cel.width + x];
            }
        }

        return canvas;
    }

    /// <summary>
    /// Frame <paramref name="frame"/> of <paramref name="direction"/> on <paramref name="sheet"/>, row by row from the top.
    /// </summary>
    private static int[] SheetFrame(CharacterSheet sheet, int direction, int frame)
    {
        Vector2Int size = sheet.Clip.FrameSize;
        RectInt rect = CharacterSheet.FrameRect(direction, frame, size);
        Color32[] pixels = new Color32[size.x * size.y];
        for (int y = 0; y < size.y; y++)
        {
            System.Array.Copy(sheet.Pixels, (rect.y + size.y - 1 - y) * sheet.Width + rect.x, pixels, y * size.x, size.x);
        }

        return Packed(pixels);
    }

    /// <summary>
    /// Each pixel as one number, fully transparent pixels as 0 whatever their colour.
    /// </summary>
    private static int[] Packed(Color32[] pixels)
    {
        return pixels.Select(p => p.a == 0 ? 0 : p.r | p.g << 8 | p.b << 16 | p.a << 24).ToArray();
    }
}
