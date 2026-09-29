using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;

#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

// Boots the game the same way a build does (Main scene -> GameFlow), so PlayMode tests exercise
// the real additive scene composition instead of a single hand-loaded scene.
public static class SceneBootTestHelper
{
    public const string MainScenePath = "Assets/Scenes/Main.unity";
    public const string StartingAreaPath = "Assets/Scenes/Areas/Area_Clearing.unity";
    private const float TimeoutSeconds = 15f;

    public static IEnumerator BootIntoStartingArea()
    {
#if UNITY_EDITOR
        EditorBootRequest.Set(new[] { StartingAreaPath });
#endif
        yield return LoadMainScene();
        yield return WaitUntil(() => ResolveGameFlow() != null, "Main scene to build its LifetimeScope");

        GameFlow gameFlow = ResolveGameFlow();
        yield return WaitUntil(() => !gameFlow.IsTransitioning && (gameFlow.IsInGame || gameFlow.IsInMenu), "boot to finish");

        if (!gameFlow.IsInGame)
        {
            // Builds (or a missing editor request) boot to the menu first.
            gameFlow.StartNewGameAsync();
            yield return WaitForTransition(gameFlow);
        }

        Assert.That(gameFlow.IsInGame, Is.True, "Boot should end in a gameplay session.");
    }

    public static IEnumerator BootIntoMainMenu()
    {
#if UNITY_EDITOR
        EditorBootRequest.Clear();
#endif
        yield return LoadMainScene();
        yield return WaitUntil(() => ResolveGameFlow() != null, "Main scene to build its LifetimeScope");

        GameFlow gameFlow = ResolveGameFlow();
        yield return WaitUntil(() => !gameFlow.IsTransitioning && gameFlow.IsInMenu, "boot to reach the main menu");
    }

    public static GameFlow ResolveGameFlow()
    {
        MainLifetimeScope scope = UnityEngine.Object.FindAnyObjectByType<MainLifetimeScope>();
        return scope != null && scope.Container != null ? scope.Container.Resolve<GameFlow>() : null;
    }

    // GameFlow flips IsTransitioning synchronously when a transition starts.
    public static IEnumerator WaitForTransition(GameFlow gameFlow)
    {
        yield return WaitUntil(() => !gameFlow.IsTransitioning, "the scene transition to finish");
    }

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

    private static IEnumerator LoadMainScene()
    {
#if UNITY_EDITOR
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
            MainScenePath,
            new LoadSceneParameters(LoadSceneMode.Single));
#else
        yield return SceneManager.LoadSceneAsync(MainScenePath, LoadSceneMode.Single);
#endif
    }
}
