using UnityEngine;

/// <summary>
/// The player's order state machine: Idle, Move, Attack, CastWhenInRange and Casting, plus at most one paused order
/// while a cast runs and at most one buffered cast. Each tick it turns a <see cref="PlayerCommand"/> and the player's
/// state into motor requests, a facing direction and swing requests, and starts casts through the
/// <see cref="IAbilityCaster"/>. Crowd control from statuses arrives in the context: a stun clears every order and
/// ignores commands, a silence ends a running cast and fails casts, a root fails casts that move the caster. No
/// engine lookups: targets are queried through <see cref="IUnitQueries"/>.
/// </summary>
public sealed class PlayerOrders
{
    private const float AimThreshold = 0.0001f;

    private readonly PlayerControlSettings settings;
    private readonly IUnitQueries units;
    private readonly IAbilityCaster caster;

    private Order current;
    private Order paused;
    private bool fresh;
    private bool chasing;
    private Vector2 chaseGoal;
    private float nextRepathTime;
    private float nextHoldEvaluationTime;

    private AbilityDefinition castAbility;
    private CastAim castAim;
    private float castEndTime;

    private bool hasBuffered;
    private int bufferedSlot;
    private CastAim bufferedAim;
    private float bufferedTime;

    private bool wasStunned;
    private bool wasSilenced;

    private PlayerMotorRequest motorRequest;
    private Vector2 destination;
    private bool hasAim;
    private Vector2 aimDirection;
    private bool swing;

    /// <summary>
    /// Creates an idle state machine timed by <paramref name="settings"/>, querying targets through
    /// <paramref name="units"/> and starting casts through <paramref name="caster"/> (null ignores ability presses).
    /// </summary>
    public PlayerOrders(PlayerControlSettings settings, IUnitQueries units, IAbilityCaster caster = null)
    {
        this.settings = settings;
        this.units = units;
        this.caster = caster;
    }

    public PlayerOrderKind Current => current.Kind;
    public PlayerOrderKind Paused => current.Kind == PlayerOrderKind.Casting ? paused.Kind : PlayerOrderKind.Idle;
    public Vector2 MoveDestination => current.Destination;
    public UnitTarget AttackTarget => current.Target;
    public int CastingSlot => current.Kind == PlayerOrderKind.Casting ? current.Slot : -1;
    public int ApproachingSlot => current.Kind == PlayerOrderKind.CastWhenInRange ? current.Slot : -1;
    public bool HasBufferedCast => hasBuffered;
    public int BufferedSlot => hasBuffered ? bufferedSlot : -1;

    /// <summary>
    /// Applies the command (stop, a new order, a throttled re-evaluation while the move button is held, an ability
    /// press), then runs the current order.
    /// </summary>
    public PlayerOrderOutput Tick(in PlayerCommand command, in PlayerOrderContext context)
    {
        motorRequest = PlayerMotorRequest.None;
        hasAim = false;
        swing = false;

        if (ApplyControls(command, context))
        {
            return new PlayerOrderOutput(motorRequest, destination, false, Vector2.zero, false);
        }

        if (command.StopPressed)
        {
            Stop();
        }

        if (command.MovePressed)
        {
            IssueFrom(command, true);
            nextHoldEvaluationTime = context.Time + settings.HoldReevaluateInterval;
        }
        else if (command.MoveHeld && context.Time >= nextHoldEvaluationTime)
        {
            IssueFrom(command, false);
            nextHoldEvaluationTime = context.Time + settings.HoldReevaluateInterval;
        }

        if (command.HasAbility)
        {
            PressAbility(command.AbilitySlot, command, context);
        }

        if (hasBuffered && context.Time - bufferedTime > settings.CastBufferWindow)
        {
            DropBuffered(CastOutcome.Expired);
        }

        RunCurrent(context);
        fresh = false;
        return new PlayerOrderOutput(motorRequest, destination, hasAim, hasAim ? aimDirection : Vector2.zero, swing);
    }

    /// <summary>
    /// Drops every order, ends a running cast and drops a buffered one, without asking the motor for anything (used
    /// on death, teleports and when the player stops being simulated here).
    /// </summary>
    public void Clear()
    {
        if (current.Kind == PlayerOrderKind.Casting && caster != null)
        {
            caster.CancelCast(current.Slot);
        }

        DropBuffered(CastOutcome.Cancelled);
        current = default;
        paused = default;
        chasing = false;
        fresh = false;
    }

