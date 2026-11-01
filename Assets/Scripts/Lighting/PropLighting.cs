using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using VContainer.Unity;

/// <summary>
/// Gameplay-scope entry point that burns the lights that are not spells by the time of day (Docs/Lighting.md, Phase 5):
/// the loaded chunks' prop lights (<see cref="PropLight"/>) and the glow of their ground
/// (<see cref="TerrainBlendRenderer.GlowLight"/>), which each chunk hands it as it loads and takes back as it unloads.
/// Each shines at its full strength times the <see cref="DayClock"/>'s night lights' share, and at full in an indoor
/// world; at none it is off. A prop light's strength steps by up to its flicker share either way at each of the
/// settings' flicker steps, by a hash of the step and where it stands, from the clock. It also hands the flicker's step
/// to the shaders (<c>_GlowFlicker</c>), which show glowing pixels in their flicker colours by it. It writes only when
/// the share has moved by a step too small to see or a flicker step has passed, allocating nothing. The lighting dev
/// panel may force the prop lights and the ground's glow on or off, still the flicker and turn the glowing pixels off,
/// on this machine alone. Presentation each machine draws on its own: nothing is sent, and nothing in the game reads it.
/// </summary>
public sealed class PropLighting : IStartable, ITickable, IDisposable
{
    private const float Step = 1f / 512f;

    private static readonly int GlowFlickerId = Shader.PropertyToID("_GlowFlicker");

    private readonly DayClock dayClock;
    private readonly IClock clock;
    private readonly Worlds worlds;
    private readonly List<Group> groups = new();
    private float shown;
    private int shownStep;
    private bool applied;
    private bool forced;
    private LightingOverride propLights;
    private LightingOverride groundGlow;
    private LightingOverride flicker;
    private LightingOverride glowingPixels;

    /// <summary>
    /// Creates the lighting of the night's lights by <paramref name="day"/>, flickering on <paramref name="time"/>,
    /// over <paramref name="loadedWorlds"/>; without a day clock they burn at full and do not flicker.
    /// </summary>
    public PropLighting(DayClock day, IClock time, Worlds loadedWorlds)
    {
        dayClock = day;
        clock = time;
        worlds = loadedWorlds;
    }

    /// <summary>
    /// The night's lights' share last written.
    /// </summary>
    public float Shown => shown;

    /// <summary>
    /// How many chunks' lights it burns.
    /// </summary>
    public int GroupCount => groups.Count;

    /// <summary>
    /// Whether the prop lights burn by the time of day, at full or not at all (the lighting dev panel).
    /// </summary>
    internal LightingOverride PropLightsOverride
    {
        get => propLights;
        set => Force(ref propLights, value);
    }

    /// <summary>
    /// Whether the ground's glow burns by the time of day, at full or not at all (the lighting dev panel).
    /// </summary>
    internal LightingOverride GroundGlowOverride
    {
        get => groundGlow;
        set => Force(ref groundGlow, value);
    }

    /// <summary>
    /// Whether the prop lights' strength and the glowing pixels flicker, or, off, stand still (the lighting dev panel).
    /// </summary>
    internal LightingOverride FlickerOverride
    {
        get => flicker;
        set => Force(ref flicker, value);
    }

    /// <summary>
    /// Whether glowing pixels glow, or, off, draw lit like any other (the lighting dev panel).
    /// </summary>
    internal LightingOverride GlowingPixelsOverride
    {
        get => glowingPixels;
        set => Force(ref glowingPixels, value);
    }

    /// <summary>
    /// Writes the lights for the first time.
    /// </summary>
    public void Start()
    {
        Tick();
    }

    /// <summary>
    /// Takes the flicker away from the shaders.
    /// </summary>
    public void Dispose()
    {
        groups.Clear();
        Shader.SetGlobalVector(GlowFlickerId, Vector4.zero);
    }

    /// <summary>
    /// Adds a chunk's <paramref name="lights"/> and the glow of its <paramref name="ground"/> (none when null), of
    /// <paramref name="world"/>, and burns them at once.
    /// </summary>
    public void Add(IReadOnlyList<PropLight> lights, TerrainBlendRenderer ground, WorldDefinition world)
    {
        Group group = new(lights, ground, IsIndoors(world));
        groups.Add(group);
        if (applied)
        {
            Write(group, shown, shownStep, true);
        }
    }

