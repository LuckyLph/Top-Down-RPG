using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VContainer;

/// <summary>
/// Replicates a unit's statuses. Host: rewrites a server-only list of <see cref="StatusEntry"/> whenever the set
/// changes and as the statuses start running again after a hold (the clients' mirrors ran down meanwhile), forwards
/// blocked statuses for Immune popups, and holds a client's player while that client loads.
/// Clients: rebuild their mirror set from the list on spawn and after every change, counting remaining time down
/// from the server time of each write and then every frame for display.
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
    private bool wasHeld;

    public bool IsHeld => IsSpawned && IsServer && !IsOwnedByServer && readiness != null && !readiness.IsClientReady(OwnerClientId);

    /// <summary>
    /// Caches the unit's <see cref="StatusEffects"/> and <see cref="DamageReceiver"/>.
    /// </summary>
    private void Awake()
    {
        statuses = GetComponent<StatusEffects>();
        receiver = GetComponent<DamageReceiver>();
    }

    /// <summary>
    /// Takes the status catalog from the combat settings, the service that publishes replicated blocks and the
    /// client readiness that decides when a client's player is held.
    /// </summary>
    [Inject]
    public void Construct(CombatSettings settings, StatusEffectService service, IClientReadiness clientReadiness)
    {
        catalog = settings != null ? settings.StatusCatalog : null;
        statusService = service;
        readiness = clientReadiness;
    }

    /// <summary>
    /// On the host, becomes the unit's status hold, writes the list now and on every change, and forwards blocks.
    /// On a client, rebuilds the mirror now and after every list change. Logs an error when there is no catalog.
    /// </summary>
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

    /// <summary>
    /// Stops listening to the set and the list, and releases the status hold.
    /// </summary>
    public override void OnNetworkDespawn()
    {
        statuses.Changed -= WriteEntries;
        statuses.Blocked -= HandleBlocked;
        entries.OnListChanged -= HandleListChanged;
        statuses.SetHold(null);
    }

    /// <summary>
    /// Rebuilds the mirror once per frame after list changes; otherwise, on a client, counts the mirrored remaining
    /// times down by the frame time. On the host, rewrites the list on the frame the statuses run again after a hold
    /// (<see cref="StatusEffectService.IsHeld"/>), since the clients counted them down through it.
    /// </summary>
    private void Update()
    {
        if (mirrorDirty)
        {
            RebuildMirror();
        }
        else if (IsSpawned && !IsServer)
        {
            statuses.Set.CountDown(Time.deltaTime);
        }
        else if (IsSpawned)
        {
            bool held = statuses.Count > 0 && statusService != null && statusService.IsHeld(receiver);
            if (wasHeld && !held && statuses.Count > 0)
            {
                WriteEntries();
            }

            wasHeld = held;
        }
    }

    /// <summary>
    /// Host: rewrites the list from the current set, stamping every entry with the server time. Statuses missing from
    /// the catalog are logged and skipped; stacks are clamped to a byte.
    /// </summary>
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

    /// <summary>
    /// Host: tells clients a status in the catalog was blocked by an immunity on this unit, naming its source unit when
    /// it is spawned, so a client's popups know whether its own player sent it (Docs/UiRevamp.md, Phase 3).
    /// </summary>
    private void HandleBlocked(StatusEffectDefinition definition, GameObject sourceObject)
    {
        int index = catalog != null ? catalog.IndexOf(definition) : -1;
        if (index >= 0)
        {
            bool hasSource = NetworkHealth.TryGetSourceObject(sourceObject, out NetworkObject source);
            BlockedRpc((ushort)index, hasSource ? new NetworkObjectReference(source) : default, hasSource);
        }
    }

    /// <summary>
    /// Runs on clients: publishes the host's block of the status at <paramref name="catalogIndex"/> for the Immune
    /// popup, from this client's copy of its source when it names one this client has.
    /// </summary>
    [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
    private void BlockedRpc(ushort catalogIndex, NetworkObjectReference source, bool hasSource)
    {
        if (statusService != null && catalog != null)
        {
            GameObject sourceObject = hasSource && source.TryGet(out NetworkObject sourceNetworkObject, NetworkManager) ? sourceNetworkObject.gameObject : null;
            statusService.PublishReplicatedBlock(receiver, catalog.Get(catalogIndex), sourceObject);
        }
    }

    /// <summary>
    /// Client: marks the mirror for a rebuild on the next frame, so a burst of list events costs one rebuild.
    /// </summary>
    private void HandleListChanged(NetworkListEvent<StatusEntry> _)
    {
        mirrorDirty = true;
    }

    /// <summary>
    /// Client: replaces the mirror set with the list's statuses, each remaining time reduced by the server time that
    /// passed since the host wrote it. Entries the catalog does not know are skipped.
    /// </summary>
    private void RebuildMirror()
    {
        mirrorDirty = false;
        if (catalog == null)
        {
            return;
        }

        mirror.Clear();
        double now = NetworkManager.ServerTime.Time;
        for (int i = 0; i < entries.Count; i++)
        {
            StatusEntry entry = entries[i];
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
