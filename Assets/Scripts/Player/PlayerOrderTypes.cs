using UnityEngine;

/// <summary>
/// The kinds of order a player can be carrying out.
/// </summary>
public enum PlayerOrderKind
{
    Idle,
    Move,
    Attack,
    AttackMove,
    CastWhenInRange,
    Casting
}

/// <summary>
/// What <see cref="PlayerOrders"/> asks of the motor on a tick.
/// </summary>
public enum PlayerMotorRequest
{
    None,
    MoveTo,
    Stop
}

/// <summary>
/// The order a tick's command gave, which the weapon's cancel rules read: none, a move (a move order or Stop: walking
/// or standing rather than fighting), or any other (an attack order on another unit, an attack move, an ability press
/// that started a cast or an approach).
/// </summary>
public enum PlayerOrderIssue
{
    None,
    Move,
    Other
}

/// <summary>
/// The player's state as <see cref="PlayerOrders"/> sees it on a tick.
/// </summary>
public readonly struct PlayerOrderContext
{
    /// <summary>
    /// Captures the player's position, the clock time, the motor's arrival and stall state, the weapon's attack range
    /// and the crowd control currently applied (none by default).
    /// </summary>
    public PlayerOrderContext(
        Vector2 position,
        float time,
        bool reachedDestination,
        float stalledTime,
        float attackRange,
        StatusControls controls = StatusControls.None)
    {
        Position = position;
        Time = time;
        ReachedDestination = reachedDestination;
        StalledTime = stalledTime;
        AttackRange = attackRange;
        Controls = controls;
    }

    public Vector2 Position { get; }
    public float Time { get; }
    public bool ReachedDestination { get; }
    public float StalledTime { get; }
    public float AttackRange { get; }
    public StatusControls Controls { get; }
}

/// <summary>
/// What a tick of <see cref="PlayerOrders"/> asks for: a motor request, a facing direction, whether to swing, and the
/// order the tick's command gave.
/// </summary>
public readonly struct PlayerOrderOutput
{
    /// <summary>
    /// Creates an output; <paramref name="destination"/> only matters for <see cref="PlayerMotorRequest.MoveTo"/> and
    /// <paramref name="aimDirection"/> only when <paramref name="hasAim"/> is set.
    /// </summary>
    public PlayerOrderOutput(PlayerMotorRequest motor, Vector2 destination, bool hasAim, Vector2 aimDirection, bool swing, PlayerOrderIssue issued = PlayerOrderIssue.None)
    {
        Motor = motor;
        Destination = destination;
        HasAim = hasAim;
        AimDirection = aimDirection;
        Swing = swing;
        Issued = issued;
    }

    public PlayerMotorRequest Motor { get; }
    public Vector2 Destination { get; }
    public bool HasAim { get; }
    public Vector2 AimDirection { get; }
    public bool Swing { get; }
    public PlayerOrderIssue Issued { get; }
}
