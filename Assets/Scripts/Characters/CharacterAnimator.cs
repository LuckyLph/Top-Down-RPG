using UnityEngine;

/// <summary>
/// A layered sprite animator for a character baked into a <see cref="CharacterAnimationSet"/> (Docs/Animation.md,
/// Runtime), driven by gameplay state rather than Mecanim. Each <see cref="Update"/> reads the injected clock and
/// returns the sprite to show. While an action plays (an attack or a cast on its timeline, <see cref="PlayAction"/> and
/// <see cref="PlayCast"/>), the action layer shows it in its direction and the locomotion layer waits where it was;
/// otherwise the facing snaps to one of the eight directions with a hysteresis margin and the locomotion layer advances
/// from the speed, resuming where it was when an action ends. A cast with a clip drawn over a gait shows that clip while
/// the character moves, the locomotion layer running on underneath so the legs keep their gait and phase; a cast
/// without one stops as soon as the character moves after its release frame showed (the follow-through is cut). A hold
/// (<see cref="Hold"/>, a hit-stop) keeps the sprite of its first update on screen for a while: the action keeps its
/// time underneath and the locomotion layer stands still (a moving cast keeps its held frame over legs that run on), so
/// once the hold ends the action shows the frame for the real time, and the stride goes on where it stopped. An action
/// played until its contact waits for it at most the settings' contact wait past its windup, then ends. A cast started
/// while another cast's release is due or showing (a buffered cast, which fires as the running one completes) waits
/// until that release has shown for its own time (Docs/KnownGaps.md, Phase 7), then plays with its gather shortened by
/// the wait, so its own release still shows on the update it completes; one completing before it starts (an instant
/// cast) shows its release as it starts. Each sprite comes with what its clip says about it: a cast's light and, for a
/// character carrying a lantern, the lantern's layer and light, held with it. Standing still with nothing else to show,
/// the character plays its set's idle animations (<see cref="CharacterIdleAnimations"/>, Docs/PlayerCharacter.md,
/// Phase 3); moving, an action or a hold cuts one and starts the wait over. Allocates nothing per update, so EditMode
/// tests drive it on a manual clock.
/// </summary>
public sealed class CharacterAnimator
{
    private readonly IClock clock;
    private readonly float directionMargin;
    private readonly float contactWait;
    private readonly CharacterLocomotionLayer locomotion;
    private readonly CharacterActionLayer action = new();
    private readonly CharacterIdleAnimations idle;
    private float lastTime;
    private bool started;
    private bool showedAction;
    private bool actionMovedLegs;
    private bool holdPending;
    private float holdUntil;
    private int shownActionFrame;
    private bool showedIdle;
    private QueuedCast queued;

    /// <summary>
    /// Builds an animator of <paramref name="set"/>'s clips, picked and paced by <paramref name="settings"/>, on
    /// <paramref name="gameClock"/>, its idle animations picked by <paramref name="idleRandom"/> (none without one).
    /// </summary>
    public CharacterAnimator(CharacterAnimationSet set, CharacterLocomotionSettings settings, IClock gameClock, IRandom idleRandom = null)
    {
        clock = gameClock;
        directionMargin = settings != null ? settings.DirectionMargin : 0f;
        contactWait = settings != null ? settings.ContactWaitSeconds : float.PositiveInfinity;
        locomotion = new CharacterLocomotionLayer(set, settings);
        idle = new CharacterIdleAnimations(set, settings, idleRandom);
    }

    public int Direction { get; private set; } = CharacterDirections.South;
    public CharacterGait Gait => locomotion.Gait;
    public float Phase => locomotion.Phase;
    public float PlaybackRate => locomotion.PlaybackRate;
    public bool IsActing => action.IsPlaying;
    public CharacterActionKind ActionKind => action.Kind;
    public AttackPhase ActionPhase => action.IsPlaying ? action.Phase : AttackPhase.None;
    public int Frame => action.IsPlaying ? action.Frame : showedIdle ? idle.Frame : locomotion.Frame;
    public Sprite Sprite { get; private set; }
    public bool IsHolding { get; private set; }

