using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// PlayMode test for the notice line on a joined client (Docs/UiRevamp.md, Phase 3), with the in-process host and client
/// in Testlands: each machine raises its notices from state it already has, so the client's notice line, a copy of the
/// HUD's driven by a presenter over the client's own services, announces the world its player stands in, shows the
/// client's own waystone discovery, which the host's line never shows, and counts the client's own death down, then
/// says "Respawning..." until the host restores it, while the host's line shows none of it and the host's own
/// discovery never reaches the client's. A heal reaches the client naming the client's copy of its healer, so its
/// popups tell its own heals apart.
/// </summary>
public class NetworkNoticesPlayModeTests
{
    private InProcessClient client;
    private NoticePresenter clientNotices;
    private NoticeView clientView;

    /// <summary>
    /// Disposes the client's notice presenter, destroys its notice line and disposes the in-process client.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        clientNotices?.Dispose();
        clientNotices = null;
        if (clientView != null)
        {
            Object.Destroy(clientView.gameObject);
        }

        client?.Dispose();
        client = null;
    }

    /// <summary>
    /// The client announces Testlands; the host's own discovery shows on the host only and the client's on the client
    /// only; the client's player, killed on the host, counts down from 3 on the client, then says "Respawning...", and
    /// the notice goes once the host restores it at its waystone, with no world announced again, while the host's line
    /// never shows the death; a heal the host's player gives the client's arrives naming the client's copy of the
    /// host's player.
    /// </summary>
    [UnityTest]
    public IEnumerator Notices_ShowOnTheirOwnMachineOnly_AndAClientsDeathAndWorldShowOnTheClient()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        WorldDefinition world = gameFlow.CurrentWorld;
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerRegistry hostPlayers = gameplay.Resolve<PlayerRegistry>();
        NoticePresenter hostNotices = gameplay.Resolve<NoticePresenter>();
        GameplaySettings settings = gameplay.Resolve<GameplaySettings>();
        LocalPlayer hostLocal = gameplay.Resolve<LocalPlayerTracker>().Current;
        string entering = string.Format(NoticePresenter.EnteringFormat, world.DisplayName);
        Assert.That(world.DisplayName, Is.Not.Empty);

        client = InProcessClient.Start(session, gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendJoined();
        yield return SceneBootTestHelper.WaitUntil(
            () => client.LocalPlayer.Current != null && client.Players.Players.Count == 2 && hostPlayers.Players.Count == 2,
            "both sides to see both players");
        client.DisableColliders();
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            mob.gameObject.SetActive(false);
        }

        LocalPlayer clientLocal = client.LocalPlayer.Current;
        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(hostPlayers, client.Manager.LocalClientId);
        PlayerHandle hostCopyHandle = HandleOf(hostPlayers, hostCopy);
        NoticeView hostView = Object.FindAnyObjectByType<NoticeView>(FindObjectsInactive.Include);
        clientView = Object.Instantiate(hostView, hostView.transform.parent);
        clientView.Hide();
        clientNotices = new NoticePresenter(clientView, gameplay.Resolve<HudSettings>(), client.Resolve<GameplaySettings>(), client.Resolve<IClock>(), client.LocalPlayer, client.Resolve<PlayerWorlds>(), client.Resolve<WaystoneNotices>(), new GameFlow(null, null, null, null));
        clientNotices.Start();
        yield return SceneBootTestHelper.WaitUntil(() => Ticked() && clientNotices.Holds(entering), "the client to announce the world its player stands in");

        gameplay.Resolve<WaystoneNotices>().Raise(hostLocal.Handle, "north");
        Assert.That(hostNotices.Holds(settings.WaystoneDiscoveredText), Is.True, "The host's own discovery shows on the host.");
        for (int frame = 0; frame < 10; frame++)
        {
            yield return null;
            Ticked();
        }

        Assert.That(clientNotices.Holds(settings.WaystoneDiscoveredText), Is.False, "The host's discovery never reaches the client's line.");
        int hostWaiting = hostNotices.WaitingCount;
        string hostShown = hostNotices.Shown;
        gameplay.Resolve<WaystoneNotices>().Raise(hostCopyHandle, "north");
        hostCopy.GetComponent<PlayerWorldSync>().SendWaystoneDiscovered(world.WorldId, "north");
        yield return SceneBootTestHelper.WaitUntil(() => Ticked() && clientNotices.Holds(settings.WaystoneDiscoveredText), "the client's discovery to reach the client's line");
        Assert.That(hostNotices.WaitingCount, Is.EqualTo(hostWaiting), "The client's discovery queues nothing on the host.");
        Assert.That(hostNotices.Shown, Is.EqualTo(hostShown));

        yield return SceneBootTestHelper.WaitUntil(() => Ticked() && !clientNotices.Holds(entering), "the client's world notice to have shown its time");
        NetworkTripAuthority clientTrips = client.Resolve<NetworkTripAuthority>();
        List<TripApproval> delivered = new();
        clientTrips.Approved += delivered.Add;
        gameplay.Resolve<DamageService>().ApplyDamage(hostCopy.GetComponent<DamageReceiver>(), hostCopy.GetComponent<Health>().MaxHealth);
        yield return SceneBootTestHelper.WaitUntil(() => Ticked() && clientLocal.Handle.Health.IsDead, "the client's player to die on the client");
        Assert.That(clientNotices.Shown, Is.EqualTo(string.Format(NoticePresenter.FellFormat, Mathf.CeilToInt(settings.RespawnDelaySeconds))), "The client counts down from its own death.");
        Assert.That(clientView.Text, Is.EqualTo(clientNotices.Shown));
        Assert.That(hostNotices.Shown ?? string.Empty, Does.Not.StartWith("You fell."), "The host's player lives, so the host's line shows no death.");

        yield return SceneBootTestHelper.WaitUntil(() => Ticked() && clientNotices.Shown == string.Format(NoticePresenter.FellFormat, 1), "the client's countdown to reach 1");
        yield return SceneBootTestHelper.WaitUntil(() => Ticked() && clientNotices.Shown == NoticePresenter.Respawning, "the client's countdown to run out");
        yield return SceneBootTestHelper.WaitUntil(() => Ticked() && delivered.Count == 1, "the host's respawn trip to reach the client");
        Assert.That(clientNotices.Shown, Is.EqualTo(NoticePresenter.Respawning), "Respawning holds while the player is dead.");
        clientLocal.Controller.Teleport(delivered[0].Position);
        Vector2Int chunk = ChunkCoords.ChunkOf(ChunkCoords.CellOf(world.ToLocal(delivered[0].Position)));
        client.SendChunkReady(client.Resolve<PlayerWorlds>().TripOf(clientLocal.Handle), ChunkId.Of(world, chunk));
        yield return SceneBootTestHelper.WaitUntil(() => Ticked() && !clientLocal.Handle.Health.IsDead, "the host to restore the client's player");
        Assert.That(clientNotices.Shown, Is.Not.EqualTo(NoticePresenter.Respawning), "The death notice goes once the player stands.");
        Assert.That(clientNotices.Holds(entering), Is.False, "A respawn within the world announces nothing.");
        Assert.That(hostNotices.Shown ?? string.Empty, Does.Not.StartWith("You fell."));
        Assert.That(hostNotices.Shown, Is.Not.EqualTo(NoticePresenter.Respawning));

        List<HealReport> heals = new();
        client.Resolve<CombatEvents>().HealApplied += heals.Add;
        DamageService damage = gameplay.Resolve<DamageService>();
        damage.ApplyDamage(hostCopy.GetComponent<DamageReceiver>(), 30);
        Assert.That(damage.ApplyHeal(hostCopy.GetComponent<DamageReceiver>(), 20, hostLocal.Controller.gameObject), Is.EqualTo(20));
        yield return SceneBootTestHelper.WaitUntil(() => Ticked() && heals.Count == 1, "the heal to reach the client");
        PlayerController hostPlayerOnClient = InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId);
        Assert.That(heals[0].Source, Is.SameAs(hostPlayerOnClient.gameObject), "The heal names the client's copy of its healer.");
    }

    /// <summary>
    /// Ticks the client's notice presenter, as the client's Gameplay scope would each frame; always true, so a wait's
    /// condition can start with it.
    /// </summary>
    private bool Ticked()
    {
        clientNotices.Tick();
        return true;
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
