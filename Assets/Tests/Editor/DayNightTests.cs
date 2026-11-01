using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Tests for the day and night (Docs/Lighting.md, Phase 3): the time of day from the start time and the world clock
/// over the cycle; the light exactly each key's at its time and blended between, round the cycle; the shipped day's
/// lengths, its afternoon today's light exactly and its night the night sky; the sun rising in the east, high at noon,
/// setting in the west and down at night, with shadows long to the lower left and right at either end and short below at
/// noon; the clock following the session's world clock where game state is decided and a client following the host's
/// from when it last heard it; the local offset and the local rate moving, holding and running one machine's time alone
/// (the dev panels); a loaded save resuming its time of day; an outdoor world's global lights taking the sky while an
/// indoor one's keep theirs; the presenter lighting the loaded worlds as the day moves and a world as it loads; the
/// presenter handing the sun to the shaders while it is up over an outdoor world shown (Docs/Lighting.md, Phase 4), and
/// taking it away at night, indoors, when it stops and while the lighting dev panel keeps the shadows off; and
/// the night lights' share blending between the keys like the light, the shipped day's off by day and full at night
/// (Phase 5).
/// </summary>
public class DayNightTests
{
    private const string SettingsPath = "Assets/Data/Lighting/DayNight.asset";
    private static readonly Color Grey85 = new(0.85f, 0.85f, 0.85f, 1f);
    private static readonly int SunColourId = Shader.PropertyToID("_SunColour");
    private static readonly int SunShadowId = Shader.PropertyToID("_SunShadow");

    private readonly List<Object> created = new();

    /// <summary>
    /// Destroys every object the test made.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        SunShadowGlobals.Set(Color.black, Vector2.zero, false);
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
    /// The time of day is the start time plus the cycles the world clock has played, wrapped into a cycle: a day of 100 s
    /// and a night of 25 s put sunset at 0.8 of the cycle.
    /// </summary>
    [Test]
    public void TheTimeOfDay_IsTheStartPlusTheCyclesPlayed()
    {
        DayNightSettings settings = Track(DayNightSettings.Create(100f, 25f, 0.2f, new DayLightKey[0]));
        Assert.That(settings.CycleSeconds, Is.EqualTo(125f));
        Assert.That(settings.DayShare, Is.EqualTo(0.8f).Within(1e-6f));
        Assert.That(settings.PhaseAt(0d), Is.EqualTo(0.2f).Within(1e-6f));
        Assert.That(settings.PhaseAt(62.5d), Is.EqualTo(0.7f).Within(1e-6f));
        Assert.That(settings.PhaseAt(125d), Is.EqualTo(0.2f).Within(1e-6f));
        Assert.That(settings.PhaseAt(100d), Is.EqualTo(0f).Within(1e-6f), "The cycle wraps to the next sunrise.");
        Assert.That(settings.PhaseAt(125d * 100000d + 25d), Is.EqualTo(0.4f).Within(1e-4f), "Far into a save the time of day keeps its precision.");
    }