    /// <summary>
    /// Whether a cast waits for the release showing to end before it starts.
    /// </summary>
    public bool HasQueuedCast => queued.IsSet;

    /// <summary>
    /// The light the sprite the last update showed casts (Docs/Lighting.md, Phase 2): a cast's glow, held with its
    /// sprite; none for a sprite without one.
    /// </summary>
    public CharacterFrameLight Light { get; private set; }

    /// <summary>
    /// The colour of <see cref="Light"/>: its clip's light colour.
    /// </summary>
    public Color LightColour { get; private set; }

    /// <summary>
    /// The lantern's layer over the sprite the last update showed (Docs/Lighting.md, Phase 9), held with its sprite;
    /// null where the body hides the whole lantern or the clip carries none.
    /// </summary>
    public Sprite Lantern { get; private set; }

    /// <summary>
    /// The light the lantern casts on the sprite the last update showed, held with its sprite; none for a clip carrying
    /// no lantern.
    /// </summary>
    public CharacterFrameLight LanternLight { get; private set; }

    /// <summary>
    /// Whether the last update showed a cast's clip drawn over a gait, the legs running on.
    /// </summary>
    public bool ShowsMovingCast => action.IsPlaying && actionMovedLegs;

    /// <summary>
    /// Whether the last update showed an idle animation.
    /// </summary>
    public bool ShowsIdleAnimation => showedIdle;

    /// <summary>
    /// The clip the last update showed: the action's (its moving clip while it shows that), else an idle animation's,
    /// else the locomotion's.
    /// </summary>
    public CharacterAnimationClip Clip => action.IsPlaying ? (actionMovedLegs ? action.MovingClip : action.Clip) : showedIdle ? idle.Clip : locomotion.Clip;

    /// <summary>
    /// Starts <paramref name="clip"/>, an attack's clip, now, toward <paramref name="direction"/> on
    /// <paramref name="timeline"/>, replacing any action playing; with <paramref name="holdUntilContact"/> its windup
    /// holds its last frame until <see cref="ReachContact"/>, at most the settings' contact wait past the windup, after
    /// which the action ends. Ignored for a clip without a contact frame.
    /// </summary>
    public void PlayAction(CharacterAnimationClip clip, Vector2 direction, in AttackTimeline timeline, bool holdUntilContact)
    {
        queued = default;
        IsHolding = false;
        idle.Stop();
        action.Play(clip, CharacterDirections.Nearest(direction), timeline, clock.Time, holdUntilContact, contactWait);
    }

    /// <summary>
    /// Starts <paramref name="clip"/>, a cast's clip, now on <paramref name="timeline"/> (its gather as long as the cast
    /// time, its release from the moment the cast completes, its follow-through), replacing any action playing:
    /// toward <paramref name="direction"/>, or the current direction when it is zero; turning with the facing when
    /// <paramref name="followsFacing"/> (a cast aimed nowhere). <paramref name="movingClip"/>, the cast drawn over a
    /// gait, shows while the character moves; a positive <paramref name="channelBeat"/> makes a cast longer than its
    /// clip's gather a channel; with <paramref name="untilRelease"/> the gather waits for <see cref="ReachContact"/>
    /// (the cast completing, on the owner or by its message on a copy) before the release, at most the settings' contact
    /// wait past the cast time, after which the cast ends. Ignored for a clip without a release frame. Started while a
    /// cast's release is due or showing, it waits until that release has shown for its own time, a hold running on, and
    /// then starts with its gather shortened by the wait (Docs/KnownGaps.md, Phase 7); a newer cast replaces one waiting.
    /// </summary>
    public void PlayCast(CharacterAnimationClip clip, CharacterAnimationClip movingClip, Vector2 direction, bool followsFacing, in AttackTimeline timeline, float channelBeat, bool untilRelease)
    {
        int facing = direction.sqrMagnitude > 1e-8f ? CharacterDirections.Nearest(direction) : Direction;
        float now = clock.Time;
        if (action.IsPlaying && action.Kind == CharacterActionKind.Cast && action.PhaseAt(now) == AttackPhase.Contact)
        {
            queued = new QueuedCast(clip, movingClip, facing, followsFacing, timeline, channelBeat, untilRelease, now, action.ContactEndsAt);
            return;
        }

        queued = default;
        IsHolding = false;
        idle.Stop();
        action.PlayCast(clip, movingClip, facing, followsFacing, timeline, now, channelBeat, untilRelease, contactWait);
    }

