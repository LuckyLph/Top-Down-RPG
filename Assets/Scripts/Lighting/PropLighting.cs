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
/// to the shaders (<c>_GlowFlicker</c>), which show glowing pixels in their flicker colours by it and step moving flames
/// through their frames (Phase 6, <see cref="FlameFrameAt"/>), by day too. Walls and props stop the lights that cast
/// (Phase 7): each chunk also hands over its props' Shadow Caster 2Ds, written off, and while a casting light burns it
/// keeps awake the casters within its reach in any loaded chunk, letting them sleep again as it goes out, so a caster
/// runs only near a burning light. It writes only when the share has moved by a step too small to see or a flicker step
/// has passed, allocating nothing. The lighting dev panel may force the prop lights and the ground's glow on or off,
/// still the flicker, turn the glowing pixels off and keep the lights' shadows off, on this machine alone.
/// Presentation each machine draws on its own: nothing is sent, and nothing in the game reads it.
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
    private int awakeCasters;
    private LightingOverride propLights;
    private LightingOverride groundGlow;
    private LightingOverride flicker;
    private LightingOverride glowingPixels;
    private LightingOverride lightShadows;

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
    /// How many props' shadow casters burning lights keep awake.
    /// </summary>
    public int AwakeCasters => awakeCasters;

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
    /// Whether the prop lights that cast shadows cast them, or, off, cast none and wake no caster (the lighting dev
    /// panel).
    /// </summary>
    internal LightingOverride LightShadowsOverride
    {
        get => lightShadows;
        set => Force(ref lightShadows, value);
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
    /// Adds a chunk's <paramref name="lights"/>, its props' shadow <paramref name="casters"/> (none when null) and the
    /// glow of its <paramref name="ground"/> (none when null), of <paramref name="world"/>, and burns them at once. Its
    /// lights that cast reach the casters of every loaded chunk within their radius, and its casters are reached by the
    /// lights of every loaded chunk, those burning waking them at once.
    /// </summary>
    public void Add(IReadOnlyList<PropLight> lights, IReadOnlyList<ShadowCaster2D> casters, TerrainBlendRenderer ground, WorldDefinition world)
    {
        Group group = new(lights, casters, ground, IsIndoors(world));
        for (int i = 0; i < groups.Count; i++)
        {
            Reach(group, groups[i]);
            Reach(groups[i], group);
        }

        Reach(group, group);
        groups.Add(group);
        if (applied)
        {
            Write(group, shown, shownStep, true);
        }
    }

    /// <summary>
    /// Takes back the chunk's lights and casters added with <paramref name="lights"/>: its lights let the casters they
    /// held sleep, and its casters go to sleep and out of the reach of every other chunk's lights.
    /// </summary>
    public void Remove(IReadOnlyList<PropLight> lights)
    {
        for (int i = groups.Count - 1; i >= 0; i--)
        {
            Group group = groups[i];
            if (!ReferenceEquals(group.Source, lights))
            {
                continue;
            }

            if (group.Awake)
            {
                SetAwake(group, false);
            }

            for (int c = 0; c < group.Casters.Length; c++)
            {
                if (group.Wakes[c] > 0)
                {
                    group.Wakes[c] = 0;
                    Sleep(group.Casters[c]);
                }
            }

            groups.RemoveAt(i);
            for (int g = 0; g < groups.Count; g++)
            {
                Unreach(groups[g], group);
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
    /// The frame a moving flame of <paramref name="frames"/> frames standing at <paramref name="position"/> shows at
    /// flicker step <paramref name="step"/> (Docs/Lighting.md, Phase 6), as the sprite shader picks it: the step on from
    /// the frame its place's seed starts it at, so flames standing side by side move out of step.
    /// </summary>
    public static int FlameFrameAt(Vector2 position, int step, int frames)
    {
        return (int)(((uint)step + (uint)SeedOf(position)) % (uint)frames);
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
    /// their level is above none, off otherwise, the casters within the reach of those that cast awake while they burn
    /// and cast. Lights already off stay untouched unless <paramref name="force"/>.
    /// </summary>
    private void Write(Group group, float share, int step, bool force)
    {
        float level = group.Indoors ? 1f : share;
        float lightLevel = Level(propLights, level);
        bool lightsOn = lightLevel > 0f;
        bool casting = lightShadows != LightingOverride.Off;
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

                if (force && group.Casts[i])
                {
                    light.shadowsEnabled = casting;
                }
            }
        }

        if (group.Awake != (lightsOn && casting))
        {
            SetAwake(group, lightsOn && casting);
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
    /// Wakes, when <paramref name="awake"/>, or lets sleep the casters within the reach of <paramref name="group"/>'s
    /// lights that cast: a caster is awake while at least one burning light holds it.
    /// </summary>
    private void SetAwake(Group group, bool awake)
    {
        group.Awake = awake;
        for (int i = 0; i < group.Reach.Length; i++)
        {
            List<CasterReach> reach = group.Reach[i];
            for (int r = 0; reach != null && r < reach.Count; r++)
            {
                Hold(reach[r], awake);
            }
        }
    }

    /// <summary>
    /// Adds a burning light's hold on the caster <paramref name="reach"/> names when <paramref name="hold"/>, or takes
    /// one away, waking it at the first hold and letting it sleep at the last.
    /// </summary>
    private void Hold(CasterReach reach, bool hold)
    {
        int[] wakes = reach.Group.Wakes;
        wakes[reach.Index] += hold ? 1 : -1;
        if (hold && wakes[reach.Index] == 1)
        {
            awakeCasters++;
            ShadowCaster2D caster = reach.Group.Casters[reach.Index];
            if (caster != null)
            {
                caster.enabled = true;
            }
        }
        else if (!hold && wakes[reach.Index] == 0)
        {
            Sleep(reach.Group.Casters[reach.Index]);
        }
    }

    /// <summary>
    /// Lets <paramref name="caster"/>, held by no burning light any more, sleep.
    /// </summary>
    private void Sleep(ShadowCaster2D caster)
    {
        awakeCasters--;
        if (caster != null)
        {
            caster.enabled = false;
        }
    }

    /// <summary>
    /// Adds to the reach of <paramref name="lighting"/>'s lights that cast the casters of <paramref name="lit"/> within
    /// each one's radius of where it stands, holding them at once while those lights are awake.
    /// </summary>
    private void Reach(Group lighting, Group lit)
    {
        for (int i = 0; i < lighting.Lights.Length; i++)
        {
            if (!lighting.Casts[i])
            {
                continue;
            }

            for (int c = 0; c < lit.Casters.Length; c++)
            {
                if (lit.Casters[c] == null || !Within(lit.Bounds[c], lighting.Positions[i], lighting.Radii[i]))
                {
                    continue;
                }

                CasterReach reach = new(lit, c);
                lighting.Reach[i].Add(reach);
                if (lighting.Awake)
                {
                    Hold(reach, true);
                }
            }
        }
    }

    /// <summary>
    /// Takes the casters of <paramref name="gone"/> out of the reach of <paramref name="lighting"/>'s lights.
    /// </summary>
    private static void Unreach(Group lighting, Group gone)
    {
        for (int i = 0; i < lighting.Reach.Length; i++)
        {
            List<CasterReach> reach = lighting.Reach[i];
            for (int r = reach != null ? reach.Count - 1 : -1; r >= 0; r--)
            {
                if (reach[r].Group == gone)
                {
                    reach.RemoveAt(r);
                }
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="bounds"/> come within <paramref name="radius"/> of <paramref name="point"/>.
    /// </summary>
    private static bool Within(Rect bounds, Vector2 point, float radius)
    {
        float dx = Mathf.Max(bounds.xMin - point.x, 0f, point.x - bounds.xMax);
        float dy = Mathf.Max(bounds.yMin - point.y, 0f, point.y - bounds.yMax);
        return dx * dx + dy * dy <= radius * radius;
    }

    /// <summary>
    /// The box a caster's shape covers in the world.
    /// </summary>
    private static Rect BoundsOf(ShadowCaster2D caster)
    {
        Vector3[] path = caster.shapePath;
        Transform at = caster.transform;
        if (path == null || path.Length == 0)
        {
            return new Rect(at.position, Vector2.zero);
        }

        Vector2 min = at.TransformPoint(path[0]);
        Vector2 max = min;
        for (int i = 1; i < path.Length; i++)
        {
            Vector2 point = at.TransformPoint(path[i]);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
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
    /// One chunk's lights and casters: the list it was added with, each light with its full strength, flicker, seed,
    /// whether it casts shadows, where it stands, its radius and the casters within its reach, each caster with the box
    /// its shape covers and how many burning lights hold it awake, its ground, whether it lies indoors and whether its
    /// lights and its ground's glow were last left on and its lights last held their casters awake.
    /// </summary>
    private sealed class Group
    {
        public Group(IReadOnlyList<PropLight> lights, IReadOnlyList<ShadowCaster2D> casters, TerrainBlendRenderer ground, bool indoors)
        {
            Source = lights;
            Ground = ground;
            Indoors = indoors;
            int count = lights != null ? lights.Count : 0;
            Lights = new Light2D[count];
            Full = new float[count];
            Flicker = new float[count];
            Seeds = new int[count];
            Casts = new bool[count];
            Positions = new Vector2[count];
            Radii = new float[count];
            Reach = new List<CasterReach>[count];
            for (int i = 0; i < count; i++)
            {
                PropLight prop = lights[i];
                if (prop == null || prop.LightSource == null)
                {
                    continue;
                }

                Lights[i] = prop.LightSource;
                Full[i] = prop.Intensity;
                Flicker[i] = prop.Flicker;
                Seeds[i] = SeedOf(prop.transform.position);
                Casts[i] = prop.LightSource.shadowsEnabled;
                Positions[i] = prop.LightSource.transform.position;
                Radii[i] = prop.LightSource.pointLightOuterRadius;
                Reach[i] = new List<CasterReach>();
            }

            int casterCount = casters != null ? casters.Count : 0;
            Casters = new ShadowCaster2D[casterCount];
            Bounds = new Rect[casterCount];
            Wakes = new int[casterCount];
            for (int c = 0; c < casterCount; c++)
            {
                Casters[c] = casters[c];
                Bounds[c] = casters[c] != null ? BoundsOf(casters[c]) : default;
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
        public bool[] Casts { get; }
        public Vector2[] Positions { get; }
        public float[] Radii { get; }
        public List<CasterReach>[] Reach { get; }
        public ShadowCaster2D[] Casters { get; }
        public Rect[] Bounds { get; }
        public int[] Wakes { get; }
        public bool On { get; set; }
        public bool GlowOn { get; set; }
        public bool Awake { get; set; }
    }

    /// <summary>
    /// A caster a light reaches: the group of its chunk and its index there.
    /// </summary>
    private readonly struct CasterReach
    {
        public CasterReach(Group group, int index)
        {
            Group = group;
            Index = index;
        }

        public Group Group { get; }
        public int Index { get; }
    }
}
