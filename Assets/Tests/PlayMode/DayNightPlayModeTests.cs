using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// PlayMode tests for the day and night (Docs/Lighting.md, Phase 3): in an offline game the world on screen is lit by
/// the time of day, which follows the session's world clock, and the dev panel moves this machine's time of day, night
/// lighting the world with the night sky and burning the night's lights, which the loaded chunks hand over as they load
/// (Phase 5); a client joining a host takes the host's time of day from its message; and in the Sandbox's lighting
/// showcase the lighting dev panel teleports the player to a spot, holds the time of day and forces the lights, the
/// lava's glow, the flicker, the glowing pixels, the lights' shadows and the sun's shadows; and in the Overworld the
/// regions tint the sky as the player goes from one to another, under drifting clouds (Phase 8).
/// </summary>
public class DayNightPlayModeTests
{
    private const float Step = 1f / 512f;
    private const string SandboxRootPath = "Assets/Worlds/Sandbox/World_Sandbox.unity";
    private const string OverworldRootPath = "Assets/Worlds/Overworld/World_Overworld.unity";
    private const string SnowfieldSkyPath = "Assets/Data/Lighting/Skies/Sky_Snowfield.asset";
    private const string LavaChunkName = "World_Sandbox_1_0";
    private const string WallsName = "CollisionTilemap";

    private InProcessClient client;
    private GameObject panel;

