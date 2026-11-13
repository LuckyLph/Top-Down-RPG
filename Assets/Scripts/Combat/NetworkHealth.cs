using Unity.Netcode;
using UnityEngine;
using VContainer;

/// <summary>
/// Replicates a unit's <see cref="Health"/> from the server: a server-written variable carries the current HP for
/// late joiners, and RPCs replay each hit (immune ones included), heal and restore on clients through
/// <see cref="DamageService"/> so their popups, hit feedback and death reactions run, then correct HP to the server's
/// value. A hit, an immune hit and a heal name their source unit when it is a spawned network object, so a client's hit
/// feedback and popups know whether its own player landed it (Docs/UiRevamp.md, Phase 3), and a hit carries the point
/// it came from as the host resolved it (Docs/KnownGaps.md, Phase 4), so every machine turns its feedback the same way.
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
    /// Server: stores the new HP and sends the hit to clients, naming its source unit when it is spawned and the point
    /// it came from when it has one.
    /// </summary>
    private void HandleServerDamaged(Health.DamageEvent damage)
    {
        current.Value = damage.CurrentHealth;
        bool hasSource = TryGetSourceObject(damage.Source, out NetworkObject source);
        HitRpc(damage.Amount, damage.Type, damage.Flags, damage.CurrentHealth, hasSource ? new NetworkObjectReference(source) : default, hasSource, damage.Origin ?? Vector2.zero, damage.Origin.HasValue);
    }

    /// <summary>
    /// Server: sends an immune hit to clients as a zero-damage hit, naming its source unit when it is spawned, so they
    /// show the immune popup.
    /// </summary>
    private void HandleServerImmuneHit(DamageType type, DamageFlags flags, GameObject sourceObject)
    {
        bool hasSource = TryGetSourceObject(sourceObject, out NetworkObject source);
        HitRpc(0, type, flags, health.CurrentHealth, hasSource ? new NetworkObjectReference(source) : default, hasSource, Vector2.zero, false);
    }

    /// <summary>
    /// Server: stores the new HP and sends the heal to clients, naming its source unit when it is spawned.
    /// </summary>
    private void HandleServerHealed(Health _, int amount, GameObject sourceObject)
    {
        current.Value = health.CurrentHealth;
        bool hasSource = TryGetSourceObject(sourceObject, out NetworkObject source);
        HealedRpc(amount, health.CurrentHealth, hasSource ? new NetworkObjectReference(source) : default, hasSource);
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
    /// Clients: replays the server's hit through <see cref="DamageService.ApplyReplicatedHit"/> (popups, hit feedback,
    /// death) with this client's copy of its source when it names one this client has and the point it came from when it
    /// has one, then syncs HP to <paramref name="healthAfter"/> if it still differs.
    /// </summary>
    [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
    private void HitRpc(int amount, DamageType type, DamageFlags flags, int healthAfter, NetworkObjectReference source, bool hasSource, Vector2 origin, bool hasOrigin)
    {
        if (damageService != null)
        {
            damageService.ApplyReplicatedHit(receiver, amount, type, flags, SourceObject(source, hasSource), hasOrigin ? origin : (Vector2?)null);
        }

        if (health.CurrentHealth != healthAfter)
        {
            health.SyncTo(healthAfter);
        }
    }

    /// <summary>
    /// This client's copy of the unit <paramref name="source"/> names, or null without one or for a unit this client
    /// does not have.
    /// </summary>
    private GameObject SourceObject(NetworkObjectReference source, bool hasSource)
    {
        return hasSource && source.TryGet(out NetworkObject sourceNetworkObject, NetworkManager) ? sourceNetworkObject.gameObject : null;
    }

    /// <summary>
    /// The spawned network object of the unit <paramref name="source"/> belongs to (its <see cref="DamageReceiver"/>'s),
    /// if any.
    /// </summary>
    internal static bool TryGetSourceObject(GameObject source, out NetworkObject networkObject)
    {
        DamageReceiver unit = CombatFactions.UnitOf(source);
        networkObject = null;
        return unit != null && unit.TryGetComponent(out networkObject) && networkObject.IsSpawned;
    }

    /// <summary>
    /// Clients: replays the server's heal through <see cref="DamageService.ApplyReplicatedHeal"/> with this client's copy
    /// of its source when it names one this client has, then syncs HP to <paramref name="healthAfter"/> if it still
    /// differs.
    /// </summary>
    [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
    private void HealedRpc(int amount, int healthAfter, NetworkObjectReference source, bool hasSource)
    {
        if (damageService != null)
        {
            damageService.ApplyReplicatedHeal(receiver, amount, SourceObject(source, hasSource));
        }

        if (health.CurrentHealth != healthAfter)
        {
            health.SyncTo(healthAfter);
        }
    }

    /// <summary>
    /// Clients: refills the unit's HP, raising <see cref="Health.Restored"/> so revive reactions run.
    /// </summary>
    [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
    private void RestoredRpc()
    {
        health.Restore();
    }
}
