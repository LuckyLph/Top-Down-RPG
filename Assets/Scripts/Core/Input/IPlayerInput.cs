using UnityEngine;

/// <summary>
/// The local player's mouse-driven input for one frame: pointer, move button, stop and ability keys.
/// Every read is neutral while gameplay input is disabled.
/// </summary>
public interface IPlayerInput
{
    bool GameplayEnabled { get; }
    Vector2 PointerScreenPosition { get; }
    bool IsPointerOverUI { get; }
    bool MovePressedThisFrame { get; }
    bool MoveHeld { get; }
    bool StopPressedThisFrame { get; }

    /// <summary>
    /// Whether the key for ability <paramref name="slot"/> (0 to <see cref="AbilitySlots.Count"/> - 1) went down this frame.
    /// </summary>
    bool WasAbilityPressedThisFrame(int slot);
}
