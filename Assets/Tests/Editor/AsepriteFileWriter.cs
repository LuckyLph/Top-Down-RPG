using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

/// <summary>
/// Writes <c>.aseprite</c> files from the published file format (github.com/aseprite/aseprite, docs/ase-file-specs.md),
/// so tests need no hand-drawn art (Docs/AsepriteCharacters.md, Phase 0): a 32-bit RGBA canvas, layers (visible or
/// hidden, with a blend mode and an opacity) and layer groups, frames with their own durations in whole milliseconds,
/// a cel per layer and frame compressed with zlib as Aseprite saves it, with an opacity and an optional user data text,
/// or linked to the cel of another frame, and tags playing in any of Aseprite's directions. Pixels and cel positions
/// are given as the file stores them: from the top left, row by row from the top.
/// </summary>
public sealed class AsepriteFileWriter
{
    public const string ContactMarker = "contact";
    public const int BlendNormal = 0;
    public const int BlendMultiply = 1;
    public const int LoopForward = 0;
    public const int LoopReverse = 1;

    private const int HeaderBytes = 128;
    private const ushort FileMagic = 0xA5E0;
    private const ushort FrameMagic = 0xF1FA;
    private const ushort LayerChunk = 0x2004;
    private const ushort CelChunk = 0x2005;
    private const ushort TagsChunk = 0x2018;
    private const ushort UserDataChunk = 0x2020;
    private const ushort LinkedCel = 1;
    private const ushort CompressedImage = 2;
    private const ushort NormalLayer = 0;
    private const ushort GroupLayer = 1;
    private const ushort LayerVisible = 1;
    private const ushort LayerEditable = 2;
    private const uint LayerOpacityValid = 1;
    private const uint UserDataHasText = 1;

    private readonly List<Layer> layers = new();
    private readonly List<int> frameMilliseconds = new();
    private readonly List<(string Name, int From, int To, int Loop)> tags = new();
    private readonly Dictionary<(int Layer, int Frame), Cel> cels = new();

