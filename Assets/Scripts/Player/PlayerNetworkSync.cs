using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using VContainer;

/// <summary>
/// Replicates a networked player. The owner binds it as the local player, writes its move (its actual speed over the
/// base move speed, so a slowed or hasted copy shows the same gait), facing, equipped weapon (its
/// <see cref="WeaponCatalog"/> index), look (its <see cref="CharacterLookCatalog"/> index) and colours (a
/// <see cref="CharacterColourPicks"/>, a swatch per colour choice) and parts (a <see cref="CharacterPartPicks"/>, an
/// option per part slot; all of which binding it makes follow the local settings) to owner network variables, and sends its attacks' starts, contacts and early
/// ends and its casts' starts, completions and cancels to the other machines, which play them on their copy; the host
/// also resolves a client's completed casts.
/// Attack and cast messages name the weapon they were made with, so they play and land right even before a weapon
/// switch reaches a machine. Every machine but the owner lands a contact around where the owner stood: only the host
/// resolves a melee hit, and every machine flies a projectile, which only hits on the host; the host also resolves a
/// client's completed casts there. The owner's teleports (arriving at an entry, a waystone included) snap on the other
/// machines instead of sliding there. Every other machine binds it as a remote copy with a kinematic body showing that
/// movement, equips the replicated weapon and shows the replicated look, colours and parts, a late joiner included. The host
/// writes the weapon a client's player holds in the save (<see cref="SetSavedWeapon"/>) to a server network variable as
/// it spawns: the owner equips it, and every copy shows it until the owner writes a weapon of its own.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController))]
public class PlayerNetworkSync : NetworkBehaviour
{
    private const float ChangeThreshold = 0.01f;
    private const int NoWeapon = -1;
    private const int NoLook = -1;

