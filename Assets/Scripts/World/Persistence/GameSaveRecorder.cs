using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Gameplay-scope entry point that writes the save during a world session (Docs/WorldStreaming.md: Persistence), only
/// on the machine that decides game state (offline or the host; a client never saves): when any player enters a world
/// (the session starting, a trip, a player joining), every <see cref="GameplaySettings.AutosaveIntervalSeconds"/> of
/// game time, and as the session ends (the menu, a new game, the host leaving, quitting). Each save first records every
/// player's place in the <see cref="SessionSave"/> by its player id (<see cref="PlayerIds"/>): where it stands, the
/// entry of the trip it is on until it gets there, or, for a dead player, the waystone it will respawn at, so leaving or
/// quitting while dead neither skips the respawn nor brings it back beside its killer. A player that leaves has its
/// place recorded as it goes, for its next join.
/// </summary>
public sealed class GameSaveRecorder : IStartable, ITickable, IDisposable
{
    private readonly GameSave gameSave;
    private readonly SessionSave sessionSave;
    private readonly IGameAuthority authority;
    private readonly PlayerIds playerIds;
    private readonly IPlayerRegistry players;
    private readonly PlayerWorlds playerWorlds;
    private readonly TripArrivals arrivals;
    private readonly GameFlow gameFlow;
    private readonly GameplaySettings settings;
    private readonly IClock clock;
    private readonly Dictionary<PlayerHandle, WorldDefinition> lastWorlds = new();
    private bool saveRequested;
    private float nextAutosave;
    private bool started;

    /// <summary>
    /// Creates the recorder that writes <paramref name="sessionSave"/> through <paramref name="gameSave"/>, following
    /// the players, their ids, worlds and trips and the session's end.
    /// </summary>
    public GameSaveRecorder(
        GameSave gameSave,
        SessionSave sessionSave,
        IGameAuthority authority,
        PlayerIds playerIds,
        IPlayerRegistry players,
        PlayerWorlds playerWorlds,
        TripArrivals arrivals,
        GameFlow gameFlow,
        GameplaySettings settings,
        IClock clock)
    {
        this.gameSave = gameSave;
        this.sessionSave = sessionSave;
        this.authority = authority;
        this.playerIds = playerIds;
        this.players = players;
        this.playerWorlds = playerWorlds;
        this.arrivals = arrivals;
        this.gameFlow = gameFlow;
        this.settings = settings;
        this.clock = clock;
    }

    /// <summary>
    /// Whether this machine saves now: it decides game state and a world session runs.
    /// </summary>
    private bool IsSaving => authority.IsAuthoritative && gameFlow.CurrentWorld != null;

    /// <summary>
    /// Follows the players leaving, their worlds, the session's end and the application quitting, and the worlds of the
    /// players already registered.
    /// </summary>
    public void Start()
    {
        players.PlayerRemoved += HandlePlayerRemoved;
        playerWorlds.Changed += HandlePlayerWorldChanged;
        gameFlow.WorldSessionEnding += HandleWorldSessionEnding;
        Application.quitting += HandleQuitting;
        started = true;
        IReadOnlyList<PlayerHandle> registered = players.Players;
        for (int i = 0; i < registered.Count; i++)
        {
            WorldDefinition world = playerWorlds.Get(registered[i]);
            if (world != null)
            {
                lastWorlds[registered[i]] = world;
            }
        }

        nextAutosave = clock.Time + settings.AutosaveIntervalSeconds;
    }

    /// <summary>
    /// Saves once a frame at most: when a player entered a world since the last save, or when the autosave interval has
    /// passed.
    /// </summary>
    public void Tick()
    {
        if (!IsSaving)
        {
            saveRequested = false;
            return;
        }

        if (saveRequested || clock.Time >= nextAutosave)
        {
            Save();
        }
    }

    /// <summary>
    /// Records every player's place and writes the session's state as the save, then waits a full autosave interval
    /// before the next autosave. Returns false, writing nothing, where this machine does not save (a client, or no world
    /// session), or when the store fails.
    /// </summary>
    public bool Save()
    {
        if (!IsSaving)
        {
            return false;
        }

        IReadOnlyList<PlayerHandle> registered = players.Players;
        for (int i = 0; i < registered.Count; i++)
        {
            RecordPlace(registered[i]);
        }

        saveRequested = false;
        nextAutosave = clock.Time + settings.AutosaveIntervalSeconds;
        return gameSave.Write(sessionSave.ToData());
    }

