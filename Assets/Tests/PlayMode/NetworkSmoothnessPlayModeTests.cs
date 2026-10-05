using System.Collections;
using NUnit.Framework;
using Unity.Multiplayer.Tools.NetworkSimulator.Runtime;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// PlayMode tests and an explicit benchmark measuring how smoothly the host's copy of a client's player moves under a
/// simulated bad connection.
/// </summary>
public class NetworkSmoothnessPlayModeTests
{
    private const float MeasureSeconds = 2f;
    private const float OnePixel = 1f / 32f;
    private const int BenchmarkRuns = 6;
    private const int RegressionRuns = 3;
    private const float FarAwayDistance = 1000f;

    private static readonly (NetworkTransform.InterpolationTypes, bool, int)[] BenchmarkConfigurations =
    {
        (NetworkTransform.InterpolationTypes.LegacyLerp, false, 0),
        (NetworkTransform.InterpolationTypes.LegacyLerp, true, 0),
        (NetworkTransform.InterpolationTypes.LegacyLerp, true, 1),
        (NetworkTransform.InterpolationTypes.Lerp, true, 1),
        (NetworkTransform.InterpolationTypes.Lerp, true, 2),
        (NetworkTransform.InterpolationTypes.SmoothDampening, true, 1),
    };

    private InProcessClient client;
    private NetworkSimulator simulator;

    /// <summary>
    /// Resets the global interpolation buffer offset and the network simulator preset, and disposes the in-process
    /// client.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        NetworkTransform.InterpolationBufferTickOffset = 0;
        if (simulator != null)
        {
            simulator.ConnectionPreset = NetworkSimulatorPresets.None;
        }

