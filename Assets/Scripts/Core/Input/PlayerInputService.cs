using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Reads the <c>Player</c> action map for the local player and answers whether the pointer is over UI through
/// the Main scope's <see cref="EventSystem"/>.
/// </summary>
public sealed class PlayerInputService : IPlayerInput, IDisposable
{
    public const string PlayerMapName = "Player";
    public const string MoveClickActionName = "MoveClick";
    public const string PointActionName = "Point";
    public const string StopActionName = "Stop";
    public const string AbilityActionPrefix = "Ability";

    private readonly InputActionMap playerMap;
    private readonly InputAction moveClickAction;
    private readonly InputAction pointAction;
    private readonly InputAction stopAction;
    private readonly InputAction[] abilityActions = new InputAction[AbilitySlots.Count];
    private readonly EventSystem eventSystem;

    public PlayerInputService(InputActionAsset actions, EventSystem eventSystem)
    {
        this.eventSystem = eventSystem;
        playerMap = actions != null ? actions.FindActionMap(PlayerMapName, false) : null;
        moveClickAction = playerMap?.FindAction(MoveClickActionName, false);
        pointAction = playerMap?.FindAction(PointActionName, false);
        stopAction = playerMap?.FindAction(StopActionName, false);

        bool hasAllAbilities = true;
        for (int i = 0; i < abilityActions.Length; i++)
        {
            abilityActions[i] = playerMap?.FindAction(AbilityActionName(i), false);
            hasAllAbilities &= abilityActions[i] != null;
        }

        if (moveClickAction == null || pointAction == null || stopAction == null || !hasAllAbilities)
        {
            Debug.LogError(
                $"{nameof(PlayerInputService)} needs an input asset with a '{PlayerMapName}' map containing " +
                $"'{MoveClickActionName}', '{PointActionName}', '{StopActionName}' and " +
                $"'{AbilityActionName(0)}' to '{AbilityActionName(AbilitySlots.Count - 1)}' actions.");
        }
    }

    public bool GameplayEnabled => playerMap != null && playerMap.enabled;
    public Vector2 PointerScreenPosition => GameplayEnabled && pointAction != null ? pointAction.ReadValue<Vector2>() : Vector2.zero;
    public bool IsPointerOverUI => eventSystem != null && eventSystem.IsPointerOverGameObject();
    public bool MovePressedThisFrame => GameplayEnabled && moveClickAction != null && moveClickAction.WasPressedThisFrame();
    public bool MoveHeld => GameplayEnabled && moveClickAction != null && moveClickAction.IsPressed();
    public bool StopPressedThisFrame => GameplayEnabled && stopAction != null && stopAction.WasPressedThisFrame();

    public bool WasAbilityPressedThisFrame(int slot)
    {
        if (!GameplayEnabled || slot < 0 || slot >= abilityActions.Length)
        {
            return false;
        }

        InputAction action = abilityActions[slot];
        return action != null && action.WasPressedThisFrame();
    }

    /// <summary>
    /// The action name for ability <paramref name="slot"/>: <c>Ability1</c> for slot 0, and so on.
    /// </summary>
    public static string AbilityActionName(int slot)
    {
        return AbilityActionPrefix + (slot + 1);
    }

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
