using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// PlayMode tests for waystones and respawns in Testlands (Docs/WorldStreaming.md: Death and respawn), whose default
/// entry `start` and waystone `north` lie 50 cells apart: offline, a dead player respawns after the delay at the nearest
/// waystone it discovered, its own machine travelling there; walking to a waystone discovers it, shows the player a
/// popup, and Continue keeps it; in a hosted game a client's dead player respawns at its waystone with the host
/// deciding and the client moving its own player there; a trip to a waystone protects the traveller (Docs/KnownGaps.md,
/// Phase 2): a mob by the entry does not hurt the host's player or a client's until its machine has faded in; and a
/// waystone's stone shows what each machine's own player discovered, a client's discoveries reaching it from the host,
/// earlier sessions' as it joins (Phase 3); and, under a delayed connection, a client's respawn waits for its machine to
/// report the waystone's chunk during the respawn's trip, though it held that chunk before (Phase 4).
/// </summary>
public class WaystonePlayModeTests
{
    private static readonly Vector2 North = new(30.5f, 90.5f);
    private static readonly Vector2 SouthOfNorth = new(30.5f, 85.5f);
    private static readonly Vector2 TwelveEastOfStart = new(36.5f, 40.5f);

    private InProcessClient client;
    private NetworkDelay delay;

