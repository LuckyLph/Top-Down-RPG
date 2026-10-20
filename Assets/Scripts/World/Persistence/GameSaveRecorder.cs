using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Gameplay-scope entry point that writes the save during a world session (Docs/WorldStreaming.md: Persistence), only
/// on the machine that decides game state (offline or the host; a client never saves): when any player enters a world
/// (the session starting, a trip, a player joining), every <see cref="GameplaySettings.AutosaveIntervalSeconds"/> of
/// game time, and as the session ends (the menu, a new game or restart, the host leaving, quitting). Each save first
/// records every player's place in the <see cref="SessionSave"/> by player id: where it stands, or the entry of the trip
/// it is on until it gets there. A player that leaves has its place recorded as it goes, for its next join. Area
/// sessions are not saved.
/// </summary>
public sealed class GameSaveRecorder : IStartable, ITickable, IDisposable
{
    private readonly GameSave gameSave;
    private readonly SessionSave sessionSave;
    private readonly IGameAuthority authority;
    private readonly NetworkSession session;
    private readonly IPlayerRegistry players;
    private readonly PlayerWorlds playerWorlds;
    private readonly TripArrivals arrivals;
    private readonly GameFlow gameFlow;
    private readonly GameplaySettings settings;
    private readonly IClock clock;
    private readonly Dictionary<PlayerHandle, string> playerIds = new();
    private readonly Dictionary<PlayerHandle, WorldDefinition> lastWorlds = new();
    private bool saveRequested;
    private float nextAutosave;
    private bool started;

    /// <summary>
    /// Creates the recorder that writes <paramref name="sessionSave"/> through <paramref name="gameSave"/>, following
    /// the players, their worlds and trips and the session's end.
    /// </summary>
    public GameSaveRecorder(
        GameSave gameSave,
        SessionSave sessionSave,
        IGameAuthority authority,
        NetworkSession session,
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
        this.session = session;
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
    /// Follows the players, their worlds, the session's end and the application quitting, and learns the players
    /// already registered.
    /// </summary>
    public void Start()
    {
        players.PlayerAdded += HandlePlayerAdded;
        players.PlayerRemoved += HandlePlayerRemoved;
        playerWorlds.Changed += HandlePlayerWorldChanged;
        gameFlow.WorldSessionEnding += HandleWorldSessionEnding;
        Application.quitting += HandleQuitting;
        started = true;
        IReadOnlyList<PlayerHandle> registered = players.Players;
        for (int i = 0; i < registered.Count; i++)
        {
            HandlePlayerAdded(registered[i]);
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
        players.PlayerAdded -= HandlePlayerAdded;
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
    /// Records <paramref name="player"/>'s place in its last world under its player id, when it has both and a place
    /// (see <see cref="TryGetPlace"/>).
    /// </summary>
    private void RecordPlace(PlayerHandle player)
    {
        if (player == null || player.Transform == null
            || !playerIds.TryGetValue(player, out string playerId) || string.IsNullOrEmpty(playerId)
            || !lastWorlds.TryGetValue(player, out WorldDefinition world) || world == null)
        {
            return;
        }

        bool travelling = arrivals.TryGet(player, out StreamInterest arrival);
        if (TryGetPlace(world, player.Transform.position, travelling, arrival, out Vector2 place))
        {
            sessionSave.SetPlace(playerId, world.WorldId, place);
        }
    }

    /// <summary>
    /// The player id of <paramref name="player"/>: its owner's on the host, this install's offline.
    /// </summary>
    private string IdOf(PlayerHandle player)
    {
        if (session.IsActive && player.Transform != null
            && player.Transform.TryGetComponent(out NetworkObject networkObject) && networkObject.IsSpawned)
        {
            return session.PlayerIdOf(networkObject.OwnerClientId);
        }

        return session.LocalPlayerId;
    }

    /// <summary>
    /// Learns a new player's id while it is spawned, since its owner may be gone by the time it leaves.
    /// </summary>
    private void HandlePlayerAdded(PlayerHandle player)
    {
        if (player != null)
        {
            playerIds[player] = IdOf(player);
        }
    }

    /// <summary>
    /// Records the place of a player that leaves (a client disconnecting, the session ending), then forgets it.
    /// </summary>
    private void HandlePlayerRemoved(PlayerHandle player)
    {
        if (authority.IsAuthoritative)
        {
            RecordPlace(player);
        }

        playerIds.Remove(player);
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
