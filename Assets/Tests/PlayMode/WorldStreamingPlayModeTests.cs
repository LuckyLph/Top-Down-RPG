using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer.Unity;

/// <summary>
/// PlayMode tests for seamless worlds on one machine: booting into a world, the chunks loading around the player under
/// the right scopes, chunks following the player across the world, and leaving to the menu unloading everything.
/// </summary>
public class WorldStreamingPlayModeTests
{
    private const string SandboxRootPath = "Assets/Worlds/Sandbox/World_Sandbox.unity";
    private const string WorldsFolder = "Assets/Worlds/";

    /// <summary>
    /// Booting with the Sandbox's root scene open starts a session in that world at its default entry: the player
    /// stands there, the world's scope is a child of Gameplay and each chunk's scope a child of the world's, its
    /// lighting is on, and exactly the chunks within the view plus the load margin are loaded.
    /// </summary>
    [UnityTest]
    public IEnumerator Boot_EntersTheWorld_WithTheChunksAroundThePlayerUnderTheWorldsScope()
    {
        yield return SceneBootTestHelper.BootIntoArea(SandboxRootPath);

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        WorldDefinition world = gameFlow.CurrentWorld;
        Assert.That(world, Is.Not.Null);
        Assert.That(world.name, Is.EqualTo("Sandbox"));
        Assert.That(gameFlow.CurrentArea, Is.Null);

        Worlds worlds = SceneBootTestHelper.ResolveFromGameplay<Worlds>();
        Assert.That(worlds.TryGet(world, out LoadedWorld loaded), Is.True);
        LifetimeScope gameplayScope = Object.FindAnyObjectByType<GameplayLifetimeScope>();
        Assert.That(loaded.Scope, Is.InstanceOf<WorldLifetimeScope>());
        Assert.That(loaded.Scope.Parent, Is.SameAs(gameplayScope));
        Assert.That(loaded.Lighting, Is.Not.Null);
        Assert.That(loaded.Lighting.gameObject.activeSelf, Is.True, "The local player's world shows its lighting.");

        Assert.That(loaded.Chunks.Keys, Is.EquivalentTo(new[] { new Vector2Int(0, 0), new Vector2Int(0, 1) }),
            "The entry at (30.5, 42) reaches 26 cells across and 22 up and down: chunk column 0, rows 0 and 1.");
        foreach (Scene chunkScene in loaded.Chunks.Values)
        {
            LifetimeScope chunkScope = LifetimeScope.Find<LifetimeScope>(chunkScene);
            Assert.That(chunkScope, Is.InstanceOf<ChunkLifetimeScope>());
            Assert.That(chunkScope.Parent, Is.SameAs(loaded.Scope));
        }

        PlayerController player = LocalPlayer().Controller;
        Assert.That((Vector2)player.transform.position, Is.EqualTo(world.ToWorld(world.DefaultEntry.Position)));
        Assert.That(SceneBootTestHelper.ResolveFromGameplay<PlayerWorlds>().Get(LocalPlayer().Handle), Is.SameAs(world));
    }

    /// <summary>
    /// When the player moves to the far corner of the world, the chunks there load and the ones left behind unload;
    /// going back to the menu unloads every scene of the world.
    /// </summary>
    [UnityTest]
    public IEnumerator Moving_StreamsChunksAroundThePlayer_AndTheMenuUnloadsTheWorld()
    {
        yield return SceneBootTestHelper.BootIntoArea(SandboxRootPath);

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        WorldDefinition world = gameFlow.CurrentWorld;
        Worlds worlds = SceneBootTestHelper.ResolveFromGameplay<Worlds>();
        worlds.TryGet(world, out LoadedWorld loaded);

        LocalPlayer().Controller.Teleport(world.ToWorld(new Vector2(100.5f, 100.5f)));
        yield return SceneBootTestHelper.WaitUntil(
            () => loaded.Chunks.Count == 1 && loaded.Chunks.ContainsKey(new Vector2Int(1, 1)),
            "the chunks to follow the player to (1, 1)");
        Assert.That(LoadedWorldScenes(), Is.EquivalentTo(new[] { "World_Sandbox", "World_Sandbox_1_1" }));

        gameFlow.ShowMainMenuAsync();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        Assert.That(gameFlow.IsInMenu, Is.True);
        Assert.That(gameFlow.CurrentWorld, Is.Null);
        Assert.That(LoadedWorldScenes(), Is.Empty, "Leaving the session unloads every world and chunk scene.");
    }

    private static LocalPlayer LocalPlayer()
    {
        return SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current;
    }

    /// <summary>
    /// The names of the loaded scenes that belong to a world.
    /// </summary>
    private static List<string> LoadedWorldScenes()
    {
        List<string> names = new();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.isLoaded && scene.path.StartsWith(WorldsFolder))
            {
                names.Add(scene.name);
            }
        }

        return names;
    }
}
