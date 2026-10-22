using UnityEngine;

/// <summary>
/// The frames of a baked one-shot effect (Docs/Animation.md, Phase 6), written by the effect recipes' writer: one
/// sequence of frames per variant (an impact's damage type) and direction (evenly spaced, counterclockwise from east),
/// each frame shown for <see cref="FrameSeconds"/>. Nothing rotates or scales an effect at runtime: a direction is
/// baked. Runtime code only reads it.
/// </summary>
public sealed class EffectSprites : ScriptableObject
{
    [SerializeField, Tooltip("Every frame, variant by variant, then direction by direction, then frame by frame.")]
    private Sprite[] sprites = new Sprite[0];
    [SerializeField, Min(1)] private int variants = 1;
    [SerializeField, Min(1)] private int directions = 1;
    [SerializeField, Min(1)] private int frames = 1;
    [SerializeField, Min(0.001f), Tooltip("How long each frame shows, in seconds.")]
    private float frameSeconds = 0.04f;

    public int VariantCount => variants;
    public int DirectionCount => directions;
    public int FrameCount => frames;
    public float FrameSeconds => frameSeconds;
    public float Duration => frames * frameSeconds;

    /// <summary>
    /// The sprite of frame <paramref name="frame"/> of <paramref name="variant"/> facing
    /// <paramref name="direction"/>, each clamped to its range; null when a sprite is missing.
    /// </summary>
    public Sprite Get(int variant, int direction, int frame)
    {
        int v = Mathf.Clamp(variant, 0, variants - 1);
        int d = Mathf.Clamp(direction, 0, directions - 1);
        int f = Mathf.Clamp(frame, 0, frames - 1);
        int index = (v * directions + d) * frames + f;
        return index < sprites.Length ? sprites[index] : null;
    }

    /// <summary>
    /// The baked direction nearest <paramref name="direction"/>: its angle counterclockwise from east over the step
    /// between directions, rounded; the first for none.
    /// </summary>
    public int DirectionOf(Vector2 direction)
    {
        if (directions <= 1 || direction.sqrMagnitude < 1e-8f)
        {
            return 0;
        }

        float turns = Mathf.Atan2(direction.y, direction.x) / (2f * Mathf.PI);
        return ((Mathf.RoundToInt(turns * directions) % directions) + directions) % directions;
    }

    /// <summary>
    /// Creates an unsaved set of <paramref name="effectSprites"/> (variant by variant, then direction, then frame), for
    /// the writer and tests.
    /// </summary>
    public static EffectSprites Create(Sprite[] effectSprites, int variantCount, int directionCount, int frameCount, float seconds)
    {
        EffectSprites set = CreateInstance<EffectSprites>();
        set.sprites = effectSprites;
        set.variants = Mathf.Max(1, variantCount);
        set.directions = Mathf.Max(1, directionCount);
        set.frames = Mathf.Max(1, frameCount);
        set.frameSeconds = Mathf.Max(0.001f, seconds);
        return set;
    }
}
