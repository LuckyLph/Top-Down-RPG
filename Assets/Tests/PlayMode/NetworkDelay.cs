using System;
using System.Collections.Generic;
using Unity.Multiplayer.Tools.NetworkSimulator.Runtime;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;
using UnityEngine;
using Object = UnityEngine.Object;
using TransportSettings = Unity.Networking.Transport.NetworkSettings;

/// <summary>
/// A delayed connection between the PlayMode tests' in-process host and clients (Docs/Multiplayer.md, Testing under a
/// bad connection), usable by any test. Unity Transport's simulator stage, which the Network Simulator drives, delays the
/// packets a driver sends and receives alike, and the simulator writes its preset to every driver in the process, so in
/// one process each way went through it twice. Installed, this builds every driver created from then on with its
/// simulator stage applied to sent packets only and from a fixed seed, and turns any driver already running the same
/// way when it sets conditions, so each way is delayed once, by its sender: <see cref="Set"/> gives every driver half
/// the round trip, give or take the jitter, and the loss, both ways alike; <see cref="SetSending"/> sets what one
/// manager sends, one way only. The Network Simulators in the scenes are disabled while it is installed, since each
/// writes its own preset over every driver as one is created. Disposing it builds drivers as before and gives every
/// running one no delay.
/// </summary>
public sealed class NetworkDelay : IDisposable
{
    public const uint Seed = 20261104;

    private readonly List<NetworkSimulator> disabledSimulators = new();
    private readonly DriverConstructor constructor;

    /// <summary>
    /// Installs the driver constructor and disables the enabled Network Simulators.
    /// </summary>
    private NetworkDelay()
    {
        constructor = new DriverConstructor();
        UnityTransport.s_DriverConstructor = constructor;
        DisableSimulators();
    }

    /// <summary>
    /// Installs a delayed connection with no delay yet. Install it before a session starts for the fixed seed; a driver
    /// already running keeps the seed it was built with.
    /// </summary>
    public static NetworkDelay Install()
    {
        return new NetworkDelay();
    }

    /// <summary>
    /// Gives every running driver, and every one created later, <paramref name="conditions"/>: what each sends is held
    /// half the round trip, give or take the jitter, and dropped at the loss rate.
    /// </summary>
    public void Set(Conditions conditions)
    {
        DisableSimulators();
        constructor.Conditions = conditions;
        foreach (UnityTransport transport in Object.FindObjectsByType<UnityTransport>())
        {
            Apply(transport, conditions.OneWayMs, conditions.JitterMs, conditions.LossPercent);
        }
    }

    /// <summary>
    /// Gives what <paramref name="manager"/> sends, one way only, <paramref name="delayMs"/> of delay give or take
    /// <paramref name="jitterMs"/> and <paramref name="lossPercent"/> of loss (100 drops all of it).
    /// </summary>
    public void SetSending(NetworkManager manager, int delayMs, int jitterMs, int lossPercent)
    {
        DisableSimulators();
        Apply(manager.GetComponent<UnityTransport>(), delayMs, jitterMs, lossPercent);
    }

    /// <summary>
    /// Builds drivers as Netcode does again, gives every running driver no delay and enables the simulators it disabled.
    /// </summary>
    public void Dispose()
    {
        if (UnityTransport.s_DriverConstructor == constructor)
        {
            UnityTransport.s_DriverConstructor = null;
        }

        foreach (UnityTransport transport in Object.FindObjectsByType<UnityTransport>())
        {
            Apply(transport, 0, 0, 0);
        }

        foreach (NetworkSimulator simulator in disabledSimulators)
        {
            if (simulator != null)
            {
                simulator.enabled = true;
            }
        }

        disabledSimulators.Clear();
    }

    /// <summary>
    /// Sets the simulator stage of <paramref name="transport"/>'s driver, when it runs one, to delay what it sends only
    /// by <paramref name="delayMs"/> give or take <paramref name="jitterMs"/> and drop <paramref name="lossPercent"/> of it.
    /// </summary>
    private static void Apply(UnityTransport transport, int delayMs, int jitterMs, int lossPercent)
    {
        if (transport == null)
        {
            return;
        }

        NetworkDriver driver = transport.GetNetworkDriver();
        if (!driver.IsCreated)
        {
            return;
        }

        TransportSettings settings = driver.CurrentSettings;
        if (!settings.TryGet(out SimulatorUtility.Parameters parameters))
        {
            return;
        }

        driver.ModifySimulatorStageParameters(Sending(parameters, delayMs, jitterMs, lossPercent));
    }

