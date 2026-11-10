using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Composes a character's frames from its layers (Docs/PlayerCharacter.md, Phase 6) exactly as the character baker
/// shades one canvas drawn with all of them: each pixel takes the nearest of the layers', a tie going to the earlier
/// drawing step (every layer is drawn in the same steps, so as on one canvas; the hair's ball is drawn just before the
/// body's bare head, so it covers the scalp), coloured by its material's ramp in its tone;
/// then the lines, an empty pixel beside an outlined material taking the outline colour of the nearest such neighbour,
/// and an outlined pixel beside another part nearer by more than the inner line gap its own outline colour. Alongside
/// the colours it gives each pixel's palette code (<see cref="CharacterPaletteMap"/>). <see cref="ComposeAll"/> crops
/// every frame of every clip to what it draws; plain C#, so it runs off the main thread. <see cref="Build"/> then packs
/// the frames into one texture and its palette map and makes the sprites and the set, on the main thread.
/// </summary>
public sealed class CharacterComposer
{
    public const int AtlasWidth = 2048;
    private const int Padding = 1;
    private const int NoMaterial = -1;

    private float[] depth = Array.Empty<float>();
    private int[] material = Array.Empty<int>();
    private int[] part = Array.Empty<int>();
    private byte[] tone = Array.Empty<byte>();
    private byte[] step = Array.Empty<byte>();