    /// <summary>
    /// The light is exactly each key's at its time, blended linearly between two keys, and from the last key round past
    /// sunrise to the first; its total is the sky and the sun together, opaque.
    /// </summary>
    [Test]
    public void TheLight_IsEachKeysAtItsTime_AndBlendsBetween_RoundTheCycle()
    {
        DayNightSettings settings = Track(DayNightSettings.Create(100f, 25f, 0f, new[]
        {
            new DayLightKey(0.1f, new Color(0.2f, 0.2f, 0.4f), new Color(0.4f, 0.2f, 0f)),
            new DayLightKey(0.5f, new Color(0.6f, 0.6f, 0.6f), new Color(0.2f, 0.2f, 0.2f)),
            new DayLightKey(0.9f, new Color(0f, 0f, 0.2f), Color.black, 1f)
        }));
        AssertLight(settings.LightAt(0.1f), new Color(0.2f, 0.2f, 0.4f), new Color(0.4f, 0.2f, 0f));
        AssertLight(settings.LightAt(0.5f), new Color(0.6f, 0.6f, 0.6f), new Color(0.2f, 0.2f, 0.2f));
        AssertLight(settings.LightAt(0.3f), new Color(0.4f, 0.4f, 0.5f), new Color(0.3f, 0.2f, 0.1f));
        AssertLight(settings.LightAt(0f), new Color(0.1f, 0.1f, 0.3f), new Color(0.2f, 0.1f, 0f));
        AssertLight(settings.LightAt(0.95f), new Color(0.05f, 0.05f, 0.25f), new Color(0.1f, 0.05f, 0f));
        AssertLight(settings.LightAt(1.1f), new Color(0.2f, 0.2f, 0.4f), new Color(0.4f, 0.2f, 0f));
        Assert.That(settings.LightAt(0.5f).NightLights, Is.EqualTo(0f), "The night lights' share is each key's at its time.");
        Assert.That(settings.LightAt(0.9f).NightLights, Is.EqualTo(1f));
        Assert.That(settings.LightAt(0.7f).NightLights, Is.EqualTo(0.5f).Within(1e-5f), "It blends between two keys.");
        Assert.That(settings.LightAt(0f).NightLights, Is.EqualTo(0.5f).Within(1e-5f), "And round the cycle.");
        Color total = settings.LightAt(0.5f).Total;
        Assert.That(total.r, Is.EqualTo(0.8f).Within(1e-6f));
        Assert.That(total.a, Is.EqualTo(1f));
    }

    /// <summary>
    /// The shipped day lasts 20 minutes and the night 5; its keys lie in order within the cycle; one key, the
    /// afternoon's, is today's light exactly, 0.85 white, and noon is a little brighter; the light warms at sunrise and
    /// sunset; the night is the night sky (0.13, 0.16, 0.30) without sun; no light passes 1; and the lights that burn at
    /// night are off from sunrise through the evening, so the afternoon draws today's pixels, rising at sunset and full
    /// from dusk to before dawn.
    /// </summary>
    [Test]
    public void TheShippedDay_IsTodaysLightInTheAfternoon_AndTheNightSkyAtNight()
    {
        DayNightSettings settings = AssetDatabase.LoadAssetAtPath<DayNightSettings>(SettingsPath);
        Assert.That(settings, Is.Not.Null);
        Assert.That(settings.DaySeconds, Is.EqualTo(1200f));
        Assert.That(settings.NightSeconds, Is.EqualTo(300f));
        Assert.That(settings.KeyCount, Is.GreaterThan(1));
        int today = -1;
        for (int i = 0; i < settings.KeyCount; i++)
        {
            DayLightKey key = settings.GetKey(i);
            Assert.That(key.Time, Is.InRange(0f, 0.999f));
            Assert.That(i == 0 || key.Time > settings.GetKey(i - 1).Time, Is.True, $"key {i} is in order");
            Color total = settings.LightAt(key.Time).Total;
            Assert.That(Mathf.Max(total.r, total.g, total.b), Is.LessThanOrEqualTo(1f), $"key {i}");
            if (total == Grey85 && total.r == 0.85f && total.g == 0.85f && total.b == 0.85f)
            {
                today = i;
            }
        }

        Assert.That(today, Is.GreaterThanOrEqualTo(0), "One key is today's light, 0.85 white, exactly.");
        float afternoon = settings.GetKey(today).Time;
        Assert.That(afternoon, Is.GreaterThan(settings.DayShare * 0.5f).And.LessThan(settings.DayShare), "Today's light is the afternoon's.");
        Assert.That(settings.LightAt(settings.DayShare * 0.5f).Total.r, Is.GreaterThan(0.85f), "Noon is a little brighter.");
        Color sunrise = settings.LightAt(0f).Total;
        Color sunset = settings.LightAt(settings.DayShare).Total;
        Assert.That(sunrise.r, Is.GreaterThan(sunrise.b));
        Assert.That(sunset.r, Is.GreaterThan(sunset.b));
        DayLight night = settings.LightAt(settings.DayShare + (1f - settings.DayShare) * 0.4f);
        Assert.That((Vector4)night.Sky, Is.EqualTo((Vector4)new Color(0.13f, 0.16f, 0.30f, 1f)).Using(Vector4EqualityComparer(1e-5f)));
        Assert.That(night.Sun.r + night.Sun.g + night.Sun.b, Is.Zero, "No sun at night.");
        Assert.That(night.NightLights, Is.EqualTo(1f), "The night's lights burn at full at night.");
        Assert.That(settings.LightAt(afternoon).NightLights, Is.EqualTo(0f), "Off in the afternoon.");
        Assert.That(settings.LightAt(0.02f).NightLights, Is.EqualTo(0f), "Off after sunrise.");
        Assert.That(settings.LightAt(0.7f).NightLights, Is.EqualTo(0f), "Off through the evening.");
        Assert.That(settings.LightAt(settings.DayShare).NightLights, Is.InRange(0.01f, 0.99f), "Rising at sunset.");
        Assert.That(settings.LightAt(0.96f).NightLights, Is.EqualTo(1f), "Full before dawn.");
        Assert.That(settings.FlickerSteps, Is.EqualTo(8f));
    }

