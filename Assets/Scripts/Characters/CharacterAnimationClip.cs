using System;
using UnityEngine;

/// <summary>
/// One baked clip of a <see cref="CharacterAnimationSet"/>: its sprites, a row of frames per direction
/// (<see cref="CharacterDirections"/>, east first), how long each frame shows at the clip's natural speed, the
/// locomotion it shows, whether it is drawn with a weapon, and for a gait its natural speed: how fast its planted feet
/// travel over the ground at that rate, east or west, in units per second, so playing it at speed over natural speed
/// keeps the feet from sliding. An attack's clip has a contact frame, the frame it lands on: the frames before it are
/// its windup and the frames after it its recovery, each stretched to its phase of the attack's timeline; a cast's clip
/// has its release frame there, the frames before it its gather and those after it its follow-through. A cast baked
/// over a gait (played while a Continue cast moves the character) has every one of its frames drawn over each of the
/// gait's frames, so the legs keep their stride while the upper body casts. A cast's clip with a light drawn on it has
/// the light each sprite casts on what is around it, and the light's colour (Docs/Lighting.md, Phase 2). A clip of a
/// character carrying a lantern has, for each sprite, the lantern's layer drawn over it (null where the body hides the
/// whole lantern) and the light the lantern casts from where it hangs (Docs/Lighting.md, Phase 9). The character
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
    [SerializeField, Min(0)] private int gaitFrames;
    [SerializeField] private Color lightColour;
    [SerializeField] private CharacterFrameLight[] lights = Array.Empty<CharacterFrameLight>();
    [SerializeField] private Sprite[] lanternSprites = Array.Empty<Sprite>();
    [SerializeField] private CharacterFrameLight[] lanternLights = Array.Empty<CharacterFrameLight>();

    public string Name => name;
    public CharacterGait Gait => gait;
    public bool Armed => armed;
    public int FrameCount => frameSeconds.Length;
    public float NaturalSpeed => naturalSpeed;
    public int ContactFrame => contactFrame;

    /// <summary>
    /// The colour of the light the clip's frames cast; clear for a clip without light.
    /// </summary>
    public Color LightColour => lightColour;

    /// <summary>
    /// Whether the clip has a light for every sprite.
    /// </summary>
    public bool HasLights => lights.Length > 0 && lights.Length == sprites.Length;

    /// <summary>
    /// Whether the clip has a lantern's layer and light for every sprite.
    /// </summary>
    public bool HasLantern => lanternSprites.Length > 0 && lanternSprites.Length == sprites.Length && lanternLights.Length == sprites.Length;

    /// <summary>
    /// How many frames of a gait each frame is drawn over: 0 for a clip drawn standing.
    /// </summary>
    public int GaitFrames => gaitFrames;

    /// <summary>
    /// Whether the clip is an attack or a cast with a contact (release) frame among its frames.
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
    /// Whether the clip has frames and a sprite for every direction and frame (and gait frame, for one drawn over a
    /// gait).
    /// </summary>
    public bool IsComplete => frameSeconds.Length > 0 && sprites.Length == CharacterDirections.Count * frameSeconds.Length * Mathf.Max(1, gaitFrames);

    /// <summary>
    /// The clip's length at its natural speed, in seconds.
    /// </summary>
    public float Duration => SumSeconds(0, frameSeconds.Length);

    /// <summary>
    /// Creates a clip from its sprites, direction by direction and within each direction frame by frame (east first,
    /// counterclockwise; for a clip drawn over <paramref name="clipGaitFrames"/> gait frames, gait frame by gait frame
    /// and within each the clip's frames), the duration of each frame and, for an attack or a cast, its contact
    /// (release) frame (-1 for none), for a clip with a light, its colour and the light of each sprite in the
    /// sprites' order, and for a clip carrying a lantern, each sprite's lantern layer and lantern light in the same
    /// order; for the baker and tests.
    /// </summary>
    public static CharacterAnimationClip Create(string clipName, CharacterGait clipGait, bool drawnArmed, Sprite[] clipSprites, float[] clipFrameSeconds, float clipNaturalSpeed, int clipContactFrame = -1, int clipGaitFrames = 0, Color clipLightColour = default, CharacterFrameLight[] clipLights = null,
        Sprite[] clipLanternSprites = null, CharacterFrameLight[] clipLanternLights = null)
    {
        return new CharacterAnimationClip
        {
            name = clipName,
            gait = clipGait,
            armed = drawnArmed,
            sprites = clipSprites ?? Array.Empty<Sprite>(),
            frameSeconds = clipFrameSeconds ?? Array.Empty<float>(),
            naturalSpeed = Mathf.Max(0f, clipNaturalSpeed),
            contactFrame = clipContactFrame < 0 ? -1 : clipContactFrame,
            gaitFrames = Mathf.Max(0, clipGaitFrames),
            lightColour = clipLightColour,
            lights = clipLights ?? Array.Empty<CharacterFrameLight>(),
            lanternSprites = clipLanternSprites ?? Array.Empty<Sprite>(),
            lanternLights = clipLanternLights ?? Array.Empty<CharacterFrameLight>()
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
    /// The sprite of frame <paramref name="frame"/> facing direction <paramref name="direction"/> (over the first gait
    /// frame, for a clip drawn over a gait).
    /// </summary>
    public Sprite SpriteAt(int direction, int frame)
    {
        return SpriteAt(direction, frame, 0);
    }

    /// <summary>
    /// The sprite of frame <paramref name="frame"/> facing direction <paramref name="direction"/> over gait frame
    /// <paramref name="gaitFrame"/>; a clip drawn standing has only gait frame 0.
    /// </summary>
    public Sprite SpriteAt(int direction, int frame, int gaitFrame)
    {
        return sprites[(direction * Mathf.Max(1, gaitFrames) + gaitFrame) * frameSeconds.Length + frame];
    }

    /// <summary>
    /// The light frame <paramref name="frame"/> casts facing direction <paramref name="direction"/> over gait frame
    /// <paramref name="gaitFrame"/>, as <see cref="SpriteAt(int, int, int)"/> finds its sprite; none for a clip without
    /// lights.
    /// </summary>
    public CharacterFrameLight LightAt(int direction, int frame, int gaitFrame)
    {
        int index = (direction * Mathf.Max(1, gaitFrames) + gaitFrame) * frameSeconds.Length + frame;
        return HasLights && index >= 0 && index < lights.Length ? lights[index] : default;
    }

    /// <summary>
    /// The lantern's layer over frame <paramref name="frame"/> facing direction <paramref name="direction"/> over gait
    /// frame <paramref name="gaitFrame"/>, as <see cref="SpriteAt(int, int, int)"/> finds its sprite; null where the
    /// body hides the whole lantern, and for a clip without one.
    /// </summary>
    public Sprite LanternAt(int direction, int frame, int gaitFrame)
    {
        int index = (direction * Mathf.Max(1, gaitFrames) + gaitFrame) * frameSeconds.Length + frame;
        return HasLantern && index >= 0 && index < lanternSprites.Length ? lanternSprites[index] : null;
    }

    /// <summary>
    /// The light the lantern casts on frame <paramref name="frame"/> facing direction <paramref name="direction"/> over
    /// gait frame <paramref name="gaitFrame"/>, as <see cref="SpriteAt(int, int, int)"/> finds its sprite, whether or
    /// not the body hides the lantern; none for a clip without one.
    /// </summary>
    public CharacterFrameLight LanternLightAt(int direction, int frame, int gaitFrame)
    {
        int index = (direction * Mathf.Max(1, gaitFrames) + gaitFrame) * frameSeconds.Length + frame;
        return HasLantern && index >= 0 && index < lanternLights.Length ? lanternLights[index] : default;
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
