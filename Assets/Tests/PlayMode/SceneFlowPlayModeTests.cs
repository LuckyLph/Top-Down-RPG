using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using VContainer.Unity;

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
        Assert.That(SceneBootTestHelper.ResolveFromMain<IPlayerInput>().GameplayEnabled, Is.True, "Input should be unlocked once the transition finished.");
        Assert.That(cameraFollow.Target, Is.SameAs(player.transform));
        Assert.That(Object.FindObjectsByType<PlayerController>().Length, Is.EqualTo(1), "Exactly one local player should be spawned.");
        Assert.That(player.gameObject.scene.path, Is.EqualTo("Assets/Scenes/Gameplay.unity"), "The player should live in the Gameplay scene so it survives area changes.");

        LifetimeScope mainScope = Object.FindAnyObjectByType<MainLifetimeScope>();
        LifetimeScope gameplayScope = Object.FindAnyObjectByType<GameplayLifetimeScope>();
        LifetimeScope areaScope = LifetimeScope.Find<LifetimeScope>(gameFlow.AreaScene);
        Assert.That(mainScope.Parent, Is.Null, "Main should be the root scope.");
        Assert.That(gameplayScope.Parent, Is.SameAs(mainScope), "GameFlow should parent the Gameplay scope to Main.");
        Assert.That(areaScope, Is.InstanceOf<AreaLifetimeScope>());
        Assert.That(areaScope.Parent, Is.SameAs(gameplayScope), "GameFlow should parent the area scope to Gameplay.");

        LocalPlayer localPlayer = gameplayScope.Container.Resolve<LocalPlayer>();
        Assert.That(localPlayer.Controller, Is.SameAs(player));
        Assert.That(gameplayScope.Container.Resolve<IPlayerRegistry>().Players, Is.EquivalentTo(new[] { localPlayer.Handle }));
    }

    [UnityTest]
    public IEnumerator ChangeArea_ReloadsAreaAndPlacesPlayerAtSpawn()
    {
        yield return SceneBootTestHelper.BootIntoStartingArea();

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        PlayerController player = Object.FindAnyObjectByType<PlayerController>();
        IPlayerInput playerInput = SceneBootTestHelper.ResolveFromMain<IPlayerInput>();
        SpawnPoint spawnPoint = Object.FindAnyObjectByType<SpawnPoint>();
        Assert.That(spawnPoint, Is.Not.Null, "The starting area should contain a SpawnPoint.");
        string spawnId = spawnPoint.SpawnId;
        Vector3 spawnPosition = spawnPoint.transform.position;

        player.Teleport(spawnPosition + new Vector3(6f, 4f, 0f));
        gameFlow.ChangeAreaAsync(gameFlow.Scenes.StartingArea, spawnId);
        Assert.That(gameFlow.IsTransitioning, Is.True);
        Assert.That(playerInput.GameplayEnabled, Is.False, "Input should be locked during a transition.");
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(SceneBootTestHelper.StartingAreaPath));
        Assert.That(Object.FindObjectsByType<NavigationGrid2D>().Length, Is.EqualTo(1), "Old area should be unloaded before the new one loads.");
        Assert.That(Vector2.Distance(player.transform.position, spawnPosition), Is.LessThan(0.01f));
        Assert.That(playerInput.GameplayEnabled, Is.True);

        MobController mob = Object.FindAnyObjectByType<MobController>();
        Assert.That(mob, Is.Not.Null);
        IPlayerRegistry sessionPlayers = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container.Resolve<IPlayerRegistry>();
        Assert.That(mob.Players, Is.SameAs(sessionPlayers), "Area mobs should be injected with the session's players.");
        Assert.That(mob.NavigationGrid.gameObject.scene, Is.EqualTo(SceneManager.GetActiveScene()));

        LifetimeScope gameplayScope = Object.FindAnyObjectByType<GameplayLifetimeScope>();
        LifetimeScope areaScope = LifetimeScope.Find<LifetimeScope>(gameFlow.AreaScene);
        Assert.That(areaScope.Parent, Is.SameAs(gameplayScope), "The reloaded area should parent to the running Gameplay scope.");
        AreaEntryRequest entryRequest = areaScope.Container.Resolve<AreaEntryRequest>();
        Assert.That(entryRequest.SpawnId, Is.EqualTo(spawnId));
        Assert.That(entryRequest.Area, Is.SameAs(gameFlow.Scenes.StartingArea));
        Assert.That(gameplayScope.Container.TryResolve(out AreaEntryRequest _), Is.False, "The entry request should only reach the area's container.");
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
        Assert.That(SceneBootTestHelper.ResolveFromMain<IPlayerInput>().GameplayEnabled, Is.False, "Gameplay input should be off in the menu.");
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
    public IEnumerator PlayerDeath_RestartsCurrentAreaWithFreshPlayer()
    {
        yield return SceneBootTestHelper.BootIntoStartingArea();

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        SceneDefinition areaBeforeDeath = gameFlow.CurrentArea;
        PlayerController deadPlayer = Object.FindAnyObjectByType<PlayerController>();
        Vector3 spawnPosition = Object.FindAnyObjectByType<SpawnPoint>().transform.position;
        Health deadHealth = deadPlayer.GetComponent<Health>();

        deadPlayer.Teleport(spawnPosition + new Vector3(5f, 3f, 0f));
        deadHealth.ApplyDamage(deadHealth.MaxHealth);
        Assert.That(deadHealth.IsDead, Is.True);

        yield return SceneBootTestHelper.WaitUntil(() => deadPlayer == null, "the Gameplay scene to be reloaded after death");
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        PlayerController freshPlayer = Object.FindAnyObjectByType<PlayerController>();
        Health freshHealth = freshPlayer.GetComponent<Health>();
        Assert.That(gameFlow.IsInGame, Is.True);
        Assert.That(gameFlow.CurrentArea.ScenePath, Is.EqualTo(areaBeforeDeath.ScenePath));
        Assert.That(freshHealth.CurrentHealth, Is.EqualTo(freshHealth.MaxHealth));
        Assert.That(Vector2.Distance(freshPlayer.transform.position, spawnPosition), Is.LessThan(0.01f));
    }

    [UnityTest]
    public IEnumerator PlayerDeath_WaitsUntilTheWholePartyIsDown()
    {
        yield return SceneBootTestHelper.BootIntoStartingArea();

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        IObjectResolver gameplayResolver = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerRegistry players = gameplayResolver.Resolve<PlayerRegistry>();
        GameplaySettings settings = gameplayResolver.Resolve<GameplaySettings>();
        PlayerController localPlayer = Object.FindAnyObjectByType<PlayerController>();
        Health localHealth = localPlayer.GetComponent<Health>();

        GameObject partnerObject = new("Partner");
        partnerObject.transform.position = new Vector3(1000f, 1000f, 0f);
        PlayerHandle partner = new(partnerObject.transform, partnerObject.AddComponent<Health>());
        players.Add(partner);

        localHealth.ApplyDamage(localHealth.MaxHealth);
        yield return new WaitForSeconds(settings.RestartDelaySeconds + 0.5f);

        Assert.That(localPlayer != null, Is.True, "The session must keep running while a party member is alive.");
        Assert.That(gameFlow.IsTransitioning, Is.False);

        partner.Health.ApplyDamage(partner.Health.MaxHealth);
        yield return SceneBootTestHelper.WaitUntil(() => localPlayer == null, "the Gameplay scene to be reloaded after the party wipe");
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        Assert.That(gameFlow.IsInGame, Is.True);
    }

    [UnityTest]
    public IEnumerator Boot_WithoutEditorRequest_ShowsMainMenuWithFocusedButton()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();

        Assert.That(Object.FindAnyObjectByType<MainMenuController>(), Is.Not.Null);
        Assert.That(
            Object.FindAnyObjectByType<MenuLifetimeScope>().Parent,
            Is.SameAs(Object.FindAnyObjectByType<MainLifetimeScope>()),
            "GameFlow should parent the menu scope to Main.");
        Assert.That(EventSystem.current.currentSelectedGameObject, Is.Not.Null, "Menu should focus a button for keyboard/gamepad.");
    }
}
