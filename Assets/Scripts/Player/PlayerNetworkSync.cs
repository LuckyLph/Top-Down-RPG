using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using VContainer;

/// <summary>
/// Replicates a networked player. The owner binds it as the local player, writes its move (its actual speed over the
/// base move speed, so a slowed or hasted copy shows the same gait), facing and equipped weapon (its
/// <see cref="WeaponCatalog"/> index) to owner network variables, and sends its attacks' starts, contacts and early
/// ends and its cast starts and cancels to the other machines, which play them on their copy, and its completed casts to
/// the host.
/// Attack and cast messages name the weapon they were made with, so they play and land right even before a weapon
/// switch reaches a machine. Every machine but the owner lands a contact around where the owner stood: only the host
/// resolves a melee hit, and every machine flies a projectile, which only hits on the host; the host also resolves a
/// client's completed casts there. The owner's teleports (arriving at an entry, a waystone included) snap on the other
/// machines instead of sliding there. Every other machine binds it as a remote copy with a kinematic body showing that
/// movement and equips the replicated weapon, a late joiner included.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController))]
public class PlayerNetworkSync : NetworkBehaviour
{
    private const float ChangeThreshold = 0.01f;
    private const int NoWeapon = -1;

    private readonly NetworkVariable<Vector2> move = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<Vector2> facing = new(Vector2.down, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<int> equippedWeapon = new(NoWeapon, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private PlayerController controller;
    private PlayerWeaponController weapon;
    private PlayerAbilities abilities;
    private Rigidbody2D body;
    private NetworkTransform networkTransform;
    private PlayerBinder binder;
    private PlayerHandle handle;
    private int castWeaponIndex = NoWeapon;

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
    /// The owner binds the player as local, writes its weapon and starts relaying its weapon switches, attacks,
    /// teleports and casts; any other machine makes the body kinematic without interpolation, binds it as remote, shows
    /// the current replicated movement and equips the replicated weapon, following it from then on. Logs an error and
    /// does nothing when the prefab was spawned without injection.
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
                weapon.AttackStarted += HandleLocalAttackStarted;
                weapon.AttackContacted += HandleLocalAttackContacted;
                weapon.AttackCancelled += HandleLocalAttackCancelled;
                weapon.EquippedWeaponChanged += HandleLocalWeaponChanged;
                WriteWeapon();
            }

            if (abilities != null)
            {
                abilities.CastStarted += HandleLocalCast;
                abilities.CastEnded += HandleLocalCastEnded;
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
        equippedWeapon.OnValueChanged += HandleWeaponReplicated;
        EquipReplicatedWeapon(equippedWeapon.Value);
    }

    /// <summary>
    /// Stops relaying the owner's events and unbinds the player from the registry and the local player tracker.
    /// </summary>
    public override void OnNetworkDespawn()
    {
        controller.Teleported -= HandleLocalTeleported;
        if (weapon != null)
        {
            weapon.AttackStarted -= HandleLocalAttackStarted;
            weapon.AttackContacted -= HandleLocalAttackContacted;
            weapon.AttackCancelled -= HandleLocalAttackCancelled;
            weapon.EquippedWeaponChanged -= HandleLocalWeaponChanged;
        }

        equippedWeapon.OnValueChanged -= HandleWeaponReplicated;
        if (abilities != null)
        {
            abilities.CastStarted -= HandleLocalCast;
            abilities.CastEnded -= HandleLocalCastEnded;
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
    /// Relays the start of an attack the owner made to the other machines.
    /// </summary>
    private void HandleLocalAttackStarted(int attack, Vector2 direction, float attackSpeed)
    {
        if (IsSpawned)
        {
            AttackStartedRpc(weapon.WeaponIndex, (byte)attack, direction, attackSpeed);
        }
    }

    /// <summary>
    /// Runs on every machine but the owner: plays the owner's attack <paramref name="attack"/> of the catalog's weapon
    /// <paramref name="weaponIndex"/> toward <paramref name="direction"/> at <paramref name="attackSpeed"/>, its windup
    /// holding until the contact arrives.
    /// </summary>
    [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Owner)]
    private void AttackStartedRpc(int weaponIndex, byte attack, Vector2 direction, float attackSpeed)
    {
        if (weapon != null)
        {
            weapon.PlayRemoteAttack(weaponIndex, attack, direction, attackSpeed);
        }
    }

    /// <summary>
    /// Relays the owner's contact, with the weapon, where it stood and the attack's target, to the other machines.
    /// </summary>
    private void HandleLocalAttackContacted(int attack, Vector2 direction, Vector2 position, UnitTarget attackTarget)
    {
        if (IsSpawned)
        {
            bool hasTarget = TryReference(attackTarget.Health, out NetworkObjectReference target);
            AttackContactRpc(weapon.WeaponIndex, (byte)attack, direction, position, target, hasTarget);
        }
    }

    /// <summary>
    /// Runs on every machine but the owner: plays the contact of the owner's attack and lands it around
    /// <paramref name="attackerPosition"/>, where the owner stood, rather than around this lagging copy: the host
    /// resolves a melee hit, and every machine flies a projectile toward <paramref name="target"/>, which only hits on
    /// the host.
    /// </summary>
    [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Owner)]
    private void AttackContactRpc(int weaponIndex, byte attack, Vector2 direction, Vector2 attackerPosition, NetworkObjectReference target, bool hasTarget)
    {
        if (weapon == null)
        {
            return;
        }

        weapon.PlayRemoteContact();
        weapon.ResolveRemoteContact(weaponIndex, attack, direction, attackerPosition, hasTarget ? ResolveTarget(target) : default);
    }

    /// <summary>
    /// Tells the other machines the owner's attack ended early, so their copies stop it.
    /// </summary>
    private void HandleLocalAttackCancelled()
    {
        if (IsSpawned)
        {
            AttackCancelledRpc();
        }
    }

    /// <summary>
    /// Runs on every machine but the owner: stops the owner's attack on the copy.
    /// </summary>
    [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Owner)]
    private void AttackCancelledRpc()
    {
        if (weapon != null)
        {
            weapon.StopRemoteAttack();
        }
    }