    private readonly NetworkVariable<Vector2> move = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<Vector2> facing = new(Vector2.down, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<int> equippedWeapon = new(NoWeapon, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<int> savedWeapon = new(NoWeapon, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> look = new(NoLook, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<CharacterColourPicks> colours = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<CharacterPartPicks> parts = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private PlayerController controller;
    private PlayerWeaponController weapon;
    private PlayerLook playerLook;
    private PlayerAbilities abilities;
    private CharacterVisuals visuals;
    private Rigidbody2D body;
    private NetworkTransform networkTransform;
    private PlayerBinder binder;
    private PlayerHandle handle;
    private int castWeaponIndex = NoWeapon;
    private int pendingSavedWeapon = NoWeapon;

    /// <summary>
    /// The player's registry handle on this machine once it is bound, else null.
    /// </summary>
    internal PlayerHandle Handle => handle;

    /// <summary>
    /// Caches the controller, weapon, look, abilities, body and network transform on this player.
    /// </summary>
    private void Awake()
    {
        controller = GetComponent<PlayerController>();
        weapon = GetComponent<PlayerWeaponController>();
        playerLook = GetComponent<PlayerLook>();
        abilities = GetComponent<PlayerAbilities>();
        visuals = GetComponentInChildren<CharacterVisuals>(true);
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
    /// Host only, before spawning: the catalog index of the weapon the save gives this client's player, written as it
    /// spawns for its owner to equip.
    /// </summary>
    public void SetSavedWeapon(int index)
    {
        pendingSavedWeapon = index;
    }

    /// <summary>
    /// The host writes the saved weapon it was given. The owner binds the player as local (which shows the look, colours
    /// and parts the local settings choose), equips the saved weapon, now or when it arrives, writes its weapon, look,
    /// colours and parts, and starts relaying its weapon switches, look, colour and part changes, attacks, teleports and
    /// casts; any other machine makes the body kinematic without interpolation, binds it as remote, shows the current
    /// replicated movement, equips the replicated weapon and shows the replicated look, colours and parts, following them
    /// from then on. Logs an error and does nothing
    /// when the prefab was spawned without injection.
    /// </summary>
    public override void OnNetworkSpawn()
    {
        if (binder == null)
        {
            Debug.LogError($"{name} was spawned without a {nameof(PlayerBinder)}; register the player prefab with the Gameplay scope's resolver.", this);
            return;
        }

        if (visuals != null)
        {
            visuals.SeedIdleAnimations(unchecked((int)NetworkObjectId));
        }

        if (IsServer && pendingSavedWeapon != NoWeapon)
        {
            savedWeapon.Value = pendingSavedWeapon;
        }

        if (IsOwner)
        {
            handle = binder.BindLocal(controller);
            controller.Teleported += HandleLocalTeleported;
            if (weapon != null)
            {
                EquipSavedWeapon(savedWeapon.Value);
                savedWeapon.OnValueChanged += HandleSavedWeaponReplicated;
                weapon.AttackStarted += HandleLocalAttackStarted;
                weapon.AttackContacted += HandleLocalAttackContacted;
                weapon.AttackCancelled += HandleLocalAttackCancelled;
                weapon.EquippedWeaponChanged += HandleLocalWeaponChanged;
                WriteWeapon();
            }

            if (playerLook != null)
            {
                playerLook.Chosen += HandleLocalLookChosen;
                HandleLocalLookChosen(playerLook.Index);
                playerLook.ColoursChosen += HandleLocalColoursChosen;
                HandleLocalColoursChosen(playerLook.ShownColours);
                playerLook.PartsChosen += HandleLocalPartsChosen;
                HandleLocalPartsChosen(playerLook.ShownParts);
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
        savedWeapon.OnValueChanged += HandleWeaponReplicated;
        EquipReplicatedWeapon(CopyWeaponIndex());
        look.OnValueChanged += HandleLookReplicated;
        colours.OnValueChanged += HandleColoursReplicated;
        parts.OnValueChanged += HandlePartsReplicated;
        if (playerLook != null)
        {
            playerLook.ShowParts(parts.Value);
            playerLook.Show(look.Value);
            playerLook.ShowColours(colours.Value);
        }
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
        savedWeapon.OnValueChanged -= HandleWeaponReplicated;
        savedWeapon.OnValueChanged -= HandleSavedWeaponReplicated;
        look.OnValueChanged -= HandleLookReplicated;
        colours.OnValueChanged -= HandleColoursReplicated;
        parts.OnValueChanged -= HandlePartsReplicated;
        if (playerLook != null)
        {
            playerLook.Chosen -= HandleLocalLookChosen;
            playerLook.ColoursChosen -= HandleLocalColoursChosen;
            playerLook.PartsChosen -= HandleLocalPartsChosen;
            playerLook.StopFollowing();
        }

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
    /// Sends a completed cast, the weapon it started with (a running cast keeps its ability across a weapon switch) and
    /// the owner's position to the other machines (Docs/KnownGaps.md, Phase 4): their copies show its release, and the
    /// host resolves a client's there; the host resolves its own casts locally.
    /// </summary>
    private void HandleLocalCastCompleted(AbilityCast cast)
    {
        if (!IsSpawned)
        {
            return;
        }

        bool hasTarget = TryReference(cast.Aim.Target.Health, out NetworkObjectReference target);
        CastCompletedRpc(cast.Slot, castWeaponIndex, cast.Aim.Point, cast.Aim.Direction, target, hasTarget, transform.position);
    }

    /// <summary>
    /// Runs on every machine but the owner: the copy shows the release of the owner's cast, and the host then resolves
    /// its effect at <paramref name="casterPosition"/> with the catalog's weapon <paramref name="weaponIndex"/>
    /// equipped, so the release shows on the update the cast lands.
    /// </summary>
    [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Owner)]
    private void CastCompletedRpc(int slot, int weaponIndex, Vector2 point, Vector2 direction, NetworkObjectReference target, bool hasTarget, Vector2 casterPosition)
    {
        if (abilities == null)
        {
            return;
        }

        abilities.CompleteRemoteCast();
        if (IsServer)
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
    /// On the owner, writes the catalog index of the look the local settings chose when it differs from what the other
    /// machines know (-1, the copies' own look, until the first look shows).
    /// </summary>
    private void HandleLocalLookChosen(int index)
    {
        if (IsSpawned && IsOwner && index != look.Value)
        {
            look.Value = index;
        }
    }

    /// <summary>
    /// On the other machines, shows the look the owner chose; the unwritten value keeps the copy's own.
    /// </summary>
    private void HandleLookReplicated(int previous, int current)
    {
        if (playerLook != null)
        {
            playerLook.Show(current);
        }
    }

    /// <summary>
    /// On the owner, writes the colours the local settings chose when they differ from what the other machines know
    /// (the default, the look's own colours, until written).
    /// </summary>
    private void HandleLocalColoursChosen(CharacterColourPicks picks)
    {
        if (IsSpawned && IsOwner && !picks.Equals(colours.Value))
        {
            colours.Value = picks;
        }
    }

    /// <summary>
    /// On the owner, writes the parts the local settings chose when they differ from what the other machines know (the
    /// default, the look's own parts, until written).
    /// </summary>
    private void HandleLocalPartsChosen(CharacterPartPicks picks)
    {
        if (IsSpawned && IsOwner && !picks.Equals(parts.Value))
        {
            parts.Value = picks;
        }
    }

    /// <summary>
    /// On the other machines, shows the parts the owner chose.
    /// </summary>
    private void HandlePartsReplicated(CharacterPartPicks previous, CharacterPartPicks current)
    {
        if (playerLook != null)
        {
            playerLook.ShowParts(current);
        }
    }

    /// <summary>
    /// On the other machines, shows the colours the owner chose.
    /// </summary>
    private void HandleColoursReplicated(CharacterColourPicks previous, CharacterColourPicks current)
    {
        if (playerLook != null)
        {
            playerLook.ShowColours(current);
        }
    }

    /// <summary>
    /// On the other machines, equips the weapon the owner switched to, or the saved one until the owner writes one,
    /// when either changes.
    /// </summary>
    private void HandleWeaponReplicated(int previous, int current)
    {
        EquipReplicatedWeapon(CopyWeaponIndex());
    }

    /// <summary>
    /// The catalog index of the weapon a copy shows: the one its owner wrote, else the one its save holds (the
    /// unwritten value of both keeps the copy's own, the starting weapon).
    /// </summary>
    private int CopyWeaponIndex()
    {
        return equippedWeapon.Value != NoWeapon ? equippedWeapon.Value : savedWeapon.Value;
    }

    /// <summary>
    /// On the owner, equips the saved weapon the host wrote after the player spawned.
    /// </summary>
    private void HandleSavedWeaponReplicated(int previous, int current)
    {
        EquipSavedWeapon(current);
    }

    /// <summary>
    /// On the owner, equips the catalog's weapon at <paramref name="index"/>, the one the save holds, unless it is the
    /// weapon already held or there is none (the unwritten value).
    /// </summary>
    private void EquipSavedWeapon(int index)
    {
        PlayerWeapon saved = weapon != null && weapon.Catalog != null ? weapon.Catalog.Get(index) : null;
        if (saved != null && saved != weapon.CurrentWeapon)
        {
            weapon.Equip(saved);
        }
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
