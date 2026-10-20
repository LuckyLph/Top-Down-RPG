using Unity.Netcode;
using UnityEngine;
using VContainer;

/// <summary>
/// Replicates a networked player's world (Docs/WorldStreaming.md: Networking). The host writes the player's world, trip
/// count and trip entry from its <see cref="PlayerWorlds"/> into a network variable whenever they change, and tells the
/// session a client's player set off on a trip so that client's chunk reports from before it are ignored. Every client
/// feeds the variable into its own <see cref="PlayerWorlds"/>, so each machine knows every player's world; the owner
/// also hands a new trip to <see cref="NetworkTripAuthority.Deliver"/>, which sets its fade and arrival off. As the
/// player's <see cref="ITripLink"/> it sends its owner's trip requests to the host as an RPC naming the exit.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerNetworkSync))]
public class PlayerWorldSync : NetworkBehaviour, ITripLink
{
    private readonly NetworkVariable<PlayerWorldState> state = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private PlayerNetworkSync playerSync;
    private PlayerWorlds playerWorlds;
    private GameScenes gameScenes;
    private NetworkTripAuthority trips;
    private IClientReadiness readiness;
    private PlayerHandle handle;
    private WorldDefinition spawnWorld;
    private bool following;

    /// <summary>
    /// The replicated state, for tests.
    /// </summary>
    internal PlayerWorldState State => state.Value;

    /// <summary>
    /// Caches the player's network sync, which binds the player and holds its registry handle.
    /// </summary>
    private void Awake()
    {
        playerSync = GetComponent<PlayerNetworkSync>();
    }

    /// <summary>
    /// Takes the record of players' worlds, the catalogue world ids are resolved against, the trip authority and the
    /// session's client readiness.
    /// </summary>
    [Inject]
    public void Construct(PlayerWorlds worldsOfPlayers, GameScenes scenes, NetworkTripAuthority tripAuthority, IClientReadiness clientReadiness)
    {
        playerWorlds = worldsOfPlayers;
        gameScenes = scenes;
        trips = tripAuthority;
        readiness = clientReadiness;
    }

    /// <summary>
    /// Host only, before spawning: the world the player starts in, which it is put in as it spawns.
    /// </summary>
    public void SetSpawnWorld(WorldDefinition world)
    {
        spawnWorld = world;
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
            readiness.BeginTrip(OwnerClientId, trip);
        }
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
