using UnityEngine;

/// <summary>
/// <see cref="IPlayerInput"/> whose pointer, move, attack move, stop and ability presses tests set directly
/// instead of reading devices.
/// </summary>
public sealed class FakePlayerInput : IPlayerInput
{
    public bool GameplayEnabled { get; set; } = true;
    public Vector2 PointerScreenPosition { get; set; }
    public bool IsPointerOverUI { get; set; }
    public bool MovePressedThisFrame { get; set; }
    public bool MoveHeld { get; set; }
    public bool AttackMovePressedThisFrame { get; set; }
    public bool StopPressedThisFrame { get; set; }
    public int AbilityPressed { get; set; } = -1;

    /// <summary>
    /// Whether <paramref name="slot"/> is the slot set in <see cref="AbilityPressed"/> (-1 means no ability).
    /// </summary>
    public bool WasAbilityPressedThisFrame(int slot)
    {
        return slot == AbilityPressed;
    }
}
