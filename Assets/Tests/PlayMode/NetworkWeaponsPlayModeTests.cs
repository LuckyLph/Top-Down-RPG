using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// PlayMode tests for weapons online (Docs/Animation.md, Phase 4) between a host and an in-process client: a client's
/// weapon switch reaching the host's copy (its weapon, its weapon abilities and the clip its attacks play), an attack
/// made on the frame of a switch playing out on the host's copy whichever arrives first, a client's arrow resolving
/// exactly once on the host from where the owner stood, a client joining after the host switched weapons seeing
/// the host's current weapon, and, under a delayed connection (Docs/KnownGaps.md, Phase 4), a copy's windup ending
/// without its contact while the late contact still lands its hit.
/// </summary>
public class NetworkWeaponsPlayModeTests
{
    private const int GreatswordIndex = 1;
    private const int LongbowIndex = 2;

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
    /// A client equipping the greatsword reaches the host's copy of its player, which equips it with its two
    /// abilities in the weapon slots; the client's next attack plays the greatsword's clip on that copy.
    /// </summary>
    [UnityTest]
    public IEnumerator AClientsWeaponSwitch_ReachesTheHostsCopy_WithItsAbilitiesAndClip()
    {
        yield return HostAndJoin();
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerRegistry hostPlayers = gameplay.Resolve<PlayerRegistry>();
        PlayerController clientOwned = client.LocalPlayer.Current.Controller;
        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(hostPlayers, client.Manager.LocalClientId);
        PlayerWeaponController ownerWeapon = clientOwned.GetComponent<PlayerWeaponController>();
        PlayerWeaponController copyWeapon = hostCopy.GetComponent<PlayerWeaponController>();
        PlayerAbilities copyAbilities = hostCopy.GetComponent<PlayerAbilities>();
        PlayerWeapon greatsword = ownerWeapon.Catalog.Get(GreatswordIndex);
        Assert.That(copyWeapon.CurrentWeapon, Is.Not.SameAs(greatsword));

        Assert.That(ownerWeapon.EquipFromCatalog(GreatswordIndex), Is.True);
        yield return SceneBootTestHelper.WaitUntil(() => copyWeapon.CurrentWeapon == greatsword, "the host's copy to equip the client's greatsword");
        Assert.That(copyAbilities.GetAbility(PlayerAbilities.ClassSlotCount), Is.SameAs(greatsword.GetAbility(0)));
        Assert.That(copyAbilities.GetAbility(PlayerAbilities.ClassSlotCount + 1), Is.SameAs(greatsword.GetAbility(1)));

        CharacterAnimator copyAnimation = hostCopy.GetComponentInChildren<CharacterVisuals>().Animator;
        Assert.That(ownerWeapon.TryAttack(Vector2.right), Is.True);
        yield return SceneBootTestHelper.WaitUntil(() => copyAnimation.IsActing, "the client's attack to play on the host's copy");
        Assert.That(copyAnimation.Clip.Name, Is.EqualTo(greatsword.AttackClip), "The copy plays the greatsword's attack.");
    }