    /// <summary>
    /// The sun rises in the east, stands highest at noon and sets in the west, and is down at night. Just after sunrise
    /// a shadow is as long as allowed, to the left and leaning toward the camera by half as much; at noon it is the noon
    /// length straight down; mid-morning it is the noon length over the sun's height, still to the left; just before
    /// sunset it is long to the lower right; at night there is none.
    /// </summary>
    [Test]
    public void TheSun_RisesEast_PeaksAtNoon_SetsWest_AndIsDownAtNight()
    {
        DayNightSettings settings = Track(DayNightSettings.Create(100f, 25f, 0f, new DayLightKey[0], 0.35f, 3f, 0.5f));
        SunState dawn = settings.SunAt(0.005f);
        Assert.That(dawn.IsUp, Is.True);
        Assert.That(dawn.Elevation, Is.LessThan(0.05f));
        Assert.That(dawn.Shadow.magnitude, Is.EqualTo(3f).Within(0.01f), "A low sun's shadow is as long as allowed.");
        Assert.That(dawn.Shadow.x, Is.LessThan(0f), "Sunrise in the east casts shadows to the west.");
        Assert.That(dawn.Shadow.y / dawn.Shadow.x, Is.EqualTo(0.5f).Within(0.01f), "Leaning toward the camera by half.");

        SunState noon = settings.SunAt(0.4f);
        Assert.That(noon.Elevation, Is.EqualTo(1f).Within(1e-5f));
        Assert.That(noon.Shadow.x, Is.EqualTo(0f).Within(1e-5f));
        Assert.That(noon.Shadow.y, Is.EqualTo(-0.35f).Within(1e-5f), "Short shadows below the casters at noon.");

        SunState morning = settings.SunAt(0.2f);
        Assert.That(morning.Elevation, Is.EqualTo(Mathf.Sin(Mathf.PI * 0.25f)).Within(1e-5f));
        Assert.That(morning.Shadow.magnitude, Is.EqualTo(0.35f / morning.Elevation).Within(1e-4f));
        Assert.That(morning.Shadow.x, Is.LessThan(0f));

        SunState dusk = settings.SunAt(0.795f);
        Assert.That(dusk.Shadow.x, Is.GreaterThan(2.5f), "Sunset in the west casts long shadows to the east.");
        Assert.That(dusk.Shadow.y, Is.LessThan(0f));

        SunState night = settings.SunAt(0.9f);
        Assert.That(night.IsUp, Is.False);
        Assert.That(night.Shadow, Is.EqualTo(Vector2.zero));
    }

