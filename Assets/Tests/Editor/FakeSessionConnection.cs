using System;
using System.Threading;
using UnityEngine;

/// <summary>
/// <see cref="ISessionConnection"/> without Netcode: hosting succeeds unless a test says otherwise, a join waits until
/// the test completes it (<see cref="CompleteJoin"/>) or its token is cancelled, and <see cref="LoseConnection"/> raises
/// the connection's loss.
/// </summary>
public sealed class FakeSessionConnection : ISessionConnection
{
    private AwaitableCompletionSource<bool> pendingJoin;

    /// <summary>
    /// Creates a connection over <paramref name="settings"/>.
    /// </summary>
    public FakeSessionConnection(NetworkSettings settings)
    {
        Settings = settings;
    }

    public event Action ConnectionLost;

    public NetworkSettings Settings { get; }
    public string LocalPlayerId { get; set; } = "local-player";
    public string JoinRefusal { get; private set; }
    public string JoinedAddress { get; private set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsHost { get; set; }
    public bool IsFollowingHost { get; set; }
    public bool IsHostLost => IsFollowingHost && !IsActive;
    public bool HostStarts { get; set; } = true;
    public int HostsStarted { get; private set; }
    public int Joins { get; private set; }
    public int Shutdowns { get; private set; }
    public string JoinHost { get; private set; }
    public ushort JoinPort { get; private set; }
    public float JoinTimeout { get; private set; }
    public bool IsJoinPending => pendingJoin != null;

    /// <summary>
    /// Starts hosting unless <see cref="HostStarts"/> is false.
    /// </summary>
    public bool StartHost()
    {
        if (!HostStarts)
        {
            return false;
        }

        HostsStarted++;
        IsActive = true;
        IsHost = true;
        return true;
    }

    /// <summary>
    /// Records the join and waits for <see cref="CompleteJoin"/>; cancelling throws.
    /// </summary>
    public Awaitable<bool> JoinAsync(string host, ushort port, float timeoutSeconds, CancellationToken cancellation)
    {
        Joins++;
        JoinHost = host;
        JoinPort = port;
        JoinTimeout = timeoutSeconds;
        JoinRefusal = null;
        JoinedAddress = new JoinAddress(host, port).ToString();
        AwaitableCompletionSource<bool> source = new();
        pendingJoin = source;
        cancellation.Register(() =>
        {
            if (pendingJoin == source)
            {
                pendingJoin = null;
            }

            source.TrySetCanceled();
        });
        return source.Awaitable;
    }

    /// <summary>
    /// Ends the waiting join: connected, or not with <paramref name="refusal"/> (null for a host that never answered).
    /// </summary>
    public void CompleteJoin(bool connected, string refusal = null)
    {
        AwaitableCompletionSource<bool> source = pendingJoin;
        pendingJoin = null;
        JoinRefusal = connected ? null : refusal;
        IsActive = connected;
        IsFollowingHost = connected;
        source.SetResult(connected);
    }

    /// <summary>
    /// Ends the session.
    /// </summary>
    public void Shutdown()
    {
        Shutdowns++;
        IsActive = false;
        IsHost = false;
        IsFollowingHost = false;
    }

    /// <summary>
    /// Raises <see cref="ConnectionLost"/>, as a host leaving does.
    /// </summary>
    public void LoseConnection()
    {
        IsActive = false;
        ConnectionLost?.Invoke();
    }
}
