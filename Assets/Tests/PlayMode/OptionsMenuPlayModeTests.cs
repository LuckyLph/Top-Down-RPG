using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

public class OptionsMenuPlayModeTests
{
    [UnityTest]
    public IEnumerator MainMenuOptions_ReplaceTheButtons_AndBackBringsThemBackWithOptionsSelected()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        MainMenuController menu = Object.FindAnyObjectByType<MainMenuController>();
        OptionsMenuView options = Object.FindAnyObjectByType<OptionsMenuView>();
        yield return null;

        Assert.That(options, Is.Not.Null, "The main menu has an options screen.");
        Assert.That(options.IsShown, Is.False);
        Assert.That(options.RowsContainer.childCount, Is.EqualTo(11), "Three section headers and eight rows.");

        menu.OpenOptions();
        yield return null;

        Assert.That(options.IsShown, Is.True);
        Assert.That(menu.ButtonsPanel.activeSelf, Is.False);
        GameObject selected = EventSystem.current.currentSelectedGameObject;
        Assert.That(selected, Is.Not.Null);
        Assert.That(selected.GetComponentInParent<KeyBindingRowView>(), Is.Not.Null, "The first binding is selected for keyboard and gamepad navigation.");

        options.BackButton.onClick.Invoke();
        yield return null;

        Assert.That(options.IsShown, Is.False);
        Assert.That(menu.ButtonsPanel.activeSelf, Is.True);
        Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(menu.OptionsButton.gameObject));
    }
}
