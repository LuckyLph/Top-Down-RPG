using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Gameplay-scope entry point that lights the loaded worlds by the time of day (Docs/Lighting.md, Phases 3 and 4): each
/// outdoor world's global light takes the <see cref="DayClock"/>'s sky and sun together, the light where no sun shadow
/// falls, and an indoor one keeps its own (<see cref="WorldLighting"/>); the sun's light and shadow vector go to the
/// shaders (<see cref="SunShadowGlobals"/>), which take the sun away where its shadows fall, while the world shown is
/// outdoors and the sun is up, unless the lighting dev panel keeps them off. Each frame it reads the clock and writes
/// the light to the worlds and the sun to the shaders only once they have moved by a step too small to see, and the
/// light to a world as it loads. Presentation each machine draws on its own.
/// </summary>
public sealed class DayNightPresenter : IStartable, ITickable, IDisposable
{
    private const float Step = 1f / 512f;
    private const float ShadowStep = 1f / 1024f;

    private readonly DayClock clock;
    private readonly Worlds worlds;
    private Color shown;
    private bool applied;
    private Color shownSun;
    private Vector2 shownShadow;
    private bool casting;
    private bool sunApplied;
    private LightingOverride sunShadows;

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
    /// Whether the sun last written to the shaders casts shadows.
    /// </summary>
    public bool Casting => casting;

    /// <summary>
    /// Whether the sun's shadows follow the day or are kept off on this machine (the lighting dev panel). There is
    /// nothing to cast with while the sun is down or the world shown is indoors, so forced on they follow the day.
    /// </summary>
    internal LightingOverride SunShadowsOverride
    {
        get => sunShadows;
        set => sunShadows = value;
    }

    /// <summary>
    /// Follows the worlds loading and lights the ones loaded.
    /// </summary>
    public void Start()
    {
        worlds.WorldLoaded += HandleWorldLoaded;
        Tick();
    }

    /// <summary>
    /// Stops following the worlds and takes the sun away from the shaders.
    /// </summary>
    public void Dispose()
    {
        worlds.WorldLoaded -= HandleWorldLoaded;
        SunShadowGlobals.Set(Color.black, Vector2.zero, false);
    }

    /// <summary>
    /// Writes the clock's light to every loaded world once it has moved by a step from the light last written, and the
    /// sun to the shaders once it has moved by a step or started or stopped casting.
    /// </summary>
    public void Tick()
    {
        DayLight day = clock.Light;
        ApplySun(day, clock.Sun);
        Color light = day.Total;
        if (applied && Near(light, shown))
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
    /// Writes the sun of <paramref name="day"/> and <paramref name="sun"/> to the shaders, casting while it is up over an
    /// outdoor world shown and its shadows are not kept off, once it has moved by a step from the sun last written.
    /// </summary>
    private void ApplySun(DayLight day, SunState sun)
    {
        bool casts = sun.IsUp && sunShadows != LightingOverride.Off && ShowsOutdoorWorld();
        Color colour = casts ? day.Sun : Color.black;
        Vector2 shadow = casts ? sun.Shadow : Vector2.zero;
        if (sunApplied && casts == casting && Near(colour, shownSun) && (shadow - shownShadow).sqrMagnitude < ShadowStep * ShadowStep)
        {
            return;
        }

        sunApplied = true;
        casting = casts;
        shownSun = colour;
        shownShadow = shadow;
        SunShadowGlobals.Set(colour, shadow, casts);
    }

    /// <summary>
    /// Whether the world shown on screen (<see cref="WorldLighting.IsShown"/>) is lit outdoors.
    /// </summary>
    private bool ShowsOutdoorWorld()
    {
        IReadOnlyList<LoadedWorld> loaded = worlds.Loaded;
        for (int i = 0; i < loaded.Count; i++)
        {
            WorldLighting lighting = loaded[i].Lighting;
            if (lighting != null && lighting.IsShown)
            {
                return lighting.Profile == WorldLightingProfile.Outdoor;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="a"/> lies within a step of <paramref name="b"/> in every channel.
    /// </summary>
    private static bool Near(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < Step && Mathf.Abs(a.g - b.g) < Step && Mathf.Abs(a.b - b.b) < Step;
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
