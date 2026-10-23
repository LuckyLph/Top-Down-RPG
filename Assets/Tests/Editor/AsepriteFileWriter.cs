using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

/// <summary>
/// Writes <c>.aseprite</c> files from the published file format (github.com/aseprite/aseprite, docs/ase-file-specs.md),
/// so tests need no hand-drawn art (Docs/AsepriteCharacters.md, Phase 0): a 32-bit RGBA canvas, layers (visible or
/// hidden), frames with their own durations in whole milliseconds, a cel per layer and frame compressed with zlib as
/// Aseprite saves it, with an optional user data text, and forward tags. Pixels and cel positions are given as the file
/// stores them: from the top left, row by row from the top.
/// </summary>
public sealed class AsepriteFileWriter
{
    public const string ContactMarker = "contact";

    private const int HeaderBytes = 128;
    private const ushort FileMagic = 0xA5E0;
    private const ushort FrameMagic = 0xF1FA;
    private const ushort LayerChunk = 0x2004;
    private const ushort CelChunk = 0x2005;
    private const ushort TagsChunk = 0x2018;
    private const ushort UserDataChunk = 0x2020;
    private const ushort CompressedImage = 2;
    private const ushort LayerVisible = 1;
    private const ushort LayerEditable = 2;
    private const uint LayerOpacityValid = 1;
    private const uint UserDataHasText = 1;

    private readonly List<(string Name, bool Visible)> layers = new();
    private readonly List<int> frameMilliseconds = new();
    private readonly List<(string Name, int From, int To)> tags = new();
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
    /// Adds a normal layer above the others, blended normally at full opacity, and returns its index.
    /// </summary>
    public int AddLayer(string name, bool visible = true)
    {
        layers.Add((name, visible));
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
    /// Adds a forward tag named <paramref name="name"/> over frames <paramref name="from"/> to <paramref name="to"/>,
    /// both included.
    /// </summary>
    public void AddTag(string name, int from, int to)
    {
        if (from < 0 || to < from || to >= frameMilliseconds.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(from), $"Tag {name} over frames {from} to {to} is outside the {frameMilliseconds.Count} frames.");
        }

        tags.Add((name, from, to));
    }

    /// <summary>
    /// Sets the cel of <paramref name="layer"/> on <paramref name="frame"/>: <paramref name="pixels"/> over
    /// <paramref name="rect"/>, its position from the canvas's top left (it may reach past the canvas, as a cel can in
    /// Aseprite), with <paramref name="userData"/> as its user data text when given.
    /// </summary>
    public void SetCel(int layer, int frame, RectInt rect, Color32[] pixels, string userData = null)
    {
        if (layer < 0 || layer >= layers.Count || frame < 0 || frame >= frameMilliseconds.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(layer), $"Layer {layer} or frame {frame} does not exist.");
        }

        if (rect.width < 1 || rect.height < 1 || pixels == null || pixels.Length != rect.width * rect.height)
        {
            throw new ArgumentException($"A cel of {rect.width} x {rect.height} pixels needs as many pixels.", nameof(pixels));
        }

        cels[(layer, frame)] = new Cel(rect, (Color32[])pixels.Clone(), userData);
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
                foreach ((string name, bool visible) in layers)
                {
                    chunks.Add(Chunk(LayerChunk, body => WriteLayer(body, name, visible)));
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
    /// Writes a layer chunk's data: a normal layer at the top level, editable, visible or hidden, blended normally at
    /// full opacity.
    /// </summary>
    private static void WriteLayer(BinaryWriter body, string name, bool visible)
    {
        body.Write((ushort)(LayerEditable | (visible ? LayerVisible : 0)));
        body.Write((ushort)0);
        body.Write((ushort)0);
        body.Write((ushort)0);
        body.Write((ushort)0);
        body.Write((ushort)0);
        body.Write((byte)255);
        body.Write(new byte[3]);
        WriteString(body, name);
    }

    /// <summary>
    /// Writes the tags chunk's data: every tag forward, without a repeat count.
    /// </summary>
    private void WriteTags(BinaryWriter body)
    {
        body.Write((ushort)tags.Count);
        body.Write(new byte[8]);
        foreach ((string name, int from, int to) in tags)
        {
            body.Write((ushort)from);
            body.Write((ushort)to);
            body.Write((byte)0);
            body.Write((ushort)0);
            body.Write(new byte[6]);
            body.Write(new byte[3]);
            body.Write((byte)0);
            WriteString(body, name);
        }
    }

    /// <summary>
    /// Writes a cel chunk's data: a compressed image at full opacity on its layer's own depth.
    /// </summary>
    private static void WriteCel(BinaryWriter body, int layer, Cel cel)
    {
        body.Write((ushort)layer);
        body.Write((short)cel.Rect.x);
        body.Write((short)cel.Rect.y);
        body.Write((byte)255);
        body.Write(CompressedImage);
        body.Write((short)0);
        body.Write(new byte[5]);
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
    /// One cel: where it lies from the canvas's top left, its pixels row by row from the top, and its user data text.
    /// </summary>
    private readonly struct Cel
    {
        public Cel(RectInt rect, Color32[] pixels, string userData)
        {
            Rect = rect;
            Pixels = pixels;
            UserData = userData;
        }

        public RectInt Rect { get; }
        public Color32[] Pixels { get; }
        public string UserData { get; }
    }
}
