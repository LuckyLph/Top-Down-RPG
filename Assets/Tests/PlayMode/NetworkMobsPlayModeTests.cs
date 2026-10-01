using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

public class NetworkMobsPlayModeTests
{
    private InProcessClient client;

    [TearDown]
    public void TearDown()
    {
        client?.Dispose();
        client = null;
    }

    [UnityTest]
    public IEnumerator HostedMobs_ReplicateMovementHealthAndDeath_AndPlayerHealthFollowsTheHost()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        yield return null;

        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerRegistry hostPlayers = gameplay.Resolve<PlayerRegistry>();
        DamageService hostDamage = gameplay.Resolve<DamageService>();
        MobController hostMob = Object.FindAnyObjectByType<MobController>();
        Assert.That(hostMob, Is.Not.Null);
        Assert.That(hostMob.GetComponent<NetworkObject>().IsSpawned, Is.True, "The host should spawn area mobs as network objects.");

        client = InProcessClient.Start(session, gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendReady();

        MobController clientMob = null;
        yield return SceneBootTestHelper.WaitUntil(
            () => (clientMob = FindCopyManagedBy(client.Manager)) != null && client.LocalPlayer.Current != null && hostPlayers.Players.Count == 2,
            "the client to receive the host's mob and its own player");
        client.DisableColliders();

        Assert.That(clientMob.enabled, Is.False, "Mob AI only runs on the host.");
        Assert.That(clientMob.GetComponent<Rigidbody2D>().bodyType, Is.EqualTo(RigidbodyType2D.Kinematic));

        Rigidbody2D hostBody = hostMob.GetComponent<Rigidbody2D>();
        hostMob.enabled = false;
        hostBody.linearVelocity = Vector2.zero;
        Vector2 clientStart = clientMob.transform.position;
        Vector2 moved = hostBody.position + new Vector2(1.5f, 0f);
        hostBody.position = moved;
        hostMob.transform.position = moved;
        yield return SceneBootTestHelper.WaitUntil(
            () => Vector2.Distance(clientMob.transform.position, hostMob.transform.position) < 0.15f
                  && Vector2.Distance(clientMob.transform.position, clientStart) > 1f,
            "the client's mob copy to follow the host's mob");

        CombatEvents clientEvents = client.Resolve<CombatEvents>();
        int clientReports = 0;
        clientEvents.DamageApplied += _ => clientReports++;
        Health hostMobHealth = hostMob.GetComponent<Health>();
        Health clientMobHealth = clientMob.GetComponent<Health>();
        hostDamage.ApplyDamage(hostMob.GetComponent<DamageReceiver>(), 1);
        yield return SceneBootTestHelper.WaitUntil(() => clientMobHealth.CurrentHealth == hostMobHealth.CurrentHealth, "the mob's health to reach the client");
        Assert.That(clientReports, Is.EqualTo(1), "The client should publish the replicated hit for its damage popup.");

        Health clientPlayerHealth = client.LocalPlayer.Current.Handle.Health;
        Health hostCopyHealth = InProcessClient.FindCopyOwnedBy(hostPlayers, client.Manager.LocalClientId).GetComponent<Health>();
        hostDamage.ApplyDamage(hostCopyHealth.GetComponent<DamageReceiver>(), 2);
        yield return SceneBootTestHelper.WaitUntil(
            () => clientPlayerHealth.CurrentHealth == hostCopyHealth.CurrentHealth && clientPlayerHealth.CurrentHealth < clientPlayerHealth.MaxHealth,
            "the client's own player to take the damage the host applied");

        hostDamage.ApplyDamage(hostCopyHealth.GetComponent<DamageReceiver>(), hostCopyHealth.MaxHealth);
        yield return SceneBootTestHelper.WaitUntil(() => clientPlayerHealth.IsDead, "the client's player to die with the host's copy");
        Assert.That(client.LocalPlayer.Current.Controller.enabled, Is.False, "A dead player stops responding to input on its owner too.");
        yield return SceneBootTestHelper.WaitUntil(
            () => !clientPlayerHealth.IsDead && clientPlayerHealth.CurrentHealth == clientPlayerHealth.MaxHealth,
            "the host to respawn the client's player");
        Assert.That(client.LocalPlayer.Current.Controller.enabled, Is.True);

        int deathAnimationsBefore = Object.FindObjectsByType<MobDeathAnimation>().Length;
        hostDamage.ApplyDamage(hostMob.GetComponent<DamageReceiver>(), hostMobHealth.CurrentHealth);
        yield return SceneBootTestHelper.WaitUntil(() => clientMob == null, "the client's mob copy to be despawned by the host");
        Assert.That(hostMob == null, Is.True);
        Assert.That(Object.FindObjectsByType<MobDeathAnimation>().Length, Is.GreaterThanOrEqualTo(deathAnimationsBefore + 2), "Both the host and the client should play the death animation.");
    }

    private static MobController FindCopyManagedBy(NetworkManager manager)
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
}