    /// <summary>
    /// Relays a cast the owner started, with its aim, any networked target and the weapon equipped as it started, to
    /// the other machines, and remembers that weapon for its completion.
    /// </summary>
    private void HandleLocalCast(AbilityCast cast)
    {
        castWeaponIndex = CurrentWeaponIndex();
        bool hasTarget = TryReference(cast.Aim.Target.Health, out NetworkObjectReference target);
        CastRpc(cast.Slot, castWeaponIndex, cast.Aim.Point, cast.Aim.Direction, target, hasTarget);
    }

    /// <summary>
    /// Tells the other machines the owner's cast was cancelled (a stun, a silence, death, a teleport), so their copies
    /// stop it; a completed cast sends nothing here.
    /// </summary>
    private void HandleLocalCastEnded(int slot, bool completed)
    {
        if (!completed && IsSpawned)
        {
            CastCancelledRpc();
        }
    }

    /// <summary>
    /// Runs on every machine but the owner: stops the owner's cast on the copy, which will never complete.
    /// </summary>
    [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Owner)]
    private void CastCancelledRpc()
    {
        if (abilities != null)
        {
            abilities.StopRemoteCast();
        }
    }

    /// <summary>
    /// On a client owner, sends a completed cast, the weapon it started with (a running cast keeps its ability across
    /// a weapon switch) and the owner's position to the host to resolve; the host resolves its own casts locally.
    /// </summary>
    private void HandleLocalCastCompleted(AbilityCast cast)
    {
        if (IsServer)
        {
            return;
        }

        bool hasTarget = TryReference(cast.Aim.Target.Health, out NetworkObjectReference target);
        CastCompletedRpc(cast.Slot, castWeaponIndex, cast.Aim.Point, cast.Aim.Direction, target, hasTarget, transform.position);
    }

    /// <summary>
    /// Runs on the host: resolves the effect of a cast the owner completed at <paramref name="casterPosition"/> with
    /// the catalog's weapon <paramref name="weaponIndex"/> equipped.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void CastCompletedRpc(int slot, int weaponIndex, Vector2 point, Vector2 direction, NetworkObjectReference target, bool hasTarget, Vector2 casterPosition)
    {
        if (abilities != null)
        {
            abilities.ResolveRemoteCast(slot, new CastAim(point, direction, hasTarget ? ResolveTarget(target) : default), casterPosition, WeaponFor(weaponIndex));
        }
    }

    /// <summary>
    /// A network reference to <paramref name="target"/>; false (and a default reference) when there is no target or it
    /// is not a spawned network object.
    /// </summary>
    private static bool TryReference(Health target, out NetworkObjectReference reference)
    {
        NetworkObject targetObject = target != null ? target.GetComponent<NetworkObject>() : null;
        bool hasTarget = targetObject != null && targetObject.IsSpawned;
        reference = hasTarget ? new NetworkObjectReference(targetObject) : default;
        return hasTarget;
    }

    /// <summary>
    /// Runs on every machine but the owner: replays a cast the owner started with the catalog's weapon
    /// <paramref name="weaponIndex"/> equipped.
    /// </summary>
    [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Owner)]
    private void CastRpc(int slot, int weaponIndex, Vector2 point, Vector2 direction, NetworkObjectReference target, bool hasTarget)
    {
        if (abilities != null)
        {
            abilities.PlayRemoteCast(slot, new CastAim(point, direction, hasTarget ? ResolveTarget(target) : default), WeaponFor(weaponIndex));
        }
    }

    /// <summary>
    /// On the owner, writes the equipped weapon's catalog index when the weapon changes.
    /// </summary>
    private void HandleLocalWeaponChanged(PlayerWeapon _)
    {
        WriteWeapon();
    }

    /// <summary>
    /// On the owner, writes the equipped weapon's catalog index once it differs from what the other machines know:
    /// nothing while the player still holds its starting weapon (the unwritten value means that weapon everywhere), and
    /// nothing for a weapon outside the catalog, so other machines keep the last one they were told of.
    /// </summary>
    private void WriteWeapon()
    {
        int index = weapon.WeaponIndex;
        bool unchanged = index == equippedWeapon.Value || (equippedWeapon.Value == NoWeapon && weapon.CurrentWeapon == weapon.StartingWeapon);
        if (IsSpawned && IsOwner && index != NoWeapon && !unchanged)
        {
            equippedWeapon.Value = index;
        }
    }

    /// <summary>
    /// On the other machines, equips the weapon the owner switched to.
    /// </summary>
    private void HandleWeaponReplicated(int previous, int current)
    {
        EquipReplicatedWeapon(current);
    }

    /// <summary>
    /// Equips the catalog's weapon at <paramref name="index"/> on this copy when it differs from the copy's, leaving
    /// the attack the copy shows alone (see <see cref="PlayerWeaponController.EquipOnCopy"/>); the unwritten value, the
    /// owner's starting weapon, keeps the copy's.
    /// </summary>
    private void EquipReplicatedWeapon(int index)
    {
        if (weapon == null || weapon.Catalog == null)
        {
            return;
        }

        PlayerWeapon replicated = weapon.Catalog.Get(index);
        if (replicated != null && replicated != weapon.CurrentWeapon)
        {
            weapon.EquipOnCopy(replicated);
        }
    }

    /// <summary>
    /// The equipped weapon's catalog index, or -1 without a weapon controller.
    /// </summary>
    private int CurrentWeaponIndex()
    {
        return weapon != null ? weapon.WeaponIndex : NoWeapon;
    }

    /// <summary>
    /// The catalog's weapon at <paramref name="index"/>, or this copy's own outside the catalog.
    /// </summary>
    private PlayerWeapon WeaponFor(int index)
    {
        return weapon != null ? weapon.WeaponFor(index) : null;
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
