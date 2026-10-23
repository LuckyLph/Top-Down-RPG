using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Replicates a mob's movement and attacks (Docs/Animation.md, Phase 7). The host writes <see cref="MobMotor2D.CurrentMove"/>
/// (its actual velocity over its move speed, so a slowed copy shows the same gait) and
/// <see cref="MobMotor2D.FacingDirection"/> into server-written network variables, and sends its
/// <see cref="MeleeDamageDealer"/>'s attack start (which attack, toward which direction), contact and early end to the
/// clients. Clients disable the brain, make the body kinematic, show the replicated movement on the motor, and play
/// each attack on their copy: the windup on the copy's own clock, held on its last frame until the contact arrives,
/// then the contact and recovery; an early end stops it. Only the host resolves hits. A late joiner sees attacks from
/// their next start; a contact or end without a start does nothing.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MobController))]
[RequireComponent(typeof(MobMotor2D))]
public class MobNetworkSync : NetworkBehaviour
{
    private const float ChangeThreshold = 0.01f;
    private const byte OnlyAttack = 0;

    private readonly NetworkVariable<Vector2> move = new(Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<Vector2> facing = new(Vector2.down, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private MobController brain;
    private MobMotor2D motor;
    private MeleeDamageDealer dealer;
    private Rigidbody2D body;

    /// <summary>
    /// Caches the brain, motor, damage dealer and rigidbody.
    /// </summary>
    private void Awake()
    {
        brain = GetComponent<MobController>();
        motor = GetComponent<MobMotor2D>();
        dealer = GetComponent<MeleeDamageDealer>();
        body = GetComponent<Rigidbody2D>();
    }

    /// <summary>
    /// On the host, starts relaying the damage dealer's attacks. On a client, disables the AI, makes the body kinematic
    /// without interpolation, follows the replicated movement and shows its current value.
    /// </summary>
    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            if (dealer != null)
            {
                dealer.AttackStarted += HandleAttackStarted;
                dealer.AttackContacted += HandleAttackContacted;
                dealer.AttackCancelled += HandleAttackCancelled;
            }

            return;
        }

        brain.enabled = false;
        if (body != null)
        {
            body.bodyType = RigidbodyType2D.Kinematic;
            body.interpolation = RigidbodyInterpolation2D.None;
            body.linearVelocity = Vector2.zero;
        }

        move.OnValueChanged += HandleMoveChanged;
        facing.OnValueChanged += HandleFacingChanged;
        motor.ShowRemoteMovement(move.Value, facing.Value);
    }

    /// <summary>
    /// Removes the attack and network-variable subscriptions made on spawn.
    /// </summary>
    public override void OnNetworkDespawn()
    {
        if (dealer != null)
        {
            dealer.AttackStarted -= HandleAttackStarted;
            dealer.AttackContacted -= HandleAttackContacted;
            dealer.AttackCancelled -= HandleAttackCancelled;
        }

        move.OnValueChanged -= HandleMoveChanged;
        facing.OnValueChanged -= HandleFacingChanged;
    }

    /// <summary>
    /// Host only: writes the motor's move and facing into the network variables when either changed past a small
    /// threshold.
    /// </summary>
    private void Update()
    {
        if (!IsSpawned || !IsServer)
        {
            return;
        }

        Vector2 currentMove = motor.CurrentMove;
        if ((currentMove - move.Value).sqrMagnitude > ChangeThreshold * ChangeThreshold)
        {
            move.Value = currentMove;
        }

        Vector2 currentFacing = motor.FacingDirection;
        if ((currentFacing - facing.Value).sqrMagnitude > ChangeThreshold * ChangeThreshold)
        {
            facing.Value = currentFacing;
        }
    }

    /// <summary>
    /// Host: sends the start of an attack to the clients.
    /// </summary>
    private void HandleAttackStarted(Vector2 direction)
    {
        if (IsSpawned)
        {
            AttackStartedRpc(OnlyAttack, direction);
        }
    }

    /// <summary>
    /// Host: sends an attack's contact to the clients.
    /// </summary>
    private void HandleAttackContacted()
    {
        if (IsSpawned)
        {
            AttackContactRpc();
        }
    }

    /// <summary>
    /// Host: tells the clients an attack ended early, so their copies stop it.
    /// </summary>
    private void HandleAttackCancelled()
    {
        if (IsSpawned)
        {
            AttackEndedRpc();
        }
    }

    /// <summary>
    /// Client: shows the new move with the current replicated facing.
    /// </summary>
    private void HandleMoveChanged(Vector2 previous, Vector2 current)
    {
        motor.ShowRemoteMovement(current, facing.Value);
    }

    /// <summary>
    /// Client: shows the new facing with the current replicated move.
    /// </summary>
    private void HandleFacingChanged(Vector2 previous, Vector2 current)
    {
        motor.ShowRemoteMovement(move.Value, current);
    }

    /// <summary>
    /// Runs on every client but not on the host: plays attack <paramref name="attack"/> (0, the mob's only one) toward
    /// <paramref name="direction"/> on this copy, on the timeline of its own config, its windup holding until the
    /// contact arrives. Visual only.
    /// </summary>
    [Rpc(SendTo.NotServer)]
    private void AttackStartedRpc(byte attack, Vector2 direction)
    {
        if (attack == OnlyAttack && dealer != null)
        {
            motor.PlayAttack(direction, dealer.Timeline, true);
        }
    }

    /// <summary>
    /// Runs on every client but not on the host: plays the contact of the attack this copy shows, from now.
    /// </summary>
    [Rpc(SendTo.NotServer)]
    private void AttackContactRpc()
    {
        motor.ShowAttackContact();
    }

    /// <summary>
    /// Runs on every client but not on the host: stops the attack this copy shows, which ended early on the host.
    /// </summary>
    [Rpc(SendTo.NotServer)]
    private void AttackEndedRpc()
    {
        motor.StopAttack();
    }
}