    /// <summary>
    /// Disposes the in-process client and the test's panel when the test made them.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        client?.Dispose();
        client = null;
        if (panel != null)
        {
            Object.Destroy(panel);
        }
    }

    /// <summary>
    /// In a new offline game the clock follows the session's world clock, and the world on screen's global light is the
    /// clock's sky and sun together at an intensity of 1; moved to the night by the dev panel, it takes the night sky,
    /// and the night's lights, which every loaded chunk has handed over, burn at full, their flicker handed to the shaders.
    /// </summary>
    [UnityTest]
    public IEnumerator TheWorldOnScreen_IsLitByTheTimeOfDay()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();
        yield return null;
        yield return null;
        DayClock clock = SceneBootTestHelper.ResolveFromGameplay<DayClock>();
        SessionSave session = SceneBootTestHelper.ResolveFromMain<SessionSave>();
        Assert.That(clock.WorldTime, Is.EqualTo(session.Time).Within(1e-6));
        Light2D sky = ShownSky();
        AssertColour(sky.color, clock.Light.Total, Step, "The sky is the clock's light.");
        Assert.That(sky.intensity, Is.EqualTo(1f));

        panel = new GameObject("TestDayNightPanel");
        DayNightDebugPanel debug = panel.AddComponent<DayNightDebugPanel>();
        float night = clock.Settings.DayShare + (1f - clock.Settings.DayShare) * 0.4f;
        Assert.That(debug.ShowTimeOfDay(night), Is.True);
        Assert.That(clock.Phase, Is.EqualTo(night).Within(1e-3f));
        Assert.That(clock.Sun.IsUp, Is.False);
        yield return null;
        yield return null;
        AssertColour(sky.color, new Color(0.13f, 0.16f, 0.30f, 1f), 0.01f, "At night the sky is the night sky.");
        PropLighting lighting = SceneBootTestHelper.ResolveFromGameplay<PropLighting>();
        Assert.That(lighting.GroupCount, Is.GreaterThan(0), "The loaded chunks hand their lights over.");
        Assert.That(lighting.Shown, Is.EqualTo(1f), "The night's lights burn at full at night.");
        Assert.That(Shader.GetGlobalVector("_GlowFlicker").y, Is.EqualTo(clock.Settings.GlowFlickerShare), "The flicker goes to the shaders.");
        clock.LocalOffset = 0f;
    }

    /// <summary>
    /// In the Sandbox, the lighting panel teleports the player to the lava, where at night held the chunk's prop lights and
    /// the lava's glow burn; forced off they go out and following the day they burn again; the flicker and the glowing pixels
    /// go off for the shaders and come back; at the spot where a lamp meets a wall and trees the burning lights cast and
    /// wake the props' casters near them, the walls cast and the sky casts nothing, and with the lights' shadows kept off
    /// every caster sleeps (Phase 7); in the afternoon the sun casts, kept off it does not; and following the session
    /// again drops the offset and the rate.
    /// </summary>
    [UnityTest]
    public IEnumerator TheLightingPanel_DrivesTheSandboxShowcase()
    {
        yield return SceneBootTestHelper.BootIntoWorld(SandboxRootPath);
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            mob.gameObject.SetActive(false);
        }

        panel = new GameObject("TestLightingPanel");
        LightingDebugControls controls = panel.AddComponent<LightingDebugPanel>().Controls();
        Assert.That(controls, Is.Not.Null, "In a game the panel has its controls.");
        WorldDefinition sandbox = SceneBootTestHelper.ResolveGameFlow().CurrentWorld;
        int lava = -1;
        for (int i = 0; i < sandbox.Entries.Count; i++)
        {
            lava = sandbox.Entries[i].Name == "lava" ? i : lava;
        }

        Assert.That(lava, Is.GreaterThanOrEqualTo(0), "The Sandbox has a lava spot.");
        Assert.That(controls.Teleport(lava + 1), Is.EqualTo("lava"));
        Vector2 spot = sandbox.ToWorld(sandbox.Entries[lava].Position);
        PlayerController player = SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current.Controller;
        Assert.That(Vector2.Distance(player.transform.position, spot), Is.LessThan(0.01f), "The player stands on the spot.");
        yield return SceneBootTestHelper.WaitUntil(() => GlowingGround() != null && LavaChunkLight() != null, "the lava chunk to load");

        controls.ShowKey(8);
        controls.ToggleHold();
        DayClock clock = controls.Clock;
        Assert.That(clock.LocalRate, Is.EqualTo(0f));
        yield return null;
        yield return null;
        Light2D propLight = LavaChunkLight().LightSource;
        Assert.That(propLight.enabled, Is.True, "At night the chunk's prop lights burn.");
        Assert.That(GlowingGround().GlowLight.enabled, Is.True, "At night the lava glows.");

        controls.CyclePropLights();
        controls.CyclePropLights();
        controls.CycleGroundGlow();
        controls.CycleGroundGlow();
        yield return null;
        Assert.That(propLight.enabled, Is.False, "Forced off, they are out.");
        Assert.That(GlowingGround().GlowLight.enabled, Is.False, "Forced off, the lava does not glow.");
        controls.CyclePropLights();
        controls.CycleGroundGlow();
        yield return null;
        Assert.That(propLight.enabled, Is.True, "Following the day, they burn again.");
        Assert.That(GlowingGround().GlowLight.enabled, Is.True);

        controls.CycleFlicker();
        controls.CycleGlowingPixels();
        yield return null;
        Vector4 flicker = Shader.GetGlobalVector("_GlowFlicker");
        Assert.That(flicker.y, Is.EqualTo(0f), "Without flicker no pixel flickers.");
        Assert.That(flicker.z, Is.EqualTo(1f), "The glowing pixels are off.");
        controls.CycleFlicker();
        controls.CycleGlowingPixels();
        yield return null;
        flicker = Shader.GetGlobalVector("_GlowFlicker");
        Assert.That(flicker.y, Is.EqualTo(clock.Settings.GlowFlickerShare));
        Assert.That(flicker.z, Is.EqualTo(0f));

        int walls = -1;
        for (int i = 0; i < sandbox.Entries.Count; i++)
        {
            walls = sandbox.Entries[i].Name == "walls" ? i : walls;
        }

        Assert.That(walls, Is.GreaterThanOrEqualTo(0), "The Sandbox has a spot where a lamp meets a wall and trees.");
        Assert.That(controls.Teleport(walls - lava), Is.EqualTo("walls"));
        PropLighting lighting = controls.PropLighting;
        yield return SceneBootTestHelper.WaitUntil(() => lighting.AwakeCasters > 0, "the burning lights to wake the props' casters near them");
        Assert.That(ShownSky().shadowsEnabled, Is.False, "The sky casts nothing.");
        Assert.That(propLight.shadowsEnabled, Is.True, "A prop light casts.");
        Assert.That(WallsCaster().enabled, Is.True, "The walls stop the light.");
        controls.CycleLightShadows();
        yield return null;
        Assert.That(lighting.AwakeCasters, Is.Zero, "With the lights' shadows kept off, every caster sleeps.");
        Assert.That(propLight.shadowsEnabled, Is.False);
        controls.CycleLightShadows();
        yield return null;
        Assert.That(lighting.AwakeCasters, Is.GreaterThan(0), "Following again, the burning lights wake their casters.");
        Assert.That(propLight.shadowsEnabled, Is.True);

        controls.ShowKey(4);
        yield return null;
        Assert.That(controls.Presenter.Casting, Is.True, "In the afternoon the sun casts.");
        controls.CycleSunShadows();
        yield return null;
        Assert.That(controls.Presenter.Casting, Is.False, "Kept off, the sun casts nothing.");
        controls.CycleSunShadows();
        yield return null;
        Assert.That(controls.Presenter.Casting, Is.True);

        controls.FollowSession();
        Assert.That(clock.LocalRate, Is.EqualTo(1f));
        Assert.That(clock.LocalOffset, Is.EqualTo(0f));
    }

    /// <summary>
    /// In the Overworld (Phase 8), at the Snowfield's waystone the loaded chunks have handed their region skies over and
    /// the world on screen takes the afternoon's sky and sun in the Snowfield's tint, the clouds forced on drifting over
    /// the ground; back at the Clearing's waystone it takes the day's own light.
    /// </summary>
    [UnityTest]
    public IEnumerator TheOverworldsRegions_TintTheSky_AsThePlayerGoesFromOneToAnother()
    {
        yield return SceneBootTestHelper.BootIntoWorld(OverworldRootPath);
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            mob.gameObject.SetActive(false);
        }

        panel = new GameObject("TestLightingPanel");
        LightingDebugControls controls = panel.AddComponent<LightingDebugPanel>().Controls();
        DayNightPresenter presenter = controls.Presenter;
        RegionSkies skies = SceneBootTestHelper.ResolveFromGameplay<RegionSkies>();
        Assert.That(skies.Count, Is.GreaterThan(0), "The loaded chunks hand their skies over.");
        controls.ShowKey(4);
        controls.ToggleHold();
        controls.CycleClouds();
        Assert.That(controls.Teleport(EntryIndex("snowfield") + 1), Is.EqualTo("snowfield"));
        RegionSky snowfield = UnityEditor.AssetDatabase.LoadAssetAtPath<RegionSky>(SnowfieldSkyPath);
        yield return SceneBootTestHelper.WaitUntil(() => Near(presenter.Sky.Tint, snowfield.Tint, 0.01f), "the Snowfield's chunks to load and tint the sky");
        yield return null;
        Color total = controls.Clock.Light.Total;
        Color tint = presenter.Sky.Tint;
        AssertColour(ShownSky().color, new Color(total.r * tint.r, total.g * tint.g, total.b * tint.b, 1f), Step, "The sky and the sun in the Snowfield's tint.");
        Assert.That(presenter.Casting, Is.True, "In the afternoon the sun casts.");
        Assert.That(presenter.CloudCover, Is.GreaterThan(0f), "The clouds forced on cover some ground.");
        Assert.That(Shader.GetGlobalVector("_SunClouds").w, Is.EqualTo(controls.Clock.Settings.CloudShade), "The clouds go to the shaders.");

        Assert.That(controls.Teleport(EntryIndex("clearing") - EntryIndex("snowfield")), Is.EqualTo("clearing"));
        yield return null;
        yield return null;
        Assert.That(presenter.Sky.Tint, Is.EqualTo(Color.white), "The Clearing keeps the day's own sky.");
        AssertColour(ShownSky().color, controls.Clock.Light.Total, Step, "The day's own light.");
        controls.FollowSession();
    }

    /// <summary>
    /// A client joining a host hears the host's world clock as it joins and from then shows the host's time of day.
    /// </summary>
    [UnityTest]
    public IEnumerator AJoiningClient_TakesTheHostsTimeOfDay()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            mob.gameObject.SetActive(false);
        }

        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        DayClock hostClock = gameplay.Resolve<DayClock>();
        client = InProcessClient.Start(session, gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");

        NetworkManager clientManager = client.Manager;
        SessionSave clientSession = new(new UnityClock());
        clientSession.StartNew();
        DayClock clientClock = new(hostClock.Settings, clientSession, new ClientAuthority(), new ManagerClock(clientManager));
        DayClockSync.Listen(clientManager, clientClock);
        client.SendReady();
        yield return SceneBootTestHelper.WaitUntil(() => clientClock.IsAnchored, "the client to hear the host's world clock");
        yield return null;

        Assert.That(clientClock.WorldTime, Is.EqualTo(hostClock.WorldTime).Within(0.25), "The client's world clock is the host's, to within the network's lag.");
        float apart = Mathf.Abs(clientClock.Phase - hostClock.Phase);
        Assert.That(Mathf.Min(apart, 1f - apart), Is.LessThan(1e-3f), "Both show the same time of day.");
        clientManager.CustomMessagingManager.UnregisterNamedMessageHandler(DayClockSync.MessageName);
    }

    /// <summary>
    /// Whether <paramref name="a"/> lies within <paramref name="tolerance"/> of <paramref name="b"/> in every colour
    /// channel.
    /// </summary>
    private static bool Near(Color a, Color b, float tolerance)
    {
        return Mathf.Abs(a.r - b.r) <= tolerance && Mathf.Abs(a.g - b.g) <= tolerance && Mathf.Abs(a.b - b.b) <= tolerance;
    }

    /// <summary>
    /// The index of the entry named <paramref name="name"/> in the world the local player is in.
    /// </summary>
    private static int EntryIndex(string name)
    {
        WorldDefinition world = SceneBootTestHelper.ResolveGameFlow().CurrentWorld;
        for (int i = 0; i < world.Entries.Count; i++)
        {
            if (world.Entries[i].Name == name)
            {
                return i;
            }
        }

        Assert.Fail($"{world.name} has no entry {name}.");
        return -1;
    }

    /// <summary>
    /// The global light of the world on screen: the first global light under a shown world's lighting.
    /// </summary>
    private static Light2D ShownSky()
    {
        foreach (LoadedWorld world in SceneBootTestHelper.ResolveFromGameplay<Worlds>().Loaded)
        {
            if (world.Lighting != null && world.Lighting.gameObject.activeInHierarchy)
            {
                foreach (Light2D light in world.Lighting.GetComponentsInChildren<Light2D>())
                {
                    if (light.lightType == Light2D.LightType.Global)
                    {
                        return light;
                    }
                }
            }
        }

        Assert.Fail("No world on screen has a global light.");
        return null;
    }

    /// <summary>
    /// The renderer of the Sandbox's lava chunk once it is loaded and glows, else null.
    /// </summary>
    private static TerrainBlendRenderer GlowingGround()
    {
        foreach (TerrainBlendRenderer renderer in Object.FindObjectsByType<TerrainBlendRenderer>())
        {
            if (renderer.gameObject.scene.name == LavaChunkName && renderer.GlowLight != null)
            {
                return renderer;
            }
        }

        return null;
    }

    /// <summary>
    /// The shadow caster of the Sandbox lava chunk's walls.
    /// </summary>
    private static ShadowCaster2D WallsCaster()
    {
        foreach (ShadowCaster2D caster in Object.FindObjectsByType<ShadowCaster2D>())
        {
            if (caster.gameObject.scene.name == LavaChunkName && caster.gameObject.name == WallsName)
            {
                return caster;
            }
        }

        Assert.Fail("The lava chunk's walls have no shadow caster.");
        return null;
    }

    /// <summary>
    /// A prop light of the Sandbox's lava chunk (its campfire or a lamp post along the road) once it is loaded, else null.
    /// </summary>
    private static PropLight LavaChunkLight()
    {
        foreach (PropLight light in Object.FindObjectsByType<PropLight>())
        {
            if (light.gameObject.scene.name == LavaChunkName)
            {
                return light;
            }
        }

        return null;
    }

    /// <summary>
    /// Asserts <paramref name="actual"/> is <paramref name="expected"/> to within <paramref name="tolerance"/> on each of
    /// red, green and blue.
    /// </summary>
    private static void AssertColour(Color actual, Color expected, float tolerance, string message)
    {
        Assert.That(actual.r, Is.EqualTo(expected.r).Within(tolerance), message);
        Assert.That(actual.g, Is.EqualTo(expected.g).Within(tolerance), message);
        Assert.That(actual.b, Is.EqualTo(expected.b).Within(tolerance), message);
    }

    /// <summary>
    /// A client's authority: it decides nothing.
    /// </summary>
    private sealed class ClientAuthority : IGameAuthority
    {
        public bool IsAuthoritative => false;
    }

    /// <summary>
    /// The network time of a client's own manager.
    /// </summary>
    private sealed class ManagerClock : INetworkClock
    {
        private readonly NetworkManager manager;

        public ManagerClock(NetworkManager networkManager)
        {
            manager = networkManager;
        }

        public double ServerTime => manager.IsListening ? manager.ServerTime.Time : 0d;
    }
}