        client?.Dispose();
        client = null;
    }

    /// <summary>
    /// Explicit benchmark that logs averaged smoothness for each interpolation type, delta reliability and buffer
    /// offset combination under the stress preset, with a count of runs that stalled for over 150 ms. Asserts nothing.
    /// </summary>
    [UnityTest, Explicit, Category("NetworkBench")]
    public IEnumerator Benchmark_RemotePlayerSmoothness_UnderStress()
    {
        PlayerController hostCopy = null;
        yield return HostWithClient(copy => hostCopy = copy);
        simulator.ConnectionPreset = StressPreset();
        yield return WaitUntilStill(hostCopy.transform);

        NetworkTransform receiving = hostCopy.GetComponent<NetworkTransform>();
        NetworkTransform sending = client.LocalPlayer.Current.Controller.GetComponent<NetworkTransform>();
        float direction = 1f;
        foreach ((NetworkTransform.InterpolationTypes type, bool unreliable, int offset) in BenchmarkConfigurations)
        {
            receiving.PositionInterpolationType = type;
            sending.UseUnreliableDeltas = unreliable;
            NetworkTransform.InterpolationBufferTickOffset = offset;
            yield return WaitUntilStill(hostCopy.transform);

            SmoothnessResult total = new();
            int stalledRuns = 0;
            for (int run = 0; run < BenchmarkRuns; run++)
            {
                SmoothnessResult result = new();
                yield return Measure(hostCopy, direction, result);
                direction = -direction;
                total.Accumulate(result);
                if (result.LongestStallSeconds > 0.15f)
                {
                    stalledRuns++;
                }
            }

            Debug.Log($"[NetSmooth] {type} unreliable={unreliable} offset={offset} over {BenchmarkRuns} runs: {total.Averaged(BenchmarkRuns)} runsWithStallOver150ms={stalledRuns}");
        }
    }

    /// <summary>
    /// With the shipped network settings under 150 ms latency, jitter and loss, the host's copy of a walking client
    /// never steps back a pixel, never freezes for 0.6 s, starts within 0.8 s and keeps a steady speed.
    /// </summary>
    [UnityTest]
    public IEnumerator RemotePlayer_MovesSmoothly_UnderStress()
    {
        PlayerController hostCopy = null;
        yield return HostWithClient(copy => hostCopy = copy);
        simulator.ConnectionPreset = StressPreset();
        yield return WaitUntilStill(hostCopy.transform);

        SmoothnessResult total = new();
        float direction = 1f;
        for (int run = 0; run < RegressionRuns; run++)
        {
            SmoothnessResult single = new();
            yield return Measure(hostCopy, direction, single);
            direction = -direction;
            total.Accumulate(single);
        }

        SmoothnessResult result = total.Averaged(RegressionRuns);
        Debug.Log($"[NetSmooth] shipped settings over {RegressionRuns} runs: {result}");

        Assert.That(result.BackwardSteps, Is.EqualTo(0), $"The remote copy should never jump backwards by a visible pixel. {result}");
        Assert.That(result.LongestStallSeconds, Is.LessThan(0.6f), $"The remote copy should not freeze. {result}");
        Assert.That(result.StartLagSeconds, Is.LessThan(0.8f), $"The remote copy should start moving soon after its owner. {result}");
        Assert.That(result.MeanSpeedError, Is.LessThan(0.5f), $"The remote copy should move at a steady speed. {result}");
    }

    /// <summary>
    /// Hosts from the main menu, despawns the area's mobs, connects a ready in-process client with its colliders off,
    /// caches the host's network simulator and hands the host's copy of the client's player to
    /// <paramref name="hostCopyFound"/>.
    /// </summary>
    private IEnumerator HostWithClient(System.Action<PlayerController> hostCopyFound)
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        simulator = session.NetworkManager.GetComponent<NetworkSimulator>();
        Assert.That(simulator, Is.Not.Null, "The NetworkManager prefab should carry a NetworkSimulator.");

        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            mob.GetComponent<NetworkObject>().Despawn();
        }

        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerRegistry hostPlayers = gameplay.Resolve<PlayerRegistry>();
        client = InProcessClient.Start(session, gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");

        client.SendReady();
        yield return SceneBootTestHelper.WaitUntil(
            () => hostPlayers.Players.Count == 2 && client.Players.Players.Count == 2 && client.LocalPlayer.Current != null,
            "both sides to see both players");
        client.DisableColliders();

        hostCopyFound(InProcessClient.FindCopyOwnedBy(hostPlayers, client.Manager.LocalClientId));
    }

    /// <summary>
    /// Orders the client's player to walk far along <paramref name="direction"/>, records how long the host's copy
    /// takes to start, then for a fixed window its backward steps, longest stall and speed error against the owner,
    /// and finally stops the player and waits for the copy to settle. Fails if the copy never starts moving.
    /// </summary>
    private IEnumerator Measure(PlayerController hostCopy, float direction, SmoothnessResult result)
    {
        Transform owner = client.LocalPlayer.Current.Transform;
        float hostStartX = hostCopy.transform.position.x;
        float moveStarted = Time.realtimeSinceStartup;
        client.Input.PointAt((Vector2)owner.position + new Vector2(direction * FarAwayDistance, 0f));
        client.Input.MovePressedThisFrame = true;
        yield return null;
        client.Input.MovePressedThisFrame = false;

        while (Mathf.Abs(hostCopy.transform.position.x - hostStartX) < 0.05f)
        {
            if (Time.realtimeSinceStartup - moveStarted > 5f)
            {
                Assert.Fail("The remote copy never started moving.");
            }

            yield return null;
        }

        result.StartLagSeconds = Time.realtimeSinceStartup - moveStarted;

        float ownerStartX = owner.position.x;
        float previousX = hostCopy.transform.position.x;
        float windowStart = Time.realtimeSinceStartup;
        float previousTime = windowStart;
        float stall = 0f;
        float speedErrorSum = 0f;
        int frames = 0;

        while (Time.realtimeSinceStartup - windowStart < MeasureSeconds)
        {
            yield return null;
            float now = Time.realtimeSinceStartup;
            float deltaTime = Mathf.Max(0.0001f, now - previousTime);
            float step = (hostCopy.transform.position.x - previousX) * direction;
            previousX = hostCopy.transform.position.x;
            previousTime = now;
            frames++;

            float expectedSpeed = (owner.position.x - ownerStartX) * direction / Mathf.Max(0.0001f, now - windowStart);
            if (step < -OnePixel)
            {
                result.BackwardSteps++;
            }

            if (step < 0.1f * expectedSpeed * deltaTime)
            {
                stall += deltaTime;
                result.LongestStallSeconds = Mathf.Max(result.LongestStallSeconds, stall);
            }
            else
            {
                stall = 0f;
            }

            if (expectedSpeed > 0.01f)
            {
                speedErrorSum += Mathf.Abs(step / deltaTime - expectedSpeed) / expectedSpeed;
            }
        }

        result.MeanSpeedError = frames > 0 ? speedErrorSum / frames : 0f;
        result.Frames = frames;

        client.Input.StopPressedThisFrame = true;
        yield return null;
        client.Input.StopPressedThisFrame = false;
        yield return WaitUntilStill(hostCopy.transform);
    }

    /// <summary>
    /// Waits until <paramref name="target"/> has not moved for half a second, giving up silently after five seconds.
    /// </summary>
    private static IEnumerator WaitUntilStill(Transform target)
    {
        float deadline = Time.realtimeSinceStartup + 5f;
        float stillSince = Time.realtimeSinceStartup;
        Vector3 previous = target.position;
        while (Time.realtimeSinceStartup - stillSince < 0.5f && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            if ((target.position - previous).sqrMagnitude > 0.000001f)
            {
                stillSince = Time.realtimeSinceStartup;
            }

            previous = target.position;
        }
    }

    /// <summary>
    /// A bad-connection preset: 150 ms latency, 20 ms jitter and 4% packet loss.
    /// </summary>
    private static NetworkSimulatorPreset StressPreset()
    {
        return NetworkSimulatorPreset.Create("Stress", "150 ms latency, 20 ms jitter, 4% loss", 150, 20, 0, 4);
    }

    /// <summary>
    /// Smoothness metrics of the host's copy over one or more measured runs.
    /// </summary>
    private sealed class SmoothnessResult
    {
        public float StartLagSeconds;
        public float LongestStallSeconds;
        public int BackwardSteps;
        public float MeanSpeedError;
        public int Frames;

        /// <summary>
        /// Adds <paramref name="other"/>'s lag, backward steps, speed error and frames, and keeps the longer stall.
        /// </summary>
        public void Accumulate(SmoothnessResult other)
        {
            StartLagSeconds += other.StartLagSeconds;
            LongestStallSeconds = Mathf.Max(LongestStallSeconds, other.LongestStallSeconds);
            BackwardSteps += other.BackwardSteps;
            MeanSpeedError += other.MeanSpeedError;
            Frames += other.Frames;
        }

        /// <summary>
        /// A copy with lag and speed error averaged over <paramref name="runs"/>; the longest stall, backward steps and
        /// frames stay as accumulated.
        /// </summary>
        public SmoothnessResult Averaged(int runs)
        {
            return new SmoothnessResult
            {
                StartLagSeconds = StartLagSeconds / runs,
                LongestStallSeconds = LongestStallSeconds,
                BackwardSteps = BackwardSteps,
                MeanSpeedError = MeanSpeedError / runs,
                Frames = Frames
            };
        }

        /// <summary>
        /// One-line summary of the metrics for logs and assertion messages.
        /// </summary>
        public override string ToString()
        {
            return $"lag={StartLagSeconds * 1000f:0}ms longestStall={LongestStallSeconds * 1000f:0}ms backward={BackwardSteps} speedError={MeanSpeedError:P0} frames={Frames}";
        }
    }
}
