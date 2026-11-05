using System;
using Unity.Netcode;

/// <summary>
/// A waystone a client's player discovered, as the host replicates it on the player object (<c>PlayerWorldSync</c>,
/// Docs/KnownGaps.md, Phase 3): its world's place in <see cref="GameScenes.Worlds"/> and its entry's place in that
/// world's <see cref="WorldDefinition.Entries"/>, the same on every machine running one build (the build version is
/// checked as a client joins), so two small numbers stand for the world id and entry name the save keys it by
/// (<see cref="GameSaveData.Waystone"/>).
/// </summary>
public struct NetworkWaystone : INetworkSerializable, IEquatable<NetworkWaystone>
{
    private int world;
    private int entry;

    /// <summary>
    /// The entry at <paramref name="entryIndex"/> of the world at <paramref name="worldIndex"/>.
    /// </summary>
    public NetworkWaystone(int worldIndex, int entryIndex)
    {
        world = worldIndex;
        entry = entryIndex;
    }

    public int World => world;
    public int Entry => entry;

    /// <summary>
    /// The waystone of <paramref name="scenes"/>' world <paramref name="worldId"/> named <paramref name="entryName"/>;
    /// false when that world is not listed or has no such entry.
    /// </summary>
    public static bool TryFrom(GameScenes scenes, string worldId, string entryName, out NetworkWaystone waystone)
    {
        waystone = default;
        if (scenes == null)
        {
            return false;
        }

        for (int w = 0; w < scenes.Worlds.Count; w++)
        {
            WorldDefinition candidate = scenes.Worlds[w];
            if (candidate == null || candidate.WorldId != worldId)
            {
                continue;
            }

            for (int e = 0; e < candidate.Entries.Count; e++)
            {
                if (candidate.Entries[e] != null && candidate.Entries[e].Name == entryName)
                {
                    waystone = new NetworkWaystone(w, e);
                    return true;
                }
            }

            return false;
        }

        return false;
    }

    /// <summary>
    /// The world id and entry name this waystone stands for in <paramref name="scenes"/>; false when its world or entry
    /// is not listed there.
    /// </summary>
    public bool TryResolve(GameScenes scenes, out string worldId, out string entryName)
    {
        worldId = null;
        entryName = null;
        if (scenes == null || world < 0 || world >= scenes.Worlds.Count || scenes.Worlds[world] == null)
        {
            return false;
        }

        WorldDefinition definition = scenes.Worlds[world];
        if (entry < 0 || entry >= definition.Entries.Count || definition.Entries[entry] == null)
        {
            return false;
        }

        worldId = definition.WorldId;
        entryName = definition.Entries[entry].Name;
        return true;
    }

    /// <summary>
    /// Reads or writes both places for Netcode.
    /// </summary>
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref world);
        serializer.SerializeValue(ref entry);
    }

    /// <summary>
    /// Value equality on both places.
    /// </summary>
    public bool Equals(NetworkWaystone other)
    {
        return world == other.world && entry == other.entry;
    }

    /// <summary>
    /// True when <paramref name="obj"/> is an equal <see cref="NetworkWaystone"/>.
    /// </summary>
    public override bool Equals(object obj)
    {
        return obj is NetworkWaystone other && Equals(other);
    }

    /// <summary>
    /// A hash of both places, consistent with <see cref="Equals(NetworkWaystone)"/>.
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(world, entry);
    }
}
