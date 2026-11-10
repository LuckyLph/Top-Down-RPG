using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

/// <summary>
/// One layer of a character's drawing before it is shaded (Docs/PlayerCharacter.md, Phase 6): the body without its
/// parts, or one option of a part, as the character baker's canvas held it, per frame of each clip of the look's set,
/// in the set's order (direction by direction, then column by column, as the clip's sprites run). Each frame keeps the
/// box around what the layer draws and, per pixel of the box (rows from the bottom), its material, part, tone (0 the
/// shadow, 1 the middle, 2 the light, before any line), depth and drawing step (every layer is drawn in the same steps,
/// so a tie goes to the earlier, as on one canvas). Depths sit on <see cref="DepthSteps"/> steps a pixel past the
/// frame's nearest whole pixel (<see cref="Quantize"/>), which the file keeps exactly in two bytes. Each clip keeps its
/// frame size and feet pixel; the body's layer holds every clip's material table, and a weapon's gear the table of each
/// clip it draws the weapon stowed in, coloured as that weapon (Docs/PlayerCharacter.md, Phase 7). The editor's baker writes it (<see cref="Write"/>) as a gzipped file beside the look's
/// sheets, and the <see cref="CharacterComposer"/> reads it (<see cref="Read"/>). Plain C#, so it is read off the main
/// thread.
/// </summary>
public sealed class CharacterLayer
{
    public const byte Empty = 255;
    public const float DepthSteps = 512f;

    private const int Magic = 0x4C524454;
    private const int Version = 4;

    /// <summary>
    /// Creates a layer of <paramref name="layerClips"/>.
    /// </summary>
    public CharacterLayer(CharacterLayerClip[] layerClips)
    {
        Clips = layerClips;
    }

    /// <summary>
    /// The layer's clips, in the look's set's order.
    /// </summary>
    public CharacterLayerClip[] Clips { get; }

    /// <summary>
    /// The layer written as the gzipped bytes <see cref="Read"/> reads.
    /// </summary>
    public byte[] Write()
    {
        using MemoryStream output = new();
        using (GZipStream zip = new(output, System.IO.Compression.CompressionLevel.Optimal, true))
        using (BinaryWriter writer = new(zip))
        {
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(Clips.Length);
            foreach (CharacterLayerClip clip in Clips)
            {
                writer.Write(clip.FrameSize.x);
                writer.Write(clip.FrameSize.y);
                writer.Write(clip.Feet.x);
                writer.Write(clip.Feet.y);
                writer.Write(clip.Frames.Length);
                writer.Write(clip.Materials != null);
                if (clip.Materials != null)
                {
                    writer.Write(clip.Materials.Length);
                    foreach (CharacterLayerMaterial material in clip.Materials)
                    {
                        writer.Write(material.Outlined);
                        writer.Write((byte)material.Dye);
                        for (int tone = 0; tone < ColourRamp.ToneCount; tone++)
                        {
                            Color32 colour = material.Ramp.Tone(tone);
                            writer.Write(colour.r);
                            writer.Write(colour.g);
                            writer.Write(colour.b);
                            writer.Write(colour.a);
                        }
                    }
                }

                foreach (CharacterLayerFrame frame in clip.Frames)
                {
                    WriteFrame(writer, frame);
                }
            }
        }

        return output.ToArray();
    }

    /// <summary>
    /// The layer written as <paramref name="data"/>; throws an <see cref="InvalidDataException"/> for data that is not
    /// one.
    /// </summary>
    public static CharacterLayer Read(byte[] data)
    {
        using MemoryStream input = new(data);
        using GZipStream zip = new(input, CompressionMode.Decompress);
        using BinaryReader reader = new(zip);
        if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version)
        {
            throw new InvalidDataException("Not a character layer of this version.");
        }

        CharacterLayerClip[] clips = new CharacterLayerClip[reader.ReadInt32()];
        for (int c = 0; c < clips.Length; c++)
        {
            Vector2Int size = new(reader.ReadInt32(), reader.ReadInt32());
            Vector2Int feet = new(reader.ReadInt32(), reader.ReadInt32());
            CharacterLayerFrame[] frames = new CharacterLayerFrame[reader.ReadInt32()];
            CharacterLayerMaterial[] table = null;
            if (reader.ReadBoolean())
            {
                table = new CharacterLayerMaterial[reader.ReadInt32()];
                for (int m = 0; m < table.Length; m++)
                {
                    bool outlined = reader.ReadBoolean();
                    CharacterDye dye = (CharacterDye)reader.ReadByte();
                    Color32[] tones = new Color32[ColourRamp.ToneCount];
                    for (int tone = 0; tone < tones.Length; tone++)
                    {
                        tones[tone] = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
                    }

                    table[m] = new CharacterLayerMaterial(outlined, dye, new ColourRamp(tones[0], tones[1], tones[2], tones[3]));
                }
            }

            for (int f = 0; f < frames.Length; f++)
            {
                frames[f] = ReadFrame(reader);
            }

            clips[c] = new CharacterLayerClip(size, feet, frames, table);
        }