    /// <summary>
    /// A client switching to the greatsword and attacking on the same frame plays the greatsword's attack on the host's
    /// copy through its contact, the switch arriving after the attack's message or before it: the attack's message goes
    /// out at once, the switch (a network variable) on the next network tick, and its arrival must not stop the attack.
    /// </summary>
    [UnityTest]
    public IEnumerator AClientsAttackRightAfterASwitch_PlaysOutOnTheHostsCopy()
    {
        yield return HostAndJoin();
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerController clientOwned = client.LocalPlayer.Current.Controller;
        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(gameplay.Resolve<PlayerRegistry>(), client.Manager.LocalClientId);
        PlayerWeaponController ownerWeapon = clientOwned.GetComponent<PlayerWeaponController>();
        PlayerWeaponController copyWeapon = hostCopy.GetComponent<PlayerWeaponController>();
        CharacterAnimator copyAnimation = hostCopy.GetComponentInChildren<CharacterVisuals>().Animator;
        PlayerWeapon greatsword = ownerWeapon.Catalog.Get(GreatswordIndex);
        bool ownerContacted = false;
        System.Action<int, Vector2, Vector2, UnitTarget> onContact = (_, _, _, _) => ownerContacted = true;
        ownerWeapon.AttackContacted += onContact;
        try
        {
            Assert.That(ownerWeapon.EquipFromCatalog(GreatswordIndex), Is.True);
            Assert.That(ownerWeapon.TryAttack(Vector2.right), Is.True);
            yield return SceneBootTestHelper.WaitUntil(() => copyAnimation.IsActing, "the client's attack to play on the host's copy");
            Assert.That(copyAnimation.Clip.Name, Is.EqualTo(greatsword.AttackClip));

            bool contactShown = false;
            for (float until = Time.time + 2f; Time.time < until && !contactShown;)
            {
                yield return null;
                Assert.That(copyAnimation.IsActing, Is.True, $"The copy's attack plays on (the switch {(copyWeapon.CurrentWeapon == greatsword ? "arrived" : "has not arrived")}).");
                contactShown = copyAnimation.ActionPhase == AttackPhase.Contact;
            }

            Assert.That(ownerContacted, Is.True);
            Assert.That(contactShown, Is.True, "The copy shows the attack's contact.");
            Assert.That(copyWeapon.CurrentWeapon, Is.SameAs(greatsword), "The switch reached the copy.");
        }
        finally
        {
            ownerWeapon.AttackContacted -= onContact;
        }
    }

    /// <summary>
    /// A client's longbow shot at a dummy three units away is resolved by the host exactly once, with the host's copy of
    /// the client's player as its source and the bow's damage (up to the dummy's 10 HP), its arrow leaving from where
    /// the owner stood (the host's copy is held five units away with its network transform off, so an arrow from the
    /// copy would miss).
    /// </summary>
    [UnityTest]
    public IEnumerator AClientsArrow_ResolvesExactlyOnceOnTheHost()
    {
        yield return HostAndJoin();
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerRegistry hostPlayers = gameplay.Resolve<PlayerRegistry>();
        PlayerController clientOwned = client.LocalPlayer.Current.Controller;
        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(hostPlayers, client.Manager.LocalClientId);
        PlayerWeaponController ownerWeapon = clientOwned.GetComponent<PlayerWeaponController>();
        PlayerWeaponController copyWeapon = hostCopy.GetComponent<PlayerWeaponController>();
        Assert.That(ownerWeapon.EquipFromCatalog(LongbowIndex), Is.True);
        PlayerWeapon longbow = ownerWeapon.CurrentWeapon;
        yield return SceneBootTestHelper.WaitUntil(() => copyWeapon.CurrentWeapon == longbow, "the host's copy to equip the longbow");
        yield return new WaitForSeconds(0.3f);

        Vector2 owner = clientOwned.transform.position;
        Health dummy = CreateDummy(owner + new Vector2(3f, 0f));
        int hits = 0;
        Health.DamageEvent landed = default;
        dummy.Damaged += hit =>
        {
            hits++;
            landed = hit;
        };
        NetworkTransform copyTransform = hostCopy.GetComponent<NetworkTransform>();
        copyTransform.enabled = false;
        Vector2 heldAway = owner + Vector2.down * 5f;
        hostCopy.GetComponent<Rigidbody2D>().position = heldAway;
        hostCopy.transform.position = heldAway;
        try
        {
            UnitTarget target = new(dummy, dummy.GetComponent<Collider2D>(), UnitTeam.Enemy);
            Assert.That(ownerWeapon.TryAttack(Vector2.right, target), Is.True);
            yield return SceneBootTestHelper.WaitUntil(() => hits > 0, "the host to resolve the client's arrow");
            Assert.That(Vector2.Distance(hostCopy.transform.position, heldAway), Is.LessThan(0.01f), "The host's copy stayed away from the dummy.");
            yield return new WaitForSeconds(1f);
            Assert.That(hits, Is.EqualTo(1), "The host resolves the arrow exactly once.");
            Assert.That(landed.Source, Is.SameAs(hostCopy.gameObject), "The host's copy of the shooter is the hit's source.");
            Assert.That(landed.Amount, Is.EqualTo(Mathf.Min(longbow.Damage, dummy.MaxHealth)));
        }
        finally
        {
            copyTransform.enabled = true;
            Object.Destroy(dummy.gameObject);
        }
    }

