using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The idle animations of a <see cref="CharacterAnimator"/> (Docs/PlayerCharacter.md, Phase 3): once the character has
/// stood still for the settings' first wait, one of its set's idle animations plays once at its natural pace, from its
/// first frame; while it keeps standing, another plays after a gap picked between the settings' fewest and most seconds,
/// never the one before. <see cref="Stop"/> (moving, an action, a hold) cuts the one playing and starts the wait over.
/// The picks and gaps come from the random source, so two machines seeding it alike pick alike for the same stops. A set
/// without idle animations, or no source, plays none. Allocates nothing once built.
/// </summary>
public sealed class CharacterIdleAnimations
{
    private const int None = -1;

    private readonly CharacterAnimationClip[] clips;
    private readonly IRandom random;
    private readonly float firstWait;
    private readonly float gapMin;
    private readonly float gapMax;
    private bool standing;
    private float nextAt;
    private float startedAt;
    private int playing = None;
    private int last = None;

    /// <summary>
    /// Takes <paramref name="set"/>'s complete idle animations, timed by <paramref name="settings"/> and picked by
    /// <paramref name="idleRandom"/>.
    /// </summary>
    public CharacterIdleAnimations(CharacterAnimationSet set, CharacterLocomotionSettings settings, IRandom idleRandom)
    {
        List<CharacterAnimationClip> found = new();
        if (set != null)
        {
            foreach (CharacterAnimationClip clip in set.Clips)
            {
                if (clip != null && clip.IsIdleAnimation && clip.IsComplete && clip.Duration > 0f)
                {
                    found.Add(clip);
                }
            }
        }

        clips = found.ToArray();
        random = idleRandom;
        firstWait = settings != null ? settings.IdleAnimationAfter : 0f;
        gapMin = settings != null ? settings.IdleAnimationGapMin : 0f;
        gapMax = settings != null ? settings.IdleAnimationGapMax : 0f;
    }

    /// <summary>
    /// How many idle animations there are to play.
    /// </summary>
    public int Count => random != null ? clips.Length : 0;

    public bool IsPlaying => playing != None;
    public CharacterAnimationClip Clip => playing != None ? clips[playing] : null;
    public int Frame { get; private set; }

    /// <summary>
    /// Advances to <paramref name="now"/> for a character standing still, the first call since a stop starting the
    /// wait; returns whether an idle animation shows, at <see cref="Clip"/>'s <see cref="Frame"/>.
    /// </summary>
    public bool Advance(float now)
    {
        if (Count == 0)
        {
            return false;
        }

        if (!standing)
        {
            standing = true;
            nextAt = now + firstWait;
        }

        if (playing == None)
        {
            if (now < nextAt)
            {
                return false;
            }

            playing = Pick();
            startedAt = now;
        }

        CharacterAnimationClip clip = clips[playing];
        float elapsed = now - startedAt;
        if (elapsed >= clip.Duration)
        {
            last = playing;
            playing = None;
            nextAt = startedAt + clip.Duration + random.Range(gapMin, gapMax);
            return false;
        }

        Frame = FrameAt(clip, elapsed);
        return true;
    }

    /// <summary>
    /// Goes on with <paramref name="to"/>'s clips where it played <paramref name="from"/>'s, a set of the same clips
    /// drawn by other sprites (a composed outfit of the same look, Docs/PlayerCharacter.md, Phase 7), changing nothing
    /// else.
    /// </summary>
    public void Rebind(CharacterAnimationSet from, CharacterAnimationSet to)
    {
        for (int i = 0; i < clips.Length; i++)
        {
            clips[i] = from.Matching(clips[i], to) ?? clips[i];
        }
    }

    /// <summary>
    /// Cuts the idle animation playing, if any, and starts the wait over on the next <see cref="Advance"/>.
    /// </summary>
    public void Stop()
    {
        if (playing != None)
        {
            last = playing;
        }

        playing = None;
        standing = false;
    }

    /// <summary>
    /// The index of the next idle animation: any but the last one played.
    /// </summary>
    private int Pick()
    {
        int choices = last == None || clips.Length == 1 ? clips.Length : clips.Length - 1;
        int pick = Mathf.Min((int)random.Range(0f, choices), choices - 1);
        return last != None && clips.Length > 1 && pick >= last ? pick + 1 : pick;
    }

    /// <summary>
    /// The frame of <paramref name="clip"/> showing <paramref name="elapsed"/> seconds into it at its natural pace.
    /// </summary>
    private static int FrameAt(CharacterAnimationClip clip, float elapsed)
    {
        float end = 0f;
        for (int frame = 0; frame < clip.FrameCount; frame++)
        {
            end += clip.FrameSeconds(frame);
            if (elapsed < end)
            {
                return frame;
            }
        }

        return clip.FrameCount - 1;
    }
}