    /// <summary>
    /// Reacts to crowd control starting: a stun clears every order (the cast ends) and stops the motor, a silence ends
    /// a running cast and drops a buffered one. Returns true while stunned, when the tick does nothing else and an
    /// ability press only reports <see cref="CastOutcome.Stunned"/>.
    /// </summary>
    private bool ApplyControls(in PlayerCommand command, in PlayerOrderContext context)
    {
        bool stunned = (context.Controls & StatusControls.Stun) != 0;
        bool silenced = (context.Controls & StatusControls.Silence) != 0;
        if (stunned)
        {
            if (!wasStunned)
            {
                Clear();
                motorRequest = PlayerMotorRequest.Stop;
            }

            if (command.HasAbility && caster != null)
            {
                caster.ReportFailure(command.AbilitySlot, CastOutcome.Stunned);
            }
        }
        else if (silenced && !wasSilenced)
        {
            InterruptCast();
        }

        wasStunned = stunned;
        wasSilenced = silenced;
        return stunned;
    }

    /// <summary>
    /// Ends a running cast early (its cooldown stays spent), drops a buffered cast and resumes the paused order.
    /// </summary>
    private void InterruptCast()
    {
        DropBuffered(CastOutcome.Cancelled);
        if (current.Kind != PlayerOrderKind.Casting)
        {
            return;
        }

        if (caster != null)
        {
            caster.CancelCast(current.Slot);
        }

        current = paused;
        paused = default;
        fresh = true;
        chasing = false;
        if (current.Kind == PlayerOrderKind.Idle)
        {
            motorRequest = PlayerMotorRequest.Stop;
        }
    }

    /// <summary>
    /// Why crowd control blocks casting <paramref name="ability"/> right now: stunned, silenced, or rooted for an
    /// ability that moves the caster. <see cref="CastOutcome.Started"/> when nothing blocks it.
    /// </summary>
    private static CastOutcome ControlFailure(AbilityDefinition ability, StatusControls controls)
    {
        if ((controls & StatusControls.Stun) != 0)
        {
            return CastOutcome.Stunned;
        }

        if ((controls & StatusControls.Silence) != 0)
        {
            return CastOutcome.Silenced;
        }

        if ((controls & StatusControls.Root) != 0 && ability.CastMovement == CastMovement.Ability)
        {
            return CastOutcome.Rooted;
        }

        return CastOutcome.Started;
    }

    /// <summary>
    /// Handles the stop key: drops a buffered cast and goes idle. A running cast keeps going, but the order it paused
    /// is dropped and the motor stops.
    /// </summary>
    private void Stop()
    {
        DropBuffered(CastOutcome.Cancelled);
        if (current.Kind == PlayerOrderKind.Casting)
        {
            paused = default;
            motorRequest = PlayerMotorRequest.Stop;
            return;
        }

        BecomeIdle();
    }

    /// <summary>
    /// Gives the order under the cursor. A held button only keeps steering a move or attack: unlike a press, it never
    /// replaces a CastWhenInRange approach or drops a buffered cast.
    /// </summary>
    private void IssueFrom(in PlayerCommand command, bool pressed)
    {
        Order order = OrderFrom(command);
        bool casting = current.Kind == PlayerOrderKind.Casting;
        Order replaced = casting ? paused : current;
        if (order.SameAs(replaced) || (!pressed && replaced.Kind == PlayerOrderKind.CastWhenInRange))
        {
            return;
        }

        if (pressed)
        {
            DropBuffered(CastOutcome.Cancelled);
        }

        if (casting)
        {
            paused = order;
        }
        else
        {
            current = order;
        }

        fresh = true;
        chasing = false;
    }

    /// <summary>
    /// The order a click gives: attack a living enemy under the cursor, otherwise move to the cursor.
    /// </summary>
    private Order OrderFrom(in PlayerCommand command)
    {
        UnitTarget target = command.Target;
        if (target.Team == UnitTeam.Enemy && units.IsAlive(target))
        {
            return Order.Attack(target);
        }

        return Order.Move(command.PointerWorld);
    }

    /// <summary>
    /// Handles an ability press: reports why it cannot cast (caster refusal, crowd control, no valid target), buffers
    /// it while a cast runs when the ability is bufferable (otherwise reports the cast in progress), or begins it.
    /// </summary>
    private void PressAbility(int slot, in PlayerCommand command, in PlayerOrderContext context)
    {
        if (caster == null)
        {
            return;
        }

        if (!caster.CanCast(slot, out CastOutcome failure))
        {
            caster.ReportFailure(slot, failure);
            return;
        }

        AbilityDefinition ability = caster.GetAbility(slot);
        CastOutcome blocked = ControlFailure(ability, context.Controls);
        if (blocked != CastOutcome.Started)
        {
            caster.ReportFailure(slot, blocked);
            return;
        }

        if (!TryAim(ability, command, context, out CastAim aim))
        {
            caster.ReportFailure(slot, CastOutcome.NoTarget);
            return;
        }

        if (current.Kind == PlayerOrderKind.Casting)
        {
            if (!ability.Bufferable)
            {
                caster.ReportFailure(slot, CastOutcome.CastInProgress);
                return;
            }

            DropBuffered(CastOutcome.Cancelled);
            hasBuffered = true;
            bufferedSlot = slot;
            bufferedAim = aim;
            bufferedTime = context.Time;
            return;
        }

        Begin(slot, ability, aim, context, current);
    }

