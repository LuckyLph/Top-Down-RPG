using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

public class GameSavePlayModeTests
{
    [UnityTest]
    public IEnumerator EnteringAnArea_SavesItsSceneAndSpawn()
    {
        MemorySaveStore saveStore = new();
        yield return SceneBootTestHelper.BootIntoStartingArea(saveStore);

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        Assert.That(GameSaveData.TryParse(saveStore.Contents, out GameSaveData data), Is.True, "Entering the area should write a save.");
        Assert.That(data.AreaSceneGuid, Is.EqualTo(gameFlow.Scenes.StartingArea.SceneGuid));
        Assert.That(data.SpawnId, Is.EqualTo(gameFlow.Scenes.StartingSpawnId));
    }

    [UnityTest]
    public IEnumerator MainMenu_WithoutASave_DisablesContinue()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu(new MemorySaveStore());

        MainMenuController menu = Object.FindAnyObjectByType<MainMenuController>();
        yield return null;

        Assert.That(menu.ContinueButton, Is.Not.Null, "The menu should have a Continue button assigned.");
        Assert.That(menu.ContinueButton.interactable, Is.False);
        Assert.That(EventSystem.current.currentSelectedGameObject, Is.Not.SameAs(menu.ContinueButton.gameObject));
    }

    [UnityTest]
    public IEnumerator MainMenu_AfterPlaying_ContinueLoadsTheSavedAreaAsANewSession()
    {
        MemorySaveStore saveStore = new();
        yield return SceneBootTestHelper.BootIntoStartingArea(saveStore);

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        GameScenes scenes = gameFlow.Scenes;
        Assert.That(saveStore.Contents, Is.Not.Null);

        gameFlow.ShowMainMenuAsync();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        MainMenuController menu = Object.FindAnyObjectByType<MainMenuController>();
        yield return null;

        Assert.That(menu.ContinueButton.interactable, Is.True);
        Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(menu.ContinueButton.gameObject), "Continue is selected when there is a save.");

        AreaTransition? started = null;
        void Record(AreaTransition transition) => started = transition;
        gameFlow.AreaLoading += Record;
        try
        {
            menu.ContinueGame();
            yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        }
        finally
        {
            gameFlow.AreaLoading -= Record;
        }

        Assert.That(gameFlow.IsInGame, Is.True);
        Assert.That(started.HasValue, Is.True);
        Assert.That(started.Value.NewSession, Is.True);
        Assert.That(started.Value.Area, Is.SameAs(scenes.StartingArea));
        Assert.That(started.Value.SpawnId, Is.EqualTo(scenes.StartingSpawnId));
    }
}
