using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

/// <summary>
/// Gameplay-scope entry point that shows each loaded waystone's marker (Docs/KnownGaps.md, Phase 3) as this machine's
/// own player knows it: awake once <see cref="KnownWaystones"/> has it discovered, sleeping otherwise, on the host and a
/// client alike, each from its own record. It reads a chunk's <see cref="WaystoneMarker"/>s from its scene as
/// <see cref="Worlds"/> reports it loaded and forgets them as it unloads, and looks at every one again when
/// <see cref="KnownWaystones.Changed"/>; a marker naming an entry its world lacks sleeps. Presentation only: it decides
/// nothing, sends nothing and does nothing per frame.
/// </summary>
public sealed class WaystoneMarkers : IStartable, IDisposable
{
    /// <summary>A loaded marker with the world and chunk it stands in.</summary>
    private readonly struct Record
    {
        public Record(WorldDefinition world, Vector2Int chunk, WaystoneMarker marker)
        {
            World = world;
            Chunk = chunk;
            Marker = marker;
        }

        public WorldDefinition World { get; }
        public Vector2Int Chunk { get; }
        public WaystoneMarker Marker { get; }
    }

    private readonly Worlds worlds;
    private readonly KnownWaystones known;
    private readonly List<Record> markers = new();
    private bool started;

    /// <summary>
    /// Shows the markers of the chunks <paramref name="loadedWorlds"/> loads as <paramref name="knownWaystones"/> has
    /// them.
    /// </summary>
    public WaystoneMarkers(Worlds loadedWorlds, KnownWaystones knownWaystones)
    {
        worlds = loadedWorlds;
        known = knownWaystones;
    }

    /// <summary>
    /// How many loaded markers it shows.
    /// </summary>
    public int Count => markers.Count;

    /// <summary>
    /// Follows the chunks as they load and unload and what the player discovers, and shows the markers of the chunks
    /// already loaded.
    /// </summary>
    public void Start()
    {
        if (started)
        {
            return;
        }

        started = true;
        worlds.ChunkLoaded += HandleChunkLoaded;
        worlds.ChunkUnloading += HandleChunkUnloading;
        known.Changed += Refresh;
        IReadOnlyList<LoadedWorld> loaded = worlds.Loaded;
        for (int i = 0; i < loaded.Count; i++)
        {
            foreach (Vector2Int coords in loaded[i].Chunks.Keys)
            {
                HandleChunkLoaded(loaded[i], coords);
            }
        }
    }

    /// <summary>
    /// Stops following.
    /// </summary>
    public void Dispose()
    {
        if (started)
        {
            worlds.ChunkLoaded -= HandleChunkLoaded;
            worlds.ChunkUnloading -= HandleChunkUnloading;
            known.Changed -= Refresh;
            started = false;
        }

        markers.Clear();
    }

    /// <summary>
    /// Shows <paramref name="marker"/>, standing in the chunk at <paramref name="chunk"/> of <paramref name="world"/>, as
    /// the player knows its waystone from now on.
    /// </summary>
    public void Add(WorldDefinition world, Vector2Int chunk, WaystoneMarker marker)
    {
        if (world == null || marker == null)
        {
            return;
        }

        Record record = new(world, chunk, marker);
        markers.Add(record);
        Show(record);
    }

    /// <summary>
    /// Whether this machine shows the marker of <paramref name="world"/>'s entry <paramref name="entryName"/> awake: the
    /// entry is a waystone or the default one and the player discovered it.
    /// </summary>
    public bool ShowsAwake(WorldDefinition world, string entryName)
    {
        WorldDefinition.Entry entry = EntryNamed(world, entryName);
        return entry != null && known.IsDiscovered(world, entry);
    }

    /// <summary>
    /// Looks at every loaded marker again.
    /// </summary>
    private void Refresh()
    {
        for (int i = markers.Count - 1; i >= 0; i--)
        {
            if (markers[i].Marker == null)
            {
                markers.RemoveAt(i);
                continue;
            }

            Show(markers[i]);
        }
    }

    /// <summary>
    /// Shows one marker as the player knows its waystone.
    /// </summary>
    private void Show(Record record)
    {
        record.Marker.SetAwake(ShowsAwake(record.World, record.Marker.EntryName));
    }

    private void HandleChunkLoaded(LoadedWorld world, Vector2Int coords)
    {
        if (!world.Chunks.TryGetValue(coords, out Scene scene))
        {
            return;
        }

        foreach (WaystoneMarker marker in SceneQuery.FindAll<WaystoneMarker>(scene))
        {
            Add(world.Definition, coords, marker);
        }
    }

    private void HandleChunkUnloading(LoadedWorld world, Vector2Int coords)
    {
        for (int i = markers.Count - 1; i >= 0; i--)
        {
            if (markers[i].World == world.Definition && markers[i].Chunk == coords)
            {
                markers.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// The entry of <paramref name="world"/> named <paramref name="entryName"/>, or null, without the default entry's
    /// fallback.
    /// </summary>
    private static WorldDefinition.Entry EntryNamed(WorldDefinition world, string entryName)
    {
        if (world == null || string.IsNullOrEmpty(entryName))
        {
            return null;
        }

        IReadOnlyList<WorldDefinition.Entry> entries = world.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i] != null && entries[i].Name == entryName)
            {
                return entries[i];
            }
        }

        return null;
    }
}