    /// <summary>
    /// Plays the contact (a cast's release) now on an action played until contact; for a cast waiting for a release to
    /// end, its release as it starts.
    /// </summary>
    public void ReachContact()
    {
        if (queued.IsSet)
        {
            queued.Released = true;
            return;
        }

        action.ReachContact(clock.Time);
    }

    /// <summary>
    /// Ends the playing action at once when it is of <paramref name="kind"/>; locomotion resumes on the next update.
    /// </summary>
    public void StopAction(CharacterActionKind kind)
    {
        if (kind == CharacterActionKind.Cast)
        {
            queued = default;
        }

        if (!action.IsPlaying || action.Kind != kind)
        {
            return;
        }

        IsHolding = false;
        action.Stop();
    }

    /// <summary>
    /// Goes on with <paramref name="to"/>, a set of the same clips as <paramref name="from"/>, the set played until now,
    /// drawn by other sprites (a composed outfit of the same look, Docs/PlayerCharacter.md, Phase 7): the action, the
    /// gait and its phase, an idle animation, a cast waiting and a hold all carry on, the sprite held taking the new
    /// set's. Returns false, changing nothing, for a set of other clips.
    /// </summary>
    public bool Rebind(CharacterAnimationSet from, CharacterAnimationSet to)
    {
        if (from == null || to == null || !from.SameClipsAs(to))
        {
            return false;
        }

        locomotion.Rebind(from, to);
        action.Rebind(from, to);
        idle.Rebind(from, to);
        if (queued.IsSet)
        {
            queued = queued.Rebound(from, to);
        }

        Sprite matched = from.SpriteMatching(Sprite, to);
        if (matched != null)
        {
            Sprite = matched;
        }

        return true;
    }

    /// <summary>
    /// Holds the sprite the next update shows on screen for <paramref name="seconds"/> on the clock (a hit-stop),
    /// longer when a hold already runs that long. The action keeps its time underneath: a hold ends early when the
    /// action reaches its contact, so the contact frame still shows on the update the hit lands, and playing or stopping
    /// an action ends it. An idle animation stops, so the hold shows the character standing. Visual only.
    /// </summary>
    public void Hold(float seconds)
    {
        Hold(seconds, false);
    }

    /// <summary>
    /// Holds as <see cref="Hold"/> does, but only when the action shows its contact (a cast's release) on the next
    /// update: an attacker or caster whose hit just landed holds that frame, even when a hold was already running (it
    /// was struck in its windup), which would otherwise end as the action reaches contact. Returns whether it holds.
    /// </summary>
    public bool HoldContact(float seconds)
    {
        if (action.PhaseAt(clock.Time) != AttackPhase.Contact)
        {
            return false;
        }

        Hold(seconds, true);
        return true;
    }

