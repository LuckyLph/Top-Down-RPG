using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Gameplay-scope entry point that keeps a joined client's copy of its own place in the host's session
/// (Docs/KnownGaps.md, Phase 5), for the day that host is gone: its own player's world, position, discovered waystones
/// and weapon, and the host's world clock (the time of day), written through <see cref="GameSave"/> as
/// <c>joined-&lt;host session id&gt;.json</c>, the id the host tells it (<see cref="HostSessionSync"/>). It records
/// on a client only, never the host's session or any world's state, which stay the host's: when its player enters a
/// world, every <see cref="GameplaySettings.AutosaveIntervalSeconds"/> of game time, and as the session ends (leaving,
/// losing the host, quitting). Its place is where its player stands, the entry of the trip it is on until it gets there,
/// or, while dead, the waystone it will respawn at, as the host saves it. Its player's last record is taken as the player
/// goes, since Netcode destroys a client's copies as its connection drops; the copy written as the session ends then
/// says whether the host is gone (<see cref="IHostLink.IsHostLost"/>), which makes it a save Continue may load. While
/// joined the copy is never read: the host's save decides where its player starts.
/// </summary>
public sealed class JoinedSaveRecorder : IStartable, ITickable, IDisposable
{
    private readonly GameSave gameSave;
    private readonly IGameAuthority authority;
    private readonly IHostLink hostLink;
    private readonly HostSessionSync hostSession;
    private readonly IPlayerIdSource playerIds;
    private readonly LocalPlayerTracker localPlayer;
    private readonly IPlayerRegistry players;
    private readonly PlayerWorlds playerWorlds;
    private readonly KnownWaystones waystones;
    private readonly DayClock dayClock;
    private readonly GameFlow gameFlow;
    private readonly GameplaySettings settings;
    private readonly IClock clock;
    private LocalPlayer known;
    private WorldDefinition knownWorld;
    private string knownEntry;
    private GameSaveData.Player record;
    private double recordTime;
    private bool saveRequested;
    private float nextAutosave;
    private bool started;

    /// <summary>
    /// Creates the recorder of this machine's copy of its place, following its own player, its world, its discoveries,
    /// the host's world clock on <paramref name="hostDayClock"/> (null: the copy keeps the time zero), and the session's
    /// end.
    /// </summary>
    public JoinedSaveRecorder(
        GameSave gameSave,
        IGameAuthority authority,
        IHostLink hostLink,
        HostSessionSync hostSession,
        IPlayerIdSource playerIds,
        LocalPlayerTracker localPlayer,
        IPlayerRegistry players,
        PlayerWorlds playerWorlds,
        KnownWaystones waystones,
        DayClock hostDayClock,
        GameFlow gameFlow,
        GameplaySettings settings,
        IClock clock)
    {
        this.gameSave = gameSave;
        this.authority = authority;
        this.hostLink = hostLink;
        this.hostSession = hostSession;
        this.playerIds = playerIds;
        this.localPlayer = localPlayer;
        this.players = players;
        this.playerWorlds = playerWorlds;
        this.waystones = waystones;
        dayClock = hostDayClock;
        this.gameFlow = gameFlow;
        this.settings = settings;
        this.clock = clock;
    }

    /// <summary>
    /// The host's session id while this machine is a client that knows it; null otherwise.
    /// </summary>
    private string HostSessionId => !authority.IsAuthoritative && hostLink.IsFollowingHost ? hostSession.HostSessionId : null;

    /// <summary>
    /// Follows its player, its worlds and its leaving, the session's end and the application quitting.
    /// </summary>
    public void Start()
    {
        HandleLocalPlayerChanged(localPlayer.Current);
        localPlayer.Changed += HandleLocalPlayerChanged;
        players.PlayerRemoved += HandlePlayerRemoved;
        playerWorlds.Changed += HandlePlayerWorldChanged;
        gameFlow.WorldSessionEnding += HandleWorldSessionEnding;
        Application.quitting += HandleQuitting;
        started = true;
        nextAutosave = clock.Time + settings.AutosaveIntervalSeconds;
    }

