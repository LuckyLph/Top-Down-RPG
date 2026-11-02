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
/// which of those pixels glow, and where the lantern shines from on each frame.
/// </summary>
public sealed class CharacterSheet
{
    /// <summary>
    /// Creates the sheet of <paramref name="clip"/> from its pixels.
    /// </summary>
    public CharacterSheet(CharacterClip clip, int frameCount, Color32[] pixels, IReadOnlyList<string> framesAtEdge, Vector2[] lightCentres, float[] lightStrengths,
        Color32[] lanternPixels = null, Color32[] lanternGlow = null, Vector2[] lanternCentres = null)
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
/// frame's lantern layer, so the sheets themselves stay as they are without it. The same recipe always gives the same
/// pixels.
/// </summary>
public sealed class CharacterBaker
{
    private const int MeasureSize = 200;
    private const float SouthDegrees = 270f;
    private const float ScaleStep = 0.005f;
    private const int ScaleSteps = 40;

    private static readonly List<MotionEffect> NoEffects = new();

    private readonly CharacterRecipe recipe;

    /// <summary>
    /// Creates a baker of <paramref name="characterRecipe"/>, measuring its scale.
    /// </summary>
    public CharacterBaker(CharacterRecipe characterRecipe)
    {
        recipe = characterRecipe;
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
    /// Draws every frame of <paramref name="clip"/> in every direction as its sheet.
    /// </summary>
    public CharacterSheet Bake(CharacterClip clip)
    {
        int frames = clip.Motion.FrameCount;
        Vector2Int size = clip.FrameSize;
        int width = frames * size.x;
        Color32[] pixels = new Color32[width * CharacterDirections.Count * size.y];
        List<string> atEdge = new();
        Vector2[] lightCentres = new Vector2[CharacterDirections.Count * frames];
        float[] lightStrengths = new float[lightCentres.Length];
        bool carries = recipe.Lantern != null;
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

        return new CharacterSheet(clip, frames, pixels, atEdge, lightCentres, lightStrengths, lanternPixels, lanternGlow, lanternCentres);
    }

    /// <summary>
    /// Draws frame <paramref name="frame"/> of <paramref name="clip"/> facing direction <paramref name="direction"/>,
    /// unshaded, so tests can read what each pixel is.
    /// </summary>
    public CharacterCanvas Render(CharacterClip clip, int direction, int frame)
    {
        CharacterMotion motion = clip.Motion;
        CharacterWeaponArt weapon = clip.Weapon;
        CharacterPose pose = motion.PoseAt(frame, recipe, weapon);
        List<BladeSample> smear = null;
        if (weapon != null && weapon.Kind == WeaponKind.Blade && motion.TryGetSmear(frame, out MotionSmear span))
        {
            int count = motion.SmearSamples;
            smear = new List<BladeSample>(count);
            for (int i = 0; i < count; i++)
            {
                CharacterPose sample = motion.SmearPoseAt(frame, Mathf.Lerp(span.From, span.To, i / (float)(count - 1)), recipe, weapon);
                smear.Add(new BladeSample(CharacterRig.Solve(recipe, sample, weapon).RightHand, sample.Blade.normalized));
            }
        }

        return RenderPose(pose, weapon, CharacterDirections.Angle(direction), clip.FrameSize, clip.Pivot, smear, motion.EffectsOn(frame), Scale);
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
        recipe.CreatePainter(clip.Weapon, canvas).PaintLantern(recipe.Lantern, CharacterDirections.Angle(direction), pose, joints);
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

        return canvas.Shade(ramps, recipe.LightThreshold, recipe.ShadowThreshold, recipe.InnerLineGap);
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
    /// a new canvas.
    /// </summary>
    private CharacterCanvas RenderPose(CharacterPose pose, CharacterWeaponArt weapon, float facing, Vector2Int size, Vector2Int pivot, IReadOnlyList<BladeSample> smear, IReadOnlyList<MotionEffect> effects, float scale)
    {
        CharacterCanvas canvas = new(size.x, size.y, pivot, scale);
        CharacterJoints joints = CharacterRig.Solve(recipe, pose, weapon);
        recipe.CreatePainter(weapon, canvas).Paint(facing, pose, joints, smear, effects);
        return canvas;
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
