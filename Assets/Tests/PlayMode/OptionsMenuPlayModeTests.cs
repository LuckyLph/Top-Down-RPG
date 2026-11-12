using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// PlayMode tests for the options screen in the main menu and in game online (Docs/UiRevamp.md, Phase 8), and for input
/// readers sharing the session's input asset copy.
/// </summary>
public class OptionsMenuPlayModeTests
{
    /// <summary>
    /// Opening options hides the menu buttons; Back restores the buttons with Options selected. The screen holds its rail
    /// of three categories and its rows: three Accessibility rows, three Interface rows, four section headers and ten
    /// binding rows, the look, parts and colours being the Character screen's (Phase 7); from the main menu it opens on
    /// its first category, Accessibility, with its rail item selected and its rows shown, and the pane's foot describes
    /// it.
    /// </summary>
    [UnityTest]
    public IEnumerator MainMenuOptions_ReplaceTheButtons_AndBackBringsThemBackWithOptionsSelected()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        MainMenuController menu = Object.FindAnyObjectByType<MainMenuController>();
        OptionsMenuView options = Object.FindAnyObjectByType<OptionsMenuView>();
        OptionsMenuController controller = Object.FindAnyObjectByType<MenuLifetimeScope>().Container.Resolve<OptionsMenuController>();
        yield return null;

        Assert.That(options, Is.Not.Null, "The main menu has an options screen.");
        Assert.That(options.IsShown, Is.False);
        Assert.That(options.Rail.childCount, Is.EqualTo(3));
        Assert.That(options.RowsContainer.childCount, Is.EqualTo(20), "Three Accessibility rows, three Interface rows, four section headers and ten binding rows.");

        menu.OpenOptions();
        yield return null;

        Assert.That(options.IsShown, Is.True);
        Assert.That(menu.ButtonsPanel.activeSelf, Is.False);
        Assert.That(controller.CurrentCategory, Is.EqualTo(OptionsMenuController.Category.Accessibility));
        GameObject selected = EventSystem.current.currentSelectedGameObject;
        Assert.That(selected, Is.Not.Null);
        Assert.That(selected.GetComponent<RailItemView>(), Is.SameAs(controller.GetRailItem(OptionsMenuController.Category.Accessibility)), "The first category's item is selected for keyboard and gamepad navigation.");
        Assert.That(options.Description.text, Is.EqualTo(OptionsMenuController.CategoryDescriptions[0]));
        Assert.That(controller.UiSizeRow.gameObject.activeInHierarchy, Is.True);
        Assert.That(controller.GetRow(0).gameObject.activeInHierarchy, Is.False);

        EventSystem.current.SetSelectedGameObject(controller.GetRailItem(OptionsMenuController.Category.Controls).gameObject);
        yield return null;
        Assert.That(controller.CurrentCategory, Is.EqualTo(OptionsMenuController.Category.Controls), "Moving onto a category shows it.");
        Assert.That(controller.GetRow(0).gameObject.activeInHierarchy, Is.True);

        options.BackButton.onClick.Invoke();
        yield return null;

        Assert.That(options.IsShown, Is.False);
        Assert.That(menu.ButtonsPanel.activeSelf, Is.True);
        Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(menu.OptionsButton.gameObject));
    }

    /// <summary>
    /// Online, where the game runs on behind the menu, the options screen opened in game shows the vitals strip with the
    /// session's line and the local player's health, and a hit on the local player says "You're taking damage." over the
    /// notice line; the strip and the warning go with the screen, and the Character screen shares them.
    /// </summary>
    [UnityTest]
    public IEnumerator Online_TheVitalsStripShows_AndAHitSaysSo()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            mob.gameObject.SetActive(false);
        }

        GameMenuController menu = SceneBootTestHelper.ResolveFromGameplay<GameMenuController>();
        GameMenuView menuView = SceneBootTestHelper.ResolveFromGameplay<GameMenuView>();
        OptionsMenuView options = SceneBootTestHelper.ResolveFromGameplay<OptionsMenuView>();
        CharacterScreenView character = SceneBootTestHelper.ResolveFromGameplay<CharacterScreenView>();
        LocalPlayerTracker localPlayer = SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>();
        yield return SceneBootTestHelper.WaitUntil(() => localPlayer.Current != null, "the local player");
        Health health = localPlayer.Current.Handle.Health;

        menu.Open();
        Assert.That(Time.timeScale, Is.EqualTo(1f), "Online nothing pauses.");
        menuView.OptionsButton.onClick.Invoke();
        Assert.That(options.IsShown, Is.True);
        yield return null;
        Assert.That(options.Vitals.IsShown, Is.True);
        Assert.That(options.Vitals.Session.text, Is.EqualTo(VitalsStripPresenter.SessionLine));
        Assert.That(options.Vitals.Health.text, Is.EqualTo($"{health.CurrentHealth} / {health.MaxHealth}"));
        Assert.That(options.WarningText.text, Is.Empty);

        health.ApplyDamage(5);
        Assert.That(options.WarningText.text, Is.EqualTo(VitalsStripPresenter.DamageWarning));
        Assert.That(options.NoticeText.enabled, Is.False, "The warning shows over the notice.");
        yield return null;
        Assert.That(options.Vitals.Health.text, Is.EqualTo($"{health.CurrentHealth} / {health.MaxHealth}"));

        options.BackButton.onClick.Invoke();
        yield return null;
        Assert.That(menuView.IsShown, Is.True);
        Assert.That(options.Vitals.IsShown, Is.False, "The strip goes with its screen.");
        Assert.That(options.WarningText.text, Is.Empty, "So does the warning.");

        menuView.CharacterButton.onClick.Invoke();
        yield return null;
        Assert.That(character.Vitals.IsShown, Is.True, "The Character screen shares the strip's presenter.");
        health.ApplyDamage(5);
        Assert.That(character.WarningLine.text, Is.EqualTo(VitalsStripPresenter.DamageWarning));
        Assert.That(options.WarningText.text, Is.Empty, "Only the screen showing its strip warns.");
        character.BackButton.onClick.Invoke();
        menu.Close();
    }

    /// <summary>
    /// The UI input module and the key bindings both use the <see cref="RuntimeInputActions"/> copy of the input asset,
    /// and the module has enabled its UI actions.
    /// </summary>
    [UnityTest]
    public IEnumerator EveryInputReader_UsesTheSessionsCopyOfTheInputAsset()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        RuntimeInputActions runtime = SceneBootTestHelper.ResolveFromMain<RuntimeInputActions>();
        InputBindings bindings = SceneBootTestHelper.ResolveFromMain<InputBindings>();
        InputSystemUIInputModule module = Object.FindAnyObjectByType<InputSystemUIInputModule>();

        Assert.That(runtime.Actions, Is.Not.Null);
        Assert.That(module.actionsAsset, Is.SameAs(runtime.Actions));
        Assert.That(module.cancel.action.enabled, Is.True, "The UI module enabled the copy's UI actions.");
        Assert.That(bindings.GetAction(0).actionMap.asset, Is.SameAs(runtime.Actions));
    }
}
