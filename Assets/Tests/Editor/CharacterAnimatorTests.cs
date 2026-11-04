using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for <see cref="CharacterAnimator"/>'s locomotion and action layers and directions and for
/// <see cref="CharacterVisuals"/>, on a set of test sprites and a manual clock: the facing snaps to the nearest of eight
/// directions and keeps it until a margin past the boundary; the gait follows the speed bands with hysteresis around
/// each boundary; switching gaits keeps the phase while starting and stopping restart it; the playback rate is speed
/// over natural speed, clamped; a missing gait shows the nearest slower one; an attack's phases play their frames stretched to the timeline at any attack speed, its contact frame
/// showing exactly when the attack state reaches contact; a copy's windup holds until contact, at most its contact wait
/// past the windup, and a contact reached shows on its update at any time on the clock; locomotion resumes
/// where it was after an action; a cast's gather stretches to its cast time with its release on the update it completes
/// (instantly for an instant cast), a long cast channels, a cancel stops it, a Continue cast while running keeps the
/// legs' gait and phase under its upper body, moving cuts a standing cast's follow-through but never its release, and a
/// hold ends at the release; updates allocate nothing once warm; the visuals put the sprite on their renderer, the
/// weapon plays its attack on them while the character stands and runs unarmed, and a copy's attack plays on through
/// a switch to its weapon that arrives after it.
/// </summary>
public class CharacterAnimatorTests
{
    private const float WalkNatural = 1f;
    private const float RunNatural = 4f;
    private const float SprintNatural = 5.6f;
    private const int Contact = 4;
    private const int CastRelease = 4;

    private readonly List<Object> created = new();
    private ManualClock clock;
    private CharacterLocomotionSettings settings;
    private CharacterAnimationSet set;

