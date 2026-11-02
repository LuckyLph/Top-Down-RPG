using System;
using System.Collections.Generic;
using UnityEngine.Rendering.Universal;
using VContainer.Unity;

/// <summary>
/// A chunk scope's entry point that hands the chunk's prop lights, its props' shadow casters and the glow of its ground
/// to <see cref="PropLighting"/> once the scene is up, so they burn by the time of day and the burning lights wake the
/// casters near them (Docs/Lighting.md, Phases 5 and 7), and takes them back when the chunk unloads and its scope is
/// disposed.
/// </summary>
public sealed class ChunkPropLights : IStartable, IDisposable
{
    private readonly PropLighting lighting;
    private readonly PropLight[] lights;
    private readonly ShadowCaster2D[] casters;
    private readonly TerrainBlendRenderer ground;
    private readonly ChunkIdentity identity;
    private bool added;

    /// <summary>
    /// Hands <paramref name="chunkLights"/>, <paramref name="chunkCasters"/> and the glow of
    /// <paramref name="chunkGround"/> (none when null) of the chunk <paramref name="chunk"/> to
    /// <paramref name="propLighting"/>.
    /// </summary>
    public ChunkPropLights(PropLighting propLighting, PropLight[] chunkLights, ShadowCaster2D[] chunkCasters, TerrainBlendRenderer chunkGround, ChunkIdentity chunk)
    {
        lighting = propLighting;
        lights = chunkLights;
        casters = chunkCasters;
        ground = chunkGround;
        identity = chunk;
    }

    /// <summary>
    /// The shadow casters the prop writer gave <paramref name="props"/>, on their <c>Visuals</c>.
    /// </summary>
    public static ShadowCaster2D[] CastersOf(IReadOnlyList<PropFade> props)
    {
        List<ShadowCaster2D> found = new();
        for (int i = 0; i < props.Count; i++)
        {
            if (props[i] != null && props[i].Visuals != null && props[i].Visuals.TryGetComponent(out ShadowCaster2D caster))
            {
                found.Add(caster);
            }
        }

        return found.ToArray();
    }

    /// <summary>
    /// Adds the chunk's lights and casters.
    /// </summary>
    public void Start()
    {
        lighting.Add(lights, casters, ground, identity.World);
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
