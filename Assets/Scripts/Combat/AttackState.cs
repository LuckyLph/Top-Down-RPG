using System;
using UnityEngine;

/// <summary>
/// What one call of <see cref="AttackState.Advance"/> passed: contact (the hit resolves now) and the attack's end,
/// cut short when an order given earlier ended its recovery as it began.
/// </summary>
[Flags]
public enum AttackStep
{
    None = 0,
    Contact = 1,
    Ended = 2,
    CutShort = 4
}

/// <summary>
/// The attack a weapon is playing (Docs/Animation.md, Attack timeline), on an injected clock. A swing starts one when
/// the cooldown allows and no attack runs, or the running one is in a recovery that orders may cut short (the next
/// attack chains into it); it then runs through its windup, passes contact once, when its hit resolves, and ends
/// after its recovery. The cooldown runs from the start. With its weapon's cancel rules, a move order during the
/// windup aborts it, and any order ends the recovery at once, or as soon as it begins when the order came earlier in
/// the attack. An attack that ends before contact, cancelled by a move, a stun, death or a cast, gives its cooldown
/// back. Allocates nothing.
/// </summary>
public sealed class AttackState
{
    private readonly IClock clock;
    private float readyTime = float.NegativeInfinity;
    private float readyTimeBefore;
    private bool contacted;
    private bool ordered;
    private bool windupCancelledByMove;
    private bool recoveryCancellable;

    /// <summary>
    /// Creates an idle attack state timed by <paramref name="gameClock"/>.
    /// </summary>
    public AttackState(IClock gameClock)
    {
        clock = gameClock;
    }

    public AttackPhase Phase { get; private set; }
    public bool IsBusy => Phase != AttackPhase.None;
    public int Attack { get; private set; }
    public Vector2 Direction { get; private set; }
    public AttackTimeline Timeline { get; private set; }
    public float StartTime { get; private set; }
    public float ReadyTime => readyTime;
    public bool IsReady => clock.Time >= readyTime;

    /// <summary>
    /// Whether a swing would start an attack now: the cooldown is over and no attack runs, or the running one is in a
    /// recovery orders may cut short.
    /// </summary>
    public bool CanStart => IsReady && (Phase == AttackPhase.None || (Phase == AttackPhase.Recovery && recoveryCancellable));

    /// <summary>
    /// Starts attack <paramref name="attack"/> of the weapon toward <paramref name="direction"/> on
    /// <paramref name="timeline"/>, with its cooldown of <paramref name="cooldown"/> seconds from now and its cancel
    /// rules, when <see cref="CanStart"/>; a running recovery it chains into ends. False when it cannot start.
    /// </summary>
    public bool TryStart(int attack, Vector2 direction, in AttackTimeline timeline, float cooldown, bool cancelWindupByMove, bool cancellableRecovery)
    {
        if (!CanStart)
        {
            return false;
        }

        float now = clock.Time;
        readyTimeBefore = readyTime;
        readyTime = now + Mathf.Max(0f, cooldown);
        StartTime = now;
        Attack = attack;
        Direction = direction;
        Timeline = timeline;
        windupCancelledByMove = cancelWindupByMove;
        recoveryCancellable = cancellableRecovery;
        contacted = false;
        ordered = false;
        Phase = AttackPhase.Windup;
        return true;
    }

    /// <summary>
    /// Moves the running attack on to the clock's time: reports contact on the call that reaches it (before any end in
    /// the same call), and the end once its recovery is over, or as it begins after an earlier order when the recovery
    /// may be cut short.
    /// </summary>
    public AttackStep Advance()
    {
        if (Phase == AttackPhase.None)
        {
            return AttackStep.None;
        }

        float elapsed = clock.Time - StartTime;
        AttackStep step = AttackStep.None;
        if (!contacted && elapsed >= Timeline.Windup)
        {
            contacted = true;
            step |= AttackStep.Contact;
        }

        Phase = Timeline.PhaseAt(elapsed);
        if (Phase == AttackPhase.Recovery && ordered && recoveryCancellable)
        {
            Phase = AttackPhase.None;
            return step | AttackStep.Ended | AttackStep.CutShort;
        }

        return Phase == AttackPhase.None ? step | AttackStep.Ended : step;
    }

    /// <summary>
    /// Takes an order the player gave during the attack, <paramref name="move"/> for a move or Stop: in the windup a
    /// move aborts the attack when its rules say so (its cooldown given back), in a recovery orders may cut short the
    /// order ends it, and otherwise it is remembered so that recovery ends as soon as it begins. Returns whether it
    /// ended the attack.
    /// </summary>
    public bool Order(bool move)
    {
        switch (Phase)
        {
            case AttackPhase.None:
                return false;
            case AttackPhase.Windup when move && windupCancelledByMove:
                Cancel();
                return true;
            case AttackPhase.Recovery when recoveryCancellable:
                Phase = AttackPhase.None;
                return true;
            default:
                ordered = true;
                return false;
        }
    }

    /// <summary>
    /// Ends the running attack at once (a stun, death, a cast, a teleport), giving its cooldown back when its hit has
    /// not landed. Returns the phase it was in, none when no attack ran.
    /// </summary>
    public AttackPhase Cancel()
    {
        AttackPhase was = Phase;
        if (was == AttackPhase.None)
        {
            return was;
        }

        if (!contacted)
        {
            readyTime = readyTimeBefore;
        }

        Phase = AttackPhase.None;
        return was;
    }
}
