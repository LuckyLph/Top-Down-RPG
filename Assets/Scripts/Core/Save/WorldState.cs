using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What persists of one world (Docs/WorldStreaming.md: Persistence): a <see cref="ChunkState"/> per chunk, keyed by
/// its coordinates, holding its killed mobs and runtime terrain edits. Kept only where game state is decided (offline or
/// on the host), in the <see cref="SessionSave"/>, for every world visited this session, loaded or not; a world's scope
/// hands it to its chunks.
/// </summary>
public sealed class WorldState
{
    private readonly Dictionary<Vector2Int, ChunkState> chunks = new();

    /// <summary>
    /// Creates the empty state of the world <paramref name="worldId"/>.
    /// </summary>
    public WorldState(string worldId)
    {
        WorldId = worldId;
    }

    public string WorldId { get; }
    public int ChunkCount => chunks.Count;

    /// <summary>
    /// The record of the chunk at <paramref name="coords"/>, made empty on first use.
    /// </summary>
    public ChunkState Chunk(Vector2Int coords)
    {
        if (!chunks.TryGetValue(coords, out ChunkState chunk))
        {
            chunk = new ChunkState(coords);
            chunks.Add(coords, chunk);
        }

        return chunk;
    }

    /// <summary>
    /// The record of the chunk at <paramref name="coords"/>; false when it has none yet.
    /// </summary>
    public bool TryGetChunk(Vector2Int coords, out ChunkState chunk)
    {
        return chunks.TryGetValue(coords, out chunk);
    }

    /// <summary>
    /// The state as save data, leaving out the chunks whose record is empty.
    /// </summary>
    public GameSaveData.World ToData()
    {
        List<GameSaveData.Chunk> saved = new();
        foreach (ChunkState chunk in chunks.Values)
        {
            if (!chunk.IsEmpty)
            {
                saved.Add(chunk.ToData());
            }
        }

        return new GameSaveData.World(WorldId, saved);
    }

    /// <summary>
    /// The state <paramref name="data"/> holds; a chunk listed twice keeps both records merged.
    /// </summary>
    public static WorldState FromData(GameSaveData.World data)
    {
        WorldState state = new(data.WorldId);
        foreach (GameSaveData.Chunk saved in data.Chunks)
        {
            if (saved == null)
            {
                continue;
            }

            ChunkState chunk = state.Chunk(saved.Coords);
            foreach (GameSaveData.MobDeath death in saved.DeadMobs)
            {
                if (death != null)
                {
                    chunk.RecordMobDeath(death.Id, death.DiedAt);
                }
            }

            foreach (GameSaveData.CellEdit edit in saved.TerrainEdits)
            {
                chunk.RecordTerrainEdit(edit.Layer, edit.Cell, edit.Filled);
            }
        }

        return state;
    }
}
