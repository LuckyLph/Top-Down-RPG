using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Multiplayer.Tools.NetworkSimulator.Runtime;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// PlayMode tests of the delayed connection the in-process tests share (<see cref="NetworkDelay"/>, Docs/Multiplayer.md:
/// Testing under a bad connection): an in-process client pings the host with named messages, the host echoes each at
/// once, and every ping's way out, way back and round trip are timed on the clock both share. At no delay and at three
/// round trips (50, 150 and 300 ms, with jitter and loss each way), on the reliable and the unreliable channel, the
/// round trip comes out the setting's plus the frames a message waits to be sent and read, and each way half of it.
/// </summary>
public class NetworkDelayPlayModeTests
{
    private const string PingMessage = "TopDownRPG.Test.Ping";
    private const string PongMessage = "TopDownRPG.Test.Pong";
    private const int PingsPerRound = 80;
    private const float PingSpacingSeconds = 0.025f;

    private readonly Dictionary<int, double> sentAt = new();
    private readonly List<double> outbound = new();
    private readonly List<double> inbound = new();
    private readonly List<double> roundTrips = new();

    private InProcessClient client;
    private NetworkDelay delay;
    private NetworkManager hostManager;
    private int nextPing;

    /// <summary>
    /// Disposes the delayed connection, stops answering pings and disposes the in-process client.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        delay?.Dispose();
        delay = null;
        if (hostManager != null && hostManager.CustomMessagingManager != null)
        {
            hostManager.CustomMessagingManager.UnregisterNamedMessageHandler(PingMessage);
        }

