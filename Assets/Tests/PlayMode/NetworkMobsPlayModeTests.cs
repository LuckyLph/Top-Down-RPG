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

        StatusEffectService hostStatuses = gameplay.Resolve<StatusEffectService>();
        StatusEffectDefinition fortify = SceneBootTestHelper.FindStatus("Fortify");
        StatusEffectDefinition burn = SceneBootTestHelper.FindStatus("Burn");
        Assert.That(hostStatuses.Apply(hostMob.GetComponent<DamageReceiver>(), fortify), Is.EqualTo(StatusApplyOutcome.Landed));

        client = InProcessClient.Start(session, gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendReady();

        MobController clientMob = null;
        yield return SceneBootTestHelper.WaitUntil(
            () => (clientMob = FindCopyManagedBy(client.Manager)) != null && client.LocalPlayer.Current != null && hostPlayers.Players.Count == 2,
            "the client to receive the host's mob and its own player");
        client.DisableColliders();

        Assert.That(clientMob.enabled, Is.False, "Mob AI only runs on the host.");
        StatusEffects clientMobStatuses = clientMob.GetComponent<StatusEffects>();
        yield return SceneBootTestHelper.WaitUntil(() => clientMobStatuses.GetStacks(fortify) == 1, "a late joiner to receive the mob's active status");
        Assert.That(clientMobStatuses.GetSnapshot(0).Remaining, Is.GreaterThan(0f).And.LessThanOrEqualTo(fortify.Duration));
        Assert.That(clientMob.GetComponent<DamageReceiver>().GetDefense(DamageType.Fire).ResistancePercent, Is.EqualTo(30), "The mirror changes the copy's defence too.");
        Assert.That(hostStatuses.Apply(hostMob.GetComponent<DamageReceiver>(), SceneBootTestHelper.FindStatus("Stun")), Is.EqualTo(StatusApplyOutcome.Landed));
        Assert.That(hostMob.IsStunned, Is.True);
        yield return SceneBootTestHelper.WaitUntil(
            () => (clientMobStatuses.Controls & StatusControls.Stun) != 0 && clientMobStatuses.MovementScale == 0f,
            "the client's copy to show the mob as stunned");
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
        DamageReport lastClientReport = default;
        clientEvents.DamageApplied += report =>
        {
            clientReports++;
            lastClientReport = report;
        };
        Health hostMobHealth = hostMob.GetComponent<Health>();
        Health clientMobHealth = clientMob.GetComponent<Health>();
        DamageReceiver hostMobReceiver = hostMob.GetComponent<DamageReceiver>();
        hostDamage.ApplyDamage(hostMobReceiver, 1, DamageType.Fire);
        yield return SceneBootTestHelper.WaitUntil(() => clientMobHealth.CurrentHealth == hostMobHealth.CurrentHealth, "the mob's health to reach the client");
        Assert.That(clientReports, Is.EqualTo(1), "The client should publish the replicated hit for its damage popup.");
        Assert.That(lastClientReport.Type, Is.EqualTo(DamageType.Fire), "The hit's damage type reaches the client.");

        CombatProfile mobProfile = hostMobReceiver.Profile;
        CombatProfile frostImmune = CombatProfile.Create(Faction.Mobs, DamageTypeMask.Frost, StatusTags.Burn);
        hostMobReceiver.SetProfile(frostImmune);
        int healthBeforeImmuneHit = hostMobHealth.CurrentHealth;
        hostDamage.ApplyDamage(hostMobReceiver, 5, DamageType.Frost);
        yield return SceneBootTestHelper.WaitUntil(() => clientReports == 2, "the immune hit to reach the client");
        Assert.That(lastClientReport.IsImmune, Is.True, "An immune hit reaches the client for its Immune popup.");
        Assert.That(lastClientReport.Amount, Is.Zero);
        Assert.That(clientMobHealth.CurrentHealth, Is.EqualTo(healthBeforeImmuneHit));
        int clientBlocks = 0;
        clientEvents.StatusBlocked += _ => clientBlocks++;
        Assert.That(hostStatuses.Apply(hostMobReceiver, burn), Is.EqualTo(StatusApplyOutcome.Immune));
        yield return SceneBootTestHelper.WaitUntil(() => clientBlocks == 1, "the blocked status to reach the client for its Immune popup");
        hostMobReceiver.SetProfile(mobProfile);
        Object.Destroy(frostImmune);

        Health clientPlayerHealth = client.LocalPlayer.Current.Handle.Health;
        Health hostCopyHealth = InProcessClient.FindCopyOwnedBy(hostPlayers, client.Manager.LocalClientId).GetComponent<Health>();
        hostDamage.ApplyDamage(hostCopyHealth.GetComponent<DamageReceiver>(), 2);
        yield return SceneBootTestHelper.WaitUntil(
            () => clientPlayerHealth.CurrentHealth == hostCopyHealth.CurrentHealth && clientPlayerHealth.CurrentHealth < clientPlayerHealth.MaxHealth,
            "the client's own player to take the damage the host applied");

        int clientHeals = 0;
        clientEvents.HealApplied += _ => clientHeals++;
        int healthBeforeHeal = hostCopyHealth.CurrentHealth;
        Assert.That(hostDamage.ApplyHeal(hostCopyHealth.GetComponent<DamageReceiver>(), 1), Is.EqualTo(1));
        yield return SceneBootTestHelper.WaitUntil(
            () => clientPlayerHealth.CurrentHealth == healthBeforeHeal + 1,
            "the client's own player to receive the host's heal");
        Assert.That(clientHeals, Is.EqualTo(1), "The client should publish the replicated heal for its popup.");

        StatusEffects clientPlayerStatuses = client.LocalPlayer.Current.Handle.Transform.GetComponent<StatusEffects>();
        int healthBeforeBurn = clientPlayerHealth.CurrentHealth;
        int periodicReports = 0;
        clientEvents.DamageApplied += report =>
        {
            if (report.Target == clientPlayerHealth && (report.Flags & DamageFlags.Periodic) != 0)
            {
                periodicReports++;
            }
        };
        Assert.That(hostStatuses.Apply(hostCopyHealth.GetComponent<DamageReceiver>(), burn), Is.EqualTo(StatusApplyOutcome.Landed));
        yield return SceneBootTestHelper.WaitUntil(() => clientPlayerStatuses.GetStacks(burn) == 1, "the client's own player to show the host's Burn");
        yield return SceneBootTestHelper.WaitUntil(
            () => periodicReports > 0 && clientPlayerHealth.CurrentHealth < healthBeforeBurn,
            "the Burn's ticks to reach the client's player");

        hostDamage.ApplyDamage(hostCopyHealth.GetComponent<DamageReceiver>(), hostCopyHealth.MaxHealth);
        yield return SceneBootTestHelper.WaitUntil(() => clientPlayerHealth.IsDead, "the client's player to die with the host's copy");
        Assert.That(client.LocalPlayer.Current.Controller.enabled, Is.False, "A dead player stops responding to input on its owner too.");
        yield return SceneBootTestHelper.WaitUntil(() => clientPlayerStatuses.Count == 0, "death to clear the player's statuses on the client too");
        yield return SceneBootTestHelper.WaitUntil(
            () => !clientPlayerHealth.IsDead && clientPlayerHealth.CurrentHealth == clientPlayerHealth.MaxHealth,
            "the host to respawn the client's player");
        Assert.That(client.LocalPlayer.Current.Controller.enabled, Is.True);

        int deathAnimationsBefore = Object.FindObjectsByType<MobDeathAnimation>().Length;
        hostDamage.ApplyDamage(hostMob.GetComponent<DamageReceiver>(), hostMobHealth.CurrentHealth, DamageType.True);
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
