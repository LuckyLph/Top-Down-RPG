using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The running session's saved state, in memory (Docs/WorldStreaming.md: Persistence): its id, which names its save file
/// (Docs/KnownGaps.md, Phase 5), the world clock, each player's place, discovered waystones and weapon by player id, and
/// the <see cref="WorldState"/> of every world visited, loaded or not.
/// It is used where game state is decided, offline or on the host; a client keeps an empty one it never writes. A
/// session starts new (<see cref="StartNew"/>, a new id), from a save (<see cref="StartFrom"/>: its id, or a new one for
/// a joined client's copy, which goes on as a session of its own), or as a throwaway (<see cref="StartThrowaway"/>: an
/// editor boot straight into a world, which nothing writes to a save file), and lasts as long as the Main scope, across
/// Gameplay scenes. The world clock is the game time played on the save: the saved time plus the game time since the
/// session started, so a time recorded in one session still means the same in the next.
/// </summary>
public sealed class SessionSave
{
    private readonly IClock clock;
    private readonly Dictionary<string, GameSaveData.Player> places = new();
    private readonly Dictionary<string, List<GameSaveData.Waystone>> waystones = new();
    private readonly Dictionary<string, string> weapons = new();
    private readonly Dictionary<string, WorldState> worlds = new();
    private double savedTime;
    private float clockAtStart;

    /// <summary>
    /// Creates an empty session state whose world clock runs on <paramref name="gameClock"/>, under an id of its own, so
    /// a session started without the menu (a test hosting directly) still saves into a file of its own.
    /// </summary>
    public SessionSave(IClock gameClock)
    {
        clock = gameClock;
        Id = NewId();
    }

    /// <summary>
    /// The world clock: the game time played on the save, in seconds.
    /// </summary>
    public double Time => savedTime + (clock.Time - clockAtStart);

    /// <summary>
    /// Whether this session is a throwaway, started by booting the editor straight into a world rather than from the
    /// main menu: it keeps its state in memory like any session, but nothing writes it to a save file.
    /// </summary>
    public bool IsThrowaway { get; private set; }

    /// <summary>
    /// The session's id, which names its save file and which a host tells its clients, so each keeps a copy of its own
    /// place per host session.
    /// </summary>
    public string Id { get; private set; }

    internal int PlayerCount => places.Count;
    internal int WorldCount => worlds.Count;

    /// <summary>
    /// Forgets everything and starts the world clock at zero under a new id, for a new game (or a joined client, which
    /// writes no session of its own).
    /// </summary>
    public void StartNew()
    {
        Restart(0d, NewId());
    }

    /// <summary>
    /// Forgets everything and starts the world clock at zero for a throwaway session (<see cref="IsThrowaway"/>): an
    /// editor boot straight into a world, which leaves the save file alone. The next session started from the main menu
    /// (<see cref="StartNew"/>, <see cref="StartFrom"/>) is a real one again.
    /// </summary>
    public void StartThrowaway()
    {
        Restart(0d, NewId());
        IsThrowaway = true;
    }

    /// <summary>
    /// Replaces the state with <paramref name="data"/>'s and continues its world clock from the saved time, under the
    /// save's id; a joined client's copy of its place (or a save with no id) goes on as a new session of its own, under a
    /// new id, its copy left as it is. Players and worlds without an id are skipped.
    /// </summary>
    public void StartFrom(GameSaveData data)
    {
        Restart(data.Time, data.IsCopy || string.IsNullOrEmpty(data.Id) ? NewId() : data.Id);
        foreach (GameSaveData.Player player in data.Players)
        {
            if (player != null && !string.IsNullOrEmpty(player.PlayerId))
            {
                places[player.PlayerId] = new GameSaveData.Player(player.PlayerId, player.WorldId, player.Position);
                waystones[player.PlayerId] = new List<GameSaveData.Waystone>(player.Waystones);
                SetWeapon(player.PlayerId, player.Weapon);
            }
        }

        foreach (GameSaveData.World world in data.Worlds)
        {
            if (world != null && !string.IsNullOrEmpty(world.WorldId))
            {
                worlds[world.WorldId] = WorldState.FromData(world);
            }
        }
    }

    /// <summary>
    /// The saved place of <paramref name="playerId"/>: its world's id and its position there; false for a player this
    /// session does not know.
    /// </summary>
    public bool TryGetPlace(string playerId, out string worldId, out Vector2 position)
    {
        if (!string.IsNullOrEmpty(playerId) && places.TryGetValue(playerId, out GameSaveData.Player player))
        {
            worldId = player.WorldId;
            position = player.Position;
            return true;
        }

        worldId = null;
        position = default;
        return false;
    }

