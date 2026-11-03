using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Gameplay-scope entry point that lights the loaded worlds by the time of day (Docs/Lighting.md, Phases 3, 4 and 8):
/// each outdoor world's global light takes the <see cref="DayClock"/>'s sky and sun together, the light where no sun
/// shadow falls, and an indoor one keeps its own (<see cref="WorldLighting"/>); the sun's light and shadow vector go to
/// the shaders (<see cref="SunShadowGlobals"/>), which take the sun away where its shadows fall, while the world shown is
/// outdoors and the sun is up, unless the lighting dev panel keeps them off; and while it casts, the clouds' shadows
/// drift with the world clock (<see cref="SunClouds"/>). The sky and the sun take the tint of the region skies around
/// the local player (<see cref="RegionSkies"/>), and the clouds their cover. Water's reflections (Phase 10) take from the
/// shaders the light of the world shown, the sky every other light adds to, and how strongly water reflects: fully at
/// night and indoors, by day the settings' <see cref="DayNightSettings.DayReflection"/>, between the two by the night
/// lights' share, unless the lighting dev panel keeps them off or at full. Relief under the lights (Phase 11) takes the
/// same light of the world shown, and whether it is drawn, unless the lighting dev panel keeps it off. Each frame it
/// reads the clock and writes the light to the worlds and the sun, the reflections and the relief to the shaders only
/// once they have moved by a step too small to see, the clouds every frame, and the light to a world as it loads.
/// Presentation each machine draws on its own.
/// </summary>
public sealed class DayNightPresenter : IStartable, ITickable, IDisposable
{
    private const float Step = 1f / 512f;
    private const float ShadowStep = 1f / 1024f;
    internal static readonly int ReflectionId = Shader.PropertyToID("_TerrainReflection");
    internal static readonly int ReliefId = Shader.PropertyToID("_LightRelief");

    private readonly DayClock clock;
    private readonly Worlds worlds;
    private readonly RegionSkies regionSkies;
    private readonly LocalPlayerTracker localPlayer;
    private readonly SunClouds clouds;
    private Color shown;
    private bool applied;
    private Color shownSun;
    private Vector2 shownShadow;
    private bool casting;
    private bool sunApplied;
    private bool cloudsCleared;
    private LightingOverride sunShadows;
    private LightingOverride cloudsOverride;
    private LightingOverride reflections;
    private Vector4 shownReflection;
    private bool reflectionApplied;
    private LightingOverride relief;
    private Vector4 shownRelief;
    private bool reliefApplied;

