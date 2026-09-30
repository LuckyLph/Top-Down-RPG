using UnityEngine;

// Gameplay input as the player character consumes it. Reads return neutral values while
// gameplay input is disabled (menus, scene transitions).
public interface IPlayerInput
{
    Vector2 Move { get; }
    bool AttackPressedThisFrame { get; }
    bool GameplayEnabled { get; }
}
