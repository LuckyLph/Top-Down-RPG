using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Tests for trips between worlds on one machine: <see cref="LocalTripAuthority"/>'s checks (the exit in the player's
/// world, the player at it, a listed target, the target's entry with the default as a fallback) and what an approved
/// trip does (the arrival held for streaming, the player put in the target world, the approval raised);
/// <see cref="WorldExits"/>' lookups; <see cref="ExitTriggerGuard"/>'s protection against firing again;
/// <see cref="TripArrivals"/> streaming an arrival until the player reaches it; the time freeze leaving trips alone;
/// <see cref="NetworkTripAuthority"/> approving on the host and sending a client's request to it; and the players'
/// trip counts.
/// </summary>
public class WorldTripTests
{
    private readonly List<Object> created = new();
    private WorldDefinition home;
    private WorldDefinition away;
    private WorldDefinition unlisted;
    private PlayerRegistry registry;
    private PlayerWorlds playerWorlds;
    private WorldExits exits;
    private TripArrivals arrivals;
    private LocalTripAuthority authority;
    private PlayerHandle player;

    /// <summary>
    /// Builds a home world at (2048, 0) and a listed away world at (4096, 0), each of one chunk with two entries, a
    /// third world at (6144, 0) the game scenes do not list, and one player standing in the home world.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        home = MakeWorld("Home", new Vector2Int(2048, 0), new WorldDefinition.Entry("start", new Vector2(10.5f, 10.5f), true), new WorldDefinition.Entry("back", new Vector2(20.5f, 10.5f), false));
        away = MakeWorld("Away", new Vector2Int(4096, 0), new WorldDefinition.Entry("start", new Vector2(32f, 32f), true), new WorldDefinition.Entry("door", new Vector2(40.5f, 40.5f), false));
        unlisted = MakeWorld("Unlisted", new Vector2Int(6144, 0), new WorldDefinition.Entry("start", new Vector2(5.5f, 5.5f), true));
        GameScenes gameScenes = Track(ScriptableObject.CreateInstance<GameScenes>());
        SerializedObject data = new(gameScenes);
        SerializedProperty listed = data.FindProperty("worlds");
        listed.arraySize = 2;
        listed.GetArrayElementAtIndex(0).objectReferenceValue = home;
        listed.GetArrayElementAtIndex(1).objectReferenceValue = away;
        data.ApplyModifiedPropertiesWithoutUndo();

