using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using VContainer;

/// <summary>
/// Replicates a networked player. The owner binds it as the local player, writes its move and facing to owner
/// network variables, and sends its swings and cast starts to the other machines and its completed casts to the host,
/// which resolves them. The owner's teleports (arriving at an entry, a waystone included) snap on the other machines
/// instead of sliding there. Every other machine binds it as a remote copy with a kinematic body showing that movement.
/// </summary>
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
    private Rigidbody2D body;
    private NetworkTransform networkTransform;
    private PlayerBinder binder;
    private PlayerHandle handle;

    /// <summary>
    /// The player's registry handle on this machine once it is bound, else null.
    /// </summary>
    internal PlayerHandle Handle => handle;

    /// <summary>
    /// Caches the controller, weapon, abilities, body and network transform on this player.
    /// </summary>
    private void Awake()
    {
        controller = GetComponent<PlayerController>();
        weapon = GetComponent<PlayerWeaponController>();
        abilities = GetComponent<PlayerAbilities>();
        body = GetComponent<Rigidbody2D>();
        networkTransform = GetComponent<NetworkTransform>();
    }

    /// <summary>
    /// Takes the binder that hooks this player into the Gameplay scope when it spawns.
    /// </summary>
    [Inject]
    public void Construct(PlayerBinder playerBinder)
    {
        binder = playerBinder;
    }

    /// <summary>
    /// The owner binds the player as local and starts relaying its swings, teleports and casts; any other machine
    /// makes the body kinematic without interpolation, binds it as remote and shows the current replicated movement.
    /// Logs an error and does nothing when the prefab was spawned without injection.
    /// </summary>
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
            controller.Teleported += HandleLocalTeleported;
            if (weapon != null)
            {
                weapon.Attacked += HandleLocalAttack;
            }

            if (abilities != null)
            {
                abilities.CastStarted += HandleLocalCast;
                abilities.CastCompleted += HandleLocalCastCompleted;
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

    /// <summary>
    /// Stops relaying the owner's events and unbinds the player from the registry and the local player tracker.
    /// </summary>
    public override void OnNetworkDespawn()
    {
        controller.Teleported -= HandleLocalTeleported;
        if (weapon != null)
        {
            weapon.Attacked -= HandleLocalAttack;
        }

        if (abilities != null)
        {
            abilities.CastStarted -= HandleLocalCast;
            abilities.CastCompleted -= HandleLocalCastCompleted;
        }

        if (binder != null)
        {
            binder.Unbind(handle);
        }

        handle = null;
    }

    /// <summary>
    /// Remote copies show the replicated move and facing; the owner writes its move and facing whenever either moved
    /// past a small threshold, to keep network traffic down.
    /// </summary>
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

    /// <summary>
    /// On the owner, makes the network transform jump to <paramref name="position"/> on the other machines instead of
    /// interpolating there, which across worlds would slide the copy thousands of units.
    /// </summary>
    private void HandleLocalTeleported(Vector2 position)
    {
        if (networkTransform != null && IsSpawned && IsOwner)
        {
            Transform own = transform;
            networkTransform.Teleport(new Vector3(position.x, position.y, own.position.z), own.rotation, own.localScale);
        }
    }

    /// <summary>
    /// Relays a swing the owner made to the other machines.
    /// </summary>
    private void HandleLocalAttack(Vector2 direction)
    {
        AttackRpc(direction);
    }

    /// <summary>
    /// Runs on every machine but the owner: plays the owner's swing visual toward <paramref name="direction"/>.
    /// </summary>
    [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Owner)]
    private void AttackRpc(Vector2 direction)
    {
        if (weapon != null)
        {
            weapon.PlayRemoteAttack(direction);
        }
    }

    /// <summary>
    /// Relays a cast the owner started, with its aim and any networked target, to the other machines.
    /// </summary>
    private void HandleLocalCast(AbilityCast cast)
    {
        bool hasTarget = TryReference(cast.Aim, out NetworkObjectReference target);
        CastRpc(cast.Slot, cast.Aim.Point, cast.Aim.Direction, target, hasTarget);
    }

    /// <summary>
    /// On a client owner, sends a completed cast and the owner's position to the host to resolve; the host resolves
    /// its own casts locally.
    /// </summary>
    private void HandleLocalCastCompleted(AbilityCast cast)
    {
        if (IsServer)
        {
            return;
        }

        bool hasTarget = TryReference(cast.Aim, out NetworkObjectReference target);
        CastCompletedRpc(cast.Slot, cast.Aim.Point, cast.Aim.Direction, target, hasTarget, transform.position);
    }

    /// <summary>
    /// Runs on the host: resolves the effect of a cast the owner completed at <paramref name="casterPosition"/>.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void CastCompletedRpc(int slot, Vector2 point, Vector2 direction, NetworkObjectReference target, bool hasTarget, Vector2 casterPosition)
    {
        if (abilities != null)
        {
            abilities.ResolveRemoteCast(slot, new CastAim(point, direction, hasTarget ? ResolveTarget(target) : default), casterPosition);
        }
    }

    /// <summary>
    /// A network reference to the aim's target unit; false (and a default reference) when there is no target or it
    /// is not a spawned network object.
    /// </summary>
    private static bool TryReference(in CastAim aim, out NetworkObjectReference reference)
    {
        Health target = aim.Target.Health;
        NetworkObject targetObject = target != null ? target.GetComponent<NetworkObject>() : null;
        bool hasTarget = targetObject != null && targetObject.IsSpawned;
        reference = hasTarget ? new NetworkObjectReference(targetObject) : default;
        return hasTarget;
    }

    /// <summary>
    /// Runs on every machine but the owner: replays a cast the owner started.
    /// </summary>
    [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Owner)]
    private void CastRpc(int slot, Vector2 point, Vector2 direction, NetworkObjectReference target, bool hasTarget)
    {
        if (abilities != null)
        {
            abilities.PlayRemoteCast(slot, new CastAim(point, direction, hasTarget ? ResolveTarget(target) : default));
        }
    }

    /// <summary>
    /// Turns a replicated target reference back into a <see cref="UnitTarget"/> on this machine (players are allies,
    /// anything else an enemy); no target when the object is not spawned here.
    /// </summary>
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
