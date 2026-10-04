using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// One cell of a runtime terrain edit: which <see cref="AreaTerrain"/> layer, which cell, and whether it was filled or
/// emptied. <see cref="NetworkAreaTerrain"/> replicates these to clients.
/// </summary>
public struct TerrainEdit : INetworkSerializable, IEquatable<TerrainEdit>
{
    private byte layer;
    private int x;
    private int y;
    private bool filled;

    public TerrainEdit(int layer, Vector3Int cell, bool filled)
    {
        this.layer = (byte)layer;
        x = cell.x;
        y = cell.y;
        this.filled = filled;
    }

    public int Layer => layer;
    public Vector3Int Cell => new(x, y, 0);
    public bool Filled => filled;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref layer);
        serializer.SerializeValue(ref x);
        serializer.SerializeValue(ref y);
        serializer.SerializeValue(ref filled);
    }

    public bool Equals(TerrainEdit other)
    {
        return layer == other.layer && x == other.x && y == other.y && filled == other.filled;
    }

    public override bool Equals(object obj)
    {
        return obj is TerrainEdit other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(layer, x, y, filled);
    }
}
