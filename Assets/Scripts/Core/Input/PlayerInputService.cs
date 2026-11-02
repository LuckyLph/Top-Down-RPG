using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Reads the <c>Player</c> action map for the local player and answers whether the pointer is over UI through
/// the Main scope's <see cref="EventSystem"/>. Also reads the <c>UI</c> map's Cancel and the <c>Menus</c> map's
/// Menu key for menus.
/// </summary>
public sealed class PlayerInputService : IPlayerInput, IMenuInput, IDisposable
{
    public const string PlayerMapName = "Player";
    public const string MoveClickActionName = "MoveClick";
    public const string AttackMoveActionName = "AttackMove";
    public const string PointActionName = "Point";
    public const string StopActionName = "Stop";
    public const string LanternActionName = "Lantern";
    public const string AbilityActionPrefix = "Ability";
    public const string UIMapName = "UI";
    public const string CancelActionName = "Cancel";
    public const string MenusMapName = "Menus";
    public const string MenuActionName = "Menu";

    private readonly InputActionMap playerMap;
    private readonly InputAction moveClickAction;
    private readonly InputAction attackMoveAction;
    private readonly InputAction pointAction;
    private readonly InputAction stopAction;
    private readonly InputAction lanternAction;
    private readonly InputAction[] abilityActions = new InputAction[AbilitySlots.Count];
    private readonly InputAction cancelAction;
    private readonly InputActionMap menusMap;
    private readonly InputAction menuAction;
    private readonly EventSystem eventSystem;

    /// <summary>
    /// Looks up the player, UI cancel and menu actions on <paramref name="actions"/> (the session's runtime copy) and
    /// logs one error listing what the asset must contain when any of them is missing.
    /// </summary>
    public PlayerInputService(InputActionAsset actions, EventSystem eventSystem)
    {
        this.eventSystem = eventSystem;
        playerMap = actions != null ? actions.FindActionMap(PlayerMapName, false) : null;
        moveClickAction = playerMap?.FindAction(MoveClickActionName, false);
        attackMoveAction = playerMap?.FindAction(AttackMoveActionName, false);
        pointAction = playerMap?.FindAction(PointActionName, false);
        stopAction = playerMap?.FindAction(StopActionName, false);
        lanternAction = playerMap?.FindAction(LanternActionName, false);
        cancelAction = actions != null ? actions.FindAction($"{UIMapName}/{CancelActionName}", false) : null;
        menusMap = actions != null ? actions.FindActionMap(MenusMapName, false) : null;
        menuAction = menusMap?.FindAction(MenuActionName, false);

        bool hasAllAbilities = true;
        for (int i = 0; i < abilityActions.Length; i++)
        {
            abilityActions[i] = playerMap?.FindAction(AbilityActionName(i), false);
            hasAllAbilities &= abilityActions[i] != null;
        }

        if (moveClickAction == null || attackMoveAction == null || pointAction == null || stopAction == null || lanternAction == null || !hasAllAbilities || cancelAction == null || menuAction == null)
        {
            Debug.LogError(
                $"{nameof(PlayerInputService)} needs an input asset with a '{PlayerMapName}' map containing " +
                $"'{MoveClickActionName}', '{AttackMoveActionName}', '{PointActionName}', '{StopActionName}', '{LanternActionName}' and " +
                $"'{AbilityActionName(0)}' to '{AbilityActionName(AbilitySlots.Count - 1)}' actions, " +
                $"a '{UIMapName}' map with a '{CancelActionName}' action and a '{MenusMapName}' map with a '{MenuActionName}' action.");
        }
    }

    public bool GameplayEnabled => playerMap != null && playerMap.enabled;
    public bool MenusEnabled => menusMap != null && menusMap.enabled;
    public Vector2 PointerScreenPosition => GameplayEnabled && pointAction != null ? pointAction.ReadValue<Vector2>() : Vector2.zero;
    public bool IsPointerOverUI => eventSystem != null && eventSystem.IsPointerOverGameObject();
    public bool MovePressedThisFrame => GameplayEnabled && moveClickAction != null && moveClickAction.WasPressedThisFrame();
    public bool MoveHeld => GameplayEnabled && moveClickAction != null && moveClickAction.IsPressed();
    public bool AttackMovePressedThisFrame => GameplayEnabled && attackMoveAction != null && attackMoveAction.WasPressedThisFrame();
    public bool StopPressedThisFrame => GameplayEnabled && stopAction != null && stopAction.WasPressedThisFrame();
    public bool LanternPressedThisFrame => GameplayEnabled && lanternAction != null && lanternAction.WasPressedThisFrame();
    public bool BackPressedThisFrame => cancelAction != null && cancelAction.enabled && cancelAction.WasPressedThisFrame();
    public bool MenuPressedThisFrame => menuAction != null && menuAction.enabled && menuAction.WasPressedThisFrame();

    /// <summary>
    /// Whether the key for ability <paramref name="slot"/> went down this frame; false while gameplay input is off,
    /// for a slot out of range, or when the action is missing.
    /// </summary>
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

    /// <summary>
    /// Enables or disables the whole <c>Player</c> map; while it is off every gameplay read is neutral.
    /// </summary>
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

    /// <summary>
    /// Enables or disables the <c>Menus</c> map that carries the in-game menu key.
    /// </summary>
    public void SetMenusEnabled(bool enabled)
    {
        if (menusMap == null)
        {
            return;
        }

        if (enabled)
        {
            menusMap.Enable();
        }
        else
        {
            menusMap.Disable();
        }
    }

    /// <summary>
    /// Disables the <c>Player</c> and <c>Menus</c> maps when the scope ends.
    /// </summary>
    public void Dispose()
    {
        SetGameplayEnabled(false);
        SetMenusEnabled(false);
    }
}
