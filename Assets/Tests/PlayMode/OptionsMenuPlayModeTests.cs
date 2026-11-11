using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;

/// <summary>
/// PlayMode tests for the options screen in the main menu and for input readers sharing the session's input asset copy.
/// </summary>
public class OptionsMenuPlayModeTests
{
    /// <summary>
    /// Opening options hides the menu buttons; Back restores the buttons with
    /// Options selected. The screen lists the Character header, its Look row, the adventurer's eight part rows and
    /// fifteen colour rows (Docs/PlayerCharacter.md, Phases 5 to 7), the Display header, its UI size row
    /// (Docs/DesignSystem.md, Phase 3) and its Reduce flashing and Reduce motion rows (Phase 6), three section headers
    /// and nine rows; opening it selects its first row, the Look row.
    /// </summary>
    [UnityTest]
    public IEnumerator MainMenuOptions_ReplaceTheButtons_AndBackBringsThemBackWithOptionsSelected()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        MainMenuController menu = Object.FindAnyObjectByType<MainMenuController>();
        OptionsMenuView options = Object.FindAnyObjectByType<OptionsMenuView>();
        yield return null;

        Assert.That(options, Is.Not.Null, "The main menu has an options screen.");
        Assert.That(options.IsShown, Is.False);
        Assert.That(options.RowsContainer.childCount, Is.EqualTo(42), "The Character header, its Look row, eight part rows and fifteen colour rows, the Display header, its UI size row, two toggle rows and the allies' damage numbers row, three section headers and nine rows.");

        menu.OpenOptions();
        yield return null;

        Assert.That(options.IsShown, Is.True);
        Assert.That(menu.ButtonsPanel.activeSelf, Is.False);
        GameObject selected = EventSystem.current.currentSelectedGameObject;
        Assert.That(selected, Is.Not.Null);
        Assert.That(selected.GetComponentInParent<KeyBindingRowView>(), Is.SameAs(options.RowsContainer.GetChild(1).GetComponent<KeyBindingRowView>()), "The first row, the Look row, is selected for keyboard and gamepad navigation.");

        options.BackButton.onClick.Invoke();
        yield return null;

        Assert.That(options.IsShown, Is.False);
        Assert.That(menu.ButtonsPanel.activeSelf, Is.True);
        Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(menu.OptionsButton.gameObject));
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
