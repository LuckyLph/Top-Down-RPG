using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// One session's saved game as written to disk (Docs/WorldStreaming.md: Persistence; Docs/KnownGaps.md, Phase 5): a
/// format version, the session's id, when it was saved, the game time played on the session, each player's place by
/// player id (its world, its position, the waystones it discovered and the weapon it holds), and the state of every world
/// visited, a record per chunk of its killed mobs and runtime terrain edits. A joined client's copy of its own place
/// names its host's session (<see cref="Host"/>) instead of an id of its own, holds its own player alone and no world,
/// and says whether that host is gone (<see cref="HostGone"/>): the connection to it was lost.
/// </summary>
[Serializable]
public sealed class GameSaveData
{
    public const int CurrentVersion = 4;
    public const int OldestImportableVersion = 2;

    /// <summary>
    /// One player's place: the world it is in, its position there, the waystones it has discovered and the id of the
    /// weapon it holds (empty for its starting weapon).
    /// </summary>
    [Serializable]
    public sealed class Player
    {
        [SerializeField] private string playerId;
        [SerializeField] private string worldId;
        [SerializeField] private Vector2 position;
        [SerializeField] private List<Waystone> waystones = new();
        [SerializeField] private string weapon;

        /// <summary>
        /// Creates an empty record for the serializer.
        /// </summary>
        public Player()
        {
        }

        /// <summary>
        /// Creates the place of <paramref name="playerId"/>: <paramref name="position"/> in the world
        /// <paramref name="worldId"/>, with the discovered <paramref name="waystones"/> (none when null) and the weapon
        /// <paramref name="weapon"/> (none, its starting weapon, when null).
        /// </summary>
        public Player(string playerId, string worldId, Vector2 position, IEnumerable<Waystone> waystones = null, string weapon = null)
        {
            this.playerId = playerId;
            this.worldId = worldId;
            this.position = position;
            this.weapon = weapon ?? string.Empty;
            if (waystones != null)
            {
                this.waystones.AddRange(waystones);
            }
        }

        public string PlayerId => playerId;
        public string WorldId => worldId;
        public Vector2 Position => position;
        public IReadOnlyList<Waystone> Waystones => waystones;
        public string Weapon => weapon ?? string.Empty;
    }

    /// <summary>A waystone a player discovered: a world's entry, by world id and entry name.</summary>
    [Serializable]
    public sealed class Waystone
    {
        [SerializeField] private string worldId;
        [SerializeField] private string entry;

        /// <summary>
        /// Creates an empty record for the serializer.
        /// </summary>
        public Waystone()
        {
        }

        /// <summary>
        /// Creates the waystone <paramref name="entry"/> of the world <paramref name="worldId"/>.
        /// </summary>
        public Waystone(string worldId, string entry)
        {
            this.worldId = worldId;
            this.entry = entry;
        }

        public string WorldId => worldId;
        public string Entry => entry;
    }

    /// <summary>The state of one world visited: a record per chunk that has one.</summary>
    [Serializable]
    public sealed class World
    {
        [SerializeField] private string worldId;
        [SerializeField] private List<Chunk> chunks = new();

        /// <summary>
        /// Creates an empty record for the serializer.
        /// </summary>
        public World()
        {
        }

        /// <summary>
        /// Creates the state of the world <paramref name="worldId"/> with <paramref name="chunks"/>.
        /// </summary>
        public World(string worldId, IEnumerable<Chunk> chunks)
        {
            this.worldId = worldId;
            this.chunks.AddRange(chunks);
        }

        public string WorldId => worldId;
        public IReadOnlyList<Chunk> Chunks => chunks;
    }

    /// <summary>One chunk's record: its killed mobs, by spawn point id, and its runtime terrain edits.</summary>
    [Serializable]
    public sealed class Chunk
    {
        [SerializeField] private Vector2Int coords;
        [SerializeField] private List<MobDeath> deadMobs = new();
        [SerializeField] private List<CellEdit> terrainEdits = new();

        /// <summary>
        /// Creates an empty record for the serializer.
        /// </summary>
        public Chunk()
        {
        }

        /// <summary>
        /// Creates the record of the chunk at <paramref name="coords"/>.
        /// </summary>
        public Chunk(Vector2Int coords, IEnumerable<MobDeath> deadMobs, IEnumerable<CellEdit> terrainEdits)
        {
            this.coords = coords;
            this.deadMobs.AddRange(deadMobs);
            this.terrainEdits.AddRange(terrainEdits);
        }

        public Vector2Int Coords => coords;
        public IReadOnlyList<MobDeath> DeadMobs => deadMobs;
        public IReadOnlyList<CellEdit> TerrainEdits => terrainEdits;
    }

    /// <summary>A killed mob: its spawn point's persistent id and the game time it died at.</summary>
    [Serializable]
    public sealed class MobDeath
    {
        [SerializeField] private string id;
        [SerializeField] private double diedAt;

        /// <summary>
        /// Creates an empty record for the serializer.
        /// </summary>
        public MobDeath()
        {
        }

        /// <summary>
        /// Creates the death of the mob of spawn point <paramref name="id"/> at game time <paramref name="diedAt"/>.
        /// </summary>
        public MobDeath(string id, double diedAt)
        {
            this.id = id;
            this.diedAt = diedAt;
        }

        public string Id => id;
        public double DiedAt => diedAt;
    }

