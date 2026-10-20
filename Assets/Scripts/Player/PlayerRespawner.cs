using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Brings dead registered players back (Docs/WorldStreaming.md: Death and respawn), where game state is decided. A dead
/// player goes on a trip within its own world to the waystone it respawns at (<see cref="WaystoneRules.TryFindNearest"/>,
/// among the waystones its player id discovered in the <see cref="SessionSave"/>): the trip's authority holds the
/// arrival, so this machine streams the waystone's chunks at once, and puts the player in its world for one more trip,
/// which its own machine follows as any trip, fading, moving itself and waiting for the chunks. Its health is restored
/// (<see cref="TryRestore"/>) once it stands at the waystone with that chunk loaded here and, for a client's player,
/// held by its client. A player in no world, or in a world without entries, is restored where it stands.
/// </summary>
public sealed class PlayerRespawner
{
    /// <summary>
    /// A respawn under way: the world, name and world position of the waystone entry the player travels to, and the
    /// coordinates of the chunk holding it.
    /// </summary>
    public readonly struct Respawn
    {
        /// <summary>
        /// Creates the respawn at the entry <paramref name="entryName"/>, at <paramref name="position"/> in
        /// <paramref name="world"/>.
        /// </summary>
        public Respawn(WorldDefinition world, string entryName, Vector2 position)
        {
            World = world;
            EntryName = entryName;
            Position = position;
            Coords = ChunkCoords.ChunkOf(ChunkCoords.CellOf(world.ToLocal(position)));
        }

        public WorldDefinition World { get; }
        public string EntryName { get; }
        public Vector2 Position { get; }
        public Vector2Int Coords { get; }
    }

    private readonly IPlayerRegistry players;
    private readonly PlayerWorlds playerWorlds;
    private readonly PlayerIds playerIds;
    private readonly SessionSave sessionSave;
    private readonly NetworkTripAuthority trips;
    private readonly TripArrivals arrivals;
    private readonly Worlds worlds;
    private readonly IClientReadiness readiness;

    /// <summary>
    /// Captures the registry, the players' worlds and ids, the session's discoveries, the trip authority and the trips
    /// under way, the loaded worlds and the clients' readiness.
    /// </summary>
    public PlayerRespawner(
        IPlayerRegistry players,
        PlayerWorlds playerWorlds,
        PlayerIds playerIds,
        SessionSave sessionSave,
        NetworkTripAuthority trips,
        TripArrivals arrivals,
        Worlds worlds,
        IClientReadiness readiness)
    {
        this.players = players;
        this.playerWorlds = playerWorlds;
        this.playerIds = playerIds;
        this.sessionSave = sessionSave;
        this.trips = trips;
        this.arrivals = arrivals;
        this.worlds = worlds;
        this.readiness = readiness;
    }

    /// <summary>
    /// Whether <paramref name="player"/> is on a trip it has not reached the end of: a respawn waits for it to arrive.
    /// </summary>
    public bool IsTravelling(PlayerHandle player)
    {
        return arrivals.IsHeld(player);
    }

    /// <summary>
    /// The world and the waystone entry <paramref name="player"/>, standing where it is, respawns at; false when it is in
    /// no world or its world has no entry.
    /// </summary>
    public bool TryFindWaystone(PlayerHandle player, out WorldDefinition world, out WorldDefinition.Entry entry)
    {
        entry = null;
        world = player != null ? playerWorlds.Get(player) : null;
        return world != null && player.Transform != null
            && WaystoneRules.TryFindNearest(world, player.Transform.position, sessionSave.WaystonesOf(playerIds.IdOf(player)), out entry);
    }

    /// <summary>
    /// Sends the dead registered <paramref name="player"/> on a trip within its world to the waystone it respawns at,
    /// giving the <paramref name="respawn"/> to wait for. False, sending nothing, when it is no dead registered player or
    /// has no waystone (<see cref="TryFindWaystone"/>).
    /// </summary>
    public bool TrySendToWaystone(PlayerHandle player, out Respawn respawn)
    {
        respawn = default;
        if (!IsDeadAndRegistered(player) || !TryFindWaystone(player, out WorldDefinition world, out WorldDefinition.Entry entry))
        {
            return false;
        }

        TripApproval approval = trips.Send(player, world, entry.Name);
        respawn = new Respawn(world, approval.EntryName, approval.Position);
        return true;
    }

    /// <summary>
    /// Whether <paramref name="player"/> has reached <paramref name="respawn"/>'s waystone: it is in that world within
    /// two cells of the entry (<see cref="TripArrivals.HasArrived"/>), the entry's chunk is loaded on this machine, and,
    /// when a client moves the player, that client holds the chunk for its current trip.
    /// </summary>
    public bool HasArrived(PlayerHandle player, Respawn respawn)
    {
        if (player == null || player.Transform == null || respawn.World == null
            || !TripArrivals.HasArrived(new StreamInterest(respawn.World, respawn.Position), playerWorlds.Get(player), player.Transform.position))
        {
            return false;
        }

        return worlds.TryGet(respawn.World, out LoadedWorld loaded) && loaded.Chunks.ContainsKey(respawn.Coords)
            && readiness.IsClientReady(OwnerOf(player), ChunkId.Of(respawn.World, respawn.Coords));
    }

    /// <summary>
    /// Restores <paramref name="player"/>'s health where it stands, which re-enables it through
    /// <see cref="DisableOnDeath"/> and reaches every machine through <see cref="NetworkHealth"/>. False when the player
    /// is not a dead registered one.
    /// </summary>
    public bool TryRestore(PlayerHandle player)
    {
        if (!IsDeadAndRegistered(player))
        {
            return false;
        }

        player.Health.Restore();
        return true;
    }

    /// <summary>
    /// Whether <paramref name="player"/> is a registered player with a body and a dead health.
    /// </summary>
    private bool IsDeadAndRegistered(PlayerHandle player)
    {
        return player != null && player.Transform != null && player.Health != null && player.Health.IsDead && players.Contains(player);
    }

    /// <summary>
    /// The client that moves <paramref name="player"/>: its network object's owner while spawned, the host's own (or
    /// offline, this machine's) client otherwise.
    /// </summary>
    private static ulong OwnerOf(PlayerHandle player)
    {
        return player.Transform.TryGetComponent(out NetworkObject networkObject) && networkObject.IsSpawned
            ? networkObject.OwnerClientId
            : NetworkManager.ServerClientId;
    }
}
