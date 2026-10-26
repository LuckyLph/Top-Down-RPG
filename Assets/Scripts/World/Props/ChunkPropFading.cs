using System;
using VContainer.Unity;

/// <summary>
/// A chunk scope's entry point that hands the chunk's props to <see cref="PropFading"/> once the scene is up, so the
/// tall ones fade while they hide the local player, and takes them out when the chunk unloads and its scope is disposed.
/// </summary>
public sealed class ChunkPropFading : IStartable, IDisposable
{
    private readonly PropFading fading;
    private readonly PropFade[] props;
    private bool added;

    /// <summary>
    /// Hands <paramref name="chunkProps"/> to <paramref name="propFading"/>.
    /// </summary>
    public ChunkPropFading(PropFading propFading, PropFade[] chunkProps)
    {
        fading = propFading;
        props = chunkProps;
    }

    /// <summary>
    /// Adds the chunk's props.
    /// </summary>
    public void Start()
    {
        fading.Add(props);
        added = true;
    }

    /// <summary>
    /// Takes them out again.
    /// </summary>
    public void Dispose()
    {
        if (added)
        {
            fading.Remove(props);
            added = false;
        }
    }
}