    /// <summary>
    /// The waystones <paramref name="playerId"/> has discovered; empty for a player that discovered none. The list
    /// stays this session's and grows with <see cref="Discover"/>.
    /// </summary>
    public IReadOnlyList<GameSaveData.Waystone> WaystonesOf(string playerId)
    {
        return !string.IsNullOrEmpty(playerId) && waystones.TryGetValue(playerId, out List<GameSaveData.Waystone> discovered)
            ? discovered
            : System.Array.Empty<GameSaveData.Waystone>();
    }

    /// <summary>
    /// Records that <paramref name="playerId"/> discovered the waystone <paramref name="entryName"/> of the world
    /// <paramref name="worldId"/>. Returns whether that is new; false, changing nothing, for an empty id or name or a
    /// waystone already recorded.
    /// </summary>
    public bool Discover(string playerId, string worldId, string entryName)
    {
        if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(worldId) || string.IsNullOrEmpty(entryName))
        {
            return false;
        }

        if (!waystones.TryGetValue(playerId, out List<GameSaveData.Waystone> discovered))
        {
            discovered = new List<GameSaveData.Waystone>();
            waystones.Add(playerId, discovered);
        }

        if (WaystoneRules.Contains(discovered, worldId, entryName))
        {
            return false;
        }

        discovered.Add(new GameSaveData.Waystone(worldId, entryName));
        return true;
    }

    /// <summary>
    /// Records <paramref name="playerId"/> at <paramref name="position"/> in the world <paramref name="worldId"/>; its
    /// discovered waystones stay. Nothing for an empty player or world id.
    /// </summary>
    public void SetPlace(string playerId, string worldId, Vector2 position)
    {
        if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(worldId))
        {
            return;
        }

        places[playerId] = new GameSaveData.Player(playerId, worldId, position);
    }

    /// <summary>
    /// The id of the weapon <paramref name="playerId"/> holds, as the save names it; empty for a player this session
    /// knows no weapon of (it holds its starting weapon).
    /// </summary>
    public string WeaponOf(string playerId)
    {
        return !string.IsNullOrEmpty(playerId) && weapons.TryGetValue(playerId, out string weapon) ? weapon : string.Empty;
    }

    /// <summary>
    /// Records that <paramref name="playerId"/> holds the weapon <paramref name="weapon"/> (an empty id forgets its
    /// weapon); saved with its place. Nothing for an empty player id.
    /// </summary>
    public void SetWeapon(string playerId, string weapon)
    {
        if (string.IsNullOrEmpty(playerId))
        {
            return;
        }

        if (string.IsNullOrEmpty(weapon))
        {
            weapons.Remove(playerId);
        }
        else
        {
            weapons[playerId] = weapon;
        }
    }

    /// <summary>
    /// The state of the world <paramref name="worldId"/>, made empty the first time the world is visited.
    /// </summary>
    public WorldState WorldOf(string worldId)
    {
        string key = worldId ?? string.Empty;
        if (!worlds.TryGetValue(key, out WorldState world))
        {
            world = new WorldState(key);
            worlds.Add(key, world);
        }

        return world;
    }

    /// <summary>
    /// The state of the world <paramref name="worldId"/>; false when it has not been visited.
    /// </summary>
    public bool TryGetWorld(string worldId, out WorldState world)
    {
        world = null;
        return worldId != null && worlds.TryGetValue(worldId, out world);
    }

    /// <summary>
    /// The state as save data of this session's id in the current format, at the world clock's time: a record per player
    /// with a place, its discovered waystones and weapon in it. A player with discoveries or a weapon but no place yet is
    /// left out; every player in a world has its place recorded before each save.
    /// </summary>
    public GameSaveData ToData()
    {
        List<GameSaveData.Player> players = new(places.Count);
        foreach (GameSaveData.Player place in places.Values)
        {
            players.Add(new GameSaveData.Player(place.PlayerId, place.WorldId, place.Position, WaystonesOf(place.PlayerId), WeaponOf(place.PlayerId)));
        }

        List<GameSaveData.World> saved = new(worlds.Count);
        foreach (WorldState world in worlds.Values)
        {
            if (!string.IsNullOrEmpty(world.WorldId))
            {
                saved.Add(world.ToData());
            }
        }

        GameSaveData data = new(Time, players, saved);
        data.SetSession(Id);
        return data;
    }

    /// <summary>
    /// A new session id: a GUID without separators.
    /// </summary>
    private static string NewId()
    {
        return System.Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Forgets every player and world, makes the session a real one again under <paramref name="id"/> and sets the world
    /// clock to <paramref name="time"/> from now.
    /// </summary>
    private void Restart(double time, string id)
    {
        Id = id;
        places.Clear();
        waystones.Clear();
        weapons.Clear();
        worlds.Clear();
        IsThrowaway = false;
        savedTime = time;
        clockAtStart = clock.Time;
    }
}
