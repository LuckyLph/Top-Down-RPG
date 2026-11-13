using System.Threading;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for <see cref="MenuSessionStarter"/> (Docs/UiRevamp.md, Phase 9) over a fake game flow and network connection:
/// a new game, Continue and hosting, playing and hosting a given save (Phase 10), each start refused while a transition
/// runs or a join is in progress; a join that
/// connects, remembered first among the recent hosts with the joining line shown; each failure's sentence; and a join
/// cancelled.
/// </summary>
public class MenuSessionStarterTests
{
    private MenuTestRig rig;

    /// <summary>
    /// Builds the rig.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        rig = new MenuTestRig();
    }

    /// <summary>
    /// Destroys the rig.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        rig.Dispose();
    }

    /// <summary>
    /// A new game starts a new session at the starting world's entry, offline.
    /// </summary>
    [Test]
    public void StartNew_StartsANewSessionAtTheStartingEntry()
    {
        Assert.That(rig.Starter.StartNew(), Is.Null);

        Assert.That(rig.Flow.WorldStarts, Is.EqualTo(1));
        Assert.That(rig.Flow.StartedWorld, Is.SameAs(rig.Home));
        Assert.That(rig.Flow.StartedPosition, Is.EqualTo(rig.Home.EntryPosition("start")));
        Assert.That(rig.Connection.HostsStarted, Is.Zero);
    }

    /// <summary>
    /// Continue without a save says so and starts nothing; with one it starts at the local player's saved place.
    /// </summary>
    [Test]
    public void Continue_StartsAtTheSavedPlace_OrSaysThereIsNoSave()
    {
        Assert.That(rig.Starter.HasSave, Is.False);
        Assert.That(rig.Starter.Continue(), Is.EqualTo(MenuSessionStarter.NoSave));
        Assert.That(rig.Flow.WorldStarts, Is.Zero);

        rig.WriteSave(MenuTestRig.NearNorth, 600d);
        Assert.That(rig.Starter.HasSave, Is.True);
        Assert.That(rig.Starter.Continue(), Is.Null);
        Assert.That(rig.Flow.StartedPosition, Is.EqualTo(rig.Home.ToWorld(MenuTestRig.NearNorth)));
        Assert.That(rig.SessionSave.Id, Is.EqualTo("saved-session"));
    }

    /// <summary>
    /// Hosting starts the host, then continues the latest save when asked to and there is one, else a new session; a
    /// host that cannot start says what to do, naming the port, and starts nothing.
    /// </summary>
    [Test]
    public void Host_ContinuesOrStartsNew_AndAFailedHostSaysWhatToDo()
    {
        rig.WriteSave(MenuTestRig.NearNorth, 600d);
        Assert.That(rig.Starter.Host(true), Is.Null);
        Assert.That(rig.Connection.HostsStarted, Is.EqualTo(1));
        Assert.That(rig.Flow.StartedPosition, Is.EqualTo(rig.Home.ToWorld(MenuTestRig.NearNorth)), "Host and continue continues the save.");

        rig.Connection.Shutdown();
        Assert.That(rig.Starter.Host(false), Is.Null);
        Assert.That(rig.SessionSave.Id, Is.Not.EqualTo("saved-session"), "Host a new game starts a session of its own.");
        Assert.That(rig.Flow.StartedPosition, Is.EqualTo(rig.Home.EntryPosition("start")));

        rig.Connection.Shutdown();
        rig.Connection.HostStarts = false;
        LogAssertIgnoringError(() => Assert.That(rig.Starter.Host(true), Is.EqualTo("Could not host on port 7777. Close any other copy of the game and try again.")));
        Assert.That(rig.Flow.WorldStarts, Is.EqualTo(2), "Nothing starts when hosting failed.");
    }

    /// <summary>
    /// Play and Host take the save they are given, not the latest (Docs/UiRevamp.md, Phase 10): Play starts it offline
    /// at the local player's saved place under its own id, Host hosts it; a joined client's copy, its host gone or not,
    /// goes on as a session of its own; a save that cannot be read says so and starts nothing.
    /// </summary>
    [Test]
    public void PlayAndHost_StartTheSaveGiven_NotTheLatest()
    {
        string older = rig.WriteSave(MenuTestRig.NearNorth, 600d, "older");
        rig.WallClock.Advance(60d);
        rig.WriteSave(new Vector2(12f, 14f), 900d, "newer");
        rig.WallClock.Advance(60d);
        string copy = rig.WriteCopy(new Vector2(20f, 30f), 300d, "host-session", false);

        Assert.That(rig.Starter.Play(older), Is.Null);
        Assert.That(rig.SessionSave.Id, Is.EqualTo("older"));
        Assert.That(rig.Flow.StartedPosition, Is.EqualTo(rig.Home.ToWorld(MenuTestRig.NearNorth)));
        Assert.That(rig.Connection.HostsStarted, Is.Zero, "Play stays offline.");

        Assert.That(rig.Starter.Host(older), Is.Null);
        Assert.That(rig.Connection.HostsStarted, Is.EqualTo(1));
        Assert.That(rig.SessionSave.Id, Is.EqualTo("older"));
        Assert.That(rig.Flow.StartedPosition, Is.EqualTo(rig.Home.ToWorld(MenuTestRig.NearNorth)));

        rig.Connection.Shutdown();
        Assert.That(rig.Starter.Play(copy), Is.Null, "A copy whose host may still be there plays on its own.");
        Assert.That(rig.SessionSave.Id, Is.Not.Empty.And.Not.EqualTo("host-session"), "The copy goes on as a session of its own.");
        Assert.That(rig.Flow.StartedPosition, Is.EqualTo(rig.Home.ToWorld(new Vector2(20f, 30f))));

        int starts = rig.Flow.WorldStarts;
        Assert.That(rig.Starter.Play("session-gone.json"), Is.EqualTo(MenuSessionStarter.UnreadableSave));
        Assert.That(rig.Starter.Host("session-gone.json"), Is.EqualTo(MenuSessionStarter.UnreadableSave));
        Assert.That(rig.Flow.WorldStarts, Is.EqualTo(starts));
        Assert.That(rig.Connection.HostsStarted, Is.EqualTo(1), "Hosting does not start for a save that cannot be read.");
    }

    /// <summary>
    /// Nothing starts while a transition runs.
    /// </summary>
    [Test]
    public void NothingStarts_WhileATransitionRuns()
    {
        string saved = rig.WriteSave(MenuTestRig.NearNorth, 600d);
        rig.Flow.IsTransitioning = true;

        Assert.That(rig.Starter.CanStart, Is.False);
        Assert.That(rig.Starter.StartNew(), Is.Null);
        Assert.That(rig.Starter.Host(false), Is.Null);
        Assert.That(rig.Starter.Play(saved), Is.Null);
        Assert.That(rig.Starter.Host(saved), Is.Null);
        Assert.That(rig.Starter.JoinAsync(new JoinAddress("192.168.1.20", 7777), CancellationToken.None).GetAwaiter().GetResult(), Is.Null);
        Assert.That(rig.Flow.WorldStarts + rig.Connection.HostsStarted + rig.Connection.Joins, Is.Zero);
    }

    /// <summary>
    /// A join waits for the host with the connect timeout on the address's port, refusing other starts meanwhile; once
    /// connected, the host goes first among the recent hosts (alone on the default port), the joining line names it and
    /// the joined game starts.
    /// </summary>
    [Test]
    public void AJoinThatConnects_RemembersTheHost_ShowsTheJoiningLine_AndStartsTheJoinedGame()
    {
        rig.Settings.AddRecentHost("10.0.0.9");
        Awaitable<string> join = rig.Starter.JoinAsync(new JoinAddress("192.168.1.20", 7777), CancellationToken.None);

        Assert.That(rig.Starter.IsJoining, Is.True);
        Assert.That(rig.Connection.JoinHost, Is.EqualTo("192.168.1.20"));
        Assert.That(rig.Connection.JoinPort, Is.EqualTo(7777));
        Assert.That(rig.Connection.JoinTimeout, Is.EqualTo(10f));
        Assert.That(rig.Starter.StartNew(), Is.Null);
        Assert.That(rig.Flow.WorldStarts, Is.Zero, "Nothing else starts while a join is in progress.");

        rig.Connection.CompleteJoin(true);
        Assert.That(join.GetAwaiter().IsCompleted, Is.True);
        Assert.That(join.GetAwaiter().GetResult(), Is.Null);
        Assert.That(rig.Starter.IsJoining, Is.False);
        Assert.That(rig.Settings.RecentHosts, Is.EqualTo(new[] { "192.168.1.20", "10.0.0.9" }));
        Assert.That(rig.Loading.Text, Is.EqualTo("Joining 192.168.1.20's game…"));
        Assert.That(rig.Flow.JoinedStarts, Is.EqualTo(1));

        rig.Flow.FinishTransition();
        Assert.That(rig.Loading.Text, Is.Empty, "The joining line goes as the transition finishes.");
    }

    /// <summary>
    /// A host on another port is remembered with its port.
    /// </summary>
    [Test]
    public void AHostOnAnotherPort_IsRememberedWithItsPort()
    {
        Awaitable<string> join = rig.Starter.JoinAsync(new JoinAddress("ana-desktop", 7778), CancellationToken.None);
        rig.Connection.CompleteJoin(true);

        Assert.That(join.GetAwaiter().GetResult(), Is.Null);
        Assert.That(rig.Connection.JoinPort, Is.EqualTo(7778));
        Assert.That(rig.Settings.RecentHosts, Is.EqualTo(new[] { "ana-desktop:7778" }));
    }

    /// <summary>
    /// Each failed join says what happened and what to do: a host that never answered, a full party, another build, or
    /// a refusal of another kind; nothing is remembered or started.
    /// </summary>
    [TestCase(null, "Could not reach 192.168.1.20:7777 in 10 s. Check the address, and that the host is in a game.")]
    [TestCase("full:4", "192.168.1.20:7777 is full: 4 of 4 players.")]
    [TestCase("something else", "192.168.1.20:7777 did not let you in.")]
    public void AFailedJoin_SaysWhatHappenedAndWhatToDo(string refusal, string message)
    {
        Awaitable<string> join = rig.Starter.JoinAsync(new JoinAddress("192.168.1.20", 7777), CancellationToken.None);
        rig.Connection.CompleteJoin(false, refusal);

        Assert.That(join.GetAwaiter().GetResult(), Is.EqualTo(message));
        Assert.That(rig.Starter.IsJoining, Is.False);
        Assert.That(rig.Settings.RecentHosts, Is.Empty);
        Assert.That(rig.Flow.JoinedStarts, Is.Zero);
        Assert.That(rig.Loading.Text, Is.Empty);
    }

    /// <summary>
    /// A host running another build names both versions.
    /// </summary>
    [Test]
    public void AnotherBuild_NamesBothVersions()
    {
        Awaitable<string> join = rig.Starter.JoinAsync(new JoinAddress("192.168.1.20", 7777), CancellationToken.None);
        rig.Connection.CompleteJoin(false, JoinApproval.OtherBuild("0.0.9-prealpha"));

        Assert.That(join.GetAwaiter().GetResult(), Is.EqualTo($"The host runs 0.0.9-prealpha and you run {Application.version}. Play together needs the same build."));
    }

    /// <summary>
    /// A join cancelled ends quietly: no message, nothing remembered or started, and starts are allowed again.
    /// </summary>
    [Test]
    public void ACancelledJoin_EndsQuietly()
    {
        using CancellationTokenSource cancel = new();
        Awaitable<string> join = rig.Starter.JoinAsync(new JoinAddress("192.168.1.20", 7777), cancel.Token);

        cancel.Cancel();

        Assert.That(join.GetAwaiter().IsCompleted, Is.True);
        Assert.That(join.GetAwaiter().GetResult(), Is.Null);
        Assert.That(rig.Starter.IsJoining, Is.False);
        Assert.That(rig.Starter.CanStart, Is.True);
        Assert.That(rig.Settings.RecentHosts, Is.Empty);
        Assert.That(rig.Flow.JoinedStarts, Is.Zero);
    }

    /// <summary>
    /// Runs <paramref name="act"/>, which logs an error on purpose.
    /// </summary>
    private static void LogAssertIgnoringError(System.Action act)
    {
        UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Could not start hosting"));
        act();
    }
}
