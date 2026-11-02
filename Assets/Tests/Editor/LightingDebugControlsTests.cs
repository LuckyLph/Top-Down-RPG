using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for the lighting dev panel's logic (<see cref="LightingDebugControls"/>, Architecture.md's Dev tools): jumping
/// to a light key and stepping the time of day, holding it, running it faster and following the session again through
/// the day clock's local offset and rate; cycling each override of the presenter (the sun's shadows, the clouds and
/// water's reflections) and the night's lights; teleporting the
/// local player through the entries of its world, round in either direction; the readout naming the time, how it runs
/// and the lighting; and controls without a clock, a presenter or the night's lights doing nothing.
/// </summary>
public class LightingDebugControlsTests
{
    private const float Tolerance = 1e-4f;

    private readonly List<Object> created = new();
    private readonly List<Vector2> teleports = new();
    private ManualClock time;
    private DayClock clock;
    private DayNightPresenter presenter;
    private PropLighting propLighting;
    private WorldDefinition world;
    private LightingDebugControls controls;

    /// <summary>
    /// A day of 100 s and a night of 25 s starting in the morning with the shipped day's ten key times, a presenter and
    /// the night's lights over no worlds, and a world of three entries at origin (2048, 0).
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        time = new ManualClock();
        SessionSave session = new(time);
        session.StartNew();
        float[] keyTimes = { 0f, 0.06f, 0.16f, 0.4f, 0.56f, 0.7f, 0.8f, 0.84f, 0.88f, 0.96f };
        DayLightKey[] keys = new DayLightKey[keyTimes.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            bool day = keyTimes[i] < 0.8f;
            keys[i] = new DayLightKey(keyTimes[i], Color.grey, day ? Color.grey : Color.black, day ? 0f : 1f);
        }

