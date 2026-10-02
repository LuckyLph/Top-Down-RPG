using UnityEngine;

/// <summary>
/// How an ability is aimed.
/// </summary>
public enum AbilityTargeting
{
    None,
    Direction,
    Point,
    Unit
}

/// <summary>
/// Which units a <see cref="AbilityTargeting.Unit"/> ability accepts.
/// </summary>
public enum AbilityUnitFilter
{
    Enemy,
    Ally,
    Any
}

/// <summary>
/// How the caster moves during the cast time.
/// </summary>
public enum CastMovement
{
    Stop,
    Continue,
    Ability
}

/// <summary>
/// What became of an ability key press or a cast attempt.
/// </summary>
public enum CastOutcome
{
    Started,
    Buffered,
    Approaching,
    EmptySlot,
    OnCooldown,
    CastInProgress,
    NoTarget,
    Dead,
    Expired,
    Cancelled
}

/// <summary>
/// Where a cast is aimed: a point, a direction and an optional unit.
/// </summary>
public readonly struct CastAim
{
    public CastAim(Vector2 point, Vector2 direction, UnitTarget target)
    {
        Point = point;
        Direction = direction;
        Target = target;
    }

    public Vector2 Point { get; }
    public Vector2 Direction { get; }
    public UnitTarget Target { get; }
}

/// <summary>
/// One started cast: the slot, its ability and the aim.
/// </summary>
public readonly struct AbilityCast
{
    public AbilityCast(int slot, AbilityDefinition ability, CastAim aim)
    {
        Slot = slot;
        Ability = ability;
        Aim = aim;
    }

    public int Slot { get; }
    public AbilityDefinition Ability { get; }
    public CastAim Aim { get; }
}