    /// <summary>
    /// One cell of a runtime terrain edit: the chunk terrain's layer it changed, named by what it draws (its ground
    /// material's id, or the walls, as the chunk's terrain names its layers), the cell, and filled or emptied. Naming the
    /// layer rather than its index lets a chunk's layers be reordered or pruned without moving the edit to another layer.
    /// </summary>
    [Serializable]
    public struct CellEdit : IEquatable<CellEdit>
    {
        [SerializeField] private string layer;
        [SerializeField] private Vector2Int cell;
        [SerializeField] private bool filled;

        /// <summary>
        /// Creates the edit of <paramref name="cell"/> on the layer named <paramref name="layer"/>.
        /// </summary>
        public CellEdit(string layer, Vector2Int cell, bool filled)
        {
            this.layer = layer ?? string.Empty;
            this.cell = cell;
            this.filled = filled;
        }

        public string Layer => layer ?? string.Empty;
        public Vector2Int Cell => cell;
        public bool Filled => filled;

        /// <summary>
        /// Value equality on layer, cell and filled flag.
        /// </summary>
        public bool Equals(CellEdit other)
        {
            return Layer == other.Layer && cell == other.cell && filled == other.filled;
        }

        /// <summary>
        /// True when <paramref name="obj"/> is a <see cref="CellEdit"/> with the same layer, cell and filled flag.
        /// </summary>
        public override bool Equals(object obj)
        {
            return obj is CellEdit other && Equals(other);
        }

        /// <summary>
        /// Hash of the layer, cell and filled flag, consistent with <see cref="Equals(CellEdit)"/>.
        /// </summary>
        public override int GetHashCode()
        {
            return HashCode.Combine(Layer, cell, filled);
        }
    }

    [SerializeField] private int version;
    [SerializeField] private string id;
    [SerializeField] private string savedAt;
    [SerializeField] private string host;
    [SerializeField] private bool hostGone;
    [SerializeField] private double time;
    [SerializeField] private List<Player> players = new();
    [SerializeField] private List<World> worlds = new();

    /// <summary>
    /// Creates save data in the current format: <paramref name="time"/> of game time played, and the given players'
    /// places and worlds' states.
    /// </summary>
    public GameSaveData(double time, IEnumerable<Player> players, IEnumerable<World> worlds)
    {
        version = CurrentVersion;
        this.time = time;
        this.players.AddRange(players);
        this.worlds.AddRange(worlds);
    }

    /// <summary>
    /// Creates empty data (version 0) for <see cref="TryParse"/> to fill from JSON.
    /// </summary>
    private GameSaveData()
    {
    }

    public int Version => version;
    public string Id => id ?? string.Empty;
    public string SavedAt => savedAt ?? string.Empty;
    public string Host => host ?? string.Empty;
    public bool HostGone => hostGone;
    public bool IsCopy => !string.IsNullOrEmpty(host);
    public double Time => time;
    public IReadOnlyList<Player> Players => players;
    public IReadOnlyList<World> Worlds => worlds;

    /// <summary>
    /// Whether this build reads a session's save of format <paramref name="formatVersion"/>: the current one only.
    /// </summary>
    public static bool IsReadable(int formatVersion)
    {
        return formatVersion == CurrentVersion;
    }

    /// <summary>
    /// Whether this build imports the single save file of an earlier build, format <paramref name="formatVersion"/>, as
    /// a session (<see cref="GameSave"/>): version 2 (its players hold no weapon) and 3, whose terrain edits named
    /// layers by index and are dropped.
    /// </summary>
    public static bool IsImportable(int formatVersion)
    {
        return formatVersion >= OldestImportableVersion && formatVersion < CurrentVersion;
    }

    /// <summary>
    /// Makes this the save of the session <paramref name="sessionId"/>, no copy of a host's.
    /// </summary>
    public void SetSession(string sessionId)
    {
        id = sessionId ?? string.Empty;
        host = string.Empty;
        hostGone = false;
    }

    /// <summary>
    /// Makes this a joined client's copy of its own place in the host's session <paramref name="hostSessionId"/>,
    /// saying whether that host is gone (<paramref name="gone"/>).
    /// </summary>
    public void SetHost(string hostSessionId, bool gone)
    {
        id = string.Empty;
        host = hostSessionId ?? string.Empty;
        hostGone = gone;
    }

    /// <summary>
    /// Records <paramref name="utc"/> as when this was saved, in the round-trip format.
    /// </summary>
    public void Stamp(DateTime utc)
    {
        savedAt = utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// When this was saved, read from <see cref="SavedAt"/>; the earliest time there is when it holds none or cannot be
    /// read.
    /// </summary>
    public DateTime SavedAtUtc()
    {
        return DateTime.TryParse(SavedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed)
            ? parsed.ToUniversalTime()
            : DateTime.MinValue;
    }

    /// <summary>
    /// The data as pretty-printed JSON.
    /// </summary>
    public string ToJson()
    {
        return JsonUtility.ToJson(this, true);
    }

    /// <summary>
    /// Parses <paramref name="json"/>; false only when it is not valid JSON. Missing fields keep their defaults (a
    /// missing version reads as 0), so callers still check <see cref="Version"/>.
    /// </summary>
    public static bool TryParse(string json, out GameSaveData data)
    {
        data = new GameSaveData();
        try
        {
            JsonUtility.FromJsonOverwrite(json, data);
            return true;
        }
        catch (ArgumentException)
        {
            data = null;
            return false;
        }
    }
}
