using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for the lights that are not spells (Docs/Lighting.md, Phase 5): <see cref="PropLighting"/> burning a chunk's
/// prop lights by the night lights' share of the time of day, off by day, flickering by its steps from the clock within
/// each light's share and the same at the same step, at full in an indoor world, the glow of a renderer's ground with
/// them, handing the flicker's step to the shaders, letting go of a chunk's lights when it unloads, and allocating
/// nothing while it flickers.
/// </summary>
public class PropLightingTests
{
    private static readonly int GlowFlickerId = Shader.PropertyToID("_GlowFlicker");

    private readonly List<Object> created = new();
    private ManualClock time;
    private DayClock day;

    /// <summary>
    /// A day of 100 s and a night of 25 s starting at sunrise, the night lights off at sunrise and noon and full from
    /// sunset to before sunrise, flickering 8 times a second with a third of the flickering pixels shown.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        time = new ManualClock();
        SessionSave session = new(time);
        session.StartNew();
        DayNightSettings settings = Track(DayNightSettings.Create(100f, 25f, 0f, new[]
        {
            new DayLightKey(0f, Color.grey, Color.grey, 0f),
            new DayLightKey(0.4f, Color.grey, Color.grey, 0f),
            new DayLightKey(0.8f, Color.grey, Color.black, 1f),
            new DayLightKey(0.96f, Color.grey, Color.black, 1f)
        }, flicker: 8f, glowFlicker: 0.33f));
        day = new DayClock(settings, session, FixedGameAuthority.Authoritative, new FakeNetworkClock());
    }

    /// <summary>
    /// Destroys every object the test made and takes the flicker away from the shaders.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        Shader.SetGlobalVector(GlowFlickerId, Vector4.zero);
        foreach (Object made in created)
        {
            if (made != null)
            {
                Object.DestroyImmediate(made);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// By day a chunk's prop lights are off; at night they burn at their strength within their flicker share, each
    /// step to a strength of its own that the same step always gives, and they go off again at sunrise.
    /// </summary>
    [Test]
    public void PropLights_AreOffByDay_AndBurnAndFlickerAtNight()
    {
        PropLight steady = CreateLight(new Vector2(3f, 4f), 1.2f, 0f);
        PropLight flickering = CreateLight(new Vector2(8.25f, 1.5f), 1f, 0.2f);
        PropLight[] lights = { steady, flickering };
        PropLighting lighting = new(day, time, new Worlds());
        lighting.Add(lights, null, null);
        lighting.Start();
        Assert.That(steady.LightSource.enabled, Is.False, "Off at sunrise.");
        Assert.That(flickering.LightSource.enabled, Is.False);

        time.Advance(102f);
        lighting.Tick();
        Assert.That(lighting.Shown, Is.EqualTo(1f).Within(1e-5f));
        Assert.That(steady.LightSource.enabled, Is.True, "On at night.");
        Assert.That(steady.LightSource.intensity, Is.EqualTo(1.2f).Within(1e-5f), "A light that does not flicker burns at its strength.");
        Assert.That(flickering.LightSource.enabled, Is.True);
        float first = flickering.LightSource.intensity;
        Assert.That(first, Is.InRange(0.8f, 1.2f), "A flickering light stays within its share.");

        HashSet<float> strengths = new() { first };
        for (int i = 0; i < 16; i++)
        {
            time.Advance(0.125f);
            lighting.Tick();
            strengths.Add(flickering.LightSource.intensity);
            Assert.That(flickering.LightSource.intensity, Is.InRange(0.8f, 1.2f));
            Assert.That(steady.LightSource.intensity, Is.EqualTo(1.2f).Within(1e-5f));
        }

        Assert.That(strengths.Count, Is.GreaterThan(4), "It flickers from step to step.");
        int step = Mathf.FloorToInt(time.Time * 8f);
        Assert.That(flickering.LightSource.intensity, Is.EqualTo(PropLighting.StrengthAt(1f, 0.2f, 1f, step, PropLighting.SeedOf(flickering.transform.position))).Within(1e-6f), "The same step always gives the same strength.");

        time.Advance(25f);
        lighting.Tick();
        Assert.That(steady.LightSource.enabled, Is.False, "Off again past sunrise.");
        Assert.That(flickering.LightSource.enabled, Is.False);
        lighting.Dispose();
    }

    /// <summary>
    /// Between the keys the lights burn at the night lights' share: half way from noon to sunset, at half strength.
    /// </summary>
    [Test]
    public void PropLights_BurnAtTheNightLightsShare()
    {
        PropLight light = CreateLight(Vector2.zero, 1.2f, 0f);
        PropLighting lighting = new(day, time, new Worlds());
        lighting.Add(new[] { light }, null, null);
        time.Advance(75f);
        lighting.Start();
        Assert.That(light.LightSource.enabled, Is.True);
        Assert.That(light.LightSource.intensity, Is.EqualTo(0.6f).Within(1e-4f));
        lighting.Dispose();
    }

    /// <summary>
    /// A chunk of an indoor world burns its lights at full whatever the time of day.
    /// </summary>
    [Test]
    public void PropLights_BurnAtFullIndoors()
    {
        WorldDefinition indoorWorld = Track(ScriptableObject.CreateInstance<WorldDefinition>());
        GameObject lightingObject = Track(new GameObject("WorldLighting"));
        lightingObject.SetActive(false);
        WorldLighting worldLighting = lightingObject.AddComponent<WorldLighting>();
        worldLighting.Configure(WorldLightingProfile.Indoor);
        Worlds worlds = new();
        worlds.Add(new LoadedWorld(indoorWorld, default, null, worldLighting));
        PropLight light = CreateLight(Vector2.zero, 0.9f, 0f);
        PropLighting lighting = new(day, time, worlds);
        lighting.Start();
        lighting.Add(new[] { light }, null, indoorWorld);
        Assert.That(light.LightSource.enabled, Is.True, "Indoors the lights burn by day.");
        Assert.That(light.LightSource.intensity, Is.EqualTo(0.9f).Within(1e-5f));
        lighting.Dispose();
    }

    /// <summary>
    /// A chunk added at night burns at once, and once taken back its lights are no longer written.
    /// </summary>
    [Test]
    public void AChunksLights_BurnAsItIsAdded_AndAreLeftAloneOnceRemoved()
    {
        PropLighting lighting = new(day, time, new Worlds());
        time.Advance(105f);
        lighting.Start();
        PropLight light = CreateLight(Vector2.zero, 1f, 0f);
        PropLight[] lights = { light };
        lighting.Add(lights, null, null);
        Assert.That(lighting.GroupCount, Is.EqualTo(1));
        Assert.That(light.LightSource.enabled, Is.True, "Added at night, it burns at once.");

        lighting.Remove(lights);
        Assert.That(lighting.GroupCount, Is.EqualTo(0));
        time.Advance(25f);
        lighting.Tick();
        Assert.That(light.LightSource.enabled, Is.True, "A removed chunk's lights are left as they were.");
        lighting.Dispose();
    }

    /// <summary>
    /// The flicker's step and the glowing pixels' share go to the shaders at each step, and are taken away when it
    /// stops.
    /// </summary>
    [Test]
    public void TheFlickersStep_GoesToTheShaders()
    {
        PropLighting lighting = new(day, time, new Worlds());
        time.Advance(10f);
        lighting.Start();
        Assert.That(Shader.GetGlobalVector(GlowFlickerId), Is.EqualTo(new Vector4(80f, 0.33f, 0f, 0f)));
        time.Advance(0.125f);
        lighting.Tick();
        Assert.That(Shader.GetGlobalVector(GlowFlickerId).x, Is.EqualTo(81f));
        lighting.Dispose();
        Assert.That(Shader.GetGlobalVector(GlowFlickerId), Is.EqualTo(Vector4.zero));
    }

    /// <summary>
    /// Burning and flickering a chunk's lights through a night allocates nothing.
    /// </summary>
    [Test]
    public void Flickering_AllocatesNothing()
    {
        PropLight[] lights = new PropLight[8];
        for (int i = 0; i < lights.Length; i++)
        {
            lights[i] = CreateLight(new Vector2(i, 0f), 1f, 0.15f);
        }

        PropLighting lighting = new(day, time, new Worlds());
        lighting.Add(lights, null, null);
        time.Advance(102f);
        lighting.Start();
        for (int i = 0; i < 8; i++)
        {
            time.Advance(0.125f);
            lighting.Tick();
        }

        void Flicker()
        {
            for (int i = 0; i < 64; i++)
            {
                time.Advance(0.125f);
                lighting.Tick();
            }
        }

        Assert.That(() => Flicker(), Is.Not.AllocatingGCMemory(), "Flickering allocates nothing.");
        lighting.Dispose();
    }

    /// <summary>
    /// A prop light standing at <paramref name="position"/> burning at <paramref name="strength"/> and flickering by
    /// <paramref name="flicker"/>, its Light 2D off as the prop writer leaves it.
    /// </summary>
    private PropLight CreateLight(Vector2 position, float strength, float flicker)
    {
        GameObject lightObject = Track(new GameObject("Light"));
        lightObject.transform.position = position;
        Light2D light = lightObject.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Point;
        light.enabled = false;
        PropLight prop = lightObject.AddComponent<PropLight>();
        prop.Configure(light, strength, flicker);
        return prop;
    }

    /// <summary>
    /// Remembers <paramref name="instance"/> for destruction after the test.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }

    /// <summary>
    /// A network clock the test sets.
    /// </summary>
    private sealed class FakeNetworkClock : INetworkClock
    {
        public double ServerTime { get; set; }
    }
}
