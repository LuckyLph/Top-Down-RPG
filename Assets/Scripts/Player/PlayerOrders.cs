using UnityEngine;

/// <summary>
/// The player's order state machine (Idle, Move, Attack). Each tick it turns a <see cref="PlayerCommand"/> and the
/// player's state into motor requests, a facing direction and swing requests. No engine lookups: targets are
/// queried through <see cref="IUnitQueries"/>.
/// </summary>
public sealed class PlayerOrders
{
    private readonly PlayerControlSettings settings;
    private readonly IUnitQueries units;

    private PlayerOrderKind current;
    private Vector2 moveDestination;
    private UnitTarget attackTarget;
    private bool freshOrder;
    private bool chasing;
    private Vector2 chaseGoal;
    private float nextRepathTime;
    private float nextHoldEvaluationTime;

    private PlayerMotorRequest motorRequest;
    private Vector2 destination;
    private bool hasAim;
    private Vector2 aimDirection;
    private bool swing;

    public PlayerOrders(PlayerControlSettings settings, IUnitQueries units)
    {
        this.settings = settings;
        this.units = units;
    }

    public PlayerOrderKind Current => current;
    public Vector2 MoveDestination => moveDestination;
    public UnitTarget AttackTarget => attackTarget;

    /// <summary>
    /// Applies the command (stop, a new order, or a throttled re-evaluation while the move button is held), then
    /// runs the current order.
    /// </summary>
    public PlayerOrderOutput Tick(in PlayerCommand command, in PlayerOrderContext context)
    {
        motorRequest = PlayerMotorRequest.None;
        hasAim = false;
        swing = false;

        if (command.StopPressed)
        {
            BecomeIdle();
        }

        if (command.MovePressed)
        {
            IssueFrom(command);
            nextHoldEvaluationTime = context.Time + settings.HoldReevaluateInterval;
        }
        else if (command.MoveHeld && context.Time >= nextHoldEvaluationTime)
        {
            IssueFrom(command);
            nextHoldEvaluationTime = context.Time + settings.HoldReevaluateInterval;
        }

        switch (current)
        {
            case PlayerOrderKind.Move:
                RunMove(context);
                break;
            case PlayerOrderKind.Attack:
                RunAttack(context);
                break;
        }

        freshOrder = false;
        return new PlayerOrderOutput(motorRequest, destination, hasAim, aimDirection, swing);
    }

    /// <summary>
    /// Drops the current order without asking the motor for anything (used on death, teleports and when the
    /// player stops being simulated here).
    /// </summary>
    public void Clear()
    {
        current = PlayerOrderKind.Idle;
        attackTarget = default;
        chasing = false;
        freshOrder = false;
    }

    private void IssueFrom(in PlayerCommand command)
    {
        UnitTarget target = command.Target;
        if (target.Team == UnitTeam.Enemy && units.IsAlive(target))
        {
            if (current == PlayerOrderKind.Attack && attackTarget.IsSameUnit(target))
            {
                return;
            }

            current = PlayerOrderKind.Attack;
            attackTarget = target;
            chasing = false;
            freshOrder = true;
            return;
        }

        if (current == PlayerOrderKind.Move && !freshOrder && command.PointerWorld == moveDestination)
        {
            return;
        }

        current = PlayerOrderKind.Move;
        moveDestination = command.PointerWorld;
        attackTarget = default;
        chasing = false;
        freshOrder = true;
    }

    private void RunMove(in PlayerOrderContext context)
    {
        if (freshOrder)
        {
            RequestMoveTo(moveDestination);
            return;
        }

        if (context.ReachedDestination || context.StalledTime >= settings.StuckTimeout)
        {
            BecomeIdle();
        }
    }

    private void RunAttack(in PlayerOrderContext context)
    {
        if (!units.IsAlive(attackTarget))
        {
            BecomeIdle();
            return;
        }

        Vector2 targetPosition = units.PositionOf(attackTarget);
        if (units.DistanceTo(attackTarget) <= context.AttackRange)
        {
            if (chasing || freshOrder)
            {
                motorRequest = PlayerMotorRequest.Stop;
            }

            chasing = false;
            Vector2 toTarget = targetPosition - context.Position;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                hasAim = true;
                aimDirection = toTarget.normalized;
            }

            swing = true;
            return;
        }

        float repathDistance = settings.TargetMoveRepathDistance;
        bool targetMoved = (targetPosition - chaseGoal).sqrMagnitude > repathDistance * repathDistance;
        if (!chasing || context.Time >= nextRepathTime || targetMoved)
        {
            chasing = true;
            chaseGoal = targetPosition;
            nextRepathTime = context.Time + settings.RepathInterval;
            RequestMoveTo(targetPosition);
        }
    }

    private void RequestMoveTo(Vector2 point)
    {
        motorRequest = PlayerMotorRequest.MoveTo;
        destination = point;
    }

    private void BecomeIdle()
    {
        if (current != PlayerOrderKind.Idle)
        {
            motorRequest = PlayerMotorRequest.Stop;
        }

        Clear();
    }
}
