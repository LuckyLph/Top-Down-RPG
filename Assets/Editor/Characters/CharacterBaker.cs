using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One clip drawn as a sheet: a row per direction (<see cref="CharacterDirections"/>, east at the top), a column per
/// frame, pixels rows from the bottom, the frames whose drawing reached their edge (named "direction frame"), which
/// the inspector warns about, and where each frame's cast light shines from and how strongly
/// (<see cref="CharacterCanvas.LightCentre"/>, <see cref="CharacterCanvas.LightStrength"/>), direction by direction and
/// within each frame by frame. For a character carrying a lantern, also its layer: laid out as the sheet, the pixels the
/// lantern changes on each frame (every other pixel clear), so drawn over the sheet they give the frame drawn with it,
/// which of those pixels glow, and where the lantern shines from on each frame. For a character a player can colour
/// (Docs/PlayerCharacter.md, Phase 5), also its palette map: laid out as the sheet, a byte per pixel naming its dye and
/// tone (<see cref="CharacterPaletteMap"/>), and the dyes it names.
/// </summary>
public sealed class CharacterSheet
{
    /// <summary>
    /// Creates the sheet of <paramref name="clip"/> from its pixels.
    /// </summary>
    public CharacterSheet(CharacterClip clip, int frameCount, Color32[] pixels, IReadOnlyList<string> framesAtEdge, Vector2[] lightCentres, float[] lightStrengths,
        Color32[] lanternPixels = null, Color32[] lanternGlow = null, Vector2[] lanternCentres = null, byte[] paletteMap = null)
    {
        Clip = clip;
        FrameCount = frameCount;
        Width = frameCount * clip.FrameSize.x;
        Height = CharacterDirections.Count * clip.FrameSize.y;
        Pixels = pixels;
        FramesAtEdge = framesAtEdge;
        LightCentres = lightCentres;
        LightStrengths = lightStrengths;
        LanternPixels = lanternPixels;
        LanternGlow = lanternGlow;
        LanternCentres = lanternCentres;
        PaletteMap = paletteMap;
        if (paletteMap == null)
        {
            return;
        }

        foreach (byte code in paletteMap)
        {
            Dyes |= code > 0 ? 1 << (int)CharacterPaletteMap.DyeOf(code) : 0;
        }
    }

    public CharacterClip Clip { get; }
    public int FrameCount { get; }
    public int Width { get; }
    public int Height { get; }
    public Color32[] Pixels { get; }
    public IReadOnlyList<string> FramesAtEdge { get; }

    /// <summary>
    /// Where each frame's cast light shines from, in pixels from the feet on the screen; zero where none is drawn.
    /// </summary>
    public Vector2[] LightCentres { get; }

    /// <summary>
    /// How strongly each frame's cast light shines, 0 where none is drawn to 1.
    /// </summary>
    public float[] LightStrengths { get; }

    /// <summary>
    /// The lantern's layer, laid out as <see cref="Pixels"/>: the pixels it changes, clear elsewhere; null without one.
    /// </summary>
    public Color32[] LanternPixels { get; }

    /// <summary>
    /// Which of the lantern layer's pixels glow, its glass and wick, in alpha, laid out as <see cref="Pixels"/>; null
    /// without a lantern.
    /// </summary>
    public Color32[] LanternGlow { get; }

    /// <summary>
    /// Where each frame's lantern shines from, in pixels from the feet on the screen, whether or not the body hides it;
    /// null without a lantern.
    /// </summary>
    public Vector2[] LanternCentres { get; }

    /// <summary>
    /// The palette map, laid out as <see cref="Pixels"/>: per pixel its dye and tone (<see cref="CharacterPaletteMap.Code"/>),
    /// 0 where the sheet's colour stays; null for a character whose colours are its own.
    /// </summary>
    public byte[] PaletteMap { get; }

    /// <summary>
    /// The dyes the palette map names, a bit per <see cref="CharacterDye"/>; 0 without one.
    /// </summary>
    public int Dyes { get; }

    /// <summary>
    /// Whether the clip's frames carry a lantern.
    /// </summary>
    public bool HasLantern => LanternPixels != null;

