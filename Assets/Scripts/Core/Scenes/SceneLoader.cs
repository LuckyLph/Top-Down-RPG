using System;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

public sealed class SceneLoader
{
    public async Awaitable<Scene> LoadAdditiveAsync(SceneDefinition definition, CancellationToken cancellation)
    {
        if (definition == null || !definition.IsValid)
        {
            throw new ArgumentException("Cannot load a missing or empty SceneDefinition.", nameof(definition));
        }

#if UNITY_EDITOR
        // Loads by path even when the scene is missing from the build list, so new areas can be
        // played before they are registered. SceneBuildValidator catches the omission at build time.
        AsyncOperation operation = EditorSceneManager.LoadSceneAsyncInPlayMode(
            definition.ScenePath,
            new LoadSceneParameters(LoadSceneMode.Additive));
#else
        AsyncOperation operation = SceneManager.LoadSceneAsync(definition.ScenePath, LoadSceneMode.Additive);
#endif
        if (operation == null)
        {
            throw new InvalidOperationException($"Unity refused to load scene '{definition.ScenePath}'.");
        }

        await operation;
        cancellation.ThrowIfCancellationRequested();

        Scene scene = SceneManager.GetSceneByPath(definition.ScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            throw new InvalidOperationException($"Scene '{definition.ScenePath}' did not finish loading.");
        }

        return scene;
    }

    public async Awaitable UnloadAsync(Scene scene, CancellationToken cancellation)
    {
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return;
        }

        AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);
        if (operation != null)
        {
            await operation;
        }

        cancellation.ThrowIfCancellationRequested();
    }

    // Additive unloads never trigger the automatic cleanup that single-mode loads do.
    public async Awaitable UnloadUnusedAssetsAsync(CancellationToken cancellation)
    {
        await Resources.UnloadUnusedAssets();
        cancellation.ThrowIfCancellationRequested();
    }
}
