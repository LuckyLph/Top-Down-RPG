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

    public LocalPlayerCommandSource(IPlayerInput input, Camera camera, PointerTargetPicker picker)
    {
        this.input = input;
        this.camera = camera;
        this.picker = picker;
    }

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
