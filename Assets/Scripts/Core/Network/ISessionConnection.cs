using System;
using System.Threading;
using UnityEngine;

/// <summary>
/// Starting, joining and ending this machine's network session, and hearing that a joined session was lost
/// (Docs/UiRevamp.md, Phase 9). Implemented by <see cref="NetworkSession"/>; the main menu's session starter and the
/// session's lifecycle use it, so tests can substitute a fake.
/// </summary>
public interface ISessionConnection : ISessionStatus, IHostLink
{
    /// <summary>
    /// Raised once when the local client stops without <see cref="Shutdown"/> having been called.
    /// </summary>
    event Action ConnectionLost;

    NetworkSettings Settings { get; }
    string LocalPlayerId { get; }

    /// <summary>
    /// The host's refusal of the last join (<see cref="JoinApproval"/>), or null.
    /// </summary>
    string JoinRefusal { get; }

    /// <summary>
    /// The address of the last join, <c>host:port</c>, or empty before any.
    /// </summary>
    string JoinedAddress { get; }

    /// <summary>
    /// Starts hosting; false when it could not.
    /// </summary>
    bool StartHost();

    /// <summary>
    /// Connects to <paramref name="host"/> on <paramref name="port"/> within <paramref name="timeoutSeconds"/>; false
    /// when refused or unreachable. Cancelling ends the attempt and throws.
    /// </summary>
    Awaitable<bool> JoinAsync(string host, ushort port, float timeoutSeconds, CancellationToken cancellation);

    /// <summary>
    /// Ends the session on purpose.
    /// </summary>
    void Shutdown();
}