    /// <summary>
    /// Writes the copy once a frame at most: when its player entered a world since the last write, or when the
    /// autosave interval has passed.
    /// </summary>
    public void Tick()
    {
        if (string.IsNullOrEmpty(HostSessionId) || hostLink.IsHostLost)
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
    /// Records its player's place and writes the copy, then waits a full autosave interval before the next autosave.
    /// When there is no record to write (its player never stood in a world in this session), a copy written before for
    /// this host session is written again only to say its host is gone. Returns false, writing nothing, where this
    /// machine keeps no copy (offline, the host, a client that does not know its host's session yet).
    /// </summary>
    public bool Save()
    {
        string hostId = HostSessionId;
        if (string.IsNullOrEmpty(hostId))
        {
            return false;
        }

        Record();
        saveRequested = false;
        nextAutosave = clock.Time + settings.AutosaveIntervalSeconds;
        bool gone = hostLink.IsHostLost;
        GameSaveData copy;
        if (record != null)
        {
            copy = new GameSaveData(recordTime, new[] { record }, Array.Empty<GameSaveData.World>());
        }
        else if (!gone || !gameSave.TryLoadCopy(hostId, out copy))
        {
            return false;
        }

        copy.SetHost(hostId, gone);
        return gameSave.Write(copy);
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
        localPlayer.Changed -= HandleLocalPlayerChanged;
        players.PlayerRemoved -= HandlePlayerRemoved;
        playerWorlds.Changed -= HandlePlayerWorldChanged;
        gameFlow.WorldSessionEnding -= HandleWorldSessionEnding;
        Application.quitting -= HandleQuitting;
    }

    /// <summary>
    /// The place to copy for this machine's player in <paramref name="world"/>, standing at <paramref name="standing"/>:
    /// the entry <paramref name="tripEntry"/> while <paramref name="travelling"/>, since it is in the world before it
    /// moves; the waystone it will respawn at, the nearest of <paramref name="discovered"/> and the default entry, while
    /// <paramref name="dead"/>; else where it stands, when that is on one of the world's chunks. False when none holds,
    /// which keeps the previous record.
    /// </summary>
    internal static bool TryGetPlace(WorldDefinition world, Vector2 standing, bool travelling, string tripEntry, bool dead, IReadOnlyList<GameSaveData.Waystone> discovered, out Vector2 place)
    {
        if (travelling)
        {
            place = world.EntryPosition(tripEntry);
            return true;
        }

        if (dead)
        {
            return GameSaveRecorder.TryGetRespawnPlace(world, standing, discovered, out place);
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
    /// Records this machine's player as it is now: its world, place, discoveries and weapon, and the host's world clock;
    /// nothing while it has no player in a world.
    /// </summary>
    private void Record()
    {
        if (known != null && known.Handle != null)
        {
            RecordFrom(known.Handle, known.Weapon);
        }
    }

    /// <summary>
    /// Records <paramref name="handle"/>, this machine's player, holding the weapon of <paramref name="weapon"/>, when it
    /// is in a world and has a place there.
    /// </summary>
    private void RecordFrom(PlayerHandle handle, PlayerWeaponController weapon)
    {
        WorldDefinition world = knownWorld;
        if (world == null || handle.Transform == null)
        {
            return;
        }

        bool dead = handle.Health != null && handle.Health.IsDead;
        bool travelling = gameFlow.IsTravelling;
        IReadOnlyList<GameSaveData.Waystone> discovered = waystones.Discovered;
        if (!TryGetPlace(world, handle.Transform.position, travelling, knownEntry, dead && !travelling, discovered, out Vector2 place))
        {
            return;
        }

        string weaponId = weapon != null && weapon.Catalog != null ? weapon.Catalog.IdOf(weapon.CurrentWeapon) : string.Empty;
        record = new GameSaveData.Player(playerIds.LocalPlayerId, world.WorldId, place, new List<GameSaveData.Waystone>(discovered), weaponId);
        if (dayClock != null)
        {
            recordTime = dayClock.WorldTime;
        }
    }

    /// <summary>
    /// Remembers this machine's player as it is assigned, and its world and trip entry, so its last record can still be
    /// taken as it leaves, whatever forgets it first (the tracker, its world).
    /// </summary>
    private void HandleLocalPlayerChanged(LocalPlayer player)
    {
        if (player == null)
        {
            return;
        }

        known = player;
        WorldDefinition world = playerWorlds.Get(player.Handle);
        if (world != null)
        {
            knownWorld = world;
            knownEntry = playerWorlds.EntryOf(player.Handle);
        }
    }

    /// <summary>
    /// Takes this machine's player's last record as it leaves the registry (the session ending, the connection to the
    /// host dropping), while it still stands where it was.
    /// </summary>
    private void HandlePlayerRemoved(PlayerHandle player)
    {
        if (string.IsNullOrEmpty(HostSessionId) || player == null || known == null || known.Handle != player)
        {
            return;
        }

        RecordFrom(player, known.Weapon);
    }

    /// <summary>
    /// Remembers the world this machine's player enters and its trip's entry, and asks for a write; a player taken out
    /// of every world keeps its last one, so its place can still be recorded as it leaves.
    /// </summary>
    private void HandlePlayerWorldChanged(PlayerHandle player, WorldDefinition world)
    {
        if (world == null || known == null || known.Handle != player)
        {
            return;
        }

        knownWorld = world;
        knownEntry = playerWorlds.EntryOf(player);
        if (!string.IsNullOrEmpty(HostSessionId))
        {
            saveRequested = true;
        }
    }

    /// <summary>
    /// Writes the copy as the world session ends, while its player still stands in its world, or, the connection lost,
    /// from its last record, saying the host is gone.
    /// </summary>
    private void HandleWorldSessionEnding()
    {
        Save();
    }

    /// <summary>
    /// Writes the copy as the application quits (in the editor, as Play Mode ends).
    /// </summary>
    private void HandleQuitting()
    {
        Save();
    }
}
