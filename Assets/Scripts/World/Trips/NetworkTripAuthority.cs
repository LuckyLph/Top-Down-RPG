using System;
using UnityEngine;

/// <summary>
/// The Gameplay scope's <see cref="ITripAuthority"/> (Docs/WorldStreaming.md: Travelling between worlds, Networking).
/// Offline and on the host a request is checked here by <see cref="LocalTripAuthority"/>, whose approval puts the player
/// in the target world for one more trip; for the host's own player (or offline) <see cref="Approved"/> then tells this
/// machine to go, while a client's player hears of it through its networked world, which changes with the trip
/// (<see cref="PlayerWorldSync"/>). On a client a request goes to the host through its player's <see cref="ITripLink"/>,
/// with where its own machine has the player, and the host's answer comes back as <see cref="Deliver"/>: the arrival is
/// held so the target world starts loading here, and <see cref="Approved"/> sets this machine's trip off. Nobody else
/// fades or pauses. The host also sends players on trips of its own (<see cref="Send"/>: a respawn at a waystone), which
/// reach a client's player the same way.
/// </summary>
public sealed class NetworkTripAuthority : ITripAuthority
{
    private readonly LocalTripAuthority local;
    private readonly ISessionStatus session;
    private readonly TripArrivals arrivals;
    private Func<Transform, ITripLink> findLink = FindLink;

    /// <summary>
    /// Creates the authority checking with <paramref name="localAuthority"/>, sending requests over the network while
    /// <paramref name="sessionStatus"/> says this machine is a client, and holding a client's arrivals in
    /// <paramref name="tripArrivals"/>.
    /// </summary>
    public NetworkTripAuthority(LocalTripAuthority localAuthority, ISessionStatus sessionStatus, TripArrivals tripArrivals)
    {
        local = localAuthority;
        session = sessionStatus;
        arrivals = tripArrivals;
    }

    public event Action<TripApproval> Approved;

    /// <summary>
    /// How a player's body is asked for its <see cref="ITripLink"/>; the component on it by default, a fake in tests.
    /// </summary>
    internal Func<Transform, ITripLink> LinkFinder
    {
        get => findLink;
        set => findLink = value ?? FindLink;
    }

    /// <summary>
    /// Whether requests go to a host over the network: this machine is a client of a session.
    /// </summary>
    public bool SendsToHost => session.IsActive && !session.IsHost;

    /// <summary>
    /// On a client, sends <paramref name="request"/> to the host through its player's <see cref="ITripLink"/> (with a
    /// warning when the player has none); otherwise checks it here (<see cref="Approve"/>).
    /// </summary>
    public void Request(TripRequest request)
    {
        if (!SendsToHost)
        {
            Approve(request);
            return;
        }

        Transform body = request.Player != null ? request.Player.Transform : null;
        ITripLink link = body != null ? findLink(body) : null;
        if (link == null)
        {
            Debug.LogWarning($"Trip not sent: the player has no {nameof(ITripLink)} to reach the host with.");
            return;
        }

        link.SendTripRequest(request.ExitId, request.HasPosition ? request.Position : (Vector2)body.position);
    }

    /// <summary>
    /// Offline or on the host: checks <paramref name="request"/> (a local one, or a client's from its RPC) and, when it
    /// goes, prepares the trip, puts the player in the target world and raises <see cref="Approved"/>. A refusal is
    /// logged as a warning. Returns whether the trip goes.
    /// </summary>
    public bool Approve(TripRequest request)
    {
        if (local.TryApprove(request, out TripApproval approval, out string refusal))
        {
            Approved?.Invoke(approval);
            return true;
        }

        Debug.LogWarning($"Trip refused: {refusal}");
        return false;
    }

    /// <summary>
    /// Offline or on the host: sends <paramref name="player"/> on a trip to <paramref name="world"/>'s entry
    /// <paramref name="entryName"/> that no request asked for (a dead player to its waystone), as an approved trip goes:
    /// the arrival held, the player put in the world for one more trip, and <see cref="Approved"/> raised, which a
    /// client's player hears of through its networked world. Returns the approval.
    /// </summary>
    public TripApproval Send(PlayerHandle player, WorldDefinition world, string entryName)
    {
        TripApproval approval = local.Send(player, world, entryName);
        Approved?.Invoke(approval);
        return approval;
    }

    /// <summary>
    /// The <see cref="ITripLink"/> component on <paramref name="body"/>, or null.
    /// </summary>
    private static ITripLink FindLink(Transform body)
    {
        return body.TryGetComponent(out ITripLink link) ? link : null;
    }

    /// <summary>
    /// On a client: the host approved this machine's trip, as <paramref name="approval"/> says. Holds the arrival so the
    /// target world and the chunks around its entry start loading here, and raises <see cref="Approved"/>.
    /// </summary>
    public void Deliver(TripApproval approval)
    {
        arrivals.Hold(approval.Player, new StreamInterest(approval.World, approval.Position));
        Approved?.Invoke(approval);
    }
}
