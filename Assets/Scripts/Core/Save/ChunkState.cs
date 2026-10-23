using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One chunk's record in its world's <see cref="WorldState"/> (Docs/WorldStreaming.md: Persistence): the mobs killed
/// there, keyed by their spawn point's persistent id with the game time they died at, and the chunk's runtime terrain
/// edits, the last write per layer and cell winning. The chunk reads it as it loads and writes to it as things happen.
/// </summary>
public sealed class ChunkState
{
    private readonly Dictionary<string, double> deadMobs = new();
    private readonly List<GameSaveData.CellEdit> terrainEdits = new();
    private readonly Dictionary<(int, Vector2Int), int> editIndexByCell = new();

    /// <summary>
    /// Creates the empty record of the chunk at <paramref name="coords"/>.
    /// </summary>
    public ChunkState(Vector2Int coords)
    {
        Coords = coords;
    }

    public Vector2Int Coords { get; }
    public int DeadMobCount => deadMobs.Count;
    public IReadOnlyList<GameSaveData.CellEdit> TerrainEdits => terrainEdits;
    public bool IsEmpty => deadMobs.Count == 0 && terrainEdits.Count == 0;

    /// <summary>
    /// Records that the mob of the spawn point <paramref name="mobId"/> died at game time <paramref name="time"/>,
    /// replacing an earlier death. Nothing for an empty id.
    /// </summary>
    public void RecordMobDeath(string mobId, double time)
    {
        if (!string.IsNullOrEmpty(mobId))
        {
            deadMobs[mobId] = time;
        }
    }

    /// <summary>
    /// Forgets the death of the mob of <paramref name="mobId"/>; false when it had none here.
    /// </summary>
    public bool ForgetMobDeath(string mobId)
    {
        return !string.IsNullOrEmpty(mobId) && deadMobs.Remove(mobId);
    }

    /// <summary>
    /// The game time the mob of <paramref name="mobId"/> died at; false when it has no recorded death.
    /// </summary>
    public bool TryGetMobDeath(string mobId, out double time)
    {
        time = 0d;
        return !string.IsNullOrEmpty(mobId) && deadMobs.TryGetValue(mobId, out time);
    }

    /// <summary>
    /// Whether the mob of <paramref name="mobId"/> stays dead as its chunk loads at game time <paramref name="now"/>:
    /// it died and either never <paramref name="respawns"/> or died less than <paramref name="respawnDelaySeconds"/>
    /// ago. A death whose delay has passed is forgotten, so the mob spawns again and can die again.
    /// </summary>
    public bool StaysDead(string mobId, double now, bool respawns, float respawnDelaySeconds)
    {
        if (!TryGetMobDeath(mobId, out double diedAt))
        {
            return false;
        }

        if (!respawns || now < diedAt + respawnDelaySeconds)
        {
            return true;
        }

        deadMobs.Remove(mobId);
        return false;
    }

    /// <summary>
    /// Records that <paramref name="cell"/> of the terrain layer <paramref name="layer"/> was filled or emptied,
    /// replacing an earlier edit of the same layer and cell in place.
    /// </summary>
    public void RecordTerrainEdit(int layer, Vector2Int cell, bool filled)
    {
        GameSaveData.CellEdit edit = new(layer, cell, filled);
        (int, Vector2Int) key = (layer, cell);
        if (editIndexByCell.TryGetValue(key, out int index))
        {
            terrainEdits[index] = edit;
            return;
        }

        editIndexByCell[key] = terrainEdits.Count;
        terrainEdits.Add(edit);
    }

    /// <summary>
    /// The record as save data.
    /// </summary>
    public GameSaveData.Chunk ToData()
    {
        List<GameSaveData.MobDeath> deaths = new(deadMobs.Count);
        foreach (KeyValuePair<string, double> death in deadMobs)
        {
            deaths.Add(new GameSaveData.MobDeath(death.Key, death.Value));
        }

        return new GameSaveData.Chunk(Coords, deaths, terrainEdits);
    }
}
