using System;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Thin async wrapper over Unity's additive scene loading and unloading, used by <see cref="GameFlow"/>. In the editor
/// it loads through <c>EditorSceneManager</c> so scenes missing from the build profile still load.
/// </summary>
public sealed class SceneLoader
{
    /// <summary>
    /// Loads <paramref name="definition"/>'s scene additively and returns it once loaded. Throws for a missing or
    /// empty definition, when Unity refuses the load or it does not finish, and when <paramref name="cancellation"/>
    /// fired during the load (the scene stays loaded then).
    /// </summary>
    public async Awaitable<Scene> LoadAdditiveAsync(SceneDefinition definition, CancellationToken cancellation)
    {
        if (definition == null || !definition.IsValid)
        {
            throw new ArgumentException("Cannot load a missing or empty SceneDefinition.", nameof(definition));
        }

#if UNITY_EDITOR
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

    /// <summary>
    /// Unloads <paramref name="scene"/> and waits for it; does nothing for a scene that is not loaded. Throws when
    /// <paramref name="cancellation"/> fired by the time the unload finished.
    /// </summary>
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

    /// <summary>
    /// Frees assets no loaded scene uses any more, then throws if <paramref name="cancellation"/> fired meanwhile.
    /// </summary>
    public async Awaitable UnloadUnusedAssetsAsync(CancellationToken cancellation)
    {
        await Resources.UnloadUnusedAssets();
        cancellation.ThrowIfCancellationRequested();
    }
}