    /// <summary>
    /// Builds settings (walk from 0.1, run from 2.4, sprint from 5.6 units per second, 10% hysteresis, rates 0.5 to
    /// 1.5, an 8 degree direction margin) and a set of idle (4 frames of 0.25 s), walk (8 of 0.125 s), run (8 of
    /// 0.1 s) and sprint (8 of 0.075 s) clips, each sprite its own.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        clock = new ManualClock();
        settings = Track(CharacterLocomotionSettings.Create(0.1f, 2.4f, 5.6f, 0.1f, 0.5f, 1.5f, 8f));
        set = Track(CharacterAnimationSet.Create(new[]
        {
            Clip("Idle", CharacterGait.Idle, 4, 0.25f, 0f),
            Clip("Walk", CharacterGait.Walk, 8, 0.125f, WalkNatural),
            Clip("Run", CharacterGait.Run, 8, 0.1f, RunNatural),
            Clip("Sprint", CharacterGait.Sprint, 8, 0.075f, SprintNatural),
            AttackClip("Slash"),
            CastClip("Cast", 0),
            CastClip("CastRun", 8),
            FlourishClip("Flourish")
        }));
    }

    /// <summary>
    /// Destroys every sprite, texture, set and object the test made.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Object instance in created)
        {
            if (instance != null)
            {
                Object.DestroyImmediate(instance);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// A facing shows as the nearest of the eight directions, counterclockwise from east, from the first update.
    /// </summary>
    [Test]
    public void Direction_SnapsToTheNearestOfEight()
    {
        for (int direction = 0; direction < CharacterDirections.Count; direction++)
        {
            foreach (float offset in new[] { -20f, 0f, 20f })
            {
                Vector2 facing = Facing(CharacterDirections.Angle(direction) + offset);
                Assert.That(CharacterDirections.Nearest(facing), Is.EqualTo(direction), $"{direction} {offset}");
                CharacterAnimator animator = new(set, settings, clock);
                animator.Update(Vector2.zero, facing);
                Assert.That(animator.Direction, Is.EqualTo(direction), $"{direction} {offset}");
            }
        }
    }

    /// <summary>
    /// The direction holds until the facing is more than the margin past the boundary with the next one, either way,
    /// and a zero facing keeps it.
    /// </summary>
    [Test]
    public void Direction_KeepsUntilTheFacingTurnsAMarginPastTheBoundary()
    {
        CharacterAnimator animator = new(set, settings, clock);
        animator.Update(Vector2.zero, Facing(0f));
        Assert.That(animator.Direction, Is.EqualTo(CharacterDirections.East));

        animator.Update(Vector2.zero, Facing(29f));
        Assert.That(animator.Direction, Is.EqualTo(CharacterDirections.East), "6.5 degrees past the boundary is within the margin.");
        animator.Update(Vector2.zero, Facing(31f));
        Assert.That(animator.Direction, Is.EqualTo(1), "8.5 degrees past it turns to north-east.");

        animator.Update(Vector2.zero, Facing(16f));
        Assert.That(animator.Direction, Is.EqualTo(1), "Turning back holds north-east within the margin.");
        animator.Update(Vector2.zero, Facing(14f));
        Assert.That(animator.Direction, Is.EqualTo(CharacterDirections.East));

        animator.Update(Vector2.zero, Vector2.zero);
        Assert.That(animator.Direction, Is.EqualTo(CharacterDirections.East), "No facing keeps the direction.");
    }

    /// <summary>
    /// Standing is idle, and each band's speed picks its gait: walk, run and sprint.
    /// </summary>
    [Test]
    public void Gait_FollowsTheSpeedBands()
    {
        Assert.That(GaitAt(0f), Is.EqualTo(CharacterGait.Idle));
        Assert.That(GaitAt(0.05f), Is.EqualTo(CharacterGait.Idle));
        Assert.That(GaitAt(1f), Is.EqualTo(CharacterGait.Walk));
        Assert.That(GaitAt(3f), Is.EqualTo(CharacterGait.Run));
        Assert.That(GaitAt(5f), Is.EqualTo(CharacterGait.Run));
        Assert.That(GaitAt(6.5f), Is.EqualTo(CharacterGait.Sprint));
    }

    /// <summary>
    /// A speed just past a boundary keeps the gait it came from: a run slowing to just under the run boundary still
    /// runs, and walks only a hysteresis share below it; a walk speeding up just past it still walks, and runs only a
    /// share above it. The same holds between idle and walking, and between running and sprinting.
    /// </summary>
    [Test]
    public void Gait_HoldsNearABoundary_UntilTheHysteresisIsCrossed()
    {
        CharacterAnimator animator = new(set, settings, clock);
        Step(animator, 3f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Run));
        Step(animator, 2.3f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Run), "Under the boundary, within the hysteresis.");
        Step(animator, 2.1f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Walk));
        Step(animator, 2.5f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Walk), "Over the boundary, within the hysteresis.");
        Step(animator, 2.7f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Run));

        Step(animator, 6f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Run));
        Step(animator, 6.2f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Sprint));
        Step(animator, 5.2f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Sprint));
        Step(animator, 5f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Run));

        Step(animator, 0.095f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Walk));
        Step(animator, 0f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Idle), "Standing still is always idle.");
        Step(animator, 0.105f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Idle));
        Step(animator, 0.12f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Walk));
    }

    /// <summary>
    /// Starting to move begins the gait at its first frame (the left foot planted in front); a switch between gaits
    /// keeps the normalised phase, so the run goes on from where the walk was and shows its own frame there; stopping
    /// begins the idle at its first frame.
    /// </summary>
    [Test]
    public void Phase_IsKeptAcrossGaitSwitches_AndRestartsOnStartingAndStopping()
    {
        CharacterAnimator animator = new(set, settings, clock);
        Vector2 east = Vector2.right;
        animator.Update(Vector2.zero, east);
        clock.Advance(0.3f);
        animator.Update(Vector2.zero, east);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Idle));
        Assert.That(animator.Phase, Is.EqualTo(0.3f).Within(1e-4f));

        clock.Advance(0.1f);
        animator.Update(east * WalkNatural, east);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Walk));
        Assert.That(animator.Phase, Is.EqualTo(0.1f).Within(1e-4f), "Starting to walk begins at the first frame.");
        clock.Advance(0.2f);
        animator.Update(east * WalkNatural, east);
        Assert.That(animator.Phase, Is.EqualTo(0.3f).Within(1e-4f));

        animator.Update(east * RunNatural, east);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Run));
        Assert.That(animator.Phase, Is.EqualTo(0.3f).Within(1e-4f), "The switch keeps the phase.");
        Assert.That(animator.Frame, Is.EqualTo(2));
        Assert.That(animator.Sprite, Is.SameAs(set.Locomotion(CharacterGait.Run).SpriteAt(CharacterDirections.East, 2)));

        clock.Advance(0.08f);
        animator.Update(east * RunNatural, east);
        Assert.That(animator.Phase, Is.EqualTo(0.4f).Within(1e-4f), "The run's 0.8 s cycle at its natural rate.");
        Assert.That(animator.Frame, Is.EqualTo(3));

        animator.Update(Vector2.zero, east);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Idle));
        Assert.That(animator.Phase, Is.Zero, "Stopping begins the idle at its first frame.");
        Assert.That(animator.Sprite, Is.SameAs(set.Locomotion(CharacterGait.Idle).SpriteAt(CharacterDirections.East, 0)));
    }

    /// <summary>
    /// A gait plays at speed over its natural speed, clamped to the settings' range; idle plays at its own rate.
    /// </summary>
    [Test]
    public void PlaybackRate_IsSpeedOverNaturalSpeed_Clamped()
    {
        Assert.That(RateAt(5f), Is.EqualTo(5f / RunNatural).Within(1e-5f));
        Assert.That(RateAt(3f), Is.EqualTo(3f / RunNatural).Within(1e-5f));
        Assert.That(RateAt(1.2f), Is.EqualTo(1.2f).Within(1e-5f));
        Assert.That(RateAt(2f), Is.EqualTo(1.5f).Within(1e-5f), "A fast walk is clamped to the fastest rate.");
        Assert.That(RateAt(0.2f), Is.EqualTo(0.5f).Within(1e-5f), "A slow walk is clamped to the slowest rate.");
        Assert.That(RateAt(10f), Is.EqualTo(1.5f).Within(1e-5f));
        Assert.That(RateAt(0f), Is.EqualTo(1f));

        CharacterAnimator animator = new(set, settings, clock);
        Step(animator, 2f);
        clock.Advance(0.1f);
        animator.Update(Vector2.right * 2f, Vector2.right);
        Assert.That(animator.Phase, Is.EqualTo(0.15f).Within(1e-4f), "The phase advances at the clamped rate.");
    }

    /// <summary>
    /// A gait the set has no clip for is shown with the nearest slower gait's clip, at that clip's natural speed.
    /// </summary>
    [Test]
    public void AMissingGait_ShowsTheNearestSlowerOne()
    {
        CharacterAnimationSet withoutSprint = Track(CharacterAnimationSet.Create(new[]
        {
            set.Locomotion(CharacterGait.Idle),
            set.Locomotion(CharacterGait.Walk),
            set.Locomotion(CharacterGait.Run)
        }));
        CharacterAnimator animator = new(withoutSprint, settings, clock);
        Step(animator, 7f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Sprint));
        Assert.That(animator.Clip, Is.SameAs(set.Locomotion(CharacterGait.Run)));
        Assert.That(animator.PlaybackRate, Is.EqualTo(1.5f).Within(1e-5f));
    }

    /// <summary>
    /// The set's attack clip records its contact frame and the natural lengths of its windup and recovery; a gait has
    /// no contact frame.
    /// </summary>
    [Test]
    public void AnAttackClip_SplitsIntoWindupContactAndRecovery()
    {
        CharacterAnimationClip slash = set.Find("Slash");
        Assert.That(slash.HasContact, Is.True);
        Assert.That(slash.ContactFrame, Is.EqualTo(Contact));
        Assert.That(slash.WindupSeconds, Is.EqualTo(0.5f));
        Assert.That(slash.RecoverySeconds, Is.EqualTo(0.25f));
        Assert.That(set.Locomotion(CharacterGait.Run).HasContact, Is.False);
        Assert.That(set.Locomotion(CharacterGait.Run).ContactFrame, Is.EqualTo(-1));
        Assert.That(set.Find("Missing"), Is.Null);
        Assert.That(set.Locomotion(CharacterGait.Run), Is.SameAs(set.Find("Run")), "Locomotion only picks unarmed clips.");
    }

    /// <summary>
    /// At attack speeds 0.5, 1 and 2, an attack plays its windup frames stretched to the windup in their natural
    /// proportions, the contact frame for exactly the contact, and the recovery frames stretched to the recovery, in the
    /// attack's direction; once the recovery is over the locomotion shows again.
    /// </summary>
    [Test]
    public void AnAttack_PlaysEachPhaseStretchedToTheTimeline_AtAnyAttackSpeed()
    {
        AttackTimeline timeline = new(0.25f, 0.125f, 0.25f);
        CharacterAnimationClip slash = set.Find("Slash");
        float[] times = { 0f, 0.125f, 0.1875f, 0.21875f, 0.24f, 0.25f, 0.37f, 0.375f, 0.5f, 0.5625f, 0.62f };
        int[] frames = { 0, 1, 2, 3, 3, 4, 4, 5, 6, 7, 7 };
        foreach (float speed in new[] { 0.5f, 1f, 2f })
        {
            AttackTimeline played = timeline.At(speed);
            clock.Time = 10f;
            CharacterAnimator animator = new(set, settings, clock);
            animator.Update(Vector2.zero, Vector2.right);
            animator.PlayAction(slash, Vector2.right, played, false);
            for (int i = 0; i < times.Length; i++)
            {
                clock.Time = 10f + times[i] / speed;
                animator.Update(Vector2.zero, Vector2.right);
                Assert.That(animator.Frame, Is.EqualTo(frames[i]), $"x{speed} at {times[i] / speed} s");
                Assert.That(animator.Sprite, Is.SameAs(slash.SpriteAt(CharacterDirections.East, frames[i])));
            }

            Assert.That(animator.ActionPhase, Is.EqualTo(AttackPhase.Recovery));
            clock.Time = 10f + 0.625f / speed;
            animator.Update(Vector2.zero, Vector2.right);
            Assert.That(animator.IsActing, Is.False, $"x{speed}: the attack is over.");
            Assert.That(animator.Sprite, Is.SameAs(set.Locomotion(CharacterGait.Idle).SpriteAt(CharacterDirections.East, 0)));
        }
    }

    /// <summary>
    /// Driven on one clock in 60 Hz steps from attacks starting at uneven times, at several attack speeds, the
    /// animator shows the contact frame on exactly the update in which the attack state reaches contact, and never
    /// before.
    /// </summary>
    [Test]
    public void TheContactFrame_ShowsExactlyWhenTheAttackStateReachesContact()
    {
        AttackTimeline timeline = new(0.15f, 0.05f, 0.15f);
        CharacterAnimationClip slash = set.Find("Slash");
        foreach (float speed in new[] { 0.7f, 1f, 1.3f, 2.1f })
        {
            foreach (float start in new[] { 0.013f, 0.37f, 2.9f })
            {
                clock.Time = start;
                AttackState state = new(clock);
                CharacterAnimator animator = new(set, settings, clock);
                AttackTimeline played = timeline.At(speed);
                Assert.That(state.TryStart(0, Vector2.left, played, 1f, true, true), Is.True);
                animator.PlayAction(slash, Vector2.left, played, false);
                bool contacted = false;
                for (int step = 0; step < 60 && !contacted; step++)
                {
                    contacted = (state.Advance() & AttackStep.Contact) != 0;
                    animator.Update(Vector2.zero, Vector2.left);
                    Assert.That(animator.Frame == Contact, Is.EqualTo(contacted), $"x{speed} from {start} at step {step}");
                    clock.Advance(1f / 60f);
                }

                Assert.That(contacted, Is.True);
            }
        }
    }

    /// <summary>
    /// A copy's attack, played until contact, holds its last windup frame past the windup until the owner's contact
    /// arrives, then shows the contact and recovery from that moment; a contact that arrives early cuts the windup
    /// short, and stopping the action shows locomotion again.
    /// </summary>
    [Test]
    public void ACopysAttack_HoldsItsWindupUntilTheContactArrives()
    {
        AttackTimeline timeline = new(0.25f, 0.125f, 0.25f);
        CharacterAnimationClip slash = set.Find("Slash");
        CharacterAnimator animator = new(set, settings, clock);
        animator.Update(Vector2.zero, Vector2.down);
        animator.PlayAction(slash, Vector2.up, timeline, true);
        clock.Advance(0.375f);
        animator.Update(Vector2.zero, Vector2.down);
        Assert.That(animator.Frame, Is.EqualTo(Contact - 1), "The windup holds its last frame.");
        Assert.That(animator.Direction, Is.EqualTo(CharacterDirections.North));
        animator.ReachContact();
        animator.Update(Vector2.zero, Vector2.down);
        Assert.That(animator.Frame, Is.EqualTo(Contact));
        clock.Advance(0.125f);
        animator.Update(Vector2.zero, Vector2.down);
        Assert.That(animator.Frame, Is.EqualTo(Contact + 1));
        animator.ReachContact();
        animator.Update(Vector2.zero, Vector2.down);
        Assert.That(animator.Frame, Is.EqualTo(Contact + 1), "A second contact changes nothing.");

        animator.PlayAction(slash, Vector2.up, timeline, true);
        clock.Advance(0.125f);
        animator.ReachContact();
        animator.Update(Vector2.zero, Vector2.down);
        Assert.That(animator.Frame, Is.EqualTo(Contact), "An early contact cuts the windup short.");
        animator.StopAction(CharacterActionKind.Attack);
        animator.Update(Vector2.zero, Vector2.down);
        Assert.That(animator.IsActing, Is.False);
        Assert.That(animator.Clip, Is.SameAs(set.Locomotion(CharacterGait.Idle)));
    }

    /// <summary>
    /// A copy's attack whose contact does not come holds its last windup frame for the settings' contact wait past the
    /// windup, then ends, the character showing its locomotion again (Docs/KnownGaps.md, Phase 4); nothing then holds
    /// for that contact and a contact arriving later shows nothing. A copy's cast waiting for its completion ends the
    /// same way past its cast time.
    /// </summary>
    [Test]
    public void ACopysWindup_EndsAfterItsContactWait_WithoutAContact()
    {
        AttackTimeline timeline = new(0.25f, 0.125f, 0.25f);
        CharacterAnimationClip slash = set.Find("Slash");
        CharacterAnimator animator = new(set, settings, clock);
        animator.Update(Vector2.zero, Vector2.down);
        animator.PlayAction(slash, Vector2.up, timeline, true);
        clock.Advance(timeline.Windup + settings.ContactWaitSeconds - 0.01f);
        animator.Update(Vector2.zero, Vector2.down);
        Assert.That(animator.IsActing, Is.True);
        Assert.That(animator.Frame, Is.EqualTo(Contact - 1), "Within its wait the windup still holds.");

        clock.Advance(0.02f);
        animator.Update(Vector2.zero, Vector2.down);
        Assert.That(animator.IsActing, Is.False, "Past its wait the copy gives up on the contact.");
        Assert.That(animator.Clip, Is.SameAs(set.Locomotion(CharacterGait.Idle)));
        Assert.That(animator.HoldContact(0.1f), Is.False, "Nothing holds for a contact given up on.");
        animator.ReachContact();
        animator.Update(Vector2.zero, Vector2.down);
        Assert.That(animator.IsActing, Is.False, "A contact arriving later shows nothing.");

        AttackTimeline castTimeline = new(0.5f, 0.06f, 0.24f);
        animator.PlayCast(set.Find("Cast"), null, Vector2.up, false, castTimeline, 0f, true);
        clock.Advance(castTimeline.Windup + settings.ContactWaitSeconds - 0.01f);
        animator.Update(Vector2.zero, Vector2.down);
        Assert.That(animator.IsActing, Is.True, "A copy's gather holds for its completion within the wait.");
        clock.Advance(0.02f);
        animator.Update(Vector2.zero, Vector2.down);
        Assert.That(animator.IsActing, Is.False, "A copy's cast whose completion does not come ends past its wait too.");
    }

    /// <summary>
    /// A copy's attack and the owner's cast, played until contact, show their contact frame on the update the contact
    /// is reached, and hold it when asked then, at any windup and any time on the clock: late in a session, taking the
    /// windup off the clock's value rounds (at 20 s, 20 minus 0.3 and back left 0.29999924), which must not leave the
    /// action in its windup.
    /// </summary>
    [Test]
    public void AReachedContact_ShowsOnItsUpdate_AtAnyTimeOnTheClock()
    {
        CharacterAnimationClip slash = set.Find("Slash");
        CharacterAnimationClip cast = set.Find("Cast");
        foreach (float windup in new[] { 0.15f, 0.3f, 0.35f, 0.45f })
        {
            foreach (float contactAt in new[] { 5f, 10f, 20f, 300f, 1234.5f })
            {
                clock.Time = contactAt - windup - 0.1f;
                CharacterAnimator copy = new(set, settings, clock);
                copy.Update(Vector2.zero, Vector2.right);
                copy.PlayAction(slash, Vector2.right, new AttackTimeline(windup, 0.05f, 0.15f), true);
                CharacterAnimator owner = new(set, settings, clock);
                owner.Update(Vector2.zero, Vector2.right);
                owner.PlayCast(cast, null, Vector2.right, false, new AttackTimeline(windup, 0.06f, 0.24f), 0f, true);
                clock.Time = contactAt;
                copy.ReachContact();
                owner.ReachContact();
                Assert.That(copy.HoldContact(0.08f), Is.True, $"A copy's {windup} s windup reaching contact at {contactAt} s holds it.");
                Assert.That(owner.HoldContact(0.08f), Is.True, $"A {windup} s cast completing at {contactAt} s holds its release.");
                copy.Update(Vector2.zero, Vector2.right);
                owner.Update(Vector2.zero, Vector2.right);
                Assert.That(copy.Frame, Is.EqualTo(Contact), $"A copy's {windup} s windup reaching contact at {contactAt} s.");
                Assert.That(copy.ActionPhase, Is.EqualTo(AttackPhase.Contact));
                Assert.That(owner.Frame, Is.EqualTo(CastRelease), $"A {windup} s cast completing at {contactAt} s.");
                Assert.That(owner.ActionPhase, Is.EqualTo(AttackPhase.Contact));
            }
        }
    }

    /// <summary>
    /// The contact frame is never skipped: an update that jumps past the whole contact, or past the whole attack, still
    /// shows it once, as the attack state still resolves the hit then; the next update goes on to the recovery or ends.
    /// </summary>
    [Test]
    public void TheContactFrame_IsNeverSkipped()
    {
        AttackTimeline timeline = new(0.25f, 0.125f, 0.25f);
        CharacterAnimator animator = new(set, settings, clock);
        animator.Update(Vector2.zero, Vector2.right);
        animator.PlayAction(set.Find("Slash"), Vector2.right, timeline, false);
        clock.Advance(0.5f);
        animator.Update(Vector2.zero, Vector2.right);
        Assert.That(animator.Frame, Is.EqualTo(Contact));
        Assert.That(animator.ActionPhase, Is.EqualTo(AttackPhase.Contact));
        animator.Update(Vector2.zero, Vector2.right);
        Assert.That(animator.Frame, Is.EqualTo(Contact + 2));

        animator.PlayAction(set.Find("Slash"), Vector2.right, timeline, false);
        clock.Advance(2f);
        animator.Update(Vector2.zero, Vector2.right);
        Assert.That(animator.Frame, Is.EqualTo(Contact), "Even past the whole attack.");
        animator.Update(Vector2.zero, Vector2.right);
        Assert.That(animator.IsActing, Is.False);
    }

    /// <summary>
    /// The locomotion layer waits where it was while an attack plays, whether the attack ends by itself or is stopped,
    /// and goes on from there: a run keeps its gait and phase across the attack.
    /// </summary>
    [Test]
    public void Locomotion_ResumesWhereItWas_AfterAnAction()
    {
        CharacterAnimator animator = new(set, settings, clock);
        Vector2 running = Vector2.right * RunNatural;
        animator.Update(running, Vector2.right);
        clock.Advance(0.3f);
        animator.Update(running, Vector2.right);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Run));
        Assert.That(animator.Phase, Is.EqualTo(0.375f).Within(1e-4f));

        animator.PlayAction(set.Find("Slash"), Vector2.right, new AttackTimeline(0.25f, 0.125f, 0.25f), false);
        for (int i = 0; i < 7; i++)
        {
            clock.Advance(0.1f);
            animator.Update(running, Vector2.right);
        }

        Assert.That(animator.IsActing, Is.False);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Run));
        Assert.That(animator.Phase, Is.EqualTo(0.375f).Within(1e-4f), "The run resumes where it was.");
        clock.Advance(0.08f);
        animator.Update(running, Vector2.right);
        Assert.That(animator.Phase, Is.EqualTo(0.475f).Within(1e-4f));

        animator.PlayAction(set.Find("Slash"), Vector2.right, new AttackTimeline(0.25f, 0.125f, 0.25f), false);
        clock.Advance(0.1f);
        animator.Update(running, Vector2.right);
        animator.StopAction(CharacterActionKind.Attack);
        clock.Advance(0.1f);
        animator.Update(running, Vector2.right);
        Assert.That(animator.Phase, Is.EqualTo(0.475f).Within(1e-4f), "A stopped action resumes it too.");
    }

    /// <summary>
    /// For cast times of 0.15, 0.25, 0.5 and 1 s from uneven starts, driven at 60 Hz half a step out of phase with the
    /// cast's start (so no update falls on the cast time itself), a cast shows its gather frames in order, stretched to
    /// the cast time, and its release frame on exactly the first update past its cast time, then its follow-through, and
    /// ends. The owner's casts do not rely on the clock alone: their orders say when they complete.
    /// </summary>
    [Test]
    public void ACast_ShowsItsReleaseFrame_OnTheUpdateItCompletes_AtAnyCastTime()
    {
        CharacterAnimationClip cast = set.Find("Cast");
        foreach (float castTime in new[] { 0.15f, 0.25f, 0.5f, 1f })
        {
            foreach (float start in new[] { 0.013f, 2.9f })
            {
                clock.Time = start;
                CharacterAnimator animator = new(set, settings, clock);
                animator.PlayCast(cast, null, Vector2.right, false, new AttackTimeline(castTime, 0.06f, 0.24f), 0f, false);
                clock.Advance(0.5f / 60f);
                bool completed = false;
                int lastGather = 0;
                for (int step = 0; step < 120 && !completed; step++)
                {
                    completed = clock.Time - start >= castTime;
                    animator.Update(Vector2.zero, Vector2.right);
                    Assert.That(animator.Frame == CastRelease, Is.EqualTo(completed), $"{castTime} s from {start} at step {step}");
                    if (!completed)
                    {
                        Assert.That(animator.Frame, Is.GreaterThanOrEqualTo(lastGather), "The gather plays forward.");
                        lastGather = animator.Frame;
                    }

                    clock.Advance(1f / 60f);
                }

                Assert.That(completed, Is.True);
                Assert.That(lastGather, Is.EqualTo(CastRelease - 1), $"{castTime} s: the gather reaches its last frame.");
                Assert.That(animator.ActionKind, Is.EqualTo(CharacterActionKind.Cast));
                clock.Advance(0.06f);
                animator.Update(Vector2.zero, Vector2.right);
                Assert.That(animator.ActionPhase, Is.EqualTo(AttackPhase.Recovery), "The follow-through plays after the release.");
                clock.Advance(0.25f);
                animator.Update(Vector2.zero, Vector2.right);
                Assert.That(animator.IsActing, Is.False);
            }
        }
    }

    /// <summary>
    /// The owner's cast, played until its release, holds its last gather frame however late the completion comes, and
    /// shows the release on the update it is reached, so it never shows before the orders complete the cast.
    /// </summary>
    [Test]
    public void TheOwnersCast_ShowsItsRelease_WhenItsCompletionIsReached()
    {
        CharacterAnimator animator = new(set, settings, clock);
        animator.Update(Vector2.zero, Vector2.right);
        animator.PlayCast(set.Find("Cast"), null, Vector2.right, false, new AttackTimeline(0.25f, 0.06f, 0.24f), 0f, true);
        clock.Advance(0.3f);
        animator.Update(Vector2.zero, Vector2.right);
        Assert.That(animator.Frame, Is.EqualTo(CastRelease - 1), "Past its cast time, the gather waits for the completion.");
        animator.ReachContact();
        animator.Update(Vector2.zero, Vector2.right);
        Assert.That(animator.Frame, Is.EqualTo(CastRelease));
        Assert.That(animator.ActionPhase, Is.EqualTo(AttackPhase.Contact));
    }

    /// <summary>
    /// An instant cast shows its release frame (a flourish's first) on the update it starts, then its follow-through.
    /// </summary>
    [Test]
    public void AnInstantCast_ShowsItsReleaseOnItsFirstUpdate()
    {
        CharacterAnimationClip flourish = set.Find("Flourish");
        CharacterAnimator animator = new(set, settings, clock);
        animator.Update(Vector2.zero, Vector2.down);
        animator.PlayCast(flourish, null, Vector2.down, false, new AttackTimeline(0f, 0.06f, 0.3f), 0f, false);
        animator.Update(Vector2.zero, Vector2.down);
        Assert.That(animator.Frame, Is.EqualTo(0));
        Assert.That(animator.ActionPhase, Is.EqualTo(AttackPhase.Contact));
        Assert.That(animator.Sprite, Is.SameAs(flourish.SpriteAt(CharacterDirections.South, 0)));
        clock.Advance(0.1f);
        animator.Update(Vector2.zero, Vector2.down);
        Assert.That(animator.ActionPhase, Is.EqualTo(AttackPhase.Recovery));
        Assert.That(animator.Frame, Is.GreaterThan(0));
    }

    /// <summary>
    /// With a channel beat, a cast longer than its clip's gather (0.35 s) plays the gather at its natural pace, then
    /// shows its last two gather frames in turn, a beat each, until the release on time; a cast shorter than the gather
    /// stretches it as without a beat.
    /// </summary>
    [Test]
    public void ALongCast_WithAChannelBeat_GathersAtItsNaturalPace_ThenChannels()
    {
        CharacterAnimationClip cast = set.Find("Cast");
        CharacterAnimator animator = new(set, settings, clock);
        animator.Update(Vector2.zero, Vector2.right);
        float start = clock.Time;
        animator.PlayCast(cast, null, Vector2.right, false, new AttackTimeline(1f, 0.06f, 0.24f), 0.1f, false);
        float[] times = { 0.05f, 0.1f, 0.2f, 0.3f, 0.36f, 0.46f, 0.56f, 0.66f, 0.99f, 1f };
        int[] frames = { 0, 1, 2, 3, 2, 3, 2, 3, 2, 4 };
        for (int i = 0; i < times.Length; i++)
        {
            clock.Time = start + times[i];
            animator.Update(Vector2.zero, Vector2.right);
            Assert.That(animator.Frame, Is.EqualTo(frames[i]), $"at {times[i]} s");
        }

        clock.Time = 10f;
        animator.PlayCast(cast, null, Vector2.right, false, new AttackTimeline(0.25f, 0.06f, 0.24f), 0.1f, false);
        clock.Time = 10.2f;
        animator.Update(Vector2.zero, Vector2.right);
        Assert.That(animator.Frame, Is.EqualTo(3), "A cast shorter than the gather stretches it.");
    }

    /// <summary>
    /// Stopping a cast ends it at once, and locomotion resumes where it was; stopping an attack leaves a cast playing,
    /// and stopping a cast leaves an attack playing.
    /// </summary>
    [Test]
    public void StoppingACast_EndsItAtOnce_AndOnlyACast()
    {
        CharacterAnimator animator = new(set, settings, clock);
        Vector2 running = Vector2.right * RunNatural;
        animator.Update(running, Vector2.right);
        clock.Advance(0.3f);
        animator.Update(running, Vector2.right);
        float phase = animator.Phase;

        animator.PlayCast(set.Find("Cast"), null, Vector2.right, false, new AttackTimeline(1f, 0.06f, 0.24f), 0f, false);
        clock.Advance(0.1f);
        animator.Update(Vector2.zero, Vector2.right);
        animator.StopAction(CharacterActionKind.Attack);
        Assert.That(animator.IsActing, Is.True, "Stopping an attack leaves the cast.");
        animator.StopAction(CharacterActionKind.Cast);
        clock.Advance(0.1f);
        animator.Update(running, Vector2.right);
        Assert.That(animator.IsActing, Is.False);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Run));
        Assert.That(animator.Phase, Is.EqualTo(phase).Within(1e-4f), "The run resumes where it was.");

        animator.PlayAction(set.Find("Slash"), Vector2.right, new AttackTimeline(0.25f, 0.125f, 0.25f), false);
        animator.StopAction(CharacterActionKind.Cast);
        Assert.That(animator.IsActing, Is.True, "Stopping a cast leaves the attack.");
    }

    /// <summary>
    /// A Continue cast while running shows its clip drawn over the run, the run's frame picked by the locomotion
    /// layer's phase, which keeps advancing at the run's rate so the legs keep their gait and stride; standing, it shows
    /// the standing clip; after it the run goes on from where the legs were.
    /// </summary>
    [Test]
    public void AContinueCast_WhileRunning_KeepsTheLegsGaitAndPhase()
    {
        CharacterAnimationClip cast = set.Find("Cast");
        CharacterAnimationClip castRun = set.Find("CastRun");
        CharacterAnimator animator = new(set, settings, clock);
        Vector2 running = Vector2.up * RunNatural;
        animator.Update(running, Vector2.up);
        clock.Advance(0.23f);
        animator.Update(running, Vector2.up);
        animator.PlayCast(cast, castRun, Vector2.zero, true, new AttackTimeline(0.3f, 0.06f, 0.24f), 0f, false);
        float phase = animator.Phase;
        for (int step = 0; step < 40; step++)
        {
            clock.Advance(0.02f);
            animator.Update(running, Vector2.up);
            phase = Mathf.Repeat(phase + 0.02f / 0.8f, 1f);
            Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Run), $"step {step}");
            Assert.That(animator.Phase, Is.EqualTo(phase).Within(1e-4f), $"step {step}: the legs keep their stride.");
            if (!animator.IsActing)
            {
                break;
            }

            Assert.That(animator.ShowsMovingCast, Is.True);
            Assert.That(animator.Clip, Is.SameAs(castRun));
            Assert.That(animator.Sprite, Is.SameAs(castRun.SpriteAt(CharacterDirections.North, animator.Frame, (int)(animator.Phase * 8f) % 8)), $"step {step}");
        }

        Assert.That(animator.IsActing, Is.False, "The cast ended while running.");
        clock.Advance(0.02f);
        animator.Update(running, Vector2.up);
        Assert.That(animator.Phase, Is.EqualTo(Mathf.Repeat(phase + 0.02f / 0.8f, 1f)).Within(1e-4f), "The run goes on without a pause.");

        animator.PlayCast(cast, castRun, Vector2.zero, true, new AttackTimeline(0.3f, 0.06f, 0.24f), 0f, false);
        clock.Advance(0.05f);
        animator.Update(Vector2.zero, Vector2.up);
        Assert.That(animator.ShowsMovingCast, Is.False);
        Assert.That(animator.Sprite, Is.SameAs(cast.SpriteAt(CharacterDirections.North, animator.Frame)), "Standing shows the standing clip.");
    }

    /// <summary>
    /// Moving during the gather keeps a cast without a moving clip; moving on the update the release first shows still
    /// shows it; moving after that cuts the follow-through, and the run shows at once.
    /// </summary>
    [Test]
    public void Moving_CutsAStandingCastsFollowThrough_ButNeverItsRelease()
    {
        CharacterAnimationClip cast = set.Find("Cast");
        CharacterAnimator animator = new(set, settings, clock);
        Vector2 running = Vector2.right * RunNatural;
        animator.Update(Vector2.zero, Vector2.right);
        animator.PlayCast(cast, null, Vector2.right, false, new AttackTimeline(0.25f, 0.06f, 0.24f), 0f, false);
        clock.Advance(0.1f);
        animator.Update(running, Vector2.right);
        Assert.That(animator.IsActing, Is.True, "Moving during the gather keeps the cast.");
        clock.Advance(0.15f);
        animator.Update(running, Vector2.right);
        Assert.That(animator.Frame, Is.EqualTo(CastRelease), "The release shows on its update even while moving.");
        clock.Advance(0.02f);
        animator.Update(running, Vector2.right);
        Assert.That(animator.IsActing, Is.False, "Moving after the release cuts the cast.");
        Assert.That(animator.Clip, Is.SameAs(set.Locomotion(CharacterGait.Run)));

        animator.PlayCast(cast, null, Vector2.right, false, new AttackTimeline(0.25f, 0.06f, 0.24f), 0f, false);
        clock.Advance(0.3f);
        animator.Update(Vector2.zero, Vector2.right);
        clock.Advance(0.05f);
        animator.Update(Vector2.zero, Vector2.right);
        Assert.That(animator.ActionPhase, Is.EqualTo(AttackPhase.Recovery), "Standing, the follow-through plays.");
    }

    /// <summary>
    /// A cast aimed nowhere turns with the facing (keeping its direction within the margin); an aimed cast keeps its
    /// aim whatever the facing; a cast with no direction starts in the current one.
    /// </summary>
    [Test]
    public void ACastAimedNowhere_TurnsWithTheFacing_AndAnAimedOneKeepsItsAim()
    {
        CharacterAnimationClip cast = set.Find("Cast");
        CharacterAnimator animator = new(set, settings, clock);
        animator.Update(Vector2.zero, Vector2.left);
        animator.PlayCast(cast, null, Vector2.zero, true, new AttackTimeline(0.5f, 0.06f, 0.24f), 0f, false);
        animator.Update(Vector2.zero, Vector2.left);
        Assert.That(animator.Direction, Is.EqualTo(CharacterDirections.West), "It starts facing the current way.");
        clock.Advance(0.05f);
        animator.Update(Vector2.zero, Vector2.down);
        Assert.That(animator.Direction, Is.EqualTo(CharacterDirections.South), "It turns with the facing.");

        animator.PlayCast(cast, null, Vector2.right, false, new AttackTimeline(0.5f, 0.06f, 0.24f), 0f, false);
        clock.Advance(0.05f);
        animator.Update(Vector2.zero, Vector2.down);
        Assert.That(animator.Direction, Is.EqualTo(CharacterDirections.East), "An aimed cast keeps its aim.");
    }

    /// <summary>
    /// A hold during a cast's gather ends when the release is reached, so the release shows on the update the cast
    /// completes; a hold taken at the release (the caster's own hit) holds the release frame.
    /// </summary>
    [Test]
    public void AHold_EndsAtTheRelease_AndACastersHitHoldsTheRelease()
    {
        CharacterAnimationClip cast = set.Find("Cast");
        CharacterAnimator animator = new(set, settings, clock);
        animator.Update(Vector2.zero, Vector2.right);
        animator.PlayCast(cast, null, Vector2.right, false, new AttackTimeline(0.25f, 0.06f, 0.24f), 0f, true);
        clock.Advance(0.2f);
        animator.Hold(0.08f);
        animator.Update(Vector2.zero, Vector2.right);
        int held = animator.Frame;
        clock.Advance(0.05f);
        animator.ReachContact();
        animator.Update(Vector2.zero, Vector2.right);
        Assert.That(animator.IsHolding, Is.False, "The release ends the hold.");
        Assert.That(animator.Frame, Is.EqualTo(CastRelease));
        Assert.That(held, Is.LessThan(CastRelease));

        animator.PlayCast(cast, null, Vector2.right, false, new AttackTimeline(0.25f, 0.06f, 0.24f), 0f, true);
        clock.Advance(0.25f);
        animator.ReachContact();
        Assert.That(animator.HoldContact(0.08f), Is.True, "The caster's own hit holds its release.");
        animator.Update(Vector2.zero, Vector2.right);
        clock.Advance(0.07f);
        animator.Update(Vector2.zero, Vector2.right);
        Assert.That(animator.Sprite, Is.SameAs(cast.SpriteAt(CharacterDirections.East, CastRelease)), "Held past the release's 60 ms.");
    }

    /// <summary>
    /// Once warm, updating with changing speeds, facings and gaits, playing, reaching the contact of and stopping
    /// attacks and casts (standing, over the run, channelling) allocates no managed memory.
    /// </summary>
    [Test]
    public void Update_DoesNotAllocate_OnceWarm()
    {
        CharacterAnimator animator = new(set, settings, clock);
        CharacterAnimationClip slash = set.Find("Slash");
        AttackTimeline timeline = new(0.15f, 0.05f, 0.15f);
        float[] speeds = { 0f, 1f, 3f, 6.5f, 2f, 0f };
        TestDelegate run = () =>
        {
            for (int i = 0; i < 60; i++)
            {
                clock.Advance(1f / 60f);
                if (i % 20 == 0)
                {
                    animator.PlayAction(slash, Facing(i * 17f), timeline, i % 40 == 0);
                }
                else if (i % 20 == 12)
                {
                    animator.ReachContact();
                }
                else if (i % 20 == 18)
                {
                    animator.StopAction(CharacterActionKind.Attack);
                }
                else if (i % 20 == 5)
                {
                    animator.PlayCast(set.Find("Cast"), set.Find("CastRun"), Facing(i * 17f), i % 40 == 5, new AttackTimeline(0.6f, 0.06f, 0.24f), 0.1f, i % 3 == 0);
                }
                else if (i % 20 == 9)
                {
                    animator.StopAction(CharacterActionKind.Cast);
                }

                animator.Update(Facing(i * 17f) * speeds[i % speeds.Length], Facing(i * 17f));
            }
        };
        run();

        Assert.That(run, Is.Not.AllocatingGCMemory());
    }

    /// <summary>
    /// The visuals feed their animator the movement they were given and put its sprite on their renderer, on the
    /// injected clock.
    /// </summary>
    [Test]
    public void Visuals_PutTheAnimatorsSpriteOnTheirRenderer()
    {
        GameObject owner = Track(new GameObject("Visuals"));
        SpriteRenderer renderer = owner.AddComponent<SpriteRenderer>();
        CharacterVisuals visuals = owner.AddComponent<CharacterVisuals>();
        SerializedObject serialized = new(visuals);
        serialized.FindProperty("animationSet").objectReferenceValue = set;
        serialized.FindProperty("locomotion").objectReferenceValue = settings;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        visuals.Construct(clock);

        visuals.SetLocomotion(Vector2.up * RunNatural, Vector2.up);
        visuals.Tick();
        Assert.That(renderer.sprite, Is.SameAs(set.Locomotion(CharacterGait.Run).SpriteAt(CharacterDirections.North, 0)));

        clock.Advance(0.25f);
        visuals.Tick();
        Assert.That(renderer.sprite, Is.SameAs(set.Locomotion(CharacterGait.Run).SpriteAt(CharacterDirections.North, 2)));

        visuals.SetLocomotion(Vector2.zero, Vector2.left);
        visuals.Tick();
        Assert.That(renderer.sprite, Is.SameAs(set.Locomotion(CharacterGait.Idle).SpriteAt(CharacterDirections.West, 0)));
    }

    /// <summary>
    /// A weapon plays its attack clip on the visuals from the attack's start, toward its direction, reaching the contact
    /// frame at contact; around it the character runs and stands unarmed, the weapon only showing in its attack.
    /// </summary>
    [Test]
    public void AWeapon_PlaysItsAttackOnTheVisuals_AndOnlyThere()
    {
        PlayerWeaponController weapon = CreateArmedPlayer(out CharacterVisuals visuals, out SpriteRenderer renderer);
        weapon.Equip(Track(PlayerWeapon.Create("Sword", 1, 1f, 0.5f, new Vector2[4], windup: 0.25f, contact: 0.125f, recovery: 0.25f, attackClipName: "Slash")));

        visuals.SetLocomotion(Vector2.right * RunNatural, Vector2.right);
        visuals.Tick();
        Assert.That(renderer.sprite, Is.SameAs(set.Locomotion(CharacterGait.Run).SpriteAt(CharacterDirections.East, 0)), "The character runs unarmed.");

        visuals.SetLocomotion(Vector2.zero, Vector2.down);
        Assert.That(weapon.TryAttack(Vector2.down), Is.True);
        visuals.Tick();
        Assert.That(renderer.sprite, Is.SameAs(set.Find("Slash").SpriteAt(CharacterDirections.South, 0)));
        clock.Advance(0.25f);
        weapon.Tick();
        visuals.Tick();
        Assert.That(weapon.AttackPhase, Is.EqualTo(AttackPhase.Contact));
        Assert.That(renderer.sprite, Is.SameAs(set.Find("Slash").SpriteAt(CharacterDirections.South, Contact)));

        weapon.CancelAttack();
        visuals.Tick();
        Assert.That(renderer.sprite, Is.SameAs(set.Locomotion(CharacterGait.Idle).SpriteAt(CharacterDirections.South, 0)), "A cancelled attack stops showing, and the character stands unarmed.");
    }

    /// <summary>
    /// On a copy, the owner's switch to a weapon arriving after an attack made with it (its message overtook the switch)
    /// leaves that attack playing, while equipping a weapon on the machine running the attack stops what it shows.
    /// </summary>
    [Test]
    public void ASwitchReachingACopy_LeavesItsAttackPlaying()
    {
        PlayerWeaponController weapon = CreateArmedPlayer(out CharacterVisuals visuals, out _);
        PlayerWeapon sword = Track(PlayerWeapon.Create("Sword", 1, 1f, 0.5f, new Vector2[4], windup: 0.25f, contact: 0.125f, recovery: 0.25f, attackClipName: "Slash"));
        PlayerWeapon heavy = Track(PlayerWeapon.Create("Heavy", 2, 1f, 0.5f, new Vector2[4], windup: 0.375f, contact: 0.125f, recovery: 0.25f, attackClipName: "Slash"));
        weapon.SetCatalog(Track(WeaponCatalog.Create(sword, heavy)));
        weapon.Equip(sword);

        weapon.PlayRemoteAttack(1, 0, Vector2.down, 1f);
        visuals.Tick();
        Assert.That(visuals.Animator.ActionKind, Is.EqualTo(CharacterActionKind.Attack));
        weapon.EquipOnCopy(heavy);
        visuals.Tick();
        Assert.That(weapon.CurrentWeapon, Is.SameAs(heavy));
        Assert.That(visuals.Animator.IsActing, Is.True, "The copy's attack plays on through the switch it overtook.");
        weapon.PlayRemoteContact();
        visuals.Tick();
        Assert.That(visuals.Animator.Frame, Is.EqualTo(Contact));

        weapon.Equip(sword);
        visuals.Tick();
        Assert.That(visuals.Animator.IsActing, Is.False, "Equipping where the attack runs ends what it shows.");
    }

    /// <summary>
    /// A tracked player with a weapon controller on the test clock, resolving hits with authority, and its visuals on a
    /// child playing the test set: <paramref name="visuals"/> and their <paramref name="renderer"/>.
    /// </summary>
    private PlayerWeaponController CreateArmedPlayer(out CharacterVisuals visuals, out SpriteRenderer renderer)
    {
        GameObject player = Track(new GameObject("Player"));
        GameObject child = new("Visuals");
        child.transform.SetParent(player.transform, false);
        renderer = child.AddComponent<SpriteRenderer>();
        visuals = child.AddComponent<CharacterVisuals>();
        SerializedObject serialized = new(visuals);
        serialized.FindProperty("animationSet").objectReferenceValue = set;
        serialized.FindProperty("locomotion").objectReferenceValue = settings;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        visuals.Construct(clock);
        PlayerWeaponController weapon = player.AddComponent<PlayerWeaponController>();
        weapon.Construct(TestCombat.CreateAttackService(FixedGameAuthority.Authoritative), null, clock);
        return weapon;
    }

    /// <summary>
    /// The gait a new animator picks at <paramref name="speed"/>.
    /// </summary>
    private CharacterGait GaitAt(float speed)
    {
        CharacterAnimator animator = new(set, settings, clock);
        Step(animator, speed);
        return animator.Gait;
    }

    /// <summary>
    /// The playback rate a new animator plays at <paramref name="speed"/>.
    /// </summary>
    private float RateAt(float speed)
    {
        CharacterAnimator animator = new(set, settings, clock);
        Step(animator, speed);
        return animator.PlaybackRate;
    }

    /// <summary>
    /// One update moving east at <paramref name="speed"/>, without advancing the clock.
    /// </summary>
    private static void Step(CharacterAnimator animator, float speed)
    {
        animator.Update(Vector2.right * speed, Vector2.right);
    }

    /// <summary>
    /// The unit facing <paramref name="degrees"/> counterclockwise from east.
    /// </summary>
    private static Vector2 Facing(float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
    }

    /// <summary>
    /// An attack clip of eight frames with its contact on frame 4: a windup of 0.25, 0.125, 0.0625 and 0.0625 s, a
    /// contact frame of 0.125 s and a recovery of 0.125, 0.0625 and 0.0625 s at natural speed, each sprite its own.
    /// </summary>
    private CharacterAnimationClip AttackClip(string name)
    {
        float[] frameSeconds = { 0.25f, 0.125f, 0.0625f, 0.0625f, 0.125f, 0.125f, 0.0625f, 0.0625f };
        return CharacterAnimationClip.Create(name, CharacterGait.None, true, Sprites(name, frameSeconds.Length), frameSeconds, 0f, Contact);
    }

    /// <summary>
    /// A cast clip of eight frames with its release on frame 4: a gather of 0.09, 0.09, 0.09 and 0.08 s (0.35 s), a
    /// release of 0.09 s and a follow-through of 0.09, 0.1 and 0.1 s at natural speed, drawn over
    /// <paramref name="gaitFrames"/> gait frames (0 for standing), each sprite its own.
    /// </summary>
    private CharacterAnimationClip CastClip(string name, int gaitFrames)
    {
        float[] frameSeconds = { 0.09f, 0.09f, 0.09f, 0.08f, 0.09f, 0.09f, 0.1f, 0.1f };
        return CharacterAnimationClip.Create(name, CharacterGait.None, false, Sprites(name, frameSeconds.Length * Mathf.Max(1, gaitFrames)), frameSeconds, 0f, CastRelease, gaitFrames);
    }

    /// <summary>
    /// An instant cast's flourish: five frames, the release first.
    /// </summary>
    private CharacterAnimationClip FlourishClip(string name)
    {
        float[] frameSeconds = { 0.08f, 0.07f, 0.09f, 0.1f, 0.1f };
        return CharacterAnimationClip.Create(name, CharacterGait.None, false, Sprites(name, frameSeconds.Length), frameSeconds, 0f, 0);
    }

    /// <summary>
    /// A clip of <paramref name="frames"/> frames of <paramref name="seconds"/> each, with a sprite of its own for every
    /// direction and frame.
    /// </summary>
    private CharacterAnimationClip Clip(string name, CharacterGait gait, int frames, float seconds, float naturalSpeed)
    {
        float[] frameSeconds = new float[frames];
        for (int frame = 0; frame < frames; frame++)
        {
            frameSeconds[frame] = seconds;
        }

        return CharacterAnimationClip.Create(name, gait, false, Sprites(name, frames), frameSeconds, naturalSpeed);
    }

    /// <summary>
    /// A sprite of its own for every direction and each of <paramref name="frames"/> frames of clip
    /// <paramref name="name"/>.
    /// </summary>
    private Sprite[] Sprites(string name, int frames)
    {
        Texture2D texture = Track(new Texture2D(4, 4));
        Sprite[] sprites = new Sprite[CharacterDirections.Count * frames];
        for (int i = 0; i < sprites.Length; i++)
        {
            sprites[i] = Track(Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), Vector2.zero, 32f));
            sprites[i].name = $"{name}_{i / frames}_{i % frames}";
        }

        return sprites;
    }

    /// <summary>
    /// Remembers <paramref name="instance"/> for destruction after the test.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}
