using UnityEngine;

/// <summary>
/// The kinds of order a player can be carrying out.
/// </summary>
public enum PlayerOrderKind
{
    Idle,
    Move,
    Attack
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
/// The player's state as <see cref="PlayerOrders"/> sees it on a tick.
/// </summary>
public readonly struct PlayerOrderContext
{
    public PlayerOrderContext(Vector2 position, float time, bool reachedDestination, float stalledTime, float attackRange)
    {
        Position = position;
        Time = time;
        ReachedDestination = reachedDestination;
        StalledTime = stalledTime;
        AttackRange = attackRange;
    }

    public Vector2 Position { get; }
    public float Time { get; }
    public bool ReachedDestination { get; }
    public float StalledTime { get; }
    public float AttackRange { get; }
}

/// <summary>
/// What a tick of <see cref="PlayerOrders"/> asks for: a motor request, a facing direction and whether to swing.
/// </summary>
public readonly struct PlayerOrderOutput
{
    public PlayerOrderOutput(PlayerMotorRequest motor, Vector2 destination, bool hasAim, Vector2 aimDirection, bool swing)
    {
        Motor = motor;
        Destination = destination;
        HasAim = hasAim;
        AimDirection = aimDirection;
        Swing = swing;
    }

    public PlayerMotorRequest Motor { get; }
    public Vector2 Destination { get; }
    public bool HasAim { get; }
    public Vector2 AimDirection { get; }
    public bool Swing { get; }
}