    /// <summary>
    /// Where game state is decided, the clock follows the session's world clock: its time of day is the settings' at the
    /// session's time, moving on as game time passes.
    /// </summary>
    [Test]
    public void TheClock_FollowsTheSessionsWorldClock_WhereGameStateIsDecided()
    {
        ManualClock time = new() { Time = 40f };
        SessionSave session = new(time);
        session.StartNew();
        DayNightSettings settings = Track(DayNightSettings.Create(100f, 25f, 0.1f, new DayLightKey[0]));
        DayClock clock = new(settings, session, FixedGameAuthority.Authoritative, new FakeNetworkClock { ServerTime = 900d });
        Assert.That(clock.WorldTime, Is.EqualTo(0d));
        Assert.That(clock.Phase, Is.EqualTo(0.1f).Within(1e-6f));
        time.Advance(25f);
        Assert.That(clock.WorldTime, Is.EqualTo(25d).Within(1e-5));
        Assert.That(clock.Phase, Is.EqualTo(0.3f).Within(1e-5f));
        clock.Anchor(1000d, 900d);
        Assert.That(clock.WorldTime, Is.EqualTo(25d).Within(1e-5), "Where game state is decided an anchor changes nothing.");
    }

    /// <summary>
    /// A client follows its own world clock until it hears the host's, then the host's from the network time it was read
    /// at, counting on with the network time, and the latest it heard after that.
    /// </summary>
    [Test]
    public void AClient_FollowsTheHostsWorldClock_FromWhenItLastHeardIt()
    {
        ManualClock time = new();
        SessionSave session = new(time);
        session.StartNew();
        time.Advance(3f);
        FakeNetworkClock network = new() { ServerTime = 1000d };
        DayNightSettings settings = Track(DayNightSettings.Create(100f, 25f, 0f, new DayLightKey[0]));
        DayClock clock = new(settings, session, new FixedGameAuthority(false), network);
        Assert.That(clock.IsAnchored, Is.False);
        Assert.That(clock.WorldTime, Is.EqualTo(3d).Within(1e-5));
        clock.Anchor(500d, 1000d);
        Assert.That(clock.IsAnchored, Is.True);
        Assert.That(clock.WorldTime, Is.EqualTo(500d));
        network.ServerTime = 1010d;
        time.Advance(50f);
        Assert.That(clock.WorldTime, Is.EqualTo(510d), "It counts on with the network time, not its own game time.");
        Assert.That(clock.Phase, Is.EqualTo(settings.PhaseAt(510d)));
        clock.Anchor(509.5d, 1010d);
        Assert.That(clock.WorldTime, Is.EqualTo(509.5d), "A later message replaces the anchor.");
    }

    /// <summary>
    /// The local offset moves this machine's time of day and its light, wrapping round the cycle either way, while the
    /// world clock and the session's time of day stay.
    /// </summary>
    [Test]
    public void TheLocalOffset_MovesThisMachinesTimeOfDayAlone()
    {
        ManualClock time = new();
        SessionSave session = new(time);
        session.StartNew();
        DayNightSettings settings = Track(DayNightSettings.Create(100f, 25f, 0.5f, new[]
        {
            new DayLightKey(0f, Color.red, Color.black),
            new DayLightKey(0.5f, Color.blue, Color.black)
        }));
        DayClock clock = new(settings, session, FixedGameAuthority.Authoritative, new FakeNetworkClock());
        clock.LocalOffset = 0.75f;
        Assert.That(clock.SessionPhase, Is.EqualTo(0.5f).Within(1e-6f));
        Assert.That(clock.Phase, Is.EqualTo(0.25f).Within(1e-6f));
        Assert.That(clock.WorldTime, Is.EqualTo(0d));
        Assert.That((Vector4)clock.Light.Sky, Is.EqualTo((Vector4)Color.Lerp(Color.red, Color.blue, 0.5f)).Using(Vector4EqualityComparer(1e-5f)));
        clock.LocalOffset = -0.1f;
        Assert.That(clock.LocalOffset, Is.EqualTo(0.9f).Within(1e-6f));
        Assert.That(clock.Phase, Is.EqualTo(0.4f).Within(1e-6f));
        clock.LocalOffset = 0f;
        Assert.That(clock.Phase, Is.EqualTo(clock.SessionPhase));
    }

