using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Replicates a mob's movement animation and attacks. The host writes <see cref="MobMotor2D.IsMoving"/> and
/// <see cref="MobMotor2D.FacingDirection"/> into server-written network variables and forwards attack animations to
/// clients; clients disable the brain, make the body kinematic and show the replicated state on the motor.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MobController))]
[RequireComponent(typeof(MobMotor2D))]
public class MobNetworkSync : NetworkBehaviour
{
    private const float FacingChangeThreshold = 0.01f;

    private readonly NetworkVariable<bool> moving = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<Vector2> facing = new(Vector2.down, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private MobController brain;
    private MobMotor2D motor;
    private Rigidbody2D body;

    /// <summary>
    /// Caches the brain, motor and rigidbody.
    /// </summary>
    private void Awake()
    {
        brain = GetComponent<MobController>();
        motor = GetComponent<MobMotor2D>();
        body = GetComponent<Rigidbody2D>();
    }

    /// <summary>
    /// On the host, starts forwarding the motor's attack animations. On a client, disables the AI, makes the body
    /// kinematic without interpolation, follows the replicated movement and shows its current value.
    /// </summary>
    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            motor.AttackAnimationPlayed += HandleAttackAnimationPlayed;
            return;
        }

        brain.enabled = false;
        if (body != null)
        {
            body.bodyType = RigidbodyType2D.Kinematic;
            body.interpolation = RigidbodyInterpolation2D.None;
            body.linearVelocity = Vector2.zero;
        }

        moving.OnValueChanged += HandleMovingChanged;
        facing.OnValueChanged += HandleFacingChanged;
        motor.ShowRemoteMovement(moving.Value, facing.Value);
    }

    /// <summary>
    /// Removes the attack-animation and network-variable subscriptions made on spawn.
    /// </summary>
    public override void OnNetworkDespawn()
    {
        motor.AttackAnimationPlayed -= HandleAttackAnimationPlayed;
        moving.OnValueChanged -= HandleMovingChanged;
        facing.OnValueChanged -= HandleFacingChanged;
    }

    /// <summary>
    /// Host only: writes the motor's moving flag and facing into the network variables when they change, ignoring
    /// facing changes below a small threshold.
    /// </summary>
    private void Update()
    {
        if (!IsSpawned || !IsServer)
        {
            return;
        }

        if (moving.Value != motor.IsMoving)
        {
            moving.Value = motor.IsMoving;
        }

        Vector2 currentFacing = motor.FacingDirection;
        if ((currentFacing - facing.Value).sqrMagnitude > FacingChangeThreshold * FacingChangeThreshold)
        {
            facing.Value = currentFacing;
        }
    }

    /// <summary>
    /// Host: sends an attack animation the motor just played to the clients.
    /// </summary>
    private void HandleAttackAnimationPlayed(Vector2 direction)
    {
        AttackRpc(direction);
    }

    /// <summary>
    /// Client: shows the new moving flag with the current replicated facing.
    /// </summary>
    private void HandleMovingChanged(bool previous, bool current)
    {
        motor.ShowRemoteMovement(current, facing.Value);
    }

    /// <summary>
    /// Client: shows the new facing with the current replicated moving flag.
    /// </summary>
    private void HandleFacingChanged(Vector2 previous, Vector2 current)
    {
        motor.ShowRemoteMovement(moving.Value, current);
    }

    /// <summary>
    /// Runs on every client but not on the host: plays the attack animation toward <paramref name="direction"/>.
    /// </summary>
    [Rpc(SendTo.NotServer)]
    private void AttackRpc(Vector2 direction)
    {
        motor.PlayAttackAnimation(direction);
    }
}
