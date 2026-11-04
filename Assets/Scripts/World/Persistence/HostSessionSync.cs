using System;
using Unity.Collections;
using Unity.Netcode;
using VContainer.Unity;

/// <summary>
/// Tells each client of a session which host session it plays in (Docs/KnownGaps.md, Phase 5), so a client keeps one
/// copy of its own place per host session (<see cref="JoinedSaveRecorder"/>). The host sends its
/// <see cref="SessionSave.Id"/> to each client once its Gameplay scene is up (<see cref="NetworkSession.ClientJoined"/>,
/// a late joiner included); a client keeps the id it hears from the host (<see cref="HostSessionId"/>) and ignores any
/// other sender. One small named message per join; nothing offline.
/// </summary>
public sealed class HostSessionSync : IInitializable, IDisposable
{
    public const string MessageName = "TopDownRPG.HostSession";

    private readonly SessionSave sessionSave;
    private readonly NetworkSession session;
    private NetworkManager manager;
    private bool hosting;

    /// <summary>
    /// Creates the sync of <paramref name="save"/>'s id over <paramref name="networkSession"/>.
    /// </summary>
    public HostSessionSync(SessionSave save, NetworkSession networkSession)
    {
        sessionSave = save;
        session = networkSession;
    }

    /// <summary>
    /// On a client, the id of the host's session it plays in, once the host has told it; null before.
    /// </summary>
    public string HostSessionId { get; private set; }

    /// <summary>
    /// In a session, the host starts sending its id to joining clients and a client starts listening for it, before any
    /// entry point tells the host it joined.
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
        }
        else
        {
            Listen(manager, id => HostSessionId = id);
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
    /// Hands <paramref name="received"/> each host session id <paramref name="client"/>, a client's manager, hears from
    /// the host; messages from anyone else are ignored.
    /// </summary>
    internal static void Listen(NetworkManager client, Action<string> received)
    {
        client.CustomMessagingManager.RegisterNamedMessageHandler(MessageName, (sender, reader) =>
        {
            if (sender != NetworkManager.ServerClientId)
            {
                return;
            }

            reader.ReadValueSafe(out string id);
            if (!string.IsNullOrEmpty(id))
            {
                received(id);
            }
        });
    }

    /// <summary>
    /// Sends <paramref name="client"/> the host session id <paramref name="id"/> from <paramref name="host"/>, the host's
    /// manager.
    /// </summary>
    internal static void Send(NetworkManager host, ulong client, string id)
    {
        if (host == null || !host.IsListening || string.IsNullOrEmpty(id))
        {
            return;
        }

        using FastBufferWriter writer = new(sizeof(int) + (id.Length * sizeof(char)), Allocator.Temp);
        writer.WriteValueSafe(id);
        host.CustomMessagingManager.SendNamedMessage(MessageName, client, writer, NetworkSession.MessageDelivery);
    }

    /// <summary>
    /// The host: sends <paramref name="client"/> this session's id.
    /// </summary>
    private void Send(ulong client)
    {
        Send(manager, client, sessionSave.Id);
    }
}
