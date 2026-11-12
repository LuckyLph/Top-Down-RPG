using System;
using Unity.Netcode;

/// <summary>
/// A player's slot as the host replicates it on the player object (<see cref="PlayerSlot"/>, Docs/UiRevamp.md, Phase 5):
/// its number from 1 to <see cref="PlayerSlots.Count"/> (0 for none), and whether the player is still joining, its
/// machine loading the world around it.
/// </summary>
public struct PlayerSlotState : INetworkSerializable, IEquatable<PlayerSlotState>
{
    private byte number;
    private bool joining;

    /// <summary>
    /// Creates the state of slot <paramref name="slotNumber"/>, still joining or not per <paramref name="isJoining"/>.
    /// </summary>
    public PlayerSlotState(int slotNumber, bool isJoining)
    {
        number = (byte)Math.Clamp(slotNumber, 0, byte.MaxValue);
        joining = isJoining;
    }

    public int Number => number;
    public bool Joining => joining;

    /// <summary>
    /// Reads or writes the number and the joining flag for Netcode.
    /// </summary>
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref number);
        serializer.SerializeValue(ref joining);
    }

    /// <summary>
    /// Whether <paramref name="other"/> holds the same number and joining flag.
    /// </summary>
    public bool Equals(PlayerSlotState other)
    {
        return number == other.number && joining == other.joining;
    }

    /// <summary>
    /// Whether <paramref name="obj"/> is a <see cref="PlayerSlotState"/> equal to this one.
    /// </summary>
    public override bool Equals(object obj)
    {
        return obj is PlayerSlotState other && Equals(other);
    }

    /// <summary>
    /// A hash of the number and the joining flag.
    /// </summary>
    public override int GetHashCode()
    {
        return number * 2 + (joining ? 1 : 0);
    }
}
