using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for the attack timeline (Docs/Animation.md, Attack timeline) on a manual clock: <see cref="AttackTimeline"/>'s
/// phases and their durations under attack speed, and <see cref="AttackState"/>: the hit resolving once at contact,
/// the cooldown from the start, each cancel rule on and off (a move cancelling the windup with the cooldown given back,
/// an order ending the recovery at once or as it begins), the next attack chaining into a recovery that may be cut
/// short, cancels before and after contact, and no allocation.
/// </summary>
public class AttackStateTests
{
    private const float Cooldown = 0.75f;

    private ManualClock clock;
    private AttackState state;
    private readonly AttackTimeline timeline = new(0.25f, 0.125f, 0.25f);

    /// <summary>
    /// Starts each test with an idle attack state on a manual clock at zero. Times are binary fractions so phase
    /// boundaries fall exactly on the clock's steps.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        clock = new ManualClock();
        state = new AttackState(clock);
    }

    /// <summary>
    /// A timeline's phases follow each other for their durations, then the attack is over; attack speed divides every
    /// phase, and a speed that is not positive leaves the timeline as it is.
    /// </summary>
    [Test]
    public void Timeline_PhasesLastTheirDurations_DividedByAttackSpeed()
    {
        Assert.That(timeline.Duration, Is.EqualTo(0.625f));
        Assert.That(timeline.PhaseAt(0f), Is.EqualTo(AttackPhase.Windup));
        Assert.That(timeline.PhaseAt(0.24f), Is.EqualTo(AttackPhase.Windup));
        Assert.That(timeline.PhaseAt(0.25f), Is.EqualTo(AttackPhase.Contact));
        Assert.That(timeline.PhaseAt(0.37f), Is.EqualTo(AttackPhase.Contact));
        Assert.That(timeline.PhaseAt(0.375f), Is.EqualTo(AttackPhase.Recovery));
        Assert.That(timeline.PhaseAt(0.62f), Is.EqualTo(AttackPhase.Recovery));
        Assert.That(timeline.PhaseAt(0.625f), Is.EqualTo(AttackPhase.None));

        AttackTimeline fast = timeline.At(2f);
        Assert.That(fast.Windup, Is.EqualTo(0.125f));
        Assert.That(fast.Contact, Is.EqualTo(0.0625f));
        Assert.That(fast.Recovery, Is.EqualTo(0.125f));
        Assert.That(fast.PhaseAt(0.125f), Is.EqualTo(AttackPhase.Contact));

        AttackTimeline slow = timeline.At(0.5f);
        Assert.That(slow.Duration, Is.EqualTo(1.25f));
        Assert.That(slow.PhaseAt(0.49f), Is.EqualTo(AttackPhase.Windup));
        Assert.That(slow.PhaseAt(0.5f), Is.EqualTo(AttackPhase.Contact));

        Assert.That(timeline.At(0f).Duration, Is.EqualTo(timeline.Duration));
        Assert.That(new AttackTimeline(-1f, -1f, -1f).Duration, Is.Zero, "Phases are never negative.");
    }

    /// <summary>
    /// The attack passes contact exactly once, on the first advance at or past the windup, and ends after the
    /// recovery; a single advance past the whole attack reports both, contact first.
    /// </summary>
    [Test]
    public void TheHit_ResolvesOnceAtContact()
    {
        Start();
        Assert.That(state.Phase, Is.EqualTo(AttackPhase.Windup));
        Assert.That(state.Advance(), Is.EqualTo(AttackStep.None));
        clock.Advance(0.125f);
        Assert.That(state.Advance(), Is.EqualTo(AttackStep.None));
        clock.Advance(0.125f);
        Assert.That(state.Advance(), Is.EqualTo(AttackStep.Contact), "Contact at the windup's end.");
        Assert.That(state.Phase, Is.EqualTo(AttackPhase.Contact));
        Assert.That(state.Advance(), Is.EqualTo(AttackStep.None), "Only once.");
        clock.Advance(0.125f);
        Assert.That(state.Advance(), Is.EqualTo(AttackStep.None));
        Assert.That(state.Phase, Is.EqualTo(AttackPhase.Recovery));
        clock.Advance(0.25f);
        Assert.That(state.Advance(), Is.EqualTo(AttackStep.Ended));
        Assert.That(state.IsBusy, Is.False);

        clock.Advance(1f);
        Start();
        clock.Advance(1f);
        Assert.That(state.Advance(), Is.EqualTo(AttackStep.Contact | AttackStep.Ended));
        Assert.That(state.Advance(), Is.EqualTo(AttackStep.None));
    }

    /// <summary>
    /// The cooldown runs from the attack's start: no attack starts while one runs or before the cooldown is over, even
    /// once the attack has ended.
    /// </summary>
    [Test]
    public void TheCooldown_RunsFromTheStart()
    {
        Assert.That(Start(), Is.True);
        Assert.That(state.StartTime, Is.Zero);
        Assert.That(state.ReadyTime, Is.EqualTo(Cooldown));
        Assert.That(Start(), Is.False, "An attack is running.");
        clock.Advance(0.625f);
        state.Advance();
        Assert.That(state.IsBusy, Is.False);
        Assert.That(state.CanStart, Is.False, "The cooldown is not over.");
        Assert.That(Start(), Is.False);
        clock.Advance(0.125f);
        Assert.That(Start(), Is.True);
        Assert.That(state.StartTime, Is.EqualTo(0.75f));
    }

    /// <summary>
    /// With windupCancelledByMove on, a move during the windup aborts the attack: it never reaches contact and its
    /// cooldown is given back, so the next attack can start at once; other orders let the windup go on.
    /// </summary>
    [Test]
    public void AMoveDuringTheWindup_CancelsIt_AndGivesTheCooldownBack()
    {
        Start();
        clock.Advance(0.125f);
        state.Advance();
        Assert.That(state.Order(false), Is.False, "An attack on another unit or an attack move does not cancel it.");
        Assert.That(state.Phase, Is.EqualTo(AttackPhase.Windup));
        Assert.That(state.Order(true), Is.True);
        Assert.That(state.IsBusy, Is.False);
        clock.Advance(0.25f);
        Assert.That(state.Advance(), Is.EqualTo(AttackStep.None), "Nothing lands.");
        Assert.That(state.CanStart, Is.True, "The cooldown was given back.");
        Assert.That(Start(), Is.True);
    }

    /// <summary>
    /// With windupCancelledByMove off, a move during the windup does not stop it: the hit lands at contact, and the
    /// move ends the recovery as soon as it begins when the recovery may be cut short, or waits for it otherwise.
    /// </summary>
    [Test]
    public void AMoveDuringTheWindup_WithTheRuleOff_LetsTheHitLand()
    {
        Start(cancelWindupByMove: false);
        clock.Advance(0.125f);
        Assert.That(state.Order(true), Is.False);
        clock.Advance(0.125f);
        Assert.That(state.Advance(), Is.EqualTo(AttackStep.Contact));
        clock.Advance(0.125f);
        Assert.That(state.Advance(), Is.EqualTo(AttackStep.Ended | AttackStep.CutShort), "The earlier order ends the recovery as it begins.");

        clock.Advance(1f);
        Start(cancelWindupByMove: false, cancellableRecovery: false);
        clock.Advance(0.125f);
        Assert.That(state.Order(true), Is.False);
        clock.Advance(0.125f);
        Assert.That(state.Advance(), Is.EqualTo(AttackStep.Contact));
        clock.Advance(0.125f);
        Assert.That(state.Advance(), Is.EqualTo(AttackStep.None));
        Assert.That(state.Phase, Is.EqualTo(AttackPhase.Recovery), "A recovery that cannot be cut short plays out.");
        clock.Advance(0.25f);
        Assert.That(state.Advance(), Is.EqualTo(AttackStep.Ended));
    }

    /// <summary>
    /// With recoveryCancellable on, any order during the recovery ends the attack at once; with it off, no order does
    /// and the recovery plays out. An order during the contact ends a recovery that may be cut short as it begins.
    /// </summary>
    [Test]
    public void AnOrderDuringTheRecovery_EndsIt_OnlyWhenTheRuleSaysSo()
    {
        Start();
        clock.Advance(0.5f);
        state.Advance();
        Assert.That(state.Phase, Is.EqualTo(AttackPhase.Recovery));
        Assert.That(state.Order(false), Is.True);
        Assert.That(state.IsBusy, Is.False);

        clock.Advance(1f);
        Start(cancellableRecovery: false);
        clock.Advance(0.5f);
        state.Advance();
        Assert.That(state.Order(true), Is.False);
        Assert.That(state.Order(false), Is.False);
        Assert.That(state.Phase, Is.EqualTo(AttackPhase.Recovery));
        clock.Advance(0.125f);
        Assert.That(state.Advance(), Is.EqualTo(AttackStep.Ended));

        clock.Advance(1f);
        Start();
        clock.Advance(0.25f);
        Assert.That(state.Advance(), Is.EqualTo(AttackStep.Contact));
        Assert.That(state.Order(false), Is.False, "The contact plays out.");
        Assert.That(state.Phase, Is.EqualTo(AttackPhase.Contact));
        clock.Advance(0.125f);
        Assert.That(state.Advance(), Is.EqualTo(AttackStep.Ended | AttackStep.CutShort));
    }

    /// <summary>
    /// A recovery that runs past the cooldown lets the next attack start in it when orders may cut it short, and holds
    /// it back until the recovery is over otherwise.
    /// </summary>
    [Test]
    public void TheNextAttack_ChainsIntoARecoveryThatMayBeCutShort()
    {
        AttackTimeline longRecovery = new(0.125f, 0.0625f, 0.5f);
        Assert.That(state.TryStart(0, Vector2.right, longRecovery, 0.375f, true, true), Is.True);
        clock.Advance(0.375f);
        state.Advance();
        Assert.That(state.Phase, Is.EqualTo(AttackPhase.Recovery));
        Assert.That(state.CanStart, Is.True);
        Assert.That(state.TryStart(0, Vector2.up, longRecovery, 0.375f, true, false), Is.True, "The next attack chains in.");
        Assert.That(state.Phase, Is.EqualTo(AttackPhase.Windup));
        Assert.That(state.Direction, Is.EqualTo(Vector2.up));

        clock.Advance(0.375f);
        state.Advance();
        Assert.That(state.Phase, Is.EqualTo(AttackPhase.Recovery));
        Assert.That(state.CanStart, Is.False, "This recovery cannot be cut short.");
        clock.Advance(0.3125f);
        Assert.That(state.Advance(), Is.EqualTo(AttackStep.Ended));
        Assert.That(state.CanStart, Is.True);
    }

    /// <summary>
    /// Cancelling (a stun, death, a cast) ends the attack in any phase; before contact nothing lands and the cooldown
    /// is given back, after it the cooldown stays spent. Cancelling with no attack running does nothing.
    /// </summary>
    [Test]
    public void Cancel_EndsTheAttack_GivingTheCooldownBackOnlyBeforeContact()
    {
        Assert.That(state.Cancel(), Is.EqualTo(AttackPhase.None));

        Start();
        clock.Advance(0.125f);
        state.Advance();
        Assert.That(state.Cancel(), Is.EqualTo(AttackPhase.Windup));
        clock.Advance(0.25f);
        Assert.That(state.Advance(), Is.EqualTo(AttackStep.None));
        Assert.That(state.CanStart, Is.True);

        Start();
        clock.Advance(0.5f);
        state.Advance();
        Assert.That(state.Cancel(), Is.EqualTo(AttackPhase.Recovery));
        Assert.That(state.IsBusy, Is.False);
        Assert.That(state.CanStart, Is.False, "The hit landed, so the cooldown stays spent.");
    }

    /// <summary>
    /// Starting, advancing, ordering and cancelling attacks allocates no managed memory.
    /// </summary>
    [Test]
    public void Attacks_DoNotAllocate()
    {
        TestDelegate run = () =>
        {
            for (int i = 0; i < 40; i++)
            {
                Start(cancellableRecovery: i % 3 != 0);
                for (int step = 0; step < 6; step++)
                {
                    clock.Advance(0.125f);
                    state.Advance();
                    if (step == i % 6)
                    {
                        state.Order(i % 2 == 0);
                    }
                }

                state.Cancel();
                clock.Advance(Cooldown);
            }
        };
        run();

        Assert.That(run, Is.Not.AllocatingGCMemory());
    }

    /// <summary>
    /// Starts an attack toward the east on the test timeline with the test cooldown and the given cancel rules.
    /// </summary>
    private bool Start(bool cancelWindupByMove = true, bool cancellableRecovery = true)
    {
        return state.TryStart(0, Vector2.right, timeline, Cooldown, cancelWindupByMove, cancellableRecovery);
    }
}