    /// <summary>
    /// The local rate holds this machine's time of day or runs it faster than the session's, keeping the time shown
    /// where it is as it changes; a time shown at a rate runs on from there; following the session again drops the
    /// offset and the rate.
    /// </summary>
    [Test]
    public void TheLocalRate_HoldsOrRunsThisMachinesTimeOfDay()
    {
        ManualClock time = new();
        SessionSave session = new(time);
        session.StartNew();
        DayNightSettings settings = Track(DayNightSettings.Create(100f, 25f, 0.2f, new[] { new DayLightKey(0f, Color.grey, Color.black) }));
        DayClock clock = new(settings, session, FixedGameAuthority.Authoritative, new FakeNetworkClock());
        clock.ShowPhase(0.5f);
        Assert.That(clock.Phase, Is.EqualTo(0.5f).Within(1e-5f));
        clock.LocalRate = 0f;
        time.Advance(25f);
        Assert.That(clock.SessionPhase, Is.EqualTo(0.4f).Within(1e-5f), "The session's time of day runs on.");
        Assert.That(clock.Phase, Is.EqualTo(0.5f).Within(1e-5f), "Held, this machine's stays.");

        clock.LocalRate = 10f;
        Assert.That(clock.Phase, Is.EqualTo(0.5f).Within(1e-5f), "Changing the rate keeps the time shown.");
        time.Advance(2.5f);
        Assert.That(clock.Phase, Is.EqualTo(0.7f).Within(1e-5f), "At 10 times, 2.5 s of a 125 s cycle move it a fifth.");
        time.Advance(10f);
        Assert.That(clock.Phase, Is.EqualTo(0.5f).Within(1e-5f), "It runs round the cycle.");

        clock.ShowPhase(0.1f);
        Assert.That(clock.Phase, Is.EqualTo(0.1f).Within(1e-5f));
        time.Advance(1.25f);
        Assert.That(clock.Phase, Is.EqualTo(0.2f).Within(1e-5f), "A time shown at a rate runs on from there.");

        clock.FollowSession();
        Assert.That(clock.LocalRate, Is.EqualTo(1f));
        Assert.That(clock.LocalOffset, Is.EqualTo(0f));
        Assert.That(clock.Phase, Is.EqualTo(clock.SessionPhase));
    }

    /// <summary>
    /// A save written at some time of day and loaded in a later session, whose game clock started elsewhere, resumes the
    /// same time of day, since it is kept as the world clock is.
    /// </summary>
    [Test]
    public void ALoadedSave_ResumesItsTimeOfDay()
    {
        DayNightSettings settings = AssetDatabase.LoadAssetAtPath<DayNightSettings>(SettingsPath);
        ManualClock firstTime = new() { Time = 12f };
        SessionSave first = new(firstTime);
        first.StartNew();
        firstTime.Advance(777.25f);
        DayClock before = new(settings, first, FixedGameAuthority.Authoritative, new FakeNetworkClock());
        float phase = before.Phase;
        Assert.That(GameSaveData.TryParse(first.ToData().ToJson(), out GameSaveData data), Is.True);

        ManualClock secondTime = new() { Time = 5000f };
        SessionSave second = new(secondTime);
        second.StartFrom(data);
        DayClock after = new(settings, second, FixedGameAuthority.Authoritative, new FakeNetworkClock());
        Assert.That(after.Phase, Is.EqualTo(phase).Within(1e-6f));
        Assert.That(phase, Is.Not.EqualTo(settings.StartTime).Within(1e-3f), "The day had moved on before it was saved.");
    }

