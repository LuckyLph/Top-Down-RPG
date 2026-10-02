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
    private PlayerOrders orders;
    private float time;
    private Vector2 position;
    private bool reachedDestination;
    private float stalledTime;

    [SetUp]
    public void SetUp()
    {
        settings = Track(TestPlayerControlSettings.Create(stuckTimeout: 0.75f, holdReevaluateInterval: 0.15f, repathInterval: 0.5f, targetMoveRepathDistance: 0.5f));
        units = new FakeUnits();
        orders = new PlayerOrders(settings, units);
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

    private PlayerOrderOutput Tick(PlayerCommand command)
    {
        return orders.Tick(command, new PlayerOrderContext(position, time, reachedDestination, stalledTime, AttackRange));
    }

    private static PlayerCommand Click(Vector2 point, UnitTarget target = default)
    {
        return new PlayerCommand(point, target, movePressed: true, moveHeld: true, stopPressed: false);
    }

    private static PlayerCommand Hold(Vector2 point, UnitTarget target = default)
    {
        return new PlayerCommand(point, target, movePressed: false, moveHeld: true, stopPressed: false);
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
