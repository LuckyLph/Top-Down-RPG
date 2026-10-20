using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// PlayMode tests for waystones and respawns in Testlands (Docs/WorldStreaming.md: Death and respawn), whose default
/// entry `start` and waystone `north` lie 50 cells apart: offline, a dead player respawns after the delay at the nearest
/// waystone it discovered, its own machine travelling there; walking to a waystone discovers it, shows the player a
/// popup, and Continue keeps it; and in a hosted game a client's dead player respawns at its waystone with the host
/// deciding and the client moving its own player there.
/// </summary>
public class WaystonePlayModeTests
{
    private static readonly Vector2 North = new(30.5f, 90.5f);
    private static readonly Vector2 SouthOfNorth = new(30.5f, 85.5f);

    private InProcessClient client;

    /// <summary>
    /// Disposes the in-process client when the test started one.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
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
