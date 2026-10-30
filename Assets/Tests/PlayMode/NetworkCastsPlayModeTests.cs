using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// PlayMode tests for cast animation online (Docs/Animation.md, Phase 5) between a host and an in-process client: a
/// client's cast plays on the host's copy and shows its release when the host resolves the cast, its cast light
/// (Docs/Lighting.md, Phase 2) shining in Bolt's colour at the release and off once the cast is over; a client's cast
/// interrupted mid-way stops the host's copy before its release and never resolves; a client switching weapons mid-cast
/// has the host resolve the ability it cast; and the host's cast plays on the client's copy of the host's player.
/// </summary>
public class NetworkCastsPlayModeTests
{
    private const int BoltSlot = 0;
    private const int MendSlot = 2;
    private const int WeaponSlot = PlayerAbilities.ClassSlotCount;
    private const int GreatswordIndex = 1;

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
    /// A client casting Bolt plays it on the host's copy of its player, which shows its release frame within a network
    /// tick or two of the host resolving the cast (both follow the owner's messages, a start and a completion), and
    /// plays its follow-through after; the copy's cast light shines in Bolt's light colour at the release and goes off
    /// once the cast is over, with nothing sent for it.
    /// </summary>
    [UnityTest]
    public IEnumerator AClientsCast_PlaysOnTheHostsCopy_AndReleasesAtCompletion()
    {
        yield return HostAndJoin();
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerController clientOwned = client.LocalPlayer.Current.Controller;
        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(gameplay.Resolve<PlayerRegistry>(), client.Manager.LocalClientId);
        PlayerAbilities copyAbilities = hostCopy.GetComponent<PlayerAbilities>();
        CharacterVisuals copyVisuals = hostCopy.GetComponentInChildren<CharacterVisuals>();
        CharacterAnimator copyAnimation = copyVisuals.Animator;
        CharacterAnimationClip clip = copyVisuals.AnimationSet.Find(copyAbilities.GetAbility(BoltSlot).CastClip);
        float resolvedAt = -1f;
        gameplay.Resolve<AbilityService>().CastResolved += (caster, cast) =>
        {
            if (caster == copyAbilities && cast.Slot == BoltSlot && resolvedAt < 0f)
            {
                resolvedAt = Time.time;
            }
        };

        client.Input.PointAt((Vector2)clientOwned.transform.position + Vector2.right * 3f);
        client.Input.AbilityPressedThisFrame = BoltSlot;
        yield return null;
        client.Input.AbilityPressedThisFrame = -1;

        bool played = false;
        float releasedAt = -1f;
        bool litAtRelease = false;
        for (float until = Time.time + 3f; Time.time < until && (resolvedAt < 0f || releasedAt < 0f);)
        {
            yield return null;
            played |= copyAnimation.IsActing && copyAnimation.Clip == clip;
            if (releasedAt < 0f && copyAnimation.IsActing && copyAnimation.Clip == clip && copyAnimation.ActionPhase == AttackPhase.Contact)
            {
                releasedAt = Time.time;
                litAtRelease = copyVisuals.CastLight.enabled && copyVisuals.CastLight.color == clip.LightColour;
            }
        }

        Assert.That(played, Is.True, "The client's cast plays on the host's copy.");
        Assert.That(resolvedAt, Is.GreaterThanOrEqualTo(0f), "The host resolves the client's cast.");
        Assert.That(releasedAt, Is.GreaterThanOrEqualTo(0f), "The host's copy shows the release.");
        Assert.That(Mathf.Abs(releasedAt - resolvedAt), Is.LessThan(0.1f), "The copy releases when the cast completes, within a few network ticks.");
        Assert.That(litAtRelease, Is.True, "The copy's cast light shines in Bolt's colour at the release.");
        yield return SceneBootTestHelper.WaitUntil(() => !copyAnimation.IsActing, "the copy's cast to end");
        yield return null;
        Assert.That(copyVisuals.CastLight.enabled, Is.False, "The copy's cast light is off once the cast is over.");
    }

    /// <summary>
    /// A client casting Mend (0.5 s) on the host's player and teleporting 0.15 s into it cancels the cast: the host's
    /// copy stops it at once, never shows its release, and the cast is never resolved.
    /// </summary>
    [UnityTest]
    public IEnumerator AClientsInterruptedCast_StopsTheHostsCopy()
    {
        yield return HostAndJoin();
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerRegistry hostPlayers = gameplay.Resolve<PlayerRegistry>();
        PlayerController clientOwned = client.LocalPlayer.Current.Controller;
        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(hostPlayers, client.Manager.LocalClientId);
        PlayerController hostPlayerOnClient = InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId);
        PlayerAbilities copyAbilities = hostCopy.GetComponent<PlayerAbilities>();
        CharacterVisuals copyVisuals = hostCopy.GetComponentInChildren<CharacterVisuals>();
        CharacterAnimator copyAnimation = copyVisuals.Animator;
        CharacterAnimationClip clip = copyVisuals.AnimationSet.Find(copyAbilities.GetAbility(MendSlot).CastClip);
        int resolved = 0;
        gameplay.Resolve<AbilityService>().CastResolved += (caster, _) => resolved += caster == copyAbilities ? 1 : 0;