    /// <summary>
    /// Creates the presenter of <paramref name="dayClock"/> over <paramref name="loadedWorlds"/>, tinting the sky by the
    /// <paramref name="skies"/> around <paramref name="player"/> (none: the day's own sky).
    /// </summary>
    public DayNightPresenter(DayClock dayClock, Worlds loadedWorlds, RegionSkies skies = null, LocalPlayerTracker player = null)
    {
        clock = dayClock;
        worlds = loadedWorlds;
        regionSkies = skies;
        localPlayer = player;
        clouds = new SunClouds(dayClock.Settings);
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
    /// The region skies blended at the local player last frame.
    /// </summary>
    public RegionSkyBlend Sky { get; private set; } = RegionSkyBlend.None;

    /// <summary>
    /// The share of the ground under cloud last written, 0 while none is drawn.
    /// </summary>
    public float CloudCover { get; private set; }

    /// <summary>
    /// The clouds the presenter draws.
    /// </summary>
    public SunClouds Clouds => clouds;

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
    /// Whether the clouds come and go with the world clock, cover the most the day and the region allow, or are kept off
    /// on this machine (the lighting dev panel). They only ever shade the ground while the sun casts.
    /// </summary>
    internal LightingOverride CloudsOverride
    {
        get => cloudsOverride;
        set => cloudsOverride = value;
    }

    /// <summary>
    /// Whether water's reflections follow the day, reflect as at night, or are kept off on this machine (the lighting
    /// dev panel).
    /// </summary>
    internal LightingOverride ReflectionsOverride
    {
        get => reflections;
        set => reflections = value;
    }

    /// <summary>
    /// Whether relief under the lights is drawn or kept off on this machine (the lighting dev panel); on, it is drawn
    /// as when it follows.
    /// </summary>
    internal LightingOverride ReliefOverride
    {
        get => relief;
        set => relief = value;
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
    /// Stops following the worlds and takes the sun, the clouds, water's reflections and the relief away from the
    /// shaders.
    /// </summary>
    public void Dispose()
    {
        worlds.WorldLoaded -= HandleWorldLoaded;
        SunShadowGlobals.Set(Color.black, Vector2.zero, false);
        SunShadowGlobals.SetClouds(Vector2.zero, 0f, 0f, 0f, 0f);
        Shader.SetGlobalVector(ReflectionId, Vector4.zero);
        Shader.SetGlobalVector(ReliefId, Vector4.zero);
    }

    /// <summary>
    /// Writes the clock's light, tinted by the region skies at the local player, to every loaded world once it has
    /// moved by a step from the light last written, the sun to the shaders once it has moved by a step or started or
    /// stopped casting, and the clouds while it casts.
    /// </summary>
    public void Tick()
    {
        DayLight day = clock.Light;
        Sky = LocalSky(out bool outdoors, out WorldLighting shownLighting);
        Color tint = Sky.Tint;
        ApplySun(day, clock.Sun, tint, outdoors);
        ApplyClouds();
        Color light = Tinted(day.Total, tint);
        Color sky = outdoors ? light : shownLighting != null ? shownLighting.GlobalLight : Color.black;
        ApplyReflection(day, sky, outdoors);
        ApplyRelief(sky);
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
    /// Writes the sun of <paramref name="day"/> and <paramref name="sun"/>, tinted by <paramref name="tint"/>, to the
    /// shaders, casting while it is up over an outdoor world shown and its shadows are not kept off, once it has moved
    /// by a step from the sun last written.
    /// </summary>
    private void ApplySun(DayLight day, SunState sun, Color tint, bool outdoors)
    {
        bool casts = sun.IsUp && sunShadows != LightingOverride.Off && outdoors;
        Color colour = casts ? Tinted(day.Sun, tint) : Color.black;
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
    /// Writes the clouds to the shaders while the sun casts: drifted by the world clock, covering the day's cover at
    /// the world clock (the most with the clouds forced on) times the region skies' cover, taking the settings' shade of
    /// the sun away. Clears them once when the sun stops casting, the clouds are kept off or none would show.
    /// </summary>
    private void ApplyClouds()
    {
        DayNightSettings settings = clock.Settings;
        double time = clock.WorldTime;
        float day = cloudsOverride == LightingOverride.On ? settings.CloudCover : clouds.CoverAt(time);
        float cover = casting && cloudsOverride != LightingOverride.Off ? Mathf.Clamp01(day * Sky.CloudCover) : 0f;
        if (cover <= 0f || settings.CloudShade <= 0f)
        {
            CloudCover = 0f;
            if (!cloudsCleared)
            {
                SunShadowGlobals.SetClouds(Vector2.zero, 0f, 0f, 0f, 0f);
                cloudsCleared = true;
            }

            return;
        }

        CloudCover = cover;
        cloudsCleared = false;
        SunShadowGlobals.SetClouds(clouds.DriftAt(time), clouds.LevelFor(cover), settings.CloudShade, settings.CloudSize, settings.CloudEdge);
    }

    /// <summary>
    /// Writes water's reflections to the shaders once they have moved by a step: <paramref name="sky"/>, the light of the
    /// world shown, and how strongly water reflects, by the night lights' share of <paramref name="day"/> outdoors and
    /// fully indoors, or as the lighting dev panel forces.
    /// </summary>
    private void ApplyReflection(DayLight day, Color sky, bool outdoors)
    {
        float strength = reflections switch
        {
            LightingOverride.Off => 0f,
            LightingOverride.On => 1f,
            _ => outdoors ? Mathf.Lerp(clock.Settings.DayReflection, 1f, day.NightLights) : 1f
        };
        Vector4 reflection = new(sky.r, sky.g, sky.b, strength);
        if (reflectionApplied && Mathf.Abs(reflection.x - shownReflection.x) < Step && Mathf.Abs(reflection.y - shownReflection.y) < Step
            && Mathf.Abs(reflection.z - shownReflection.z) < Step && Mathf.Abs(reflection.w - shownReflection.w) < Step)
        {
            return;
        }

        reflectionApplied = true;
        shownReflection = reflection;
        Shader.SetGlobalVector(ReflectionId, reflection);
    }

    /// <summary>
    /// Writes the relief under the lights to the shaders once it has moved by a step: <paramref name="sky"/>, the light
    /// of the world shown, and 1 while it is drawn, 0 while the lighting dev panel keeps it off.
    /// </summary>
    private void ApplyRelief(Color sky)
    {
        Vector4 value = new(sky.r, sky.g, sky.b, relief == LightingOverride.Off ? 0f : 1f);
        if (reliefApplied && Mathf.Abs(value.x - shownRelief.x) < Step && Mathf.Abs(value.y - shownRelief.y) < Step
            && Mathf.Abs(value.z - shownRelief.z) < Step && value.w == shownRelief.w)
        {
            return;
        }

        reliefApplied = true;
        shownRelief = value;
        Shader.SetGlobalVector(ReliefId, value);
    }

    /// <summary>
    /// The region skies blended at the local player in the world shown on screen (<see cref="WorldLighting.IsShown"/>),
    /// the day's own without skies, a local player or a world shown; whether that world is lit outdoors, and its
    /// lighting (none without a world shown).
    /// </summary>
    private RegionSkyBlend LocalSky(out bool outdoors, out WorldLighting shown)
    {
        outdoors = false;
        shown = null;
        IReadOnlyList<LoadedWorld> loaded = worlds.Loaded;
        for (int i = 0; i < loaded.Count; i++)
        {
            WorldLighting lighting = loaded[i].Lighting;
            if (lighting == null || !lighting.IsShown)
            {
                continue;
            }

            shown = lighting;
            outdoors = lighting.Profile == WorldLightingProfile.Outdoor;
            if (!outdoors || regionSkies == null || localPlayer?.Current == null)
            {
                return RegionSkyBlend.None;
            }

            Transform body = localPlayer.Current.Handle.Transform;
            WorldDefinition world = loaded[i].Definition;
            return body != null && world != null ? regionSkies.At(world, world.ToLocal(body.position)) : RegionSkyBlend.None;
        }

        return RegionSkyBlend.None;
    }

    /// <summary>
    /// <paramref name="colour"/> tinted by <paramref name="tint"/>, opaque.
    /// </summary>
    private static Color Tinted(Color colour, Color tint)
    {
        return new Color(colour.r * tint.r, colour.g * tint.g, colour.b * tint.b, 1f);
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
