using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Gameplay-scope entry point that lights the loaded worlds by the time of day (Docs/Lighting.md, Phase 3): each
/// outdoor world's global light takes the <see cref="DayClock"/>'s sky and sun together (until sun shadows take the sun
/// away where they fall, Phase 4), an indoor one keeps its own (<see cref="WorldLighting"/>). Each frame it reads the
/// clock's light and writes it to the worlds only once it has moved by a step too small to see, and to a world as it
/// loads. Presentation each machine draws on its own.
/// </summary>
public sealed class DayNightPresenter : IStartable, ITickable, IDisposable
{
    private const float Step = 1f / 512f;

    private readonly DayClock clock;
    private readonly Worlds worlds;
    private Color shown;
    private bool applied;

    /// <summary>
    /// Creates the presenter of <paramref name="dayClock"/> over <paramref name="loadedWorlds"/>.
    /// </summary>
    public DayNightPresenter(DayClock dayClock, Worlds loadedWorlds)
    {
        clock = dayClock;
        worlds = loadedWorlds;
    }

    /// <summary>
    /// The light last written to the worlds.
    /// </summary>
    public Color Shown => shown;

    /// <summary>
    /// Follows the worlds loading and lights the ones loaded.
    /// </summary>
    public void Start()
    {
        worlds.WorldLoaded += HandleWorldLoaded;
        Tick();
    }

    /// <summary>
    /// Stops following the worlds.
    /// </summary>
    public void Dispose()
    {
        worlds.WorldLoaded -= HandleWorldLoaded;
    }

    /// <summary>
    /// Writes the clock's light to every loaded world once it has moved by a step from the light last written.
    /// </summary>
    public void Tick()
    {
        Color light = clock.Light.Total;
        if (applied && Mathf.Abs(light.r - shown.r) < Step && Mathf.Abs(light.g - shown.g) < Step && Mathf.Abs(light.b - shown.b) < Step)
        {
            return;
        }

        shown = light;
        applied = true;
        IReadOnlyList<LoadedWorld> loaded = worlds.Loaded;
        for (int i = 0; i < loaded.Count; i++)
        {
            Apply(loaded[i]);
        }
    }

    /// <summary>
    /// Lights a world as it loads with the light last written.
    /// </summary>
    private void HandleWorldLoaded(LoadedWorld world)
    {
        if (applied)
        {
            Apply(world);
        }
    }

    /// <summary>
    /// Writes the light last written to <paramref name="world"/>'s lighting, when it has one.
    /// </summary>
    private void Apply(LoadedWorld world)
    {
        if (world.Lighting != null)
        {
            world.Lighting.ApplySky(shown);
        }
    }
}
