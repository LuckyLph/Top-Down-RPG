using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Decides which worlds and chunks one machine keeps loaded, one load or unload at a time. Each player the machine
/// streams for is an interest: a world and a position in it. Every chunk within the view plus the load margin of an
/// interest loads, nearest first; a loaded chunk unloads once no interest is within the view plus the load margin and
/// the unload hysteresis of it; a world nobody is in keeps everything it had for the empty-world grace, then unloads
/// its chunks and itself. A chunk another machine still holds (<see cref="IChunkHolds"/>: on the host, a chunk some
/// client has not released) never unloads, nor does its world, until it is released or everything is let go. Plain
/// logic with no engine lookups, so it is tested with fakes.
/// </summary>
public sealed class ChunkStreamer
{
    private sealed class WorldRecord
    {
        public WorldRecord(WorldDefinition world)
        {
            World = world;
        }

        public WorldDefinition World { get; }
        public bool Loaded { get; set; }
        public bool Failed { get; set; }
        public bool Occupied { get; set; }
        public float EmptySince { get; set; }
        public HashSet<Vector2Int> Chunks { get; } = new();
        public HashSet<Vector2Int> FailedChunks { get; } = new();
    }

    private readonly Vector2Int loadExtents;
    private readonly Vector2Int keepExtents;
    private readonly float emptyWorldGrace;
    private readonly IChunkHolds holds;
    private readonly List<WorldRecord> worlds = new();
    private readonly List<StreamInterest> interests = new();
    private readonly HashSet<Vector2Int> scratch = new();
    private StreamAction? pending;
    private bool releasingAll;
    private float now;

    /// <summary>
    /// Creates a streamer for a view of <paramref name="viewCells"/> cells, loading what lies within
    /// <paramref name="loadMargin"/> cells of it, unloading only what lies beyond a further
    /// <paramref name="unloadHysteresis"/> cells, and keeping an empty world for <paramref name="emptyWorldGrace"/>
    /// seconds, never unloading a chunk <paramref name="chunkHolds"/> says another machine holds (none when null).
    /// </summary>
    public ChunkStreamer(Vector2Int viewCells, int loadMargin, int unloadHysteresis, float emptyWorldGrace, IChunkHolds chunkHolds = null)
    {
        holds = chunkHolds;
        loadExtents = ExtentsOf(viewCells, loadMargin);
        keepExtents = ExtentsOf(viewCells, loadMargin + Mathf.Max(0, unloadHysteresis));
        this.emptyWorldGrace = Mathf.Max(0f, emptyWorldGrace);
    }

    public bool IsBusy => pending.HasValue;

    /// <summary>
    /// How many cells around a player's cell a rectangle reaches on each side: half of <paramref name="viewCells"/>,
    /// rounded up, plus <paramref name="margin"/>. With the load margin, the interest rectangle; with the load margin
    /// and the unload hysteresis, the rectangle a chunk must leave to unload.
    /// </summary>
    public static Vector2Int ExtentsOf(Vector2Int viewCells, int margin)
    {
        Vector2Int half = new((viewCells.x + 1) / 2, (viewCells.y + 1) / 2);
        return half + Vector2Int.one * Mathf.Max(0, margin);
    }

    /// <summary>
    /// Whether nothing is loaded and nothing is being loaded or unloaded.
    /// </summary>
    public bool IsEmpty => worlds.Count == 0 && !pending.HasValue;

    /// <summary>
    /// Takes the players streamed for now, at <paramref name="time"/> seconds of the game clock: a world gains an
    /// interest (and stops counting down its grace), or loses its last one (and starts).
    /// </summary>
    public void SetInterests(IReadOnlyList<StreamInterest> current, float time)
    {
        now = time;
        interests.Clear();
        for (int i = 0; i < current.Count; i++)
        {
            if (current[i].World != null)
            {
                interests.Add(current[i]);
                Record(current[i].World);
            }
        }

        foreach (WorldRecord record in worlds)
        {
            bool occupied = false;
            for (int i = 0; i < interests.Count && !occupied; i++)
            {
                occupied = interests[i].World == record.World;
            }

            if (record.Occupied && !occupied)
            {
                record.EmptySince = time;
            }

            record.Occupied = occupied;
        }
    }

