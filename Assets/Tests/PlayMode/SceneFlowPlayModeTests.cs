using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class SceneFlowPlayModeTests
{
    [UnityTest]
    public IEnumerator Boot_LoadsGameplayAndAreaWithSingleCameraListenerAndEventSystem()
    {
        yield return SceneBootTestHelper.BootIntoStartingArea();

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(SceneBootTestHelper.StartingAreaPath));
        Assert.That(gameFlow.CurrentArea, Is.Not.Null);
        Assert.That(Object.FindObjectsByType<Camera>().Length, Is.EqualTo(1), "Only Main should own a camera.");
        Assert.That(Object.FindObjectsByType<AudioListener>().Length, Is.EqualTo(1), "Only Main should own an AudioListener.");
        Assert.That(Object.FindObjectsByType<EventSystem>().Length, Is.EqualTo(1), "Only Main should own an EventSystem.");

        PlayerController player = Object.FindAnyObjectByType<PlayerController>();
        CameraFollow2D cameraFollow = Object.FindAnyObjectByType<CameraFollow2D>();
        Assert.That(player, Is.Not.Null);
        Assert.That(player.InputEnabled, Is.True, "Input should be unlocked once the transition finished.");
        Assert.That(cameraFollow.Target, Is.SameAs(player.transform));
    }

    [UnityTest]
    public IEnumerator ChangeArea_ReloadsAreaAndPlacesPlayerAtSpawn()
    {
        yield return SceneBootTestHelper.BootIntoStartingArea();

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        PlayerController player = Object.FindAnyObjectByType<PlayerController>();
        SpawnPoint spawnPoint = Object.FindAnyObjectByType<SpawnPoint>();
        Assert.That(spawnPoint, Is.Not.Null, "The starting area should contain a SpawnPoint.");
        string spawnId = spawnPoint.SpawnId;
        Vector3 spawnPosition = spawnPoint.transform.position;

        player.Teleport(spawnPosition + new Vector3(6f, 4f, 0f));
        gameFlow.ChangeAreaAsync(gameFlow.Scenes.StartingArea, spawnId);
        Assert.That(gameFlow.IsTransitioning, Is.True);
        Assert.That(player.InputEnabled, Is.False, "Input should be locked during a transition.");
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(SceneBootTestHelper.StartingAreaPath));
        Assert.That(Object.FindObjectsByType<NavigationGrid2D>().Length, Is.EqualTo(1), "Old area should be unloaded before the new one loads.");
        Assert.That(Vector2.Distance(player.transform.position, spawnPosition), Is.LessThan(0.01f));
        Assert.That(player.InputEnabled, Is.True);

        MobController mob = Object.FindAnyObjectByType<MobController>();
        Assert.That(mob, Is.Not.Null);
        Assert.That(mob.TargetProvider.Target, Is.SameAs(player.transform), "Area mobs should be injected with the session player.");
        Assert.That(mob.NavigationGrid.gameObject.scene, Is.EqualTo(SceneManager.GetActiveScene()));
    }

    [UnityTest]
    public IEnumerator ShowMainMenu_UnloadsSessionAndNewGameStartsFresh()
    {
        yield return SceneBootTestHelper.BootIntoStartingArea();

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        Health playerHealth = Object.FindAnyObjectByType<PlayerController>().GetComponent<Health>();
        int maxHealth = playerHealth.MaxHealth;
        playerHealth.ApplyDamage(1);

        gameFlow.ShowMainMenuAsync();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        Assert.That(gameFlow.IsInMenu, Is.True);
        Assert.That(gameFlow.IsInGame, Is.False);
        Assert.That(Object.FindAnyObjectByType<PlayerController>(), Is.Null, "Gameplay should be unloaded in the menu.");
        Assert.That(Object.FindAnyObjectByType<MobController>(), Is.Null, "The area should be unloaded in the menu.");
        Assert.That(Object.FindAnyObjectByType<CameraFollow2D>().Target, Is.Null);

        gameFlow.StartNewGameAsync();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        Assert.That(gameFlow.IsInGame, Is.True);
        Assert.That(gameFlow.IsInMenu, Is.False);
        Health freshHealth = Object.FindAnyObjectByType<PlayerController>().GetComponent<Health>();
        Assert.That(freshHealth.CurrentHealth, Is.EqualTo(maxHealth), "A new game should start with a fresh player.");
    }

    [UnityTest]
    public IEnumerator Boot_WithoutEditorRequest_ShowsMainMenuWithFocusedButton()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();

        Assert.That(Object.FindAnyObjectByType<MainMenuController>(), Is.Not.Null);
        Assert.That(EventSystem.current.currentSelectedGameObject, Is.Not.Null, "Menu should focus a button for keyboard/gamepad.");
    }
}
