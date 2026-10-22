using System;
using UnityEngine;

/// <summary>
/// One baked clip of a <see cref="CharacterAnimationSet"/>: its sprites, a row of frames per direction
/// (<see cref="CharacterDirections"/>, east first), how long each frame shows at the clip's natural speed, the
/// locomotion it shows, whether it is drawn with a weapon, and for a gait its natural speed: how fast its planted feet
/// travel over the ground at that rate, east or west, in units per second, so playing it at speed over natural speed
/// keeps the feet from sliding. An attack's clip has a contact frame, the frame it lands on: the frames before it are
/// its windup and the frames after it its recovery, each stretched to its phase of the attack's timeline. The character
/// baker writes it; nothing changes it at runtime.
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
    [SerializeField] private int contactFrame = -1;

    public string Name => name;
    public CharacterGait Gait => gait;
    public bool Armed => armed;
    public int FrameCount => frameSeconds.Length;
    public float NaturalSpeed => naturalSpeed;
    public int ContactFrame => contactFrame;

    /// <summary>
    /// Whether the clip is an attack with a contact frame among its frames.
    /// </summary>
    public bool HasContact => contactFrame >= 0 && contactFrame < frameSeconds.Length;

    /// <summary>
    /// The natural length of the windup, the frames before the contact frame, in seconds; 0 without a contact frame.
    /// </summary>
    public float WindupSeconds => HasContact ? SumSeconds(0, contactFrame) : 0f;

    /// <summary>
    /// The natural length of the recovery, the frames after the contact frame, in seconds; 0 without a contact frame.
    /// </summary>
    public float RecoverySeconds => HasContact ? SumSeconds(contactFrame + 1, frameSeconds.Length - contactFrame - 1) : 0f;

    /// <summary>
    /// Whether the clip has frames and a sprite for every direction and frame.
    /// </summary>
    public bool IsComplete => frameSeconds.Length > 0 && sprites.Length == CharacterDirections.Count * frameSeconds.Length;

    /// <summary>
    /// The clip's length at its natural speed, in seconds.
    /// </summary>
    public float Duration => SumSeconds(0, frameSeconds.Length);

    /// <summary>
    /// Creates a clip from its sprites, direction by direction and within each direction frame by frame (east first,
    /// counterclockwise), the duration of each frame and, for an attack, its contact frame (-1 for none); for the baker
    /// and tests.
    /// </summary>
    public static CharacterAnimationClip Create(string clipName, CharacterGait clipGait, bool drawnArmed, Sprite[] clipSprites, float[] clipFrameSeconds, float clipNaturalSpeed, int clipContactFrame = -1)
    {
        return new CharacterAnimationClip
        {
            name = clipName,
            gait = clipGait,
            armed = drawnArmed,
            sprites = clipSprites ?? Array.Empty<Sprite>(),
            frameSeconds = clipFrameSeconds ?? Array.Empty<float>(),
            naturalSpeed = Mathf.Max(0f, clipNaturalSpeed),
            contactFrame = clipContactFrame < 0 ? -1 : clipContactFrame
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

    /// <summary>
    /// The natural length of <paramref name="count"/> frames from <paramref name="first"/>, in seconds.
    /// </summary>
    private float SumSeconds(int first, int count)
    {
        float total = 0f;
        for (int i = first; i < first + count; i++)
        {
            total += frameSeconds[i];
        }

        return total;
    }
}
