using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Boots the game the same way a build does (Main scene -&gt; GameFlow), so PlayMode tests exercise
/// the real additive scene composition instead of a single hand-loaded scene.
/// </summary>
public static class SceneBootTestHelper
{
    public const string MainScenePath = "Assets/Scenes/Main.unity";
    public const string TestlandsRootPath = "Assets/Worlds/Testlands/World_Testlands.unity";
    private const float TimeoutSeconds = 15f;

    /// <summary>
    /// Boots into a new game in Testlands, the hand-painted test world, at its default entry, backed by
    /// <paramref name="saveStore"/> or a fresh in-memory store.
    /// </summary>
    public static IEnumerator BootIntoTestlands(ISaveStore saveStore = null)
    {
        yield return BootIntoWorld(TestlandsRootPath, saveStore);
    }

    /// <summary>
    /// Boots into a new game in the world whose root or chunk scene is <paramref name="worldScenePath"/>, at its
    /// default entry, starting a new game in the starting world if the boot lands in the menu, and asserts it ends in
    /// game. Any world's scene can be booted into in the editor, listed in GameScenes or not (see BootFlow).
    /// </summary>
    public static IEnumerator BootIntoWorld(string worldScenePath, ISaveStore saveStore = null)
    {
#if UNITY_EDITOR
        EditorBootRequest.Set(new[] { worldScenePath });
#endif
        yield return LoadMainScene(saveStore);
        yield return WaitUntil(() => ResolveGameFlow() != null, "Main scene to build its LifetimeScope");

        GameFlow gameFlow = ResolveGameFlow();
        yield return WaitUntil(() => !gameFlow.IsTransitioning && (gameFlow.IsInGame || gameFlow.IsInMenu), "boot to finish");

        if (!gameFlow.IsInGame)
        {
            gameFlow.StartWorldGameAsync(gameFlow.Scenes.StartingWorld, gameFlow.Scenes.StartingWorldEntry);
            yield return WaitForTransition(gameFlow);
        }

        Assert.That(gameFlow.IsInGame, Is.True, "Boot should end in a gameplay session.");
    }

    /// <summary>
    /// Clears any editor boot request and boots Main until it settles in the main menu.
    /// </summary>
    public static IEnumerator BootIntoMainMenu(ISaveStore saveStore = null)
    {
#if UNITY_EDITOR
        EditorBootRequest.Clear();
#endif
        yield return LoadMainScene(saveStore);
        yield return WaitUntil(() => ResolveGameFlow() != null, "Main scene to build its LifetimeScope");

        GameFlow gameFlow = ResolveGameFlow();
        yield return WaitUntil(() => !gameFlow.IsTransitioning && gameFlow.IsInMenu, "boot to reach the main menu");
    }

    /// <summary>
    /// The running <see cref="GameFlow"/>, or null before the Main scope is built.
    /// </summary>
    public static GameFlow ResolveGameFlow()
    {
        return ResolveFromMain<GameFlow>();
    }

    /// <summary>
    /// Resolves <typeparamref name="T"/> from the Main scope's container, or default when that scope is not built.
    /// </summary>
    public static T ResolveFromMain<T>()
    {
        MainLifetimeScope scope = UnityEngine.Object.FindAnyObjectByType<MainLifetimeScope>();
        return scope != null && scope.Container != null ? scope.Container.Resolve<T>() : default;
    }

    /// <summary>
    /// Resolves <typeparamref name="T"/> from the Gameplay scope's container, or default when that scope is not built.
    /// </summary>
    public static T ResolveFromGameplay<T>()
    {
        GameplayLifetimeScope scope = UnityEngine.Object.FindAnyObjectByType<GameplayLifetimeScope>();
        return scope != null && scope.Container != null ? scope.Container.Resolve<T>() : default;
    }

    /// <summary>
    /// The status in the gameplay combat settings' catalog named <paramref name="displayName"/>; fails the test when
    /// there is none.
    /// </summary>
    public static StatusEffectDefinition FindStatus(string displayName)
    {
        StatusEffectCatalog catalog = ResolveFromGameplay<CombatSettings>().StatusCatalog;
        for (int i = 0; i < catalog.Count; i++)
        {
            if (catalog.Get(i).DisplayName == displayName)
            {
                return catalog.Get(i);
            }
        }

        Assert.Fail($"The status catalog has no '{displayName}'.");
        return null;
    }

    /// <summary>
    /// Waits for the current scene transition to finish. GameFlow flips IsTransitioning synchronously when a
    /// transition starts.
    /// </summary>
    public static IEnumerator WaitForTransition(GameFlow gameFlow)
    {
        yield return WaitUntil(() => !gameFlow.IsTransitioning, "the scene transition to finish");
    }

    /// <summary>
    /// Yields frames until <paramref name="condition"/> holds, failing the test with <paramref name="description"/>
    /// after the real-time timeout.
    /// </summary>
    public static IEnumerator WaitUntil(Func<bool> condition, string description)
    {
        float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > deadline)
            {
                Assert.Fail($"Timed out after {TimeoutSeconds}s waiting for {description}.");
            }

            yield return null;
        }
    }

    /// <summary>
    /// Loads Main alone with its scope given <paramref name="saveStore"/> (or a fresh in-memory store) as the save
    /// store and an in-memory user settings store, then waits for the scope to build.
    /// </summary>
    private static IEnumerator LoadMainScene(ISaveStore saveStore)
    {
        ISaveStore store = saveStore ?? new MemorySaveStore();
        using (LifetimeScope.Enqueue(builder =>
               {
                   builder.RegisterInstance<ISaveStore>(store);
                   builder.RegisterInstance<ISaveStore>(new MemorySaveStore()).Keyed(UserSettings.FileName);
               }))
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                MainScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(MainScenePath, LoadSceneMode.Single);
#endif
            yield return WaitUntil(() => ResolveGameFlow() != null, "Main scene to build its LifetimeScope");
        }
    }
}
