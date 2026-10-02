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
    private PlayerAbilities abilities;
    private Health health;
    private Rigidbody2D body;
    private PlayerBinder binder;
    private PlayerHandle handle;

    private void Awake()
    {
        controller = GetComponent<PlayerController>();
        weapon = GetComponent<PlayerWeaponController>();
        abilities = GetComponent<PlayerAbilities>();
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

            if (abilities != null)
            {
                abilities.CastStarted += HandleLocalCast;
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

        if (abilities != null)
        {
            abilities.CastStarted -= HandleLocalCast;
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
        if (weapon != null)
        {
            weapon.PlayRemoteAttack(direction);
        }
    }

    private void HandleLocalCast(AbilityCast cast)
    {
        Health target = cast.Aim.Target.Health;
        NetworkObject targetObject = target != null ? target.GetComponent<NetworkObject>() : null;
        bool hasTarget = targetObject != null && targetObject.IsSpawned;
        CastRpc(cast.Slot, cast.Aim.Point, cast.Aim.Direction, hasTarget ? new NetworkObjectReference(targetObject) : default, hasTarget);
    }

    [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Owner)]
    private void CastRpc(int slot, Vector2 point, Vector2 direction, NetworkObjectReference target, bool hasTarget)
    {
        if (abilities != null)
        {
            abilities.PlayRemoteCast(slot, new CastAim(point, direction, hasTarget ? ResolveTarget(target) : default));
        }
    }

    private UnitTarget ResolveTarget(NetworkObjectReference reference)
    {
        if (!reference.TryGet(out NetworkObject targetObject, NetworkManager))
        {
            return default;
        }

        UnitTeam team = targetObject.TryGetComponent(out PlayerController _) ? UnitTeam.Ally : UnitTeam.Enemy;
        return new UnitTarget(targetObject.GetComponent<Health>(), targetObject.GetComponent<Collider2D>(), team);
    }
}
