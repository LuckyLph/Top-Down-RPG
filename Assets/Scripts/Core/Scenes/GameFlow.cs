using System;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

public sealed class GameFlow
{
    private readonly SceneLoader sceneLoader;
    private readonly ScreenFader screenFader;
    private readonly GameScenes gameScenes;
    private readonly LifetimeScope mainScope;

    private Scene menuScene;
    private Scene gameplayScene;
    private Scene areaScene;
    private LifetimeScope gameplayScope;

    public GameFlow(SceneLoader sceneLoader, ScreenFader screenFader, GameScenes gameScenes, LifetimeScope mainScope)
    {
        this.sceneLoader = sceneLoader;
        this.screenFader = screenFader;
        this.gameScenes = gameScenes;
        this.mainScope = mainScope;
    }

    public event Action TransitionStarted;
    public event Action TransitionFinished;
    public event Action<AreaTransition> AreaLoading;

    public bool IsTransitioning { get; private set; }
    public bool IsInMenu => IsLoaded(menuScene);
    public bool IsInGame => IsLoaded(gameplayScene) && IsLoaded(areaScene);
    public SceneDefinition CurrentArea { get; private set; }
    public Scene AreaScene => areaScene;
    public Scene GameplayScene => gameplayScene;
    public GameScenes Scenes => gameScenes;

    public Awaitable ShowMainMenuAsync(CancellationToken cancellation = default)
    {
        return RunTransitionAsync(ShowMainMenuCoreAsync, cancellation);
    }

    public Awaitable StartNewGameAsync(
        SceneDefinition area = null,
        string spawnId = null,
        CancellationToken cancellation = default)
    {
        SceneDefinition targetArea = area != null ? area : gameScenes.StartingArea;
        string targetSpawn = string.IsNullOrEmpty(spawnId) ? gameScenes.StartingSpawnId : spawnId;
        return RunTransitionAsync(
            token =>
            {
                AreaLoading?.Invoke(new AreaTransition(targetArea, targetSpawn, true));
                return StartNewGameCoreAsync(targetArea, targetSpawn, token);
            },
            cancellation);
    }

    public async Awaitable ChangeAreaAsync(SceneDefinition area, string spawnId, CancellationToken cancellation = default)
    {
        if (!IsLoaded(gameplayScene))
        {
            Debug.LogError($"{nameof(GameFlow)} cannot change area while no gameplay session is running.");
            return;
        }

        await RunTransitionAsync(
            token =>
            {
                AreaLoading?.Invoke(new AreaTransition(area, spawnId, false));
                return LoadAreaCoreAsync(area, spawnId, token);
            },
            cancellation);
    }

    private async Awaitable RunTransitionAsync(Func<CancellationToken, Awaitable> transition, CancellationToken cancellation)
    {
        if (IsTransitioning)
        {
            Debug.LogWarning($"{nameof(GameFlow)} ignored a transition request because another transition is running.");
            return;
        }

        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(cancellation, Application.exitCancellationToken);
        CancellationToken token = linked.Token;

        IsTransitioning = true;
        TransitionStarted?.Invoke();
        try
        {
            await screenFader.FadeOutAsync(token);
            await transition(token);
            await screenFader.FadeInAsync(token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            throw;
        }
        finally
        {
            IsTransitioning = false;
            TransitionFinished?.Invoke();
        }
    }

    private async Awaitable ShowMainMenuCoreAsync(CancellationToken cancellation)
    {
        await UnloadAreaAsync(cancellation);
        await UnloadGameplayAsync(cancellation);
        await sceneLoader.UnloadUnusedAssetsAsync(cancellation);

        if (!IsLoaded(menuScene))
        {
            menuScene = await LoadUnderScopeAsync(gameScenes.MainMenu, mainScope, cancellation);
        }

        SceneManager.SetActiveScene(menuScene);
    }

    private async Awaitable StartNewGameCoreAsync(SceneDefinition area, string spawnId, CancellationToken cancellation)
    {
        if (IsLoaded(menuScene))
        {
            await sceneLoader.UnloadAsync(menuScene, cancellation);
            menuScene = default;
        }

        await UnloadAreaAsync(cancellation);
        await UnloadGameplayAsync(cancellation);
        gameplayScene = await LoadUnderScopeAsync(gameScenes.Gameplay, mainScope, cancellation);
        gameplayScope = LifetimeScope.Find<LifetimeScope>(gameplayScene);

        await LoadAreaCoreAsync(area, spawnId, cancellation);
    }

    private async Awaitable LoadAreaCoreAsync(SceneDefinition area, string spawnId, CancellationToken cancellation)
    {
        await UnloadAreaAsync(cancellation);
        await sceneLoader.UnloadUnusedAssetsAsync(cancellation);

        AreaEntryRequest entryRequest = new AreaEntryRequest(area, spawnId);
        using (LifetimeScope.Enqueue(builder => builder.RegisterInstance(entryRequest)))
        {
            areaScene = await LoadUnderScopeAsync(area, gameplayScope, cancellation);
        }

        CurrentArea = area;

        SceneManager.SetActiveScene(areaScene);
        EnterArea(areaScene);
    }

    private async Awaitable<Scene> LoadUnderScopeAsync(
        SceneDefinition definition,
        LifetimeScope parent,
        CancellationToken cancellation)
    {
        using (LifetimeScope.EnqueueParent(parent))
        {
            return await sceneLoader.LoadAdditiveAsync(definition, cancellation);
        }
    }

    private async Awaitable UnloadAreaAsync(CancellationToken cancellation)
    {
        if (!IsLoaded(areaScene))
        {
            return;
        }

        Scene scene = areaScene;
        areaScene = default;
        CurrentArea = null;
        await sceneLoader.UnloadAsync(scene, cancellation);
    }

    private async Awaitable UnloadGameplayAsync(CancellationToken cancellation)
    {
        if (!IsLoaded(gameplayScene))
        {
            return;
        }

        Scene scene = gameplayScene;
        gameplayScene = default;
        gameplayScope = null;
        await sceneLoader.UnloadAsync(scene, cancellation);
    }

    private static void EnterArea(Scene scene)
    {
        LifetimeScope scope = LifetimeScope.Find<LifetimeScope>(scene);
        if (scope == null || scope.Container == null || !scope.Container.TryResolve(out IAreaEntry areaEntry))
        {
            Debug.LogWarning(
                $"Area scene '{scene.name}' has no LifetimeScope providing {nameof(IAreaEntry)}; the player was not placed.");
            return;
        }

        areaEntry.Enter();
    }

    private static bool IsLoaded(Scene scene)
    {
        return scene.IsValid() && scene.isLoaded;
    }
}
