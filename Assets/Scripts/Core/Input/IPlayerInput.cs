using UnityEngine;

public interface IPlayerInput
{
    Vector2 Move { get; }
    bool AttackPressedThisFrame { get; }
    bool GameplayEnabled { get; }
}
