using Unity.Netcode;
using UnityEngine;
using VContainer;

/// <summary>
/// Replicates a unit's <see cref="Health"/> from the server: a server-written variable carries the current HP for
/// late joiners, and RPCs replay each hit (immune ones included), heal and restore on clients through
/// <see cref="DamageService"/> so their popups and death reactions run, then correct HP to the server's value.
/// </summary>
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

    /// <summary>
    /// Caches the sibling health and damage receiver.
    /// </summary>
    private void Awake()
    {
        health = GetComponent<Health>();
        receiver = GetComponent<DamageReceiver>();
    }

    /// <summary>
    /// Injects the service clients replay the server's hits and heals through.
    /// </summary>
    [Inject]
    public void Construct(DamageService damage)
    {
        damageService = damage;
    }

    /// <summary>
    /// On the server, publishes the current HP and starts forwarding damage, immune hits, heals and restores. On a
    /// client, logs an error when no <see cref="DamageService"/> was injected and syncs to the replicated HP if the
    /// server has written one.
    /// </summary>
    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            current.Value = health.CurrentHealth;
            health.Damaged += HandleServerDamaged;
            health.Healed += HandleServerHealed;
            health.Restored += HandleServerRestored;
            receiver.ImmuneHit += HandleServerImmuneHit;
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

    /// <summary>
    /// Stops forwarding the server-side health events (harmless on clients, which never subscribed).
    /// </summary>
    public override void OnNetworkDespawn()
    {
        health.Damaged -= HandleServerDamaged;
        health.Healed -= HandleServerHealed;
        health.Restored -= HandleServerRestored;
        receiver.ImmuneHit -= HandleServerImmuneHit;
    }

    /// <summary>
    /// Server: stores the new HP and sends the hit to clients.
    /// </summary>
    private void HandleServerDamaged(Health.DamageEvent damage)
    {
        current.Value = damage.CurrentHealth;
        HitRpc(damage.Amount, damage.Type, damage.Flags, damage.CurrentHealth);
    }

    /// <summary>
    /// Server: sends an immune hit to clients as a zero-damage hit so they show the immune popup.
    /// </summary>
    private void HandleServerImmuneHit(DamageType type, DamageFlags flags)
    {
        HitRpc(0, type, flags, health.CurrentHealth);
    }

    /// <summary>
    /// Server: stores the new HP and sends the heal to clients.
    /// </summary>
    private void HandleServerHealed(Health _, int amount)
    {
        current.Value = health.CurrentHealth;
        HealedRpc(amount, health.CurrentHealth);
    }

    /// <summary>
    /// Server: stores the refilled HP and tells clients to restore the unit.
    /// </summary>
    private void HandleServerRestored(Health _)
    {
        current.Value = health.CurrentHealth;
        RestoredRpc();
    }

    /// <summary>
    /// Clients: replays the server's hit through <see cref="DamageService.ApplyReplicatedHit"/> (popups, death), then
    /// syncs HP to <paramref name="healthAfter"/> if it still differs.
    /// </summary>
    [Rpc(SendTo.NotServer)]
    private void HitRpc(int amount, DamageType type, DamageFlags flags, int healthAfter)
    {
        if (damageService != null)
        {
            damageService.ApplyReplicatedHit(receiver, amount, type, flags);
        }

        if (health.CurrentHealth != healthAfter)
        {
            health.SyncTo(healthAfter);
        }
    }

    /// <summary>
    /// Clients: replays the server's heal through <see cref="DamageService.ApplyReplicatedHeal"/>, then syncs HP to
    /// <paramref name="healthAfter"/> if it still differs.
    /// </summary>
    [Rpc(SendTo.NotServer)]
    private void HealedRpc(int amount, int healthAfter)
    {
        if (damageService != null)
        {
            damageService.ApplyReplicatedHeal(receiver, amount);
        }

        if (health.CurrentHealth != healthAfter)
        {
            health.SyncTo(healthAfter);
        }
    }

    /// <summary>
    /// Clients: refills the unit's HP, raising <see cref="Health.Restored"/> so revive reactions run.
    /// </summary>
    [Rpc(SendTo.NotServer)]
    private void RestoredRpc()
    {
        health.Restore();
    }
}
