using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VContainer;

/// <summary>
/// Replicates a networked player's world (Docs/WorldStreaming.md: Networking). The host writes the player's world, trip
/// count and trip entry from its <see cref="PlayerWorlds"/> into a network variable whenever they change, and tells the
/// session a client's player set off on a trip so that client's chunk reports from before it are ignored. Every client
/// feeds the variable into its own <see cref="PlayerWorlds"/>, so each machine knows every player's world; the owner
/// also hands a new trip to <see cref="NetworkTripAuthority.Deliver"/>, which sets its fade and arrival off; a respawn
/// at a waystone is such a trip. As the player's <see cref="ITripLink"/> it sends its owner's trip requests to the host
/// as an RPC naming the exit, and its fade in at the end of each trip as an RPC naming the trip, which ends the trip's
/// protection there; and as its <see cref="IWaystoneLink"/> it tells a client's owner which waystone its player
/// discovered, raised there on <see cref="WaystoneNotices"/>. It also replicates to a client's owner, alone, the
/// waystones its player discovered (Docs/KnownGaps.md, Phase 3), each by its world's and entry's places in the build's
/// Game Scenes (<see cref="NetworkWaystone"/>): the host fills a list read by the owner only from its save as the player
/// spawns (<see cref="SetSpawnWaystones"/>; a world or entry the build lacks is left out) and adds each later discovery
/// to it, and the owner hands the list to its <see cref="KnownWaystones"/> at spawn and on every change, so its markers
/// show what its player discovered, in earlier sessions too.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerNetworkSync))]
public class PlayerWorldSync : NetworkBehaviour, ITripLink, IWaystoneLink
{
    private readonly NetworkVariable<PlayerWorldState> state = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkList<NetworkWaystone> waystones = new(null, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
    private readonly List<GameSaveData.Waystone> spawnWaystones = new();
    private readonly List<GameSaveData.Waystone> replicated = new();

    private PlayerNetworkSync playerSync;
    private PlayerWorlds playerWorlds;
    private GameScenes gameScenes;
    private NetworkTripAuthority trips;
    private IClientReadiness readiness;
    private WaystoneNotices waystoneNotices;
    private KnownWaystones knownWaystones;
    private PlayerHandle handle;
    private WorldDefinition spawnWorld;
    private bool following;
    private bool replicating;

    /// <summary>
    /// The replicated state, for tests.
    /// </summary>
    internal PlayerWorldState State => state.Value;

    /// <summary>
    /// How many waystones the replicated list holds, for tests.
    /// </summary>
    internal int ReplicatedWaystoneCount => waystones.Count;

    /// <summary>
    /// Caches the player's network sync, which binds the player and holds its registry handle.
    /// </summary>
    private void Awake()
    {
        playerSync = GetComponent<PlayerNetworkSync>();
    }

    /// <summary>
    /// Takes the record of players' worlds, the catalogue world ids are resolved against, the trip authority, the
    /// session's client readiness, where waystone discoveries are told and the record of this machine's player's.
    /// </summary>
    [Inject]
    public void Construct(PlayerWorlds worldsOfPlayers, GameScenes scenes, NetworkTripAuthority tripAuthority, IClientReadiness clientReadiness, WaystoneNotices notices, KnownWaystones known)
    {
        playerWorlds = worldsOfPlayers;
        gameScenes = scenes;
        trips = tripAuthority;
        readiness = clientReadiness;
        waystoneNotices = notices;
        knownWaystones = known;
    }

    /// <summary>
    /// Host only, before spawning: the world the player starts in, which it is put in as it spawns.
    /// </summary>
    public void SetSpawnWorld(WorldDefinition world)
    {
        spawnWorld = world;
    }

    /// <summary>
    /// Host only, before spawning: the waystones the player's save says it discovered, which a client's owner receives
    /// as it spawns.
    /// </summary>
    public void SetSpawnWaystones(IReadOnlyList<GameSaveData.Waystone> discovered)
    {
        spawnWaystones.Clear();
        for (int i = 0; discovered != null && i < discovered.Count; i++)
        {
            if (discovered[i] != null)
            {
                spawnWaystones.Add(discovered[i]);
            }
        }
    }

    /// <summary>
    /// The host fills a client's player's list of discovered waystones from its save.
    /// </summary>
    public override void OnNetworkSpawn()
    {
        if (!IsServer || IsOwnedByServer)
        {
            return;
        }

        for (int i = 0; i < spawnWaystones.Count; i++)
        {
            if (NetworkWaystone.TryFrom(gameScenes, spawnWaystones[i].WorldId, spawnWaystones[i].Entry, out NetworkWaystone waystone))
            {
                waystones.Add(waystone);
            }
        }

        spawnWaystones.Clear();
    }

    /// <summary>
    /// Once the player is bound: the host puts it in its spawn world and writes its world now and on every change; a
    /// client applies the replicated world now and on every change. Logs an error when the prefab was spawned without
    /// injection.
    /// </summary>
    protected override void OnNetworkPostSpawn()
    {
        handle = playerSync.Handle;
        if (handle == null)
        {
            return;
        }

        if (playerWorlds == null || gameScenes == null || trips == null)
        {
            Debug.LogError($"{name} was spawned without its world services; register the player prefab with the Gameplay scope's resolver.", this);
            return;
        }

        following = true;
        if (IsServer)
        {
            playerWorlds.Changed += HandleWorldChanged;
            if (spawnWorld != null)
            {
                playerWorlds.Set(handle, spawnWorld);
            }

            Write();
            return;
        }

        state.OnValueChanged += HandleStateChanged;
        Apply(state.Value, false);
        if (IsOwner && knownWaystones != null)
        {
            waystones.OnListChanged += HandleWaystonesChanged;
            replicating = true;
            ReplicateWaystones();
        }
    }

    /// <summary>
    /// Stops following the player's world.
    /// </summary>
    public override void OnNetworkDespawn()
    {
        if (following)
        {
            playerWorlds.Changed -= HandleWorldChanged;
            state.OnValueChanged -= HandleStateChanged;
            following = false;
        }

        if (replicating)
        {
            waystones.OnListChanged -= HandleWaystonesChanged;
            replicating = false;
        }

        handle = null;
    }

    /// <summary>
    /// Owner only: sends the host this player's request for the exit <paramref name="exitId"/>, from
    /// <paramref name="position"/>.
    /// </summary>
    public void SendTripRequest(string exitId, Vector2 position)
    {
        if (!IsSpawned || !IsOwner)
        {
            Debug.LogWarning($"{name} can only ask for its own trips once spawned.", this);
            return;
        }

        RequestTripRpc(exitId ?? string.Empty, position);
    }

    /// <summary>
    /// Owner only: tells the host this machine faded in at the end of its player's trip number <paramref name="trip"/>.
    /// </summary>
    public void SendTripArrived(int trip)
    {
        if (!IsSpawned || !IsOwner)
        {
            Debug.LogWarning($"{name} can only report its own trips once spawned.", this);
            return;
        }

        TripArrivedRpc(trip);
    }

    /// <summary>
    /// Host only: tells this player's owner, when it is a client, that its player discovered the waystone
    /// <paramref name="entryName"/> of the world <paramref name="worldId"/>: added to its replicated list, and raised
    /// there for its popup.
    /// </summary>
    public void SendWaystoneDiscovered(string worldId, string entryName)
    {
        if (IsSpawned && IsServer && !IsOwnedByServer)
        {
            if (NetworkWaystone.TryFrom(gameScenes, worldId, entryName, out NetworkWaystone waystone))
            {
                waystones.Add(waystone);
            }

            WaystoneDiscoveredRpc(entryName ?? string.Empty);
        }
    }

    /// <summary>
    /// Runs on the owner: raises the discovery of the waystone <paramref name="entryName"/> by this machine's player.
    /// </summary>
    [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
    private void WaystoneDiscoveredRpc(string entryName)
    {
        if (handle != null && waystoneNotices != null)
        {
            waystoneNotices.Raise(handle, entryName);
        }
    }

    /// <summary>
    /// Runs on the host: checks the owner's trip request and, when it goes, puts the player in the target world, which
    /// reaches the owner through the network variable.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestTripRpc(string exitId, Vector2 position)
    {
        if (handle != null && trips != null)
        {
            trips.Approve(new TripRequest(handle, exitId, position));
        }
    }

    /// <summary>
    /// Runs on the host: the owner faded in at the end of its trip number <paramref name="trip"/>, which ends the
    /// protection that trip gave the player.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void TripArrivedRpc(int trip)
    {
        if (handle != null && trips != null)
        {
            trips.ReportArrived(handle, trip);
        }
    }

    /// <summary>
    /// Host only: writes the player's world when <paramref name="player"/> is this one.
    /// </summary>
    private void HandleWorldChanged(PlayerHandle player, WorldDefinition world)
    {
        if (player == handle)
        {
            Write();
        }
    }

    /// <summary>
    /// Host only: writes the player's world, trip count and trip entry, and tells the session a client's player is on
    /// that trip.
    /// </summary>
    private void Write()
    {
        WorldDefinition world = playerWorlds.Get(handle);
        int trip = playerWorlds.TripOf(handle);
        state.Value = new PlayerWorldState(world != null ? world.WorldId : string.Empty, trip, playerWorlds.EntryOf(handle));
        if (!IsOwnedByServer && readiness != null)
        {
            readiness.BeginTrip(OwnerClientId, trip, world != null ? world.WorldId : string.Empty);
        }
    }

    /// <summary>
    /// The owner, a client: hands its player's replicated discoveries on.
    /// </summary>
    private void HandleWaystonesChanged(NetworkListEvent<NetworkWaystone> change)
    {
        ReplicateWaystones();
    }

    /// <summary>
    /// The owner, a client: tells its <see cref="KnownWaystones"/> what the list holds now.
    /// </summary>
    private void ReplicateWaystones()
    {
        replicated.Clear();
        foreach (NetworkWaystone waystone in waystones)
        {
            if (waystone.TryResolve(gameScenes, out string worldId, out string entryName))
            {
                replicated.Add(new GameSaveData.Waystone(worldId, entryName));
            }
        }

        knownWaystones.Replicate(replicated);
    }

    /// <summary>
    /// Client only: applies a replicated change, handing the owner's new trip on.
    /// </summary>
    private void HandleStateChanged(PlayerWorldState previous, PlayerWorldState current)
    {
        Apply(current, IsOwner && current.Trip > previous.Trip);
    }

    /// <summary>
    /// Client only: puts the player in the world <paramref name="replicated"/> names, with its trip count and entry, and
    /// when <paramref name="deliverTrip"/> tells the trip authority this machine's player is on its way to that entry.
    /// A world id this build does not list logs an error and leaves the player in no world.
    /// </summary>
    private void Apply(PlayerWorldState replicated, bool deliverTrip)
    {
        WorldDefinition world = null;
        if (replicated.HasWorld)
        {
            world = gameScenes.FindWorldById(replicated.WorldId);
            if (world == null)
            {
                Debug.LogError($"The host put {name} in the world {replicated.WorldId}, which this build does not list.", this);
            }
        }

        string entryName = replicated.Entry;
        playerWorlds.Apply(handle, world, replicated.Trip, entryName);
        if (!deliverTrip || world == null)
        {
            return;
        }

        WorldDefinition.Entry entry = world.FindEntry(entryName);
        Vector2 position = world.ToWorld(entry != null ? entry.Position : world.LocalBounds.center);
        trips.Deliver(new TripApproval(handle, world, entry != null ? entry.Name : entryName, position));
    }
}
