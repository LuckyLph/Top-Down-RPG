using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

/// <summary>
/// Gameplay-scope entry point that shows only the local player's world: that world's lighting is on and every other
/// loaded world's is off, and only that world's chunks near the local player draw their ground. A chunk's terrain
/// (<see cref="WorldChunk"/>) is enabled once the local player's interest rectangle (the view plus the load margin)
/// overlaps it, and stays enabled until it leaves the rectangle widened by the unload hysteresis, so walking back and
/// forth over a border rebuilds nothing; every chunk of another world is disabled, so the host builds no ground for
/// the chunks it loads for other players. It applies this when the local player, its world, its cell or the loaded
/// worlds and chunks change; each frame it only compares the local player's cell with the last one.
/// </summary>
public sealed class LocalWorldPresentation : IStartable, ITickable, IDisposable
{
    /// <summary>A loaded chunk's marker, with its world and coordinates.</summary>
    private readonly struct ShownChunk
    {
        public ShownChunk(LoadedWorld world, Vector2Int coords, WorldChunk chunk)
        {
            World = world;
            Coords = coords;
            Chunk = chunk;
        }

        public LoadedWorld World { get; }
        public Vector2Int Coords { get; }
        public WorldChunk Chunk { get; }
    }

    private readonly Worlds worlds;
    private readonly PlayerWorlds playerWorlds;
    private readonly LocalPlayerTracker localPlayer;
    private readonly Vector2Int drawExtents;
    private readonly Vector2Int keepExtents;
    private readonly List<ShownChunk> chunks = new();
    private WorldDefinition shownWorld;
    private Vector2Int shownCell;
    private bool dirty = true;

    /// <summary>
    /// Captures the loaded worlds, the players' worlds, the local player, and the interest rectangle's extents from
    /// <paramref name="settings"/>.
    /// </summary>
    public LocalWorldPresentation(Worlds worlds, PlayerWorlds playerWorlds, LocalPlayerTracker localPlayer, WorldStreamingSettings settings)
    {
        this.worlds = worlds;
        this.playerWorlds = playerWorlds;
        this.localPlayer = localPlayer;
        drawExtents = settings.LoadExtents;
        keepExtents = settings.KeepExtents;
    }

    /// <summary>
    /// Subscribes to the changes that move the local player's world or the loaded chunks, records the chunks already
    /// loaded and applies it once.
    /// </summary>
    public void Start()
    {
        worlds.WorldLoaded += HandleWorldLoaded;
        worlds.ChunkLoaded += HandleChunkLoaded;
        worlds.ChunkUnloading += HandleChunkUnloading;
        playerWorlds.Changed += HandlePlayerWorldChanged;
        localPlayer.Changed += HandleLocalPlayerChanged;
        foreach (LoadedWorld world in worlds.Loaded)
        {
            foreach (KeyValuePair<Vector2Int, Scene> chunk in world.Chunks)
            {
                Record(world, chunk.Key, chunk.Value);
            }
        }

        ApplyLighting();
        Tick();
    }

    /// <summary>
    /// Unsubscribes.
    /// </summary>
    public void Dispose()
    {
        worlds.WorldLoaded -= HandleWorldLoaded;
        worlds.ChunkLoaded -= HandleChunkLoaded;
        worlds.ChunkUnloading -= HandleChunkUnloading;
        playerWorlds.Changed -= HandlePlayerWorldChanged;
        localPlayer.Changed -= HandleLocalPlayerChanged;
    }

    /// <summary>
    /// Shows and hides the chunks' ground when the local player's world or cell changed, or a chunk came or went.
    /// </summary>
    public void Tick()
    {
        WorldDefinition local = LocalWorld();
        Vector2Int cell = shownCell;
        Transform body = localPlayer.Current != null ? localPlayer.Current.Handle.Transform : null;
        if (local != null && body != null)
        {
            cell = ChunkCoords.CellOf(local.ToLocal(body.position));
        }

        if (!dirty && local == shownWorld && cell == shownCell)
        {
            return;
        }

        dirty = false;
        shownWorld = local;
        shownCell = cell;
        RectInt draw = ChunkCoords.Around(cell, drawExtents);
        RectInt keep = ChunkCoords.Around(cell, keepExtents);
        for (int i = chunks.Count - 1; i >= 0; i--)
        {
            ShownChunk shown = chunks[i];
            if (shown.Chunk == null)
            {
                chunks.RemoveAt(i);
                continue;
            }

            bool inLocalWorld = local != null && shown.World.Definition == local;
            shown.Chunk.SetDrawn(ShouldDraw(inLocalWorld, shown.Chunk.IsDrawn, shown.Coords, draw, keep));
        }
    }

    /// <summary>
    /// Whether the chunk at <paramref name="coords"/> draws its ground: only in the local player's world
    /// (<paramref name="inLocalWorld"/>), once the interest rectangle <paramref name="draw"/> overlaps it, and while
    /// it is drawn (<paramref name="drawn"/>) until it leaves the wider <paramref name="keep"/>.
    /// </summary>
    internal static bool ShouldDraw(bool inLocalWorld, bool drawn, Vector2Int coords, RectInt draw, RectInt keep)
    {
        return inLocalWorld && (ChunkCoords.Overlaps(draw, coords) || (drawn && ChunkCoords.Overlaps(keep, coords)));
    }

    /// <summary>
    /// The local player's world, or null.
    /// </summary>
    private WorldDefinition LocalWorld()
    {
        return localPlayer.Current != null ? playerWorlds.Get(localPlayer.Current.Handle) : null;
    }

    /// <summary>
    /// Turns on the lighting of the local player's world and off every other loaded world's.
    /// </summary>
    private void ApplyLighting()
    {
        WorldDefinition local = LocalWorld();
        foreach (LoadedWorld world in worlds.Loaded)
        {
            if (world.Lighting != null)
            {
                world.Lighting.SetShown(world.Definition == local);
            }
        }
    }

    /// <summary>
    /// Records the chunk marker of the chunk at <paramref name="coords"/> of <paramref name="world"/>, loaded as
    /// <paramref name="scene"/>; a chunk scene without one draws as it was saved.
    /// </summary>
    private void Record(LoadedWorld world, Vector2Int coords, Scene scene)
    {
        WorldChunk chunk = SceneQuery.FindFirst<WorldChunk>(scene);
        if (chunk != null)
        {
            chunks.Add(new ShownChunk(world, coords, chunk));
            dirty = true;
        }
    }

    private void HandleWorldLoaded(LoadedWorld world)
    {
        ApplyLighting();
    }

    private void HandleChunkLoaded(LoadedWorld world, Vector2Int coords)
    {
        if (world.Chunks.TryGetValue(coords, out Scene scene))
        {
            Record(world, coords, scene);
            Tick();
        }
    }

    private void HandleChunkUnloading(LoadedWorld world, Vector2Int coords)
    {
        for (int i = chunks.Count - 1; i >= 0; i--)
        {
            if (chunks[i].World == world && chunks[i].Coords == coords)
            {
                chunks.RemoveAt(i);
            }
        }
    }

    private void HandlePlayerWorldChanged(PlayerHandle player, WorldDefinition world)
    {
        ApplyLighting();
        dirty = true;
    }

    private void HandleLocalPlayerChanged(LocalPlayer player)
    {
        ApplyLighting();
        dirty = true;
    }
}
