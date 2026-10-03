using System;
using Unity.Netcode;

/// <summary>
/// One status instance on the wire: catalog index, instance id, stacks, and the seconds remaining when the host
/// wrote it, with the server time of that write so a client can count down from it.
/// </summary>
public struct StatusEntry : INetworkSerializable, IEquatable<StatusEntry>
{
    private int instanceId;
    private ushort catalogIndex;
    private byte stacks;
    private float remaining;
    private double writtenAt;

    public StatusEntry(int instanceId, ushort catalogIndex, byte stacks, float remaining, double writtenAt)
    {
        this.instanceId = instanceId;
        this.catalogIndex = catalogIndex;
        this.stacks = stacks;
        this.remaining = remaining;
        this.writtenAt = writtenAt;
    }

    public int InstanceId => instanceId;
    public ushort CatalogIndex => catalogIndex;
    public byte Stacks => stacks;
    public float Remaining => remaining;
    public double WrittenAt => writtenAt;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref instanceId);
        serializer.SerializeValue(ref catalogIndex);
        serializer.SerializeValue(ref stacks);
        serializer.SerializeValue(ref remaining);
        serializer.SerializeValue(ref writtenAt);
    }

    public bool Equals(StatusEntry other)
    {
        return instanceId == other.instanceId
            && catalogIndex == other.catalogIndex
            && stacks == other.stacks
            && remaining.Equals(other.remaining)
            && writtenAt.Equals(other.writtenAt);
    }

    public override bool Equals(object obj)
    {
        return obj is StatusEntry other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(instanceId, catalogIndex, stacks, remaining, writtenAt);
    }
}
