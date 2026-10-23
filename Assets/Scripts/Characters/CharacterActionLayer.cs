using UnityEngine;

/// <summary>
/// The action layer of a <see cref="CharacterAnimator"/>, above locomotion: an attack's or a cast's clip playing its
/// timeline (Docs/Animation.md, Runtime and Phase 5). Each phase plays its frames stretched to that phase's duration,
/// keeping their natural proportions: the frames before the clip's contact frame over the windup (a cast's gather, as
/// long as its cast time), the contact frame for the contact (a cast's release), and the frames after it over the
/// recovery (a cast's follow-through), so the contact frame shows exactly when the hit resolves or the cast completes,
/// at any speed or cast time. The contact frame is never skipped: an update that jumps past the whole contact still
/// shows it once. Played until contact (a copy of another machine's attack, or the owner's cast, which its orders
/// complete), the windup holds its last frame until <see cref="ReachContact"/> says the hit landed or the cast
/// completed, and the contact and recovery then play from that moment. A cast with
/// a channel beat whose cast time is longer than its clip's gather plays the gather at its natural speed, then shows its
/// last two gather frames in turn, a beat each, until the release. A cast may come with a clip drawn over a gait, which
/// the animator shows while the character moves. Allocates nothing.
/// </summary>
public sealed class CharacterActionLayer
{
    private CharacterAnimationClip clip;
    private CharacterAnimationClip movingClip;
    private AttackTimeline timeline;
    private float startTime;
    private bool holdUntilContact;
    private bool contacted;
    private float channelFrom;
    private float channelBeat;

    public bool IsPlaying { get; private set; }
    public CharacterActionKind Kind { get; private set; }
    public CharacterAnimationClip Clip => clip;

    /// <summary>
    /// The cast's clip drawn over a gait, shown while the character moves; null for none.
    /// </summary>
    public CharacterAnimationClip MovingClip => movingClip;

    /// <summary>
    /// Whether the action turns with the character's facing (a cast aimed nowhere) rather than keeping its direction.
    /// </summary>
    public bool FollowsFacing { get; private set; }

    /// <summary>
    /// Whether the contact (release) frame has shown since the action started.
    /// </summary>
    public bool ContactShown { get; private set; }

    public int Direction { get; private set; }
    public int Frame { get; private set; }
    public AttackPhase Phase { get; private set; }

    /// <summary>
    /// The current frame's sprite in the action's direction, or null when nothing plays.
    /// </summary>
    public Sprite Sprite => IsPlaying ? clip.SpriteAt(Direction, Frame) : null;

    /// <summary>
    /// Starts <paramref name="actionClip"/>, an attack's clip, facing <paramref name="direction"/> at
    /// <paramref name="now"/> on <paramref name="actionTimeline"/>, replacing any action playing; with
    /// <paramref name="untilContact"/> the windup holds its last frame until <see cref="ReachContact"/>. False, and
    /// nothing changes, for a clip that is incomplete or has no contact frame.
    /// </summary>
    public bool Play(CharacterAnimationClip actionClip, int direction, in AttackTimeline actionTimeline, float now, bool untilContact)
    {
        if (!Playable(actionClip))
        {
            return false;
        }

        Start(CharacterActionKind.Attack, actionClip, null, direction, false, actionTimeline, now, untilContact, 0f);
        return true;
    }

    /// <summary>
    /// Starts <paramref name="castClip"/>, a cast's clip, facing <paramref name="direction"/> at <paramref name="now"/>
    /// on <paramref name="castTimeline"/> (the gather as long as the cast time, the release, the follow-through),
    /// replacing any action playing. <paramref name="castMovingClip"/>, the same cast drawn over a gait, is kept when
    /// it is complete and has the clip's frames and release frame (null otherwise); <paramref name="followsFacing"/>
    /// turns the cast with the facing; a positive <paramref name="beat"/> makes a cast longer than the clip's gather a
    /// channel; with <paramref name="untilRelease"/> (the owner's cast, completed by its orders) the gather holds its
    /// last frame past the cast time until <see cref="ReachContact"/>, so the release shows on exactly the update the
    /// cast completes. False, and nothing changes, for a clip that is incomplete or has no release frame.
    /// </summary>
    public bool PlayCast(CharacterAnimationClip castClip, CharacterAnimationClip castMovingClip, int direction, bool followsFacing, in AttackTimeline castTimeline, float now, float beat, bool untilRelease)
    {
        if (!Playable(castClip))
        {
            return false;
        }

        bool movingFits = castMovingClip != null && castMovingClip.IsComplete && castMovingClip.GaitFrames > 0
            && castMovingClip.FrameCount == castClip.FrameCount && castMovingClip.ContactFrame == castClip.ContactFrame;
        Start(CharacterActionKind.Cast, castClip, movingFits ? castMovingClip : null, direction, followsFacing, castTimeline, now, untilRelease, beat);
        return true;
    }

    /// <summary>
    /// Plays the contact from <paramref name="now"/> on an action played until contact, cutting its windup short or
    /// ending its hold; does nothing otherwise, or once it has reached contact.
    /// </summary>
    public void ReachContact(float now)
    {
        if (!IsPlaying || !holdUntilContact || contacted)
        {
            return;
        }

        contacted = true;
        startTime = now - timeline.Windup;
    }

    /// <summary>
    /// Turns the action to <paramref name="direction"/>.
    /// </summary>
    public void Turn(int direction)
    {
        Direction = direction;
    }