    /// <summary>
    /// An outdoor world's global lights take the sky at an intensity of 1, its other lights untouched; an indoor world's
    /// keep the light they were authored with.
    /// </summary>
    [Test]
    public void AnOutdoorWorld_TakesTheSky_AnIndoorOneKeepsItsLight()
    {
        WorldLighting outdoor = CreateLighting(WorldLightingProfile.Outdoor, out Light2D outdoorSky, out Light2D lamp);
        WorldLighting indoor = CreateLighting(WorldLightingProfile.Indoor, out Light2D indoorSky, out _);
        Color dusk = new(0.6f, 0.4f, 0.45f, 1f);
        outdoor.ApplySky(dusk);
        indoor.ApplySky(dusk);
        Assert.That(outdoorSky.color, Is.EqualTo(dusk));
        Assert.That(outdoorSky.intensity, Is.EqualTo(1f));
        Assert.That(lamp.color, Is.EqualTo(Color.white), "A light that is not global is not the sky.");
        Assert.That(lamp.intensity, Is.EqualTo(0.85f));
        Assert.That(indoorSky.color, Is.EqualTo(Color.white));
        Assert.That(indoorSky.intensity, Is.EqualTo(0.85f));
    }

    /// <summary>
    /// The presenter lights every loaded world with the clock's sky and sun together as it starts, again once the light
    /// has moved by a visible step, not for a step too small to see, and a world as it loads.
    /// </summary>
    [Test]
    public void ThePresenter_LightsTheLoadedWorlds_AsTheDayMoves()
    {
        ManualClock time = new();
        SessionSave session = new(time);
        session.StartNew();
        DayNightSettings settings = Track(DayNightSettings.Create(100f, 25f, 0f, new[]
        {
            new DayLightKey(0f, new Color(0.2f, 0.2f, 0.4f), new Color(0.2f, 0.1f, 0f)),
            new DayLightKey(0.5f, new Color(0.6f, 0.6f, 0.6f), new Color(0.2f, 0.2f, 0.2f))
        }));
        DayClock clock = new(settings, session, FixedGameAuthority.Authoritative, new FakeNetworkClock());
        Worlds worlds = new();
        WorldLighting first = CreateLighting(WorldLightingProfile.Outdoor, out Light2D firstSky, out _);
        worlds.Add(new LoadedWorld(Track(ScriptableObject.CreateInstance<WorldDefinition>()), default, null, first));
        DayNightPresenter presenter = new(clock, worlds);
        presenter.Start();
        try
        {
            Assert.That(firstSky.color, Is.EqualTo(new Color(0.4f, 0.3f, 0.4f, 1f)));
            time.Advance(0.01f);
            presenter.Tick();
            Assert.That(firstSky.color, Is.EqualTo(new Color(0.4f, 0.3f, 0.4f, 1f)), "A step too small to see is not written.");
            time.Advance(30f);
            presenter.Tick();
            Assert.That((Vector4)firstSky.color, Is.EqualTo((Vector4)clock.Light.Total).Using(Vector4EqualityComparer(1e-6f)));

            WorldLighting second = CreateLighting(WorldLightingProfile.Outdoor, out Light2D secondSky, out _);
            worlds.Add(new LoadedWorld(Track(ScriptableObject.CreateInstance<WorldDefinition>()), default, null, second));
            Assert.That(secondSky.color, Is.EqualTo(presenter.Shown), "A world takes the light as it loads.");
        }
        finally
        {
            presenter.Dispose();
        }
    }

