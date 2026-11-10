using UnityEngine;

/// <summary>
/// The locomotion layer of a <see cref="CharacterAnimator"/>: idle, walk, run or sprint, picked from the speed the
/// character moves at with hysteresis around the boundaries of <see cref="CharacterLocomotionSettings"/>. A gait plays
/// at speed over its natural speed, clamped to the settings' range, and idle at its own rate. Switching between gaits
/// keeps the normalised phase, so the stride goes on where it was (every gait starts with the left foot planted in
/// front); starting to move or stopping starts the new clip from its first frame. A gait the set lacks is shown with
/// the nearest slower one it has, or else the nearest faster. Allocates nothing once built.
/// </summary>
public sealed class CharacterLocomotionLayer
{
    private const int Levels = 4;

    private readonly CharacterLocomotionSettings settings;
    private readonly CharacterAnimationClip[] clips = new CharacterAnimationClip[Levels];
    private readonly float[][] frameEnds = new float[Levels][];
    private readonly float[] durations = new float[Levels];

    /// <summary>
    /// Takes the unarmed idle and gait clips of <paramref name="set"/> (complete ones only) and the settings that pick
    /// and pace them.
    /// </summary>
    public CharacterLocomotionLayer(CharacterAnimationSet set, CharacterLocomotionSettings locomotionSettings)
    {
        settings = locomotionSettings;
        for (int level = 0; level < Levels; level++)
        {
            CharacterAnimationClip clip = set != null ? set.Locomotion(GaitOf(level)) : null;
            if (clip == null || !clip.IsComplete)
            {
                continue;
            }

            clips[level] = clip;
            durations[level] = clip.Duration;
            float[] ends = new float[clip.FrameCount];
            float elapsed = 0f;
            for (int frame = 0; frame < ends.Length; frame++)
            {
                elapsed += clip.FrameSeconds(frame);
                ends[frame] = durations[level] > 0f ? elapsed / durations[level] : (frame + 1f) / ends.Length;
            }

            frameEnds[level] = ends;
        }

        FillMissingLevels();
    }

    public CharacterGait Gait { get; private set; } = CharacterGait.Idle;
    public float Phase { get; private set; }
    public float PlaybackRate { get; private set; } = 1f;
    public int Frame { get; private set; }

    /// <summary>
    /// The clip showing the current gait, or null when the set has no locomotion clip at all.
    /// </summary>
    public CharacterAnimationClip Clip => clips[LevelOf(Gait)];

    /// <summary>
    /// Moves on by <paramref name="deltaTime"/> seconds at <paramref name="speed"/> units per second: picks the gait,
    /// sets the playback rate and advances the phase and frame.
    /// </summary>
    public void Advance(float speed, float deltaTime)
    {
        CharacterGait next = settings != null ? Pick(Gait, speed, settings) : CharacterGait.Idle;
        if (next != Gait)
        {
            if (Gait == CharacterGait.Idle || next == CharacterGait.Idle)
            {
                Phase = 0f;
            }

            Gait = next;
        }

        int level = LevelOf(Gait);
        CharacterAnimationClip clip = clips[level];
        if (clip == null)
        {
            Frame = 0;
            return;
        }

        PlaybackRate = Gait == CharacterGait.Idle || clip.NaturalSpeed <= 0f
            ? 1f
            : Mathf.Clamp(speed / clip.NaturalSpeed, settings.MinRate, settings.MaxRate);
        if (durations[level] > 0f)
        {
            Phase = Mathf.Repeat(Phase + deltaTime * PlaybackRate / durations[level], 1f);
        }

        Frame = FrameAt(frameEnds[level], Phase);
    }

    /// <summary>
    /// Whether <paramref name="speed"/> picks a gait other than idle from the current one: whether the character moves.
    /// </summary>
    public bool WouldMove(float speed)
    {
        return settings != null && Pick(Gait, speed, settings) != CharacterGait.Idle;
    }

    /// <summary>
    /// The current frame's sprite facing <paramref name="direction"/>, or null without a clip.
    /// </summary>
    public Sprite SpriteFacing(int direction)
    {
        CharacterAnimationClip clip = Clip;
        return clip != null ? clip.SpriteAt(direction, Frame) : null;
    }

    /// <summary>
    /// The gait <paramref name="speed"/> picks coming from <paramref name="current"/>: each boundary below the current
    /// gait holds until the speed drops a hysteresis share under it, and each one above is crossed only a share over
    /// it. Standing still is always idle.
    /// </summary>
    public static CharacterGait Pick(CharacterGait current, float speed, CharacterLocomotionSettings settings)
    {
        int currentLevel = LevelOf(current);
        float hysteresis = settings.GaitHysteresis;
        int level = 0;
        for (int candidate = 1; candidate < Levels; candidate++)
        {
            float boundary = settings.From(GaitOf(candidate));
            float threshold = boundary * (currentLevel >= candidate ? 1f - hysteresis : 1f + hysteresis);
            if (speed <= 0f || speed < threshold)
            {
                break;
            }

            level = candidate;
        }

        return GaitOf(level);
    }

    /// <summary>
    /// The frame <paramref name="phase"/> falls in, given each frame's end as a share of the clip.
    /// </summary>
    private static int FrameAt(float[] ends, float phase)
    {
        for (int frame = 0; frame < ends.Length; frame++)
        {
            if (phase < ends[frame])
            {
                return frame;
            }
        }

        return ends.Length - 1;
    }

    /// <summary>
    /// Goes on with <paramref name="to"/>'s clips where it played <paramref name="from"/>'s, a set of the same clips
    /// drawn by other sprites (a composed outfit of the same look, Docs/PlayerCharacter.md, Phase 7), changing nothing
    /// else.
    /// </summary>
    public void Rebind(CharacterAnimationSet from, CharacterAnimationSet to)
    {
        for (int level = 0; level < Levels; level++)
        {
            clips[level] = from.Matching(clips[level], to) ?? clips[level];
        }
    }

    /// <summary>
    /// Gives every level without a clip of its own the nearest slower level's, or else the nearest faster one's.
    /// </summary>
    private void FillMissingLevels()
    {
        CharacterAnimationClip[] own = (CharacterAnimationClip[])clips.Clone();
        for (int level = 0; level < Levels; level++)
        {
            if (own[level] != null)
            {
                continue;
            }

            int source = -1;
            for (int slower = level - 1; slower >= 0 && source < 0; slower--)
            {
                source = own[slower] != null ? slower : -1;
            }

            for (int faster = level + 1; faster < Levels && source < 0; faster++)
            {
                source = own[faster] != null ? faster : -1;
            }

            if (source < 0)
            {
                continue;
            }

            clips[level] = own[source];
            durations[level] = durations[source];
            frameEnds[level] = frameEnds[source];
        }
    }

    /// <summary>
    /// The layer's level of <paramref name="gait"/>: idle 0 (also for none), walk 1, run 2, sprint 3.
    /// </summary>
    private static int LevelOf(CharacterGait gait)
    {
        switch (gait)
        {
            case CharacterGait.Walk:
                return 1;
            case CharacterGait.Run:
                return 2;
            case CharacterGait.Sprint:
                return 3;
            default:
                return 0;
        }
    }

    /// <summary>
    /// The gait of level <paramref name="level"/>.
    /// </summary>
    private static CharacterGait GaitOf(int level)
    {
        switch (level)
        {
            case 1:
                return CharacterGait.Walk;
            case 2:
                return CharacterGait.Run;
            case 3:
                return CharacterGait.Sprint;
            default:
                return CharacterGait.Idle;
        }
    }
}