        return new CharacterLayer(clips);
    }

    /// <summary>
    /// Puts the depths of <paramref name="material"/>'s filled pixels on <see cref="DepthSteps"/> steps a pixel past the
    /// nearest whole pixel among them, in place, as a layer's file keeps them.
    /// </summary>
    public static void Quantize(byte[] material, float[] depth)
    {
        float origin = DepthOrigin(material, depth);
        for (int i = 0; i < depth.Length; i++)
        {
            if (material[i] != Empty)
            {
                depth[i] = origin + Mathf.Clamp(Mathf.Round((depth[i] - origin) * DepthSteps), 0f, ushort.MaxValue) / DepthSteps;
            }
        }
    }

    /// <summary>
    /// The whole pixel at or in front of the nearest filled pixel's depth, 0 for none.
    /// </summary>
    private static float DepthOrigin(byte[] material, float[] depth)
    {
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < depth.Length; i++)
        {
            if (material[i] != Empty && depth[i] < nearest)
            {
                nearest = depth[i];
            }
        }

        return float.IsPositiveInfinity(nearest) ? 0f : Mathf.Floor(nearest);
    }

    /// <summary>
    /// Writes <paramref name="frame"/>: its box, then its materials, parts, tones and steps, then its depth origin and
    /// the depth of its filled pixels only in steps past it, two bytes each, a byte plane at a time (every low byte, then
    /// every high byte), which compresses well and reads back exactly a depth <see cref="Quantize"/> gave.
    /// </summary>
    private static void WriteFrame(BinaryWriter writer, CharacterLayerFrame frame)
    {
        RectInt box = frame.Box;
        writer.Write((short)box.x);
        writer.Write((short)box.y);
        writer.Write((short)box.width);
        writer.Write((short)box.height);
        writer.Write(frame.Material);
        writer.Write(frame.Part);
        writer.Write(frame.Tone);
        writer.Write(frame.Step);
        float origin = DepthOrigin(frame.Material, frame.Depth);
        writer.Write(origin);
        List<int> steps = new();
        for (int i = 0; i < frame.Material.Length; i++)
        {
            if (frame.Material[i] != Empty)
            {
                steps.Add(Mathf.Clamp(Mathf.RoundToInt((frame.Depth[i] - origin) * DepthSteps), 0, ushort.MaxValue));
            }
        }

        for (int plane = 0; plane < sizeof(ushort); plane++)
        {
            foreach (int value in steps)
            {
                writer.Write((byte)(value >> (plane * 8)));
            }
        }
    }

    /// <summary>
    /// Reads a frame <see cref="WriteFrame"/> wrote.
    /// </summary>
    private static CharacterLayerFrame ReadFrame(BinaryReader reader)
    {
        RectInt box = new(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16());
        int count = box.width * box.height;
        byte[] material = reader.ReadBytes(count);
        byte[] part = reader.ReadBytes(count);
        byte[] tone = reader.ReadBytes(count);
        byte[] step = reader.ReadBytes(count);
        float origin = reader.ReadSingle();
        float[] depth = new float[count];
        int filled = 0;
        for (int i = 0; i < count; i++)
        {
            filled += material[i] != Empty ? 1 : 0;
        }

        int[] steps = new int[filled];
        for (int plane = 0; plane < sizeof(ushort); plane++)
        {
            byte[] bytes = reader.ReadBytes(filled);
            for (int k = 0; k < filled; k++)
            {
                steps[k] |= bytes[k] << (plane * 8);
            }
        }

        for (int i = 0, k = 0; i < count; i++)
        {
            depth[i] = material[i] != Empty ? origin + steps[k++] / DepthSteps : float.PositiveInfinity;
        }

        return new CharacterLayerFrame(box, material, part, tone, step, depth);
    }
}

/// <summary>
/// One clip of a <see cref="CharacterLayer"/>: the size of its frames, the pixel its feet stand on (from the bottom
/// left), its frames, and in the body's layer the material table its frames are coloured with (null in a part's).
/// </summary>
public sealed class CharacterLayerClip
{
    /// <summary>
    /// Creates a clip of <paramref name="clipFrames"/> of <paramref name="frameSize"/>, the feet on
    /// <paramref name="feet"/>, coloured by <paramref name="materials"/> (or none).
    /// </summary>
    public CharacterLayerClip(Vector2Int frameSize, Vector2Int feet, CharacterLayerFrame[] clipFrames, CharacterLayerMaterial[] materials)
    {
        FrameSize = frameSize;
        Feet = feet;
        Frames = clipFrames;
        Materials = materials;
    }

    public Vector2Int FrameSize { get; }
    public Vector2Int Feet { get; }
    public CharacterLayerFrame[] Frames { get; }
    public CharacterLayerMaterial[] Materials { get; }
}

/// <summary>
/// One frame of a <see cref="CharacterLayer"/>: the box around what it draws in the frame, and per pixel of the box,
/// rows from the bottom, its material (<see cref="CharacterLayer.Empty"/> for none), part, tone, drawing step and depth
/// (larger is farther; infinite where empty).
/// </summary>
public sealed class CharacterLayerFrame
{
    /// <summary>
    /// Creates a frame drawing in <paramref name="frameBox"/>.
    /// </summary>
    public CharacterLayerFrame(RectInt frameBox, byte[] material, byte[] part, byte[] tone, byte[] step, float[] depth)
    {
        Box = frameBox;
        Material = material;
        Part = part;
        Tone = tone;
        Step = step;
        Depth = depth;
    }

    public RectInt Box { get; }
    public byte[] Material { get; }
    public byte[] Part { get; }
    public byte[] Tone { get; }
    public byte[] Step { get; }
    public float[] Depth { get; }
}

/// <summary>
/// How a material of a clip is coloured: whether it lines its edges, the dye a player's colours give it, and its
/// ramp, the colours it is baked in.
/// </summary>
public readonly struct CharacterLayerMaterial
{
    /// <summary>
    /// Creates a material entry.
    /// </summary>
    public CharacterLayerMaterial(bool outlined, CharacterDye dye, ColourRamp ramp)
    {
        Outlined = outlined;
        Dye = dye;
        Ramp = ramp;
    }

    public bool Outlined { get; }
    public CharacterDye Dye { get; }
    public ColourRamp Ramp { get; }
}
