using UnityEngine;

/// <summary>
/// Turns the local <see cref="IPlayerInput"/> into world-space <see cref="PlayerCommand"/>s with the main camera
/// and the <see cref="PointerTargetPicker"/> (picking only on move presses, holds and ability presses). A move press or
/// hold picks the unit of either team under the pointer; an ability press picks with the unit filter of the ability in
/// that slot of the local player, so an ally-only cast takes the closest ally even over a closer enemy. A move press
/// over UI is dropped, and so is the hold that follows it. The attack-move key, like the ability keys, is passed on
/// wherever the pointer is.
/// </summary>
public sealed class LocalPlayerCommandSource : IPlayerCommandSource
{
    private readonly IPlayerInput input;
    private readonly Camera camera;
    private readonly PointerTargetPicker picker;
    private readonly LocalPlayerTracker localPlayer;
    private bool holdAccepted;

    /// <summary>
    /// Captures the input, the camera that maps the pointer into the world, the target picker (null picks nothing)
    /// and the local player whose abilities' unit filters the ability presses pick with (none picks either team).
    /// </summary>
    public LocalPlayerCommandSource(IPlayerInput input, Camera camera, PointerTargetPicker picker, LocalPlayerTracker localPlayer)
    {
        this.input = input;
        this.camera = camera;
        this.picker = picker;
        this.localPlayer = localPlayer;
    }

    /// <summary>
    /// Builds this frame's command from the input. Returns an empty command, and forgets any accepted hold, while
    /// gameplay input is disabled or there is no camera.
    /// </summary>
    public PlayerCommand ReadCommand()
    {
        if (!input.GameplayEnabled || camera == null)
        {
            holdAccepted = false;
            return default;
        }

        bool pressedThisFrame = input.MovePressedThisFrame;
        bool movePressed = pressedThisFrame && !input.IsPointerOverUI;
        if (pressedThisFrame)
        {
            holdAccepted = movePressed;
        }

        bool moveHeld = holdAccepted && input.MoveHeld;
        if (!input.MoveHeld)
        {
            holdAccepted = false;
        }

        Vector2 screen = input.PointerScreenPosition;
        Vector2 pointerWorld = camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
        int abilitySlot = ReadAbilitySlot();
        UnitTarget target = (movePressed || moveHeld) && picker != null ? picker.Pick(pointerWorld) : default;
        UnitTarget abilityTarget = abilitySlot != PlayerCommand.NoAbility && picker != null
            ? picker.Pick(pointerWorld, UnitFilterOf(abilitySlot))
            : default;

        return new PlayerCommand(pointerWorld, target, movePressed, moveHeld, input.StopPressedThisFrame, abilitySlot, input.AttackMovePressedThisFrame, input.LanternPressedThisFrame, abilityTarget);
    }

    /// <summary>
    /// The lowest ability slot pressed this frame, or <see cref="PlayerCommand.NoAbility"/> when none was.
    /// </summary>
    private int ReadAbilitySlot()
    {
        for (int slot = 0; slot < AbilitySlots.Count; slot++)
        {
            if (input.WasAbilityPressedThisFrame(slot))
            {
                return slot;
            }
        }

        return PlayerCommand.NoAbility;
    }

    /// <summary>
    /// The unit filter of the local player's ability in <paramref name="slot"/> when it targets a unit; either team for
    /// any other ability, an empty slot or no local player.
    /// </summary>
    private AbilityUnitFilter UnitFilterOf(int slot)
    {
        LocalPlayer local = localPlayer != null ? localPlayer.Current : null;
        PlayerAbilities abilities = local != null ? local.Abilities : null;
        AbilityDefinition ability = abilities != null ? abilities.GetAbility(slot) : null;
        return ability != null && ability.Targeting == AbilityTargeting.Unit ? ability.UnitFilter : AbilityUnitFilter.Any;
    }
}