    /// <summary>
    /// Where frame <paramref name="frame"/> of direction <paramref name="direction"/> lies on a sheet of frames of
    /// <paramref name="size"/>, in pixels from the bottom left: direction 0 in the top row.
    /// </summary>
    public static RectInt FrameRect(int direction, int frame, Vector2Int size)
    {
        return new RectInt(frame * size.x, (CharacterDirections.Count - 1 - direction) * size.y, size.x, size.y);
    }
}

/// <summary>
/// Draws a <see cref="CharacterRecipe"/>'s clips without touching an asset. The scale, in pixels per rig unit, is
/// set once so the standing character (<see cref="CharacterRig.StandingPose"/>, facing south) is the recipe's target
/// height, outline included: estimated from its height at one pixel per unit, then moved in steps of half a percent
/// while rounding leaves it a pixel off. Each frame is posed by its clip's motion, solved by <see cref="CharacterRig"/>, drawn by
/// the recipe's <see cref="CharacterPainter"/> onto a <see cref="CharacterCanvas"/> of the clip's frame size with the feet on its
/// pivot, and shaded with the recipe's, the weapon's and the motion's light colours. A recipe carrying a lantern has it
/// painted over each frame once the frame is shaded (<see cref="DrawLantern"/>), and the pixels that changed are the
/// frame's lantern layer, so the sheets themselves stay as they are without it. A recipe whose materials take a
/// player's colours (<see cref="CharacterRecipe.HasDyes"/>) also gets each frame's palette map (<see cref="PaletteMap"/>).
/// A recipe whose character wears parts (<see cref="CharacterRecipe.HasParts"/>, Docs/PlayerCharacter.md, Phase 6) is
/// drawn a layer at a time, its body then each part of its <see cref="Outfit"/> (its own until set) on a canvas of its own, merged by depth
/// (<see cref="ArtCanvas.MergeFrom"/>), so a part drawn on the body's surface covers it; <see cref="BakeLayers"/> keeps
/// every layer apart, before shading, for the runtime composer. The same recipe always gives the same pixels.
/// </summary>
public sealed class CharacterBaker
{
    private const int MeasureSize = 200;
    private const float SouthDegrees = 270f;
    private const float ScaleStep = 0.005f;
    private const int ScaleSteps = 40;

    private static readonly List<MotionEffect> NoEffects = new();
    private static readonly ColourRamp[] MaterialTones = MaterialToneRamps();

    private readonly CharacterRecipe recipe;
    private IReadOnlyList<CharacterLayerKey> parts = new CharacterLayerKey[0];

    /// <summary>
    /// Creates a baker of <paramref name="characterRecipe"/>, measuring its scale.
    /// </summary>
    public CharacterBaker(CharacterRecipe characterRecipe)
    {
        recipe = characterRecipe;
        parts = PartsOf(default);

        int rows = FilledRows(RenderPose(CharacterRig.StandingPose(recipe), null, SouthDegrees, new Vector2Int(MeasureSize, MeasureSize), new Vector2Int(MeasureSize / 2, MeasureSize / 4), null, NoEffects, 1f));
        Scale = (recipe.TargetHeight - 2f) / Mathf.Max(rows, 1);
        int height = StandingHeight();
        int target = recipe.TargetHeight;
        for (int step = 0; step < ScaleSteps && height != target; step++)
        {
            float next = Scale * (height < target ? 1f + ScaleStep : 1f - ScaleStep);
            int nextHeight = StandingHeightAt(next);
            if ((height < target) != (nextHeight < target) && Mathf.Abs(nextHeight - target) > Mathf.Abs(height - target))
            {
                break;
            }

            Scale = next;
            height = nextHeight;
        }
    }

    /// <summary>
    /// Pixels per rig unit.
    /// </summary>
    public float Scale { get; private set; }

    /// <summary>
    /// The parts <see cref="Render"/> and <see cref="Bake"/> draw the character in (Docs/PlayerCharacter.md, Phase 6):
    /// the first option of each slot until set, the recipe's own; the scale stays as measured on those.
    /// </summary>
    public CharacterPartPicks Outfit
    {
        get => outfit;
        set
        {
            outfit = value;
            parts = PartsOf(value);
        }
    }

