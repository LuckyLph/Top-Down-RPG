using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Local-only cursor feedback for the local player: every frame it picks under the pointer and, over a living
/// enemy, shows the hover ring and the attack cursor; a move click that is not on an enemy drops a move marker.
/// Nothing is shown while gameplay input is off, the pointer is over UI, or the local player is missing or dead.
/// </summary>
public sealed class PointerFeedbackPresenter : ITickable
{
    private readonly IPlayerInput input;
    private readonly Camera camera;
    private readonly PointerTargetPicker picker;
    private readonly LocalPlayerTracker localPlayer;
    private readonly PointerFeedbackLayer layer;

    public PointerFeedbackPresenter(
        IPlayerInput input,
        Camera camera,
        PointerTargetPicker picker,
        LocalPlayerTracker localPlayer,
        PointerFeedbackLayer layer)
    {
        this.input = input;
        this.camera = camera;
        this.picker = picker;
        this.localPlayer = localPlayer;
        this.layer = layer;
    }

    public void Tick()
    {
        Tick(Time.deltaTime);
    }

    internal void Tick(float deltaTime)
    {
        layer.Tick(deltaTime);

        LocalPlayer player = localPlayer.Current;
        if (!input.GameplayEnabled || camera == null || player == null || !player.Handle.IsAlive || input.IsPointerOverUI)
        {
            layer.SetHovered(null);
            layer.SetAttackCursor(false);
            return;
        }

        Vector2 screen = input.PointerScreenPosition;
        Vector2 pointerWorld = camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
        UnitTarget hovered = picker.Pick(pointerWorld);
        bool overEnemy = hovered.Team == UnitTeam.Enemy;
        layer.SetHovered(overEnemy ? hovered.Health.transform : null);
        layer.SetAttackCursor(overEnemy);

        if (input.MovePressedThisFrame && !overEnemy)
        {
            layer.SpawnMarker(pointerWorld);
        }
    }
}
