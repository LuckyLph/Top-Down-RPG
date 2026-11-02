using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// PlayMode tests for the lantern online (Docs/Lighting.md, Phase 9) between a host and an in-process client: a
/// client's lantern key lighting its lantern and putting it out on the host's copy of its player, the host's lantern
/// reaching the client's copy of the host's player, and a client joining after the host lit its lantern seeing it lit.
/// </summary>
public class NetworkLanternPlayModeTests
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
    /// The client's lantern key lights its lantern, the client's machine deciding it, and the host's copy of its player
    /// burns it too, drawing its layer and casting its light; the key again puts it out on both.
    /// </summary>
    [UnityTest]
    public IEnumerator AClientsLanternKey_LightsAndPutsOutItsLanternOnTheHostsCopy()
    {
        yield return HostAndJoin();
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(gameplay.Resolve<PlayerRegistry>(), client.Manager.LocalClientId);
        PlayerLantern owner = client.LocalPlayer.Current.Controller.GetComponent<PlayerLantern>();
        PlayerLantern copy = hostCopy.GetComponent<PlayerLantern>();
        CharacterVisuals copyVisuals = hostCopy.GetComponentInChildren<CharacterVisuals>();
        Assert.That(owner.Deciding, Is.True, "The client's machine decides its lantern.");
        Assert.That(copy.Deciding, Is.False);
        Assert.That(owner.Lit, Is.False);
        Assert.That(copy.Lit, Is.False);

        yield return PressLantern();
        Assert.That(owner.Lit, Is.True, "The key lights it.");
        yield return SceneBootTestHelper.WaitUntil(() => copy.Lit, "the host's copy to light the client's lantern");
        yield return null;
        Assert.That(copyVisuals.LanternLit, Is.True);
        Assert.That(copyVisuals.LanternLight.enabled, Is.True, "The copy casts the lantern's light.");

        yield return PressLantern();
        Assert.That(owner.Lit, Is.False, "The key puts it out.");
        yield return SceneBootTestHelper.WaitUntil(() => !copy.Lit, "the host's copy to put the client's lantern out");
        yield return null;
        Assert.That(copyVisuals.LanternLight.enabled, Is.False);
        Assert.That(copyVisuals.LanternRenderer.sprite, Is.Null);
    }

    /// <summary>
    /// The host's lantern reaches the client's copy of the host's player as it is, and each time its key lights it or
    /// puts it out.
    /// </summary>
    [UnityTest]
    public IEnumerator TheHostsLantern_ReachesTheClientsCopy()
    {
        yield return HostAndJoin();
        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerLantern hostLantern = gameplay.Resolve<LocalPlayerTracker>().Current.Controller.GetComponent<PlayerLantern>();
        PlayerLantern copy = InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId).GetComponent<PlayerLantern>();
        bool before = hostLantern.Lit;
        yield return SceneBootTestHelper.WaitUntil(() => copy.Lit == before, "the client's copy to show the host's lantern as it is");

        hostLantern.Toggle();
        yield return SceneBootTestHelper.WaitUntil(() => copy.Lit == !before, "the client's copy to follow the host's key");
        hostLantern.Toggle();
        yield return SceneBootTestHelper.WaitUntil(() => copy.Lit == before, "the client's copy to follow the host's key back");
    }

    /// <summary>
    /// A client joining after the host lit its lantern gets a copy of the host's player with it lit.
    /// </summary>
    [UnityTest]
    public IEnumerator AClientJoiningAfterTheHostLitItsLantern_SeesItLit()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        DisableMobs();

        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerLantern hostLantern = gameplay.Resolve<LocalPlayerTracker>().Current.Controller.GetComponent<PlayerLantern>();
        if (!hostLantern.Lit)
        {
            hostLantern.Toggle();
        }

        Assert.That(hostLantern.Lit, Is.True);
        yield return new WaitForSeconds(0.2f);

        client = InProcessClient.Start(session, gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendReady();
        yield return SceneBootTestHelper.WaitUntil(
            () => InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId) != null,
            "the client to get a copy of the host's player");
        PlayerController copy = InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId);
        yield return null;

        Assert.That(copy.GetComponent<PlayerLantern>().Lit, Is.True, "A late joiner sees the host's lantern lit.");
        Assert.That(copy.GetComponentInChildren<CharacterVisuals>().LanternLit, Is.True);
    }

    /// <summary>
    /// Presses the client's lantern key for one frame.
    /// </summary>
    private IEnumerator PressLantern()
    {
        client.Input.LanternPressedThisFrame = true;
        yield return null;
        client.Input.LanternPressedThisFrame = false;
        yield return null;
    }

    /// <summary>
    /// Hosts from the menu with every mob deactivated, then starts an in-process client and waits until both machines
    /// see both players, with the client's colliders off.
    /// </summary>
    private IEnumerator HostAndJoin()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        DisableMobs();

        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerRegistry hostPlayers = gameplay.Resolve<PlayerRegistry>();
        client = InProcessClient.Start(session, gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendReady();
        yield return SceneBootTestHelper.WaitUntil(
            () => hostPlayers.Players.Count == 2 && client.Players.Players.Count == 2 && client.LocalPlayer.Current != null,
            "both sides to see both players");
        client.DisableColliders();
    }

    /// <summary>
    /// Deactivates every mob so none attacks during the test.
    /// </summary>
    private static void DisableMobs()
    {
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            mob.gameObject.SetActive(false);
        }
    }
}