    /// <summary>
    /// Advances by the clock's time since the last update for a character moving at <paramref name="velocity"/>
    /// (units per second) and facing <paramref name="facing"/>, and returns the sprite to show (null without clips).
    /// The first update snaps to the nearest direction and does not advance. While an action shows the locomotion layer
    /// stands still, unless a cast shows its clip drawn over a gait, and the first update after a standing action
    /// advances it by nothing. A cast without such a clip ends once the character moves after its release frame showed,
    /// ending any hold. While a hold runs, after its first update, the sprite and direction stay and the locomotion
    /// layer stands still, except under a moving cast, whose held frame stays over legs that run on. A cast waiting for a
    /// release starts once that release has shown for its time, or as soon as moving would cut it or it ends otherwise.
    /// </summary>
    public Sprite Update(Vector2 velocity, Vector2 facing)
    {
        float now = clock.Time;
        float deltaTime = started ? Mathf.Max(0f, now - lastTime) : 0f;
        lastTime = now;
        bool resuming = showedAction && !actionMovedLegs;
        if (queued.IsSet && (!action.IsPlaying || action.Kind != CharacterActionKind.Cast || (action.ContactShown && now >= queued.After)))
        {
            StartQueued(now);
        }

        AttackPhase before = action.IsPlaying ? action.Phase : AttackPhase.None;
        bool releaseShown = action.IsPlaying && action.ContactShown;
        showedAction = action.Advance(now);
        showedIdle = false;
        float speed = velocity.magnitude;
        bool moving = locomotion.WouldMove(speed);
        if (showedAction && moving && releaseShown && action.Kind == CharacterActionKind.Cast && action.MovingClip == null)
        {
            if (queued.IsSet)
            {
                StartQueued(now);
                before = AttackPhase.Windup;
                showedAction = action.Advance(now);
            }
            else
            {
                action.Stop();
                showedAction = false;
                IsHolding = false;
            }
        }

        if (IsHolding)
        {
            bool reachedContact = !holdPending && showedAction && before == AttackPhase.Windup && action.Phase == AttackPhase.Contact;
            if (now >= holdUntil || reachedContact)
            {
                IsHolding = false;
            }
            else if (!holdPending && Sprite != null)
            {
                if (showedAction && actionMovedLegs && moving)
                {
                    locomotion.Advance(speed, deltaTime);
                    int heldGaitFrame = GaitFrameOf(action.MovingClip);
                    Sprite = action.MovingSpriteAt(Direction, shownActionFrame, heldGaitFrame);
                    ShowLight(action.MovingLightAt(Direction, shownActionFrame, heldGaitFrame), action.MovingClip);
                    ShowLantern(action.MovingClip, shownActionFrame, heldGaitFrame);
                }

                return Sprite;
            }

            holdPending = false;
        }

        if (showedAction)
        {
            started = true;
            idle.Stop();
            if (action.FollowsFacing && facing.sqrMagnitude >= 1e-8f)
            {
                action.Turn(CharacterDirections.Keep(action.Direction, facing, directionMargin));
            }

            Direction = action.Direction;
            shownActionFrame = action.Frame;
            actionMovedLegs = action.MovingClip != null && moving;
            if (actionMovedLegs)
            {
                locomotion.Advance(speed, deltaTime);
                int gaitFrame = GaitFrameOf(action.MovingClip);
                Sprite = action.MovingSpriteAt(Direction, action.Frame, gaitFrame);
                ShowLight(action.MovingLightAt(Direction, action.Frame, gaitFrame), action.MovingClip);
                ShowLantern(action.MovingClip, action.Frame, gaitFrame);
                return Sprite;
            }

            Sprite = action.Sprite;
            ShowLight(action.Light, action.Clip);
            ShowLantern(action.Clip, action.Frame, 0);
            return Sprite;
        }

        Direction = started || facing.sqrMagnitude < 1e-8f
            ? CharacterDirections.Keep(Direction, facing, directionMargin)
            : CharacterDirections.Nearest(facing);
        started = true;

        locomotion.Advance(speed, resuming ? 0f : deltaTime);
        actionMovedLegs = false;
        if (locomotion.Gait != CharacterGait.Idle)
        {
            idle.Stop();
        }
        else if (idle.Advance(now))
        {
            showedIdle = true;
            Sprite = idle.Clip.SpriteAt(Direction, idle.Frame);
            ShowLight(default, null);
            ShowLantern(idle.Clip, idle.Frame, 0);
            return Sprite;
        }

        Sprite = locomotion.SpriteFacing(Direction);
        ShowLight(default, null);
        ShowLantern(locomotion.Clip, locomotion.Frame, 0);
        return Sprite;
    }

    /// <summary>
    /// Starts the cast waiting for a release at <paramref name="now"/>, its gather shortened by how long it waited, and
    /// plays its release at once when it completed meanwhile.
    /// </summary>
    private void StartQueued(float now)
    {
        QueuedCast cast = queued;
        queued = default;
        AttackTimeline timeline = new(Mathf.Max(0f, cast.Timeline.Windup - Mathf.Max(0f, now - cast.Since)), cast.Timeline.Contact, cast.Timeline.Recovery);
        if (action.PlayCast(cast.Clip, cast.MovingClip, cast.Direction, cast.FollowsFacing, timeline, now, cast.Beat, cast.UntilRelease, contactWait) && cast.Released)
        {
            action.ReachContact(now);
        }
    }

