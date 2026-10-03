using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

public class NetworkAreaSyncPlayModeTests
{
    private InProcessClient client;

    [TearDown]
    public void TearDown()
    {
        client?.Dispose();
        client = null;
    }

    [UnityTest]
    public IEnumerator Host_AnnouncesAreas_AndOnlySendsNewAreaObjectsToClientsReadyForThatArea()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        yield return null;

        string areaPath = gameFlow.CurrentArea.ScenePath;
        client = InProcessClient.Start(session, ResolveGameplay<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Announcements.Count == 1, "a late joiner to be told the host's current area");
        AreaAnnouncement joined = client.Announcements[0];
        Assert.That(joined.ScenePath, Is.EqualTo(areaPath));
        Assert.That(joined.Epoch, Is.EqualTo(session.AreaEpoch));
        Assert.That(joined.NewSession, Is.True, "A joining client starts a fresh session in the host's area.");

        client.SendReady();
        yield return SceneBootTestHelper.WaitUntil(
            () => client.LocalPlayer.Current != null && client.Players.Players.Count == 2 && FindMobManagedBy(client.Manager) != null,
            "the ready client to receive both players and the area's mob");
        client.DisableColliders();
        MobController firstAreaCopy = FindMobManagedBy(client.Manager);

        gameFlow.ChangeAreaAsync(gameFlow.Scenes.StartingArea, gameFlow.Scenes.StartingSpawnId);
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        yield return SceneBootTestHelper.WaitUntil(() => client.Announcements.Count == 2, "the area change to be announced");
        AreaAnnouncement changed = client.Announcements[1];
        Assert.That(changed.Epoch, Is.EqualTo(joined.Epoch + 1));
        Assert.That(changed.NewSession, Is.False);
        yield return SceneBootTestHelper.WaitUntil(() => firstAreaCopy == null, "the old area's mob to be despawned on the client");

        MobController hostMob = FindMobManagedBy(session.NetworkManager);
        Assert.That(hostMob, Is.Not.Null, "The host should spawn the new area's mob.");
        yield return Frames(15);
        Assert.That(FindMobManagedBy(client.Manager), Is.Null, "A client still loading the new area must not receive its mobs.");

        PlayerController loadingCopy = InProcessClient.FindCopyOwnedBy(ResolveGameplay<PlayerRegistry>(), client.Manager.LocalClientId);
        NetworkStatusEffects loadingStatuses = loadingCopy.GetComponent<NetworkStatusEffects>();
        Health loadingHealth = loadingCopy.GetComponent<Health>();
        int periodicHits = 0;
        ResolveGameplay<CombatEvents>().DamageApplied += report =>
        {
            if (report.Target == loadingHealth && (report.Flags & DamageFlags.Periodic) != 0)
            {
                periodicHits++;
            }
        };
        Assert.That(loadingStatuses.IsHeld, Is.True, "The host holds a loading client's player.");
        Assert.That(
            ResolveGameplay<StatusEffectService>().Apply(loadingCopy.GetComponent<DamageReceiver>(), SceneBootTestHelper.FindStatus("Burn")),
            Is.EqualTo(StatusApplyOutcome.Landed));
        float heldRemaining = loadingCopy.GetComponent<StatusEffects>().GetSnapshot(0).Remaining;
        yield return new WaitForSeconds(1.2f);
        Assert.That(periodicHits, Is.Zero, "A held player's statuses do not tick.");
        Assert.That(loadingCopy.GetComponent<StatusEffects>().GetSnapshot(0).Remaining, Is.EqualTo(heldRemaining), "Nor do they run down.");

        client.SendReady(joined.Epoch);
        yield return Frames(15);
        Assert.That(FindMobManagedBy(client.Manager), Is.Null, "A ready report for the previous area is ignored.");

        client.SendReady(changed.Epoch);
        yield return SceneBootTestHelper.WaitUntil(() => FindMobManagedBy(client.Manager) != null, "the client to receive the new area's mob once ready");
        Assert.That(loadingStatuses.IsHeld, Is.False, "A ready client's player is no longer held.");
        yield return SceneBootTestHelper.WaitUntil(() => periodicHits > 0, "the released player's Burn to tick");
        Assert.That(CountPlayersOwnedBy(ResolveGameplay<PlayerRegistry>(), client.Manager.LocalClientId), Is.EqualTo(1), "Reporting ready again must not spawn a second player.");
        Assert.That(client.Players.Players.Count, Is.EqualTo(2));
    }

    [UnityTest]
    public IEnumerator HostRestart_AnnouncesANewSession_AndRespawnsItsPlayer()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        int epochBefore = session.AreaEpoch;

        gameFlow.StartNewGameAsync(gameFlow.CurrentArea, gameFlow.Scenes.StartingSpawnId);
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        yield return null;

        Assert.That(session.AreaEpoch, Is.EqualTo(epochBefore + 1));
        Assert.That(session.IsHost, Is.True, "Restarting the area keeps the session running.");
        LocalPlayerTracker localPlayer = ResolveGameplay<LocalPlayerTracker>();
        Assert.That(localPlayer.Current, Is.Not.Null);
        Assert.That(localPlayer.Current.Controller.GetComponent<NetworkObject>().IsSpawned, Is.True);
    }

    private static T ResolveGameplay<T>()
    {
        return Object.FindAnyObjectByType<GameplayLifetimeScope>().Container.Resolve<T>();
    }

    private static MobController FindMobManagedBy(NetworkManager manager)
    {
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            NetworkObject networkObject = mob.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsSpawned && networkObject.NetworkManager == manager)
            {
                return mob;
            }
        }

        return null;
    }

    private static int CountPlayersOwnedBy(PlayerRegistry players, ulong clientId)
    {
        int count = 0;
        foreach (PlayerHandle handle in players.Players)
        {
            if (handle.Transform.TryGetComponent(out NetworkObject networkObject) && networkObject.OwnerClientId == clientId)
            {
                count++;
            }
        }

        return count;
    }

    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return null;
        }
    }
}
