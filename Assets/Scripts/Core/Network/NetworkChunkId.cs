using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A <see cref="ChunkId"/> on the wire: the world id as a fixed string and the chunk's coordinates. It travels in the
/// chunk readiness messages and as the instantiation data of every object spawned into a chunk, so a client creates
/// its copy in its home chunk's scope.
/// </summary>
public struct NetworkChunkId : INetworkSerializable
{
    private FixedString64Bytes worldId;
    private int x;
    private int y;

    /// <summary>
    /// The wire form of <paramref name="chunk"/>. A world id longer than the fixed string holds is cut, which no asset
    /// GUID is.
    /// </summary>
    public NetworkChunkId(ChunkId chunk)
    {
        worldId = default;
        string id = chunk.WorldId ?? string.Empty;
        if (id.Length > 0)
        {
            worldId = new FixedString64Bytes(id.Length <= FixedString64Bytes.UTF8MaxLengthInBytes ? id : id.Substring(0, FixedString64Bytes.UTF8MaxLengthInBytes));
        }

        x = chunk.Coords.x;
        y = chunk.Coords.y;
    }

    /// <summary>
    /// Whether it names a world; the default value, which a spawn without instantiation data gives, does not.
    /// </summary>
    public bool IsValid => worldId.Length > 0;

    /// <summary>
    /// The chunk it names.
    /// </summary>
    public ChunkId ToChunkId()
    {
        return new ChunkId(worldId.ToString(), new Vector2Int(x, y));
    }

    /// <summary>
    /// Reads or writes the world id and the coordinates for Netcode.
    /// </summary>
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref worldId);
        serializer.SerializeValue(ref x);
        serializer.SerializeValue(ref y);
    }
}
