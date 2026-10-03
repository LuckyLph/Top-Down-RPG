using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class GameMenuPlayModeTests
{
    [UnityTest]
    public IEnumerator Solo_TheMenuPausesTheGame_AndBlocksGameplayInputUntilItCloses()
    {
        yield return SceneBootTestHelper.BootIntoStartingArea();
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

    [UnityTest]
    public IEnumerator Solo_LeavingFromAPause_ReachesTheMainMenuWithTimeRunning()
    {
        yield return SceneBootTestHelper.BootIntoStartingArea();
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

    [UnityTest]
    public IEnumerator Hosting_TheMenuPausesNothing_AndLeavingAsksFirstThenEndsTheSession()
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

        view.LeaveButton.onClick.Invoke();
        Assert.That(view.IsConfirmShown, Is.True);
        Assert.That(menu.IsLeaving, Is.False);

        view.ConfirmYesButton.onClick.Invoke();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        yield return SceneBootTestHelper.WaitUntil(() => !session.IsActive, "the session to shut down in the menu");

        Assert.That(gameFlow.IsInMenu, Is.True);
    }
}