    /// <summary>
    /// Aims <paramref name="ability"/> from the command: at the caster when untargeted, toward the cursor for a
    /// direction, at the cursor clamped to range for a point, at the unit under the cursor for a unit. False when a
    /// unit ability has no living target of a team it accepts.
    /// </summary>
    private bool TryAim(AbilityDefinition ability, in PlayerCommand command, in PlayerOrderContext context, out CastAim aim)
    {
        Vector2 toCursor = command.PointerWorld - context.Position;
        Vector2 direction = toCursor.sqrMagnitude > AimThreshold ? toCursor.normalized : Vector2.zero;
        switch (ability.Targeting)
        {
            case AbilityTargeting.None:
                aim = new CastAim(context.Position, Vector2.zero, default);
                return true;
            case AbilityTargeting.Direction:
                aim = new CastAim(command.PointerWorld, direction, default);
                return true;
            case AbilityTargeting.Point:
                aim = new CastAim(context.Position + Vector2.ClampMagnitude(toCursor, ability.Range), direction, default);
                return true;
            default:
                UnitTarget target = command.Target;
                if (!target.Exists || !ability.Accepts(target.Team) || !units.IsAlive(target))
                {
                    aim = default;
                    return false;
                }

                aim = UnitAim(target, context);
                return true;
        }
    }

    /// <summary>
    /// An aim at <paramref name="target"/>'s current position, with the direction from the player toward it.
    /// </summary>
    private CastAim UnitAim(UnitTarget target, in PlayerOrderContext context)
    {
        Vector2 targetPosition = units.PositionOf(target);
        Vector2 toTarget = targetPosition - context.Position;
        return new CastAim(targetPosition, toTarget.sqrMagnitude > AimThreshold ? toTarget.normalized : Vector2.zero, target);
    }

    /// <summary>
    /// Starts a cast that pauses <paramref name="toPause"/>, or turns an out-of-range unit cast into a
    /// CastWhenInRange order. False when the caster refused the cast (it reported why).
    /// </summary>
    private bool Begin(int slot, AbilityDefinition ability, in CastAim aim, in PlayerOrderContext context, Order toPause)
    {
        if (ability.Targeting == AbilityTargeting.Unit && units.DistanceTo(aim.Target) > ability.Range)
        {
            current = Order.CastWhenInRange(slot, aim.Target);
            paused = default;
            fresh = true;
            chasing = false;
            return true;
        }

        CastOutcome blocked = ControlFailure(ability, context.Controls);
        if (blocked != CastOutcome.Started)
        {
            caster.ReportFailure(slot, blocked);
            return false;
        }

        if (caster.TryCast(slot, aim) != CastOutcome.Started)
        {
            return false;
        }

        paused = toPause;
        current = Order.Casting(slot);
        castAbility = ability;
        castAim = aim;
        castEndTime = context.Time + ability.CastTime;
        if (ability.CastMovement != CastMovement.Continue)
        {
            motorRequest = PlayerMotorRequest.Stop;
            chasing = false;
        }

        return true;
    }

    /// <summary>
    /// Runs one tick of the current order; Idle does nothing.
    /// </summary>
    private void RunCurrent(in PlayerOrderContext context)
    {
        switch (current.Kind)
        {
            case PlayerOrderKind.Move:
                RunMove(ref current, context);
                break;
            case PlayerOrderKind.Attack:
                RunAttack(ref current, context, true);
                break;
            case PlayerOrderKind.CastWhenInRange:
                RunCastWhenInRange(context);
                break;
            case PlayerOrderKind.Casting:
                RunCasting(context);
                break;
        }
    }

    /// <summary>
    /// Paths to a fresh move's destination, then ends <paramref name="order"/> and stops once the motor arrives or
    /// has stalled for the stuck timeout.
    /// </summary>
    private void RunMove(ref Order order, in PlayerOrderContext context)
    {
        if (fresh)
        {
            RequestMoveTo(order.Destination);
            return;
        }

        if (context.ReachedDestination || context.StalledTime >= settings.StuckTimeout)
        {
            order = default;
            motorRequest = PlayerMotorRequest.Stop;
        }
    }

