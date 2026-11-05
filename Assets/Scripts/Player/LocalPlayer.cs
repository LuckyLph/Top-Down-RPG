using UnityEngine;

/// <summary>
/// The player this machine controls: its controller, weapon, abilities and <see cref="PlayerHandle"/>, as published by
/// <see cref="LocalPlayerTracker"/> to the camera, HUD and other local-only consumers.
/// </summary>
public sealed class LocalPlayer
{
    /// <summary>
    /// Wraps <paramref name="controller"/>, looking up its weapon and abilities and creating a handle over its transform
    /// and health.
    /// </summary>
    public LocalPlayer(PlayerController controller)
    {
        Controller = controller;
        Weapon = controller.GetComponent<PlayerWeaponController>();
        Abilities = controller.GetComponent<PlayerAbilities>();
        Handle = new PlayerHandle(controller.transform, controller.GetComponent<Health>());
    }

    public PlayerController Controller { get; }
    public PlayerWeaponController Weapon { get; }
    public PlayerAbilities Abilities { get; }
    public PlayerHandle Handle { get; }
    public Transform Transform => Controller.transform;
}