    /// <summary>
    /// <paramref name="parameters"/> applied to sent packets only, with the given delay, jitter and loss.
    /// </summary>
    private static SimulatorUtility.Parameters Sending(SimulatorUtility.Parameters parameters, int delayMs, int jitterMs, int lossPercent)
    {
        parameters.Mode = ApplyMode.SentPacketsOnly;
        parameters.PacketDelayMs = Mathf.Max(0, delayMs);
        parameters.PacketJitterMs = Mathf.Max(0, jitterMs);
        parameters.PacketDropPercentage = Mathf.Clamp(lossPercent, 0, 100);
        parameters.PacketDropInterval = 0;
        return parameters;
    }

    /// <summary>
    /// Disables every enabled Network Simulator, remembering it to enable again on dispose.
    /// </summary>
    private void DisableSimulators()
    {
        foreach (NetworkSimulator simulator in Object.FindObjectsByType<NetworkSimulator>())
        {
            if (simulator.enabled)
            {
                simulator.enabled = false;
                disabledSimulators.Add(simulator);
            }
        }
    }

    /// <summary>
    /// A connection's conditions: its round trip, each way's jitter (a way's delay lies within it of half the round
    /// trip) and each way's loss.
    /// </summary>
    public readonly struct Conditions
    {
        public static readonly Conditions None = new(0, 0, 0);
        public static readonly Conditions Fast = new(50, 5, 1);
        public static readonly Conditions Typical = new(150, 15, 2);
        public static readonly Conditions Slow = new(300, 30, 4);

        /// <summary>
        /// A round trip of <paramref name="roundTripMs"/>, each way given or taken <paramref name="jitterMs"/> and losing
        /// <paramref name="lossPercent"/> of its packets.
        /// </summary>
        public Conditions(int roundTripMs, int jitterMs, int lossPercent)
        {
            RoundTripMs = roundTripMs;
            JitterMs = jitterMs;
            LossPercent = lossPercent;
        }

        public int RoundTripMs { get; }
        public int JitterMs { get; }
        public int LossPercent { get; }
        public int OneWayMs => RoundTripMs / 2;

        /// <summary>
        /// The conditions for logs and assertion messages.
        /// </summary>
        public override string ToString()
        {
            return $"{RoundTripMs} ms round trip, ±{JitterMs} ms each way, {LossPercent}% loss each way";
        }
    }

    /// <summary>
    /// Builds a driver as <see cref="UnityTransport"/> does, its simulator stage applied to sent packets only, from a
    /// seed of its own counted from <see cref="Seed"/>, at the current conditions.
    /// </summary>
    private sealed class DriverConstructor : INetworkStreamDriverConstructor
    {
        private uint built;

        public Conditions Conditions { get; set; } = Conditions.None;

        /// <summary>
        /// Creates the driver over UDP with the transport's own settings and pipelines.
        /// </summary>
        public void CreateDriver(
            UnityTransport transport,
            out NetworkDriver driver,
            out NetworkPipeline unreliableFragmentedPipeline,
            out NetworkPipeline unreliableSequencedFragmentedPipeline,
            out NetworkPipeline reliableSequencedPipeline)
        {
            TransportSettings settings = transport.GetDefaultNetworkSettings();
            if (settings.TryGet(out SimulatorUtility.Parameters parameters))
            {
                parameters = Sending(parameters, Conditions.OneWayMs, Conditions.JitterMs, Conditions.LossPercent);
                parameters.RandomSeed = Seed + built;
                settings.AddRawParameterStruct(ref parameters);
            }

            built++;
            driver = NetworkDriver.Create(new UDPNetworkInterface(), settings);
            transport.GetDefaultPipelineConfigurations(
                ref driver,
                out var unreliableFragmentedStages,
                out var unreliableSequencedFragmentedStages,
                out var reliableSequencedStages);
            unreliableFragmentedPipeline = driver.CreatePipeline(unreliableFragmentedStages);
            unreliableSequencedFragmentedPipeline = driver.CreatePipeline(unreliableSequencedFragmentedStages);
            reliableSequencedPipeline = driver.CreatePipeline(reliableSequencedStages);
        }
    }
}