    private CharacterPartPicks outfit;

    /// <summary>
    /// The layers of the parts <paramref name="picks"/> pick, one per slot with options, a slot drawn under another's
    /// option under the one picked there; an option the recipe lacks counts as the first.
    /// </summary>
    private IReadOnlyList<CharacterLayerKey> PartsOf(CharacterPartPicks picks)
    {
        List<CharacterLayerKey> keys = new();
        foreach (CharacterSlot slot in (CharacterSlot[])Enum.GetValues(typeof(CharacterSlot)))
        {
            int options = recipe.PartOptions(slot);
            if (options == 0)
            {
                continue;
            }

            int under = recipe.PartVariesWith(slot);
            int variant = under >= 0 ? Picked(picks, (CharacterSlot)under) : 0;
            keys.Add(new CharacterLayerKey((int)slot, Picked(picks, slot), variant));
        }

        return keys;
    }

    /// <summary>
    /// The option <paramref name="picks"/> pick in <paramref name="slot"/>, the first for one the recipe lacks.
    /// </summary>
    private int Picked(CharacterPartPicks picks, CharacterSlot slot)
    {
        int option = picks.Get(slot);
        return option < recipe.PartOptions(slot) ? option : 0;
    }

    /// <summary>
    /// Draws every frame of <paramref name="clip"/> in every direction as its sheet.
    /// </summary>
    public CharacterSheet Bake(CharacterClip clip)
    {
        if (recipe.HasParts)
        {
            return BakeComposed(clip);
        }

        int frames = clip.Motion.FrameCount;
        Vector2Int size = clip.FrameSize;
        int width = frames * size.x;
        Color32[] pixels = new Color32[width * CharacterDirections.Count * size.y];
        List<string> atEdge = new();
        Vector2[] lightCentres = new Vector2[CharacterDirections.Count * frames];
        float[] lightStrengths = new float[lightCentres.Length];
        bool carries = recipe.Lantern != null;
        byte[] paletteMap = recipe.HasDyes ? new byte[pixels.Length] : null;
        Color32[] lanternPixels = carries ? new Color32[pixels.Length] : null;
        Color32[] lanternGlow = carries ? new Color32[pixels.Length] : null;
        Vector2[] lanternCentres = carries ? new Vector2[lightCentres.Length] : null;
        for (int direction = 0; direction < CharacterDirections.Count; direction++)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                CharacterCanvas canvas = Render(clip, direction, frame);
                lightCentres[direction * frames + frame] = canvas.LightCentre;
                lightStrengths[direction * frames + frame] = canvas.LightStrength;
                Color32[] colours = Shade(canvas, clip.Weapon, clip.Motion.Light);
                if (TouchesEdge(colours, size))
                {
                    atEdge.Add($"{CharacterDirections.Name(direction)} {frame}");
                }

                RectInt rect = CharacterSheet.FrameRect(direction, frame, size);
                for (int y = 0; y < size.y; y++)
                {
                    Array.Copy(colours, y * size.x, pixels, (rect.y + y) * width + rect.x, size.x);
                }

                if (paletteMap != null)
                {
                    byte[] codes = PaletteMap(canvas);
                    for (int y = 0; y < size.y; y++)
                    {
                        Array.Copy(codes, y * size.x, paletteMap, (rect.y + y) * width + rect.x, size.x);
                    }
                }

                if (!carries)
                {
                    continue;
                }

                DrawLantern(clip, direction, frame, canvas);
                lanternCentres[direction * frames + frame] = canvas.LanternCentre;
                Color32[] carried = Shade(canvas, clip.Weapon, clip.Motion.Light);
                if (TouchesEdge(carried, size) && !atEdge.Contains($"{CharacterDirections.Name(direction)} {frame}"))
                {
                    atEdge.Add($"{CharacterDirections.Name(direction)} {frame}");
                }

                for (int y = 0; y < size.y; y++)
                {
                    for (int x = 0; x < size.x; x++)
                    {
                        int i = y * size.x + x;
                        if (carried[i].Equals(colours[i]))
                        {
                            continue;
                        }

                        int o = (rect.y + y) * width + rect.x + x;
                        lanternPixels[o] = carried[i];
                        if (canvas.IsFilled(x, y) && CharacterMaterials.Glows(canvas.MaterialAt(x, y)))
                        {
                            lanternGlow[o] = new Color32(0, 0, 0, 255);
                        }
                    }
                }
            }
        }

        return new CharacterSheet(clip, frames, pixels, atEdge, lightCentres, lightStrengths, lanternPixels, lanternGlow, lanternCentres, paletteMap);
    }

    /// <summary>
    /// Draws frame <paramref name="frame"/> of <paramref name="clip"/> facing direction <paramref name="direction"/>
    /// turned by the pose's spin, unshaded, so tests can read what each pixel is. A smear's samples are turned by their
    /// own spin against the frame's.
    /// </summary>
    public CharacterCanvas Render(CharacterClip clip, int direction, int frame)
    {
        CharacterPose pose = PoseOf(clip, frame, out List<BladeSample> smear);
        return RenderPose(pose, clip.Weapon, CharacterDirections.Angle(direction) + pose.Spin, clip.FrameSize, clip.Pivot, smear, clip.Motion.EffectsOn(frame), Scale);
    }

    /// <summary>
    /// The sheet of <paramref name="clip"/> for a recipe whose character wears parts: its body's layer and the
    /// <see cref="Outfit"/>'s parts (<see cref="BakeLayers"/>) composed by the runtime's <see cref="CharacterComposer"/>,
    /// so what the game composes for any outfit is what the editor bakes for it, with the palette map it gives and each
    /// frame's cast light from the body's layer.
    /// </summary>
    private CharacterSheet BakeComposed(CharacterClip clip)
    {
        int frames = clip.Motion.FrameCount;
        Vector2Int size = clip.FrameSize;
        int width = frames * size.x;
        Color32[] pixels = new Color32[width * CharacterDirections.Count * size.y];
        byte[] paletteMap = new byte[pixels.Length];
        List<CharacterLayerKey> keys = new() { CharacterLayerKey.Base };
        keys.AddRange(parts);
        Vector2[] lightCentres = new Vector2[CharacterDirections.Count * frames];
        float[] lightStrengths = new float[lightCentres.Length];
        CharacterLayerClip[] layers = BakeLayers(clip, keys, lightCentres, lightStrengths);
        CharacterComposer composer = new();
        List<CharacterLayerFrame> merged = new();
        Color32[] colours = new Color32[size.x * size.y];
        byte[] codes = new byte[colours.Length];
        List<string> atEdge = new();
        for (int direction = 0; direction < CharacterDirections.Count; direction++)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                merged.Clear();
                foreach (CharacterLayerClip layer in layers)
                {
                    merged.Add(layer.Frames[direction * frames + frame]);
                }

                composer.ComposeFrame(size.x, size.y, merged, CharacterComposer.MaterialsOf(layers), recipe.InnerLineGap, colours, codes);
                if (TouchesEdge(colours, size))
                {
                    atEdge.Add($"{CharacterDirections.Name(direction)} {frame}");
                }

                RectInt rect = CharacterSheet.FrameRect(direction, frame, size);
                for (int y = 0; y < size.y; y++)
                {
                    Array.Copy(colours, y * size.x, pixels, (rect.y + y) * width + rect.x, size.x);
                    Array.Copy(codes, y * size.x, paletteMap, (rect.y + y) * width + rect.x, size.x);
                }
            }
        }

        return new CharacterSheet(clip, frames, pixels, atEdge, lightCentres, lightStrengths, paletteMap: recipe.HasDyes ? paletteMap : null);
    }

    /// <summary>
    /// Every layer of the recipe's parts (Docs/PlayerCharacter.md, Phase 6): the body's, then each option of each slot,
    /// for a slot drawn under another's option once per option of that slot; none for a recipe without parts.
    /// </summary>
    public IReadOnlyList<CharacterLayerKey> LayerKeys()
    {
        List<CharacterLayerKey> keys = new();
        if (!recipe.HasParts)
        {
            return keys;
        }

        keys.Add(CharacterLayerKey.Base);
        foreach (CharacterSlot slot in (CharacterSlot[])Enum.GetValues(typeof(CharacterSlot)))
        {
            int under = recipe.PartVariesWith(slot);
            int variants = under >= 0 ? Mathf.Max(1, recipe.PartOptions((CharacterSlot)under)) : 1;
            for (int option = 0; option < recipe.PartOptions(slot); option++)
            {
                for (int variant = 0; variant < variants; variant++)
                {
                    keys.Add(new CharacterLayerKey((int)slot, option, variant));
                }
            }
        }

        return keys;
    }

    /// <summary>
    /// Draws every frame of <paramref name="clip"/> in every direction as each of <paramref name="keys"/>' layers, each
    /// on a canvas of its own, kept before shading for the runtime composer (<see cref="CharacterLayer"/>): per frame the
    /// box around what the layer draws and per pixel its material, part, tone, drawing step and depth, frames direction by direction
    /// and within each frame by frame. The body's layer also holds the clip's material table: whether each material
    /// lines its edges, its dye and the ramp <see cref="Shade(CharacterCanvas, CharacterWeaponArt, ColourRamp)"/>
    /// colours it with. A frame's pose, solved once, draws every layer.
    /// </summary>
    public CharacterLayerClip[] BakeLayers(CharacterClip clip, IReadOnlyList<CharacterLayerKey> keys)
    {
        return BakeLayers(clip, keys, null, null);
    }

    /// <summary>
    /// <see cref="BakeLayers(CharacterClip, IReadOnlyList{CharacterLayerKey})"/>, keeping where each frame's cast light
    /// shines from and how strongly, as the body's layer marks it, in <paramref name="lightCentres"/> and
    /// <paramref name="lightStrengths"/> (direction by direction, frame by frame) when given.
    /// </summary>
    private CharacterLayerClip[] BakeLayers(CharacterClip clip, IReadOnlyList<CharacterLayerKey> keys, Vector2[] lightCentres, float[] lightStrengths)
    {
        int frames = clip.Motion.FrameCount;
        CharacterLayerFrame[][] layers = new CharacterLayerFrame[keys.Count][];
        for (int k = 0; k < keys.Count; k++)
        {
            layers[k] = new CharacterLayerFrame[CharacterDirections.Count * frames];
        }

        for (int frame = 0; frame < frames; frame++)
        {
            CharacterPose pose = PoseOf(clip, frame, out List<BladeSample> smear);
            CharacterJoints joints = CharacterRig.Solve(recipe, pose, clip.Weapon);
            IReadOnlyList<MotionEffect> effects = clip.Motion.EffectsOn(frame);
            for (int direction = 0; direction < CharacterDirections.Count; direction++)
            {
                float facing = CharacterDirections.Angle(direction) + pose.Spin;
                for (int k = 0; k < keys.Count; k++)
                {
                    CharacterCanvas canvas = new(clip.FrameSize.x, clip.FrameSize.y, clip.Pivot, Scale);
                    recipe.CreatePainter(clip.Weapon, canvas, keys[k]).Paint(facing, pose, joints, smear, effects);
                    layers[k][direction * frames + frame] = Extract(canvas);
                    if (keys[k].IsBase && lightCentres != null)
                    {
                        lightCentres[direction * frames + frame] = canvas.LightCentre;
                        lightStrengths[direction * frames + frame] = canvas.LightStrength;
                    }
                }
            }
        }

        CharacterLayerMaterial[] table = Table(clip.Weapon, clip.Motion.Light);
        CharacterLayerClip[] clips = new CharacterLayerClip[keys.Count];
        for (int k = 0; k < keys.Count; k++)
        {
            CharacterLayerMaterial[] own = keys[k].IsBase ? table : null;
            CharacterWeaponArt carried = keys[k].IsBase ? null : recipe.PartWeapon((CharacterSlot)keys[k].Slot, keys[k].Option);
            if (carried != null && clip.Weapon == null)
            {
                own = Table(carried, clip.Motion.Light);
            }

            clips[k] = new CharacterLayerClip(clip.FrameSize, clip.Pivot, layers[k], own);
        }

        return clips;
    }

    /// <summary>
    /// The material table of a clip holding <paramref name="weapon"/> (or none) with <paramref name="light"/> for a
    /// cast's light: whether each material lines its edges, its dye and its ramp.
    /// </summary>
    private CharacterLayerMaterial[] Table(CharacterWeaponArt weapon, ColourRamp light)
    {
        ColourRamp[] ramps = Ramps(weapon, light);
        CharacterLayerMaterial[] table = new CharacterLayerMaterial[ramps.Length];
        for (int i = 0; i < table.Length; i++)
        {
            CharacterMaterial material = (CharacterMaterial)i;
            table[i] = new CharacterLayerMaterial(CharacterMaterials.HasOutline(material), recipe.DyeOf(material), ramps[i]);
        }

        return table;
    }

    /// <summary>
    /// The pose of frame <paramref name="frame"/> of <paramref name="clip"/>, and the blade's smear samples drawn on it
    /// (null for none), each turned by its own spin against the frame's.
    /// </summary>
    private CharacterPose PoseOf(CharacterClip clip, int frame, out List<BladeSample> smear)
    {
        CharacterMotion motion = clip.Motion;
        CharacterWeaponArt weapon = clip.Weapon;
        CharacterPose pose = motion.PoseAt(frame, recipe, weapon);
        smear = null;
        if (weapon != null && weapon.Kind == WeaponKind.Blade && motion.TryGetSmear(frame, out MotionSmear span))
        {
            int count = motion.SmearSamples;
            smear = new List<BladeSample>(count);
            for (int i = 0; i < count; i++)
            {
                CharacterPose sample = motion.SmearPoseAt(frame, Mathf.Lerp(span.From, span.To, i / (float)(count - 1)), recipe, weapon);
                Quaternion turn = Quaternion.AngleAxis(sample.Spin - pose.Spin, Vector3.forward);
                smear.Add(new BladeSample(turn * CharacterRig.Solve(recipe, sample, weapon).RightHand, turn * sample.Blade.normalized));
            }
        }

        return pose;
    }

    /// <summary>
    /// A layer's frame drawn on <paramref name="canvas"/>: the box around its filled pixels and, per pixel of the box,
    /// its material, part, tone before any line, drawing step and depth, the depth on the steps of
    /// <see cref="CharacterLayer.DepthSteps"/> a pixel past the frame's nearest whole pixel
    /// (<see cref="CharacterLayer.Quantize"/>), as the layer's file keeps it.
    /// </summary>
    private CharacterLayerFrame Extract(CharacterCanvas canvas)
    {
        int x0 = canvas.Width;
        int y0 = canvas.Height;
        int x1 = -1;
        int y1 = -1;
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                if (!canvas.IsFilled(x, y))
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
            return new CharacterLayerFrame(new RectInt(0, 0, 0, 0), Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<float>());
        }

        RectInt box = new(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        int count = box.width * box.height;
        byte[] material = new byte[count];
        byte[] part = new byte[count];
        byte[] tone = new byte[count];
        byte[] step = new byte[count];
        float[] depth = new float[count];
        for (int y = 0; y < box.height; y++)
        {
            for (int x = 0; x < box.width; x++)
            {
                int i = y * box.width + x;
                int cx = box.x + x;
                int cy = box.y + y;
                if (!canvas.IsFilled(cx, cy))
                {
                    material[i] = CharacterLayer.Empty;
                    depth[i] = float.PositiveInfinity;
                    continue;
                }

                CharacterMaterial value = canvas.MaterialAt(cx, cy);
                material[i] = (byte)value;
                part[i] = (byte)canvas.PartAt(cx, cy);
                tone[i] = (byte)canvas.BaseTone(cx, cy, CharacterMaterials.IsFlat(value), recipe.LightThreshold, recipe.ShadowThreshold);
                step[i] = (byte)canvas.StepAt(cx, cy);
                depth[i] = canvas.DepthAt(cx, cy);
            }
        }

        CharacterLayer.Quantize(material, depth);
        return new CharacterLayerFrame(box, material, part, tone, step, depth);
    }

    /// <summary>
    /// Paints the recipe's lantern over <paramref name="canvas"/>, frame <paramref name="frame"/> of
    /// <paramref name="clip"/> facing direction <paramref name="direction"/> as <see cref="Render"/> drew it, hanging as
    /// that frame's pose holds it (see <see cref="CharacterPainter.PaintLantern"/>); nothing without a lantern.
    /// </summary>
    public void DrawLantern(CharacterClip clip, int direction, int frame, CharacterCanvas canvas)
    {
        if (recipe.Lantern == null)
        {
            return;
        }

        CharacterPose pose = clip.Motion.PoseAt(frame, recipe, clip.Weapon);
        CharacterJoints joints = CharacterRig.Solve(recipe, pose, clip.Weapon);
        recipe.CreatePainter(clip.Weapon, canvas).PaintLantern(recipe.Lantern, CharacterDirections.Angle(direction) + pose.Spin, pose, joints);
    }

    /// <summary>
    /// The colours of a drawn frame, with the recipe's colours for the character and <paramref name="weapon"/>'s for
    /// the weapon and its effects.
    /// </summary>
    public Color32[] Shade(CharacterCanvas canvas, CharacterWeaponArt weapon)
    {
        return Shade(canvas, weapon, default);
    }

    /// <summary>
    /// The colours of a drawn frame, with the recipe's colours for the character, <paramref name="weapon"/>'s for the
    /// weapon and its effects, <paramref name="light"/> for a cast's light (its rim, glow and core each flat in the
    /// light's shadow, middle and light colours, outlined in its outline colour) and the recipe's lantern's for the
    /// lantern.
    /// </summary>
    public Color32[] Shade(CharacterCanvas canvas, CharacterWeaponArt weapon, ColourRamp light)
    {
        return canvas.Shade(Ramps(weapon, light), recipe.LightThreshold, recipe.ShadowThreshold, recipe.InnerLineGap);
    }

    /// <summary>
    /// The ramp each material is drawn in: the recipe's for the character, <paramref name="weapon"/>'s for the weapon and
    /// its effects, <paramref name="light"/> for a cast's light and the recipe's lantern's for the lantern.
    /// </summary>
    private ColourRamp[] Ramps(CharacterWeaponArt weapon, ColourRamp light)
    {
        ColourRamp[] ramps = new ColourRamp[CharacterMaterials.Count];
        for (int i = 0; i < ramps.Length; i++)
        {
            CharacterMaterial material = (CharacterMaterial)i;
            if (CharacterMaterials.IsLight(material))
            {
                Color32 tone = material == CharacterMaterial.LightRim ? light.Shadow : material == CharacterMaterial.LightGlow ? light.Middle : light.Light;
                ramps[i] = new ColourRamp(light.Outline, tone, tone, tone);
                continue;
            }

            if (CharacterMaterials.IsLantern(material))
            {
                ramps[i] = recipe.Lantern != null ? recipe.Lantern.Ramp(material) : default;
                continue;
            }

            ramps[i] = CharacterMaterials.IsWeapons(material) ? (weapon != null ? weapon.Ramp(material) : default) : recipe.Ramp(material);
        }

        return ramps;
    }

    /// <summary>
    /// The palette map of a drawn frame (Docs/PlayerCharacter.md, Phase 5), rows from the bottom: per pixel the dye the
    /// recipe gives the material <see cref="Shade(CharacterCanvas, CharacterWeaponArt, ColourRamp)"/> colours it with and
    /// the tone it takes (<see cref="CharacterPaletteMap.Code"/>), its outline and inner lines included, found by shading
    /// the frame the same way with each material's tones as its colours; 0 where the material keeps its colour or nothing
    /// is drawn.
    /// </summary>
    public byte[] PaletteMap(CharacterCanvas canvas)
    {
        Color32[] tones = canvas.Shade(MaterialTones, recipe.LightThreshold, recipe.ShadowThreshold, recipe.InnerLineGap);
        byte[] codes = new byte[tones.Length];
        for (int i = 0; i < tones.Length; i++)
        {
            if (tones[i].a == 0)
            {
                continue;
            }

            CharacterDye dye = recipe.DyeOf((CharacterMaterial)tones[i].r);
            codes[i] = dye == CharacterDye.None ? (byte)0 : CharacterPaletteMap.Code(dye, tones[i].g);
        }

        return codes;
    }

    /// <summary>
    /// How many pixels tall the standing character is, outline included, at the baker's scale.
    /// </summary>
    public int StandingHeight()
    {
        return StandingHeightAt(Scale);
    }

    /// <summary>
    /// How many pixels tall the standing character is, outline included, at <paramref name="scale"/> pixels per rig
    /// unit.
    /// </summary>
    private int StandingHeightAt(float scale)
    {
        CharacterCanvas canvas = RenderPose(CharacterRig.StandingPose(recipe), null, SouthDegrees, new Vector2Int(MeasureSize, MeasureSize), new Vector2Int(MeasureSize / 2, MeasureSize / 4), null, NoEffects, scale);
        return FilledRows(canvas) + 2;
    }

    /// <summary>
    /// Draws <paramref name="pose"/> holding <paramref name="weapon"/> facing <paramref name="facing"/> degrees onto
    /// a new canvas: for a recipe with parts, its body and the <see cref="Outfit"/>'s option of each part merged by depth.
    /// </summary>
    private CharacterCanvas RenderPose(CharacterPose pose, CharacterWeaponArt weapon, float facing, Vector2Int size, Vector2Int pivot, IReadOnlyList<BladeSample> smear, IReadOnlyList<MotionEffect> effects, float scale)
    {
        CharacterCanvas canvas = new(size.x, size.y, pivot, scale);
        CharacterJoints joints = CharacterRig.Solve(recipe, pose, weapon);
        recipe.CreatePainter(weapon, canvas, CharacterLayerKey.Base).Paint(facing, pose, joints, smear, effects);
        foreach (CharacterLayerKey part in parts)
        {
            CharacterCanvas layer = new(size.x, size.y, pivot, scale);
            recipe.CreatePainter(weapon, layer, part).Paint(facing, pose, joints, smear, effects);
            canvas.MergeFrom(layer);
        }

        return canvas;
    }

    /// <summary>
    /// A ramp per material whose colours name it and their tone: the material in red, the tone (0 the outline to 3 the
    /// light) in green.
    /// </summary>
    private static ColourRamp[] MaterialToneRamps()
    {
        ColourRamp[] ramps = new ColourRamp[CharacterMaterials.Count];
        for (int i = 0; i < ramps.Length; i++)
        {
            byte m = (byte)i;
            ramps[i] = new ColourRamp(new Color32(m, 0, 0, 255), new Color32(m, 1, 0, 255), new Color32(m, 2, 0, 255), new Color32(m, 3, 0, 255));
        }

        return ramps;
    }

    /// <summary>
    /// How many rows, from the lowest to the highest, hold anything.
    /// </summary>
    private static int FilledRows(CharacterCanvas canvas)
    {
        int lowest = -1;
        int highest = -1;
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                if (!canvas.IsFilled(x, y))
                {
                    continue;
                }

                lowest = lowest < 0 ? y : lowest;
                highest = y;
                break;
            }
        }

        return lowest < 0 ? 0 : highest - lowest + 1;
    }

    /// <summary>
    /// Whether any pixel on the frame's border is drawn.
    /// </summary>
    private static bool TouchesEdge(Color32[] colours, Vector2Int size)
    {
        for (int x = 0; x < size.x; x++)
        {
            if (colours[x].a > 0 || colours[(size.y - 1) * size.x + x].a > 0)
            {
                return true;
            }
        }

        for (int y = 0; y < size.y; y++)
        {
            if (colours[y * size.x].a > 0 || colours[y * size.x + size.x - 1].a > 0)
            {
                return true;
            }
        }

        return false;
    }
}