    /// <summary>
    /// Takes back the chunk's lights added with <paramref name="lights"/>.
    /// </summary>
    public void Remove(IReadOnlyList<PropLight> lights)
    {
        for (int i = groups.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(groups[i].Source, lights))
            {
                groups.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Writes every light once the night's lights' share has moved by a step, a flicker step has passed or an override
    /// changed, and the flicker's step to the shaders.
    /// </summary>
    public void Tick()
    {
        float share = dayClock != null ? dayClock.Light.NightLights : 1f;
        bool flickers = dayClock != null && flicker != LightingOverride.Off;
        int step = flickers ? Mathf.FloorToInt(clock.Time * dayClock.Settings.FlickerSteps) : 0;
        if (applied && !forced && Mathf.Abs(share - shown) < Step && step == shownStep)
        {
            return;
        }

        bool stepped = !applied || forced || step != shownStep;
        bool force = forced;
        shown = share;
        shownStep = step;
        applied = true;
        forced = false;
        if (stepped)
        {
            float glowShare = flickers ? dayClock.Settings.GlowFlickerShare : 0f;
            Shader.SetGlobalVector(GlowFlickerId, new Vector4(step, glowShare, glowingPixels == LightingOverride.Off ? 1f : 0f, 0f));
        }

        for (int i = 0; i < groups.Count; i++)
        {
            Write(groups[i], share, step, force);
        }
    }

    /// <summary>
    /// The strength a light of <paramref name="full"/> strength flickering by <paramref name="flicker"/> shines at,
    /// at <paramref name="share"/> of it, at flicker step <paramref name="step"/> for the light hashed to
    /// <paramref name="seed"/>.
    /// </summary>
    public static float StrengthAt(float full, float flicker, float share, int step, int seed)
    {
        return full * share * (1f + flicker * (2f * Hash(step, seed) - 1f));
    }

    /// <summary>
    /// The seed a light standing at <paramref name="position"/> flickers by: a hash of the art pixel it stands on.
    /// </summary>
    public static int SeedOf(Vector2 position)
    {
        return Mathf.FloorToInt(Hash(Mathf.RoundToInt(position.x * TerrainStyle.PixelsPerCell), Mathf.RoundToInt(position.y * TerrainStyle.PixelsPerCell)) * 16777216f);
    }

    /// <summary>
    /// Writes <paramref name="group"/>'s lights and its ground's glow at <paramref name="share"/>, at full indoors, or as
    /// their overrides force them, the lights at flicker step <paramref name="step"/>: on and at their strength while
    /// their level is above none, off otherwise. Lights already off stay untouched unless <paramref name="force"/>.
    /// </summary>
    private void Write(Group group, float share, int step, bool force)
    {
        float level = group.Indoors ? 1f : share;
        float lightLevel = Level(propLights, level);
        bool lightsOn = lightLevel > 0f;
        if (lightsOn || group.On || force)
        {
            group.On = lightsOn;
            bool flickers = flicker != LightingOverride.Off;
            for (int i = 0; i < group.Lights.Length; i++)
            {
                Light2D light = group.Lights[i];
                if (light == null)
                {
                    continue;
                }

                light.enabled = lightsOn;
                if (lightsOn)
                {
                    light.intensity = StrengthAt(group.Full[i], flickers ? group.Flicker[i] : 0f, lightLevel, step, group.Seeds[i]);
                }
            }
        }

        float glowLevel = Level(groundGlow, level);
        bool glowOn = glowLevel > 0f;
        if (group.Ground == null || (!glowOn && !group.GlowOn && !force))
        {
            return;
        }

        group.GlowOn = glowOn;
        Light2D glow = group.Ground.GlowLight;
        if (glow != null)
        {
            glow.enabled = glowOn;
            glow.intensity = group.Ground.GlowStrength * glowLevel;
        }
    }

    /// <summary>
    /// The level a light burns at under <paramref name="mode"/>: <paramref name="level"/> following the time of day,
    /// full forced on, none forced off.
    /// </summary>
    private static float Level(LightingOverride mode, float level)
    {
        return mode switch
        {
            LightingOverride.On => 1f,
            LightingOverride.Off => 0f,
            _ => level
        };
    }

    /// <summary>
    /// Sets <paramref name="field"/> to <paramref name="value"/>, writing every light at the next update when it changed.
    /// </summary>
    private void Force(ref LightingOverride field, LightingOverride value)
    {
        forced |= field != value;
        field = value;
    }

    /// <summary>
    /// Whether <paramref name="world"/> is loaded and lit indoors.
    /// </summary>
    private bool IsIndoors(WorldDefinition world)
    {
        return world != null && worlds != null && worlds.TryGet(world, out LoadedWorld loaded) && loaded.Lighting != null
            && loaded.Lighting.Profile == WorldLightingProfile.Indoor;
    }

    /// <summary>
    /// A value in [0, 1) for (<paramref name="a"/>, <paramref name="b"/>).
    /// </summary>
    private static float Hash(int a, int b)
    {
        unchecked
        {
            uint h = (uint)a * 374761393u + (uint)b * 668265263u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    /// <summary>
    /// One chunk's lights: the list it was added with, each light with its full strength, flicker and seed, its
    /// ground, whether it lies indoors and whether its lights and its ground's glow were last left on.
    /// </summary>
    private sealed class Group
    {
        public Group(IReadOnlyList<PropLight> lights, TerrainBlendRenderer ground, bool indoors)
        {
            Source = lights;
            Ground = ground;
            Indoors = indoors;
            int count = lights != null ? lights.Count : 0;
            Lights = new Light2D[count];
            Full = new float[count];
            Flicker = new float[count];
            Seeds = new int[count];
            for (int i = 0; i < count; i++)
            {
                PropLight prop = lights[i];
                if (prop == null)
                {
                    continue;
                }

                Lights[i] = prop.LightSource;
                Full[i] = prop.Intensity;
                Flicker[i] = prop.Flicker;
                Seeds[i] = SeedOf(prop.transform.position);
            }

            On = true;
            GlowOn = true;
        }

        public IReadOnlyList<PropLight> Source { get; }
        public TerrainBlendRenderer Ground { get; }
        public bool Indoors { get; }
        public Light2D[] Lights { get; }
        public float[] Full { get; }
        public float[] Flicker { get; }
        public int[] Seeds { get; }
        public bool On { get; set; }
        public bool GlowOn { get; set; }
    }
}