        registry = new PlayerRegistry();
        playerWorlds = new PlayerWorlds(registry);
        exits = new WorldExits(new Worlds());
        arrivals = new TripArrivals();
        authority = new LocalTripAuthority(exits, playerWorlds, arrivals, gameScenes);
        player = new PlayerHandle(Track(new GameObject("Player")).transform, null);
        registry.Add(player);
        playerWorlds.Set(player, home);
    }

    /// <summary>
    /// Destroys what the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        exits.Dispose();
        playerWorlds.Dispose();
        for (int i = created.Count - 1; i >= 0; i--)
        {
            if (created[i] != null)
            {
                Object.DestroyImmediate(created[i]);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// A player standing on an exit of its own world that leads to a listed world's entry is approved: the arrival
    /// at that entry is held for streaming, the player is in the target world, and the approval names the entry and
    /// where it is.
    /// </summary>
    [Test]
    public void ARequestAtTheExit_IsApproved_PreparesTheTargetAndPutsThePlayerInIt()
    {
        WorldExit exit = PlaceExit(home, new Vector2Int(30, 30), away, "door", new Vector2Int(1, 2));
        Stand(home, 30.5f, 31.5f);
        TripApproval? raised = null;
        authority.Approved += approval => raised = approval;

        authority.Request(new TripRequest(player, exit.Id));

        Assert.That(raised.HasValue, Is.True);
        Assert.That(raised.Value.Player, Is.SameAs(player));
        Assert.That(raised.Value.World, Is.SameAs(away));
        Assert.That(raised.Value.EntryName, Is.EqualTo("door"));
        Assert.That(raised.Value.Position, Is.EqualTo(new Vector2(4096f + 40.5f, 40.5f)));
        Assert.That(playerWorlds.Get(player), Is.SameAs(away), "The player is in the target world once approved.");
        Assert.That(arrivals.IsHeld(player), Is.True, "The arrival streams before the player moves.");
        List<StreamInterest> interests = new();
        arrivals.AddInterests(interests, playerWorlds);
        Assert.That(interests.Count, Is.EqualTo(1));
        Assert.That(interests[0].World, Is.SameAs(away));
        Assert.That(interests[0].Position, Is.EqualTo(raised.Value.Position));
    }

    /// <summary>
    /// An exit of another world than the player's is not one it can take, even when the player's position would be on
    /// it; nothing changes.
    /// </summary>
    [Test]
    public void AnExitInAnotherWorld_IsRefused()
    {
        WorldExit exit = PlaceExit(away, new Vector2Int(30, 30), home, "start", Vector2Int.one);
        Stand(home, 30.5f, 30.5f);

        Assert.That(authority.TryApprove(new TripRequest(player, exit.Id), out _, out string refusal), Is.False);
        Assert.That(refusal, Does.Contain("Home"));
        AssertNothingChanged();
    }

    /// <summary>
    /// A player more than a cell from the exit's cells is refused; one cell away, within the reach, it goes.
    /// </summary>
    [Test]
    public void APlayerAwayFromTheExit_IsRefused_OneCellAwayGoes()
    {
        WorldExit exit = PlaceExit(home, new Vector2Int(30, 30), away, "start", Vector2Int.one);
        Stand(home, 35.5f, 30.5f);

        Assert.That(authority.TryApprove(new TripRequest(player, exit.Id), out _, out string refusal), Is.False);
        Assert.That(refusal, Does.Contain("not at the exit"));
        AssertNothingChanged();

        Stand(home, 31.5f, 31.5f);
        Assert.That(authority.TryApprove(new TripRequest(player, exit.Id), out _, out _), Is.True);
    }

    /// <summary>
    /// An exit leading to a world the game scenes do not list, or to no world, is refused.
    /// </summary>
    [Test]
    public void AnExitToAnUnlistedWorld_IsRefused()
    {
        WorldExit exit = PlaceExit(home, new Vector2Int(30, 30), unlisted, "start", Vector2Int.one);
        WorldExit nowhere = PlaceExit(home, new Vector2Int(31, 30), null, null, Vector2Int.one);
        Stand(home, 30.5f, 30.5f);

        Assert.That(authority.TryApprove(new TripRequest(player, exit.Id), out _, out string refusal), Is.False);
        Assert.That(refusal, Does.Contain("Unlisted").And.Contain("does not list"));
        Assert.That(authority.TryApprove(new TripRequest(player, nowhere.Id), out _, out _), Is.False);
        AssertNothingChanged();
    }

    /// <summary>
    /// An exit naming an entry its target does not have takes the player to the target's default entry, with a
    /// warning; an empty entry name means the default entry.
    /// </summary>
    [Test]
    public void AnUnknownEntry_FallsBackToTheDefault_WithAWarning()
    {
        WorldExit exit = PlaceExit(home, new Vector2Int(30, 30), away, "cellar", Vector2Int.one);
        Stand(home, 30.5f, 30.5f);
        LogAssert.Expect(LogType.Warning, "Away has no entry 'cellar'; using 'start' instead.");

        Assert.That(authority.TryApprove(new TripRequest(player, exit.Id), out TripApproval approval, out _), Is.True);
        Assert.That(approval.EntryName, Is.EqualTo("start"));
        Assert.That(approval.Position, Is.EqualTo(new Vector2(4096f + 32f, 32f)));

        WorldExit plain = PlaceExit(away, new Vector2Int(10, 10), home, string.Empty, Vector2Int.one);
        arrivals.Release(player);
        Stand(away, 10.5f, 10.5f);
        Assert.That(authority.TryApprove(new TripRequest(player, plain.Id), out approval, out _), Is.True);
        Assert.That(approval.EntryName, Is.EqualTo("start"));
        Assert.That(approval.World, Is.SameAs(home));
    }

    /// <summary>
    /// A player whose trip is under way cannot ask for another; an unknown exit id is refused, and a refused request
    /// logs why and raises nothing.
    /// </summary>
    [Test]
    public void ASecondRequestWhileTravelling_AndAnUnknownExit_AreRefused()
    {
        WorldExit exit = PlaceExit(home, new Vector2Int(30, 30), home, "back", Vector2Int.one);
        Stand(home, 30.5f, 30.5f);
        Assert.That(authority.TryApprove(new TripRequest(player, exit.Id), out _, out _), Is.True);
        Assert.That(authority.TryApprove(new TripRequest(player, exit.Id), out _, out string refusal), Is.False);
        Assert.That(refusal, Does.Contain("already travelling"));

        arrivals.Release(player);
        bool raised = false;
        authority.Approved += _ => raised = true;
        LogAssert.Expect(LogType.Warning, "Trip refused: no exit 'missing' is loaded in Home, the player's world.");
        authority.Request(new TripRequest(player, "missing"));
        Assert.That(raised, Is.False);
    }

    /// <summary>
    /// The registry finds a loaded exit by its id within its own world only, and by any cell it covers; a chunk
    /// unloading takes its exits with it.
    /// </summary>
    [Test]
    public void WorldExits_FindExitsByIdAndCell_InTheirOwnWorld()
    {
        WorldExit exit = PlaceExit(home, new Vector2Int(30, 30), away, "start", new Vector2Int(2, 1));

        Assert.That(exits.TryFind(home, exit.Id, out WorldExit found), Is.True);
        Assert.That(found, Is.SameAs(exit));
        Assert.That(exits.TryFind(away, exit.Id, out _), Is.False);
        Assert.That(exits.TryFindAt(home, new Vector2Int(31, 30), out found), Is.True);
        Assert.That(exits.TryFindAt(home, new Vector2Int(32, 30), out _), Is.False);
        Assert.That(exits.TryFindAt(home, new Vector2Int(30, 31), out _), Is.False);
        Assert.That(exit.CellsIn(home), Is.EqualTo(new RectInt(30, 30, 2, 1)));
    }

    /// <summary>
    /// The guard starts unarmed, so a player that appears on an exit must step off first; it fires once on stepping
    /// onto an exit from a cell that is none, not again while the player stays on exits, and a trip disarms it.
    /// </summary>
    [Test]
    public void TheGuard_FiresOncePerStepOntoAnExit_AndNeverRightAfterATrip()
    {
        ExitTriggerGuard guard = new();
        Assert.That(guard.Step(true), Is.False, "Appearing on an exit fires nothing.");
        Assert.That(guard.Step(false), Is.False);
        Assert.That(guard.Step(true), Is.True, "Stepping onto an exit fires it.");
        Assert.That(guard.Step(true), Is.False, "Walking along the exit fires nothing more.");
        Assert.That(guard.Step(false), Is.False);
        Assert.That(guard.Step(true), Is.True, "Stepping off and back on fires again.");

        guard.Step(false);
        guard.Disarm();
        Assert.That(guard.Step(true), Is.False, "Right after a trip nothing fires.");
        guard.Step(false);
        Assert.That(guard.IsArmed, Is.True);
    }

    /// <summary>
    /// An arrival streams as an interest until the player stands within two cells of the entry in that world, then is
    /// dropped; it also goes when the player does.
    /// </summary>
    [Test]
    public void Arrivals_StreamUntilThePlayerReachesTheEntry()
    {
        Vector2 entry = away.ToWorld(new Vector2(32f, 32f));
        arrivals.Hold(player, new StreamInterest(away, entry));
        List<StreamInterest> interests = new();
        playerWorlds.Set(player, away);
        Stand(home, 30.5f, 30.5f);

        arrivals.AddInterests(interests, playerWorlds);
        Assert.That(interests.Count, Is.EqualTo(1), "Put in the world but not moved there yet.");

        player.Transform.position = entry + new Vector2(1.5f, -1.5f);
        interests.Clear();
        arrivals.AddInterests(interests, playerWorlds);
        Assert.That(interests, Is.Empty);
        Assert.That(arrivals.IsHeld(player), Is.False);

        arrivals.Hold(player, new StreamInterest(away, entry));
        Object.DestroyImmediate(player.Transform.gameObject);
        arrivals.AddInterests(interests, playerWorlds);
        Assert.That(arrivals.Count, Is.Zero, "A player that is gone arrives nowhere.");
    }

    /// <summary>
    /// Game time stops for every transition but a trip between worlds.
    /// </summary>
    [Test]
    public void TheTimeFreeze_HoldsForTransitions_ButNotForTrips()
    {
        Assert.That(TransitionTimeFreeze.Freezes(true, false), Is.True);
        Assert.That(TransitionTimeFreeze.Freezes(true, true), Is.False);
        Assert.That(TransitionTimeFreeze.Freezes(false, false), Is.False);
    }

    /// <summary>
    /// Offline or on the host the network authority checks a request here: the player is in the target world for its
    /// first trip, its trip leads to the resolved entry, and the approval is raised for this machine.
    /// </summary>
    [Test]
    public void NetworkAuthority_OnTheHost_ApprovesHere_CountingTheTrip()
    {
        WorldExit exit = PlaceExit(home, new Vector2Int(30, 30), away, "door", Vector2Int.one);
        Stand(home, 30.5f, 30.5f);
        NetworkTripAuthority network = new(authority, new FakeSessionStatus { IsActive = true, IsHost = true }, arrivals);
        TripApproval? raised = null;
        network.Approved += approval => raised = approval;

        network.Request(new TripRequest(player, exit.Id));

        Assert.That(raised.HasValue, Is.True);
        Assert.That(network.SendsToHost, Is.False);
        Assert.That(playerWorlds.Get(player), Is.SameAs(away));
        Assert.That(playerWorlds.TripOf(player), Is.EqualTo(1), "Each approved trip counts.");
        Assert.That(playerWorlds.EntryOf(player), Is.EqualTo("door"));
    }

    /// <summary>
    /// A client's request names where its own machine has the player, and the host checks that position rather than
    /// its lagging copy: at the exit there it goes even with the copy far away, and away from it there it is refused
    /// even with the copy on the exit, with a warning.
    /// </summary>
    [Test]
    public void NetworkAuthority_ChecksTheClientsOwnPosition_NotTheHostsCopy()
    {
        WorldExit exit = PlaceExit(home, new Vector2Int(30, 30), away, "start", Vector2Int.one);
        NetworkTripAuthority network = new(authority, new FakeSessionStatus { IsActive = true, IsHost = true }, arrivals);

        Stand(home, 30.5f, 30.5f);
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Trip refused: .*not at the exit"));
        Assert.That(network.Approve(new TripRequest(player, exit.Id, home.ToWorld(new Vector2(40.5f, 30.5f)))), Is.False);
        AssertNothingChanged();

        Stand(home, 50.5f, 50.5f);
        Assert.That(network.Approve(new TripRequest(player, exit.Id, home.ToWorld(new Vector2(30.5f, 31.5f)))), Is.True);
        Assert.That(playerWorlds.Get(player), Is.SameAs(away));
    }

    /// <summary>
    /// On a client a request goes to the host through the player's link, with the player's own position, and changes
    /// nothing here; the host's answer, delivered, holds the arrival and raises the approval. A player without a link
    /// sends nothing, with a warning.
    /// </summary>
    [Test]
    public void NetworkAuthority_OnAClient_SendsThroughTheLink_AndActsOnTheDeliveredAnswer()
    {
        WorldExit exit = PlaceExit(home, new Vector2Int(30, 30), away, "door", Vector2Int.one);
        Stand(home, 30.5f, 30.5f);
        NetworkTripAuthority network = new(authority, new FakeSessionStatus { IsActive = true, IsHost = false }, arrivals);
        int raised = 0;
        network.Approved += _ => raised++;

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no ITripLink"));
        network.Request(new TripRequest(player, exit.Id));
        FakeTripLink link = new();
        network.LinkFinder = body => body == player.Transform ? link : null;
        network.Request(new TripRequest(player, exit.Id));

        Assert.That(network.SendsToHost, Is.True);
        Assert.That(link.Sent.Count, Is.EqualTo(1));
        Assert.That(link.Sent[0].ExitId, Is.EqualTo(exit.Id));
        Assert.That(link.Sent[0].Position, Is.EqualTo((Vector2)player.Transform.position));
        Assert.That(raised, Is.Zero, "Nothing goes before the host answers.");
        AssertNothingChanged();

        Vector2 entry = away.ToWorld(new Vector2(40.5f, 40.5f));
        playerWorlds.Apply(player, away, 1, "door");
        network.Deliver(new TripApproval(player, away, "door", entry));

        Assert.That(raised, Is.EqualTo(1));
        Assert.That(arrivals.IsHeld(player), Is.True, "The client streams the target world while it fades out.");
    }

    /// <summary>
    /// The players' worlds count trips: a trip raises the change even back into the same world, applying another
    /// machine's state raises it only when the world or the trip count changes, and leaving every world forgets the
    /// trips.
    /// </summary>
    [Test]
    public void PlayerWorlds_CountTrips()
    {
        int changes = 0;
        playerWorlds.Changed += (_, _) => changes++;

        playerWorlds.Travel(player, home, "back");
        Assert.That(changes, Is.EqualTo(1), "A trip back into the same world still counts.");
        Assert.That(playerWorlds.TripOf(player), Is.EqualTo(1));
        Assert.That(playerWorlds.EntryOf(player), Is.EqualTo("back"));

        playerWorlds.Set(player, away);
        Assert.That(playerWorlds.TripOf(player), Is.EqualTo(1), "Moving a player without a trip keeps its count.");
        playerWorlds.Apply(player, away, 1, "back");
        Assert.That(changes, Is.EqualTo(2), "Applying the same world and trip again raises nothing.");
        playerWorlds.Apply(player, away, 2, "door");
        Assert.That(changes, Is.EqualTo(3));
        Assert.That(playerWorlds.EntryOf(player), Is.EqualTo("door"));

        playerWorlds.Set(player, null);
        Assert.That(playerWorlds.TripOf(player), Is.Zero);
        Assert.That(playerWorlds.EntryOf(player), Is.Empty);
    }

    /// <summary>
    /// An <see cref="ITripLink"/> that records the requests a client would send the host instead of sending an RPC.
    /// </summary>
    private sealed class FakeTripLink : ITripLink
    {
        public List<(string ExitId, Vector2 Position)> Sent { get; } = new();

        /// <summary>
        /// Records the request.
        /// </summary>
        public void SendTripRequest(string exitId, Vector2 position)
        {
            Sent.Add((exitId, position));
        }
    }

    private void AssertNothingChanged()
    {
        Assert.That(playerWorlds.Get(player), Is.SameAs(home));
        Assert.That(arrivals.IsHeld(player), Is.False);
    }

    /// <summary>
    /// Puts the player at (<paramref name="x"/>, <paramref name="y"/>) from <paramref name="world"/>'s origin.
    /// </summary>
    private void Stand(WorldDefinition world, float x, float y)
    {
        player.Transform.position = world.ToWorld(new Vector2(x, y));
    }

    /// <summary>
    /// An exit on <paramref name="world"/>'s cell <paramref name="cell"/>, with a persistent id, leading to
    /// <paramref name="target"/>'s entry <paramref name="entry"/>, recorded as loaded in its chunk.
    /// </summary>
    private WorldExit PlaceExit(WorldDefinition world, Vector2Int cell, WorldDefinition target, string entry, Vector2Int size)
    {
        GameObject exitObject = Track(new GameObject($"Exit {cell}"));
        exitObject.transform.position = world.ToWorld(cell + Vector2.one * 0.5f);
        WorldExit exit = exitObject.AddComponent<WorldExit>();
        PersistentIds.SetId(exitObject.GetComponent<PersistentId>(), PersistentIds.NewId());
        WorldAuthoring.ConfigureExit(exit, target, entry, size);
        exits.Add(world, ChunkCoords.ChunkOf(cell), exit);
        return exit;
    }

    private WorldDefinition MakeWorld(string worldName, Vector2Int origin, params WorldDefinition.Entry[] entries)
    {
        WorldDefinition world = Track(WorldDefinition.Create(worldName, origin, Vector2Int.one, new[] { Vector2Int.zero }, entries));
        Track(world.RootScene);
        foreach (WorldDefinition.Chunk chunk in world.Chunks)
        {
            Track(chunk.Scene);
        }

        return world;
    }

    /// <summary>
    /// Records <paramref name="target"/> for tear down and returns it.
    /// </summary>
    private T Track<T>(T target) where T : Object
    {
        created.Add(target);
        return target;
    }
}
