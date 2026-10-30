using System;
using Unity.Collections;
using Unity.Netcode;
using VContainer.Unity;

/// <summary>
/// Shares the host's world clock, which the time of day follows, with the clients of a session (Docs/Lighting.md,
/// Phase 3). The host sends it, with the network time it was read at, to each client once its Gameplay scene is up
/// (<see cref="NetworkSession.ClientJoined"/>, a late joiner included) and to every joined client again each
/// <see cref="DayNightSettings.ResyncSeconds"/> of its world clock, so a client's day never drifts from the host's
/// (the host's world clock runs on game time, the network time on real time). A client anchors its
/// <see cref="DayClock"/> on each message from the host and ignores any other sender. One small named message;
/// nothing is sent each frame, and nothing at all offline.
/// </summary>
public sealed class DayClockSync : IInitializable, ITickable, IDisposable
{
    public const string MessageName = "TopDownRPG.DayClock";

    private readonly DayClock clock;
    private readonly NetworkSession session;
    private NetworkManager manager;
    private bool hosting;
    private double nextResync;

    /// <summary>
    /// Creates the sync of <paramref name="dayClock"/> over <paramref name="networkSession"/>.
    /// </summary>
    public DayClockSync(DayClock dayClock, NetworkSession networkSession)
    {
        clock = dayClock;
        session = networkSession;
    }

    /// <summary>
    /// In a session, the host starts sending its clock to joining clients and a client starts listening for it, before
    /// any entry point tells the host it joined.
    /// </summary>
    public void Initialize()
    {
        if (session == null || !session.IsActive)
        {
            return;
        }

        manager = session.NetworkManager;
        hosting = manager.IsServer;
        if (hosting)
        {
            session.ClientJoined += Send;
            nextResync = clock.WorldTime + clock.Settings.ResyncSeconds;
        }
        else
        {
            Listen(manager, clock);
        }
    }

    /// <summary>
    /// The host: sends its clock to every joined client again once a resync's time has passed.
    /// </summary>
    public void Tick()
    {
        if (!hosting || manager == null || !manager.IsListening || clock.WorldTime < nextResync)
        {
            return;
        }

        nextResync = clock.WorldTime + clock.Settings.ResyncSeconds;
        foreach (ulong client in session.JoinedClients)
        {
            Send(client);
        }
    }

    /// <summary>
    /// Stops sending or listening.
    /// </summary>
    public void Dispose()
    {
        if (hosting)
        {
            session.ClientJoined -= Send;
        }
        else if (manager != null && manager.CustomMessagingManager != null)
        {
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(MessageName);
        }

        manager = null;
    }

    /// <summary>
    /// Makes <paramref name="dayClock"/> follow the host's clock as <paramref name="client"/>, a client's manager,
    /// hears it; messages from anyone but the host are ignored.
    /// </summary>
    internal static void Listen(NetworkManager client, DayClock dayClock)
    {
        client.CustomMessagingManager.RegisterNamedMessageHandler(MessageName, (sender, reader) =>
        {
            if (sender != NetworkManager.ServerClientId)
            {
                return;
            }

            reader.ReadValueSafe(out double worldTime);
            reader.ReadValueSafe(out double serverTime);
            dayClock.Anchor(worldTime, serverTime);
        });
    }

    /// <summary>
    /// The host: sends <paramref name="client"/> its world clock and the network time it was read at.
    /// </summary>
    private void Send(ulong client)
    {
        if (manager == null || !manager.IsListening)
        {
            return;
        }

        using FastBufferWriter writer = new(sizeof(double) * 2, Allocator.Temp);
        writer.WriteValueSafe(clock.WorldTime);
        writer.WriteValueSafe(manager.ServerTime.Time);
        manager.CustomMessagingManager.SendNamedMessage(MessageName, client, writer, NetworkSession.MessageDelivery);
    }
}
