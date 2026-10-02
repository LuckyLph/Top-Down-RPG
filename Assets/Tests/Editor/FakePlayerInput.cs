using UnityEngine;

public sealed class FakePlayerInput : IPlayerInput
{
    public bool GameplayEnabled { get; set; } = true;
    public Vector2 PointerScreenPosition { get; set; }
    public bool IsPointerOverUI { get; set; }
    public bool MovePressedThisFrame { get; set; }
    public bool MoveHeld { get; set; }
    public bool StopPressedThisFrame { get; set; }
    public int AbilityPressed { get; set; } = -1;

    public bool WasAbilityPressedThisFrame(int slot)
    {
        return slot == AbilityPressed;
    }
}
