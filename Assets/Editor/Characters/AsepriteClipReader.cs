using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.U2D.Aseprite;
using UnityEngine;

/// <summary>
/// Reads one clip's <c>.aseprite</c> file in the conventions of Docs/AsepriteCharacters.md with the Aseprite importer's
/// own parser (<see cref="AsepriteReader"/>) and draws its frames into a sheet laid out as the baker's
/// (<see cref="CharacterSheet.FrameRect"/>: a row per direction, east on top, a column per frame), checking it on the
/// way. Each direction is the tag named after it (<see cref="CharacterDirections.Name"/>); other tags are ignored. A
/// frame is its visible image layers drawn in layer order as Aseprite draws them: normal blending at the cel's opacity
/// times the layer's, a linked cel drawn from the cel it links to; hidden layers, layers in a hidden group and reference
/// layers are left out, and transparent pixels are written clear. A cel's z-index is not read (the importer's parser
/// keeps it to itself), so layers always draw in their order. The contact (release) frame is the one with the user data
/// <see cref="ContactMarker"/> on any of its cels, hidden or not. Every problem found is reported naming the file, and
/// the tag and frame where it has one, frames counted from 1 as Aseprite shows them; a clip with a problem gives no
/// sheet.
/// </summary>
public static class AsepriteClipReader
{
    public const string ContactMarker = "contact";

