using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What persists of one world (Docs/WorldStreaming.md: Persistence): a <see cref="ChunkState"/> per chunk, keyed by
/// its coordinates, holding its killed mobs and runtime terrain edits. A mob's death is kept in its home chunk's record
/// and found in any chunk's, since a regenerated world can move a spawn point, with its id, to another chunk. Kept only
/// where game state is decided (offline or on the host), in the <see cref="SessionSave"/>, for every world visited
/// this session, loaded or not; a world's scope hands it to its chunks.
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
    /// Records that the mob of the spawn point <paramref name="mobId"/> died at game time <paramref name="time"/> in the
    /// record of the chunk at <paramref name="coords"/>, its home, forgetting any death of the same id another chunk
    /// holds: ids are unique in a world, and regenerating it can move a spawn point to another chunk.
    /// </summary>
    public void RecordMobDeath(Vector2Int coords, string mobId, double time)
    {
        if (string.IsNullOrEmpty(mobId))
        {
            return;
        }

        foreach (ChunkState chunk in chunks.Values)
        {
            if (chunk.Coords != coords)
            {
                chunk.ForgetMobDeath(mobId);
            }
        }

        Chunk(coords).RecordMobDeath(mobId, time);
    }

    /// <summary>
    /// The record holding the death of the mob of <paramref name="mobId"/>, whichever chunk it is in: a spawn point
    /// regenerating the world moved to another chunk keeps its death. The record of <paramref name="home"/> is looked at
    /// first. False when no chunk holds one.
    /// </summary>
    public bool TryFindMobDeath(string mobId, Vector2Int home, out ChunkState holder)
    {
        holder = null;
        if (string.IsNullOrEmpty(mobId))
        {
            return false;
        }

        if (chunks.TryGetValue(home, out ChunkState own) && own.TryGetMobDeath(mobId, out _))
        {
            holder = own;
            return true;
        }

        foreach (ChunkState chunk in chunks.Values)
        {
            if (chunk.TryGetMobDeath(mobId, out _))
            {
                holder = chunk;
                return true;
            }
        }

        return false;
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
