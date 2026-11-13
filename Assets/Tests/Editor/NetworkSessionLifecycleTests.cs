using System.Threading;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for <see cref="NetworkSessionLifecycle"/> over a fake game flow and connection: the session shut down as a
/// transition ends in the menu, a lost connection taking the game back to the menu once no transition runs, and the
/// <see cref="MenuNotice"/> it leaves a joined client (Docs/UiRevamp.md, Phase 9): lost while joining before its player
/// stands in a world, the host gone after, each naming the host's address; none in the idle menu or for a host.
/// </summary>
public class NetworkSessionLifecycleTests
{
    private NetworkSettings settings;
    private FakeGameFlow flow;
    private FakeSessionConnection connection;
    private MenuNotice notice;
    private NetworkSessionLifecycle lifecycle;

    /// <summary>
    /// Starts a lifecycle over a fake flow, in the menu, and a fake connection.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        settings = ScriptableObject.CreateInstance<NetworkSettings>();
        flow = new FakeGameFlow();
        connection = new FakeSessionConnection(settings);
        notice = new MenuNotice();
        lifecycle = new NetworkSessionLifecycle(connection, flow, notice);
        lifecycle.Start();
    }

    /// <summary>
    /// Stops the lifecycle and destroys the settings.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        lifecycle.Dispose();
        Object.DestroyImmediate(settings);
    }

    /// <summary>
    /// A joined client losing its host in game is taken back to the menu with the host-gone notice naming the host.
    /// </summary>
    [Test]
    public void LosingTheHostInGame_LeavesTheHostGoneNotice()
    {
        Join("100.101.102.103");
        InGame();

        ExpectWarning();
        connection.LoseConnection();

        Assert.That(notice.TryTake(out MenuNoticeKind kind, out string address), Is.True);
        Assert.That(kind, Is.EqualTo(MenuNoticeKind.HostGone));
        Assert.That(address, Is.EqualTo("100.101.102.103:7777"));
        Assert.That(flow.MenusShown, Is.EqualTo(1));
        Assert.That(notice.TryTake(out _, out _), Is.False, "The notice is taken once.");
    }

    /// <summary>
    /// Losing the host on a trip between worlds is the host gone too, the menu shown once the trip ends.
    /// </summary>
    [Test]
    public void LosingTheHostOnATrip_LeavesTheHostGoneNotice()
    {
        Join("192.168.1.20");
        InGame();
        flow.IsTransitioning = true;
        flow.IsTravelling = true;

        ExpectWarning();
        connection.LoseConnection();

        Assert.That(notice.Kind, Is.EqualTo(MenuNoticeKind.HostGone));
        Assert.That(flow.MenusShown, Is.Zero, "The menu waits for the trip to end.");
        flow.IsTransitioning = false;
        flow.FinishTransition();
        Assert.That(flow.MenusShown, Is.EqualTo(1));
    }

    /// <summary>
    /// Losing the host while the joined game's transition still runs, the player not standing in a world yet, leaves
    /// the lost-while-joining notice, and the menu shows once that transition ends.
    /// </summary>
    [Test]
    public void LosingTheHostWhileJoining_LeavesTheLostWhileJoiningNotice()
    {
        Join("192.168.1.20");
        flow.IsInMenu = false;
        flow.IsTransitioning = true;

        ExpectWarning();
        connection.LoseConnection();

        Assert.That(notice.TryTake(out MenuNoticeKind kind, out string address), Is.True);
        Assert.That(kind, Is.EqualTo(MenuNoticeKind.LostWhileJoining));
        Assert.That(address, Is.EqualTo("192.168.1.20:7777"));
        Assert.That(flow.MenusShown, Is.Zero);
        flow.IsTransitioning = false;
        flow.FinishTransition();
        Assert.That(flow.MenusShown, Is.EqualTo(1));
    }

    /// <summary>
    /// A connection lost in the idle menu (a join that never started its game) only ends the session, with no notice.
    /// </summary>
    [Test]
    public void LosingTheConnectionInTheIdleMenu_OnlyEndsTheSession()
    {
        Join("192.168.1.20");

        connection.LoseConnection();

        Assert.That(notice.Kind, Is.EqualTo(MenuNoticeKind.None));
        Assert.That(connection.Shutdowns, Is.EqualTo(1));
        Assert.That(flow.MenusShown, Is.Zero);
    }

    /// <summary>
    /// A host losing its own connection returns to the menu without a notice, which is a joined client's.
    /// </summary>
    [Test]
    public void AHostLosingItsConnection_LeavesNoNotice()
    {
        connection.StartHost();
        InGame();

        ExpectWarning();
        connection.LoseConnection();

        Assert.That(notice.Kind, Is.EqualTo(MenuNoticeKind.None));
        Assert.That(flow.MenusShown, Is.EqualTo(1));
    }

    /// <summary>
    /// A transition ending in the menu shuts the session down.
    /// </summary>
    [Test]
    public void ATransitionEndingInTheMenu_ShutsTheSessionDown()
    {
        connection.StartHost();

        flow.FinishTransition();

        Assert.That(connection.Shutdowns, Is.EqualTo(1));
        Assert.That(connection.IsActive, Is.False);
    }

    /// <summary>
    /// Joins <paramref name="host"/> on the default port through the fake connection.
    /// </summary>
    private void Join(string host)
    {
        connection.JoinAsync(host, settings.Port, settings.ConnectTimeoutSeconds, CancellationToken.None);
        connection.CompleteJoin(true);
    }

    /// <summary>
    /// Puts the flow in game, out of the menu, no transition running.
    /// </summary>
    private void InGame()
    {
        flow.IsInMenu = false;
        flow.IsInGame = true;
        flow.IsTransitioning = false;
    }

    /// <summary>
    /// Expects the lifecycle's warning as it returns to the menu.
    /// </summary>
    private static void ExpectWarning()
    {
        UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, "Lost the connection to the session; returning to the main menu.");
    }
}
