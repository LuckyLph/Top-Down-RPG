using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

public class PlayerOrdersTests
{
    private const float AttackRange = 0.3f;

    private readonly List<Object> createdObjects = new();
    private PlayerControlSettings settings;
    private FakeUnits units;
    private FakeCaster caster;
    private PlayerOrders orders;
    private float time;
    private Vector2 position;
    private bool reachedDestination;
    private float stalledTime;

    [SetUp]
    public void SetUp()
    {
        settings = Track(TestPlayerControlSettings.Create(stuckTimeout: 0.75f, holdReevaluateInterval: 0.15f, repathInterval: 0.5f, targetMoveRepathDistance: 0.5f, castBufferWindow: 0.4f));
        units = new FakeUnits();
        caster = new FakeCaster();
        orders = new PlayerOrders(settings, units, caster);
        time = 0f;
        position = Vector2.zero;
        reachedDestination = true;
        stalledTime = 0f;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object created in createdObjects)
        {
            if (created != null)
            {
                Object.DestroyImmediate(created);
            }
        }

        createdObjects.Clear();
    }

    [Test]
    public void ClickOnTheGround_MovesToThePointer()
    {
        PlayerOrderOutput output = Tick(Click(new Vector2(3f, 1f)));

        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Move));
        Assert.That(output.Motor, Is.EqualTo(PlayerMotorRequest.MoveTo));
        Assert.That(output.Destination, Is.EqualTo(new Vector2(3f, 1f)));
        Assert.That(output.Swing, Is.False);
    }

    [Test]
    public void Move_IgnoresTheMotorsStaleArrivalOnTheTickItIsIssued_ThenEndsWhenTheMotorArrives()
    {
        reachedDestination = true;
        Tick(Click(new Vector2(3f, 1f)));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Move));

        reachedDestination = false;
        Assert.That(Tick(default).Motor, Is.EqualTo(PlayerMotorRequest.None));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Move));

        reachedDestination = true;
        PlayerOrderOutput output = Tick(default);
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Idle));
        Assert.That(output.Motor, Is.EqualTo(PlayerMotorRequest.Stop));
    }

    [Test]
    public void Move_GivesUpOnceStuckForTheTimeout()
    {
        Tick(Click(new Vector2(3f, 1f)));
        reachedDestination = false;

        stalledTime = 0.74f;
        Tick(default);
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Move));

        stalledTime = 0.75f;
        Assert.That(Tick(default).Motor, Is.EqualTo(PlayerMotorRequest.Stop));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Idle));
    }

    [Test]
    public void ClickOnAnAlly_MovesToTheClickedPoint()
    {
        UnitTarget ally = CreateUnit(UnitTeam.Ally, new Vector2(2f, 0f), distance: 0.1f);

        PlayerOrderOutput output = Tick(Click(new Vector2(2f, 0.2f), ally));

        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Move));
        Assert.That(output.Destination, Is.EqualTo(new Vector2(2f, 0.2f)));
    }

    [Test]
    public void ClickOnADeadEnemy_MovesToTheClickedPoint()
    {
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(2f, 0f), distance: 1.5f);
        units.SetAlive(enemy, false);

        Tick(Click(new Vector2(2f, 0f), enemy));

        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Move));
    }

    [Test]
    public void ClickOnAnEnemyOutOfRange_ChasesItsPosition()
    {
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(4f, 0f), distance: 3.5f);

        PlayerOrderOutput output = Tick(Click(new Vector2(4f, 0.3f), enemy));

        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Attack));
        Assert.That(orders.AttackTarget.IsSameUnit(enemy), Is.True);
        Assert.That(output.Motor, Is.EqualTo(PlayerMotorRequest.MoveTo));
        Assert.That(output.Destination, Is.EqualTo(new Vector2(4f, 0f)));
        Assert.That(output.Swing, Is.False);
    }

    [Test]
    public void Chase_RepathsAtTheIntervalOrAsSoonAsTheTargetMovesFarEnough()
    {
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(4f, 0f), distance: 3.5f);
        Tick(Click(new Vector2(4f, 0f), enemy));

        time = 0.2f;
        units.SetPosition(enemy, new Vector2(4.4f, 0f));
        Assert.That(Tick(default).Motor, Is.EqualTo(PlayerMotorRequest.None), "A small move before the interval keeps the path.");

        time = 0.3f;
        units.SetPosition(enemy, new Vector2(4.6f, 0f));
        PlayerOrderOutput moved = Tick(default);
        Assert.That(moved.Motor, Is.EqualTo(PlayerMotorRequest.MoveTo), "Moving past the threshold repaths early.");
        Assert.That(moved.Destination, Is.EqualTo(new Vector2(4.6f, 0f)));

        time = 0.79f;
        Assert.That(Tick(default).Motor, Is.EqualTo(PlayerMotorRequest.None));
        time = 0.81f;
        Assert.That(Tick(default).Motor, Is.EqualTo(PlayerMotorRequest.MoveTo), "The interval repaths even if the target stood still.");
    }

    [Test]
    public void InRange_StopsOnce_FacesTheTarget_AndKeepsSwinging()
    {
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(0f, 1f), distance: 3f);
        Tick(Click(new Vector2(0f, 1f), enemy));

        units.SetDistance(enemy, AttackRange);
        PlayerOrderOutput arrived = Tick(default);
        Assert.That(arrived.Motor, Is.EqualTo(PlayerMotorRequest.Stop));
        Assert.That(arrived.HasAim, Is.True);
        Assert.That(arrived.AimDirection, Is.EqualTo(Vector2.up));
        Assert.That(arrived.Swing, Is.True);

        PlayerOrderOutput next = Tick(default);
        Assert.That(next.Motor, Is.EqualTo(PlayerMotorRequest.None));
        Assert.That(next.Swing, Is.True, "Swings are requested every tick; the weapon cooldown decides when one lands.");
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Attack));
    }

    [Test]
    public void ClickOnAnEnemyAlreadyInRange_SwingsAtOnce()
    {
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(-0.4f, 0f), distance: 0.1f);

        PlayerOrderOutput output = Tick(Click(new Vector2(-0.4f, 0f), enemy));

        Assert.That(output.Motor, Is.EqualTo(PlayerMotorRequest.Stop));
        Assert.That(output.AimDirection, Is.EqualTo(Vector2.left));
        Assert.That(output.Swing, Is.True);
    }

    [Test]
    public void TargetLeavingRange_IsChasedAgain_AndDistanceNeverCancelsTheOrder()
    {
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(0.4f, 0f), distance: 0.1f);
        Tick(Click(new Vector2(0.4f, 0f), enemy));

        units.SetPosition(enemy, new Vector2(500f, 0f));
        units.SetDistance(enemy, 499f);
        PlayerOrderOutput output = Tick(default);

        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Attack));
        Assert.That(output.Motor, Is.EqualTo(PlayerMotorRequest.MoveTo));
        Assert.That(output.Destination, Is.EqualTo(new Vector2(500f, 0f)));
        Assert.That(output.Swing, Is.False);
    }

    [Test]
    public void TargetDying_EndsTheAttackOrder()
    {
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(0.4f, 0f), distance: 0.1f);
        Tick(Click(new Vector2(0.4f, 0f), enemy));

        units.SetAlive(enemy, false);
        PlayerOrderOutput output = Tick(default);

        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Idle));
        Assert.That(output.Motor, Is.EqualTo(PlayerMotorRequest.Stop));
        Assert.That(output.Swing, Is.False);
    }

    [Test]
    public void Stop_CancelsTheCurrentOrder()
    {
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(4f, 0f), distance: 3.5f);
        Tick(Click(new Vector2(4f, 0f), enemy));

        PlayerOrderOutput output = Tick(new PlayerCommand(Vector2.zero, default, false, false, stopPressed: true));

        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Idle));
        Assert.That(output.Motor, Is.EqualTo(PlayerMotorRequest.Stop));
    }

    [Test]
    public void ANewClick_ReplacesTheCurrentOrderAtOnce()
    {
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(0.4f, 0f), distance: 0.1f);
        Tick(Click(new Vector2(0.4f, 0f), enemy));

        PlayerOrderOutput output = Tick(Click(new Vector2(-5f, 2f)));

        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Move));
        Assert.That(output.Destination, Is.EqualTo(new Vector2(-5f, 2f)));
        Assert.That(output.Swing, Is.False);
    }

    [Test]
    public void HoldingTheButton_FollowsTheCursorAtTheThrottledRate()
    {
        Tick(Click(new Vector2(1f, 0f)));
        reachedDestination = false;

        time = 0.1f;
        Assert.That(Tick(Hold(new Vector2(2f, 0f))).Motor, Is.EqualTo(PlayerMotorRequest.None));
        Assert.That(orders.MoveDestination, Is.EqualTo(new Vector2(1f, 0f)));

        time = 0.15f;
        PlayerOrderOutput output = Tick(Hold(new Vector2(2f, 0f)));
        Assert.That(output.Motor, Is.EqualTo(PlayerMotorRequest.MoveTo));
        Assert.That(output.Destination, Is.EqualTo(new Vector2(2f, 0f)));

        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(3f, 0f), distance: 2.5f);
        time = 0.31f;
        Tick(Hold(new Vector2(3f, 0f), enemy));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Attack), "Holding over an enemy switches to attacking it.");
    }

    [Test]
    public void HoldingOverTheSameEnemy_KeepsTheChaseRunning()
    {
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(4f, 0f), distance: 3.5f);
        Tick(Click(new Vector2(4f, 0f), enemy));

        time = 0.2f;
        PlayerOrderOutput output = Tick(Hold(new Vector2(4f, 0f), enemy));

        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Attack));
        Assert.That(output.Motor, Is.EqualTo(PlayerMotorRequest.None), "Re-evaluating onto the same target must not restart the chase.");
    }

    [Test]
    public void AnEmptyCommand_LetsTheCurrentOrderRunOn()
    {
        Tick(Click(new Vector2(3f, 1f)));
        reachedDestination = false;

        for (int i = 0; i < 10; i++)
        {
            time += 0.1f;
            Tick(default);
        }

        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Move), "Disabled input makes no new orders and leaves the current one running.");
    }

    [Test]
    public void Clear_DropsTheOrderWithoutAMotorRequest()
    {
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(4f, 0f), distance: 3.5f);
        Tick(Click(new Vector2(4f, 0f), enemy));

        orders.Clear();

        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Idle));
        Assert.That(orders.AttackTarget.Exists, Is.False);
        Assert.That(Tick(default).Motor, Is.EqualTo(PlayerMotorRequest.None));
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(4f, 0f), distance: 3.5f);
        PlayerCommand click = Click(new Vector2(4f, 0f), enemy);
        PlayerCommand ground = Click(new Vector2(-4f, 0f));
        PlayerOrderContext context = new(position, time, false, 0f, AttackRange);
        orders.Tick(click, context);

        TestDelegate tick = () =>
        {
            orders.Tick(ground, context);
            orders.Tick(click, context);
            orders.Tick(default, context);
        };

        Assert.That(tick, Is.Not.AllocatingGCMemory());
    }

    [Test]
    public void AnInstantCast_StartsAndEndsInOneTick_AndTheOrderRunsOn()
    {
        Tick(Click(new Vector2(5f, 0f)));
        reachedDestination = false;
        caster.Set(0, Ability(castTime: 0f, targeting: AbilityTargeting.Direction));

        PlayerOrderOutput output = Tick(Press(0, new Vector2(0f, 3f)));

        Assert.That(caster.Started, Is.EqualTo(1));
        Assert.That(caster.Ended, Is.EqualTo(1));
        Assert.That(caster.LastAim.Direction, Is.EqualTo(Vector2.up));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Move));
        Assert.That(output.Motor, Is.EqualTo(PlayerMotorRequest.MoveTo), "The resumed move repaths from where the player stopped.");
        Assert.That(output.Destination, Is.EqualTo(new Vector2(5f, 0f)));
        Assert.That(output.AimDirection, Is.EqualTo(Vector2.up), "The player turns toward an instant cast.");
    }

    [Test]
    public void ACastWithCastTime_PausesTheOrder_FacesTheAim_AndResumesWhenItIsOver()
    {
        Tick(Click(new Vector2(5f, 0f)));
        reachedDestination = false;
        caster.Set(1, Ability(castTime: 0.5f, targeting: AbilityTargeting.Direction));

        PlayerOrderOutput started = Tick(Press(1, new Vector2(-3f, 0f)));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Casting));
        Assert.That(orders.Paused, Is.EqualTo(PlayerOrderKind.Move));
        Assert.That(orders.CastingSlot, Is.EqualTo(1));
        Assert.That(started.Motor, Is.EqualTo(PlayerMotorRequest.Stop));
        Assert.That(started.HasAim, Is.True);
        Assert.That(started.AimDirection, Is.EqualTo(Vector2.left));

        time = 0.49f;
        PlayerOrderOutput during = Tick(default);
        Assert.That(during.Motor, Is.EqualTo(PlayerMotorRequest.None));
        Assert.That(during.AimDirection, Is.EqualTo(Vector2.left));
        Assert.That(caster.Ended, Is.Zero);

        time = 0.5f;
        PlayerOrderOutput resumed = Tick(default);
        Assert.That(caster.Ended, Is.EqualTo(1));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Move));
        Assert.That(resumed.Motor, Is.EqualTo(PlayerMotorRequest.MoveTo));

        time = 0.6f;
        Assert.That(Tick(default).HasAim, Is.False, "Facing follows movement again after the cast.");
    }

    [Test]
    public void ContinueMovement_KeepsThePausedOrderMoving_WithoutSwinging()
    {
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(4f, 0f), distance: 3.5f);
        Tick(Click(new Vector2(4f, 0f), enemy));
        caster.Set(0, Ability(castTime: 2f, targeting: AbilityTargeting.None, movement: CastMovement.Continue));

        PlayerOrderOutput started = Tick(Press(0, Vector2.zero));
        Assert.That(started.Motor, Is.EqualTo(PlayerMotorRequest.None), "Continue keeps the current path.");
        Assert.That(started.HasAim, Is.False, "A self cast has no aim.");

        time = 0.6f;
        units.SetPosition(enemy, new Vector2(4.2f, 0f));
        Assert.That(Tick(default).Motor, Is.EqualTo(PlayerMotorRequest.MoveTo), "The paused chase keeps repathing.");

        units.SetDistance(enemy, 0.1f);
        PlayerOrderOutput inRange = Tick(default);
        Assert.That(inRange.Swing, Is.False, "The paused attack does not swing during the cast.");
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Casting));
    }

    [Test]
    public void AbilityMovement_StandsStillForNow()
    {
        Tick(Click(new Vector2(5f, 0f)));
        caster.Set(0, Ability(castTime: 0.3f, movement: CastMovement.Ability));

        Assert.That(Tick(Press(0, new Vector2(1f, 0f))).Motor, Is.EqualTo(PlayerMotorRequest.Stop));
        time = 0.1f;
        Assert.That(Tick(default).Motor, Is.EqualTo(PlayerMotorRequest.None));
    }

    [Test]
    public void PointTargeting_ClampsTheCursorToTheRange()
    {
        position = new Vector2(1f, 1f);
        caster.Set(0, Ability(targeting: AbilityTargeting.Point, range: 2f));

        Tick(Press(0, new Vector2(11f, 1f)));

        Assert.That(caster.LastAim.Point, Is.EqualTo(new Vector2(3f, 1f)));
        Assert.That(caster.LastAim.Direction, Is.EqualTo(Vector2.right));

        caster.Set(1, Ability(targeting: AbilityTargeting.Point, range: 2f));
        Tick(Press(1, new Vector2(1f, 2f)));
        Assert.That(caster.LastAim.Point, Is.EqualTo(new Vector2(1f, 2f)), "A point inside the range is used as is.");
    }

    [Test]
    public void UnitTargeting_UsesTheUnitUnderTheCursorThatPassesTheFilter()
    {
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(0f, 1f), distance: 0.5f);
        UnitTarget self = CreateUnit(UnitTeam.Ally, Vector2.zero, distance: 0f);
        caster.Set(0, Ability(targeting: AbilityTargeting.Unit, filter: AbilityUnitFilter.Enemy, range: 3f));
        caster.Set(1, Ability(targeting: AbilityTargeting.Unit, filter: AbilityUnitFilter.Ally, range: 3f));
        caster.Set(2, Ability(targeting: AbilityTargeting.Unit, filter: AbilityUnitFilter.Any, range: 3f));

        Tick(Press(0, new Vector2(0f, 1f), self));
        Assert.That(caster.LastFailure, Is.EqualTo((0, CastOutcome.NoTarget)), "An enemy-only ability cannot target an ally.");

        Tick(Press(0, new Vector2(0f, 1f), enemy));
        Assert.That(caster.LastAim.Target.IsSameUnit(enemy), Is.True);
        Assert.That(caster.LastAim.Direction, Is.EqualTo(Vector2.up));

        Tick(Press(1, Vector2.zero, self));
        Assert.That(caster.LastAim.Target.IsSameUnit(self), Is.True, "The caster counts as an ally.");

        Tick(Press(2, new Vector2(0f, 1f), enemy));
        Assert.That(caster.LastAim.Target.IsSameUnit(enemy), Is.True);
        Tick(Press(2, Vector2.zero, self));
        Assert.That(caster.LastAim.Target.IsSameUnit(self), Is.True);

        Tick(Press(2, new Vector2(5f, 5f)));
        Assert.That(caster.LastFailure, Is.EqualTo((2, CastOutcome.NoTarget)), "Nothing under the cursor means no cast.");
        Assert.That(caster.Started, Is.EqualTo(4));
    }

    [Test]
    public void CasterFailures_AreReported_AndChangeNothing()
    {
        Tick(Click(new Vector2(5f, 0f)));
        reachedDestination = false;
        caster.Set(0, Ability());
        caster.Failure = CastOutcome.OnCooldown;

        PlayerOrderOutput output = Tick(Press(0, Vector2.right));

        Assert.That(caster.LastFailure, Is.EqualTo((0, CastOutcome.OnCooldown)));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Move));
        Assert.That(output.Motor, Is.EqualTo(PlayerMotorRequest.None));

        caster.Failure = null;
        Tick(Press(3, Vector2.right));
        Assert.That(caster.LastFailure, Is.EqualTo((3, CastOutcome.EmptySlot)));
    }

    [Test]
    public void ANonBufferablePressDuringACast_Fails()
    {
        caster.Set(0, Ability(castTime: 1f));
        caster.Set(1, Ability(castTime: 0f));
        Tick(Press(0, Vector2.right));

        Tick(Press(1, Vector2.right));

        Assert.That(caster.LastFailure, Is.EqualTo((1, CastOutcome.CastInProgress)));
        Assert.That(orders.CastingSlot, Is.EqualTo(0));
        Assert.That(orders.HasBufferedCast, Is.False);
    }

    [Test]
    public void ABufferedCast_FiresWhenTheRunningCastEnds_PausingTheOrderAboutToResume()
    {
        Tick(Click(new Vector2(5f, 0f)));
        reachedDestination = false;
        caster.Set(0, Ability(castTime: 0.3f));
        caster.Set(1, Ability(castTime: 0.5f, bufferable: true));
        Tick(Press(0, Vector2.right));

        time = 0.1f;
        Tick(Press(1, Vector2.up));
        Assert.That(orders.BufferedSlot, Is.EqualTo(1));

        time = 0.3f;
        Tick(default);

        Assert.That(orders.CastingSlot, Is.EqualTo(1));
        Assert.That(orders.Paused, Is.EqualTo(PlayerOrderKind.Move));
        Assert.That(caster.LastAim.Direction, Is.EqualTo(Vector2.up), "The buffered cast keeps the aim taken at the press.");
        Assert.That(orders.HasBufferedCast, Is.False);

        time = 0.8f;
        Tick(default);
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Move));
    }

    [Test]
    public void TheNewestBufferedPress_Wins()
    {
        caster.Set(0, Ability(castTime: 0.3f));
        caster.Set(1, Ability(bufferable: true));
        caster.Set(2, Ability(bufferable: true));
        Tick(Press(0, Vector2.right));

        Tick(Press(1, Vector2.right));
        Tick(Press(2, Vector2.right));

        Assert.That(caster.LastFailure, Is.EqualTo((1, CastOutcome.Cancelled)));
        Assert.That(orders.BufferedSlot, Is.EqualTo(2));
    }

    [Test]
    public void ABufferedCast_ExpiresAfterTheWindow()
    {
        caster.Set(0, Ability(castTime: 1f));
        caster.Set(1, Ability(bufferable: true));
        Tick(Press(0, Vector2.right));
        Tick(Press(1, Vector2.right));

        time = 0.4f;
        Tick(default);
        Assert.That(orders.HasBufferedCast, Is.True);

        time = 0.41f;
        Tick(default);
        Assert.That(orders.HasBufferedCast, Is.False);
        Assert.That(caster.LastFailure, Is.EqualTo((1, CastOutcome.Expired)));

        time = 1f;
        Tick(default);
        Assert.That(caster.Started, Is.EqualTo(1));
    }

    [Test]
    public void ABufferedCast_IsDroppedByANewOrderOrStop()
    {
        caster.Set(0, Ability(castTime: 1f));
        caster.Set(1, Ability(bufferable: true));
        Tick(Press(0, Vector2.right));

        Tick(Press(1, Vector2.right));
        Tick(Click(new Vector2(3f, 3f)));
        Assert.That(caster.LastFailure, Is.EqualTo((1, CastOutcome.Cancelled)));
        Assert.That(orders.HasBufferedCast, Is.False);

        Tick(Press(1, Vector2.right));
        Tick(new PlayerCommand(Vector2.zero, default, false, false, stopPressed: true));
        Assert.That(orders.HasBufferedCast, Is.False);
        Assert.That(caster.FailureCount, Is.EqualTo(2));
    }

    [Test]
    public void ABufferedUnitCast_IsDroppedIfItsTargetDiedBeforeItFires()
    {
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(1f, 0f), distance: 0.5f);
        caster.Set(0, Ability(castTime: 0.3f));
        caster.Set(1, Ability(targeting: AbilityTargeting.Unit, range: 3f, bufferable: true));
        Tick(Press(0, Vector2.right));
        Tick(Press(1, new Vector2(1f, 0f), enemy));

        units.SetAlive(enemy, false);
        time = 0.3f;
        Tick(default);

        Assert.That(caster.LastFailure, Is.EqualTo((1, CastOutcome.NoTarget)));
        Assert.That(caster.Started, Is.EqualTo(1));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Idle));
    }

    [Test]
    public void ANewOrderDuringACast_ReplacesThePausedOrder_AndStartsWhenTheCastEnds()
    {
        Tick(Click(new Vector2(5f, 0f)));
        caster.Set(0, Ability(castTime: 0.5f));
        Tick(Press(0, Vector2.right));

        PlayerOrderOutput clicked = Tick(Click(new Vector2(-2f, 0f)));
        Assert.That(clicked.Motor, Is.EqualTo(PlayerMotorRequest.None), "The cast keeps the player still.");
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Casting));

        time = 0.5f;
        PlayerOrderOutput resumed = Tick(default);
        Assert.That(resumed.Motor, Is.EqualTo(PlayerMotorRequest.MoveTo));
        Assert.That(resumed.Destination, Is.EqualTo(new Vector2(-2f, 0f)));
    }

    [Test]
    public void StopDuringACast_ClearsThePausedOrder_AndTheCastEndsIntoIdle()
    {
        Tick(Click(new Vector2(5f, 0f)));
        caster.Set(0, Ability(castTime: 0.5f));
        Tick(Press(0, Vector2.right));

        Tick(new PlayerCommand(Vector2.zero, default, false, false, stopPressed: true));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Casting), "A started cast runs to its end.");
        Assert.That(orders.Paused, Is.EqualTo(PlayerOrderKind.Idle));

        time = 0.5f;
        Tick(default);
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Idle));
        Assert.That(caster.Ended, Is.EqualTo(1));
    }

    [Test]
    public void AUnitCastOutOfRange_WalksIntoRange_ThenCasts_ThenGoesIdle()
    {
        UnitTarget ally = CreateUnit(UnitTeam.Ally, new Vector2(6f, 0f), distance: 5.5f);
        caster.Set(2, Ability(castTime: 0.2f, targeting: AbilityTargeting.Unit, filter: AbilityUnitFilter.Ally, range: 4f));

        PlayerOrderOutput approaching = Tick(Press(2, new Vector2(6f, 0f), ally));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.CastWhenInRange));
        Assert.That(orders.ApproachingSlot, Is.EqualTo(2));
        Assert.That(approaching.Motor, Is.EqualTo(PlayerMotorRequest.MoveTo));
        Assert.That(approaching.Destination, Is.EqualTo(new Vector2(6f, 0f)));
        Assert.That(caster.Started, Is.Zero);

        position = new Vector2(2.5f, 0f);
        units.SetDistance(ally, 3.5f);
        time = 0.1f;
        PlayerOrderOutput inRange = Tick(default);
        Assert.That(caster.Started, Is.EqualTo(1));
        Assert.That(caster.LastAim.Target.IsSameUnit(ally), Is.True);
        Assert.That(inRange.Motor, Is.EqualTo(PlayerMotorRequest.Stop));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Casting));

        time = 0.3f;
        Tick(default);
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Idle), "The approach order ends with its cast.");
    }

    [Test]
    public void AnApproachWhoseTargetDies_EndsIdle()
    {
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(6f, 0f), distance: 5.5f);
        caster.Set(3, Ability(targeting: AbilityTargeting.Unit, range: 3f));
        Tick(Press(3, new Vector2(6f, 0f), enemy));

        units.SetAlive(enemy, false);
        PlayerOrderOutput output = Tick(default);

        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Idle));
        Assert.That(output.Motor, Is.EqualTo(PlayerMotorRequest.Stop));
        Assert.That(caster.LastFailure, Is.EqualTo((3, CastOutcome.NoTarget)));
    }

    [Test]
    public void Clear_EndsARunningCast_AndDropsTheBufferedOne()
    {
        caster.Set(0, Ability(castTime: 1f));
        caster.Set(1, Ability(bufferable: true));
        Tick(Press(0, Vector2.right));
        Tick(Press(1, Vector2.right));

        orders.Clear();

        Assert.That(caster.Ended, Is.EqualTo(1));
        Assert.That(caster.LastFailure, Is.EqualTo((1, CastOutcome.Cancelled)));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Idle));
    }

    [Test]
    public void Casting_DoesNotAllocate()
    {
        caster.Set(0, Ability(castTime: 0f));
        caster.Set(1, Ability(castTime: 0.1f, bufferable: true));
        PlayerCommand instant = Press(0, Vector2.right);
        PlayerCommand buffered = Press(1, Vector2.up);
        PlayerOrderContext context = new(position, 0f, false, 0f, AttackRange);
        PlayerOrderContext later = new(position, 1f, false, 0f, AttackRange);
        orders.Tick(instant, context);

        TestDelegate cast = () =>
        {
            orders.Tick(buffered, context);
            orders.Tick(buffered, context);
            orders.Tick(default, later);
            orders.Tick(instant, later);
        };

        Assert.That(cast, Is.Not.AllocatingGCMemory());
    }

    [Test]
    public void ABufferedContinueCast_KeepsThePausedMoveGoing()
    {
        Tick(Click(new Vector2(5f, 0f)));
        reachedDestination = false;
        caster.Set(0, Ability(castTime: 0.25f));
        caster.Set(1, Ability(castTime: 0.5f, targeting: AbilityTargeting.None, movement: CastMovement.Continue, bufferable: true));
        Tick(Press(0, Vector2.right));
        Tick(Press(1, Vector2.right));

        reachedDestination = true;
        time = 0.25f;
        PlayerOrderOutput fired = Tick(default);

        Assert.That(orders.CastingSlot, Is.EqualTo(1));
        Assert.That(orders.Paused, Is.EqualTo(PlayerOrderKind.Move), "The stopped motor's stale arrival must not end the paused move.");
        Assert.That(fired.Motor, Is.EqualTo(PlayerMotorRequest.MoveTo));
        Assert.That(fired.Destination, Is.EqualTo(new Vector2(5f, 0f)));

        reachedDestination = false;
        time = 0.75f;
        Tick(default);
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Move));
    }

    [Test]
    public void ABufferedInstantCast_FiresAndTheOrderResumesInTheSameTick()
    {
        Tick(Click(new Vector2(5f, 0f)));
        reachedDestination = false;
        caster.Set(0, Ability(castTime: 0.3f));
        caster.Set(1, Ability(castTime: 0f, bufferable: true));
        Tick(Press(0, Vector2.right));
        Tick(Press(1, Vector2.up));

        time = 0.3f;
        PlayerOrderOutput output = Tick(default);

        Assert.That(caster.Started, Is.EqualTo(2));
        Assert.That(caster.Ended, Is.EqualTo(2));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Move));
        Assert.That(output.Motor, Is.EqualTo(PlayerMotorRequest.MoveTo));
    }

    [Test]
    public void ABufferedUnitCastOutOfRange_BecomesAnApproach_ThatChasesAtOnce()
    {
        Tick(Click(new Vector2(5f, 0f)));
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(0f, 2f), distance: 1f);
        caster.Set(0, Ability(castTime: 0.3f));
        caster.Set(1, Ability(targeting: AbilityTargeting.Unit, range: 3f, bufferable: true));
        Tick(Press(0, Vector2.right));
        Tick(Press(1, new Vector2(0f, 2f), enemy));

        units.SetPosition(enemy, new Vector2(0f, 6f));
        units.SetDistance(enemy, 5.5f);
        time = 0.3f;
        PlayerOrderOutput output = Tick(default);

        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.CastWhenInRange));
        Assert.That(orders.ApproachingSlot, Is.EqualTo(1));
        Assert.That(output.Motor, Is.EqualTo(PlayerMotorRequest.MoveTo));
        Assert.That(output.Destination, Is.EqualTo(new Vector2(0f, 6f)));
    }

    [Test]
    public void HoldingDuringACast_SteersThePausedOrder_ButOnlyAPressDropsTheBufferedCast()
    {
        caster.Set(0, Ability(castTime: 1f));
        caster.Set(1, Ability(bufferable: true));
        Tick(Click(new Vector2(5f, 0f)));
        Tick(Press(0, Vector2.right));
        Tick(Press(1, Vector2.right));

        time = 0.2f;
        Tick(Hold(new Vector2(6f, 1f)));
        Assert.That(orders.HasBufferedCast, Is.True, "Holding the move button keeps the buffered cast.");
        Assert.That(orders.Paused, Is.EqualTo(PlayerOrderKind.Move));

        Tick(Click(new Vector2(-3f, 0f)));
        Assert.That(orders.HasBufferedCast, Is.False);
        Assert.That(caster.LastFailure, Is.EqualTo((1, CastOutcome.Cancelled)));
    }

    [Test]
    public void HoldingTheButton_KeepsAnApproach_ButAClickReplacesIt()
    {
        UnitTarget ally = CreateUnit(UnitTeam.Ally, new Vector2(6f, 0f), distance: 5.5f);
        caster.Set(2, Ability(targeting: AbilityTargeting.Unit, filter: AbilityUnitFilter.Ally, range: 4f));
        Tick(Click(new Vector2(-1f, 0f)));
        Tick(Press(2, new Vector2(6f, 0f), ally));

        time = 0.2f;
        Tick(Hold(new Vector2(-1f, 2f)));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.CastWhenInRange));

        Tick(Click(new Vector2(-1f, 2f)));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Move));
    }

    [Test]
    public void StopDuringAContinueCast_StopsTheMotorAndThePausedMove()
    {
        Tick(Click(new Vector2(5f, 0f)));
        reachedDestination = false;
        caster.Set(0, Ability(castTime: 1f, targeting: AbilityTargeting.None, movement: CastMovement.Continue));
        Tick(Press(0, Vector2.zero));

        PlayerOrderOutput stopped = Tick(new PlayerCommand(Vector2.zero, default, false, false, stopPressed: true));

        Assert.That(stopped.Motor, Is.EqualTo(PlayerMotorRequest.Stop));
        Assert.That(orders.Paused, Is.EqualTo(PlayerOrderKind.Idle));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Casting));
        Assert.That(Tick(default).Motor, Is.EqualTo(PlayerMotorRequest.None), "Nothing moves the player for the rest of the cast.");
    }

    [Test]
    public void ASwingAtATargetOnTopOfThePlayer_CarriesNoStaleAim()
    {
        caster.Set(0, Ability(castTime: 0f));
        Tick(Press(0, new Vector2(-2f, 0f)));
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, Vector2.zero, distance: 0f);

        PlayerOrderOutput output = Tick(Click(Vector2.zero, enemy));

        Assert.That(output.Swing, Is.True);
        Assert.That(output.HasAim, Is.False);
        Assert.That(output.AimDirection, Is.EqualTo(Vector2.zero), "The weapon falls back to the facing instead of an old aim.");
    }

    [Test]
    public void Stun_ClearsEveryOrderEndsTheCastAndStopsTheMotor()
    {
        Tick(Click(new Vector2(5f, 0f)));
        reachedDestination = false;
        caster.Set(0, Ability(castTime: 1f));
        caster.Set(1, Ability(bufferable: true));
        Tick(Press(0, Vector2.right));
        Tick(Press(1, Vector2.right));
        Assert.That(orders.Paused, Is.EqualTo(PlayerOrderKind.Move));

        PlayerOrderOutput output = Tick(default, StatusControls.Stun);

        Assert.That(output.Motor, Is.EqualTo(PlayerMotorRequest.Stop));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Idle));
        Assert.That(orders.Paused, Is.EqualTo(PlayerOrderKind.Idle));
        Assert.That(orders.HasBufferedCast, Is.False);
        Assert.That(caster.Ended, Is.EqualTo(1), "The running cast ends; its cooldown stays spent.");
        Assert.That(Tick(default, StatusControls.Stun).Motor, Is.EqualTo(PlayerMotorRequest.None), "The motor is stopped once.");
    }

    [Test]
    public void Stun_IgnoresCommandsAndFailsCastsWithStunned_ThenThePlayerIsIdle()
    {
        caster.Set(0, Ability());
        Tick(default, StatusControls.Stun);

        PlayerOrderOutput click = Tick(Click(new Vector2(3f, 0f)), StatusControls.Stun);
        Assert.That(click.Motor, Is.EqualTo(PlayerMotorRequest.None));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Idle));

        Tick(Press(0, Vector2.right), StatusControls.Stun);
        Assert.That(caster.Started, Is.Zero);
        Assert.That(caster.LastFailure, Is.EqualTo((0, CastOutcome.Stunned)));

        Assert.That(Tick(default).Motor, Is.EqualTo(PlayerMotorRequest.None), "The order that was running before the stun does not come back.");
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Idle));
        Tick(Click(new Vector2(3f, 0f)));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Move), "Orders work again once the stun ends.");
    }

    [Test]
    public void Stun_StopsAutoAttacks()
    {
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(0.2f, 0f), distance: 0.1f);
        Assert.That(Tick(Click(new Vector2(0.2f, 0f), enemy)).Swing, Is.True);

        Assert.That(Tick(default, StatusControls.Stun).Swing, Is.False);
        Assert.That(Tick(Click(new Vector2(0.2f, 0f), enemy), StatusControls.Stun).Swing, Is.False);
    }

    [Test]
    public void Silence_EndsARunningCast_AndResumesThePausedOrder()
    {
        Tick(Click(new Vector2(5f, 0f)));
        reachedDestination = false;
        caster.Set(0, Ability(castTime: 1f));
        caster.Set(1, Ability(castTime: 0.5f, bufferable: true));
        Tick(Press(0, Vector2.right));
        Tick(Press(1, Vector2.up));

        PlayerOrderOutput output = Tick(default, StatusControls.Silence);

        Assert.That(caster.Ended, Is.EqualTo(1));
        Assert.That(orders.HasBufferedCast, Is.False);
        Assert.That(caster.LastFailure, Is.EqualTo((1, CastOutcome.Cancelled)));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Move));
        Assert.That(output.Motor, Is.EqualTo(PlayerMotorRequest.MoveTo));
        Assert.That(output.Destination, Is.EqualTo(new Vector2(5f, 0f)));
    }

    [Test]
    public void Silence_FailsCastsWithSilenced_ButAutoAttacksGoOn()
    {
        caster.Set(0, Ability());
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(0.2f, 0f), distance: 0.1f);

        Assert.That(Tick(Click(new Vector2(0.2f, 0f), enemy), StatusControls.Silence).Swing, Is.True);
        Tick(Press(0, Vector2.right), StatusControls.Silence);

        Assert.That(caster.Started, Is.Zero);
        Assert.That(caster.LastFailure, Is.EqualTo((0, CastOutcome.Silenced)));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Attack));
    }

    [Test]
    public void Silence_StopsAnApproachFromCastingOnceInRange()
    {
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(6f, 0f), distance: 5.5f);
        caster.Set(3, Ability(targeting: AbilityTargeting.Unit, range: 3f));
        Tick(Press(3, new Vector2(6f, 0f), enemy));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.CastWhenInRange));

        units.SetDistance(enemy, 1f);
        Tick(default, StatusControls.Silence);

        Assert.That(caster.Started, Is.Zero);
        Assert.That(caster.LastFailure, Is.EqualTo((3, CastOutcome.Silenced)));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Idle));
    }

    [Test]
    public void Root_KeepsTheMoveOrder_AndFailsOnlyCastsThatMoveTheCaster()
    {
        Tick(Click(new Vector2(5f, 0f)), StatusControls.Root);
        reachedDestination = false;
        for (int i = 0; i < 10; i++)
        {
            time += 0.1f;
            Tick(default, StatusControls.Root);
        }

        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Move), "The motor does not stall while rooted, so the order waits.");

        caster.Set(0, Ability(movement: CastMovement.Ability));
        caster.Set(1, Ability(movement: CastMovement.Stop));
        Tick(Press(0, Vector2.right), StatusControls.Root);
        Assert.That(caster.LastFailure, Is.EqualTo((0, CastOutcome.Rooted)));
        Tick(Press(1, Vector2.right), StatusControls.Root);
        Assert.That(caster.Started, Is.EqualTo(1));
    }

    private PlayerOrderOutput Tick(PlayerCommand command)
    {
        return orders.Tick(command, new PlayerOrderContext(position, time, reachedDestination, stalledTime, AttackRange));
    }

    private PlayerOrderOutput Tick(PlayerCommand command, StatusControls controls)
    {
        return orders.Tick(command, new PlayerOrderContext(position, time, reachedDestination, stalledTime, AttackRange, controls));
    }

    private static PlayerCommand Click(Vector2 point, UnitTarget target = default)
    {
        return new PlayerCommand(point, target, movePressed: true, moveHeld: true, stopPressed: false);
    }

    private static PlayerCommand Hold(Vector2 point, UnitTarget target = default)
    {
        return new PlayerCommand(point, target, movePressed: false, moveHeld: true, stopPressed: false);
    }

    private static PlayerCommand Press(int slot, Vector2 point, UnitTarget target = default)
    {
        return new PlayerCommand(point, target, movePressed: false, moveHeld: false, stopPressed: false, abilitySlot: slot);
    }

    private AbilityDefinition Ability(
        float castTime = 0f,
        AbilityTargeting targeting = AbilityTargeting.Direction,
        AbilityUnitFilter filter = AbilityUnitFilter.Enemy,
        float range = 3f,
        CastMovement movement = CastMovement.Stop,
        bool bufferable = false)
    {
        return Track(AbilityDefinition.Create("Test", 1f, castTime, targeting, filter, range, movement, bufferable));
    }

    private UnitTarget CreateUnit(UnitTeam team, Vector2 unitPosition, float distance)
    {
        GameObject unit = Track(new GameObject($"{team}Unit"));
        UnitTarget target = new(unit.AddComponent<Health>(), null, team);
        units.Add(target, unitPosition, distance);
        return target;
    }

    private T Track<T>(T created) where T : Object
    {
        createdObjects.Add(created);
        return created;
    }

    private sealed class FakeCaster : IAbilityCaster
    {
        private readonly AbilityDefinition[] slots = new AbilityDefinition[AbilitySlots.Count];

        public CastOutcome? Failure { get; set; }
        public int Started { get; private set; }
        public int Ended { get; private set; }
        public int FailureCount { get; private set; }
        public CastAim LastAim { get; private set; }
        public (int, CastOutcome) LastFailure { get; private set; }

        public void Set(int slot, AbilityDefinition ability)
        {
            slots[slot] = ability;
        }

        public AbilityDefinition GetAbility(int slot)
        {
            return slots[slot];
        }

        public bool CanCast(int slot, out CastOutcome failure)
        {
            if (slots[slot] == null)
            {
                failure = CastOutcome.EmptySlot;
                return false;
            }

            failure = Failure ?? CastOutcome.Started;
            return Failure == null;
        }

        public CastOutcome TryCast(int slot, in CastAim aim)
        {
            if (!CanCast(slot, out CastOutcome failure))
            {
                ReportFailure(slot, failure);
                return failure;
            }

            Started++;
            LastAim = aim;
            return CastOutcome.Started;
        }

        public void EndCast(int slot)
        {
            Ended++;
        }

        public void ReportFailure(int slot, CastOutcome reason)
        {
            FailureCount++;
            LastFailure = (slot, reason);
        }
    }

    private sealed class FakeUnits : IUnitQueries
    {
        private readonly Dictionary<Health, UnitState> states = new();

        public void Add(UnitTarget target, Vector2 unitPosition, float distance)
        {
            states[target.Health] = new UnitState { Alive = true, Position = unitPosition, Distance = distance };
        }

        public void SetAlive(UnitTarget target, bool alive)
        {
            states[target.Health].Alive = alive;
        }

        public void SetPosition(UnitTarget target, Vector2 unitPosition)
        {
            states[target.Health].Position = unitPosition;
        }

        public void SetDistance(UnitTarget target, float distance)
        {
            states[target.Health].Distance = distance;
        }

        public bool IsAlive(UnitTarget target)
        {
            return target.Health != null && states.TryGetValue(target.Health, out UnitState state) && state.Alive;
        }

        public Vector2 PositionOf(UnitTarget target)
        {
            return states[target.Health].Position;
        }

        public float DistanceTo(UnitTarget target)
        {
            return states[target.Health].Distance;
        }

        private sealed class UnitState
        {
            public bool Alive;
            public Vector2 Position;
            public float Distance;
        }
    }
}