    /// <summary>
    /// While the sun is up over the outdoor world shown, the presenter hands the shaders the sun's light and its shadow
    /// vector, casting; with an indoor world shown, at night, and once disposed, nothing is cast and no sun is taken
    /// away anywhere. A world loaded but not shown does not count.
    /// </summary>
    [Test]
    public void ThePresenter_HandsTheSunToTheShaders_WhileItIsUpOverAnOutdoorWorldShown()
    {
        ManualClock time = new();
        SessionSave session = new(time);
        session.StartNew();
        DayNightSettings settings = Track(DayNightSettings.Create(100f, 25f, 0.3f, new[]
        {
            new DayLightKey(0f, new Color(0.2f, 0.2f, 0.4f), new Color(0.2f, 0.1f, 0f)),
            new DayLightKey(0.5f, new Color(0.6f, 0.6f, 0.6f), new Color(0.2f, 0.2f, 0.2f))
        }));
        DayClock clock = new(settings, session, FixedGameAuthority.Authoritative, new FakeNetworkClock());
        Worlds worlds = new();
        WorldLighting outdoor = CreateLighting(WorldLightingProfile.Outdoor, out _, out _);
        WorldLighting indoor = CreateLighting(WorldLightingProfile.Indoor, out _, out _);
        worlds.Add(new LoadedWorld(Track(ScriptableObject.CreateInstance<WorldDefinition>()), default, null, outdoor));
        worlds.Add(new LoadedWorld(Track(ScriptableObject.CreateInstance<WorldDefinition>()), default, null, indoor));
        DayNightPresenter presenter = new(clock, worlds);
        outdoor.SetShown(true);
        presenter.Start();
        try
        {
            Assert.That(presenter.Casting, Is.True);
            Vector4 shadow = Shader.GetGlobalVector(SunShadowId);
            Assert.That(new Vector2(shadow.x, shadow.y), Is.EqualTo(clock.Sun.Shadow));
            Assert.That(shadow.z, Is.EqualTo(1f));
            Color sun = clock.Light.Sun;
            Assert.That(Shader.GetGlobalVector(SunColourId), Is.EqualTo(new Vector4(sun.r, sun.g, sun.b, 0f)));

            outdoor.SetShown(false);
            indoor.SetShown(true);
            presenter.Tick();
            Assert.That(presenter.Casting, Is.False, "Indoors there is no sun.");
            Assert.That(Shader.GetGlobalVector(SunShadowId), Is.EqualTo(Vector4.zero));
            Assert.That(Shader.GetGlobalVector(SunColourId), Is.EqualTo(Vector4.zero));

            indoor.SetShown(false);
            outdoor.SetShown(true);
            presenter.Tick();
            Assert.That(presenter.Casting, Is.True);
            time.Advance(70f);
            presenter.Tick();
            Assert.That(clock.Sun.IsUp, Is.False);
            Assert.That(presenter.Casting, Is.False, "At night there is no sun.");
            Assert.That(Shader.GetGlobalVector(SunShadowId), Is.EqualTo(Vector4.zero));

            time.Advance(75f);
            presenter.Tick();
            Assert.That(presenter.Casting, Is.True);
        }
        finally
        {
            presenter.Dispose();
        }

        Assert.That(Shader.GetGlobalVector(SunShadowId), Is.EqualTo(Vector4.zero), "Stopping takes the sun away.");
        Assert.That(Shader.GetGlobalVector(SunColourId), Is.EqualTo(Vector4.zero));
    }