    /// <summary>
    /// Reads the clip at <paramref name="path"/>, a gait (no frame marked) or, for <see cref="CharacterGait.None"/>, an
    /// attack or a cast (one frame marked in every direction, the same one), whose feet stand on the
    /// <paramref name="feet"/> pixel from the canvas's bottom left.
    /// </summary>
    public static AsepriteClipFrames Read(string path, CharacterGait gait, Vector2Int feet)
    {
        string file = Path.GetFileName(path);
        List<string> problems = new();
        if (!File.Exists(path))
        {
            problems.Add($"{file}: not found at {path}.");
            return AsepriteClipFrames.Failed(problems);
        }

        using AsepriteFile parsed = HasAsepriteHeader(path) ? AsepriteReader.ReadFile(path) : null;
        if (parsed == null)
        {
            problems.Add($"{file}: could not be read as an Aseprite file.");
            return AsepriteClipFrames.Failed(problems);
        }

        Vector2Int size = new(parsed.width, parsed.height);
        if (feet.x < 0 || feet.y < 0 || feet.x >= size.x || feet.y >= size.y)
        {
            problems.Add($"{file}: the feet pixel ({feet.x}, {feet.y}) lies outside its {size.x} x {size.y} canvas.");
        }

        List<LayerChunk> layers = parsed.frameData.SelectMany(f => f.chunks.OfType<LayerChunk>()).ToList();
        bool[] drawn = DrawnLayers(file, layers, problems);
        TagData[] directions = DirectionTags(file, parsed, problems);
        if (directions == null)
        {
            return AsepriteClipFrames.Failed(problems);
        }

        int frames = directions[0].toFrame - directions[0].fromFrame + 1;
        bool sameLength = true;
        for (int direction = 1; direction < CharacterDirections.Count; direction++)
        {
            int count = directions[direction].toFrame - directions[direction].fromFrame + 1;
            if (count != frames)
            {
                problems.Add($"{file}, tag {CharacterDirections.Name(direction)}: {count} frames, but {frames} in tag {CharacterDirections.Name(0)}.");
                sameLength = false;
            }
        }

        if (!sameLength)
        {
            return AsepriteClipFrames.Failed(problems);
        }

        int[] milliseconds = new int[frames];
        int contact = -1;
        for (int direction = 0; direction < CharacterDirections.Count; direction++)
        {
            string tag = CharacterDirections.Name(direction);
            List<int> marked = new();
            for (int frame = 0; frame < frames; frame++)
            {
                int index = directions[direction].fromFrame + frame;
                int duration = parsed.frameData[index].frameDuration;
                if (direction == 0)
                {
                    milliseconds[frame] = duration;
                }
                else if (duration != milliseconds[frame])
                {
                    problems.Add($"{Where(file, tag, frame, index)}: {duration} ms, but {milliseconds[frame]} ms in tag {CharacterDirections.Name(0)}.");
                }

                if (IsMarked(parsed, index))
                {
                    marked.Add(frame);
                }
            }

            if (gait != CharacterGait.None)
            {
                foreach (int frame in marked)
                {
                    problems.Add($"{Where(file, tag, frame, directions[direction].fromFrame + frame)}: marked {ContactMarker}, but a {gait.ToString().ToLowerInvariant()} has no contact frame.");
                }
            }
            else if (marked.Count == 0)
            {
                problems.Add($"{file}, tag {tag}: no frame is marked {ContactMarker}; an attack or a cast marks its contact (release) frame.");
            }
            else if (marked.Count > 1)
            {
                problems.Add($"{file}, tag {tag}: frames {string.Join(" and ", marked.Select(f => f + 1))} are each marked {ContactMarker}; mark one.");
            }
            else if (direction == 0)
            {
                contact = marked[0];
            }
            else if (contact >= 0 && marked[0] != contact)
            {
                problems.Add($"{file}, tag {tag}: frame {marked[0] + 1} is marked {ContactMarker}, but frame {contact + 1} in tag {CharacterDirections.Name(0)}.");
            }
        }

        Color32[] sheet = new Color32[frames * size.x * CharacterDirections.Count * size.y];
        Color32[] canvas = new Color32[size.x * size.y];
        for (int direction = 0; direction < CharacterDirections.Count; direction++)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                int index = directions[direction].fromFrame + frame;
                string where = Where(file, CharacterDirections.Name(direction), frame, index);
                Draw(parsed, index, layers, drawn, size, canvas, where, problems);
                RectInt rect = CharacterSheet.FrameRect(direction, frame, size);
                bool empty = true;
                for (int y = 0; y < size.y; y++)
                {
                    for (int x = 0; x < size.x; x++)
                    {
                        Color32 pixel = canvas[y * size.x + x];
                        empty &= pixel.a == 0;
                        sheet[(rect.y + size.y - 1 - y) * frames * size.x + rect.x + x] = pixel.a == 0 ? default : pixel;
                    }
                }

                if (empty)
                {
                    problems.Add($"{where}: empty.");
                }
            }
        }

        if (problems.Count > 0)
        {
            return AsepriteClipFrames.Failed(problems);
        }

        return new AsepriteClipFrames(size, frames, sheet, milliseconds.Select(ms => ms / 1000f).ToArray(), contact, problems);
    }

    /// <summary>
    /// Whether the file at <paramref name="path"/> is at least a header long and holds the Aseprite magic number, so a
    /// file that is not one is reported without the parser logging an error.
    /// </summary>
    private static bool HasAsepriteHeader(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] header = new byte[6];
        return stream.Length >= 128 && stream.Read(header, 0, header.Length) == header.Length && header[4] == 0xE0 && header[5] == 0xA5;
    }

    /// <summary>
    /// Where a frame is: the file, the tag and the frame in it, and the frame in the file, counted from 1 as Aseprite
    /// shows them.
    /// </summary>
    private static string Where(string file, string tag, int frame, int index)
    {
        return $"{file}, tag {tag}, frame {frame + 1} (Aseprite frame {index + 1})";
    }

    /// <summary>
    /// Which layers are drawn: the visible image layers whose groups are all visible, reference layers left out.
    /// Reports a drawn layer or one of its groups that does not blend normally (or a group not at full opacity, which
    /// Aseprite would draw on its own first) and a visible tilemap layer, neither of which is drawn here.
    /// </summary>
    private static bool[] DrawnLayers(string file, IReadOnlyList<LayerChunk> layers, List<string> problems)
    {
        bool[] drawn = new bool[layers.Count];
        int[] parents = new int[layers.Count];
        Dictionary<int, int> lastAtLevel = new();
        for (int i = 0; i < layers.Count; i++)
        {
            LayerChunk layer = layers[i];
            parents[i] = layer.childLevel == 0 || !lastAtLevel.TryGetValue(layer.childLevel - 1, out int parent) ? -1 : parent;
            lastAtLevel[layer.childLevel] = i;
        }

        HashSet<int> groupsReported = new();
        for (int i = 0; i < layers.Count; i++)
        {
            LayerChunk layer = layers[i];
            if (layer.layerType == LayerTypes.Group || !Shows(layers, parents, i))
            {
                continue;
            }

            if (layer.layerType == LayerTypes.Tilemap)
            {
                problems.Add($"{file}, layer {layer.name}: a tilemap layer, which is not drawn; hide it or draw it as an image layer.");
                continue;
            }

            if (layer.blendMode != BlendModes.Normal)
            {
                problems.Add($"{file}, layer {layer.name}: blends by {layer.blendMode}; only normal layers are drawn.");
            }

            for (int group = parents[i]; group >= 0; group = parents[group])
            {
                LayerChunk groupLayer = layers[group];
                if ((groupLayer.blendMode != BlendModes.Normal || groupLayer.opacity != 255) && groupsReported.Add(group))
                {
                    problems.Add($"{file}, group {groupLayer.name}: blends by {groupLayer.blendMode} at opacity {groupLayer.opacity}; a group must blend normally at full opacity.");
                }
            }

            drawn[i] = true;
        }

        return drawn;
    }

    /// <summary>
    /// Whether layer <paramref name="index"/> shows: visible, not a reference layer, and in visible groups only.
    /// </summary>
    private static bool Shows(IReadOnlyList<LayerChunk> layers, int[] parents, int index)
    {
        for (int i = index; i >= 0; i = parents[i])
        {
            LayerFlags flags = layers[i].flags;
            if ((flags & LayerFlags.Visible) == 0 || (flags & LayerFlags.ReferenceLayer) != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The tag of each direction, in <see cref="CharacterDirections"/> order, or null after reporting a direction
    /// without a tag or with two. Also reports a direction tag that does not play forward, reaches past the frames, or
    /// overlaps another direction's.
    /// </summary>
    private static TagData[] DirectionTags(string file, AsepriteFile parsed, List<string> problems)
    {
        List<TagData> tags = parsed.frameData.SelectMany(f => f.chunks.OfType<TagsChunk>()).SelectMany(t => t.tagData).ToList();
        TagData[] directions = new TagData[CharacterDirections.Count];
        bool complete = true;
        for (int direction = 0; direction < CharacterDirections.Count; direction++)
        {
            string name = CharacterDirections.Name(direction);
            List<TagData> named = tags.Where(t => t.name == name).ToList();
            if (named.Count != 1)
            {
                problems.Add(named.Count == 0 ? $"{file}: no tag {name}; every direction has a tag named E, NE, N, NW, W, SW, S or SE." : $"{file}: {named.Count} tags named {name}.");
                complete = false;
                continue;
            }

            TagData tag = named[0];
            directions[direction] = tag;
            if (tag.loopDirection != LoopAnimationDirection.Forward)
            {
                problems.Add($"{file}, tag {name}: plays {tag.loopDirection}; direction tags play forward.");
            }

            if (tag.toFrame < tag.fromFrame || tag.toFrame >= parsed.noOfFrames)
            {
                problems.Add($"{file}, tag {name}: covers frames {tag.fromFrame + 1} to {tag.toFrame + 1} of {parsed.noOfFrames}.");
                complete = false;
            }
        }

        if (!complete)
        {
            return null;
        }

        for (int a = 0; a < CharacterDirections.Count; a++)
        {
            for (int b = a + 1; b < CharacterDirections.Count; b++)
            {
                if (directions[a].fromFrame <= directions[b].toFrame && directions[b].fromFrame <= directions[a].toFrame)
                {
                    problems.Add($"{file}: tags {CharacterDirections.Name(a)} and {CharacterDirections.Name(b)} overlap.");
                }
            }
        }

        return directions;
    }

    /// <summary>
    /// Whether any cel of frame <paramref name="index"/>, or a cel a linked cel there links to, holds the user data
    /// <see cref="ContactMarker"/>.
    /// </summary>
    private static bool IsMarked(AsepriteFile parsed, int index)
    {
        foreach (CellChunk cel in parsed.frameData[index].chunks.OfType<CellChunk>())
        {
            CellChunk own = Resolve(parsed, cel);
            if (own?.dataChunk != null && own.dataChunk.text != null && own.dataChunk.text.Trim() == ContactMarker)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The cel holding <paramref name="cel"/>'s image and user data: itself, or for a linked cel the cel of the same
    /// layer on the frame it links to (null when there is none).
    /// </summary>
    private static CellChunk Resolve(AsepriteFile parsed, CellChunk cel)
    {
        if (cel.cellType != CellTypes.LinkedCell)
        {
            return cel;
        }

        int frame = cel.linkedToFrame;
        if (frame < 0 || frame >= parsed.noOfFrames)
        {
            return null;
        }

        return parsed.frameData[frame].chunks.OfType<CellChunk>().FirstOrDefault(c => c.layerIndex == cel.layerIndex && c.cellType != CellTypes.LinkedCell);
    }

    /// <summary>
    /// Draws frame <paramref name="index"/> onto <paramref name="canvas"/> (rows from the top), cleared first: each
    /// drawn layer's cel in layer order, blended normally at the cel's opacity times the layer's. Reports the first
    /// pixel a layer draws past the canvas, in Aseprite's coordinates from the canvas's top left.
    /// </summary>
    private static void Draw(AsepriteFile parsed, int index, IReadOnlyList<LayerChunk> layers, bool[] drawn, Vector2Int size, Color32[] canvas, string where, List<string> problems)
    {
        Array.Clear(canvas, 0, canvas.Length);
        List<CellChunk> cels = parsed.frameData[index].chunks.OfType<CellChunk>().Where(c => c.layerIndex < layers.Count && drawn[c.layerIndex]).OrderBy(c => c.layerIndex).ToList();
        foreach (CellChunk cel in cels)
        {
            CellChunk own = Resolve(parsed, cel);
            if (own == null || !own.image.IsCreated)
            {
                continue;
            }

            int opacity = MultiplyUnit(own.opacity, layers[cel.layerIndex].opacity);
            bool reportedPast = false;
            for (int y = 0; y < own.height; y++)
            {
                for (int x = 0; x < own.width; x++)
                {
                    Color32 source = own.image[y * own.width + x];
                    int cx = own.posX + x;
                    int cy = own.posY + y;
                    if (cx < 0 || cy < 0 || cx >= size.x || cy >= size.y)
                    {
                        if (source.a > 0 && opacity > 0 && !reportedPast)
                        {
                            problems.Add($"{where}: layer {layers[cel.layerIndex].name} draws past the canvas, at ({cx}, {cy}).");
                            reportedPast = true;
                        }

                        continue;
                    }

                    int i = cy * size.x + cx;
                    canvas[i] = BlendNormal(canvas[i], source, opacity);
                }
            }
        }
    }

    /// <summary>
    /// Aseprite's normal blend (<c>rgba_blender_normal</c>) of <paramref name="source"/> over
    /// <paramref name="backdrop"/> at <paramref name="opacity"/> (0 to 255), in its integer arithmetic.
    /// </summary>
    private static Color32 BlendNormal(Color32 backdrop, Color32 source, int opacity)
    {
        if (backdrop.a == 0)
        {
            return new Color32(source.r, source.g, source.b, (byte)MultiplyUnit(source.a, opacity));
        }

        if (source.a == 0)
        {
            return backdrop;
        }

        int sa = MultiplyUnit(source.a, opacity);
        int ra = sa + backdrop.a - MultiplyUnit(backdrop.a, sa);
        int r = backdrop.r + (source.r - backdrop.r) * sa / ra;
        int g = backdrop.g + (source.g - backdrop.g) * sa / ra;
        int b = backdrop.b + (source.b - backdrop.b) * sa / ra;
        return new Color32((byte)r, (byte)g, (byte)b, (byte)ra);
    }

    /// <summary>
    /// <paramref name="a"/> times <paramref name="b"/> over 255, rounded as pixman's <c>MUL_UN8</c>, which Aseprite
    /// uses.
    /// </summary>
    private static int MultiplyUnit(int a, int b)
    {
        int t = a * b + 0x80;
        return ((t >> 8) + t) >> 8;
    }
}

/// <summary>
/// One clip read by <see cref="AsepriteClipReader"/>: its canvas size, its frames a direction, its sheet's pixels (rows
/// from the bottom, a row of frames per direction, east on top), each frame's duration in seconds, its contact
/// (release) frame or -1, and the problems found; a clip with problems has no sheet.
/// </summary>
public sealed class AsepriteClipFrames
{
    /// <summary>
    /// Creates a clip read whole, or one with <paramref name="clipProblems"/>.
    /// </summary>
    public AsepriteClipFrames(Vector2Int frameSize, int frameCount, Color32[] pixels, float[] frameSeconds, int contactFrame, IReadOnlyList<string> clipProblems)
    {
        FrameSize = frameSize;
        FrameCount = frameCount;
        Pixels = pixels;
        FrameSeconds = frameSeconds;
        ContactFrame = contactFrame;
        Problems = clipProblems;
    }

    public Vector2Int FrameSize { get; }
    public int FrameCount { get; }
    public Color32[] Pixels { get; }
    public float[] FrameSeconds { get; }
    public int ContactFrame { get; }
    public IReadOnlyList<string> Problems { get; }
    public bool IsValid => Problems.Count == 0 && Pixels != null;
    public int SheetWidth => FrameCount * FrameSize.x;
    public int SheetHeight => CharacterDirections.Count * FrameSize.y;

    /// <summary>
    /// A clip that could not be read whole, holding only its problems.
    /// </summary>
    public static AsepriteClipFrames Failed(IReadOnlyList<string> problems)
    {
        return new AsepriteClipFrames(Vector2Int.zero, 0, null, Array.Empty<float>(), -1, problems);
    }
}
