using Unity.Netcode;
using UnityEngine;

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

    private void Awake()
    {
        brain = GetComponent<MobController>();
        motor = GetComponent<MobMotor2D>();
        body = GetComponent<Rigidbody2D>();
    }

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

    public override void OnNetworkDespawn()
    {
        motor.AttackAnimationPlayed -= HandleAttackAnimationPlayed;
        moving.OnValueChanged -= HandleMovingChanged;
        facing.OnValueChanged -= HandleFacingChanged;
    }

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

    private void HandleAttackAnimationPlayed(Vector2 direction)
    {
        AttackRpc(direction);
    }

    private void HandleMovingChanged(bool previous, bool current)
    {
        motor.ShowRemoteMovement(current, facing.Value);
    }

    private void HandleFacingChanged(Vector2 previous, Vector2 current)
    {
        motor.ShowRemoteMovement(moving.Value, current);
    }

    [Rpc(SendTo.NotServer)]
    private void AttackRpc(Vector2 direction)
    {
        motor.PlayAttackAnimation(direction);
    }
}
