using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools.Constraints;
using UnityEngine.Tilemaps;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for the lights that are not spells (Docs/Lighting.md, Phase 5): <see cref="PropLighting"/> burning a chunk's
/// prop lights by the night lights' share of the time of day, off by day, flickering by its steps from the clock within
/// each light's share and the same at the same step, at full in an indoor world, the glow of a renderer's ground with
/// them, handing the flicker's step to the shaders, letting go of a chunk's lights when it unloads, allocating nothing
/// while it flickers, and the lighting dev panel's overrides forcing the lights and the glow on or off, stilling the
/// flicker and turning the glowing pixels off; a moving flame's frame at each step (Phase 6); and the burning lights that
/// cast shadows waking the props' casters within their reach in any loaded chunk and letting them sleep as they go out or
/// their chunk leaves, a caster held by two lights sleeping only once both are out, and the lights' shadows kept off
/// (Phase 7).
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
        lighting.Add(lights, null, null, null);
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
        lighting.Add(new[] { light }, null, null, null);
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
        lighting.Add(new[] { light }, null, null, indoorWorld);
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
        lighting.Add(lights, null, null, null);
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
    /// A moving flame (Docs/Lighting.md, Phase 6) shows the frame its place's seed starts it at, then the next one at each
    /// flicker step, round its loop; flames standing elsewhere start on frames of their own; and the step goes to the
    /// shaders by day too, so flames move while their lights are off.
    /// </summary>
    [Test]
    public void AMovingFlame_StepsThroughItsFrames_FromWhereItsPlaceStartsIt_ByDayToo()
    {
        Vector2 place = new(12.5f, 7.03125f);
        int start = PropLighting.SeedOf(place) % 6;
        for (int step = 0; step < 13; step++)
        {
            Assert.That(PropLighting.FlameFrameAt(place, step, 6), Is.EqualTo((start + step) % 6), $"step {step}");
        }

        HashSet<int> starts = new();
        for (int x = 0; x < 12; x++)
        {
            starts.Add(PropLighting.FlameFrameAt(new Vector2(x + 0.5f, 3.03125f), 0, 6));
        }

        Assert.That(starts.Count, Is.GreaterThan(3), "Flames side by side start on frames of their own.");
        PropLighting lighting = new(day, time, new Worlds());
        time.Advance(10f);
        lighting.Start();
        Assert.That(day.Light.NightLights, Is.EqualTo(0f), "It is day.");
        Assert.That(Shader.GetGlobalVector(GlowFlickerId).y, Is.GreaterThan(0f), "The flames move by day.");
        lighting.Dispose();
    }

    /// <summary>
    /// Forced on, the prop lights and the ground's glow burn at full by day; forced off, they are out at night; with the
    /// flicker kept off a flickering light burns steady at its strength and the shaders show no flickering pixel; with
    /// the glowing pixels off the shaders draw none glowing; following the day again, each is as the day has it.
    /// </summary>
    [Test]
    public void TheOverrides_ForceTheLightsAndTheGlow_StillTheFlicker_AndTurnTheGlowingPixelsOff()
    {
        PropLight light = CreateLight(new Vector2(2f, 3f), 1.2f, 0.2f);
        TerrainBlendRenderer ground = CreateGlowingGround(1.3f);
        PropLighting lighting = new(day, time, new Worlds());
        lighting.Add(new[] { light }, null, ground, null);
        time.Advance(10f);
        lighting.Start();
        Assert.That(light.LightSource.enabled, Is.False, "Off by day.");
        Assert.That(ground.GlowLight.enabled, Is.False);

        lighting.PropLightsOverride = LightingOverride.On;
        lighting.Tick();
        Assert.That(light.LightSource.enabled, Is.True, "Forced on, the lights burn by day.");
        Assert.That(light.LightSource.intensity, Is.InRange(0.96f, 1.44f), "At full, within its flicker.");
        Assert.That(ground.GlowLight.enabled, Is.False, "The glow follows the day on its own.");
        lighting.GroundGlowOverride = LightingOverride.On;
        lighting.Tick();
        Assert.That(ground.GlowLight.enabled, Is.True, "Forced on, the glow burns by day.");
        Assert.That(ground.GlowLight.intensity, Is.EqualTo(1.3f).Within(1e-5f));

        lighting.FlickerOverride = LightingOverride.Off;
        for (int i = 0; i < 4; i++)
        {
            time.Advance(0.125f);
            lighting.Tick();
            Assert.That(light.LightSource.intensity, Is.EqualTo(1.2f).Within(1e-5f), "Without flicker it burns steady at its strength.");
        }

        Assert.That(Shader.GetGlobalVector(GlowFlickerId).y, Is.EqualTo(0f), "No pixel flickers.");
        lighting.FlickerOverride = LightingOverride.Follow;
        lighting.Tick();
        Assert.That(Shader.GetGlobalVector(GlowFlickerId).y, Is.EqualTo(0.33f));

        lighting.GlowingPixelsOverride = LightingOverride.Off;
        lighting.Tick();
        Assert.That(Shader.GetGlobalVector(GlowFlickerId).z, Is.EqualTo(1f), "The shaders draw no pixel glowing.");
        lighting.GlowingPixelsOverride = LightingOverride.Follow;
        lighting.Tick();
        Assert.That(Shader.GetGlobalVector(GlowFlickerId).z, Is.EqualTo(0f));

        time.Advance(92f);
        lighting.PropLightsOverride = LightingOverride.Off;
        lighting.GroundGlowOverride = LightingOverride.Off;
        lighting.Tick();
        Assert.That(lighting.Shown, Is.EqualTo(1f).Within(1e-5f));
        Assert.That(light.LightSource.enabled, Is.False, "Forced off, the lights are out at night.");
        Assert.That(ground.GlowLight.enabled, Is.False, "Forced off, the glow is out at night.");

        lighting.PropLightsOverride = LightingOverride.Follow;
        lighting.GroundGlowOverride = LightingOverride.Follow;
        lighting.Tick();
        Assert.That(light.LightSource.enabled, Is.True, "Following the day, they burn at night.");
        Assert.That(ground.GlowLight.enabled, Is.True);
        Assert.That(ground.GlowLight.intensity, Is.EqualTo(1.3f).Within(1e-5f));
        lighting.Dispose();
    }

    /// <summary>
    /// By day every prop's caster sleeps; at night a burning light that casts wakes the casters within its radius of
    /// where it stands, those of its own chunk and of a chunk added later, and none farther away nor any near a light
    /// that casts nothing; at sunrise they all sleep again.
    /// </summary>
    [Test]
    public void ABurningLight_WakesTheCastersWithinItsReach_InAnyChunk_AndTheyAllSleepAtSunrise()
    {
        PropLight casting = CreateLight(Vector2.zero, 1f, 0f, 4f);
        PropLight plain = CreateLight(new Vector2(12f, 0f), 1f, 0f, 4f);
        plain.LightSource.shadowsEnabled = false;
        ShadowCaster2D near = CreateCaster(new Vector2(2.5f, 0.5f));
        ShadowCaster2D far = CreateCaster(new Vector2(9f, 0f));
        ShadowCaster2D byPlain = CreateCaster(new Vector2(12.5f, 1f));
        ShadowCaster2D neighbour = CreateCaster(new Vector2(-3f, -1f));
        PropLighting lighting = new(day, time, new Worlds());
        lighting.Add(new[] { casting, plain }, new[] { near, far, byPlain }, null, null);
        lighting.Start();
        Assert.That(lighting.AwakeCasters, Is.Zero, "Every caster sleeps by day.");
        Assert.That(near.enabled, Is.False);

        time.Advance(102f);
        lighting.Tick();
        Assert.That(near.enabled, Is.True, "A burning light wakes a caster within its reach.");
        Assert.That(far.enabled, Is.False, "One beyond its radius sleeps.");
        Assert.That(byPlain.enabled, Is.False, "A light that casts nothing wakes none.");
        Assert.That(lighting.AwakeCasters, Is.EqualTo(1));

        PropLight[] none = new PropLight[0];
        lighting.Add(none, new[] { neighbour }, null, null);
        Assert.That(neighbour.enabled, Is.True, "A chunk added at night has its casters within a burning light's reach woken.");
        Assert.That(lighting.AwakeCasters, Is.EqualTo(2));

        time.Advance(25f);
        lighting.Tick();
        Assert.That(near.enabled || neighbour.enabled, Is.False, "At sunrise the casters sleep again.");
        Assert.That(lighting.AwakeCasters, Is.Zero);
        lighting.Dispose();
    }

    /// <summary>
    /// A caster within the reach of two burning lights of two chunks stays awake while either burns: it sleeps only once
    /// both are gone; and a chunk taken back takes its casters out of every other light's reach, asleep.
    /// </summary>
    [Test]
    public void ACasterHeldByTwoLights_SleepsOnlyOnceBothAreGone_AndAChunksCastersLeaveWithIt()
    {
        PropLight first = CreateLight(Vector2.zero, 1f, 0f, 4f);
        PropLight second = CreateLight(new Vector2(4f, 0f), 1f, 0f, 4f);
        ShadowCaster2D between = CreateCaster(new Vector2(2f, 0f));
        ShadowCaster2D besideSecond = CreateCaster(new Vector2(1f, 2.5f));
        PropLight[] firstChunk = { first };
        PropLight[] secondChunk = { second };
        PropLighting lighting = new(day, time, new Worlds());
        time.Advance(102f);
        lighting.Start();
        lighting.Add(firstChunk, new[] { between }, null, null);
        lighting.Add(secondChunk, new[] { besideSecond }, null, null);
        Assert.That(between.enabled && besideSecond.enabled, Is.True);
        Assert.That(lighting.AwakeCasters, Is.EqualTo(2));

        lighting.Remove(secondChunk);
        Assert.That(between.enabled, Is.True, "The first light still holds it.");
        Assert.That(besideSecond.enabled, Is.False, "A chunk's casters leave with it, asleep.");
        Assert.That(lighting.AwakeCasters, Is.EqualTo(1));

        lighting.Remove(firstChunk);
        Assert.That(between.enabled, Is.False);
        Assert.That(lighting.AwakeCasters, Is.Zero);
        lighting.Dispose();
    }

    /// <summary>
    /// With the lights' shadows kept off (the lighting dev panel) the lights that cast cast nothing and wake no caster;
    /// following again, they cast and wake theirs, and a light that never cast is left casting nothing.
    /// </summary>
    [Test]
    public void TheLightShadowsOverride_KeepsTheLightsFromCasting_AndTheirCastersAsleep()
    {
        PropLight casting = CreateLight(Vector2.zero, 1f, 0f, 4f);
        PropLight plain = CreateLight(new Vector2(20f, 0f), 1f, 0f, 4f);
        plain.LightSource.shadowsEnabled = false;
        ShadowCaster2D near = CreateCaster(new Vector2(1.5f, 0f));
        PropLighting lighting = new(day, time, new Worlds());
        lighting.Add(new[] { casting, plain }, new[] { near }, null, null);
        time.Advance(102f);
        lighting.Start();
        Assert.That(near.enabled, Is.True);

        lighting.LightShadowsOverride = LightingOverride.Off;
        lighting.Tick();
        Assert.That(casting.LightSource.shadowsEnabled, Is.False, "Kept off, the light casts nothing.");
        Assert.That(near.enabled, Is.False, "And wakes no caster.");
        Assert.That(lighting.AwakeCasters, Is.Zero);

        lighting.LightShadowsOverride = LightingOverride.Follow;
        lighting.Tick();
        Assert.That(casting.LightSource.shadowsEnabled, Is.True);
        Assert.That(plain.LightSource.shadowsEnabled, Is.False, "A light that never cast stays so.");
        Assert.That(near.enabled, Is.True);
        lighting.Dispose();
    }

    /// <summary>
    /// Burning and flickering a chunk's lights through a night, its casters awake, allocates nothing.
    /// </summary>
    [Test]
    public void Flickering_AllocatesNothing()
    {
        PropLight[] lights = new PropLight[8];
        ShadowCaster2D[] casters = new ShadowCaster2D[8];
        for (int i = 0; i < lights.Length; i++)
        {
            lights[i] = CreateLight(new Vector2(i, 0f), 1f, 0.15f);
            casters[i] = CreateCaster(new Vector2(i + 0.5f, 1f));
        }

        PropLighting lighting = new(day, time, new Worlds());
        lighting.Add(lights, casters, null, null);
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

        Assert.That(lighting.AwakeCasters, Is.EqualTo(8));
        Assert.That(() => Flicker(), Is.Not.AllocatingGCMemory(), "Flickering allocates nothing.");
        lighting.Dispose();
    }

    /// <summary>
    /// A prop light standing at <paramref name="position"/> burning at <paramref name="strength"/> and flickering by
    /// <paramref name="flicker"/>, of <paramref name="radius"/>, casting shadows, its Light 2D off as the prop writer
    /// leaves it.
    /// </summary>
    private PropLight CreateLight(Vector2 position, float strength, float flicker, float radius = 1f)
    {
        GameObject lightObject = Track(new GameObject("Light"));
        lightObject.transform.position = position;
        Light2D light = lightObject.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Point;
        light.pointLightOuterRadius = radius;
        light.shadowsEnabled = true;
        light.enabled = false;
        PropLight prop = lightObject.AddComponent<PropLight>();
        prop.Configure(light, strength, flicker);
        return prop;
    }

    /// <summary>
    /// A prop's shadow caster, off as the prop writer leaves it, shaped as a box of one unit round
    /// <paramref name="position"/>.
    /// </summary>
    private ShadowCaster2D CreateCaster(Vector2 position)
    {
        GameObject casterObject = Track(new GameObject("Caster"));
        casterObject.transform.position = position;
        ShadowCaster2D caster = casterObject.AddComponent<ShadowCaster2D>();
        caster.enabled = false;
        return caster;
    }

    /// <summary>
    /// A built renderer of a grid holding ground over 16 x 8 cells and a 2 x 2 pool of a material glowing at
    /// <paramref name="strength"/>, so it has a glow light.
    /// </summary>
    private TerrainBlendRenderer CreateGlowingGround(float strength)
    {
        GameObject root = Track(new GameObject("GlowingGround", typeof(Grid)));
        Tile tile = Track(ScriptableObject.CreateInstance<Tile>());
        Tilemap[] layers = new Tilemap[2];
        for (int i = 0; i < layers.Length; i++)
        {
            GameObject layerObject = new($"Layer{i}", typeof(Tilemap));
            layerObject.transform.SetParent(root.transform);
            layers[i] = layerObject.GetComponent<Tilemap>();
        }

        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                layers[0].SetTile(new Vector3Int(x, y, 0), tile);
                if (x >= 6 && x < 8 && y >= 3 && y < 5)
                {
                    layers[1].SetTile(new Vector3Int(x, y, 0), tile);
                }
            }
        }

        TerrainStyle style = TestTerrainStyle.Create(created);
        TerrainMaterial plain = TestTerrainStyle.PlainMaterial("Ground", TerrainMaterial.CellMode.None, created);
        TerrainMaterial lava = TestTerrainStyle.PlainMaterial("Lava", TerrainMaterial.CellMode.None, created);
        lava.SetGlow(strength, 2.5f, new Color(1f, 0.5f, 0.2f, 1f));
        root.SetActive(false);
        TerrainBlendRenderer renderer = root.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(style, new TerrainBlendRenderer.Layer(layers[0], plain), new TerrainBlendRenderer.Layer(layers[1], lava));
        root.SetActive(true);
        Assert.That(renderer.GlowLight, Is.Not.Null, "The renderer glows.");
        return renderer;
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