    /// <summary>
    /// Stops following everything it followed.
    /// </summary>
    public void Dispose()
    {
        if (!started)
        {
            return;
        }

        started = false;
        players.PlayerRemoved -= HandlePlayerRemoved;
        playerWorlds.Changed -= HandlePlayerWorldChanged;
        gameFlow.WorldSessionEnding -= HandleWorldSessionEnding;
        Application.quitting -= HandleQuitting;
    }

    /// <summary>
    /// The place to save for a player recorded in <paramref name="world"/> and standing at <paramref name="standing"/>:
    /// the entry of its trip into that world (<paramref name="arrival"/>, when <paramref name="travelling"/>) until it
    /// stands there, since it is put in the world before it moves; else where it stands, when that is on one of the
    /// world's chunks. False when neither holds, which keeps its previous place.
    /// </summary>
    internal static bool TryGetPlace(WorldDefinition world, Vector2 standing, bool travelling, StreamInterest arrival, out Vector2 place)
    {
        if (travelling && arrival.World == world && !TripArrivals.HasArrived(arrival, world, standing))
        {
            place = arrival.Position;
            return true;
        }

        if (world.HasChunkAt(standing))
        {
            place = standing;
            return true;
        }

        place = default;
        return false;
    }

    /// <summary>
    /// The place to save for a dead player of <paramref name="world"/> standing at <paramref name="standing"/>: the
    /// waystone it will respawn at, the nearest of <paramref name="discovered"/> and the world's default entry
    /// (<see cref="WaystoneRules.TryFindNearest"/>). False when the world has no entry.
    /// </summary>
    internal static bool TryGetRespawnPlace(WorldDefinition world, Vector2 standing, IReadOnlyList<GameSaveData.Waystone> discovered, out Vector2 place)
    {
        if (WaystoneRules.TryFindNearest(world, standing, discovered, out WorldDefinition.Entry entry))
        {
            place = world.ToWorld(entry.Position);
            return true;
        }

        place = default;
        return false;
    }

    /// <summary>
    /// Records <paramref name="player"/>'s place in its last world under its player id, when it has both and a place:
    /// its trip's entry while it travels, the waystone it respawns at while it is dead (<see cref="TryGetRespawnPlace"/>),
    /// else where it stands (see <see cref="TryGetPlace"/>).
    /// </summary>
    private void RecordPlace(PlayerHandle player)
    {
        string playerId = playerIds.IdOf(player);
        if (player == null || player.Transform == null || string.IsNullOrEmpty(playerId)
            || !lastWorlds.TryGetValue(player, out WorldDefinition world) || world == null)
        {
            return;
        }

        Vector2 standing = player.Transform.position;
        bool travelling = arrivals.TryGet(player, out StreamInterest arrival);
        bool dead = player.Health != null && player.Health.IsDead && !travelling;
        Vector2 place;
        bool placed = dead
            ? TryGetRespawnPlace(world, standing, sessionSave.WaystonesOf(playerId), out place)
            : TryGetPlace(world, standing, travelling, arrival, out place);
        if (placed)
        {
            sessionSave.SetPlace(playerId, world.WorldId, place);
        }
    }

    /// <summary>
    /// Records the place of a player that leaves (a client disconnecting, the session ending), then forgets its world.
    /// </summary>
    private void HandlePlayerRemoved(PlayerHandle player)
    {
        if (authority.IsAuthoritative)
        {
            RecordPlace(player);
        }

        lastWorlds.Remove(player);
    }

    /// <summary>
    /// Remembers the world a player entered and asks for a save; a player taken out of every world keeps its last one,
    /// so its place can still be recorded as it leaves.
    /// </summary>
    private void HandlePlayerWorldChanged(PlayerHandle player, WorldDefinition world)
    {
        if (player == null || world == null)
        {
            return;
        }

        lastWorlds[player] = world;
        if (IsSaving)
        {
            saveRequested = true;
        }
    }

    /// <summary>
    /// Saves as the world session ends, while every player still stands in its world.
    /// </summary>
    private void HandleWorldSessionEnding()
    {
        Save();
    }

    /// <summary>
    /// Saves as the application quits (in the editor, as Play Mode ends).
    /// </summary>
    private void HandleQuitting()
    {
        Save();
    }
}