        clientOwned.Teleport((Vector2)hostPlayerOnClient.transform.position + Vector2.right * 2f);
        yield return null;
        Physics2D.SyncTransforms();
        client.Input.PointAt(hostPlayerOnClient.transform.position);
        client.Input.AbilityPressedThisFrame = MendSlot;
        yield return null;
        client.Input.AbilityPressedThisFrame = -1;
        Assert.That(clientOwned.CurrentOrder, Is.EqualTo(PlayerOrderKind.Casting), "The client casts Mend on the host's player.");

        yield return SceneBootTestHelper.WaitUntil(() => copyAnimation.IsActing && copyAnimation.Clip == clip, "the client's Mend to play on the host's copy");
        yield return new WaitForSeconds(0.15f);
        clientOwned.Teleport((Vector2)clientOwned.transform.position + Vector2.down * 2f);
        bool released = false;
        float until = Time.time + 0.25f;
        while (Time.time < until && copyAnimation.IsActing && copyAnimation.Clip == clip)
        {
            released |= copyAnimation.ActionPhase == AttackPhase.Contact;
            yield return null;
        }

        Assert.That(copyAnimation.IsActing && copyAnimation.Clip == clip, Is.False, "The cancel stops the host's copy before the cast could end.");
        Assert.That(released, Is.False, "The copy never shows a release for a cast that never completes.");
        yield return new WaitForSeconds(0.6f);
        Assert.That(resolved, Is.Zero, "The interrupted cast never resolves.");
    }

    /// <summary>
    /// A client switching to the greatsword in the middle of its sword's Whirl has the host resolve Whirl, the ability
    /// it cast, rather than the greatsword's ability in that slot: a running cast keeps its ability across a switch.
    /// </summary>
    [UnityTest]
    public IEnumerator AClientsSwitchMidCast_ResolvesTheAbilityItCast()
    {
        yield return HostAndJoin();
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerController clientOwned = client.LocalPlayer.Current.Controller;
        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(gameplay.Resolve<PlayerRegistry>(), client.Manager.LocalClientId);
        PlayerAbilities copyAbilities = hostCopy.GetComponent<PlayerAbilities>();
        PlayerWeaponController ownerWeapon = clientOwned.GetComponent<PlayerWeaponController>();
        AbilityDefinition cast = clientOwned.GetComponent<PlayerAbilities>().GetAbility(WeaponSlot);
        Assert.That(ownerWeapon.Catalog.Get(GreatswordIndex).GetAbility(0), Is.Not.SameAs(cast), "The greatsword has another ability in the slot.");
        AbilityDefinition resolved = null;
        gameplay.Resolve<AbilityService>().CastResolved += (caster, done) =>
        {
            if (caster == copyAbilities && done.Slot == WeaponSlot && resolved == null)
            {
                resolved = done.Ability;
            }
        };

        client.Input.PointAt((Vector2)clientOwned.transform.position + Vector2.right * 3f);
        client.Input.AbilityPressedThisFrame = WeaponSlot;
        yield return null;
        client.Input.AbilityPressedThisFrame = -1;
        Assert.That(clientOwned.CurrentOrder, Is.EqualTo(PlayerOrderKind.Casting), "The client casts its sword's ability.");
        Assert.That(ownerWeapon.EquipFromCatalog(GreatswordIndex), Is.True);

        yield return SceneBootTestHelper.WaitUntil(() => resolved != null, "the host to resolve the client's cast");
        Assert.That(resolved, Is.SameAs(cast), "The host resolves the ability the client cast.");
    }

    /// <summary>
    /// The host casting Bolt plays it on the client's copy of the host's player, toward the host's aim.
    /// </summary>
    [UnityTest]
    public IEnumerator TheHostsCast_PlaysOnTheClientsCopy()
    {
        yield return HostAndJoin();
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerAbilities hostAbilities = gameplay.Resolve<LocalPlayerTracker>().Current.Controller.GetComponent<PlayerAbilities>();
        PlayerController hostPlayerOnClient = InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId);
        CharacterVisuals copyVisuals = hostPlayerOnClient.GetComponentInChildren<CharacterVisuals>();
        CharacterAnimator copyAnimation = copyVisuals.Animator;
        CharacterAnimationClip clip = copyVisuals.AnimationSet.Find(hostAbilities.GetAbility(BoltSlot).CastClip);

        Assert.That(hostAbilities.TryCast(BoltSlot, new CastAim(Vector2.zero, Vector2.up, default)), Is.EqualTo(CastOutcome.Started));
        yield return SceneBootTestHelper.WaitUntil(() => copyAnimation.IsActing && copyAnimation.Clip == clip, "the host's cast to play on the client's copy");
        yield return null;
        Assert.That(copyAnimation.IsActing, Is.True);
        Assert.That(copyAnimation.Direction, Is.EqualTo(CharacterDirections.North), "The copy casts toward the host's aim.");
        hostAbilities.CompleteCast(BoltSlot);
    }

    /// <summary>
    /// Hosts from the menu with every mob deactivated, then starts an in-process client and waits until both machines
    /// see both players.
    /// </summary>
    private IEnumerator HostAndJoin()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            mob.gameObject.SetActive(false);
        }

        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerRegistry hostPlayers = gameplay.Resolve<PlayerRegistry>();
        client = InProcessClient.Start(session, gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendReady();
        yield return SceneBootTestHelper.WaitUntil(
            () => hostPlayers.Players.Count == 2 && client.Players.Players.Count == 2 && client.LocalPlayer.Current != null,
            "both sides to see both players");
    }
}