    /// <summary>
    /// Ends <paramref name="order"/> and stops when its target died, chases while it is out of weapon range, and
    /// otherwise stops chasing and, when <paramref name="act"/> is set, aims at it and swings. A paused attack runs
    /// with <paramref name="act"/> false so it only keeps up.
    /// </summary>
    private void RunAttack(ref Order order, in PlayerOrderContext context, bool act)
    {
        if (!units.IsAlive(order.Target))
        {
            order = default;
            motorRequest = PlayerMotorRequest.Stop;
            chasing = false;
            return;
        }

        Vector2 targetPosition = units.PositionOf(order.Target);
        if (units.DistanceTo(order.Target) > context.AttackRange)
        {
            Chase(targetPosition, context);
            return;
        }

        StopChasing();
        if (!act)
        {
            return;
        }

        Vector2 toTarget = targetPosition - context.Position;
        if (toTarget.sqrMagnitude > AimThreshold)
        {
            hasAim = true;
            aimDirection = toTarget.normalized;
        }

        swing = true;
    }

    /// <summary>
    /// Chases the target of a unit cast until it is within the ability's range, then begins the cast. Goes idle when
    /// the slot emptied or the target died (reporting why) or when the cast is refused.
    /// </summary>
    private void RunCastWhenInRange(in PlayerOrderContext context)
    {
        int slot = current.Slot;
        AbilityDefinition ability = caster != null ? caster.GetAbility(slot) : null;
        if (ability == null || !units.IsAlive(current.Target))
        {
            if (caster != null)
            {
                caster.ReportFailure(slot, ability == null ? CastOutcome.EmptySlot : CastOutcome.NoTarget);
            }

            BecomeIdle();
            return;
        }

        if (units.DistanceTo(current.Target) > ability.Range)
        {
            Chase(units.PositionOf(current.Target), context);
            return;
        }

        StopChasing();
        if (!Begin(slot, ability, UnitAim(current.Target, context), context, default))
        {
            BecomeIdle();
            return;
        }

        RunCasting(context);
    }

    /// <summary>
    /// Keeps facing a targeted cast's aim, keeps the paused order moving when the cast allows movement, and finishes
    /// the cast once its cast time has passed.
    /// </summary>
    private void RunCasting(in PlayerOrderContext context)
    {
        if (castAbility.Targeting != AbilityTargeting.None && castAim.Direction.sqrMagnitude > AimThreshold)
        {
            hasAim = true;
            aimDirection = castAim.Direction;
        }

        if (castAbility.CastMovement == CastMovement.Continue)
        {
            RunPausedMovement(context);
        }

        if (context.Time >= castEndTime)
        {
            FinishCast(context);
        }
    }

    /// <summary>
    /// Moves for the paused order during a cast that allows it: a move keeps walking, an attack keeps chasing without
    /// swinging, and a CastWhenInRange approach keeps closing in until in range.
    /// </summary>
    private void RunPausedMovement(in PlayerOrderContext context)
    {
        switch (paused.Kind)
        {
            case PlayerOrderKind.Move:
                RunMove(ref paused, context);
                break;
            case PlayerOrderKind.Attack:
                RunAttack(ref paused, context, false);
                break;
            case PlayerOrderKind.CastWhenInRange:
                AbilityDefinition ability = caster.GetAbility(paused.Slot);
                if (ability != null && units.IsAlive(paused.Target) && units.DistanceTo(paused.Target) > ability.Range)
                {
                    Chase(units.PositionOf(paused.Target), context);
                }
                else
                {
                    StopChasing();
                }

                break;
        }
    }

    /// <summary>
    /// Completes the running cast, then fires the buffered cast if it can start, otherwise resumes the paused order
    /// right away (or stops when there is none).
    /// </summary>
    private void FinishCast(in PlayerOrderContext context)
    {
        caster.CompleteCast(current.Slot);
        current = default;

        if (hasBuffered && TryFireBuffered(context))
        {
            return;
        }

        current = paused;
        paused = default;
        fresh = true;
        chasing = false;
        if (current.Kind == PlayerOrderKind.Idle)
        {
            motorRequest = PlayerMotorRequest.Stop;
            return;
        }

        RunCurrent(context);
    }