        client?.Dispose();
        client = null;
    }

    /// <summary>
    /// With the delayed connection installed before hosting, an unreliable ping's round trip comes out each setting's
    /// within its jitter, over what the same pings take with no delay (the frames a message waits to be sent and read),
    /// each way about half of it, losing about what both ways' loss make. The reliable channel loses nothing, and its
    /// quickest pings take the setting's round trip too; the others wait behind a lost packet's resend, since it
    /// delivers in order.
    /// </summary>
    [UnityTest]
    public IEnumerator TheDelayedConnection_DelaysEachWayByHalfTheRoundTrip()
    {
        yield return HostAndJoin(true);

        RoundResult baseline = null;
        yield return MeasureRound(NetworkDelay.Conditions.None, false, result => baseline = result);
        Debug.Log($"[NetDelay] {baseline}");
        NetworkDelay.Conditions[] settings = { NetworkDelay.Conditions.Fast, NetworkDelay.Conditions.Typical, NetworkDelay.Conditions.Slow };
        foreach (NetworkDelay.Conditions conditions in settings)
        {
            double expected = conditions.RoundTripMs + baseline.MedianRoundTripMs;
            double slack = conditions.JitterMs + 25.0;
            RoundResult unreliable = null;
            yield return MeasureRound(conditions, false, found => unreliable = found);
            Debug.Log($"[NetDelay] {unreliable}");
            Assert.That(unreliable.MedianRoundTripMs, Is.InRange(expected - slack, expected + slack), $"The median round trip follows the setting. {unreliable}");
            Assert.That(unreliable.MedianOutboundMs, Is.InRange(conditions.OneWayMs - slack, conditions.OneWayMs + baseline.MedianRoundTripMs + slack), $"The client's way to the host is delayed by half. {unreliable}");
            Assert.That(unreliable.MedianInboundMs, Is.InRange(conditions.OneWayMs - slack, conditions.OneWayMs + baseline.MedianRoundTripMs + slack), $"The host's way to the client is delayed by half. {unreliable}");
            Assert.That(unreliable.Lost, Is.LessThanOrEqualTo(PingsPerRound / 4), $"The unreliable channel loses about what the setting drops. {unreliable}");

            RoundResult reliable = null;
            yield return MeasureRound(conditions, true, found => reliable = found);
            Debug.Log($"[NetDelay] {reliable}");
            Assert.That(reliable.Lost, Is.Zero, $"The reliable channel loses nothing. {reliable}");
            Assert.That(reliable.QuickestRoundTripMs, Is.InRange(expected - slack, expected + slack), $"The reliable channel's quickest pings take the setting's round trip. {reliable}");
        }
    }

    /// <summary>
    /// Explicit measurement of what the Network Simulator alone did before the delayed connection: with its preset at
    /// 150 ms, 20 ms jitter and 4% loss, as the smoothness tests set it, on drivers Netcode built itself (the simulator
    /// stage on sent and received packets), each way goes through the stage on both machines' drivers. Logs the round
    /// trip; asserts nothing.
    /// </summary>
    [UnityTest, Explicit, Category("NetworkBench")]
    public IEnumerator Benchmark_TheNetworkSimulatorsPreset_InOneProcess()
    {
        yield return HostAndJoin(false);
        NetworkSimulator simulator = hostManager.GetComponent<NetworkSimulator>();
        simulator.ConnectionPreset = NetworkSimulatorPreset.Create("Stress", "150 ms latency, 20 ms jitter, 4% loss", 150, 20, 0, 4);
        try
        {
            foreach (bool reliable in new[] { true, false })
            {
                RoundResult result = null;
                yield return MeasureRound(null, reliable, found => result = found, "Network Simulator preset 150 ms, 20 ms jitter, 4% loss");
                Debug.Log($"[NetDelay] {result}");
            }
        }
        finally
        {
            simulator.ConnectionPreset = NetworkSimulatorPresets.None;
        }
    }

    /// <summary>
    /// Boots to the menu, installs the delayed connection when <paramref name="delayed"/>, hosts with every mob
    /// deactivated, and joins an in-process client whose pongs are timed; the host echoes every ping.
    /// </summary>
    private IEnumerator HostAndJoin(bool delayed)
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        if (delayed)
        {
            delay = NetworkDelay.Install();
        }

        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            mob.gameObject.SetActive(false);
        }

        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        hostManager = session.NetworkManager;
        client = InProcessClient.Start(session, gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendReady();
        hostManager.CustomMessagingManager.RegisterNamedMessageHandler(PingMessage, Echo);
        client.Manager.CustomMessagingManager.RegisterNamedMessageHandler(PongMessage, TimePong);
    }

    /// <summary>
    /// Sets <paramref name="conditions"/> (none leaves the connection as it is), lets what is in flight land, then sends
    /// a round of pings on the reliable or the unreliable channel and waits out the slowest, handing the timings to
    /// <paramref name="done"/>.
    /// </summary>
    private IEnumerator MeasureRound(NetworkDelay.Conditions? conditions, bool reliable, System.Action<RoundResult> done, string label = null)
    {
        if (conditions.HasValue)
        {
            delay.Set(conditions.Value);
        }

        yield return new WaitForSecondsRealtime(0.6f);
        sentAt.Clear();
        outbound.Clear();
        inbound.Clear();
        roundTrips.Clear();
        NetworkDelivery delivery = reliable ? NetworkDelivery.ReliableSequenced : NetworkDelivery.Unreliable;
        int frames = Time.frameCount;
        double started = Time.realtimeSinceStartupAsDouble;
        for (int i = 0; i < PingsPerRound; i++)
        {
            int ping = nextPing++;
            double now = Time.realtimeSinceStartupAsDouble;
            sentAt[ping] = now;
            using (FastBufferWriter writer = new(32, Allocator.Temp))
            {
                writer.WriteValueSafe(ping);
                writer.WriteValueSafe(now);
                writer.WriteValueSafe(reliable);
                client.Manager.CustomMessagingManager.SendNamedMessage(PingMessage, NetworkManager.ServerClientId, writer, delivery);
            }

            yield return new WaitForSecondsRealtime(PingSpacingSeconds);
        }

        double frameMs = (Time.realtimeSinceStartupAsDouble - started) * 1000.0 / Mathf.Max(1, Time.frameCount - frames);
        float deadline = Time.realtimeSinceStartup + 3f;
        while (roundTrips.Count < PingsPerRound && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        string name = label ?? conditions.Value.ToString();
        done(new RoundResult(name, reliable, frameMs, roundTrips, outbound, inbound, PingsPerRound - roundTrips.Count));
    }

    /// <summary>
    /// The host's answer to a ping: the same ping back to its sender at once, on the channel it came on, with the time
    /// it arrived.
    /// </summary>
    private void Echo(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int ping);
        reader.ReadValueSafe(out double pingSentAt);
        reader.ReadValueSafe(out bool reliable);
        double now = Time.realtimeSinceStartupAsDouble;
        using FastBufferWriter writer = new(32, Allocator.Temp);
        writer.WriteValueSafe(ping);
        writer.WriteValueSafe(pingSentAt);
        writer.WriteValueSafe(now);
        hostManager.CustomMessagingManager.SendNamedMessage(PongMessage, sender, writer, reliable ? NetworkDelivery.ReliableSequenced : NetworkDelivery.Unreliable);
    }

    /// <summary>
    /// Times a pong on the client: the ping's way out, the way back and the round trip, for a ping of this round.
    /// </summary>
    private void TimePong(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int ping);
        reader.ReadValueSafe(out double pingSentAt);
        reader.ReadValueSafe(out double echoedAt);
        if (!sentAt.Remove(ping))
        {
            return;
        }

        double now = Time.realtimeSinceStartupAsDouble;
        outbound.Add((echoedAt - pingSentAt) * 1000.0);
        inbound.Add((now - echoedAt) * 1000.0);
        roundTrips.Add((now - pingSentAt) * 1000.0);
    }

    /// <summary>
    /// The timings of one round of pings, in milliseconds.
    /// </summary>
    private sealed class RoundResult
    {
        /// <summary>
        /// Sorts and keeps the timings of the round named <paramref name="name"/>.
        /// </summary>
        public RoundResult(string name, bool reliable, double frameMs, List<double> roundTrips, List<double> outbound, List<double> inbound, int lost)
        {
            Name = name;
            Reliable = reliable;
            FrameMs = frameMs;
            RoundTrips = Sorted(roundTrips);
            Outbound = Sorted(outbound);
            Inbound = Sorted(inbound);
            Lost = lost;
        }

        public string Name { get; }
        public bool Reliable { get; }
        public double FrameMs { get; }
        public double[] RoundTrips { get; }
        public double[] Outbound { get; }
        public double[] Inbound { get; }
        public int Lost { get; }
        public double MedianRoundTripMs => Median(RoundTrips);
        public double QuickestRoundTripMs => Percentile(RoundTrips, 0.1);
        public double MedianOutboundMs => Median(Outbound);
        public double MedianInboundMs => Median(Inbound);

        /// <summary>
        /// One line: the round trip's median, 10th and 90th percentiles and range, each way's median, the losses and the
        /// frame time.
        /// </summary>
        public override string ToString()
        {
            return $"{Name}, {(Reliable ? "reliable" : "unreliable")}: round trip median {MedianRoundTripMs:0} ms (p10 {Percentile(RoundTrips, 0.1):0}, p90 {Percentile(RoundTrips, 0.9):0}, min {Percentile(RoundTrips, 0):0}, max {Percentile(RoundTrips, 1):0}); out {MedianOutboundMs:0} ms, back {MedianInboundMs:0} ms; lost {Lost} of {PingsPerRound}; frame {FrameMs:0.0} ms";
        }

        /// <summary>
        /// A sorted copy of <paramref name="values"/>.
        /// </summary>
        private static double[] Sorted(List<double> values)
        {
            double[] sorted = values.ToArray();
            System.Array.Sort(sorted);
            return sorted;
        }

        /// <summary>
        /// The median of sorted <paramref name="values"/>, NaN for none.
        /// </summary>
        private static double Median(double[] values)
        {
            return Percentile(values, 0.5);
        }

        /// <summary>
        /// The value at <paramref name="share"/> of sorted <paramref name="values"/>, NaN for none.
        /// </summary>
        private static double Percentile(double[] values, double share)
        {
            if (values.Length == 0)
            {
                return double.NaN;
            }

            return values[Mathf.Clamp((int)System.Math.Round(share * (values.Length - 1)), 0, values.Length - 1)];
        }
    }
}
