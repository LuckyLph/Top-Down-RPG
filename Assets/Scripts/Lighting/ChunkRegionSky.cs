using System;
using VContainer.Unity;

/// <summary>
/// A chunk scope's entry point that hands the chunk's <see cref="ChunkSky"/> to the Gameplay scope's
/// <see cref="RegionSkies"/> once the scene is up, so the day and night blends its region skies around the local player
/// (Docs/Lighting.md, Phase 8), and takes it back when the chunk unloads and its scope is disposed.
/// </summary>
public sealed class ChunkRegionSky : IStartable, IDisposable
{
    private readonly RegionSkies skies;
    private readonly ChunkSky sky;
    private readonly ChunkIdentity identity;
    private bool added;

    /// <summary>
    /// Hands <paramref name="chunkSky"/> of the chunk <paramref name="chunk"/> to <paramref name="regionSkies"/>.
    /// </summary>
    public ChunkRegionSky(RegionSkies regionSkies, ChunkSky chunkSky, ChunkIdentity chunk)
    {
        skies = regionSkies;
        sky = chunkSky;
        identity = chunk;
    }

    /// <summary>
    /// Adds the chunk's sky.
    /// </summary>
    public void Start()
    {
        skies.Add(identity.World, identity.Coords, sky);
        added = true;
    }

    /// <summary>
    /// Takes it back.
    /// </summary>
    public void Dispose()
    {
        if (added)
        {
            skies.Remove(identity.World, identity.Coords, sky);
            added = false;
        }
    }
}
