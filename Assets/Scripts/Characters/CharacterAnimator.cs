using UnityEngine;

/// <summary>
/// A layered sprite animator for a character baked into a <see cref="CharacterAnimationSet"/> (Docs/Animation.md,
/// Runtime), driven by gameplay state rather than Mecanim. Each <see cref="Update"/> reads the injected clock and
/// returns the sprite to show. While an action plays (an attack on its timeline, <see cref="PlayAction"/>), the action
/// layer shows it in the attack's direction and the locomotion layer waits where it was; otherwise the facing snaps to
/// one of the eight directions with a hysteresis margin and the locomotion layer advances from the speed, resuming
/// where it was when an action ends. A hold (<see cref="Hold"/>, a hit-stop) keeps the sprite of its first update on
/// screen for a while: the action keeps its time underneath and the locomotion layer stands still, so once the hold
/// ends the action shows the frame for the real time, and the stride goes on where it stopped. Allocates nothing per
/// update, so EditMode tests drive it on a manual clock.
/// </summary>
public sealed class CharacterAnimator
{
    private readonly IClock clock;
    private readonly float directionMargin;
    private readonly CharacterLocomotionLayer locomotion;
    private readonly CharacterActionLayer action = new();
    private float lastTime;
    private bool started;
    private bool showedAction;
    private bool holdPending;
    private float holdUntil;

    /// <summary>
    /// Builds an animator of <paramref name="set"/>'s clips, picked and paced by <paramref name="settings"/>, on
    /// <paramref name="gameClock"/>.
    /// </summary>
    public CharacterAnimator(CharacterAnimationSet set, CharacterLocomotionSettings settings, IClock gameClock)
    {
        clock = gameClock;
        directionMargin = settings != null ? settings.DirectionMargin : 0f;
        locomotion = new CharacterLocomotionLayer(set, settings);
    }

    public int Direction { get; private set; } = CharacterDirections.South;
    public CharacterGait Gait => locomotion.Gait;
    public float Phase => locomotion.Phase;
    public float PlaybackRate => locomotion.PlaybackRate;
    public bool IsActing => action.IsPlaying;
    public AttackPhase ActionPhase => action.IsPlaying ? action.Phase : AttackPhase.None;
    public int Frame => action.IsPlaying ? action.Frame : locomotion.Frame;
    public CharacterAnimationClip Clip => action.IsPlaying ? action.Clip : locomotion.Clip;
    public Sprite Sprite { get; private set; }
    public bool IsHolding { get; private set; }

    /// <summary>
    /// Starts <paramref name="clip"/>, an attack's clip, now, toward <paramref name="direction"/> on
    /// <paramref name="timeline"/>, replacing any action playing; with <paramref name="holdUntilContact"/> its windup
    /// holds its last frame until <see cref="ReachContact"/>. Ignored for a clip without a contact frame.
    /// </summary>
    public void PlayAction(CharacterAnimationClip clip, Vector2 direction, in AttackTimeline timeline, bool holdUntilContact)
    {
        IsHolding = false;
        action.Play(clip, CharacterDirections.Nearest(direction), timeline, clock.Time, holdUntilContact);
    }

    /// <summary>
    /// Plays the contact now on an action played until contact.
    /// </summary>
    public void ReachContact()
    {
        action.ReachContact(clock.Time);
    }

    /// <summary>
    /// Ends the playing action at once, if any; locomotion resumes on the next update.
    /// </summary>
    public void StopAction()
    {
        IsHolding = false;
        action.Stop();
    }

    /// <summary>
    /// Holds the sprite the next update shows on screen for <paramref name="seconds"/> on the clock (a hit-stop),
    /// longer when a hold already runs that long. The action keeps its time underneath: a hold ends early when the
    /// action reaches its contact, so the contact frame still shows on the update the hit lands, and playing or stopping
    /// an action ends it. Visual only.
    /// </summary>
    public void Hold(float seconds)
    {
        if (seconds <= 0f)
        {
            return;
        }

        float until = clock.Time + seconds;
        if (IsHolding)
        {
            holdUntil = Mathf.Max(holdUntil, until);
            return;
        }

        IsHolding = true;
        holdPending = true;
        holdUntil = until;
    }

    /// <summary>
    /// Holds as <see cref="Hold"/> does, but only when the action shows its contact on the next update: an attacker
    /// whose hit just landed holds its contact frame. Returns whether it holds.
    /// </summary>
    public bool HoldContact(float seconds)
    {
        if (action.PhaseAt(clock.Time) != AttackPhase.Contact)
        {
            return false;
        }

        Hold(seconds);
        return true;
    }

    /// <summary>
    /// Advances by the clock's time since the last update for a character moving at <paramref name="velocity"/>
    /// (units per second) and facing <paramref name="facing"/>, and returns the sprite to show (null without clips).
    /// The first update snaps to the nearest direction and does not advance. While an action shows the locomotion layer
    /// stands still, and the first update after it advances it by nothing. While a hold runs, after its first update,
    /// the sprite and direction stay and the locomotion layer stands still.
    /// </summary>
    public Sprite Update(Vector2 velocity, Vector2 facing)
    {
        float now = clock.Time;
        float deltaTime = started ? Mathf.Max(0f, now - lastTime) : 0f;
        lastTime = now;
        bool resuming = showedAction;
        AttackPhase before = action.IsPlaying ? action.Phase : AttackPhase.None;
        showedAction = action.Advance(now);
        if (IsHolding)
        {
            bool reachedContact = !holdPending && showedAction && before == AttackPhase.Windup && action.Phase == AttackPhase.Contact;
            if (now >= holdUntil || reachedContact)
            {
                IsHolding = false;
            }
            else if (!holdPending && Sprite != null)
            {
                return Sprite;
            }

            holdPending = false;
        }

        if (showedAction)
        {
            started = true;
            Direction = action.Direction;
            Sprite = action.Sprite;
            return Sprite;
        }

        Direction = started || facing.sqrMagnitude < 1e-8f
            ? CharacterDirections.Keep(Direction, facing, directionMargin)
            : CharacterDirections.Nearest(facing);
        started = true;

        locomotion.Advance(velocity.magnitude, resuming ? 0f : deltaTime);
        Sprite = locomotion.SpriteFacing(Direction);
        return Sprite;
    }
}
