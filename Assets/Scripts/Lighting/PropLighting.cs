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
/// the share has moved by a step too small to see or a flicker step has passed, allocating nothing. Presentation each
/// machine draws on its own: nothing is sent, and nothing in the game reads it.
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
    /// Writes every light once the night's lights' share has moved by a step or a flicker step has passed, and the
    /// flicker's step to the shaders.
    /// </summary>
    public void Tick()
    {
        float share = dayClock != null ? dayClock.Light.NightLights : 1f;
        int step = dayClock != null ? Mathf.FloorToInt(clock.Time * dayClock.Settings.FlickerSteps) : 0;
        if (applied && Mathf.Abs(share - shown) < Step && step == shownStep)
        {
            return;
        }

        bool stepped = !applied || step != shownStep;
        shown = share;
        shownStep = step;
        applied = true;
        if (stepped)
        {
            float glowShare = dayClock != null ? dayClock.Settings.GlowFlickerShare : 0f;
            Shader.SetGlobalVector(GlowFlickerId, new Vector4(step, glowShare, 0f, 0f));
        }

        for (int i = 0; i < groups.Count; i++)
        {
            Write(groups[i], share, step, false);
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
    /// Writes <paramref name="group"/>'s lights at <paramref name="share"/>, at full indoors, at flicker step
    /// <paramref name="step"/>: on and at their strength while it is above none, off otherwise. Lights already off stay
    /// untouched unless <paramref name="force"/>.
    /// </summary>
    private static void Write(Group group, float share, int step, bool force)
    {
        float level = group.Indoors ? 1f : share;
        bool on = level > 0f;
        if (!on && !group.On && !force)
        {
            return;
        }

        group.On = on;
        for (int i = 0; i < group.Lights.Length; i++)
        {
            Light2D light = group.Lights[i];
            if (light == null)
            {
                continue;
            }

            light.enabled = on;
            if (on)
            {
                light.intensity = StrengthAt(group.Full[i], group.Flicker[i], level, step, group.Seeds[i]);
            }
        }

        if (group.Ground == null)
        {
            return;
        }

        Light2D glow = group.Ground.GlowLight;
        if (glow != null)
        {
            glow.enabled = on;
            glow.intensity = group.Ground.GlowStrength * level;
        }
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
    /// ground, whether it lies indoors and whether its lights were last left on.
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
        }

        public IReadOnlyList<PropLight> Source { get; }
        public TerrainBlendRenderer Ground { get; }
        public bool Indoors { get; }
        public Light2D[] Lights { get; }
        public float[] Full { get; }
        public float[] Flicker { get; }
        public int[] Seeds { get; }
        public bool On { get; set; }
    }
}