    /// <summary>
    /// Composes one frame of <paramref name="width"/> x <paramref name="height"/> pixels from
    /// <paramref name="layers"/> (merged in order, nulls skipped) coloured by <paramref name="materials"/> with inner
    /// lines past <paramref name="innerLineGap"/>, into <paramref name="pixels"/> and <paramref name="codes"/> (the
    /// frame's size, rows from the bottom; clear and 0 where nothing is drawn).
    /// </summary>
    public void ComposeFrame(int width, int height, IReadOnlyList<CharacterLayerFrame> layers, IReadOnlyList<CharacterLayerMaterial> materials, float innerLineGap, Color32[] pixels, byte[] codes)
    {
        int count = width * height;
        Reserve(count);
        for (int i = 0; i < count; i++)
        {
            depth[i] = float.PositiveInfinity;
            material[i] = NoMaterial;
            pixels[i] = default;
            codes[i] = 0;
        }

        int x0 = width;
        int y0 = height;
        int x1 = 0;
        int y1 = 0;
        foreach (CharacterLayerFrame layer in layers)
        {
            if (layer == null || layer.Box.width <= 0 || layer.Box.height <= 0)
            {
                continue;
            }

            RectInt box = layer.Box;
            x0 = Mathf.Min(x0, box.xMin);
            y0 = Mathf.Min(y0, box.yMin);
            x1 = Mathf.Max(x1, box.xMax);
            y1 = Mathf.Max(y1, box.yMax);
            for (int y = 0; y < box.height; y++)
            {
                for (int x = 0; x < box.width; x++)
                {
                    int k = y * box.width + x;
                    byte m = layer.Material[k];
                    if (m == CharacterLayer.Empty)
                    {
                        continue;
                    }

                    int i = (box.y + y) * width + box.x + x;
                    if (layer.Depth[k] > depth[i] || (layer.Depth[k] == depth[i] && layer.Step[k] >= step[i]))
                    {
                        continue;
                    }

                    depth[i] = layer.Depth[k];
                    material[i] = m;
                    part[i] = layer.Part[k];
                    tone[i] = layer.Tone[k];
                    step[i] = layer.Step[k];
                }
            }
        }

        if (x1 <= x0 || y1 <= y0)
        {
            return;
        }

        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                int i = y * width + x;
                int m = material[i];
                if (m == NoMaterial)
                {
                    continue;
                }

                int shade = tone[i] + 1;
                pixels[i] = Opaque(materials[m].Ramp.Tone(shade));
                codes[i] = Code(materials[m].Dye, shade);
            }
        }

        int lx0 = Mathf.Max(x0 - 1, 0);
        int ly0 = Mathf.Max(y0 - 1, 0);
        int lx1 = Mathf.Min(x1 + 1, width);
        int ly1 = Mathf.Min(y1 + 1, height);
        for (int y = ly0; y < ly1; y++)
        {
            for (int x = lx0; x < lx1; x++)
            {
                int i = y * width + x;
                int m = material[i];
                if (m == NoMaterial)
                {
                    int nearest = NearestOutlinedNeighbour(x, y, width, height, materials);
                    if (nearest >= 0)
                    {
                        CharacterLayerMaterial lined = materials[material[nearest]];
                        pixels[i] = Opaque(lined.Ramp.Outline);
                        codes[i] = Code(lined.Dye, 0);
                    }
                }
                else if (materials[m].Outlined && HasNearerNeighbour(x, y, width, height, innerLineGap, materials))
                {
                    pixels[i] = Opaque(materials[m].Ramp.Outline);
                    codes[i] = Code(materials[m].Dye, 0);
                }
            }
        }
    }

    /// <summary>
    /// Composes every frame of every clip of <paramref name="body"/> with <paramref name="parts"/> laid over it in
    /// order (nulls skipped), each cropped to the box around what it draws.
    /// </summary>
    public CharacterOutfitFrames ComposeAll(CharacterLayer body, IReadOnlyList<CharacterLayer> parts, float innerLineGap)
    {
        CharacterOutfitFrame[][] clips = new CharacterOutfitFrame[body.Clips.Length][];
        List<CharacterLayerFrame> layers = new();
        Color32[] pixels = Array.Empty<Color32>();
        byte[] codes = Array.Empty<byte>();
        for (int c = 0; c < clips.Length; c++)
        {
            CharacterLayerClip clip = body.Clips[c];
            int width = clip.FrameSize.x;
            int height = clip.FrameSize.y;
            if (pixels.Length < width * height)
            {
                pixels = new Color32[width * height];
                codes = new byte[width * height];
            }

            clips[c] = new CharacterOutfitFrame[clip.Frames.Length];
            for (int f = 0; f < clip.Frames.Length; f++)
            {
                layers.Clear();
                layers.Add(clip.Frames[f]);
                foreach (CharacterLayer layer in parts)
                {
                    layers.Add(layer?.Clips[c].Frames[f]);
                }

                ComposeFrame(width, height, layers, MaterialsOf(clip, parts, c), innerLineGap, pixels, codes);
                clips[c][f] = Crop(width, height, pixels, codes);
            }
        }

        return new CharacterOutfitFrames(body, clips);
    }

    /// <summary>
    /// Packs <paramref name="frames"/> into one texture and its palette map, makes a sprite per frame pivoted on its
    /// clip's feet, and the set of <paramref name="look"/>'s clips over them, naming the dyes of the look and
    /// <paramref name="dyes"/>, all at once. Main thread only; the caller destroys what it made
    /// (<see cref="CharacterOutfit.Destroy"/>).
    /// </summary>
    public static CharacterOutfit Build(CharacterOutfitFrames frames, CharacterAnimationSet look, int dyes, string outfitName)
    {
        CharacterOutfitBuild build = BeginBuild(Pack(frames), look, dyes, outfitName);
        build.Step(int.MaxValue);
        return build.Finish();
    }

    /// <summary>
    /// Lays <paramref name="frames"/> out in rows of an atlas <see cref="AtlasWidth"/> wide, tallest first, and copies
    /// their colours and palette codes into it (<see cref="CharacterOutfitAtlas"/>). Plain arrays, so it runs off the
    /// main thread (Docs/PlayerCharacter.md, Phase 7).
    /// </summary>
    public static CharacterOutfitAtlas Pack(CharacterOutfitFrames frames)
    {
        List<(int Clip, int Frame)> order = new();
        for (int c = 0; c < frames.Clips.Length; c++)
        {
            for (int f = 0; f < frames.Clips[c].Length; f++)
            {
                order.Add((c, f));
            }
        }

        order.Sort((a, b) => frames.Clips[b.Clip][b.Frame].Box.height.CompareTo(frames.Clips[a.Clip][a.Frame].Box.height));
        RectInt[][] places = new RectInt[frames.Clips.Length][];
        for (int c = 0; c < places.Length; c++)
        {
            places[c] = new RectInt[frames.Clips[c].Length];
        }

        int cursorX = 0;
        int cursorY = 0;
        int rowHeight = 0;
        foreach ((int c, int f) in order)
        {
            RectInt box = frames.Clips[c][f].Box;
            int w = Mathf.Max(box.width, 1);
            int h = Mathf.Max(box.height, 1);
            if (cursorX + w + Padding > AtlasWidth)
            {
                cursorX = 0;
                cursorY += rowHeight + Padding;
                rowHeight = 0;
            }

            places[c][f] = new RectInt(cursorX, cursorY, w, h);
            cursorX += w + Padding;
            rowHeight = Mathf.Max(rowHeight, h);
        }

        int atlasHeight = Mathf.Max(1, cursorY + rowHeight);
        Color32[] pixels = new Color32[AtlasWidth * atlasHeight];
        byte[] codes = new byte[AtlasWidth * atlasHeight];
        for (int c = 0; c < frames.Clips.Length; c++)
        {
            for (int f = 0; f < frames.Clips[c].Length; f++)
            {
                CharacterOutfitFrame frame = frames.Clips[c][f];
                RectInt place = places[c][f];
                for (int y = 0; y < frame.Box.height; y++)
                {
                    Array.Copy(frame.Pixels, y * frame.Box.width, pixels, (place.y + y) * AtlasWidth + place.x, frame.Box.width);
                    Array.Copy(frame.Codes, y * frame.Box.width, codes, (place.y + y) * AtlasWidth + place.x, frame.Box.width);
                }
            }
        }

        return new CharacterOutfitAtlas(frames, AtlasWidth, atlasHeight, pixels, codes, places);
    }

    /// <summary>
    /// Starts building <paramref name="atlas"/> into an outfit on the main thread: its texture and palette map now, then
    /// its sprites a few at a time (<see cref="CharacterOutfitBuild.Step"/>) and the set of <paramref name="look"/>'s
    /// clips over them, naming the dyes of the look and <paramref name="dyes"/>
    /// (<see cref="CharacterOutfitBuild.Finish"/>).
    /// </summary>
    public static CharacterOutfitBuild BeginBuild(CharacterOutfitAtlas atlas, CharacterAnimationSet look, int dyes, string outfitName)
    {
        return new CharacterOutfitBuild(atlas, look, dyes, outfitName);
    }

    /// <summary>
    /// The material table a clip's frames are coloured with, its layers given body first: the last layer's that has one
    /// for the clip, as a weapon's gear has where the clip holds no weapon and it draws the weapon stowed
    /// (Docs/PlayerCharacter.md, Phase 7), else the body's.
    /// </summary>
    public static IReadOnlyList<CharacterLayerMaterial> MaterialsOf(IReadOnlyList<CharacterLayerClip> layers)
    {
        for (int k = layers.Count - 1; k > 0; k--)
        {
            if (layers[k]?.Materials != null)
            {
                return layers[k].Materials;
            }
        }

        return layers[0].Materials;
    }

    /// <summary>
    /// The material table clip <paramref name="clip"/> of <paramref name="body"/> is coloured with under
    /// <paramref name="parts"/> (see <see cref="MaterialsOf(IReadOnlyList{CharacterLayerClip})"/>).
    /// </summary>
    private static IReadOnlyList<CharacterLayerMaterial> MaterialsOf(CharacterLayerClip body, IReadOnlyList<CharacterLayer> parts, int clip)
    {
        for (int k = parts.Count - 1; k >= 0; k--)
        {
            CharacterLayerMaterial[] table = parts[k]?.Clips[clip].Materials;
            if (table != null)
            {
                return table;
            }
        }

        return body.Materials;
    }

    /// <summary>
    /// The palette code of <paramref name="dye"/> in ramp tone <paramref name="shade"/>, 0 for no dye.
    /// </summary>
    private static byte Code(CharacterDye dye, int shade)
    {
        return dye == CharacterDye.None ? (byte)0 : CharacterPaletteMap.Code(dye, shade);
    }

    /// <summary>
    /// <paramref name="colour"/> with full alpha.
    /// </summary>
    private static Color32 Opaque(Color32 colour)
    {
        return new Color32(colour.r, colour.g, colour.b, 255);
    }

    /// <summary>
    /// The frame's drawn pixels cropped to the box around them; an empty box for a frame drawing nothing.
    /// </summary>
    private static CharacterOutfitFrame Crop(int width, int height, Color32[] pixels, byte[] codes)
    {
        int x0 = width;
        int y0 = height;
        int x1 = -1;
        int y1 = -1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (pixels[y * width + x].a == 0)
                {
                    continue;
                }

                x0 = Mathf.Min(x0, x);
                y0 = Mathf.Min(y0, y);
                x1 = Mathf.Max(x1, x);
                y1 = Mathf.Max(y1, y);
            }
        }

        if (x1 < 0)
        {
            return new CharacterOutfitFrame(new RectInt(0, 0, 0, 0), Array.Empty<Color32>(), Array.Empty<byte>());
        }

        RectInt box = new(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        Color32[] cropped = new Color32[box.width * box.height];
        byte[] croppedCodes = new byte[cropped.Length];
        for (int y = 0; y < box.height; y++)
        {
            Array.Copy(pixels, (box.y + y) * width + box.x, cropped, y * box.width, box.width);
            Array.Copy(codes, (box.y + y) * width + box.x, croppedCodes, y * box.width, box.width);
        }

        return new CharacterOutfitFrame(box, cropped, croppedCodes);
    }

    /// <summary>
    /// Grows the buffers to hold <paramref name="count"/> pixels.
    /// </summary>
    private void Reserve(int count)
    {
        if (depth.Length >= count)
        {
            return;
        }

        depth = new float[count];
        material = new int[count];
        part = new int[count];
        tone = new byte[count];
        step = new byte[count];
    }

    /// <summary>
    /// The index of the nearest filled pixel of an outlined material among the four beside (<paramref name="x"/>,
    /// <paramref name="y"/>), in the baker's order, or -1.
    /// </summary>
    private int NearestOutlinedNeighbour(int x, int y, int width, int height, IReadOnlyList<CharacterLayerMaterial> materials)
    {
        int best = -1;
        float bestDepth = float.PositiveInfinity;
        for (int k = 0; k < 4; k++)
        {
            int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0);
            int ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
            if (nx < 0 || nx >= width || ny < 0 || ny >= height)
            {
                continue;
            }

            int j = ny * width + nx;
            if (material[j] == NoMaterial || !materials[material[j]].Outlined || depth[j] >= bestDepth)
            {
                continue;
            }

            best = j;
            bestDepth = depth[j];
        }

        return best;
    }

    /// <summary>
    /// Whether a pixel of another part of an outlined material lies beside (<paramref name="x"/>,
    /// <paramref name="y"/>) nearer than it by more than <paramref name="gap"/>.
    /// </summary>
    private bool HasNearerNeighbour(int x, int y, int width, int height, float gap, IReadOnlyList<CharacterLayerMaterial> materials)
    {
        int i = y * width + x;
        for (int k = 0; k < 4; k++)
        {
            int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0);
            int ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
            if (nx < 0 || nx >= width || ny < 0 || ny >= height)
            {
                continue;
            }

            int j = ny * width + nx;
            if (material[j] != NoMaterial && part[j] != part[i] && depth[j] < depth[i] - gap && materials[material[j]].Outlined)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Every frame <see cref="CharacterComposer.ComposeAll"/> composed, by clip and frame in the set's order, over the body
/// layer it composed them on (whose clips name their frames' sizes and feet).
/// </summary>
public sealed class CharacterOutfitFrames
{
    /// <summary>
    /// Creates the frames of <paramref name="clips"/> composed over <paramref name="body"/>.
    /// </summary>
    public CharacterOutfitFrames(CharacterLayer body, CharacterOutfitFrame[][] clips)
    {
        Body = body;
        Clips = clips;
    }

    public CharacterLayer Body { get; }
    public CharacterOutfitFrame[][] Clips { get; }
}

/// <summary>
/// One composed frame cropped to what it draws: its box in the frame and its colours and palette codes, rows from the
/// bottom.
/// </summary>
public sealed class CharacterOutfitFrame
{
    /// <summary>
    /// Creates a frame of <paramref name="framePixels"/> and <paramref name="frameCodes"/> over <paramref name="box"/>.
    /// </summary>
    public CharacterOutfitFrame(RectInt box, Color32[] framePixels, byte[] frameCodes)
    {
        Box = box;
        Pixels = framePixels;
        Codes = frameCodes;
    }

    public RectInt Box { get; }
    public Color32[] Pixels { get; }
    public byte[] Codes { get; }
}

/// <summary>
/// Frames composed for an outfit laid out in one atlas (<see cref="CharacterComposer.Pack"/>): its size, its colours
/// and palette codes, rows from the bottom, and where each frame of each clip lies in it.
/// </summary>
public sealed class CharacterOutfitAtlas
{
    /// <summary>
    /// Creates the atlas of <paramref name="atlasFrames"/>, <paramref name="atlasWidth"/> by
    /// <paramref name="atlasHeight"/> pixels, with each frame at its place in <paramref name="framePlaces"/>.
    /// </summary>
    public CharacterOutfitAtlas(CharacterOutfitFrames atlasFrames, int atlasWidth, int atlasHeight, Color32[] atlasPixels, byte[] atlasCodes, RectInt[][] framePlaces)
    {
        Frames = atlasFrames;
        Width = atlasWidth;
        Height = atlasHeight;
        Pixels = atlasPixels;
        Codes = atlasCodes;
        Places = framePlaces;
    }

    public CharacterOutfitFrames Frames { get; }
    public int Width { get; }
    public int Height { get; }
    public Color32[] Pixels { get; }
    public byte[] Codes { get; }
    public RectInt[][] Places { get; }
}

/// <summary>
/// An outfit being built on the main thread (<see cref="CharacterComposer.BeginBuild"/>), so its sprites can be made a
/// few at a time across frames rather than all in one (Docs/PlayerCharacter.md, Phase 7): its texture and palette map
/// exist from the start, <see cref="Step"/> makes sprites in clip order, <see cref="Finish"/> makes the set and hands
/// the outfit over, and <see cref="Abort"/> destroys what was made.
/// </summary>
public sealed class CharacterOutfitBuild
{
    private readonly CharacterOutfitAtlas atlas;
    private readonly CharacterAnimationSet look;
    private readonly int dyes;
    private readonly string outfitName;
    private readonly Texture2D sheet;
    private readonly Texture2D map;
    private readonly SecondarySpriteTexture[] secondary;
    private readonly List<Sprite> sprites = new();
    private readonly Sprite[][] clipSprites;
    private int clip;
    private int frame;

    /// <summary>
    /// Makes the texture and palette map of <paramref name="outfitAtlas"/>, the sprites to come over
    /// <paramref name="lookSet"/>'s clips.
    /// </summary>
    internal CharacterOutfitBuild(CharacterOutfitAtlas outfitAtlas, CharacterAnimationSet lookSet, int setDyes, string name)
    {
        atlas = outfitAtlas;
        look = lookSet;
        dyes = setDyes;
        outfitName = name;
        sheet = new Texture2D(atlas.Width, atlas.Height, TextureFormat.RGBA32, false, false) { name = $"{name} Sheet", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        sheet.SetPixelData(atlas.Pixels, 0);
        sheet.Apply(false, true);
        map = new Texture2D(atlas.Width, atlas.Height, TextureFormat.R8, false, true) { name = $"{name} Palette Map", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        map.SetPixelData(atlas.Codes, 0);
        map.Apply(false, true);
        secondary = new[] { new SecondarySpriteTexture { name = CharacterPaletteMap.MapTextureName, texture = map } };
        clipSprites = new Sprite[atlas.Frames.Clips.Length][];
        for (int c = 0; c < clipSprites.Length; c++)
        {
            clipSprites[c] = new Sprite[atlas.Frames.Clips[c].Length];
        }

        SkipEmptyClips();
    }

    /// <summary>
    /// Whether every sprite has been made.
    /// </summary>
    public bool IsDone => clip >= clipSprites.Length;

    /// <summary>
    /// Makes up to <paramref name="count"/> more sprites, each pivoted on its clip's feet; returns whether every sprite
    /// has been made.
    /// </summary>
    public bool Step(int count)
    {
        CharacterOutfitFrames frames = atlas.Frames;
        for (int made = 0; made < count && !IsDone; made++)
        {
            CharacterLayerClip layerClip = frames.Body.Clips[clip];
            CharacterOutfitFrame outfitFrame = frames.Clips[clip][frame];
            RectInt place = atlas.Places[clip][frame];
            Vector2 pivot = new((layerClip.Feet.x - outfitFrame.Box.x) / (float)place.width, (layerClip.Feet.y - outfitFrame.Box.y) / (float)place.height);
            Sprite sprite = Sprite.Create(sheet, new Rect(place.x, place.y, place.width, place.height), pivot, CharacterOutfit.PixelsPerUnit, 0, SpriteMeshType.FullRect, Vector4.zero, false, secondary);
            sprite.name = $"{outfitName} {look.Clips[clip].Name} {frame}";
            clipSprites[clip][frame] = sprite;
            sprites.Add(sprite);
            frame++;
            if (frame >= clipSprites[clip].Length)
            {
                frame = 0;
                clip++;
                SkipEmptyClips();
            }
        }

        return IsDone;
    }

    /// <summary>
    /// The outfit: the set of the look's clips over the sprites, once every sprite is made (<see cref="Step"/>).
    /// </summary>
    public CharacterOutfit Finish()
    {
        List<CharacterAnimationClip> clips = new();
        for (int c = 0; c < clipSprites.Length; c++)
        {
            clips.Add(look.Clips[c].WithSprites(clipSprites[c]));
        }

        CharacterAnimationSet set = CharacterAnimationSet.Create(clips, look.Dyes | dyes);
        set.name = outfitName;
        return new CharacterOutfit(set, sheet, map, sprites);
    }

    /// <summary>
    /// Destroys the texture, the palette map and every sprite made so far, for a build no longer wanted.
    /// </summary>
    public void Abort()
    {
        new CharacterOutfit(null, sheet, map, sprites).Destroy();
    }

    /// <summary>
    /// Moves past clips without frames.
    /// </summary>
    private void SkipEmptyClips()
    {
        while (clip < clipSprites.Length && clipSprites[clip].Length == 0)
        {
            clip++;
        }
    }
}

/// <summary>
/// A composed outfit (<see cref="CharacterComposer.Build"/>): its set, the texture and palette map its sprites are cut
/// from, and the sprites, all made at runtime and destroyed together by <see cref="Destroy"/>.
/// </summary>
public sealed class CharacterOutfit
{
    public const float PixelsPerUnit = 32f;

    private readonly List<Sprite> sprites;

    /// <summary>
    /// Creates an outfit of <paramref name="outfitSet"/> over the textures and sprites made for it.
    /// </summary>
    public CharacterOutfit(CharacterAnimationSet outfitSet, Texture2D sheet, Texture2D paletteMap, List<Sprite> outfitSprites)
    {
        Set = outfitSet;
        Sheet = sheet;
        PaletteMap = paletteMap;
        sprites = outfitSprites;
    }

    public CharacterAnimationSet Set { get; }
    public Texture2D Sheet { get; }
    public Texture2D PaletteMap { get; }

    /// <summary>
    /// Destroys the set, the sprites and the textures.
    /// </summary>
    public void Destroy()
    {
        foreach (Sprite sprite in sprites)
        {
            DestroyObject(sprite);
        }

        sprites.Clear();
        DestroyObject(Set);
        DestroyObject(Sheet);
        DestroyObject(PaletteMap);
    }

    /// <summary>
    /// Destroys <paramref name="instance"/> at once in the editor outside play, else at the end of the frame.
    /// </summary>
    private static void DestroyObject(UnityEngine.Object instance)
    {
        if (instance == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(instance);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }
}
