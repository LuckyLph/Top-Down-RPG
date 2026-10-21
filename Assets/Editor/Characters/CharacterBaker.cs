using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One clip drawn as a sheet: a row per direction (<see cref="CharacterDirections"/>, east at the top), a column per
/// frame, pixels rows from the bottom, and the frames whose drawing reached their edge (named "direction frame"),
/// which the inspector warns about.
/// </summary>
public sealed class CharacterSheet
{
    /// <summary>
    /// Creates the sheet of <paramref name="clip"/> from its pixels.
    /// </summary>
    public CharacterSheet(CharacterClip clip, int frameCount, Color32[] pixels, IReadOnlyList<string> framesAtEdge)
    {
        Clip = clip;
        FrameCount = frameCount;
        Width = frameCount * clip.FrameSize.x;
        Height = CharacterDirections.Count * clip.FrameSize.y;
        Pixels = pixels;
        FramesAtEdge = framesAtEdge;
    }

    public CharacterClip Clip { get; }
    public int FrameCount { get; }
    public int Width { get; }
    public int Height { get; }
    public Color32[] Pixels { get; }
    public IReadOnlyList<string> FramesAtEdge { get; }

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
/// height, outline included. Each frame is posed by its clip's motion, solved by <see cref="CharacterRig"/>, drawn by
/// <see cref="CharacterPainter"/> onto a <see cref="CharacterCanvas"/> of the clip's frame size with the feet on its
/// pivot, and shaded with the recipe's and the weapon's colours. The same recipe always gives the same pixels.
/// </summary>
public sealed class CharacterBaker
{
    private const int MeasureSize = 200;
    private const float SouthDegrees = 270f;

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
    }

    /// <summary>
    /// Pixels per rig unit.
    /// </summary>
    public float Scale { get; }

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
        for (int direction = 0; direction < CharacterDirections.Count; direction++)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                Color32[] colours = Shade(Render(clip, direction, frame), clip.Weapon);
                if (TouchesEdge(colours, size))
                {
                    atEdge.Add($"{CharacterDirections.Name(direction)} {frame}");
                }

                RectInt rect = CharacterSheet.FrameRect(direction, frame, size);
                for (int y = 0; y < size.y; y++)
                {
                    Array.Copy(colours, y * size.x, pixels, (rect.y + y) * width + rect.x, size.x);
                }
            }
        }

        return new CharacterSheet(clip, frames, pixels, atEdge);
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
                CharacterPose sample = motion.PoseAtTime(Mathf.Lerp(span.From, span.To, i / (float)(count - 1)), recipe, weapon);
                smear.Add(new BladeSample(CharacterRig.Solve(recipe, sample, weapon).RightHand, sample.Blade.normalized));
            }
        }

        return RenderPose(pose, weapon, CharacterDirections.Angle(direction), clip.FrameSize, clip.Pivot, smear, motion.EffectsOn(frame), Scale);
    }

    /// <summary>
    /// The colours of a drawn frame, with the recipe's colours for the character and <paramref name="weapon"/>'s for
    /// the weapon and its effects.
    /// </summary>
    public Color32[] Shade(CharacterCanvas canvas, CharacterWeaponArt weapon)
    {
        ColourRamp[] ramps = new ColourRamp[CharacterMaterials.Count];
        for (int i = 0; i < ramps.Length; i++)
        {
            CharacterMaterial material = (CharacterMaterial)i;
            ramps[i] = CharacterMaterials.IsWeapons(material) ? (weapon != null ? weapon.Ramp(material) : default) : recipe.Ramp(material);
        }

        return canvas.Shade(ramps, recipe.LightThreshold, recipe.ShadowThreshold, recipe.InnerLineGap);
    }

    /// <summary>
    /// How many pixels tall the standing character is, outline included, at the baker's scale.
    /// </summary>
    public int StandingHeight()
    {
        CharacterCanvas canvas = RenderPose(CharacterRig.StandingPose(recipe), null, SouthDegrees, new Vector2Int(MeasureSize, MeasureSize), new Vector2Int(MeasureSize / 2, MeasureSize / 4), null, NoEffects, Scale);
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
        new CharacterPainter(recipe, weapon, canvas).Paint(facing, pose, joints, smear, effects);
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
