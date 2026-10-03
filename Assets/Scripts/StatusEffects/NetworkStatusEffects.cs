using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VContainer;

/// <summary>
/// Replicates a unit's statuses. Host: rewrites a server-only list of <see cref="StatusEntry"/> whenever the set
/// changes, forwards blocked statuses for Immune popups, and holds a client's player while that client loads.
/// Clients: rebuild their mirror set from the list on spawn and after every change, counting remaining time down
/// from the server time of each write.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(StatusEffects))]
public class NetworkStatusEffects : NetworkBehaviour, IStatusHold
{
    private readonly NetworkList<StatusEntry> entries = new(null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly List<StatusSnapshot> mirror = new();

    private StatusEffects statuses;
    private DamageReceiver receiver;
    private StatusEffectCatalog catalog;
    private StatusEffectService statusService;
    private IClientReadiness readiness;
    private bool mirrorDirty;

    public bool IsHeld => IsSpawned && IsServer && !IsOwnedByServer && readiness != null && !readiness.IsClientReady(OwnerClientId);

    private void Awake()
    {
        statuses = GetComponent<StatusEffects>();
        receiver = GetComponent<DamageReceiver>();
    }

    [Inject]
    public void Construct(CombatSettings settings, StatusEffectService service, IClientReadiness clientReadiness)
    {
        catalog = settings != null ? settings.StatusCatalog : null;
        statusService = service;
        readiness = clientReadiness;
    }

    public override void OnNetworkSpawn()
    {
        if (catalog == null)
        {
            Debug.LogError($"{name} was spawned without a {nameof(StatusEffectCatalog)}; its statuses will not replicate.", this);
        }

        if (IsServer)
        {
            statuses.SetHold(this);
            statuses.Changed += WriteEntries;
            statuses.Blocked += HandleBlocked;
            WriteEntries();
            return;
        }

        entries.OnListChanged += HandleListChanged;
        RebuildMirror();
    }

    public override void OnNetworkDespawn()
    {
        statuses.Changed -= WriteEntries;
        statuses.Blocked -= HandleBlocked;
        entries.OnListChanged -= HandleListChanged;
        statuses.SetHold(null);
    }

    private void Update()
    {
        if (mirrorDirty)
        {
            RebuildMirror();
        }
    }

    private void WriteEntries()
    {
        if (catalog == null)
        {
            return;
        }

        entries.Clear();
        double now = NetworkManager.ServerTime.Time;
        for (int i = 0; i < statuses.Count; i++)
        {
            StatusSnapshot snapshot = statuses.GetSnapshot(i);
            int index = catalog.IndexOf(snapshot.Definition);
            if (index < 0)
            {
                Debug.LogError($"Status '{snapshot.Definition.name}' on {name} is not in the {nameof(StatusEffectCatalog)}; clients will not see it.", this);
                continue;
            }

            byte stacks = (byte)Mathf.Clamp(snapshot.Stacks, 1, byte.MaxValue);
            entries.Add(new StatusEntry(snapshot.InstanceId, (ushort)index, stacks, snapshot.Remaining, now));
        }
    }

    private void HandleBlocked(StatusEffectDefinition definition)
    {
        int index = catalog != null ? catalog.IndexOf(definition) : -1;
        if (index >= 0)
        {
            BlockedRpc((ushort)index);
        }
    }

    [Rpc(SendTo.NotServer)]
    private void BlockedRpc(ushort catalogIndex)
    {
        if (statusService != null && catalog != null)
        {
            statusService.PublishReplicatedBlock(receiver, catalog.Get(catalogIndex));
        }
    }

    private void HandleListChanged(NetworkListEvent<StatusEntry> _)
    {
        mirrorDirty = true;
    }

    private void RebuildMirror()
    {
        mirrorDirty = false;
        if (catalog == null)
        {
            return;
        }

        mirror.Clear();
        double now = NetworkManager.ServerTime.Time;
        foreach (StatusEntry entry in entries)
        {
            StatusEffectDefinition definition = catalog.Get(entry.CatalogIndex);
            if (definition == null)
            {
                continue;
            }

            float elapsed = Mathf.Max(0f, (float)(now - entry.WrittenAt));
            float remaining = Mathf.Max(0f, entry.Remaining - elapsed);
            mirror.Add(new StatusSnapshot(definition, entry.InstanceId, entry.Stacks, remaining));
        }

        statuses.Set.SyncFrom(mirror);
    }
}
