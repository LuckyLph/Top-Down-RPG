using UnityEngine;

/// <summary>
/// One frame of player intent in world space: where the pointer is, the unit under it for the move button and the one
/// for the ability pressed (picked with its ability's unit filter), the move button, stop, the ability slot pressed (if
/// any), the attack-move key and the lantern key.
/// </summary>
public readonly struct PlayerCommand
{
    public const int NoAbility = -1;

    private readonly int abilitySlotPlusOne;

    /// <summary>
    /// Creates a command; a negative <paramref name="abilitySlot"/> means no ability was pressed, so
    /// <c>default</c> is a command with no intent at all. <paramref name="attackMovePressed"/> gives an attack move
    /// to <paramref name="pointerWorld"/>; <paramref name="lanternPressed"/> lights or puts out the lantern.
    /// <paramref name="abilityTarget"/> is the unit the ability press aims at, <paramref name="target"/> when not given.
    /// </summary>
    public PlayerCommand(
        Vector2 pointerWorld,
        UnitTarget target,
        bool movePressed,
        bool moveHeld,
        bool stopPressed,
        int abilitySlot = NoAbility,
        bool attackMovePressed = false,
        bool lanternPressed = false,
        UnitTarget? abilityTarget = null)
    {
        PointerWorld = pointerWorld;
        Target = target;
        AbilityTarget = abilityTarget ?? target;
        MovePressed = movePressed;
        MoveHeld = moveHeld;
        StopPressed = stopPressed;
        AttackMovePressed = attackMovePressed;
        LanternPressed = lanternPressed;
        abilitySlotPlusOne = abilitySlot >= 0 ? abilitySlot + 1 : 0;
    }

    public Vector2 PointerWorld { get; }
    public UnitTarget Target { get; }
    public UnitTarget AbilityTarget { get; }
    public bool MovePressed { get; }
    public bool MoveHeld { get; }
    public bool StopPressed { get; }
    public bool AttackMovePressed { get; }
    public bool LanternPressed { get; }
    public bool HasAbility => abilitySlotPlusOne > 0;
    public int AbilitySlot => abilitySlotPlusOne - 1;
}
