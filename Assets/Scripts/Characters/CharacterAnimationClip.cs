using System;
using UnityEngine;

/// <summary>
/// One baked clip of a <see cref="CharacterAnimationSet"/>: its sprites, a row of frames per direction
/// (<see cref="CharacterDirections"/>, east first), how long each frame shows at the clip's natural speed, the
/// locomotion it shows, whether it is drawn with a weapon, and for a gait its natural speed: how fast its planted feet
/// travel over the ground at that rate, east or west, in units per second, so playing it at speed over natural speed
/// keeps the feet from sliding. The character baker writes it; nothing changes it at runtime.
/// </summary>
[Serializable]
public sealed class CharacterAnimationClip
{
    [SerializeField] private string name;
    [SerializeField] private CharacterGait gait;
    [SerializeField] private bool armed;
    [SerializeField] private Sprite[] sprites = Array.Empty<Sprite>();
    [SerializeField] private float[] frameSeconds = Array.Empty<float>();
    [SerializeField, Min(0f)] private float naturalSpeed;

    public string Name => name;
    public CharacterGait Gait => gait;
    public bool Armed => armed;
    public int FrameCount => frameSeconds.Length;
    public float NaturalSpeed => naturalSpeed;

    /// <summary>
    /// Whether the clip has frames and a sprite for every direction and frame.
    /// </summary>
    public bool IsComplete => frameSeconds.Length > 0 && sprites.Length == CharacterDirections.Count * frameSeconds.Length;

    /// <summary>
    /// The clip's length at its natural speed, in seconds.
    /// </summary>
    public float Duration
    {
        get
        {
            float total = 0f;
            for (int i = 0; i < frameSeconds.Length; i++)
            {
                total += frameSeconds[i];
            }

            return total;
        }
    }

    /// <summary>
    /// Creates a clip from its sprites, direction by direction and within each direction frame by frame (east first,
    /// counterclockwise), and the duration of each frame; for the baker and tests.
    /// </summary>
    public static CharacterAnimationClip Create(string clipName, CharacterGait clipGait, bool drawnArmed, Sprite[] clipSprites, float[] clipFrameSeconds, float clipNaturalSpeed)
    {
        return new CharacterAnimationClip
        {
            name = clipName,
            gait = clipGait,
            armed = drawnArmed,
            sprites = clipSprites ?? Array.Empty<Sprite>(),
            frameSeconds = clipFrameSeconds ?? Array.Empty<float>(),
            naturalSpeed = Mathf.Max(0f, clipNaturalSpeed)
        };
    }

    /// <summary>
    /// How long frame <paramref name="frame"/> shows at the clip's natural speed, in seconds.
    /// </summary>
    public float FrameSeconds(int frame)
    {
        return frameSeconds[frame];
    }

    /// <summary>
    /// The sprite of frame <paramref name="frame"/> facing direction <paramref name="direction"/>.
    /// </summary>
    public Sprite SpriteAt(int direction, int frame)
    {
        return sprites[direction * frameSeconds.Length + frame];
    }
}
