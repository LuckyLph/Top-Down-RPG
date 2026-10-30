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
/// lighting the world with the night sky; and a client joining a host takes the host's time of day from its message.
/// </summary>
public class DayNightPlayModeTests
{
    private const float Step = 1f / 512f;

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
    /// clock's sky and sun together at an intensity of 1; moved to the night by the dev panel, it takes the night sky.
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
        clock.LocalOffset = 0f;
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
        SessionSave clientSession = new(UnityClock.Shared);
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