    /// <summary>
    /// Starts a hold of <paramref name="seconds"/>, or lengthens the one running; with <paramref name="retake"/> a
    /// running hold also takes the sprite of the next update again and holds it from there.
    /// </summary>
    private void Hold(float seconds, bool retake)
    {
        if (seconds <= 0f)
        {
            return;
        }

        idle.Stop();
        float until = clock.Time + seconds;
        if (IsHolding)
        {
            holdUntil = Mathf.Max(holdUntil, until);
            holdPending |= retake;
            return;
        }

        IsHolding = true;
        holdPending = true;
        holdUntil = until;
    }

    /// <summary>
    /// Keeps <paramref name="light"/>, the light of the sprite shown, in <paramref name="clip"/>'s light colour.
    /// </summary>
    private void ShowLight(CharacterFrameLight light, CharacterAnimationClip clip)
    {
        Light = light;
        LightColour = light.IsLit && clip != null ? clip.LightColour : default;
    }

    /// <summary>
    /// Keeps the lantern's layer and light of <paramref name="clip"/>'s frame <paramref name="frame"/> over gait frame
    /// <paramref name="gaitFrame"/> in the direction shown; none without a clip.
    /// </summary>
    private void ShowLantern(CharacterAnimationClip clip, int frame, int gaitFrame)
    {
        Lantern = clip != null ? clip.LanternAt(Direction, frame, gaitFrame) : null;
        LanternLight = clip != null ? clip.LanternLightAt(Direction, frame, gaitFrame) : default;
    }

    /// <summary>
    /// The frame of <paramref name="clip"/>'s gait at the locomotion layer's phase: gait clips start with the left foot
    /// planted in front and keep an even pace, so the phase picks the matching stride.
    /// </summary>
    private int GaitFrameOf(CharacterAnimationClip clip)
    {
        int frames = clip.GaitFrames;
        return Mathf.Clamp((int)(locomotion.Phase * frames), 0, frames - 1);
    }

    /// <summary>
    /// A cast waiting for the release showing to end: what <see cref="PlayCast"/> was asked to play, when
    /// (<see cref="Since"/>), when that release ends (<see cref="After"/>) and whether the cast completed meanwhile.
    /// </summary>
    private struct QueuedCast
    {
        /// <summary>
        /// A cast asked for at <paramref name="since"/>, to start once the release showing ends at
        /// <paramref name="after"/>.
        /// </summary>
        public QueuedCast(CharacterAnimationClip clip, CharacterAnimationClip movingClip, int direction, bool followsFacing, in AttackTimeline timeline, float beat, bool untilRelease, float since, float after)
        {
            Clip = clip;
            MovingClip = movingClip;
            Direction = direction;
            FollowsFacing = followsFacing;
            Timeline = timeline;
            Beat = beat;
            UntilRelease = untilRelease;
            Since = since;
            After = after;
            Released = false;
        }

        public bool IsSet => Clip != null;

        /// <summary>
        /// This cast with <paramref name="to"/>'s clips where it names <paramref name="from"/>'s, a set of the same
        /// clips.
        /// </summary>
        public QueuedCast Rebound(CharacterAnimationSet from, CharacterAnimationSet to)
        {
            return new QueuedCast(from.Matching(Clip, to) ?? Clip, from.Matching(MovingClip, to) ?? MovingClip, Direction, FollowsFacing, Timeline, Beat, UntilRelease, Since, After) { Released = Released };
        }

        public CharacterAnimationClip Clip { get; }
        public CharacterAnimationClip MovingClip { get; }
        public int Direction { get; }
        public bool FollowsFacing { get; }
        public AttackTimeline Timeline { get; }
        public float Beat { get; }
        public bool UntilRelease { get; }
        public float Since { get; }
        public float After { get; }
        public bool Released { get; set; }
    }
}