    /// <summary>
    /// Begins the buffered cast, re-aiming a unit cast at its target's current position, and runs its first tick.
    /// False when it can no longer be cast, its target died or it was refused (each reported).
    /// </summary>
    private bool TryFireBuffered(in PlayerOrderContext context)
    {
        hasBuffered = false;
        int slot = bufferedSlot;
        if (!caster.CanCast(slot, out CastOutcome failure))
        {
            caster.ReportFailure(slot, failure);
            return false;
        }

        AbilityDefinition ability = caster.GetAbility(slot);
        CastAim aim = bufferedAim;
        if (ability.Targeting == AbilityTargeting.Unit)
        {
            if (!units.IsAlive(aim.Target))
            {
                caster.ReportFailure(slot, CastOutcome.NoTarget);
                return false;
            }

            aim = UnitAim(aim.Target, context);
        }

        Order toPause = paused;
        fresh = true;
        chasing = false;
        if (!Begin(slot, ability, aim, context, toPause))
        {
            return false;
        }

        if (current.Kind == PlayerOrderKind.Casting)
        {
            RunCasting(context);
        }
        else
        {
            RunCastWhenInRange(context);
        }

        return true;
    }

    /// <summary>
    /// Paths toward a moving target, repathing when a chase starts, the order is fresh, the repath interval elapsed
    /// or the target moved too far from the last goal.
    /// </summary>
    private void Chase(Vector2 targetPosition, in PlayerOrderContext context)
    {
        float repathDistance = settings.TargetMoveRepathDistance;
        bool targetMoved = (targetPosition - chaseGoal).sqrMagnitude > repathDistance * repathDistance;
        if (!chasing || fresh || context.Time >= nextRepathTime || targetMoved)
        {
            chasing = true;
            chaseGoal = targetPosition;
            nextRepathTime = context.Time + settings.RepathInterval;
            RequestMoveTo(targetPosition);
        }
    }

    /// <summary>
    /// Ends a chase, stopping the motor when it was chasing or the order is fresh.
    /// </summary>
    private void StopChasing()
    {
        if (chasing || fresh)
        {
            motorRequest = PlayerMotorRequest.Stop;
        }

        chasing = false;
    }

    /// <summary>
    /// Asks the motor to path to <paramref name="point"/> this tick.
    /// </summary>
    private void RequestMoveTo(Vector2 point)
    {
        motorRequest = PlayerMotorRequest.MoveTo;
        destination = point;
    }

    /// <summary>
    /// Ends the current order, asking the motor to stop when there was one.
    /// </summary>
    private void BecomeIdle()
    {
        if (current.Kind != PlayerOrderKind.Idle)
        {
            motorRequest = PlayerMotorRequest.Stop;
        }

        current = default;
        chasing = false;
        fresh = false;
    }

    /// <summary>
    /// Drops the buffered cast, if any, reporting <paramref name="reason"/> for its slot.
    /// </summary>
    private void DropBuffered(CastOutcome reason)
    {
        if (!hasBuffered)
        {
            return;
        }

        hasBuffered = false;
        if (caster != null)
        {
            caster.ReportFailure(bufferedSlot, reason);
        }
    }

    /// <summary>
    /// One order: its kind plus the destination, target or ability slot that kind uses; <c>default</c> is Idle.
    /// </summary>
    private struct Order
    {
        public PlayerOrderKind Kind;
        public Vector2 Destination;
        public UnitTarget Target;
        public int Slot;

        /// <summary>
        /// A move to <paramref name="point"/>.
        /// </summary>
        public static Order Move(Vector2 point)
        {
            return new Order { Kind = PlayerOrderKind.Move, Destination = point };
        }

        /// <summary>
        /// An auto attack on <paramref name="target"/>.
        /// </summary>
        public static Order Attack(UnitTarget target)
        {
            return new Order { Kind = PlayerOrderKind.Attack, Target = target };
        }

        /// <summary>
        /// An approach to <paramref name="target"/> that casts the ability in <paramref name="slot"/> once in range.
        /// </summary>
        public static Order CastWhenInRange(int slot, UnitTarget target)
        {
            return new Order { Kind = PlayerOrderKind.CastWhenInRange, Target = target, Slot = slot };
        }

        /// <summary>
        /// The cast running from <paramref name="slot"/>.
        /// </summary>
        public static Order Casting(int slot)
        {
            return new Order { Kind = PlayerOrderKind.Casting, Slot = slot };
        }

        /// <summary>
        /// Whether giving this order over <paramref name="other"/> would change nothing: a move to the same point or an
        /// attack on the same unit. Other kinds never match, so they are always reissued.
        /// </summary>
        public readonly bool SameAs(in Order other)
        {
            if (Kind != other.Kind)
            {
                return false;
            }

            return Kind switch
            {
                PlayerOrderKind.Move => Destination == other.Destination,
                PlayerOrderKind.Attack => Target.IsSameUnit(other.Target),
                _ => false
            };
        }
    }
}
