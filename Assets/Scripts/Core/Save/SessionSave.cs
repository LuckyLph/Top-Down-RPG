using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The running session's saved state, in memory (Docs/WorldStreaming.md: Persistence): the world clock, each player's
/// place by player id, and the <see cref="WorldState"/> of every world visited, loaded or not. It is used where game
/// state is decided, offline or on the host; a client keeps an empty one it never writes. A session starts new
/// (<see cref="StartNew"/>) or from the save file (<see cref="StartFrom"/>), and lasts across restarts, which reload the
/// Gameplay scene. The world clock is the game time played on the save: the saved time plus the game time since the
/// session started, so a time recorded in one session still means the same in the next.
/// </summary>
public sealed class SessionSave
{
    private readonly IClock clock;
    private readonly Dictionary<string, GameSaveData.Player> players = new();
    private readonly Dictionary<string, WorldState> worlds = new();
    private double savedTime;
    private float clockAtStart;

    /// <summary>
    /// Creates an empty session state whose world clock runs on <paramref name="gameClock"/>.
    /// </summary>
    public SessionSave(IClock gameClock)
    {
        clock = gameClock;
    }

    /// <summary>
    /// The world clock: the game time played on the save, in seconds.
    /// </summary>
    public double Time => savedTime + (clock.Time - clockAtStart);

    public int PlayerCount => players.Count;
    public int WorldCount => worlds.Count;

    /// <summary>
    /// Forgets everything and starts the world clock at zero, for a new game (or a joined client, which saves nothing).
    /// </summary>
    public void StartNew()
    {
        Restart(0d);
    }

    /// <summary>
    /// Replaces the state with <paramref name="data"/>'s and continues its world clock from the saved time. Players
    /// and worlds without an id are skipped.
    /// </summary>
    public void StartFrom(GameSaveData data)
    {
        Restart(data.Time);
        foreach (GameSaveData.Player player in data.Players)
        {
            if (player != null && !string.IsNullOrEmpty(player.PlayerId))
            {
                players[player.PlayerId] = player;
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
        if (!string.IsNullOrEmpty(playerId) && players.TryGetValue(playerId, out GameSaveData.Player player))
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
    /// The waystones <paramref name="playerId"/> has discovered; empty for a player this session does not know.
    /// </summary>
    public IReadOnlyList<GameSaveData.Waystone> WaystonesOf(string playerId)
    {
        return !string.IsNullOrEmpty(playerId) && players.TryGetValue(playerId, out GameSaveData.Player player)
            ? player.Waystones
            : System.Array.Empty<GameSaveData.Waystone>();
    }

    /// <summary>
    /// Records <paramref name="playerId"/> at <paramref name="position"/> in the world <paramref name="worldId"/>,
    /// keeping its discovered waystones. Nothing for an empty player or world id.
    /// </summary>
    public void SetPlace(string playerId, string worldId, Vector2 position)
    {
        if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(worldId))
        {
            return;
        }

        players[playerId] = new GameSaveData.Player(playerId, worldId, position, WaystonesOf(playerId));
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
    /// The state as save data in the current format, at the world clock's time.
    /// </summary>
    public GameSaveData ToData()
    {
        List<GameSaveData.World> saved = new(worlds.Count);
        foreach (WorldState world in worlds.Values)
        {
            if (!string.IsNullOrEmpty(world.WorldId))
            {
                saved.Add(world.ToData());
            }
        }

        return new GameSaveData(Time, players.Values, saved);
    }

    /// <summary>
    /// Forgets every player and world and sets the world clock to <paramref name="time"/> from now.
    /// </summary>
    private void Restart(double time)
    {
        players.Clear();
        worlds.Clear();
        savedTime = time;
        clockAtStart = clock.Time;
    }
}
