using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// PlayMode tests for the in-game menu: pausing and input gating in solo play, and leaving to the main menu solo or
/// while hosting.
/// </summary>
public class GameMenuPlayModeTests
{
    /// <summary>
    /// In solo play, opening the menu stops time and gameplay input while keeping the menu key live, and closing it
    /// restores both.
    /// </summary>
    [UnityTest]
    public IEnumerator Solo_TheMenuPausesTheGame_AndBlocksGameplayInputUntilItCloses()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();
        PlayerInputService input = SceneBootTestHelper.ResolveFromMain<PlayerInputService>();
        GameMenuController menu = SceneBootTestHelper.ResolveFromGameplay<GameMenuController>();

        Assert.That(input.GameplayEnabled, Is.True);
        Assert.That(input.MenusEnabled, Is.True, "The menu key is live in game.");

        menu.Open();
        yield return null;

        Assert.That(Time.timeScale, Is.Zero);
        Assert.That(input.GameplayEnabled, Is.False);
        Assert.That(input.MenusEnabled, Is.True);

        menu.Close();
        yield return null;

        Assert.That(Time.timeScale, Is.EqualTo(1f));
        Assert.That(input.GameplayEnabled, Is.True);
    }

    /// <summary>
    /// Leave from the paused solo menu goes straight to the main menu without a confirmation, with time running again
    /// and both gameplay and menu input off.
    /// </summary>
    [UnityTest]
    public IEnumerator Solo_LeavingFromAPause_ReachesTheMainMenuWithTimeRunning()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        PlayerInputService input = SceneBootTestHelper.ResolveFromMain<PlayerInputService>();
        GameMenuController menu = SceneBootTestHelper.ResolveFromGameplay<GameMenuController>();
        GameMenuView view = Object.FindAnyObjectByType<GameMenuView>();

        menu.Open();
        view.LeaveButton.onClick.Invoke();
        Assert.That(menu.IsLeaving, Is.True, "Solo play leaves without asking.");
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        Assert.That(gameFlow.IsInMenu, Is.True);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
        Assert.That(input.GameplayEnabled, Is.False);
        Assert.That(input.MenusEnabled, Is.False, "The menu key is off outside the game.");
    }

    /// <summary>
    /// While hosting, the menu blocks gameplay input but does not pause time, and a host alone leaves at once
    /// (Docs/UiRevamp.md, Phase 6: nobody else loses the game), returning to the main menu and shutting the session down.
    /// </summary>
    [UnityTest]
    public IEnumerator Hosting_TheMenuPausesNothing_AndALoneHostLeavesAtOnce_EndingTheSession()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(gameFlow.IsInGame, Is.True);

        GameMenuController menu = SceneBootTestHelper.ResolveFromGameplay<GameMenuController>();
        GameMenuView view = Object.FindAnyObjectByType<GameMenuView>();
        PlayerInputService input = SceneBootTestHelper.ResolveFromMain<PlayerInputService>();

        menu.Open();
        yield return null;

        Assert.That(menu.IsPaused, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(1f), "A host alone is still online; a client may join at any moment.");
        Assert.That(input.GameplayEnabled, Is.False);
        Assert.That(view.PartyButton.gameObject.activeSelf, Is.True);
        Assert.That(view.SaveButton.interactable, Is.True, "A session started from the main menu is saved.");

        view.LeaveButton.onClick.Invoke();
        Assert.That(view.IsConfirmShown, Is.False, "Nobody else loses the game.");
        Assert.That(menu.IsLeaving, Is.True);

        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        yield return SceneBootTestHelper.WaitUntil(() => !session.IsActive, "the session to shut down in the menu");

        Assert.That(gameFlow.IsInMenu, Is.True);
    }
}
