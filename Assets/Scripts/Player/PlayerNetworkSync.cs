using Unity.Netcode;
using UnityEngine;
using VContainer;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController))]
public class PlayerNetworkSync : NetworkBehaviour
{
    private const float ChangeThreshold = 0.01f;

    private readonly NetworkVariable<Vector2> move = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<Vector2> facing = new(Vector2.down, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private PlayerController controller;
    private PlayerWeaponController weapon;
    private Health health;
    private Rigidbody2D body;
    private PlayerBinder binder;
    private PlayerHandle handle;

    private void Awake()
    {
        controller = GetComponent<PlayerController>();
        weapon = GetComponent<PlayerWeaponController>();
        health = GetComponent<Health>();
        body = GetComponent<Rigidbody2D>();
    }

    [Inject]
    public void Construct(PlayerBinder playerBinder)
    {
        binder = playerBinder;
    }

    public override void OnNetworkSpawn()
    {
        if (binder == null)
        {
            Debug.LogError($"{name} was spawned without a {nameof(PlayerBinder)}; register the player prefab with the Gameplay scope's resolver.", this);
            return;
        }

        if (IsOwner)
        {
            handle = binder.BindLocal(controller);
            if (weapon != null)
            {
                weapon.Attacked += HandleLocalAttack;
            }

            if (health != null)
            {
                health.Restored += HandleLocalRestored;
            }

            return;
        }

        if (body != null)
        {
            body.bodyType = RigidbodyType2D.Kinematic;
            body.interpolation = RigidbodyInterpolation2D.None;
            body.linearVelocity = Vector2.zero;
        }

        handle = binder.BindRemote(controller);
        controller.ShowRemoteMovement(move.Value, facing.Value);
    }

    public override void OnNetworkDespawn()
    {
        if (weapon != null)
        {
            weapon.Attacked -= HandleLocalAttack;
        }

        if (health != null)
        {
            health.Restored -= HandleLocalRestored;
        }

        if (binder != null)
        {
            binder.Unbind(handle);
        }

        handle = null;
    }

    private void Update()
    {
        if (!IsSpawned)
        {
            return;
        }

        if (!IsOwner)
        {
            controller.ShowRemoteMovement(move.Value, facing.Value);
            return;
        }

        Vector2 currentMove = controller.CurrentMove;
        if ((currentMove - move.Value).sqrMagnitude > ChangeThreshold * ChangeThreshold)
        {
            move.Value = currentMove;
        }

        Vector2 currentFacing = controller.FacingDirection;
        if ((currentFacing - facing.Value).sqrMagnitude > ChangeThreshold * ChangeThreshold)
        {
            facing.Value = currentFacing;
        }
    }

    private void HandleLocalRestored(Health _)
    {
        binder.PlaceAtActiveSpawn(handle);
    }

    private void HandleLocalAttack(Vector2 direction)
    {
        AttackRpc(direction);
    }

    [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Owner)]
    private void AttackRpc(Vector2 direction)
    {
        controller.Face(direction);
        if (weapon != null)
        {
            weapon.PlayRemoteAttack(direction);
        }
    }
}
