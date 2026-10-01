using Unity.Netcode;
using UnityEngine;
using VContainer;

[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(DamageReceiver))]
public class NetworkHealth : NetworkBehaviour
{
    private const int UnknownHealth = -1;

    private readonly NetworkVariable<int> current = new(UnknownHealth, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Health health;
    private DamageReceiver receiver;
    private DamageService damageService;

    private void Awake()
    {
        health = GetComponent<Health>();
        receiver = GetComponent<DamageReceiver>();
    }

    [Inject]
    public void Construct(DamageService damage)
    {
        damageService = damage;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            current.Value = health.CurrentHealth;
            health.Damaged += HandleServerDamaged;
            health.Restored += HandleServerRestored;
            return;
        }

        if (damageService == null)
        {
            Debug.LogError($"{name} was spawned without a {nameof(DamageService)}; register its prefab with a scope that provides one.", this);
        }

        if (current.Value != UnknownHealth)
        {
            health.SyncTo(current.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        health.Damaged -= HandleServerDamaged;
        health.Restored -= HandleServerRestored;
    }

    private void HandleServerDamaged(Health.DamageEvent damage)
    {
        current.Value = damage.CurrentHealth;
        DamagedRpc(damage.Amount, damage.CurrentHealth);
    }

    private void HandleServerRestored(Health _)
    {
        current.Value = health.CurrentHealth;
        RestoredRpc();
    }

    [Rpc(SendTo.NotServer)]
    private void DamagedRpc(int amount, int healthAfter)
    {
        if (damageService != null)
        {
            damageService.ApplyReplicatedDamage(receiver, amount);
        }

        if (health.CurrentHealth != healthAfter)
        {
            health.SyncTo(healthAfter);
        }
    }

    [Rpc(SendTo.NotServer)]
    private void RestoredRpc()
    {
        health.Restore();
    }
}
