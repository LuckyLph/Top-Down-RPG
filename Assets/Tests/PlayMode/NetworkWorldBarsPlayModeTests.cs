using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// PlayMode tests for the players' world bars (Docs/UiRevamp.md, Phase 4), with the in-process host and client: the
/// client's copy of the host's player shows the host's health, debuffs and casts from what is already replicated, the
/// host's own bar shows its own cast, the host's copy of the client's player shows the client's cast as the client's
/// own bar does, and a client that joins after the host was hurt sees the host's current health from its spawn.
/// </summary>
public class NetworkWorldBarsPlayModeTests
{
    private const int BoltSlot = 0;

    private InProcessClient client;

    /// <summary>
    /// Disposes the in-process client.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        client?.Dispose();
        client = null;
    }

    /// <summary>
    /// The host is hurt, chilled and casts Bolt: the client's copy of the host's player shows the lower health, a pip
    /// for the chill and the cast bar while Bolt casts, hidden again once the host completes it; the host's own bar
    /// shows its cast as the owner. The client casts Bolt: its own bar and the host's copy of its player show the cast.
    /// </summary>
    [UnityTest]
    public IEnumerator TheClient_SeesTheHostsBarPipsAndCast_AndTheHostTheClients()
    {
        yield return Host();
        yield return Join();
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerController hostOwned = gameplay.Resolve<LocalPlayerTracker>().Current.Controller;
        PlayerController hostOnClient = InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId);
        WorldHealthBar copyBar = hostOnClient.GetComponentInChildren<WorldHealthBar>();
        WorldStatusIcons copyPips = hostOnClient.GetComponentInChildren<WorldStatusIcons>();
        WorldCastBar copyCast = hostOnClient.GetComponentInChildren<WorldCastBar>();
        WorldCastBar ownCast = hostOwned.GetComponentInChildren<WorldCastBar>();
        Assert.That(copyBar.IsShown, Is.True, "Every player has a world bar on every machine.");
        Assert.That(copyBar.FillFraction, Is.EqualTo(1f));
        Assert.That(hostOwned.GetComponentInChildren<WorldHealthBar>().IsShown, Is.True, "The local player's own bar shows too.");

        Health hostHealth = hostOwned.GetComponent<Health>();
        gameplay.Resolve<DamageService>().ApplyDamage(hostOwned.GetComponent<DamageReceiver>(), 30);
        float hurt = (float)hostHealth.CurrentHealth / hostHealth.MaxHealth;
        Assert.That(hurt, Is.LessThan(1f));
        yield return SceneBootTestHelper.WaitUntil(() => Mathf.Approximately(copyBar.FillFraction, hurt), "the host's health to reach the client's copy");

        StatusEffectDefinition chill = SceneBootTestHelper.FindStatus("Chill");
        Assert.That(gameplay.Resolve<StatusEffectService>().Apply(hostOwned.GetComponent<DamageReceiver>(), chill), Is.EqualTo(StatusApplyOutcome.Landed));
        yield return SceneBootTestHelper.WaitUntil(() => copyPips.ShownCount == 1, "the host's chill to show as a pip on the client's copy");

        PlayerAbilities hostAbilities = hostOwned.GetComponent<PlayerAbilities>();
        Assert.That(hostAbilities.GetAbility(BoltSlot).CastTime, Is.GreaterThan(0f));
        Assert.That(hostAbilities.TryCast(BoltSlot, new CastAim(Vector2.zero, Vector2.up, default)), Is.EqualTo(CastOutcome.Started));
        Assert.That(ownCast.IsShown, Is.True, "The host's own bar shows its cast at once.");
        yield return SceneBootTestHelper.WaitUntil(() => copyCast.IsShown, "the host's cast to show on the client's copy");
        yield return SceneBootTestHelper.WaitUntil(() => copyCast.FillFraction > 0f, "the copy's cast bar to fill");
        Assert.That(copyCast.IsShown, Is.True, "The copy's cast bar stays until the host's completion.");

        hostAbilities.CompleteCast(BoltSlot);
        Assert.That(ownCast.IsShown, Is.False);
        yield return SceneBootTestHelper.WaitUntil(() => !copyCast.IsShown, "the host's completion to hide the client's cast bar");

        PlayerController clientOwned = client.LocalPlayer.Current.Controller;
        PlayerController clientOnHost = InProcessClient.FindCopyOwnedBy(gameplay.Resolve<PlayerRegistry>(), client.Manager.LocalClientId);
        WorldCastBar clientOwnCast = clientOwned.GetComponentInChildren<WorldCastBar>();
        WorldCastBar clientCopyCast = clientOnHost.GetComponentInChildren<WorldCastBar>();
        client.Input.PointAt((Vector2)clientOwned.transform.position + Vector2.up * 3f);
        client.Input.AbilityPressedThisFrame = BoltSlot;
        yield return null;
        client.Input.AbilityPressedThisFrame = -1;
        yield return SceneBootTestHelper.WaitUntil(() => clientOwnCast.IsShown && clientCopyCast.IsShown, "the client's cast to show on its own bar and on the host's copy");
        yield return SceneBootTestHelper.WaitUntil(() => !clientOwnCast.IsShown && !clientCopyCast.IsShown, "the client's cast to end on both machines");
    }

    /// <summary>
    /// The host is hurt before the client joins: the client's copy of the host's player shows the host's current
    /// health from its spawn, not a full bar.
    /// </summary>
    [UnityTest]
    public IEnumerator ALateJoiner_SeesTheHostsCurrentHealth()
    {
        yield return Host();
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerController hostOwned = gameplay.Resolve<LocalPlayerTracker>().Current.Controller;
        Health hostHealth = hostOwned.GetComponent<Health>();
        gameplay.Resolve<DamageService>().ApplyDamage(hostOwned.GetComponent<DamageReceiver>(), 40);
        float hurt = (float)hostHealth.CurrentHealth / hostHealth.MaxHealth;
        Assert.That(hurt, Is.LessThan(1f));

        yield return Join();
        PlayerController hostOnClient = InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId);
        WorldHealthBar copyBar = hostOnClient.GetComponentInChildren<WorldHealthBar>();
        yield return null;
        Assert.That(copyBar.IsShown, Is.True);
        Assert.That(copyBar.FillFraction, Is.EqualTo(hurt).Within(1e-4f), "The joiner's copy shows the host's current health from its spawn.");
    }

    /// <summary>
    /// Boots into the main menu and hosts a game, deactivating every mob.
    /// </summary>
    private static IEnumerator Host()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            mob.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Starts the in-process client and waits until both sides see both players.
    /// </summary>
    private IEnumerator Join()
    {
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerRegistry hostPlayers = gameplay.Resolve<PlayerRegistry>();
        client = InProcessClient.Start(SceneBootTestHelper.ResolveFromMain<NetworkSession>(), gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendReady();
        yield return SceneBootTestHelper.WaitUntil(
            () => hostPlayers.Players.Count == 2 && client.Players.Players.Count == 2 && client.LocalPlayer.Current != null,
            "both sides to see both players");
    }
}
