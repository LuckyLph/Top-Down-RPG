using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gameplay-scope registry of the <see cref="WorldExit"/>s in the loaded chunks of every loaded world on this machine.
/// It reads a chunk's exits from its scene as <see cref="Worlds"/> reports it loaded and forgets them as it unloads, so
/// an exit is known exactly while its chunk is up. Lookups scan the few loaded exits and run only when asked: by the
/// trip's authority checking a request and by the local player's trigger when its cell changes.
/// </summary>
public sealed class WorldExits : IDisposable
{
    /// <summary>A loaded exit with the world and chunk it belongs to.</summary>
    private readonly struct Record
    {
        public Record(WorldDefinition world, Vector2Int chunk, WorldExit exit)
        {
            World = world;
            Chunk = chunk;
            Exit = exit;
        }

        public WorldDefinition World { get; }
        public Vector2Int Chunk { get; }
        public WorldExit Exit { get; }
    }

    private readonly Worlds worlds;
    private readonly List<Record> exits = new();

    /// <summary>
    /// Follows the chunks <paramref name="loadedWorlds"/> loads and unloads.
    /// </summary>
    public WorldExits(Worlds loadedWorlds)
    {
        worlds = loadedWorlds;
        worlds.ChunkLoaded += HandleChunkLoaded;
        worlds.ChunkUnloading += HandleChunkUnloading;
    }

    public int Count => exits.Count;

    /// <summary>
    /// Records <paramref name="exit"/> as standing in the chunk at <paramref name="chunk"/> of
    /// <paramref name="world"/>.
    /// </summary>
    public void Add(WorldDefinition world, Vector2Int chunk, WorldExit exit)
    {
        if (world != null && exit != null)
        {
            exits.Add(new Record(world, chunk, exit));
        }
    }

    /// <summary>
    /// The loaded exit of <paramref name="world"/> whose persistent id is <paramref name="exitId"/>; false when no
    /// loaded chunk of that world holds one.
    /// </summary>
    public bool TryFind(WorldDefinition world, string exitId, out WorldExit exit)
    {
        exit = null;
        if (world == null || string.IsNullOrEmpty(exitId))
        {
            return false;
        }

        for (int i = 0; i < exits.Count; i++)
        {
            if (exits[i].World == world && exits[i].Exit != null && exits[i].Exit.Id == exitId)
            {
                exit = exits[i].Exit;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The loaded exit of <paramref name="world"/> covering its cell <paramref name="cell"/>; false when none does.
    /// </summary>
    public bool TryFindAt(WorldDefinition world, Vector2Int cell, out WorldExit exit)
    {
        exit = null;
        if (world == null)
        {
            return false;
        }

        for (int i = 0; i < exits.Count; i++)
        {
            if (exits[i].World == world && exits[i].Exit != null && exits[i].Exit.CellsIn(world).Contains(cell))
            {
                exit = exits[i].Exit;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Stops following the loaded chunks.
    /// </summary>
    public void Dispose()
    {
        worlds.ChunkLoaded -= HandleChunkLoaded;
        worlds.ChunkUnloading -= HandleChunkUnloading;
        exits.Clear();
    }

    private void HandleChunkLoaded(LoadedWorld world, Vector2Int coords)
    {
        if (!world.Chunks.TryGetValue(coords, out Scene scene))
        {
            return;
        }

        foreach (WorldExit exit in SceneQuery.FindAll<WorldExit>(scene))
        {
            Add(world.Definition, coords, exit);
        }
    }

    private void HandleChunkUnloading(LoadedWorld world, Vector2Int coords)
    {
        for (int i = exits.Count - 1; i >= 0; i--)
        {
            if (exits[i].World == world.Definition && exits[i].Chunk == coords)
            {
                exits.RemoveAt(i);
            }
        }
    }
}