    /// <summary>
    /// The phase the next <see cref="Advance"/> to <paramref name="now"/> shows: none when nothing plays, the windup
    /// while a copy's windup holds for its contact, and the contact on the first update past the windup.
    /// </summary>
    public AttackPhase PhaseAt(float now)
    {
        if (!IsPlaying)
        {
            return AttackPhase.None;
        }

        float elapsed = now - startTime;
        if (holdUntilContact && !contacted && elapsed >= timeline.Windup)
        {
            return AttackPhase.Windup;
        }

        AttackPhase phase = timeline.PhaseAt(elapsed);
        return !ContactShown && phase != AttackPhase.Windup ? AttackPhase.Contact : phase;
    }

    /// <summary>
    /// Ends the action at once.
    /// </summary>
    public void Stop()
    {
        IsPlaying = false;
        Phase = AttackPhase.None;
    }

    /// <summary>
    /// The current frame's sprite in <paramref name="direction"/> over gait frame <paramref name="gaitFrame"/> of the
    /// moving clip, or null when nothing plays or there is no moving clip.
    /// </summary>
    public Sprite MovingSpriteAt(int direction, int frame, int gaitFrame)
    {
        return IsPlaying && movingClip != null ? movingClip.SpriteAt(direction, frame, Mathf.Clamp(gaitFrame, 0, movingClip.GaitFrames - 1)) : null;
    }

    /// <summary>
    /// Moves the action on to <paramref name="now"/>: its phase and the frame that phase shows, the contact frame on the
    /// first update past the windup whatever the time. Returns whether it is still playing; it ends once its recovery is
    /// over.
    /// </summary>
    public bool Advance(float now)
    {
        if (!IsPlaying)
        {
            return false;
        }

        float elapsed = now - startTime;
        int contactFrame = clip.ContactFrame;
        if (holdUntilContact && !contacted && elapsed >= timeline.Windup)
        {
            Phase = AttackPhase.Windup;
            Frame = Mathf.Max(0, contactFrame - 1);
            return true;
        }

        Phase = timeline.PhaseAt(elapsed);
        if (!ContactShown && Phase != AttackPhase.Windup)
        {
            ContactShown = true;
            Phase = AttackPhase.Contact;
            Frame = contactFrame;
            return true;
        }

        switch (Phase)
        {
            case AttackPhase.Windup:
                Frame = channelFrom >= 0f ? ChannelFrame(elapsed, contactFrame) : Stretched(0, contactFrame, elapsed / timeline.Windup, contactFrame);
                return true;
            case AttackPhase.Contact:
                Frame = contactFrame;
                return true;
            case AttackPhase.Recovery:
                float share = timeline.Recovery > 0f ? (elapsed - timeline.Windup - timeline.Contact) / timeline.Recovery : 1f;
                Frame = Stretched(contactFrame + 1, clip.FrameCount - contactFrame - 1, share, contactFrame);
                return true;
            default:
                Stop();
                return false;
        }
    }

    /// <summary>
    /// Whether <paramref name="actionClip"/> can play: complete, with a contact frame.
    /// </summary>
    private static bool Playable(CharacterAnimationClip actionClip)
    {
        return actionClip != null && actionClip.IsComplete && actionClip.HasContact;
    }

    /// <summary>
    /// Starts an action of <paramref name="kind"/>; a positive <paramref name="beat"/> makes a windup longer than the
    /// clip's natural one, with at least two frames, a channel.
    /// </summary>
    private void Start(CharacterActionKind kind, CharacterAnimationClip actionClip, CharacterAnimationClip actionMovingClip, int direction, bool followsFacing, in AttackTimeline actionTimeline, float now, bool untilContact, float beat)
    {
        Kind = kind;
        clip = actionClip;
        movingClip = actionMovingClip;
        timeline = actionTimeline;
        Direction = direction;
        FollowsFacing = followsFacing;
        startTime = now;
        holdUntilContact = untilContact;
        contacted = false;
        ContactShown = false;
        channelBeat = beat;
        float natural = actionClip.WindupSeconds;
        channelFrom = beat > 0f && actionClip.ContactFrame >= 2 && natural > 0f && actionTimeline.Windup > natural ? natural : -1f;
        IsPlaying = true;
        Phase = AttackPhase.Windup;
        Frame = 0;
    }

    /// <summary>
    /// A channel's windup frame <paramref name="elapsed"/> seconds in: the gather at its natural speed, then the last
    /// two gather frames before <paramref name="contactFrame"/> in turn, a beat each.
    /// </summary>
    private int ChannelFrame(float elapsed, int contactFrame)
    {
        if (elapsed < channelFrom)
        {
            return Stretched(0, contactFrame, elapsed / channelFrom, contactFrame);
        }

        return (int)((elapsed - channelFrom) / channelBeat) % 2 == 0 ? contactFrame - 2 : contactFrame - 1;
    }

    /// <summary>
    /// The frame among <paramref name="count"/> frames from <paramref name="first"/> that <paramref name="share"/> of
    /// their stretched length falls in, keeping their natural proportions; <paramref name="none"/> when there are none.
    /// </summary>
    private int Stretched(int first, int count, float share, int none)
    {
        if (count <= 0)
        {
            return none;
        }

        float total = 0f;
        for (int frame = first; frame < first + count; frame++)
        {
            total += clip.FrameSeconds(frame);
        }

        float reached = 0f;
        for (int frame = first; frame < first + count; frame++)
        {
            reached += clip.FrameSeconds(frame);
            if (total > 0f ? share * total < reached : share * count < frame - first + 1)
            {
                return frame;
            }
        }

        return first + count - 1;
    }
}
