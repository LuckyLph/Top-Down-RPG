using UnityEngine;

/// <summary>
/// The action layer of a <see cref="CharacterAnimator"/>, above locomotion: an attack's clip playing its timeline
/// (Docs/Animation.md, Runtime). Each phase plays its frames stretched to that phase's duration, keeping their natural
/// proportions: the frames before the clip's contact frame over the windup, the contact frame for the contact, and the
/// frames after it over the recovery, so the contact frame shows exactly when the hit resolves at any attack speed. The
/// contact frame is never skipped: an update that jumps past the whole contact still shows it once. Played until contact (a copy of another machine's attack), the windup holds its last frame until
/// <see cref="ReachContact"/> says the hit landed, and the contact and recovery then play from that moment. Allocates
/// nothing.
/// </summary>
public sealed class CharacterActionLayer
{
    private CharacterAnimationClip clip;
    private AttackTimeline timeline;
    private float startTime;
    private bool holdUntilContact;
    private bool contacted;
    private bool contactShown;

    public bool IsPlaying { get; private set; }
    public CharacterAnimationClip Clip => clip;
    public int Direction { get; private set; }
    public int Frame { get; private set; }
    public AttackPhase Phase { get; private set; }

    /// <summary>
    /// The current frame's sprite in the action's direction, or null when nothing plays.
    /// </summary>
    public Sprite Sprite => IsPlaying ? clip.SpriteAt(Direction, Frame) : null;

    /// <summary>
    /// Starts <paramref name="actionClip"/> facing <paramref name="direction"/> at <paramref name="now"/> on
    /// <paramref name="actionTimeline"/>, replacing any action playing; with <paramref name="untilContact"/> the windup
    /// holds its last frame until <see cref="ReachContact"/>. False, and nothing changes, for a clip that is incomplete
    /// or has no contact frame.
    /// </summary>
    public bool Play(CharacterAnimationClip actionClip, int direction, in AttackTimeline actionTimeline, float now, bool untilContact)
    {
        if (actionClip == null || !actionClip.IsComplete || !actionClip.HasContact)
        {
            return false;
        }

        clip = actionClip;
        timeline = actionTimeline;
        Direction = direction;
        startTime = now;
        holdUntilContact = untilContact;
        contacted = false;
        contactShown = false;
        IsPlaying = true;
        Phase = AttackPhase.Windup;
        Frame = 0;
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
    /// Ends the action at once.
    /// </summary>
    public void Stop()
    {
        IsPlaying = false;
        Phase = AttackPhase.None;
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
        if (!contactShown && Phase != AttackPhase.Windup)
        {
            contactShown = true;
            Phase = AttackPhase.Contact;
            Frame = contactFrame;
            return true;
        }

        switch (Phase)
        {
            case AttackPhase.Windup:
                Frame = Stretched(0, contactFrame, elapsed / timeline.Windup, contactFrame);
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
