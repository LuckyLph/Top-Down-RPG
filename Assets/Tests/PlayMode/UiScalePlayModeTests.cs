using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using VContainer;

/// <summary>
/// PlayMode tests for the UI scale (Docs/DesignSystem.md, Phase 3): the main menu's canvas follows the UI zoom from the
/// boot; and while hosting with an in-process client, the host's HUD, popup and in-game menu canvases follow its UI size
/// step, which changes nothing the client shows: its own settings, and its copy of the host's player.
/// </summary>
public class UiScalePlayModeTests
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
    /// The main menu's canvas is at a constant pixel size of 32 reference pixels per unit and the zoom, and follows a
    /// new display at once.
    /// </summary>
    [UnityTest]
    public IEnumerator TheMainMenu_FollowsTheZoom()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        UiScale scale = SceneBootTestHelper.ResolveFromMain<UiScale>();
        CanvasScaler scaler = Object.FindAnyObjectByType<MainMenuController>().GetComponent<CanvasScaler>();
        Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ConstantPixelSize));
        Assert.That(scaler.referencePixelsPerUnit, Is.EqualTo(UiPixelScale.ReferencePixelsPerUnit));
        Assert.That(scaler.scaleFactor, Is.EqualTo(scale.Factor));

        scale.Apply(3840, 2160, 0);
        Assert.That(scaler.scaleFactor, Is.EqualTo(6f));
    }

    /// <summary>
    /// While hosting with a client connected, the host choosing Large at 1080p puts its HUD, popup and in-game menu
    /// canvases at zoom 4, and its health text, injected with the zoom, at 3/4 (Docs/UiRevamp.md, Phase 3); the
    /// client's settings keep Normal and its copy of the host's player keeps its look, colours
    /// and parts.
    /// </summary>
    [UnityTest]
    public IEnumerator TheHostsUiSize_RescalesItsCanvases_AndNothingOnTheClient()
    {
        yield return Host();
        yield return Join();
        UiScale scale = SceneBootTestHelper.ResolveFromMain<UiScale>();
        CanvasScaler[] scalers =
        {
            Object.FindAnyObjectByType<PlayerHudView>().GetComponentInParent<CanvasScaler>(),
            Object.FindAnyObjectByType<DamagePopupLayer>().GetComponent<CanvasScaler>(),
            Object.FindAnyObjectByType<GameMenuView>(FindObjectsInactive.Include).GetComponentInParent<CanvasScaler>(true),
        };
        PlayerLook copy = InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId).GetComponent<PlayerLook>();
        int look = copy.Index;
        CharacterColourPicks colours = copy.ShownColours;
        CharacterPartPicks parts = copy.ShownParts;

        Transform healthText = Object.FindAnyObjectByType<PlayerHudView>().GetComponentInChildren<PixelTextScale>(true).transform;
        Assert.That(healthText.localScale.x, Is.EqualTo(PixelTextScale.ScaleFor(scale.Factor, 0.75f)).Within(1e-5f), "The health text follows the zoom it was injected with.");

        SceneBootTestHelper.ResolveFromMain<UserSettings>().SetUiSizeStep(1);
        scale.Apply(1920, 1080, 1);
        foreach (CanvasScaler scaler in scalers)
        {
            Assert.That(scaler.scaleFactor, Is.EqualTo(4f), scaler.name);
        }

        Assert.That(healthText.localScale.x, Is.EqualTo(0.75f).Within(1e-5f), "Each font pixel of the health text covers 3 display pixels at x4.");

        for (int frame = 0; frame < 10; frame++)
        {
            yield return null;
        }

        Assert.That(client.Manager.IsConnectedClient, Is.True);
        Assert.That(client.Resolve<UserSettings>().UiSizeStep, Is.Zero);
        Assert.That(copy.Index, Is.EqualTo(look));
        Assert.That(copy.ShownColours, Is.EqualTo(colours));
        Assert.That(copy.ShownParts, Is.EqualTo(parts));
    }

    /// <summary>
    /// Boots into the main menu and hosts from it, with the mobs out of the way.
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
        client.DisableColliders();
    }
}