        DayNightSettings settings = Track(DayNightSettings.Create(100f, 25f, 0.16f, keys));
        clock = new DayClock(settings, session, FixedGameAuthority.Authoritative, new FakeNetworkClock());
        Worlds worlds = new();
        presenter = new DayNightPresenter(clock, worlds);
        propLighting = new PropLighting(clock, time, worlds);
        world = Track(WorldDefinition.Create("Showcase", new Vector2Int(2048, 0), Vector2Int.one, new[] { Vector2Int.zero },
            new WorldDefinition.Entry("start", new Vector2(30.5f, 42f), true),
            new WorldDefinition.Entry("campsite", new Vector2(23.5f, 30.5f), false),
            new WorldDefinition.Entry("lava", new Vector2(99.5f, 19.5f), false)));
        controls = new LightingDebugControls(clock, presenter, propLighting, () => world, position => teleports.Add(position));
    }

    /// <summary>
    /// Takes the sun and the flicker away from the shaders and destroys every object the test made.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        presenter.Dispose();
        propLighting.Dispose();
        foreach (Object made in created)
        {
            if (made != null)
            {
                Object.DestroyImmediate(made);
            }
        }

        created.Clear();
        teleports.Clear();
    }

    /// <summary>
    /// Each light key moves this machine's time of day to the key's time, named after the shipped day's keys, leaving the
    /// session's alone; there is no key past the last.
    /// </summary>
    [Test]
    public void ShowKey_MovesThisMachinesTimeOfDayToTheKey()
    {
        Assert.That(controls.ShowKey(4), Is.True);
        Assert.That(clock.Phase, Is.EqualTo(0.56f).Within(Tolerance));
        Assert.That(clock.SessionPhase, Is.EqualTo(0.16f).Within(Tolerance), "The session's time of day stays.");
        Assert.That(controls.KeyName(4), Is.EqualTo("afternoon"));
        Assert.That(controls.ShowKey(8), Is.True);
        Assert.That(clock.Phase, Is.EqualTo(0.88f).Within(Tolerance));
        Assert.That(controls.KeyName(8), Is.EqualTo("night"));
        Assert.That(controls.ShowKey(10), Is.False);
        Assert.That(controls.ShowKey(-1), Is.False);
        Assert.That(clock.Phase, Is.EqualTo(0.88f).Within(Tolerance));
    }

    /// <summary>
    /// Steps move the time of day by a hundredth of the cycle, or a thousandth when fine, round the cycle either way.
    /// </summary>
    [Test]
    public void StepTime_MovesByStepsRoundTheCycle()
    {
        controls.ShowKey(0);
        controls.StepTime(3, false);
        Assert.That(clock.Phase, Is.EqualTo(0.03f).Within(Tolerance));
        controls.StepTime(-5, true);
        Assert.That(clock.Phase, Is.EqualTo(0.025f).Within(Tolerance));
        controls.StepTime(-3, false);
        Assert.That(clock.Phase, Is.EqualTo(0.995f).Within(Tolerance), "Stepping back past sunrise wraps to the night before.");
    }

    /// <summary>
    /// Held, the time of day stays while the world clock runs; let go, it runs on at the rate it ran at; the next rate
    /// runs it 10 then 60 times the session's and back, letting a held time run; following the session again drops the
    /// offset and the rate.
    /// </summary>
    [Test]
    public void HoldRateAndFollow_DriveTheLocalRate()
    {
        controls.ShowKey(3);
        Assert.That(controls.RateText(), Is.EqualTo("running, moved from the session"));
        controls.ToggleHold();
        Assert.That(clock.LocalRate, Is.EqualTo(0f));
        time.Advance(30f);
        Assert.That(clock.Phase, Is.EqualTo(0.4f).Within(Tolerance), "Held, the time of day stays.");
        Assert.That(controls.RateText(), Is.EqualTo("held"));

        controls.NextRate();
        Assert.That(clock.LocalRate, Is.EqualTo(10f), "The next rate lets a held time run.");
        Assert.That(clock.Phase, Is.EqualTo(0.4f).Within(Tolerance), "Changing the rate keeps the time shown.");
        time.Advance(2.5f);
        Assert.That(clock.Phase, Is.EqualTo(0.6f).Within(Tolerance), "At 10 times, 2.5 s of a 125 s cycle move it a fifth.");
        Assert.That(controls.RateText(), Is.EqualTo("running x10"));

        controls.ToggleHold();
        time.Advance(10f);
        Assert.That(clock.Phase, Is.EqualTo(0.6f).Within(Tolerance));
        controls.ToggleHold();
        Assert.That(clock.LocalRate, Is.EqualTo(10f), "Let go, it runs at the rate it ran at.");

        controls.NextRate();
        Assert.That(clock.LocalRate, Is.EqualTo(60f));
        controls.NextRate();
        Assert.That(clock.LocalRate, Is.EqualTo(1f));

        controls.FollowSession();
        Assert.That(clock.LocalRate, Is.EqualTo(1f));
        Assert.That(clock.LocalOffset, Is.EqualTo(0f));
        Assert.That(clock.Phase, Is.EqualTo(clock.SessionPhase).Within(Tolerance));
        Assert.That(controls.RateText(), Is.EqualTo("following the session"));
    }

    /// <summary>
    /// The sun's shadows, the flicker, the glowing pixels and the lights' shadows cycle between following the day and off;
    /// the clouds, water's reflections, the prop lights and the ground's glow between following the day, on and off.
    /// </summary>
    [Test]
    public void TheOverrides_CycleThroughTheirStates()
    {
        controls.CycleSunShadows();
        Assert.That(presenter.SunShadowsOverride, Is.EqualTo(LightingOverride.Off));
        controls.CycleSunShadows();
        Assert.That(presenter.SunShadowsOverride, Is.EqualTo(LightingOverride.Follow));

        controls.CycleClouds();
        Assert.That(presenter.CloudsOverride, Is.EqualTo(LightingOverride.On));
        controls.CycleClouds();
        Assert.That(presenter.CloudsOverride, Is.EqualTo(LightingOverride.Off));
        controls.CycleClouds();
        Assert.That(presenter.CloudsOverride, Is.EqualTo(LightingOverride.Follow));

        controls.CycleReflections();
        Assert.That(presenter.ReflectionsOverride, Is.EqualTo(LightingOverride.On));
        controls.CycleReflections();
        Assert.That(presenter.ReflectionsOverride, Is.EqualTo(LightingOverride.Off));
        controls.CycleReflections();
        Assert.That(presenter.ReflectionsOverride, Is.EqualTo(LightingOverride.Follow));

        controls.CyclePropLights();
        Assert.That(propLighting.PropLightsOverride, Is.EqualTo(LightingOverride.On));
        controls.CyclePropLights();
        Assert.That(propLighting.PropLightsOverride, Is.EqualTo(LightingOverride.Off));
        controls.CyclePropLights();
        Assert.That(propLighting.PropLightsOverride, Is.EqualTo(LightingOverride.Follow));

        controls.CycleGroundGlow();
        controls.CycleGroundGlow();
        Assert.That(propLighting.GroundGlowOverride, Is.EqualTo(LightingOverride.Off));

        controls.CycleFlicker();
        Assert.That(propLighting.FlickerOverride, Is.EqualTo(LightingOverride.Off));
        controls.CycleFlicker();
        Assert.That(propLighting.FlickerOverride, Is.EqualTo(LightingOverride.Follow));

        controls.CycleGlowingPixels();
        Assert.That(propLighting.GlowingPixelsOverride, Is.EqualTo(LightingOverride.Off));

        controls.CycleLightShadows();
        Assert.That(propLighting.LightShadowsOverride, Is.EqualTo(LightingOverride.Off));
        controls.CycleLightShadows();
        Assert.That(propLighting.LightShadowsOverride, Is.EqualTo(LightingOverride.Follow));
        Assert.That(LightingDebugControls.StateText(LightingOverride.Off), Is.EqualTo("OFF"));
        Assert.That(LightingDebugControls.StateText(LightingOverride.On), Is.EqualTo("ON"));
        Assert.That(LightingDebugControls.StateText(LightingOverride.Follow), Is.EqualTo("follow"));
    }

    /// <summary>
    /// Teleporting goes to the world's first entry, then on through the others and round, or back the other way, sending
    /// the local player to each entry's place in the world; without a world there is nowhere to go.
    /// </summary>
    [Test]
    public void Teleport_GoesThroughTheEntriesOfTheWorld()
    {
        Assert.That(controls.SpotText(), Is.EqualTo("-"));
        Assert.That(controls.Teleport(1), Is.EqualTo("start"));
        Assert.That(teleports[^1], Is.EqualTo(new Vector2(2078.5f, 42f)));
        Assert.That(controls.Teleport(1), Is.EqualTo("campsite"));
        Assert.That(controls.Teleport(1), Is.EqualTo("lava"));
        Assert.That(teleports[^1], Is.EqualTo(new Vector2(2147.5f, 19.5f)));
        Assert.That(controls.SpotText(), Is.EqualTo("3/3 lava"));
        Assert.That(controls.Teleport(1), Is.EqualTo("start"), "Past the last entry it wraps to the first.");
        Assert.That(controls.Teleport(-1), Is.EqualTo("lava"), "Back from the first it wraps to the last.");
        Assert.That(teleports, Has.Count.EqualTo(5));

        world = null;
        Assert.That(controls.Teleport(1), Is.Null);
        Assert.That(teleports, Has.Count.EqualTo(5));
    }

    /// <summary>
    /// The readout names the time of day by its keys, how it runs, the sun, the sky's and the sun's colours, the region
    /// skies' tint and the clouds' cover, the night lights' share, the chunks the night's lights hold and the casters they keep awake, the spot and the frame rate.
    /// </summary>
    [Test]
    public void TheReadout_DescribesTheLighting()
    {
        controls.ShowKey(8);
        controls.ToggleHold();
        controls.Teleport(2);
        string readout = controls.Readout(59.6f);
        StringAssert.Contains("60 fps", readout);
        StringAssert.Contains("Time 0.880", readout);
        StringAssert.Contains("min after sunrise, night\n", readout);
        StringAssert.Contains("held", readout);
        StringAssert.Contains("Sun down", readout);
        StringAssert.Contains("Sky 0.50 0.50 0.50", readout);
        StringAssert.Contains("Region tint 1.00 1.00 1.00   Clouds 0.00 (region x1.00)", readout);
        StringAssert.Contains("Night lights 1.00", readout);
        StringAssert.Contains("PropLighting holds 0 chunks, 0 casters awake", readout);
        StringAssert.Contains("Spot 2/3 campsite", readout);

        controls.StepTime(3, false);
        StringAssert.Contains("night to before dawn", controls.Readout(60f));
    }

    /// <summary>
    /// Controls without a clock, a presenter, the night's lights or a world do nothing, and say there is no clock.
    /// </summary>
    [Test]
    public void ControlsWithoutTheirParts_DoNothing()
    {
        LightingDebugControls empty = new(null, null, null, null, null);
        Assert.That(empty.ShowKey(0), Is.False);
        Assert.DoesNotThrow(() =>
        {
            empty.StepTime(1, false);
            empty.ToggleHold();
            empty.NextRate();
            empty.FollowSession();
            empty.CycleSunShadows();
            empty.CycleClouds();
            empty.CycleReflections();
            empty.CyclePropLights();
            empty.CycleGroundGlow();
            empty.CycleFlicker();
            empty.CycleGlowingPixels();
            empty.CycleLightShadows();
        });
        Assert.That(empty.Teleport(1), Is.Null);
        StringAssert.Contains("No day clock", empty.Readout(0f));
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
