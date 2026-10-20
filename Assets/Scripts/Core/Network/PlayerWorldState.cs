using System;
using Unity.Collections;
using Unity.Netcode;

/// <summary>
/// A player's world as the host replicates it on the player object (<c>PlayerWorldSync</c>): the world's id (empty for
/// no world), the player's trip count, and the entry its latest trip leads to (empty before its first trip).
/// </summary>
public struct PlayerWorldState : INetworkSerializable, IEquatable<PlayerWorldState>
{
    private FixedString64Bytes worldId;
    private int trip;
    private FixedString64Bytes entry;

    /// <summary>
    /// Creates the state of a player in the world <paramref name="worldId"/> after <paramref name="tripCount"/> trips,
    /// the latest to <paramref name="entryName"/>. Null strings read as empty.
    /// </summary>
    public PlayerWorldState(string worldId, int tripCount, string entryName)
    {
        this.worldId = new FixedString64Bytes(worldId ?? string.Empty);
        trip = tripCount;
        entry = new FixedString64Bytes(entryName ?? string.Empty);
    }

    public string WorldId => worldId.ToString();
    public bool HasWorld => worldId.Length > 0;
    public int Trip => trip;
    public string Entry => entry.ToString();

    /// <summary>
    /// Reads or writes the world id, trip count and entry for Netcode.
    /// </summary>
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref worldId);
        serializer.SerializeValue(ref trip);
        serializer.SerializeValue(ref entry);
    }

    /// <summary>
    /// Value equality on every field, which the network variable uses to tell a change.
    /// </summary>
    public bool Equals(PlayerWorldState other)
    {
        return worldId.Equals(other.worldId) && trip == other.trip && entry.Equals(other.entry);
    }

    /// <summary>
    /// True when <paramref name="obj"/> is an equal <see cref="PlayerWorldState"/>.
    /// </summary>
    public override bool Equals(object obj)
    {
        return obj is PlayerWorldState other && Equals(other);
    }

    /// <summary>
    /// A hash of every field, consistent with <see cref="Equals(PlayerWorldState)"/>.
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(worldId.GetHashCode(), trip, entry.GetHashCode());
    }
}
