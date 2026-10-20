using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Gameplay-scope entry point that detects waystone discoveries (Docs/WorldStreaming.md: Death and respawn), where game
/// state is decided (offline or the host, which has every player's position): each frame it compares every living
/// player's cell in its world with the last one, and on a new cell asks <see cref="WaystoneRules.TryFindDiscovery"/>
/// whether the player came within <see cref="GameplaySettings.WaystoneReachCells"/> of a waystone it had not
/// discovered. A discovery is recorded in the <see cref="SessionSave"/> under the player's id, raised on
/// <see cref="WaystoneNotices"/>, and sent to the player's own machine when that is a client (<see cref="IWaystoneLink"/>).
/// A client detects nothing: it hears of its own player's discoveries from the host.
/// </summary>
public sealed class WaystoneDiscovery : IStartable, ITickable, IDisposable
{
    private struct LastCell
    {
        public WorldDefinition World;
        public Vector2Int Cell;
    }

    private readonly IPlayerRegistry players;
    private readonly PlayerWorlds playerWorlds;
    private readonly PlayerIds playerIds;
    private readonly SessionSave sessionSave;
    private readonly IGameAuthority authority;
    private readonly WaystoneNotices notices;
    private readonly int reachCells;
    private readonly Dictionary<PlayerHandle, LastCell> lastCells = new();
    private Func<Transform, IWaystoneLink> findLink = FindLink;

    /// <summary>
    /// Captures the players, their worlds and ids, the session's save, the authority, where discoveries are told and
    /// the reach of <paramref name="settings"/>.
    /// </summary>
    public WaystoneDiscovery(
        IPlayerRegistry players,
        PlayerWorlds playerWorlds,
        PlayerIds playerIds,
        SessionSave sessionSave,
        IGameAuthority authority,
        WaystoneNotices notices,
        GameplaySettings settings)
    {
        this.players = players;
        this.playerWorlds = playerWorlds;
        this.playerIds = playerIds;
        this.sessionSave = sessionSave;
        this.authority = authority;
        this.notices = notices;
        reachCells = settings != null ? settings.WaystoneReachCells : 0;
    }

    /// <summary>
    /// How a player's body is asked for its <see cref="IWaystoneLink"/>; the component on it by default, a fake in tests.
    /// </summary>
    internal Func<Transform, IWaystoneLink> LinkFinder
    {
        get => findLink;
        set => findLink = value ?? FindLink;
    }

    /// <summary>
    /// Forgets players as they leave.
    /// </summary>
    public void Start()
    {
        players.PlayerRemoved += Forget;
    }

    /// <summary>
    /// On the authority, checks every living player in a world that stands on a new cell for a waystone it discovers.
    /// </summary>
    public void Tick()
    {
        if (!authority.IsAuthoritative)
        {
            return;
        }

        IReadOnlyList<PlayerHandle> registered = players.Players;
        for (int i = 0; i < registered.Count; i++)
        {
            PlayerHandle player = registered[i];
            WorldDefinition world = playerWorlds.Get(player);
            if (world == null || player.Transform == null || !player.IsAlive)
            {
                continue;
            }

            Vector2 position = player.Transform.position;
            Vector2Int cell = ChunkCoords.CellOf(world.ToLocal(position));
            if (lastCells.TryGetValue(player, out LastCell last) && last.World == world && last.Cell == cell)
            {
                continue;
            }

            lastCells[player] = new LastCell { World = world, Cell = cell };
            Discover(player, world, position);
        }
    }

    /// <summary>
    /// Stops following the registry.
    /// </summary>
    public void Dispose()
    {
        players.PlayerRemoved -= Forget;
        lastCells.Clear();
    }

    /// <summary>
    /// Records and tells the waystone of <paramref name="world"/> that <paramref name="player"/>, at
    /// <paramref name="position"/>, discovers now, if any.
    /// </summary>
    private void Discover(PlayerHandle player, WorldDefinition world, Vector2 position)
    {
        string playerId = playerIds.IdOf(player);
        if (string.IsNullOrEmpty(playerId)
            || !WaystoneRules.TryFindDiscovery(world, position, reachCells, sessionSave.WaystonesOf(playerId), out WorldDefinition.Entry entry)
            || !sessionSave.Discover(playerId, world.WorldId, entry.Name))
        {
            return;
        }

        notices.Raise(player, entry.Name);
        IWaystoneLink link = findLink(player.Transform);
        if (link != null)
        {
            link.SendWaystoneDiscovered(entry.Name);
        }
    }

    /// <summary>
    /// The <see cref="IWaystoneLink"/> component on <paramref name="body"/>, or null.
    /// </summary>
    private static IWaystoneLink FindLink(Transform body)
    {
        return body.TryGetComponent(out IWaystoneLink link) ? link : null;
    }

    private void Forget(PlayerHandle player)
    {
        lastCells.Remove(player);
    }
}
