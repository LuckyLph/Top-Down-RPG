using UnityEngine;

/// <summary>
/// Turns the local <see cref="IPlayerInput"/> into world-space <see cref="PlayerCommand"/>s with the main camera
/// and the <see cref="PointerTargetPicker"/> (picking only on move presses, holds and ability presses). A move press
/// over UI is dropped, and so is the hold that follows it.
/// </summary>
public sealed class LocalPlayerCommandSource : IPlayerCommandSource
{
    private readonly IPlayerInput input;
    private readonly Camera camera;
    private readonly PointerTargetPicker picker;
    private bool holdAccepted;

    /// <summary>
    /// Captures the input, the camera that maps the pointer into the world and the target picker (null picks nothing).
    /// </summary>
    public LocalPlayerCommandSource(IPlayerInput input, Camera camera, PointerTargetPicker picker)
    {
        this.input = input;
        this.camera = camera;
        this.picker = picker;
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
        bool wantsTarget = movePressed || moveHeld || abilitySlot != PlayerCommand.NoAbility;
        UnitTarget target = wantsTarget && picker != null ? picker.Pick(pointerWorld) : default;

        return new PlayerCommand(pointerWorld, target, movePressed, moveHeld, input.StopPressedThisFrame, abilitySlot);
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
}
