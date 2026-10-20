using System;
using UnityEngine;

/// <summary>
/// A chunk of a seamless world as every machine names it: its world's id (<see cref="WorldDefinition.WorldId"/>) and
/// its coordinates in that world's chunk grid. Plain data, so the per-chunk readiness and the streaming's holds are
/// tested without Netcode types; <see cref="NetworkChunkId"/> is its form on the wire.
/// </summary>
public readonly struct ChunkId : IEquatable<ChunkId>
{
    /// <summary>
    /// Names the chunk at <paramref name="coords"/> of the world whose id is <paramref name="worldId"/>; null reads as
    /// an empty id.
    /// </summary>
    public ChunkId(string worldId, Vector2Int coords)
    {
        WorldId = worldId ?? string.Empty;
        Coords = coords;
    }

    public string WorldId { get; }
    public Vector2Int Coords { get; }

    /// <summary>
    /// Whether it names a world at all; the default value does not.
    /// </summary>
    public bool IsValid => !string.IsNullOrEmpty(WorldId);

    /// <summary>
    /// The id of the chunk at <paramref name="coords"/> of <paramref name="world"/> (an invalid id for no world).
    /// </summary>
    public static ChunkId Of(WorldDefinition world, Vector2Int coords)
    {
        return new ChunkId(world != null ? world.WorldId : string.Empty, coords);
    }

    /// <summary>
    /// Whether <paramref name="other"/> names the same world, by ordinal comparison of the ids, and coordinates.
    /// </summary>
    public bool Equals(ChunkId other)
    {
        return string.Equals(WorldId ?? string.Empty, other.WorldId ?? string.Empty, StringComparison.Ordinal) && Coords == other.Coords;
    }

    /// <summary>
    /// True when <paramref name="obj"/> is a <see cref="ChunkId"/> naming the same chunk.
    /// </summary>
    public override bool Equals(object obj)
    {
        return obj is ChunkId other && Equals(other);
    }

    /// <summary>
    /// A hash of the world id and coordinates, consistent with <see cref="Equals(ChunkId)"/>.
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(StringComparer.Ordinal.GetHashCode(WorldId ?? string.Empty), Coords);
    }

    /// <summary>
    /// The id for logs and test messages.
    /// </summary>
    public override string ToString()
    {
        return $"{WorldId} {Coords}";
    }
}