    /// <summary>
    /// A client joining after the host switched to the longbow gets a copy of the host's player with the longbow and
    /// its abilities equipped.
    /// </summary>
    [UnityTest]
    public IEnumerator AClientJoiningAfterTheHostSwitched_SeesTheHostsWeapon()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        DisableMobs();

        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerWeaponController hostWeapon = gameplay.Resolve<LocalPlayerTracker>().Current.Weapon;
        Assert.That(hostWeapon.EquipFromCatalog(LongbowIndex), Is.True);
        PlayerWeapon longbow = hostWeapon.CurrentWeapon;
        yield return new WaitForSeconds(0.2f);

        client = InProcessClient.Start(session, gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendReady();
        yield return SceneBootTestHelper.WaitUntil(
            () => InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId) != null,
            "the client to get a copy of the host's player");
        PlayerController copy = InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId);
        yield return null;

        Assert.That(copy.GetComponent<PlayerWeaponController>().CurrentWeapon, Is.SameAs(longbow), "A late joiner sees the host's weapon.");
        Assert.That(copy.GetComponent<PlayerAbilities>().GetAbility(PlayerAbilities.ClassSlotCount), Is.SameAs(longbow.GetAbility(0)));
    }

    /// <summary>
    /// Under a typical delayed connection, the client's sword attack on a Weasel in reach plays on the host's copy, and
    /// everything the client sends is dropped from the moment its start arrived, so its contact does not come: the copy
    /// holds its last windup frame for the character's contact wait past its windup, then goes back to its locomotion
    /// without ever showing the contact. Once the connection comes back, the late contact still lands its hit on the
    /// host's Weasel, and the copy stays in its locomotion.
    /// </summary>
    [UnityTest]
    public IEnumerator ACopysWindup_EndsWithoutItsContact_AndTheLateContactStillLands()
    {
        MobController mob = null;
        yield return HostAndJoin(true, kept => mob = kept);
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerController clientOwned = client.LocalPlayer.Current.Controller;
        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(gameplay.Resolve<PlayerRegistry>(), client.Manager.LocalClientId);
        PlayerWeaponController ownerWeapon = clientOwned.GetComponent<PlayerWeaponController>();
        CharacterVisuals copyVisuals = hostCopy.GetComponentInChildren<CharacterVisuals>();
        CharacterAnimator copyAnimation = copyVisuals.Animator;
        Health mobHealth = mob.GetComponent<Health>();
        Vector2 inReach = (Vector2)clientOwned.transform.position + new Vector2(0.8f, 0f);
        mob.GetComponent<Rigidbody2D>().position = inReach;
        mob.transform.position = inReach;
        Physics2D.SyncTransforms();
        delay.Set(NetworkDelay.Conditions.Typical);
        yield return new WaitForSecondsRealtime(0.3f);

        Assert.That(ownerWeapon.TryAttack(Vector2.right), Is.True);
        yield return SceneBootTestHelper.WaitUntil(() => copyAnimation.IsActing && copyAnimation.ActionKind == CharacterActionKind.Attack, "the attack's start to reach the host's copy");
        float startedAt = Time.time;
        delay.SetSending(client.Manager, NetworkDelay.Conditions.Typical.OneWayMs, NetworkDelay.Conditions.Typical.JitterMs, 100);
        bool contactShown = false;
        while (copyAnimation.IsActing)
        {
            contactShown |= copyAnimation.ActionPhase == AttackPhase.Contact;
            Assert.That(Time.time - startedAt, Is.LessThan(3f), "The copy's windup should not hold for ever.");
            yield return null;
        }

        float heldFor = Time.time - startedAt;
        float expected = ownerWeapon.CurrentWeapon.Timeline.Windup + copyVisuals.Locomotion.ContactWaitSeconds;
        Assert.That(contactShown, Is.False, "The copy never showed a contact that never came.");
        Assert.That(heldFor, Is.InRange(expected - 0.05f, expected + 0.1f), "The copy waited its contact wait past the windup.");
        Assert.That(copyAnimation.Clip, Is.SameAs(copyVisuals.AnimationSet.Locomotion(CharacterGait.Idle)), "Back to its locomotion.");
        Assert.That(mobHealth.CurrentHealth, Is.EqualTo(mobHealth.MaxHealth), "The contact has not landed yet.");

        delay.Set(NetworkDelay.Conditions.Typical);
        yield return SceneBootTestHelper.WaitUntil(() => mobHealth.CurrentHealth < mobHealth.MaxHealth, "the late contact to land its hit on the host");
        Assert.That(copyAnimation.IsActing, Is.False, "The late contact does not play the swing on the copy.");
    }

    /// <summary>
    /// Hosts from the menu with every mob deactivated, or all but one kept with its AI off and handed to
    /// <paramref name="keepOneMob"/>, the delayed connection installed first when <paramref name="delayed"/> (with no
    /// delay yet), then starts an in-process client and waits until both machines see both players, with the client's
    /// colliders off (and its copy of the kept mob's).
    /// </summary>
    private IEnumerator HostAndJoin(bool delayed = false, System.Action<MobController> keepOneMob = null)
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        if (delayed)
        {
            delay = NetworkDelay.Install();
        }

        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        MobController kept = null;
        if (keepOneMob != null)
        {
            kept = Object.FindAnyObjectByType<MobController>();
            Assert.That(kept, Is.Not.Null, "A mob should be loaded around the start.");
            kept.enabled = false;
            keepOneMob(kept);
        }

        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            if (mob != kept)
            {
                mob.gameObject.SetActive(false);
            }
        }

        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerRegistry hostPlayers = gameplay.Resolve<PlayerRegistry>();
        client = InProcessClient.Start(session, gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendReady();
        yield return SceneBootTestHelper.WaitUntil(
            () => hostPlayers.Players.Count == 2 && client.Players.Players.Count == 2 && client.LocalPlayer.Current != null,
            "both sides to see both players");
        client.DisableColliders();
        if (kept == null)
        {
            yield break;
        }

        ulong keptId = kept.GetComponent<NetworkObject>().NetworkObjectId;
        MobController copy = null;
        yield return SceneBootTestHelper.WaitUntil(() => (copy = CopyOf(keptId)) != null, "the client's copy of the kept mob");
        foreach (Collider2D body in copy.GetComponentsInChildren<Collider2D>())
        {
            body.enabled = false;
        }
    }

    /// <summary>
    /// The client's spawned copy of the mob whose network object id is <paramref name="id"/>, or null.
    /// </summary>
    private MobController CopyOf(ulong id)
    {
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            NetworkObject networkObject = mob.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsSpawned && networkObject.NetworkManager == client.Manager && networkObject.NetworkObjectId == id)
            {
                return mob;
            }
        }

        return null;
    }

    /// <summary>
    /// Deactivates every mob so none attacks or blocks a shot during the test.
    /// </summary>
    private static void DisableMobs()
    {
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            mob.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// A unit on the <c>Enemy</c> layer with a small collider, health and a receiver, standing at
    /// <paramref name="position"/> in the shared scene; only the host can hurt it.
    /// </summary>
    private static Health CreateDummy(Vector2 position)
    {
        GameObject dummy = new("ArrowDummy") { layer = LayerMask.NameToLayer("Enemy") };
        dummy.transform.position = position;
        dummy.AddComponent<BoxCollider2D>().size = new Vector2(0.4f, 0.4f);
        dummy.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        Health health = dummy.AddComponent<Health>();
        dummy.AddComponent<DamageReceiver>();
        Physics2D.SyncTransforms();
        return health;
    }
}
