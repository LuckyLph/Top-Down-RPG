using UnityEngine;

public sealed class LocalPlayer
{
    public LocalPlayer(PlayerController controller)
    {
        Controller = controller;
        Weapon = controller.GetComponent<PlayerWeaponController>();
        Handle = new PlayerHandle(controller.transform, controller.GetComponent<Health>());
    }

    public PlayerController Controller { get; }
    public PlayerWeaponController Weapon { get; }
    public PlayerHandle Handle { get; }
    public Transform Transform => Controller.transform;
}
