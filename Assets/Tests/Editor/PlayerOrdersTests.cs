using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for <see cref="PlayerOrders"/>: move, attack and attack move orders, ability casts with buffering and
/// approach, and crowd control, driven tick by tick against fake units and a fake caster.
/// </summary>
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
    private float weaponRange;

    /// <summary>
    /// Builds the orders with test control settings, fake units and a fake caster, and resets the simulated
    /// player to the origin at time zero with its motor arrived and not stalled.
    /// </summary>
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
        weaponRange = AttackRange;
    }

    /// <summary>
    /// Destroys the settings, abilities and unit objects the test created.
    /// </summary>
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

    /// <summary>
    /// A click on empty ground issues a move order that sends the motor to the clicked point without swinging.
    /// </summary>
    [Test]
    public void ClickOnTheGround_MovesToThePointer()
    {
        PlayerOrderOutput output = Tick(Click(new Vector2(3f, 1f)));

        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Move));
        Assert.That(output.Motor, Is.EqualTo(PlayerMotorRequest.MoveTo));
        Assert.That(output.Destination, Is.EqualTo(new Vector2(3f, 1f)));
        Assert.That(output.Swing, Is.False);
    }

    /// <summary>
    /// A move order ignores the arrival flag left over from the previous path on the tick it is issued, and goes
    /// idle with a stop request once the motor reports arriving on the new path.
    /// </summary>
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

    /// <summary>
    /// A move order whose motor has stalled for the configured stuck timeout stops and goes idle, but not before.
    /// </summary>
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

    /// <summary>
    /// Clicking an ally is a plain move to the clicked point, not an attack.
    /// </summary>
    [Test]
    public void ClickOnAnAlly_MovesToTheClickedPoint()
    {
        UnitTarget ally = CreateUnit(UnitTeam.Ally, new Vector2(2f, 0f), distance: 0.1f);

        PlayerOrderOutput output = Tick(Click(new Vector2(2f, 0.2f), ally));

        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Move));
        Assert.That(output.Destination, Is.EqualTo(new Vector2(2f, 0.2f)));
    }

    /// <summary>
    /// Clicking a dead enemy is a plain move, not an attack.
    /// </summary>
    [Test]
    public void ClickOnADeadEnemy_MovesToTheClickedPoint()
    {
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(2f, 0f), distance: 1.5f);
        units.SetAlive(enemy, false);

        Tick(Click(new Vector2(2f, 0f), enemy));

        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Move));
    }

    /// <summary>
    /// Clicking a living enemy out of attack range starts an attack order that paths to the enemy's position
    /// rather than the clicked point, without swinging yet.
    /// </summary>
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

    /// <summary>
    /// A chase keeps its path while the target moves less than the repath distance, repaths early once it moves
    /// further, and repaths at the repath interval even when the target stands still.
    /// </summary>
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

    /// <summary>
    /// Reaching attack range stops the motor once, aims at the target, and requests a swing on every tick after
    /// that while the attack order stays active.
    /// </summary>
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

    /// <summary>
    /// Clicking an enemy already in range stops, aims and swings on the same tick.
    /// </summary>
    [Test]
    public void ClickOnAnEnemyAlreadyInRange_SwingsAtOnce()
    {
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(-0.4f, 0f), distance: 0.1f);

        PlayerOrderOutput output = Tick(Click(new Vector2(-0.4f, 0f), enemy));

        Assert.That(output.Motor, Is.EqualTo(PlayerMotorRequest.Stop));
        Assert.That(output.AimDirection, Is.EqualTo(Vector2.left));
        Assert.That(output.Swing, Is.True);
    }

    /// <summary>
    /// A target that moves out of range, however far, is chased again instead of the attack order being dropped.
    /// </summary>
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

    /// <summary>
    /// The attack order goes idle and stops the motor once its target dies.
    /// </summary>
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

    /// <summary>
    /// The stop command cancels a running order and stops the motor.
    /// </summary>
    [Test]
    public void Stop_CancelsTheCurrentOrder()
    {
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(4f, 0f), distance: 3.5f);
        Tick(Click(new Vector2(4f, 0f), enemy));

        PlayerOrderOutput output = Tick(new PlayerCommand(Vector2.zero, default, false, false, stopPressed: true));

        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Idle));
        Assert.That(output.Motor, Is.EqualTo(PlayerMotorRequest.Stop));
    }

    /// <summary>
    /// A new click replaces a running attack order on the same tick, so the player stops swinging and moves.
    /// </summary>
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

    /// <summary>
    /// Holding the move button re-evaluates the cursor only once per hold re-evaluate interval, moving to the new
    /// point and switching to an attack when the cursor is over an enemy.
    /// </summary>
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

    /// <summary>
    /// Holding the move button over the enemy already being attacked keeps the current chase instead of
    /// restarting it with a new path.
    /// </summary>
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

    /// <summary>
    /// An attack move with no enemy within the acquire range walks to its point like a move, ignoring the motor's
    /// stale arrival on the tick it is issued, and goes idle once the motor arrives.
    /// </summary>
    [Test]
    public void AttackMove_WithNoEnemyInRange_WalksToItsPoint_AndEndsOnArrival()
    {
        CreateUnit(UnitTeam.Enemy, new Vector2(10f, 0f), distance: 9.5f);

        PlayerOrderOutput issued = Tick(AttackMove(new Vector2(5f, 0f)));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.AttackMove));
        Assert.That(issued.Motor, Is.EqualTo(PlayerMotorRequest.MoveTo));
        Assert.That(issued.Destination, Is.EqualTo(new Vector2(5f, 0f)));
        Assert.That(issued.Swing, Is.False);
        Assert.That(orders.AttackTarget.Exists, Is.False, "An enemy beyond the acquire range is not engaged.");

        reachedDestination = false;
        Assert.That(Tick(default).Motor, Is.EqualTo(PlayerMotorRequest.None));

        reachedDestination = true;
        PlayerOrderOutput arrived = Tick(default);
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Idle));
        Assert.That(arrived.Motor, Is.EqualTo(PlayerMotorRequest.Stop));
    }

    /// <summary>
    /// An attack move engages the living enemy within the acquire range that is closest to its point, ignoring
    /// allies and enemies beyond the range even when they are closer to the point; it chases that enemy, then stops
    /// and swings at it in weapon range.
    /// </summary>
    [Test]
    public void AttackMove_EngagesTheEnemyInRangeClosestToItsPoint()
    {
        CreateUnit(UnitTeam.Enemy, new Vector2(-1f, 0f), distance: 0.8f);
        UnitTarget nearPoint = CreateUnit(UnitTeam.Enemy, new Vector2(2f, 0f), distance: 1.5f);
        CreateUnit(UnitTeam.Enemy, new Vector2(4.9f, 0f), distance: 4f);
        CreateUnit(UnitTeam.Ally, new Vector2(4.5f, 0f), distance: 0.1f);

        PlayerOrderOutput issued = Tick(AttackMove(new Vector2(5f, 0f)));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.AttackMove));
        Assert.That(orders.AttackTarget.IsSameUnit(nearPoint), Is.True);
        Assert.That(issued.Motor, Is.EqualTo(PlayerMotorRequest.MoveTo));
        Assert.That(issued.Destination, Is.EqualTo(new Vector2(2f, 0f)), "It chases the enemy, not the point.");
        Assert.That(issued.Swing, Is.False);

        units.SetDistance(nearPoint, AttackRange);
        PlayerOrderOutput inRange = Tick(default);
        Assert.That(inRange.Motor, Is.EqualTo(PlayerMotorRequest.Stop));
        Assert.That(inRange.AimDirection, Is.EqualTo(Vector2.right));
        Assert.That(inRange.Swing, Is.True);
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.AttackMove));
    }

    /// <summary>
    /// With a long-range weapon (a bow's 5) an attack order chases a farther enemy only until it is within that range,
    /// then stops and swings, and an attack move takes an enemy within the weapon's range though it lies past the
    /// acquire range.
    /// </summary>
    [Test]
    public void ALongRangeWeapon_StopsChasingAtItsRange_AndAttackMovesAcquireWithinIt()
    {
        weaponRange = 5f;
        UnitTarget far = CreateUnit(UnitTeam.Enemy, new Vector2(8f, 0f), distance: 7.5f);
        PlayerOrderOutput chasing = Tick(Click(new Vector2(8f, 0f), far));
        Assert.That(chasing.Motor, Is.EqualTo(PlayerMotorRequest.MoveTo));
        Assert.That(chasing.Swing, Is.False);

        units.SetDistance(far, 5f);
        PlayerOrderOutput inRange = Tick(default);
        Assert.That(inRange.Motor, Is.EqualTo(PlayerMotorRequest.Stop), "It stops chasing at the weapon's range.");
        Assert.That(inRange.Swing, Is.True);

        Tick(new PlayerCommand(Vector2.zero, default, movePressed: false, moveHeld: false, stopPressed: true));
        units.SetDistance(far, 4.5f);
        Assert.That(4.5f, Is.GreaterThan(settings.AttackMoveAcquireRange));
        PlayerOrderOutput attackMove = Tick(AttackMove(new Vector2(10f, 0f)));
        Assert.That(orders.AttackTarget.IsSameUnit(far), Is.True, "An attack move acquires within the weapon's longer range.");
        Assert.That(attackMove.Swing, Is.True);
    }

    /// <summary>
    /// When the engaged enemy dies the attack move walks on to its point at once, ignoring the motor's stale
    /// arrival, and engages the next enemy that comes within the acquire range on the way.
    /// </summary>
    [Test]
    public void AttackMove_AfterItsTargetDies_WalksOn_AndTakesTheNextEnemyItMeets()
    {
        UnitTarget first = CreateUnit(UnitTeam.Enemy, new Vector2(1f, 0f), distance: AttackRange);
        Assert.That(Tick(AttackMove(new Vector2(6f, 0f))).Swing, Is.True);

        units.SetAlive(first, false);
        reachedDestination = true;
        PlayerOrderOutput walkingOn = Tick(default);
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.AttackMove));
        Assert.That(walkingOn.Motor, Is.EqualTo(PlayerMotorRequest.MoveTo));
        Assert.That(walkingOn.Destination, Is.EqualTo(new Vector2(6f, 0f)));
        Assert.That(walkingOn.Swing, Is.False);

        reachedDestination = false;
        Assert.That(Tick(default).Motor, Is.EqualTo(PlayerMotorRequest.None));

        UnitTarget second = CreateUnit(UnitTeam.Enemy, new Vector2(4f, 0f), distance: 2f);
        PlayerOrderOutput engaged = Tick(default);
        Assert.That(orders.AttackTarget.IsSameUnit(second), Is.True);
        Assert.That(engaged.Motor, Is.EqualTo(PlayerMotorRequest.MoveTo));
        Assert.That(engaged.Destination, Is.EqualTo(new Vector2(4f, 0f)));
    }

    /// <summary>
    /// Stop ends an attack move, and a click replaces it like any other order.
    /// </summary>
    [Test]
    public void AttackMove_EndsOnStop_AndIsReplacedByAClick()
    {
        Tick(AttackMove(new Vector2(5f, 0f)));

        PlayerOrderOutput stopped = Tick(new PlayerCommand(Vector2.zero, default, false, false, stopPressed: true));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Idle));
        Assert.That(stopped.Motor, Is.EqualTo(PlayerMotorRequest.Stop));

        Tick(AttackMove(new Vector2(5f, 0f)));
        PlayerOrderOutput clicked = Tick(Click(new Vector2(-3f, 0f)));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Move));
        Assert.That(clicked.Destination, Is.EqualTo(new Vector2(-3f, 0f)));
    }

    /// <summary>
    /// An attack move given during a cast waits as the paused order; in a cast that lets the caster move it takes
    /// its target without swinging, and it attacks once the cast ends.
    /// </summary>
    [Test]
    public void AttackMove_DuringACast_IsPaused_AndSwingsOnlyAfterTheCast()
    {
        caster.Set(0, Ability(castTime: 1f, movement: CastMovement.Continue));
        Tick(Press(0, Vector2.right));
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(0.2f, 0f), distance: AttackRange);

        PlayerOrderOutput duringCast = Tick(AttackMove(new Vector2(5f, 0f)));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Casting));
        Assert.That(orders.Paused, Is.EqualTo(PlayerOrderKind.AttackMove));
        Assert.That(duringCast.Swing, Is.False, "A paused attack move never swings.");

        time = 1f;
        PlayerOrderOutput afterCast = Tick(default);
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.AttackMove));
        Assert.That(orders.AttackTarget.IsSameUnit(enemy), Is.True);
        Assert.That(afterCast.Swing, Is.True);
    }

    /// <summary>
    /// Empty commands, as sent while input is disabled, leave the running move order untouched.
    /// </summary>
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

    /// <summary>
    /// <see cref="PlayerOrders.Clear"/> drops the order and its target and the next tick makes no motor request.
    /// </summary>
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

    /// <summary>
    /// Ticking through move, attack and empty commands allocates no managed memory.
    /// </summary>
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

    /// <summary>
    /// An ability with no cast time starts and ends on the press tick, turns the player toward its aim, and the
    /// running move order resumes with a fresh path.
    /// </summary>
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

    /// <summary>
    /// A cast with a cast time pauses the running order, stops the motor and holds the aim until the cast time
    /// runs out, then ends the cast and resumes the paused order with facing back on movement.
    /// </summary>
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

    /// <summary>
    /// A <see cref="CastMovement.Continue"/> cast keeps the paused chase pathing toward its target but never
    /// swings, even once the target is in range.
    /// </summary>
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

    /// <summary>
    /// A <see cref="CastMovement.Ability"/> cast stops the player once and then keeps it still, like a stopping
    /// cast, since abilities do not move the caster yet.
    /// </summary>
    [Test]
    public void AbilityMovement_StandsStillForNow()
    {
        Tick(Click(new Vector2(5f, 0f)));
        caster.Set(0, Ability(castTime: 0.3f, movement: CastMovement.Ability));

        Assert.That(Tick(Press(0, new Vector2(1f, 0f))).Motor, Is.EqualTo(PlayerMotorRequest.Stop));
        time = 0.1f;
        Assert.That(Tick(default).Motor, Is.EqualTo(PlayerMotorRequest.None));
    }

    /// <summary>
    /// A point-targeted cast aims at the cursor clamped to the ability's range from the player, and uses a point
    /// inside the range unchanged.
    /// </summary>
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

    /// <summary>
    /// A unit-targeted cast targets the unit under the cursor only when it passes the ability's enemy, ally or
    /// any filter (the caster counts as an ally), and fails with <see cref="CastOutcome.NoTarget"/> otherwise.
    /// </summary>
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

    /// <summary>
    /// A press the caster refuses (on cooldown, empty slot) is reported with its reason and leaves the running
    /// order and the motor alone.
    /// </summary>
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

    /// <summary>
    /// Pressing a non-bufferable ability during a cast fails with <see cref="CastOutcome.CastInProgress"/> and is
    /// not buffered.
    /// </summary>
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

    /// <summary>
    /// A bufferable press during a cast fires with the aim taken at the press as soon as the running cast ends,
    /// keeping the move order paused until the buffered cast is over too.
    /// </summary>
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

    /// <summary>
    /// A second bufferable press replaces the buffered one, which is reported as cancelled.
    /// </summary>
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

    /// <summary>
    /// A buffered cast still waiting after the cast buffer window is dropped and reported as expired, so it does
    /// not fire when the running cast ends.
    /// </summary>
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

    /// <summary>
    /// A new click or the stop command drops the buffered cast and reports it.
    /// </summary>
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

    /// <summary>
    /// A buffered unit-targeted cast whose target died while it waited fails with
    /// <see cref="CastOutcome.NoTarget"/> instead of firing, and the player ends idle.
    /// </summary>
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

    /// <summary>
    /// A click during a cast replaces the paused order without moving the player, and the new move starts once
    /// the cast ends.
    /// </summary>
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

    /// <summary>
    /// Stop during a cast clears the paused order but lets the started cast run to its end, after which the
    /// player is idle.
    /// </summary>
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

    /// <summary>
    /// A unit-targeted cast on a target beyond the ability's range becomes a
    /// <see cref="PlayerOrderKind.CastWhenInRange"/> approach that walks toward the target, casts once in range,
    /// and ends idle when the cast is over.
    /// </summary>
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

    /// <summary>
    /// An approach whose target dies stops the motor, goes idle and reports <see cref="CastOutcome.NoTarget"/>.
    /// </summary>
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

    /// <summary>
    /// <see cref="PlayerOrders.Clear"/> ends a running cast, reports the buffered cast as cancelled and leaves
    /// the player idle.
    /// </summary>
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

    /// <summary>
    /// Starting instant casts, buffering a cast and firing it when the running one ends allocates no managed
    /// memory.
    /// </summary>
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

    /// <summary>
    /// A buffered <see cref="CastMovement.Continue"/> cast that fires after a stopping cast repaths the paused
    /// move instead of letting the stopped motor's stale arrival end it.
    /// </summary>
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

    /// <summary>
    /// A buffered instant cast fires and ends on the tick the running cast ends, and the paused move resumes on
    /// that same tick.
    /// </summary>
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

    /// <summary>
    /// A buffered unit-targeted cast whose target moved out of range by the time it fires becomes an approach
    /// that paths to the target on that tick.
    /// </summary>
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

    /// <summary>
    /// Holding the move button during a cast steers the paused move and keeps the buffered cast, while a fresh
    /// click drops the buffered cast.
    /// </summary>
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

    /// <summary>
    /// Holding the move button keeps a cast-when-in-range approach running, while a fresh click replaces it with
    /// a move.
    /// </summary>
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

    /// <summary>
    /// Stop during a <see cref="CastMovement.Continue"/> cast stops the motor once and clears the paused move,
    /// while the cast itself keeps running.
    /// </summary>
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

    /// <summary>
    /// A swing at a target at the player's own position carries no aim and a zero direction, rather than the
    /// aim left over from an earlier cast, so the weapon falls back to the facing.
    /// </summary>
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

    /// <summary>
    /// A stun cancels the running cast, drops the buffered cast and the current and paused orders, and stops the
    /// motor once.
    /// </summary>
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
        Assert.That(caster.Cancelled, Is.EqualTo(1), "A stunned cast is cancelled, so its effect never lands.");
        Assert.That(Tick(default, StatusControls.Stun).Motor, Is.EqualTo(PlayerMotorRequest.None), "The motor is stopped once.");
    }

    /// <summary>
    /// While stunned, clicks are ignored and ability presses fail with <see cref="CastOutcome.Stunned"/>; once
    /// the stun ends the player is idle and takes new orders.
    /// </summary>
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

    /// <summary>
    /// A stunned player requests no swings, even when clicking an enemy in range.
    /// </summary>
    [Test]
    public void Stun_StopsAutoAttacks()
    {
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(0.2f, 0f), distance: 0.1f);
        Assert.That(Tick(Click(new Vector2(0.2f, 0f), enemy)).Swing, Is.True);

        Assert.That(Tick(default, StatusControls.Stun).Swing, Is.False);
        Assert.That(Tick(Click(new Vector2(0.2f, 0f), enemy), StatusControls.Stun).Swing, Is.False);
    }

    /// <summary>
    /// A silence cancels the running cast and drops the buffered one, and the paused move resumes at once.
    /// </summary>
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
        Assert.That(caster.Cancelled, Is.EqualTo(1), "A silenced cast is cancelled, so its effect never lands.");
        Assert.That(orders.HasBufferedCast, Is.False);
        Assert.That(caster.LastFailure, Is.EqualTo((1, CastOutcome.Cancelled)));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Move));
        Assert.That(output.Motor, Is.EqualTo(PlayerMotorRequest.MoveTo));
        Assert.That(output.Destination, Is.EqualTo(new Vector2(5f, 0f)));
    }

    /// <summary>
    /// While silenced, ability presses fail with <see cref="CastOutcome.Silenced"/> but attack orders still
    /// swing.
    /// </summary>
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

    /// <summary>
    /// An approach that reaches range while silenced does not cast; it reports
    /// <see cref="CastOutcome.Silenced"/> and goes idle.
    /// </summary>
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

    /// <summary>
    /// While rooted, a move order keeps waiting for the motor, abilities that move the caster
    /// (<see cref="CastMovement.Ability"/>) fail with <see cref="CastOutcome.Rooted"/>, and stopping casts
    /// still start.
    /// </summary>
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

    /// <summary>
    /// Casts that run their full cast time, instant casts, and casts left running after a stop complete, while a
    /// cast cut short by <see cref="PlayerOrders.Clear"/> is cancelled.
    /// </summary>
    [Test]
    public void ACastThatRunsItsCastTime_Completes_AndOneThatIsCleared_IsCancelled()
    {
        caster.Set(0, Ability(castTime: 0.5f));
        caster.Set(1, Ability(castTime: 0f));
        Tick(Press(0, Vector2.right));
        time = 0.5f;
        Tick(default);
        Assert.That(caster.Completed, Is.EqualTo(1));
        Assert.That(caster.Cancelled, Is.Zero);

        Tick(Press(1, Vector2.right));
        Assert.That(caster.Completed, Is.EqualTo(2), "An instant cast completes on the tick it starts.");

        time = 1f;
        Tick(Press(0, Vector2.right));
        orders.Clear();
        Assert.That(caster.Completed, Is.EqualTo(2));
        Assert.That(caster.Cancelled, Is.EqualTo(1), "Death or a teleport cancels the cast.");

        time = 2f;
        Tick(Press(0, Vector2.right));
        Tick(new PlayerCommand(Vector2.zero, default, false, false, stopPressed: true));
        time = 2.5f;
        Tick(default);
        Assert.That(caster.Completed, Is.EqualTo(3), "Stop lets the started cast run to its end.");
    }

    /// <summary>
    /// Each tick reports the order its command gave, which the weapon's cancel rules read: a ground click, a held
    /// button steering to a new point and Stop are moves; a click on an enemy, an attack move and an ability press that
    /// starts a cast are other orders; clicking or holding over the enemy already attacked, an empty command and a
    /// failed press give none.
    /// </summary>
    [Test]
    public void EachTick_ReportsTheOrderItsCommandGave()
    {
        UnitTarget enemy = CreateUnit(UnitTeam.Enemy, new Vector2(0.2f, 0f), distance: 0.1f);
        caster.Set(0, Ability());

        Assert.That(Tick(Click(new Vector2(3f, 1f))).Issued, Is.EqualTo(PlayerOrderIssue.Move));
        time = 0.2f;
        Assert.That(Tick(Hold(new Vector2(4f, 1f))).Issued, Is.EqualTo(PlayerOrderIssue.Move), "A held button steering to a new point.");
        Assert.That(Tick(default).Issued, Is.EqualTo(PlayerOrderIssue.None));
        Assert.That(Tick(Click(new Vector2(0.2f, 0f), enemy)).Issued, Is.EqualTo(PlayerOrderIssue.Other));
        Assert.That(Tick(Click(new Vector2(0.2f, 0f), enemy)).Issued, Is.EqualTo(PlayerOrderIssue.None), "The same attack again gives no order.");
        time = 0.4f;
        Assert.That(Tick(Hold(new Vector2(0.2f, 0f), enemy)).Issued, Is.EqualTo(PlayerOrderIssue.None));
        Assert.That(Tick(new PlayerCommand(Vector2.zero, default, false, false, stopPressed: true)).Issued, Is.EqualTo(PlayerOrderIssue.Move));
        Assert.That(Tick(AttackMove(new Vector2(5f, 0f))).Issued, Is.EqualTo(PlayerOrderIssue.Other));
        Assert.That(Tick(Press(0, Vector2.right)).Issued, Is.EqualTo(PlayerOrderIssue.Other), "A press that starts a cast.");
        Assert.That(Tick(Press(1, Vector2.right)).Issued, Is.EqualTo(PlayerOrderIssue.None), "A press on an empty slot gives no order.");
    }

    /// <summary>
    /// Ticks the orders with <paramref name="command"/> and the test's current position, time, motor state and
    /// attack range, with no crowd control.
    /// </summary>
    private PlayerOrderOutput Tick(PlayerCommand command)
    {
        return orders.Tick(command, new PlayerOrderContext(position, time, reachedDestination, stalledTime, weaponRange));
    }

    /// <summary>
    /// Ticks the orders like <see cref="Tick(PlayerCommand)"/> but under the crowd control in
    /// <paramref name="controls"/>.
    /// </summary>
    private PlayerOrderOutput Tick(PlayerCommand command, StatusControls controls)
    {
        return orders.Tick(command, new PlayerOrderContext(position, time, reachedDestination, stalledTime, weaponRange, controls));
    }

    /// <summary>
    /// A command for the frame the move button is pressed at <paramref name="point"/>, over
    /// <paramref name="target"/> if any.
    /// </summary>
    private static PlayerCommand Click(Vector2 point, UnitTarget target = default)
    {
        return new PlayerCommand(point, target, movePressed: true, moveHeld: true, stopPressed: false);
    }

    /// <summary>
    /// A command for a frame the move button is still held with the cursor at <paramref name="point"/>, over
    /// <paramref name="target"/> if any.
    /// </summary>
    private static PlayerCommand Hold(Vector2 point, UnitTarget target = default)
    {
        return new PlayerCommand(point, target, movePressed: false, moveHeld: true, stopPressed: false);
    }

    /// <summary>
    /// A command pressing ability <paramref name="slot"/> with the cursor at <paramref name="point"/>, over
    /// <paramref name="target"/> if any, and the move button up.
    /// </summary>
    private static PlayerCommand Press(int slot, Vector2 point, UnitTarget target = default)
    {
        return new PlayerCommand(point, target, movePressed: false, moveHeld: false, stopPressed: false, abilitySlot: slot);
    }

    /// <summary>
    /// A command for the frame the attack-move key is pressed with the cursor at <paramref name="point"/> and the
    /// move button up.
    /// </summary>
    private static PlayerCommand AttackMove(Vector2 point)
    {
        return new PlayerCommand(point, default, movePressed: false, moveHeld: false, stopPressed: false, attackMovePressed: true);
    }

    /// <summary>
    /// Creates a tracked test ability with a one-second cooldown and the given cast time, targeting, filter,
    /// range, cast movement and bufferability.
    /// </summary>
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

    /// <summary>
    /// Creates a living unit with a <see cref="Health"/> and no collider, registered with the fake unit queries
    /// at <paramref name="unitPosition"/> and <paramref name="distance"/> from the player.
    /// </summary>
    private UnitTarget CreateUnit(UnitTeam team, Vector2 unitPosition, float distance)
    {
        GameObject unit = Track(new GameObject($"{team}Unit"));
        UnitTarget target = new(unit.AddComponent<Health>(), null, team);
        units.Add(target, unitPosition, distance);
        return target;
    }

    /// <summary>
    /// Records <paramref name="created"/> for destruction in <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T created) where T : Object
    {
        createdObjects.Add(created);
        return created;
    }

    /// <summary>
    /// An <see cref="IAbilityCaster"/> that holds abilities per slot, succeeds unless an empty slot or a forced
    /// <see cref="Failure"/> says otherwise, and counts starts, completions, cancellations and failures.
    /// </summary>
    private sealed class FakeCaster : IAbilityCaster
    {
        private readonly AbilityDefinition[] slots = new AbilityDefinition[AbilitySlots.Count];

        public CastOutcome? Failure { get; set; }
        public int Started { get; private set; }
        public int Ended { get; private set; }
        public int Completed { get; private set; }
        public int Cancelled { get; private set; }
        public int FailureCount { get; private set; }
        public CastAim LastAim { get; private set; }
        public (int, CastOutcome) LastFailure { get; private set; }

        /// <summary>
        /// Puts <paramref name="ability"/> in <paramref name="slot"/>.
        /// </summary>
        public void Set(int slot, AbilityDefinition ability)
        {
            slots[slot] = ability;
        }

        /// <summary>
        /// Returns the ability set in <paramref name="slot"/>, or null when it is empty.
        /// </summary>
        public AbilityDefinition GetAbility(int slot)
        {
            return slots[slot];
        }

        /// <summary>
        /// False with <see cref="CastOutcome.EmptySlot"/> for an empty slot, false with the forced
        /// <see cref="Failure"/> when one is set, true otherwise.
        /// </summary>
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

        /// <summary>
        /// Counts a start and records <paramref name="aim"/> when <see cref="CanCast"/> allows it; otherwise
        /// reports and returns the failure.
        /// </summary>
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

        /// <summary>
        /// Counts the cast as ended and completed.
        /// </summary>
        public void CompleteCast(int slot)
        {
            Ended++;
            Completed++;
        }

        /// <summary>
        /// Counts the cast as ended and cancelled.
        /// </summary>
        public void CancelCast(int slot)
        {
            Ended++;
            Cancelled++;
        }

        /// <summary>
        /// Counts the failure and records <paramref name="slot"/> and <paramref name="reason"/> as the last one.
        /// </summary>
        public void ReportFailure(int slot, CastOutcome reason)
        {
            FailureCount++;
            LastFailure = (slot, reason);
        }
    }

    /// <summary>
    /// An <see cref="IUnitQueries"/> backed by per-unit state the test sets directly: team, alive, position and
    /// distance from the player, keyed by the unit's <see cref="Health"/>.
    /// </summary>
    private sealed class FakeUnits : IUnitQueries
    {
        private readonly Dictionary<Health, UnitState> states = new();

        /// <summary>
        /// Registers <paramref name="target"/> as alive at <paramref name="unitPosition"/> and
        /// <paramref name="distance"/> from the player.
        /// </summary>
        public void Add(UnitTarget target, Vector2 unitPosition, float distance)
        {
            states[target.Health] = new UnitState { Target = target, Alive = true, Position = unitPosition, Distance = distance };
        }

        /// <summary>
        /// Marks a registered unit alive or dead without touching its <see cref="Health"/>.
        /// </summary>
        public void SetAlive(UnitTarget target, bool alive)
        {
            states[target.Health].Alive = alive;
        }

        /// <summary>
        /// Moves a registered unit to <paramref name="unitPosition"/>; its distance is set separately.
        /// </summary>
        public void SetPosition(UnitTarget target, Vector2 unitPosition)
        {
            states[target.Health].Position = unitPosition;
        }

        /// <summary>
        /// Sets the distance <see cref="DistanceTo"/> reports for a registered unit.
        /// </summary>
        public void SetDistance(UnitTarget target, float distance)
        {
            states[target.Health].Distance = distance;
        }

        /// <summary>
        /// True only for a registered unit marked alive; false for an empty target or an unknown unit.
        /// </summary>
        public bool IsAlive(UnitTarget target)
        {
            return target.Health != null && states.TryGetValue(target.Health, out UnitState state) && state.Alive;
        }

        /// <summary>
        /// Returns the position last set for a registered unit.
        /// </summary>
        public Vector2 PositionOf(UnitTarget target)
        {
            return states[target.Health].Position;
        }

        /// <summary>
        /// Returns the distance last set for a registered unit.
        /// </summary>
        public float DistanceTo(UnitTarget target)
        {
            return states[target.Health].Distance;
        }

        /// <summary>
        /// The registered living enemy within <paramref name="range"/> whose position is closest to
        /// <paramref name="point"/>, or an empty target.
        /// </summary>
        public UnitTarget NearestEnemyInRange(Vector2 point, float range)
        {
            UnitTarget best = default;
            float bestDistance = float.MaxValue;
            foreach (UnitState state in states.Values)
            {
                float distance = (state.Position - point).sqrMagnitude;
                if (state.Target.Team == UnitTeam.Enemy && state.Alive && state.Distance <= range && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = state.Target;
                }
            }

            return best;
        }

        /// <summary>
        /// Mutable per-unit state behind the fake queries.
        /// </summary>
        private sealed class UnitState
        {
            public UnitTarget Target;
            public bool Alive;
            public Vector2 Position;
            public float Distance;
        }
    }
}