    /// <summary>
    /// Lets every world go, as if nobody were in any and their grace were over, so the next actions unload
    /// everything; a later <see cref="SetInterests"/> with players streams again.
    /// </summary>
    public void ReleaseAll()
    {
        releasingAll = true;
        interests.Clear();
        foreach (WorldRecord record in worlds)
        {
            record.Occupied = false;
        }
    }

    /// <summary>
    /// Stops releasing everything (<see cref="ReleaseAll"/>) so interests stream again.
    /// </summary>
    public void Resume()
    {
        releasingAll = false;
    }

    /// <summary>
    /// The next load or unload to run, when none is running: a world an interest is in, then the nearest missing chunk
    /// around an interest, then a chunk no interest keeps, then the chunks and world of a world whose grace is over.
    /// The action counts as running until <see cref="Complete"/>.
    /// </summary>
    public bool TryNext(out StreamAction action)
    {
        action = default;
        if (pending.HasValue)
        {
            return false;
        }

        if (TryLoadWorld(out action) || TryLoadChunk(out action) || TryUnloadChunk(out action) || TryUnloadWorld(out action))
        {
            pending = action;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Records how the running action ended. A world or chunk that failed to load is not tried again while its world
    /// stays loaded.
    /// </summary>
    public void Complete(bool success)
    {
        if (!pending.HasValue)
        {
            return;
        }

        StreamAction action = pending.Value;
        pending = null;
        WorldRecord record = Find(action.World);
        if (record == null)
        {
            return;
        }

        switch (action.Kind)
        {
            case StreamActionKind.LoadWorld:
                record.Loaded = success;
                record.Failed = !success;
                break;
            case StreamActionKind.LoadChunk:
                if (success)
                {
                    record.Chunks.Add(action.Chunk);
                }
                else
                {
                    record.FailedChunks.Add(action.Chunk);
                }

                break;
            case StreamActionKind.UnloadChunk:
                record.Chunks.Remove(action.Chunk);
                break;
            case StreamActionKind.UnloadWorld:
                worlds.Remove(record);
                break;
        }
    }

    /// <summary>
    /// Whether <paramref name="world"/>'s root scene is loaded.
    /// </summary>
    public bool IsWorldLoaded(WorldDefinition world)
    {
        WorldRecord record = Find(world);
        return record != null && record.Loaded;
    }

    /// <summary>
    /// Whether the chunk at <paramref name="chunk"/> of <paramref name="world"/> is loaded.
    /// </summary>
    public bool IsChunkLoaded(WorldDefinition world, Vector2Int chunk)
    {
        WorldRecord record = Find(world);
        return record != null && record.Chunks.Contains(chunk);
    }

    /// <summary>
    /// Whether <paramref name="interest"/>'s world and every chunk within its load extents are loaded (chunks that
    /// failed to load count as done), so a player placed there stands on loaded ground.
    /// </summary>
    public bool IsSettled(StreamInterest interest)
    {
        WorldRecord record = Find(interest.World);
        if (record == null || !(record.Loaded || record.Failed))
        {
            return false;
        }

        scratch.Clear();
        AddLoadChunks(interest, scratch);
        foreach (Vector2Int chunk in scratch)
        {
            if (!record.Chunks.Contains(chunk) && !record.FailedChunks.Contains(chunk))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Adds the chunks within <paramref name="interest"/>'s load extents that its world has.
    /// </summary>
    private void AddLoadChunks(StreamInterest interest, HashSet<Vector2Int> chunks)
    {
        AddChunks(interest, loadExtents, chunks);
    }

    /// <summary>
    /// Adds the chunks of <paramref name="interest"/>'s world, overlapping the cells within
    /// <paramref name="extents"/> of its cell, that the world has a scene for.
    /// </summary>
    private static void AddChunks(StreamInterest interest, Vector2Int extents, HashSet<Vector2Int> chunks)
    {
        WorldDefinition world = interest.World;
        Vector2Int cell = ChunkCoords.CellOf(world.ToLocal(interest.Position));
        RectInt cells = ChunkCoords.Around(cell, extents);
        Vector2Int min = ChunkCoords.ChunkOf(cells.min);
        Vector2Int max = ChunkCoords.ChunkOf(new Vector2Int(cells.xMax - 1, cells.yMax - 1));
        for (int y = Mathf.Max(min.y, 0); y <= Mathf.Min(max.y, world.SizeInChunks.y - 1); y++)
        {
            for (int x = Mathf.Max(min.x, 0); x <= Mathf.Min(max.x, world.SizeInChunks.x - 1); x++)
            {
                Vector2Int chunk = new(x, y);
                if (world.HasChunk(chunk))
                {
                    chunks.Add(chunk);
                }
            }
        }
    }

    private bool TryLoadWorld(out StreamAction action)
    {
        action = default;
        if (releasingAll)
        {
            return false;
        }

        foreach (StreamInterest interest in interests)
        {
            WorldRecord record = Find(interest.World);
            if (record != null && !record.Loaded && !record.Failed)
            {
                action = new StreamAction(StreamActionKind.LoadWorld, record.World, default);
                return true;
            }
        }

        return false;
    }

    private bool TryLoadChunk(out StreamAction action)
    {
        action = default;
        if (releasingAll)
        {
            return false;
        }

        int bestDistance = int.MaxValue;
        foreach (StreamInterest interest in interests)
        {
            WorldRecord record = Find(interest.World);
            if (record == null || !record.Loaded)
            {
                continue;
            }

            Vector2Int cell = ChunkCoords.CellOf(record.World.ToLocal(interest.Position));
            scratch.Clear();
            AddLoadChunks(interest, scratch);
            foreach (Vector2Int chunk in scratch)
            {
                if (record.Chunks.Contains(chunk) || record.FailedChunks.Contains(chunk))
                {
                    continue;
                }

                int distance = ChunkCoords.SquaredDistance(cell, chunk);
                if (distance < bestDistance || (distance == bestDistance && IsBefore(chunk, action.Chunk)))
                {
                    bestDistance = distance;
                    action = new StreamAction(StreamActionKind.LoadChunk, record.World, chunk);
                }
            }
        }

        return bestDistance != int.MaxValue;
    }

    private bool TryUnloadChunk(out StreamAction action)
    {
        action = default;
        foreach (WorldRecord record in worlds)
        {
            bool releaseAll = IsReleased(record);
            if (!releaseAll && !record.Occupied)
            {
                continue;
            }

            scratch.Clear();
            if (!releaseAll)
            {
                foreach (StreamInterest interest in interests)
                {
                    if (interest.World == record.World)
                    {
                        AddChunks(interest, keepExtents, scratch);
                    }
                }
            }

            foreach (Vector2Int chunk in record.Chunks)
            {
                if (!scratch.Contains(chunk) && (releasingAll || !IsHeld(record.World, chunk)))
                {
                    action = new StreamAction(StreamActionKind.UnloadChunk, record.World, chunk);
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryUnloadWorld(out StreamAction action)
    {
        action = default;
        foreach (WorldRecord record in worlds)
        {
            if (IsReleased(record) && record.Chunks.Count == 0)
            {
                if (!record.Loaded)
                {
                    worlds.Remove(record);
                    return TryUnloadWorld(out action);
                }

                action = new StreamAction(StreamActionKind.UnloadWorld, record.World, default);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="record"/>'s world is to go: everything is being released, or nobody has been in it for
    /// the empty-world grace.
    /// </summary>
    private bool IsReleased(WorldRecord record)
    {
        return releasingAll || (!record.Occupied && now - record.EmptySince >= emptyWorldGrace);
    }

    /// <summary>
    /// Whether another machine still holds the chunk at <paramref name="chunk"/> of <paramref name="world"/>.
    /// </summary>
    private bool IsHeld(WorldDefinition world, Vector2Int chunk)
    {
        return holds != null && holds.IsChunkHeld(ChunkId.Of(world, chunk));
    }

    private WorldRecord Record(WorldDefinition world)
    {
        WorldRecord record = Find(world);
        if (record == null)
        {
            record = new WorldRecord(world) { EmptySince = now };
            worlds.Add(record);
        }

        return record;
    }

    private WorldRecord Find(WorldDefinition world)
    {
        foreach (WorldRecord record in worlds)
        {
            if (record.World == world)
            {
                return record;
            }
        }

        return null;
    }

    /// <summary>
    /// A fixed order between chunks at the same distance, so loads go the same way every time: rows from the bottom,
    /// then columns from the left.
    /// </summary>
    private static bool IsBefore(Vector2Int a, Vector2Int b)
    {
        return a.y != b.y ? a.y < b.y : a.x < b.x;
    }
}
