using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// PlayMode tests for host-authoritative mobs and player health replicated to an in-process client.
/// </summary>
public class NetworkMobsPlayModeTests
{
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
    /// The client's mob copy runs no AI and mirrors the host's statuses (including for a late joiner), stun, position,
    /// health, typed and immune hits, blocked statuses and death. The client's own player follows the host's damage,
    /// heals, Burn ticks, death with cleared statuses and respawn.
    /// </summary>
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
            () => (clientMob = FindCopyManagedBy(client.Manager, hostMob)) != null && client.LocalPlayer.Current != null && hostPlayers.Players.Count == 2,
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

        EffectSprites deathSprites = hostMob.GetComponent<DestroyMobOnDeath>().DeathEffect.Sprites;
        int deathEffects = 0;
        System.Action<EffectPlayer> onHostEffect = effect => deathEffects += effect.Sprites == deathSprites ? 1 : 0;
        System.Action<EffectPlayer> onClientEffect = effect => deathEffects += effect.Sprites == deathSprites ? 1 : 0;
        EffectPool hostEffects = gameplay.Resolve<EffectPool>();
        EffectPool clientEffects = client.Resolve<EffectPool>();
        hostEffects.Played += onHostEffect;
        clientEffects.Played += onClientEffect;
        try
        {
            hostDamage.ApplyDamage(hostMob.GetComponent<DamageReceiver>(), hostMobHealth.CurrentHealth, DamageType.True);
            yield return SceneBootTestHelper.WaitUntil(() => clientMob == null, "the client's mob copy to be despawned by the host");
            Assert.That(hostMob == null, Is.True);
            Assert.That(deathEffects, Is.EqualTo(2), "Both the host and the client should play the death effect.");
        }
        finally
        {
            hostEffects.Played -= onHostEffect;
            clientEffects.Played -= onClientEffect;
        }
    }

    /// <summary>
    /// The host's mob attacks reach a client (Docs/Animation.md, Phase 7): a client joining while the mob attacks the
    /// host's player sees no attack replayed, then each next one on its copy: the windup, its frames before the contact
    /// frame until the host's contact arrives, then the contact frame; a stun on the host mid-windup stops the copy's
    /// attack at once, though the copy would otherwise hold its windup until a contact.
    /// </summary>
    [UnityTest]
    public IEnumerator AHostsMobAttacks_PlayOnTheClientsCopy_FromTheNextStart()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        yield return null;

        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        DamageService hostDamage = gameplay.Resolve<DamageService>();
        GameObject hostPlayer = GameObject.FindGameObjectWithTag("Player");
        DamageReceiver hostPlayerReceiver = hostPlayer.GetComponent<DamageReceiver>();
        MobController hostMob = Object.FindAnyObjectByType<MobController>();
        MeleeDamageDealer hostDealer = hostMob.GetComponent<MeleeDamageDealer>();
        int hostStarts = 0;
        int hostContacts = 0;
        hostDealer.AttackStarted += _ => hostStarts++;
        hostDealer.AttackContacted += () => hostContacts++;

        Vector3 nextToTheMob = hostMob.transform.position + Vector3.right * 0.7f;
        hostPlayer.transform.position = nextToTheMob;
        hostPlayer.GetComponent<Rigidbody2D>().position = nextToTheMob;
        yield return SceneBootTestHelper.WaitUntil(() => hostDealer.IsAttacking, "the host's mob to attack the host's player");

        client = InProcessClient.Start(session, gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendReady();
        MobController clientMob = null;
        yield return SceneBootTestHelper.WaitUntil(() => (clientMob = FindCopyManagedBy(client.Manager, hostMob)) != null, "the client to receive the host's mob");
        client.DisableColliders();
        CharacterVisuals copy = clientMob.GetComponentInChildren<CharacterVisuals>();
        int startsBeforeTheCopy = hostStarts;
        Assert.That(copy.Animator.IsActing, Is.False, "A late joiner sees no attack replayed.");

        for (int attack = 0; attack < 2; attack++)
        {
            hostDamage.ApplyHeal(hostPlayerReceiver, 1000);
            int startsBefore = Mathf.Max(hostStarts, startsBeforeTheCopy);
            yield return SceneBootTestHelper.WaitUntil(() => hostStarts > startsBefore && copy.Animator.IsActing && copy.Animator.Frame < copy.Animator.Clip.ContactFrame, "the next attack to start on the host and play its windup on the client's copy");
            Assert.That(copy.Animator.Clip.Name, Is.EqualTo("DaggerSlash"));
            int contactsBefore = hostContacts;
            bool showedContact = false;
            float giveUp = Time.realtimeSinceStartup + 3f;
            while (!showedContact && Time.realtimeSinceStartup < giveUp)
            {
                yield return new WaitForEndOfFrame();
                if (!copy.Animator.IsActing)
                {
                    continue;
                }

                if (copy.Animator.Frame == copy.Animator.Clip.ContactFrame)
                {
                    showedContact = true;
                    Assert.That(hostContacts, Is.GreaterThan(contactsBefore), "The copy shows its contact only once the host's has landed.");
                }
                else if (hostContacts == contactsBefore)
                {
                    Assert.That(copy.Animator.Frame, Is.LessThan(copy.Animator.Clip.ContactFrame), "Before the host's contact the copy shows its windup.");
                }
            }

            Assert.That(showedContact, Is.True, "The copy shows the contact frame.");
        }

        hostDamage.ApplyHeal(hostPlayerReceiver, 1000);
        int startsBeforeStun = hostStarts;
        yield return SceneBootTestHelper.WaitUntil(() => hostStarts > startsBeforeStun && hostDealer.Phase == AttackPhase.Windup && copy.Animator.IsActing && copy.Animator.Frame < copy.Animator.Clip.ContactFrame, "the next windup on the host and the client's copy");
        StatusEffectService hostStatuses = gameplay.Resolve<StatusEffectService>();
        Assert.That(hostStatuses.Apply(hostMob.GetComponent<DamageReceiver>(), SceneBootTestHelper.FindStatus("Stun")), Is.EqualTo(StatusApplyOutcome.Landed));
        yield return SceneBootTestHelper.WaitUntil(() => !copy.Animator.IsActing, "the stun to stop the attack on the client's copy");
        Assert.That(hostDealer.IsAttacking, Is.False);
    }

    /// <summary>
    /// The spawned copy of <paramref name="original"/> (the same network object) that belongs to
    /// <paramref name="manager"/>, or null.
    /// </summary>
    private static MobController FindCopyManagedBy(NetworkManager manager, MobController original)
    {
        ulong id = original.GetComponent<NetworkObject>().NetworkObjectId;
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            NetworkObject networkObject = mob.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsSpawned && networkObject.NetworkManager == manager && networkObject.NetworkObjectId == id)
            {
                return mob;
            }
        }

        return null;
    }
}
