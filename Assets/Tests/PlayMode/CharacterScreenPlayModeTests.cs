using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

/// <summary>
/// PlayMode tests for the Character screen (Docs/UiRevamp.md, Phase 7): from the main menu it takes the buttons' place,
/// its preview drawing the character into the texture its box shows, and Back brings the buttons back with Character
/// selected; in game offline, where the menu holds the game still, its preview runs on unscaled time.
/// </summary>
public class CharacterScreenPlayModeTests
{
    /// <summary>
    /// The main menu's Character button hides the buttons and opens the screen, its first row's button selected and its
    /// box showing the stage's texture, the character drawn; Back brings the buttons back with Character selected.
    /// </summary>
    [UnityTest]
    public IEnumerator MainMenuCharacter_ReplacesTheButtons_AndBackBringsThemBack()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        MainMenuController menu = Object.FindAnyObjectByType<MainMenuController>();
        CharacterScreenView view = Object.FindAnyObjectByType<CharacterScreenView>();
        yield return null;
        Assert.That(view, Is.Not.Null, "The main menu has a Character screen.");
        Assert.That(view.IsShown, Is.False);

        menu.OpenCharacter();
        yield return null;
        Assert.That(view.IsShown, Is.True);
        Assert.That(menu.ButtonsPanel.activeSelf, Is.False);
        Assert.That(EventSystem.current.currentSelectedGameObject, Is.Not.Null);
        Assert.That(EventSystem.current.currentSelectedGameObject.GetComponentInParent<RowChipsView>(), Is.Not.Null, "The first row's button is selected.");
        Assert.That(view.Preview.texture, Is.SameAs(view.Stage.Texture));
        SpriteRenderer body = view.Stage.Visuals.GetComponent<SpriteRenderer>();
        yield return SceneBootTestHelper.WaitUntil(() => body.sprite != null, "the stage to show the character");
        Assert.That(body.enabled, Is.False, "Only the stage draws it.");

        view.BackButton.onClick.Invoke();
        yield return null;
        Assert.That(view.IsShown, Is.False);
        Assert.That(menu.ButtonsPanel.activeSelf, Is.True);
        Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(menu.CharacterButton.gameObject));
    }

    /// <summary>
    /// Offline the in-game menu holds the game still; its Character button opens the screen, and in the running pose
    /// the preview's character runs through its frames while game time stands.
    /// </summary>
    [UnityTest]
    public IEnumerator Offline_ThePreviewRunsWhileTheGameIsPaused()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();
        GameMenuController menu = SceneBootTestHelper.ResolveFromGameplay<GameMenuController>();
        GameMenuView menuView = SceneBootTestHelper.ResolveFromGameplay<GameMenuView>();
        CharacterScreenView view = SceneBootTestHelper.ResolveFromGameplay<CharacterScreenView>();
        menu.Open();
        Assert.That(Time.timeScale, Is.Zero, "Offline the menu pauses the game.");
        menuView.CharacterButton.onClick.Invoke();
        Assert.That(view.IsShown, Is.True);
        view.PoseButton.onClick.Invoke();
        Assert.That(view.Stage.Pose, Is.EqualTo(CharacterPreviewPose.Running));

        SpriteRenderer body = view.Stage.Visuals.GetComponent<SpriteRenderer>();
        HashSet<Sprite> shown = new();
        yield return SceneBootTestHelper.WaitUntil(() =>
        {
            if (body.sprite != null)
            {
                shown.Add(body.sprite);
            }

            return shown.Count >= 3;
        }, "the preview to run through its frames");
        Assert.That(Time.timeScale, Is.Zero, "The game stayed paused.");

        view.BackButton.onClick.Invoke();
        Assert.That(menuView.IsShown, Is.True);
        Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(menuView.CharacterButton.gameObject));
        menu.Close();
        Assert.That(Time.timeScale, Is.EqualTo(1f));
    }
}