    /// <summary>
    /// Creates a writer of a <paramref name="width"/> by <paramref name="height"/> pixel canvas without layers, frames
    /// or tags.
    /// </summary>
    public AsepriteFileWriter(int width, int height)
    {
        if (width < 1 || width > ushort.MaxValue || height < 1 || height > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"A canvas of {width} x {height} pixels cannot be written.");
        }

        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }
    public int FrameCount => frameMilliseconds.Count;

    /// <summary>
    /// Writes the rogue's baked <paramref name="sheet"/> in the conventions of Docs/AsepriteCharacters.md: a canvas the
    /// size of its frames, so the feet stand on the baked pivot's pixel; one layer; each direction's frames in turn, in
    /// <see cref="CharacterDirections"/> order, under a tag named after the direction; each frame's duration rounded
    /// to whole milliseconds; and <see cref="ContactMarker"/> as the user data of the contact (release) frame's cel in
    /// every direction. A clip drawn over a gait gets its sheet's columns as frames, gait frame by gait frame.
    /// </summary>
    public static AsepriteFileWriter FromSheet(CharacterSheet sheet)
    {
        CharacterClip clip = sheet.Clip;
        Vector2Int size = clip.FrameSize;
        AsepriteFileWriter writer = new(size.x, size.y);
        int layer = writer.AddLayer("Body");
        int contact = clip.Motion.ContactFrame;
        Color32[] canvas = new Color32[size.x * size.y];
        for (int direction = 0; direction < CharacterDirections.Count; direction++)
        {
            int first = writer.FrameCount;
            for (int column = 0; column < sheet.FrameCount; column++)
            {
                int frame = writer.AddFrame(Mathf.RoundToInt(clip.Motion.FrameSeconds(column) * 1000f));
                RectInt rect = CharacterSheet.FrameRect(direction, column, size);
                for (int y = 0; y < size.y; y++)
                {
                    Array.Copy(sheet.Pixels, (rect.y + size.y - 1 - y) * sheet.Width + rect.x, canvas, y * size.x, size.x);
                }

                writer.SetImage(layer, frame, canvas, column == contact ? ContactMarker : null);
            }

            writer.AddTag(CharacterDirections.Name(direction), first, writer.FrameCount - 1);
        }

        return writer;
    }

    /// <summary>
    /// Writes frames cut from a baked sheet laid out as the effect and weapon sprite writers lay them (rows from the
    /// bottom, <paramref name="sheetWidth"/> wide, frames of <paramref name="size"/>): <paramref name="rows"/> rows of
    /// <paramref name="columns"/> frames from row <paramref name="firstRow"/> counted from the top, row after row, on one
    /// layer, each frame shown <paramref name="milliseconds"/>, each row's frames under the tag
    /// <paramref name="tagOf"/> names for it (none without it or for null). Docs/AsepriteCharacters.md's Phase 3
    /// conventions: an impact's variant is its eight rows tagged by direction, the death puff its one row untagged, the
    /// arrow its one row of a frame per direction.
    /// </summary>
    public static AsepriteFileWriter FromSheetRows(Color32[] sheet, int sheetWidth, Vector2Int size, int firstRow, int rows, int columns, int milliseconds, Func<int, string> tagOf = null)
    {
        int sheetHeight = sheet.Length / sheetWidth;
        AsepriteFileWriter writer = new(size.x, size.y);
        int layer = writer.AddLayer("Body");
        Color32[] canvas = new Color32[size.x * size.y];
        for (int row = 0; row < rows; row++)
        {
            int first = writer.FrameCount;
            int bottom = sheetHeight - (firstRow + row + 1) * size.y;
            for (int column = 0; column < columns; column++)
            {
                int frame = writer.AddFrame(milliseconds);
                for (int y = 0; y < size.y; y++)
                {
                    Array.Copy(sheet, (bottom + size.y - 1 - y) * sheetWidth + column * size.x, canvas, y * size.x, size.x);
                }

                writer.SetImage(layer, frame, canvas);
            }

            string tag = tagOf?.Invoke(row);
            if (tag != null)
            {
                writer.AddTag(tag, first, writer.FrameCount - 1);
            }
        }

        return writer;
    }

    /// <summary>
    /// Adds an image layer above the others, blended by <paramref name="blendMode"/> (Aseprite's numbers:
    /// <see cref="BlendNormal"/>, <see cref="BlendMultiply"/>, ...) at <paramref name="opacity"/>, at the top level or in
    /// <paramref name="group"/>, and returns its index. A group's layers are added right after it, as the file lists
    /// them.
    /// </summary>
    public int AddLayer(string name, bool visible = true, int blendMode = BlendNormal, byte opacity = 255, int group = -1)
    {
        layers.Add(new Layer(name, visible, NormalLayer, blendMode, opacity, LevelIn(group)));
        return layers.Count - 1;
    }

    /// <summary>
    /// Adds a layer group above the others, blended normally at <paramref name="opacity"/>, at the top level or in
    /// <paramref name="group"/>, and returns its index.
    /// </summary>
    public int AddGroup(string name, bool visible = true, byte opacity = 255, int group = -1)
    {
        layers.Add(new Layer(name, visible, GroupLayer, BlendNormal, opacity, LevelIn(group)));
        return layers.Count - 1;
    }

    /// <summary>
    /// Adds a frame after the others, shown for <paramref name="milliseconds"/>, and returns its index.
    /// </summary>
    public int AddFrame(int milliseconds)
    {
        if (milliseconds < 1 || milliseconds > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(milliseconds), $"A frame of {milliseconds} ms cannot be written.");
        }

        frameMilliseconds.Add(milliseconds);
        return frameMilliseconds.Count - 1;
    }

    /// <summary>
    /// Adds a tag named <paramref name="name"/> over frames <paramref name="from"/> to <paramref name="to"/>, both
    /// included, playing in <paramref name="loop"/> (Aseprite's numbers: <see cref="LoopForward"/>,
    /// <see cref="LoopReverse"/>, 2 ping-pong, 3 ping-pong reverse).
    /// </summary>
    public void AddTag(string name, int from, int to, int loop = LoopForward)
    {
        if (from < 0 || to < from || to >= frameMilliseconds.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(from), $"Tag {name} over frames {from} to {to} is outside the {frameMilliseconds.Count} frames.");
        }

        tags.Add((name, from, to, loop));
    }

    /// <summary>
    /// Sets the cel of <paramref name="layer"/> on <paramref name="frame"/>: <paramref name="pixels"/> over
    /// <paramref name="rect"/>, its position from the canvas's top left (it may reach past the canvas, as a cel can in
    /// Aseprite), at <paramref name="opacity"/>, with <paramref name="userData"/> as its user data text when given.
    /// </summary>
    public void SetCel(int layer, int frame, RectInt rect, Color32[] pixels, string userData = null, byte opacity = 255)
    {
        CheckCel(layer, frame);
        if (layers[layer].Type != NormalLayer)
        {
            throw new ArgumentException($"Layer {layer} is a group, which holds no cels.", nameof(layer));
        }

        if (rect.width < 1 || rect.height < 1 || pixels == null || pixels.Length != rect.width * rect.height)
        {
            throw new ArgumentException($"A cel of {rect.width} x {rect.height} pixels needs as many pixels.", nameof(pixels));
        }

        cels[(layer, frame)] = new Cel(rect, (Color32[])pixels.Clone(), userData, opacity, -1);
    }

    /// <summary>
    /// Makes the cel of <paramref name="layer"/> on <paramref name="frame"/> a link to its cel on
    /// <paramref name="linkedFrame"/>, which must be set and not itself a link: the two share their image, place,
    /// opacity and user data, and Aseprite writes that user data only after the cel linked to.
    /// </summary>
    public void SetLinkedCel(int layer, int frame, int linkedFrame)
    {
        CheckCel(layer, frame);
        if (!cels.TryGetValue((layer, linkedFrame), out Cel linked) || linked.LinkedFrame >= 0)
        {
            throw new ArgumentException($"Frame {linkedFrame} of layer {layer} has no cel of its own to link to.", nameof(linkedFrame));
        }

        cels[(layer, frame)] = new Cel(linked.Rect, null, null, linked.Opacity, linkedFrame);
    }

    /// <summary>
    /// Sets the cel of <paramref name="layer"/> on <paramref name="frame"/> from a whole canvas of pixels, cropped to
    /// the pixels that are not fully transparent, as Aseprite saves a cel, with <paramref name="userData"/> as its user
    /// data text when given. Returns false, leaving the cel empty, when every pixel is transparent.
    /// </summary>
    public bool SetImage(int layer, int frame, Color32[] canvas, string userData = null)
    {
        if (canvas == null || canvas.Length != Width * Height)
        {
            throw new ArgumentException($"A canvas of {Width} x {Height} pixels needs as many pixels.", nameof(canvas));
        }

        int left = Width;
        int top = Height;
        int right = -1;
        int bottom = -1;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (canvas[y * Width + x].a > 0)
                {
                    left = Mathf.Min(left, x);
                    right = Mathf.Max(right, x);
                    top = Mathf.Min(top, y);
                    bottom = Mathf.Max(bottom, y);
                }
            }
        }

        if (right < 0)
        {
            cels.Remove((layer, frame));
            return false;
        }

        RectInt rect = new(left, top, right - left + 1, bottom - top + 1);
        Color32[] pixels = new Color32[rect.width * rect.height];
        for (int y = 0; y < rect.height; y++)
        {
            Array.Copy(canvas, (rect.y + y) * Width + rect.x, pixels, y * rect.width, rect.width);
        }

        SetCel(layer, frame, rect, pixels, userData);
        return true;
    }

    /// <summary>
    /// Writes the file to <paramref name="path"/>.
    /// </summary>
    public void Write(string path)
    {
        File.WriteAllBytes(path, ToBytes());
    }

    /// <summary>
    /// The file's bytes: the 128-byte header, then each frame's header and chunks; the first frame holds the layers and
    /// the tags, and every frame its cels in layer order, each followed by its user data.
    /// </summary>
    private byte[] ToBytes()
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write(new byte[HeaderBytes]);
        for (int frame = 0; frame < frameMilliseconds.Count; frame++)
        {
            List<byte[]> chunks = new();
            if (frame == 0)
            {
                foreach (Layer layer in layers)
                {
                    chunks.Add(Chunk(LayerChunk, body => WriteLayer(body, layer)));
                }

                if (tags.Count > 0)
                {
                    chunks.Add(Chunk(TagsChunk, WriteTags));
                }
            }

            for (int layer = 0; layer < layers.Count; layer++)
            {
                if (!cels.TryGetValue((layer, frame), out Cel cel))
                {
                    continue;
                }

                chunks.Add(Chunk(CelChunk, body => WriteCel(body, layer, cel)));
                if (cel.UserData != null)
                {
                    chunks.Add(Chunk(UserDataChunk, body => WriteUserData(body, cel.UserData)));
                }
            }

            int frameBytes = 16;
            foreach (byte[] chunk in chunks)
            {
                frameBytes += chunk.Length;
            }

            writer.Write((uint)frameBytes);
            writer.Write(FrameMagic);
            writer.Write((ushort)Mathf.Min(chunks.Count, ushort.MaxValue));
            writer.Write((ushort)frameMilliseconds[frame]);
            writer.Write((ushort)0);
            writer.Write((uint)chunks.Count);
            foreach (byte[] chunk in chunks)
            {
                writer.Write(chunk);
            }
        }

        long fileBytes = stream.Length;
        stream.Position = 0;
        WriteHeader(writer, (uint)fileBytes);
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>
    /// Writes the 128-byte file header: 32 bits per pixel, the layers' opacity valid, square pixels and Aseprite's
    /// default 16 x 16 grid.
    /// </summary>
    private void WriteHeader(BinaryWriter writer, uint fileBytes)
    {
        writer.Write(fileBytes);
        writer.Write(FileMagic);
        writer.Write((ushort)frameMilliseconds.Count);
        writer.Write((ushort)Width);
        writer.Write((ushort)Height);
        writer.Write((ushort)32);
        writer.Write(LayerOpacityValid);
        writer.Write((ushort)(frameMilliseconds.Count > 0 ? frameMilliseconds[0] : 100));
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(new byte[4]);
        writer.Write((ushort)0);
        writer.Write((byte)1);
        writer.Write((byte)1);
        writer.Write((short)0);
        writer.Write((short)0);
        writer.Write((ushort)16);
        writer.Write((ushort)16);
        writer.Write(new byte[84]);
    }

    /// <summary>
    /// Writes a layer chunk's data: an image layer or a group at its child level, editable, visible or hidden, with
    /// its blend mode and opacity.
    /// </summary>
    private static void WriteLayer(BinaryWriter body, Layer layer)
    {
        body.Write((ushort)(LayerEditable | (layer.Visible ? LayerVisible : 0)));
        body.Write(layer.Type);
        body.Write((ushort)layer.ChildLevel);
        body.Write((ushort)0);
        body.Write((ushort)0);
        body.Write((ushort)layer.BlendMode);
        body.Write(layer.Opacity);
        body.Write(new byte[3]);
        WriteString(body, layer.Name);
    }

    /// <summary>
    /// Writes the tags chunk's data: every tag with its loop direction, without a repeat count.
    /// </summary>
    private void WriteTags(BinaryWriter body)
    {
        body.Write((ushort)tags.Count);
        body.Write(new byte[8]);
        foreach ((string name, int from, int to, int loop) in tags)
        {
            body.Write((ushort)from);
            body.Write((ushort)to);
            body.Write((byte)loop);
            body.Write((ushort)0);
            body.Write(new byte[6]);
            body.Write(new byte[3]);
            body.Write((byte)0);
            WriteString(body, name);
        }
    }

    /// <summary>
    /// Writes a cel chunk's data on its layer's own depth: a link to another frame's cel, or a compressed image.
    /// </summary>
    private static void WriteCel(BinaryWriter body, int layer, Cel cel)
    {
        body.Write((ushort)layer);
        body.Write((short)cel.Rect.x);
        body.Write((short)cel.Rect.y);
        body.Write(cel.Opacity);
        body.Write(cel.LinkedFrame >= 0 ? LinkedCel : CompressedImage);
        body.Write((short)0);
        body.Write(new byte[5]);
        if (cel.LinkedFrame >= 0)
        {
            body.Write((ushort)cel.LinkedFrame);
            return;
        }

        body.Write((ushort)cel.Rect.width);
        body.Write((ushort)cel.Rect.height);
        byte[] raw = new byte[cel.Pixels.Length * 4];
        for (int i = 0; i < cel.Pixels.Length; i++)
        {
            Color32 pixel = cel.Pixels[i];
            raw[i * 4] = pixel.r;
            raw[i * 4 + 1] = pixel.g;
            raw[i * 4 + 2] = pixel.b;
            raw[i * 4 + 3] = pixel.a;
        }

        body.Write(Zlib(raw));
    }

    /// <summary>
    /// Writes a user data chunk's data holding only <paramref name="text"/>.
    /// </summary>
    private static void WriteUserData(BinaryWriter body, string text)
    {
        body.Write(UserDataHasText);
        WriteString(body, text);
    }

    /// <summary>
    /// Writes a string as the format does: its length in bytes, then its UTF-8 bytes.
    /// </summary>
    private static void WriteString(BinaryWriter body, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        body.Write((ushort)bytes.Length);
        body.Write(bytes);
    }

    /// <summary>
    /// A chunk of <paramref name="type"/>: its size, counting the six bytes of size and type, its type and the data
    /// <paramref name="writeBody"/> writes.
    /// </summary>
    private static byte[] Chunk(ushort type, Action<BinaryWriter> writeBody)
    {
        using MemoryStream stream = new();
        using BinaryWriter body = new(stream);
        body.Write(0u);
        body.Write(type);
        writeBody(body);
        body.Flush();
        stream.Position = 0;
        body.Write((uint)stream.Length);
        body.Flush();
        return stream.ToArray();
    }

    /// <summary>
    /// <paramref name="data"/> in the zlib format (RFC 1950): its two-byte header, the deflated data and the Adler-32
    /// checksum of the data, most significant byte first.
    /// </summary>
    private static byte[] Zlib(byte[] data)
    {
        using MemoryStream output = new();
        output.WriteByte(0x78);
        output.WriteByte(0x9C);
        using (DeflateStream deflate = new(output, System.IO.Compression.CompressionLevel.Optimal, true))
        {
            deflate.Write(data, 0, data.Length);
        }

        uint a = 1;
        uint b = 0;
        foreach (byte value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        uint checksum = (b << 16) | a;
        output.WriteByte((byte)(checksum >> 24));
        output.WriteByte((byte)(checksum >> 16));
        output.WriteByte((byte)(checksum >> 8));
        output.WriteByte((byte)checksum);
        return output.ToArray();
    }

    /// <summary>
    /// The child level of a layer added in <paramref name="group"/> (-1 for the top level): one below the group's.
    /// </summary>
    private int LevelIn(int group)
    {
        if (group < 0)
        {
            return 0;
        }

        if (group >= layers.Count || layers[group].Type != GroupLayer)
        {
            throw new ArgumentException($"Layer {group} is not a group.", nameof(group));
        }

        return layers[group].ChildLevel + 1;
    }

    /// <summary>
    /// Throws when <paramref name="layer"/> or <paramref name="frame"/> does not exist.
    /// </summary>
    private void CheckCel(int layer, int frame)
    {
        if (layer < 0 || layer >= layers.Count || frame < 0 || frame >= frameMilliseconds.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(layer), $"Layer {layer} or frame {frame} does not exist.");
        }
    }

    /// <summary>
    /// One layer: its name, whether it shows, its type (an image layer or a group), blend mode, opacity and child
    /// level.
    /// </summary>
    private readonly struct Layer
    {
        public Layer(string name, bool visible, ushort type, int blendMode, byte opacity, int childLevel)
        {
            Name = name;
            Visible = visible;
            Type = type;
            BlendMode = blendMode;
            Opacity = opacity;
            ChildLevel = childLevel;
        }

        public string Name { get; }
        public bool Visible { get; }
        public ushort Type { get; }
        public int BlendMode { get; }
        public byte Opacity { get; }
        public int ChildLevel { get; }
    }

    /// <summary>
    /// One cel: where it lies from the canvas's top left, its pixels row by row from the top, its user data text and
    /// opacity, or the frame whose cel it links to (-1 for none).
    /// </summary>
    private readonly struct Cel
    {
        public Cel(RectInt rect, Color32[] pixels, string userData, byte opacity, int linkedFrame)
        {
            Rect = rect;
            Pixels = pixels;
            UserData = userData;
            Opacity = opacity;
            LinkedFrame = linkedFrame;
        }

        public RectInt Rect { get; }
        public Color32[] Pixels { get; }
        public string UserData { get; }
        public byte Opacity { get; }
        public int LinkedFrame { get; }
    }
}
