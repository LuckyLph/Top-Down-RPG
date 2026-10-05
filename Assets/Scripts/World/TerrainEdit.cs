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

    /// <summary>
    /// Creates an edit of <paramref name="cell"/> on layer <paramref name="layer"/>. The layer is stored as a byte and
    /// the cell's z is dropped.
    /// </summary>
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

    /// <summary>
    /// Reads or writes the layer, cell coordinates and filled flag for Netcode.
    /// </summary>
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref layer);
        serializer.SerializeValue(ref x);
        serializer.SerializeValue(ref y);
        serializer.SerializeValue(ref filled);
    }

    /// <summary>
    /// Value equality on layer, cell and filled flag, as <see cref="NetworkList{T}"/> requires.
    /// </summary>
    public bool Equals(TerrainEdit other)
    {
        return layer == other.layer && x == other.x && y == other.y && filled == other.filled;
    }

    /// <summary>
    /// True when <paramref name="obj"/> is a <see cref="TerrainEdit"/> with the same layer, cell and filled flag.
    /// </summary>
    public override bool Equals(object obj)
    {
        return obj is TerrainEdit other && Equals(other);
    }

    /// <summary>
    /// Hash of the layer, cell and filled flag, consistent with <see cref="Equals(TerrainEdit)"/>.
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(layer, x, y, filled);
    }
}
