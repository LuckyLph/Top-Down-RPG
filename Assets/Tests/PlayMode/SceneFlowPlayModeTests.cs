using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using VContainer.Unity;

/// <summary>
/// PlayMode tests for the scene flow: booting into Testlands, the Main, Gameplay, world and chunk scope hierarchy, the
/// time freeze of offline transitions, the main menu, and respawns at a waystone after deaths with nothing restarting.
/// </summary>
public class SceneFlowPlayModeTests
{
    /// <summary>
    /// Booting into Testlands makes Gameplay the active scene with one camera, audio listener and event system, spawns
    /// one local player in the Gameplay scene followed by the camera with input unlocked, parents the scopes Main,
    /// Gameplay, world, chunk, and gives the start chunk's mobs the world's players and navigation.
    /// </summary>
    [UnityTest]
    public IEnumerator Boot_LoadsGameplayAndTheWorld_WithSingleCameraListenerAndEventSystem()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(gameFlow.GameplayScene));
        Assert.That(gameFlow.CurrentWorld, Is.SameAs(gameFlow.Scenes.StartingWorld), "Testlands is the starting world.");
        Assert.That(Object.FindObjectsByType<Camera>().Length, Is.EqualTo(1), "Only Main should own a camera.");
        Assert.That(Object.FindObjectsByType<AudioListener>().Length, Is.EqualTo(1), "Only Main should own an AudioListener.");
        Assert.That(Object.FindObjectsByType<EventSystem>().Length, Is.EqualTo(1), "Only Main should own an EventSystem.");

        PlayerController player = Object.FindAnyObjectByType<PlayerController>();
        CameraFollow2D cameraFollow = Object.FindAnyObjectByType<CameraFollow2D>();
        Assert.That(player, Is.Not.Null);
        Assert.That(SceneBootTestHelper.ResolveFromMain<IPlayerInput>().GameplayEnabled, Is.True, "Input should be unlocked once the transition finished.");
        Assert.That(cameraFollow.Target, Is.SameAs(player.transform));
        Assert.That(Object.FindObjectsByType<PlayerController>().Length, Is.EqualTo(1), "Exactly one local player should be spawned.");
        Assert.That(player.gameObject.scene.path, Is.EqualTo("Assets/Scenes/Gameplay.unity"), "The player should live in the Gameplay scene so it survives trips.");

        LifetimeScope mainScope = Object.FindAnyObjectByType<MainLifetimeScope>();
        LifetimeScope gameplayScope = Object.FindAnyObjectByType<GameplayLifetimeScope>();
        Assert.That(mainScope.Parent, Is.Null, "Main should be the root scope.");
        Assert.That(gameplayScope.Parent, Is.SameAs(mainScope), "GameFlow should parent the Gameplay scope to Main.");
        Assert.That(gameplayScope.Container.Resolve<Worlds>().TryGet(gameFlow.CurrentWorld, out LoadedWorld loaded), Is.True);
        Assert.That(loaded.Scope, Is.InstanceOf<WorldLifetimeScope>());
        Assert.That(loaded.Scope.Parent, Is.SameAs(gameplayScope), "The streaming parents a world's scope to Gameplay.");
        foreach (Scene chunk in loaded.Chunks.Values)
        {
            Assert.That(LifetimeScope.Find<ChunkLifetimeScope>(chunk).Parent, Is.SameAs(loaded.Scope), $"{chunk.name} parents to its world.");
        }

        LocalPlayer localPlayer = gameplayScope.Container.Resolve<LocalPlayerTracker>().Current;
        Assert.That(localPlayer.Controller, Is.SameAs(player));
        Assert.That(gameplayScope.Container.Resolve<IPlayerRegistry>().Players, Is.EquivalentTo(new[] { localPlayer.Handle }));

        MobController mob = Object.FindAnyObjectByType<MobController>();
        Assert.That(mob, Is.Not.Null, "The start chunk's mobs spawned.");
        Assert.That(mob.Players, Is.SameAs(loaded.Scope.Container.Resolve<IPlayerRegistry>()), "A chunk's mobs see the players of their world.");
        Assert.That(mob.Navigation, Is.SameAs(loaded.Navigation));
    }

    /// <summary>
    /// Offline, game time stands still for a whole transition that is no trip, so a player's Burn neither ticks nor runs
    /// down behind the fade of a new game.
    /// </summary>
    [UnityTest]
    public IEnumerator AnOfflineNewGame_FreezesGameTimeForTheWholeTransition()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        PlayerController player = Object.FindAnyObjectByType<PlayerController>();
        Assert.That(
            SceneBootTestHelper.ResolveFromGameplay<StatusEffectService>().Apply(player.GetComponent<DamageReceiver>(), SceneBootTestHelper.FindStatus("Burn")),
            Is.EqualTo(StatusApplyOutcome.Landed));
        Assert.That(Time.timeScale, Is.EqualTo(1f));

        int periodicHitsDuringTransition = 0;
        SceneBootTestHelper.ResolveFromGameplay<CombatEvents>().DamageApplied += report =>
        {
            if ((report.Flags & DamageFlags.Periodic) != 0)
            {
                periodicHitsDuringTransition++;
            }
        };

        gameFlow.StartWorldGameAsync(gameFlow.Scenes.StartingWorld, gameFlow.Scenes.StartingWorldEntry);
        float deadline = Time.realtimeSinceStartup + 30f;
        while (gameFlow.IsTransitioning)
        {
            Assert.That(Time.timeScale, Is.Zero, "Game time stands still for the whole transition.");
            Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The transition should finish.");
            yield return null;
        }

        Assert.That(Time.timeScale, Is.EqualTo(1f), "Game time runs again once the transition finished.");
        Assert.That(periodicHitsDuringTransition, Is.Zero, "Nothing ticks behind the fade.");
        Assert.That(gameFlow.IsInGame, Is.True);
    }

    /// <summary>
    /// Returning to the main menu unloads the player and the world, turns gameplay input off and clears the camera
    /// target; a new game then starts with a fully healed player.
    /// </summary>
    [UnityTest]
    public IEnumerator ShowMainMenu_UnloadsSessionAndNewGameStartsFresh()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();

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
        Assert.That(Object.FindAnyObjectByType<MobController>(), Is.Null, "The world should be unloaded in the menu.");
        Assert.That(Object.FindAnyObjectByType<CameraFollow2D>().Target, Is.Null);

        gameFlow.StartWorldGameAsync(gameFlow.Scenes.StartingWorld, gameFlow.Scenes.StartingWorldEntry);
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        Assert.That(gameFlow.IsInGame, Is.True);
        Assert.That(gameFlow.IsInMenu, Is.False);
        Health freshHealth = Object.FindAnyObjectByType<PlayerController>().GetComponent<Health>();
        Assert.That(freshHealth.CurrentHealth, Is.EqualTo(maxHealth), "A new game should start with a fresh player.");
    }

    /// <summary>
    /// The only player dies: it is disabled, then after the respawn delay it travels to `start` within Testlands (a trip,
    /// which never stops time) and is back there at full health with its controller, collider and body on, the same
    /// player in the same Gameplay scene and world.
    /// </summary>
    [UnityTest]
    public IEnumerator TheOnlyPlayerDying_RespawnsWithoutRestarting()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        Scene gameplay = gameFlow.GameplayScene;
        PlayerController localPlayer = Object.FindAnyObjectByType<PlayerController>();
        Health localHealth = localPlayer.GetComponent<Health>();
        Collider2D localCollider = localPlayer.GetComponent<Collider2D>();
        Rigidbody2D localBody = localPlayer.GetComponent<Rigidbody2D>();

        WorldDefinition world = gameFlow.CurrentWorld;
        localPlayer.Teleport(world.ToWorld(new Vector2(30.5f, 40.5f)));
        localHealth.ApplyDamage(localHealth.MaxHealth);
        Assert.That(localPlayer.enabled, Is.False, "A dead player stops responding to input.");
        Assert.That(localBody.simulated, Is.False);

        yield return SceneBootTestHelper.WaitUntil(() => gameFlow.IsTravelling, "the dead player's trip to its waystone");
        Assert.That(Time.timeScale, Is.EqualTo(1f), "A respawn's trip never stops time.");
        yield return SceneBootTestHelper.WaitUntil(() => !localHealth.IsDead, "the dead player to respawn");
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        Assert.That(localPlayer != null, Is.True, "The same player respawns; nothing reloads.");
        Assert.That(gameFlow.GameplayScene, Is.EqualTo(gameplay));
        Assert.That(gameFlow.CurrentWorld, Is.SameAs(world));
        Assert.That(Vector2.Distance(localPlayer.transform.position, world.EntryPosition("start")), Is.LessThan(1.5f), "It respawns at `start`.");
        Assert.That(localHealth.CurrentHealth, Is.EqualTo(localHealth.MaxHealth));
        Assert.That(localPlayer.enabled, Is.True);
        Assert.That(localCollider == null || localCollider.enabled, Is.True);
        Assert.That(localBody.simulated, Is.True);
    }

    /// <summary>
    /// Every player dead at once restarts nothing: past the respawn delay the Gameplay scene and the local player are the
    /// same, and each player respawns on its own: the local one at its waystone, a player in no world where it stands.
    /// </summary>
    [UnityTest]
    public IEnumerator EveryPlayerDeadAtOnce_RestartsNothing()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        Scene gameplay = gameFlow.GameplayScene;
        IObjectResolver gameplayResolver = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerRegistry players = gameplayResolver.Resolve<PlayerRegistry>();
        PlayerController localPlayer = Object.FindAnyObjectByType<PlayerController>();
        Health localHealth = localPlayer.GetComponent<Health>();

        GameObject partnerObject = new("Partner");
        partnerObject.transform.position = new Vector3(1000f, 1000f, 0f);
        PlayerHandle partner = new(partnerObject.transform, partnerObject.AddComponent<Health>());
        players.Add(partner);

        localHealth.ApplyDamage(localHealth.MaxHealth);
        partner.Health.ApplyDamage(partner.Health.MaxHealth);
        Assert.That(players.AnyAlive, Is.False);
        Assert.That(gameFlow.IsTransitioning, Is.False, "Everyone dead at once starts no transition.");

        yield return SceneBootTestHelper.WaitUntil(() => !localHealth.IsDead && !partner.Health.IsDead, "both players to respawn");
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        Assert.That(localPlayer != null, Is.True, "Nothing reloaded the Gameplay scene.");
        Assert.That((Vector2)partnerObject.transform.position, Is.EqualTo(new Vector2(1000f, 1000f)), "A player in no world comes back where it stands.");
        Assert.That(gameFlow.GameplayScene, Is.EqualTo(gameplay));
        Assert.That(gameFlow.IsInGame, Is.True);
        players.Remove(partner);
        Object.Destroy(partnerObject);
    }

    /// <summary>
    /// Booting without an editor boot request shows the main menu, with its scope parented to Main and a button
    /// focused for keyboard and gamepad.
    /// </summary>
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
