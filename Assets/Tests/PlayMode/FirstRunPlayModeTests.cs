using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

/// <summary>
/// PlayMode tests for the first run (Docs/UiRevamp.md, Phase 11), booted with empty settings and no save: the Welcome
/// panel shows over the title view before the menu's panel; Continue marks the first run over and brings the panel with
/// New game selected; New game goes through the Character screen, whose Start begins the game in the starting world;
/// and a boot after it opens on the panel at once.
/// </summary>
public class FirstRunPlayModeTests
{
    /// <summary>
    /// A first run: Welcome, then the panel, then New game through the Character screen's Start into the starting world;
    /// the settings, kept, open the menu on its panel next time.
    /// </summary>
    [UnityTest]
    public IEnumerator TheFirstRun_WelcomesThenStartsThroughTheCharacterScreen()
    {
        MemorySaveStore settings = new();
        yield return SceneBootTestHelper.BootIntoMainMenu(null, SceneBootTestHelper.TestlandsRootPath, SceneBootTestHelper.TestlandsStartEntry, settings);
        MainMenuController menu = Object.FindAnyObjectByType<MainMenuController>();
        WelcomeView welcome = Object.FindAnyObjectByType<WelcomeView>();
        CharacterScreenView character = Object.FindAnyObjectByType<CharacterScreenView>();
        yield return null;
        Assert.That(welcome.IsShown, Is.True, "The first run opens on the Welcome.");
        Assert.That(menu.Panel.gameObject.activeSelf, Is.False);
        Assert.That(Object.FindAnyObjectByType<TitleView>(), Is.Not.Null, "The title view stands behind it.");
        Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(welcome.UiSizeRow.BindingButton.gameObject));

        welcome.ContinueButton.onClick.Invoke();
        yield return null;
        Assert.That(welcome.IsShown, Is.False);
        Assert.That(menu.Panel.gameObject.activeSelf, Is.True);
        Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(menu.NewGameButton.gameObject));
        Assert.That(UserSettingsData.TryParse(settings.Contents, out UserSettingsData written) && written.FirstRunDone, Is.True, "The first run is over in the file.");

        menu.NewGameButton.onClick.Invoke();
        yield return null;
        Assert.That(character.IsShown, Is.True, "The first New game goes through the Character screen.");
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        Assert.That(gameFlow.IsTransitioning, Is.False, "Nothing starts until Start.");
        character.BackButton.onClick.Invoke();
        yield return SceneBootTestHelper.WaitUntil(() => gameFlow.IsTransitioning || gameFlow.IsInGame, "Start to begin the new game");
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(gameFlow.IsInGame, Is.True);
        Assert.That(gameFlow.CurrentWorld, Is.SameAs(gameFlow.Scenes.StartingWorld));

        yield return SceneBootTestHelper.BootIntoMainMenu(null, SceneBootTestHelper.TestlandsRootPath, SceneBootTestHelper.TestlandsStartEntry, settings);
        yield return null;
        Assert.That(Object.FindAnyObjectByType<WelcomeView>().IsShown, Is.False, "A later boot opens on the panel.");
        Assert.That(Object.FindAnyObjectByType<MainMenuController>().Panel.gameObject.activeSelf, Is.True);
    }
}
