using System;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class PlayerInputService : IPlayerInput, IDisposable
{
    private const string PlayerMapName = "Player";
    private const string MoveActionName = "Move";
    private const string AttackActionName = "Attack";

    private readonly InputActionMap playerMap;
    private readonly InputAction moveAction;
    private readonly InputAction attackAction;

    public PlayerInputService(InputActionAsset actions)
    {
        playerMap = actions != null ? actions.FindActionMap(PlayerMapName, false) : null;
        moveAction = playerMap?.FindAction(MoveActionName, false);
        attackAction = playerMap?.FindAction(AttackActionName, false);

        if (moveAction == null || attackAction == null)
        {
            Debug.LogError(
                $"{nameof(PlayerInputService)} needs an input asset with a '{PlayerMapName}' map containing " +
                $"'{MoveActionName}' and '{AttackActionName}' actions.");
        }
    }

    public Vector2 Move => GameplayEnabled && moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
    public bool AttackPressedThisFrame => GameplayEnabled && attackAction != null && attackAction.WasPressedThisFrame();
    public bool GameplayEnabled => playerMap != null && playerMap.enabled;

    public void SetGameplayEnabled(bool enabled)
    {
        if (playerMap == null)
        {
            return;
        }

        if (enabled)
        {
            playerMap.Enable();
        }
        else
        {
            playerMap.Disable();
        }
    }

    public void Dispose()
    {
        SetGameplayEnabled(false);
    }
}