    /// <summary>
    /// Kept off by the lighting dev panel, the sun's shadows are not cast while it is up over the outdoor world shown,
    /// and the world keeps the sky and the sun together; following the day again, or forced on, they are cast again.
    /// </summary>
    [Test]
    public void ThePresenter_KeepsTheSunsShadowsOff_WhenTheOverrideSaysSo()
    {
        ManualClock time = new();
        SessionSave session = new(time);
        session.StartNew();
        DayNightSettings settings = Track(DayNightSettings.Create(100f, 25f, 0.3f, new[]
        {
            new DayLightKey(0f, new Color(0.2f, 0.2f, 0.4f), new Color(0.2f, 0.1f, 0f)),
            new DayLightKey(0.5f, new Color(0.6f, 0.6f, 0.6f), new Color(0.2f, 0.2f, 0.2f))
        }));
        DayClock clock = new(settings, session, FixedGameAuthority.Authoritative, new FakeNetworkClock());
        Worlds worlds = new();
        WorldLighting outdoor = CreateLighting(WorldLightingProfile.Outdoor, out Light2D sky, out _);
        worlds.Add(new LoadedWorld(Track(ScriptableObject.CreateInstance<WorldDefinition>()), default, null, outdoor));
        DayNightPresenter presenter = new(clock, worlds);
        outdoor.SetShown(true);
        presenter.Start();
        try
        {
            Assert.That(presenter.Casting, Is.True);
            presenter.SunShadowsOverride = LightingOverride.Off;
            presenter.Tick();
            Assert.That(presenter.Casting, Is.False, "Kept off, nothing is cast.");
            Assert.That(Shader.GetGlobalVector(SunShadowId), Is.EqualTo(Vector4.zero));
            Assert.That(Shader.GetGlobalVector(SunColourId), Is.EqualTo(Vector4.zero));
            Assert.That((Vector4)sky.color, Is.EqualTo((Vector4)clock.Light.Total).Using(Vector4EqualityComparer(1e-5f)), "The world keeps the sky and the sun together.");

            presenter.SunShadowsOverride = LightingOverride.On;
            presenter.Tick();
            Assert.That(presenter.Casting, Is.True, "Forced on, they follow the day.");
            presenter.SunShadowsOverride = LightingOverride.Off;
            presenter.Tick();
            presenter.SunShadowsOverride = LightingOverride.Follow;
            presenter.Tick();
            Assert.That(presenter.Casting, Is.True);
        }
        finally
        {
            presenter.Dispose();
        }
    }

    /// <summary>
    /// A world's lighting object of <paramref name="profile"/> holding a global light and a point light, both white at
    /// 0.85, as the worlds' scenes author theirs.
    /// </summary>
    private WorldLighting CreateLighting(WorldLightingProfile profile, out Light2D sky, out Light2D lamp)
    {
        GameObject root = Track(new GameObject("WorldLighting"));
        WorldLighting lighting = root.AddComponent<WorldLighting>();
        lighting.Configure(profile);
        GameObject skyObject = new("Global Light 2D");
        skyObject.transform.SetParent(root.transform, false);
        sky = skyObject.AddComponent<Light2D>();
        sky.lightType = Light2D.LightType.Global;
        sky.color = Color.white;
        sky.intensity = 0.85f;
        GameObject lampObject = new("Lamp");
        lampObject.transform.SetParent(root.transform, false);
        lamp = lampObject.AddComponent<Light2D>();
        lamp.lightType = Light2D.LightType.Point;
        lamp.color = Color.white;
        lamp.intensity = 0.85f;
        root.SetActive(false);
        return lighting;
    }

    /// <summary>
    /// Asserts <paramref name="light"/> is <paramref name="sky"/> and <paramref name="sun"/>, to within float rounding.
    /// </summary>
    private static void AssertLight(DayLight light, Color sky, Color sun)
    {
        Assert.That((Vector4)light.Sky, Is.EqualTo((Vector4)sky).Using(Vector4EqualityComparer(1e-5f)), "sky");
        Assert.That((Vector4)light.Sun, Is.EqualTo((Vector4)sun).Using(Vector4EqualityComparer(1e-5f)), "sun");
    }

    /// <summary>
    /// Compares vectors component by component to within <paramref name="tolerance"/>.
    /// </summary>
    private static System.Comparison<Vector4> Vector4EqualityComparer(float tolerance)
    {
        return (a, b) => Mathf.Abs(a.x - b.x) <= tolerance && Mathf.Abs(a.y - b.y) <= tolerance && Mathf.Abs(a.z - b.z) <= tolerance && Mathf.Abs(a.w - b.w) <= tolerance ? 0 : 1;
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
