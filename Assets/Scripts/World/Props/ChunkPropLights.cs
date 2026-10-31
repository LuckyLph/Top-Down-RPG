using System;
using VContainer.Unity;

/// <summary>
/// A chunk scope's entry point that hands the chunk's prop lights and the glow of its ground to
/// <see cref="PropLighting"/> once the scene is up, so they burn by the time of day, and takes them back when the chunk
/// unloads and its scope is disposed.
/// </summary>
public sealed class ChunkPropLights : IStartable, IDisposable
{
    private readonly PropLighting lighting;
    private readonly PropLight[] lights;
    private readonly TerrainBlendRenderer ground;
    private readonly ChunkIdentity identity;
    private bool added;

    /// <summary>
    /// Hands <paramref name="chunkLights"/> and the glow of <paramref name="chunkGround"/> (none when null) of the
    /// chunk <paramref name="chunk"/> to <paramref name="propLighting"/>.
    /// </summary>
    public ChunkPropLights(PropLighting propLighting, PropLight[] chunkLights, TerrainBlendRenderer chunkGround, ChunkIdentity chunk)
    {
        lighting = propLighting;
        lights = chunkLights;
        ground = chunkGround;
        identity = chunk;
    }

    /// <summary>
    /// Adds the chunk's lights.
    /// </summary>
    public void Start()
    {
        lighting.Add(lights, ground, identity.World);
        added = true;
    }

    /// <summary>
    /// Takes them back.
    /// </summary>
    public void Dispose()
    {
        if (added)
        {
            lighting.Remove(lights);
            added = false;
        }
    }
}