    /// <summary>
    /// Disposes the delayed connection and the in-process client when the test started them.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        delay?.Dispose();
        delay = null;
        client?.Dispose();
        client = null;
    }

    /// <summary>
    /// Offline, a player that dies near `north` before discovering it is still dead two seconds later, then respawns at
    /// `start`, the only waystone it has; once it stood at `north`, dying near it again respawns it there, alive at full
    /// health, with no restart.
    /// </summary>
    [UnityTest]
    public IEnumerator ADeadPlayer_RespawnsAtTheNearestWaystoneItDiscovered_AfterTheDelay()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        WorldDefinition world = gameFlow.CurrentWorld;
        PlayerController player = SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current.Controller;
        Health health = player.GetComponent<Health>();
        SessionSave session = SceneBootTestHelper.ResolveFromMain<SessionSave>();
        string playerId = SceneBootTestHelper.ResolveFromMain<NetworkSession>().LocalPlayerId;

        yield return TeleportAndSettle(player, world, world.ToWorld(SouthOfNorth));
        DeactivateMobs();
        Assert.That(WaystoneRules.Contains(session.WaystonesOf(playerId), world.WorldId, "north"), Is.False, "Five cells away is too far to discover it.");
        health.ApplyDamage(health.MaxHealth);
        yield return new WaitForSeconds(2f);
        Assert.That(health.IsDead, Is.True, "The respawn waits its delay.");

        yield return SceneBootTestHelper.WaitUntil(() => !health.IsDead, "the player to respawn");
        Assert.That(Vector2.Distance(player.transform.position, world.EntryPosition("start")), Is.LessThan(1.5f), "It respawns at `start`, the only waystone it has.");
        Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
        Assert.That(gameFlow.CurrentWorld, Is.SameAs(world));
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        yield return TeleportAndSettle(player, world, world.ToWorld(North));
        yield return SceneBootTestHelper.WaitUntil(() => WaystoneRules.Contains(session.WaystonesOf(playerId), world.WorldId, "north"), "standing at `north` to discover it");

        yield return TeleportAndSettle(player, world, world.ToWorld(SouthOfNorth));
        health.ApplyDamage(health.MaxHealth);
        yield return SceneBootTestHelper.WaitUntil(() => !health.IsDead, "the player to respawn again");
        Assert.That(Vector2.Distance(player.transform.position, world.EntryPosition("north")), Is.LessThan(1.5f), "It respawns at `north`, nearer than `start`.");
        Assert.That(player.enabled, Is.True);
    }

    /// <summary>
    /// Walking the last cells to `north` discovers it: the session records it under this install's player id and the
    /// player sees the discovery text. Leaving to the menu saves it with the player's place; Continue brings it back,
    /// and a death near `north` then respawns the player there.
    /// </summary>
    [UnityTest]
    public IEnumerator WalkingToAWaystone_DiscoversIt_AndContinueKeepsIt()
    {
        MemorySaveStore saveStore = new();
        yield return SceneBootTestHelper.BootIntoMainMenu(saveStore);
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        Object.FindAnyObjectByType<MainMenuController>().StartNewGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        WorldDefinition world = gameFlow.CurrentWorld;
        PlayerController player = SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current.Controller;
        string playerId = SceneBootTestHelper.ResolveFromMain<NetworkSession>().LocalPlayerId;
        List<string> told = new();
        SceneBootTestHelper.ResolveFromGameplay<WaystoneNotices>().Discovered += (who, entry) => told.Add(entry);
        GameplaySettings settings = SceneBootTestHelper.ResolveFromGameplay<GameplaySettings>();

        yield return TeleportAndSettle(player, world, world.ToWorld(SouthOfNorth));
        DeactivateMobs();
        Assert.That(player.GetComponent<PlayerMotor2D>().MoveTo(world.ToWorld(North)), Is.True);
        SessionSave session = SceneBootTestHelper.ResolveFromMain<SessionSave>();
        yield return SceneBootTestHelper.WaitUntil(() => WaystoneRules.Contains(session.WaystonesOf(playerId), world.WorldId, "north"), "walking to `north` to discover it");
        Assert.That(told, Is.EqualTo(new[] { "north" }));
        Assert.That(ShowsText(settings.WaystoneDiscoveredText), Is.True, "The player sees the discovery.");

        gameFlow.ShowMainMenuAsync();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(GameSaveData.TryParse(saveStore.Contents, out GameSaveData data), Is.True);
        GameSaveData.Player saved = null;
        foreach (GameSaveData.Player candidate in data.Players)
        {
            saved = candidate.PlayerId == playerId ? candidate : saved;
        }

        Assert.That(saved, Is.Not.Null);
        Assert.That(WaystoneRules.Contains(saved.Waystones, world.WorldId, "north"), Is.True, "The save keeps the discovery under the player id.");

        Object.FindAnyObjectByType<MainMenuController>().ContinueGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        session = SceneBootTestHelper.ResolveFromMain<SessionSave>();
        Assert.That(WaystoneRules.Contains(session.WaystonesOf(playerId), world.WorldId, "north"), Is.True, "Continue keeps the discovery.");
        player = SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current.Controller;
        Health health = player.GetComponent<Health>();

        yield return TeleportAndSettle(player, world, world.ToWorld(SouthOfNorth));
        health.ApplyDamage(health.MaxHealth);
        yield return SceneBootTestHelper.WaitUntil(() => !health.IsDead, "the continued player to respawn");
        Assert.That(Vector2.Distance(player.transform.position, world.EntryPosition("north")), Is.LessThan(1.5f));
    }

    /// <summary>
    /// Hosting Testlands with an in-process client: the client's player dies away from `start`; after the delay the
    /// host sends it on a trip within Testlands to `start` (one more trip, the host fading nothing), which reaches the
    /// client as its approval; the player stays dead until the client moves it there and reports the entry's chunk for
    /// that trip, when the host restores it and the client's copy comes back too.
    /// </summary>
    [UnityTest]
    public IEnumerator AClientsDeadPlayer_RespawnsAtItsWaystone_TheHostDecidingAndTheClientMoving()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        WorldDefinition world = gameFlow.CurrentWorld;
        PlayerRegistry hostPlayers = SceneBootTestHelper.ResolveFromGameplay<PlayerRegistry>();
        PlayerWorlds hostWorlds = SceneBootTestHelper.ResolveFromGameplay<PlayerWorlds>();
        PlayerController hostPlayer = SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current.Controller;

        client = InProcessClient.Start(session, SceneBootTestHelper.ResolveFromGameplay<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendJoined();
        yield return SceneBootTestHelper.WaitUntil(
            () => client.LocalPlayer.Current != null && client.Players.Players.Count == 2 && hostPlayers.Players.Count == 2,
            "both sides to see both players");
        client.DisableColliders();
        DeactivateMobs();
        LocalPlayer clientLocal = client.LocalPlayer.Current;
        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(hostPlayers, client.Manager.LocalClientId);
        PlayerHandle hostCopyHandle = HandleOf(hostPlayers, hostCopy);
        Vector2 away = world.ToWorld(new Vector2(32.5f, 40.5f));
        clientLocal.Controller.Teleport(away);
        yield return SceneBootTestHelper.WaitUntil(() => Vector2.Distance(hostCopy.transform.position, away) < 0.1f, "the host to see the client's player move");

        NetworkTripAuthority clientTrips = client.Resolve<NetworkTripAuthority>();
        List<TripApproval> delivered = new();
        clientTrips.Approved += delivered.Add;
        int tripBefore = hostWorlds.TripOf(hostCopyHandle);
        Vector2 hostPlayerAt = hostPlayer.transform.position;
        SceneBootTestHelper.ResolveFromGameplay<DamageService>().ApplyDamage(hostCopy.GetComponent<DamageReceiver>(), hostCopy.GetComponent<Health>().MaxHealth);
        Assert.That(hostCopy.GetComponent<Health>().IsDead, Is.True);

        yield return SceneBootTestHelper.WaitUntil(() => delivered.Count == 1, "the host's respawn trip to reach the client");
        Assert.That(delivered[0].World, Is.SameAs(world), "A respawn is a trip within the player's world.");
        Assert.That(delivered[0].EntryName, Is.EqualTo("start"));
        Assert.That(delivered[0].Position, Is.EqualTo(world.EntryPosition("start")));
        Assert.That(hostWorlds.TripOf(hostCopyHandle), Is.EqualTo(tripBefore + 1));
        Assert.That(SceneBootTestHelper.ResolveFromGameplay<TripArrivals>().IsHeld(hostCopyHandle), Is.True, "The host streams the waystone.");
        Assert.That(gameFlow.IsTransitioning, Is.False, "The host fades for nobody's respawn but its own.");
        Assert.That((Vector2)hostPlayer.transform.position, Is.EqualTo(hostPlayerAt));
        yield return null;
        Assert.That(hostCopy.GetComponent<Health>().IsDead, Is.True, "The player stays dead until its machine moved it there.");
        Assert.That(clientLocal.Handle.Health.IsDead, Is.True);

        clientLocal.Controller.Teleport(delivered[0].Position);
        client.SendChunkReady(client.Resolve<PlayerWorlds>().TripOf(clientLocal.Handle), ChunkId.Of(world, Vector2Int.zero));
        yield return SceneBootTestHelper.WaitUntil(
            () => !hostCopy.GetComponent<Health>().IsDead && !clientLocal.Handle.Health.IsDead,
            "the host to restore the client's player at its waystone");
        Assert.That(Vector2.Distance(hostCopy.transform.position, world.EntryPosition("start")), Is.LessThan(1.5f));
    }

    /// <summary>
    /// Under a typical delayed connection, hosting Testlands with an in-process client that reported every loaded chunk,
    /// `start`'s among them, at its join (Docs/KnownGaps.md, Phase 4): its player dies away from `start`, and the host's
    /// respawn trip to `start`, within the same world, keeps that chunk held, its objects still shown to the client, but
    /// no longer ready for the trip. The player stays dead though the client moved it to `start`, until the client
    /// reports the chunk again during that trip, as its streaming does once the trip reaches it; then the host restores
    /// it, on both machines.
    /// </summary>
    [UnityTest]
    public IEnumerator AClientsRespawn_WaitsForItsChunkReportedDuringTheTrip()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        delay = NetworkDelay.Install();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        WorldDefinition world = gameFlow.CurrentWorld;
        PlayerRegistry hostPlayers = SceneBootTestHelper.ResolveFromGameplay<PlayerRegistry>();
        ChunkId startChunk = ChunkId.Of(world, Vector2Int.zero);

        client = InProcessClient.Start(session, SceneBootTestHelper.ResolveFromGameplay<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendReady();
        yield return SceneBootTestHelper.WaitUntil(
            () => client.LocalPlayer.Current != null && client.Players.Players.Count == 2 && hostPlayers.Players.Count == 2,
            "both sides to see both players");
        client.DisableColliders();
        DeactivateMobs();
        delay.Set(NetworkDelay.Conditions.Typical);
        ulong clientId = client.Manager.LocalClientId;
        LocalPlayer clientLocal = client.LocalPlayer.Current;
        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(hostPlayers, clientId);
        Health hostCopyHealth = hostCopy.GetComponent<Health>();
        yield return SceneBootTestHelper.WaitUntil(() => session.IsClientReady(clientId, startChunk), "the client's report of `start`'s chunk");
        Vector2 away = world.ToWorld(new Vector2(32.5f, 40.5f));
        clientLocal.Controller.Teleport(away);
        yield return SceneBootTestHelper.WaitUntil(() => Vector2.Distance(hostCopy.transform.position, away) < 0.1f, "the host to see the client's player move");

        List<TripApproval> delivered = new();
        client.Resolve<NetworkTripAuthority>().Approved += delivered.Add;
        SceneBootTestHelper.ResolveFromGameplay<DamageService>().ApplyDamage(hostCopy.GetComponent<DamageReceiver>(), hostCopyHealth.MaxHealth);
        yield return SceneBootTestHelper.WaitUntil(() => delivered.Count == 1, "the host's respawn trip to reach the client");
        Assert.That(delivered[0].World, Is.SameAs(world), "A respawn is a trip within the player's world.");
        Assert.That(session.Readiness.Holds(clientId, startChunk), Is.True, "The trip's world stays held, its objects shown to the client.");
        Assert.That(session.IsClientReady(clientId, startChunk), Is.False, "But it is not ready for the respawn's trip.");

        clientLocal.Controller.Teleport(delivered[0].Position);
        yield return SceneBootTestHelper.WaitUntil(() => Vector2.Distance(hostCopy.transform.position, delivered[0].Position) < 1.5f, "the host to see the client's player at `start`");
        for (float until = Time.time + 1f; Time.time < until;)
        {
            Assert.That(hostCopyHealth.IsDead, Is.True, "The respawn waits for the chunk reported during the trip.");
            yield return null;
        }

        client.SendChunkReady(client.Resolve<PlayerWorlds>().TripOf(clientLocal.Handle), startChunk);
        yield return SceneBootTestHelper.WaitUntil(
            () => !hostCopyHealth.IsDead && !clientLocal.Handle.Health.IsDead,
            "the host to restore the client's player once it reported the chunk");
        Assert.That(session.IsClientReady(clientId, startChunk), Is.True);
    }

    /// <summary>
    /// Hosting Testlands, the host's own player is sent on a trip to `start` while a mob stands by the entry: through the
    /// whole trip, its fade out, arrival and fade in, the host keeps it out of play (the mob never targets it, a hit on
    /// it is ignored, it loses no health); once its screen has faded in the protection is over, and the mob comes for it
    /// and hurts it.
    /// </summary>
    [UnityTest]
    public IEnumerator TheHostsTraveller_IsNotHurtAtItsArrival_UntilItFadesIn()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        WorldDefinition world = gameFlow.CurrentWorld;
        LocalPlayer local = SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current;
        Health health = local.Handle.Health;
        TripProtection protection = SceneBootTestHelper.ResolveFromGameplay<TripProtection>();
        LocalTripTraveller traveller = SceneBootTestHelper.ResolveFromGameplay<LocalTripTraveller>();
        HitService hits = SceneBootTestHelper.ResolveFromGameplay<HitService>();
        Vector2 entry = world.EntryPosition("start");
        yield return TeleportAndSettle(local.Controller, world, world.ToWorld(TwelveEastOfStart));
        MobController mob = StandAMobBy(world, entry);
        MobPerception2D perception = mob.GetComponent<MobPerception2D>();

        SceneBootTestHelper.ResolveFromGameplay<NetworkTripAuthority>().Send(local.Handle, world, "start");
        mob.enabled = true;
        Assert.That(protection.IsProtected(local.Handle), Is.True, "The trip protects the traveller from its approval on.");
        Assert.That(hits.ApplyHit(local.Controller.GetComponent<DamageReceiver>(), new Hit(10, DamageType.Physical), mob.gameObject), Is.False, "A hit on the traveller is ignored.");
        bool arrived = false;
        while (traveller.IsTravelling)
        {
            arrived |= Vector2.Distance(local.Transform.position, entry) < 1.5f;
            Assert.That(protection.IsProtected(local.Handle), Is.True, "The host keeps its traveller out of play until it fades in.");
            Assert.That(perception.CurrentTargetPlayer, Is.Not.SameAs(local.Handle), "The mob by the entry does not see the traveller.");
            Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
            yield return null;
        }

        Assert.That(arrived, Is.True, "The traveller stood by the mob before its screen faded in.");
        Assert.That(protection.IsProtected(local.Handle), Is.False, "Its fade in ends the protection.");
        yield return SceneBootTestHelper.WaitUntil(() => health.CurrentHealth < health.MaxHealth, "the mob to hurt the player once it has faded in");
    }

    /// <summary>
    /// Hosting Testlands with an in-process client, the host sends the client's player on a trip to `start` while a mob
    /// stands by the entry: the host keeps it out of play while the client moves it there and reports the entry's chunk
    /// for that trip, until the client reports its fade in; the protection then ends on the host and the mob hurts the
    /// player, on both machines.
    /// </summary>
    [UnityTest]
    public IEnumerator AClientsTraveller_IsNotHurtAtItsArrival_UntilItReportsItsFadeIn()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        WorldDefinition world = gameFlow.CurrentWorld;
        PlayerRegistry hostPlayers = SceneBootTestHelper.ResolveFromGameplay<PlayerRegistry>();
        PlayerController hostPlayer = SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current.Controller;
        TripProtection protection = SceneBootTestHelper.ResolveFromGameplay<TripProtection>();
        yield return TeleportAndSettle(hostPlayer, world, world.ToWorld(North));

        client = InProcessClient.Start(session, SceneBootTestHelper.ResolveFromGameplay<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendJoined();
        yield return SceneBootTestHelper.WaitUntil(
            () => client.LocalPlayer.Current != null && client.Players.Players.Count == 2 && hostPlayers.Players.Count == 2,
            "both sides to see both players");
        client.DisableColliders();
        LocalPlayer clientLocal = client.LocalPlayer.Current;
        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(hostPlayers, client.Manager.LocalClientId);
        PlayerHandle hostCopyHandle = HandleOf(hostPlayers, hostCopy);
        Health hostCopyHealth = hostCopy.GetComponent<Health>();
        Vector2 away = world.ToWorld(TwelveEastOfStart);
        clientLocal.Controller.Teleport(away);
        yield return SceneBootTestHelper.WaitUntil(() => Vector2.Distance(hostCopy.transform.position, away) < 0.1f, "the host to see the client's player move");
        Vector2 entry = world.EntryPosition("start");
        MobController mob = StandAMobBy(world, entry);
        MobPerception2D perception = mob.GetComponent<MobPerception2D>();

        NetworkTripAuthority clientTrips = client.Resolve<NetworkTripAuthority>();
        List<TripApproval> delivered = new();
        clientTrips.Approved += delivered.Add;
        SceneBootTestHelper.ResolveFromGameplay<NetworkTripAuthority>().Send(hostCopyHandle, world, "start");
        mob.enabled = true;
        Assert.That(protection.IsProtected(hostCopyHandle), Is.True);
        yield return SceneBootTestHelper.WaitUntil(() => delivered.Count == 1, "the trip to reach the client");
        clientLocal.Controller.Teleport(delivered[0].Position);
        int trip = client.Resolve<PlayerWorlds>().TripOf(clientLocal.Handle);
        client.SendChunkReady(trip, ChunkId.Of(world, Vector2Int.zero));
        yield return SceneBootTestHelper.WaitUntil(() => Vector2.Distance(hostCopy.transform.position, entry) < 1.5f, "the host to see the client's player at the entry");

        float until = Time.time + 1.5f;
        while (Time.time < until)
        {
            Assert.That(protection.IsProtected(hostCopyHandle), Is.True, "The host keeps the traveller out of play until its machine fades in, its chunk reported or not.");
            Assert.That(perception.CurrentTargetPlayer, Is.Not.SameAs(hostCopyHandle), "The mob by the entry does not see the traveller.");
            Assert.That(hostCopyHealth.CurrentHealth, Is.EqualTo(hostCopyHealth.MaxHealth));
            yield return null;
        }

        clientTrips.ReportArrived(clientLocal.Handle, trip);
        yield return SceneBootTestHelper.WaitUntil(() => !protection.IsProtected(hostCopyHandle), "the client's fade in to end the protection on the host");
        yield return SceneBootTestHelper.WaitUntil(
            () => hostCopyHealth.CurrentHealth < hostCopyHealth.MaxHealth && clientLocal.Handle.Health.CurrentHealth < clientLocal.Handle.Health.MaxHealth,
            "the mob to hurt the client's player, on both machines");
    }

    /// <summary>
    /// Hosting Testlands with an in-process client (Docs/KnownGaps.md, Phase 3): the client's player walks to `north`,
    /// which the host discovers for it and replicates to its machine alone; there its own record has it discovered and its
    /// markers' presenter shows the stone awake, while on the host, whose own player has not been there, the stone sleeps
    /// and `start`'s, the default entry, is awake; once the host's player stands at `north` too, the host's stone wakes.
    /// </summary>
    [UnityTest]
    public IEnumerator ADiscoveredWaystone_ShowsItsLook_OnTheHostAndOnAClient_EachByItsOwnPlayer()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        WorldDefinition world = gameFlow.CurrentWorld;
        PlayerRegistry hostPlayers = SceneBootTestHelper.ResolveFromGameplay<PlayerRegistry>();
        PlayerController hostPlayer = SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current.Controller;
        KnownWaystones hostKnown = SceneBootTestHelper.ResolveFromGameplay<KnownWaystones>();
        WaystoneMarkers hostMarkers = SceneBootTestHelper.ResolveFromGameplay<WaystoneMarkers>();

        client = InProcessClient.Start(session, SceneBootTestHelper.ResolveFromGameplay<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendJoined();
        yield return SceneBootTestHelper.WaitUntil(
            () => client.LocalPlayer.Current != null && client.Players.Players.Count == 2 && hostPlayers.Players.Count == 2,
            "both sides to see both players");
        client.DisableColliders();
        DeactivateMobs();
        KnownWaystones clientKnown = client.Resolve<KnownWaystones>();
        WorldDefinition.Entry north = world.FindEntry("north");
        Assert.That(clientKnown.IsDiscovered(world, north), Is.False);
        Assert.That(clientKnown.IsDiscovered(world, world.DefaultEntry), Is.True, "The default entry counts on a client too.");

        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(hostPlayers, client.Manager.LocalClientId);
        client.LocalPlayer.Current.Controller.Teleport(world.ToWorld(North));
        yield return SceneBootTestHelper.WaitUntil(() => Vector2.Distance(hostCopy.transform.position, world.ToWorld(North)) < 0.1f, "the host to see the client's player at `north`");
        yield return SceneBootTestHelper.WaitUntil(() => clientKnown.IsDiscovered(world, north), "the host's discovery to reach the client's record");
        Assert.That(hostCopy.GetComponent<PlayerWorldSync>().ReplicatedWaystoneCount, Is.EqualTo(1));
        Assert.That(hostKnown.IsDiscovered(world, north), Is.False, "The host's own player has not discovered it.");

        WaystoneMarker hostStone = null;
        yield return SceneBootTestHelper.WaitUntil(() => (hostStone = FindStone("north")) != null, "the host to load `north`'s chunk");
        Assert.That(hostStone.IsAwake, Is.False, "On the host the stone shows the host's player's discoveries: it sleeps.");
        Assert.That(hostStone.Glow.activeSelf, Is.False);
        Assert.That(hostMarkers.ShowsAwake(world, "start"), Is.True);
        Assert.That(FindStone("start").IsAwake, Is.True, "The default entry's stone is awake from the start.");

        WaystoneMarkers clientMarkers = client.Resolve<WaystoneMarkers>();
        clientMarkers.Start();
        WaystoneMarker clientStone = Object.Instantiate(hostStone.gameObject).GetComponent<WaystoneMarker>();
        try
        {
            clientMarkers.Add(world, ChunkCoords.ChunkOf(ChunkCoords.CellOf(North)), clientStone);
            Assert.That(clientStone.IsAwake, Is.True, "On the client the stone shows its own player's discovery: awake.");
            Assert.That(clientStone.Glow.activeSelf, Is.True, "Its light may burn there.");
            Assert.That(hostStone.IsAwake, Is.False, "The host's stays asleep.");

            hostPlayer.Teleport(world.ToWorld(North));
            yield return SceneBootTestHelper.WaitUntil(() => hostStone.IsAwake, "the host's own discovery to wake its stone");
            Assert.That(hostStone.Glow.activeSelf, Is.True);
        }
        finally
        {
            Object.Destroy(clientStone.gameObject);
        }
    }

    /// <summary>
    /// Hosting Testlands with a save that has a known client's player discover `north` in an earlier session: as that
    /// client joins, the host hands its machine the discovery with its player, once, so its record has `north` from the
    /// spawn on, while the host's own player, a stranger to `north`, does not.
    /// </summary>
    [UnityTest]
    public IEnumerator AKnownClient_KnowsTheWaystonesItDiscoveredBefore_FromItsSpawnOn()
    {
        const string KnownId = "waystone-client";
        MemorySaveStore saveStore = new();
        yield return SceneBootTestHelper.BootIntoMainMenu(saveStore);
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        WorldDefinition world = gameFlow.Scenes.StartingWorld;
        saveStore.Contents = new GameSaveData(
            1000d,
            new[] { new GameSaveData.Player(KnownId, world.WorldId, world.ToWorld(TwelveEastOfStart), new[] { new GameSaveData.Waystone(world.WorldId, "north") }) },
            new GameSaveData.World[0]).ToJson();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        PlayerRegistry hostPlayers = SceneBootTestHelper.ResolveFromGameplay<PlayerRegistry>();

        client = InProcessClient.Start(session, SceneBootTestHelper.ResolveFromGameplay<PlayerSpawner>().PlayerNetworkPrefab, KnownId);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendJoined();
        yield return SceneBootTestHelper.WaitUntil(() => client.LocalPlayer.Current != null && hostPlayers.Players.Count == 2, "the host to spawn the known player");
        KnownWaystones clientKnown = client.Resolve<KnownWaystones>();
        yield return SceneBootTestHelper.WaitUntil(() => clientKnown.IsDiscovered(world, world.FindEntry("north")), "the client's record to have `north`");

        Assert.That(clientKnown.Discovered, Has.Count.EqualTo(1), "Exactly the saved discovery, once.");
        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(hostPlayers, client.Manager.LocalClientId);
        Assert.That(hostCopy.GetComponent<PlayerWorldSync>().ReplicatedWaystoneCount, Is.EqualTo(1));
        Assert.That(SceneBootTestHelper.ResolveFromGameplay<KnownWaystones>().IsDiscovered(world, world.FindEntry("north")), Is.False, "The host's player has not discovered it.");
    }

    /// <summary>
    /// Keeps the first mob of the loaded chunk holding <paramref name="position"/> in <paramref name="world"/> standing
    /// one and a half cells east of it with its AI off, and turns every other mob's AI off.
    /// </summary>
    private static MobController StandAMobBy(WorldDefinition world, Vector2 position)
    {
        Assert.That(SceneBootTestHelper.ResolveFromGameplay<Worlds>().TryGet(world, out LoadedWorld loaded), Is.True);
        Scene chunk = loaded.Chunks[ChunkCoords.ChunkOf(ChunkCoords.CellOf(world.ToLocal(position)))];
        MobController mob = SceneQuery.FindFirst<MobController>(chunk);
        Assert.That(mob, Is.Not.Null, "The chunk should have a mob.");
        foreach (MobController other in Object.FindObjectsByType<MobController>())
        {
            other.enabled = false;
        }

        Vector2 spot = position + new Vector2(1.5f, 0f);
        mob.GetComponent<Rigidbody2D>().position = spot;
        mob.transform.position = spot;
        Physics2D.SyncTransforms();
        return mob;
    }

    /// <summary>
    /// Teleports <paramref name="player"/> to <paramref name="position"/> in <paramref name="world"/> and waits until the
    /// chunks around it are loaded.
    /// </summary>
    private static IEnumerator TeleportAndSettle(PlayerController player, WorldDefinition world, Vector2 position)
    {
        player.Teleport(position);
        WorldStreaming streaming = SceneBootTestHelper.ResolveFromGameplay<WorldStreaming>();
        yield return SceneBootTestHelper.WaitUntil(() => streaming.Streamer.IsSettled(new StreamInterest(world, position)), "the chunks around the player to load");
        yield return null;
    }

    /// <summary>
    /// The marker of Testlands' waystone <paramref name="entryName"/> in a loaded chunk, or null.
    /// </summary>
    private static WaystoneMarker FindStone(string entryName)
    {
        foreach (WaystoneMarker marker in Object.FindObjectsByType<WaystoneMarker>())
        {
            if (marker.EntryName == entryName && marker.gameObject.scene.name.StartsWith("World_Testlands"))
            {
                return marker;
            }
        }

        return null;
    }

    /// <summary>
    /// Deactivates every loaded mob, so none attacks the player during the test.
    /// </summary>
    private static void DeactivateMobs()
    {
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            mob.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Whether an active floating popup shows <paramref name="message"/>.
    /// </summary>
    private static bool ShowsText(string message)
    {
        foreach (FloatingDamageText popup in Object.FindObjectsByType<FloatingDamageText>())
        {
            TMP_Text text = popup.GetComponentInChildren<TMP_Text>();
            if (popup.gameObject.activeInHierarchy && text != null && text.text == message)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The handle in <paramref name="players"/> of <paramref name="player"/>, or null.
    /// </summary>
    private static PlayerHandle HandleOf(IPlayerRegistry players, PlayerController player)
    {
        foreach (PlayerHandle handle in players.Players)
        {
            if (player != null && handle.Transform == player.transform)
            {
                return handle;
            }
        }

        return null;
    }
}
