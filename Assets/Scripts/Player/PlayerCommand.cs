using UnityEngine;

/// <summary>
/// One frame of player intent in world space: where the pointer is, the unit under it, the move button, stop,
/// and the ability slot pressed (if any).
/// </summary>
public readonly struct PlayerCommand
{
    public const int NoAbility = -1;

    private readonly int abilitySlotPlusOne;

    public PlayerCommand(
        Vector2 pointerWorld,
        UnitTarget target,
        bool movePressed,
        bool moveHeld,
        bool stopPressed,
        int abilitySlot = NoAbility)
    {
        PointerWorld = pointerWorld;
        Target = target;
        MovePressed = movePressed;
        MoveHeld = moveHeld;
        StopPressed = stopPressed;
        abilitySlotPlusOne = abilitySlot >= 0 ? abilitySlot + 1 : 0;
    }

    public Vector2 PointerWorld { get; }
    public UnitTarget Target { get; }
    public bool MovePressed { get; }
    public bool MoveHeld { get; }
    public bool StopPressed { get; }
    public bool HasAbility => abilitySlotPlusOne > 0;
    public int AbilitySlot => abilitySlotPlusOne - 1;
}
