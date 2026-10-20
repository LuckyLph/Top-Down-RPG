using System;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

/// <summary>
/// Moves the app between the main menu, a new game, area changes and sessions in seamless worlds (offline, hosted or
/// joined) by loading and unloading scenes additively under the right DI scopes (menu and gameplay under Main, the area
/// under gameplay; a world's scenes through the gameplay scope's <see cref="IWorldSession"/>), behind a screen fade.
/// Areas are played offline only; online sessions run in worlds. A trip of the local
/// player between worlds is a transition too, flagged <see cref="IsTravelling"/>: a local fade only, which loads and
/// unloads nothing itself and never stops game time. Runs one transition at a time and ignores requests made while
/// one runs.
/// </summary>
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
    private Vector2 restartPosition;

    /// <summary>
    /// Creates the flow; <paramref name="mainScope"/> is the parent scope for the menu and gameplay scenes.
    /// </summary>
    public GameFlow(SceneLoader sceneLoader, ScreenFader screenFader, GameScenes gameScenes, LifetimeScope mainScope)
    {
        this.sceneLoader = sceneLoader;
        this.screenFader = screenFader;
        this.gameScenes = gameScenes;
        this.mainScope = mainScope;
    }

    public event Action TransitionStarted;
    public event Action TransitionFinished;

    /// <summary>
    /// Raised as a world session is about to end (the menu, a new session, a restart), while its players are still in
    /// their worlds and before anything unloads.
    /// </summary>
    public event Action WorldSessionEnding;

    public bool IsTransitioning { get; private set; }
    public bool IsTravelling { get; private set; }
    public bool IsInMenu => IsLoaded(menuScene);
    public bool IsInGame => IsLoaded(gameplayScene) && (IsLoaded(areaScene) || CurrentWorld != null);
    public SceneDefinition CurrentArea { get; private set; }
    public WorldDefinition CurrentWorld { get; private set; }
    public Scene AreaScene => areaScene;
    public Scene GameplayScene => gameplayScene;
    public GameScenes Scenes => gameScenes;

    /// <summary>
    /// Unloads any area and gameplay scene and shows the main menu, loading it if needed.
    /// </summary>
    public Awaitable ShowMainMenuAsync(CancellationToken cancellation = default)
    {
        return RunTransitionAsync(ShowMainMenuCoreAsync, cancellation);
    }

    /// <summary>
    /// Starts a new game session in an area: unloads the menu and any running game, loads a fresh gameplay scene, then
    /// enters <paramref name="area"/> at <paramref name="spawnId"/> (the starting area and spawn id when omitted). Areas
    /// are played offline and are not saved.
    /// </summary>
    public Awaitable StartNewGameAsync(
        SceneDefinition area = null,
        string spawnId = null,
        CancellationToken cancellation = default)
    {
        SceneDefinition targetArea = area != null ? area : gameScenes.StartingArea;
        string targetSpawn = string.IsNullOrEmpty(spawnId) ? gameScenes.StartingSpawnId : spawnId;
        return RunTransitionAsync(token => StartNewGameCoreAsync(targetArea, targetSpawn, token), cancellation);
    }

    /// <summary>
    /// Starts a new game session in the seamless <paramref name="world"/> at its entry <paramref name="entryName"/> (its
    /// default entry when null), as the overload taking a position does.
    /// </summary>
    public Awaitable StartWorldGameAsync(WorldDefinition world, string entryName = null, CancellationToken cancellation = default)
    {
        return StartWorldGameAsync(world, world.EntryPosition(entryName), cancellation);
    }

    /// <summary>
    /// Starts a new game session in the seamless <paramref name="world"/>: unloads the menu and any running game,
    /// loads a fresh gameplay scene, and enters the world at the world position <paramref name="position"/> (an entry's,
    /// or a saved place) through the gameplay scope's <see cref="IWorldSession"/>, fading in once the ground around the
    /// players is loaded.
    /// </summary>
    public Awaitable StartWorldGameAsync(WorldDefinition world, Vector2 position, CancellationToken cancellation = default)
    {
        return RunTransitionAsync(token => StartWorldGameCoreAsync(world, position, token), cancellation);
    }

    /// <summary>
    /// Starts a joined client's session in whichever world the host puts its player: unloads the menu and any running
    /// game, loads a fresh gameplay scene, and lets the gameplay scope's <see cref="IWorldSession"/> wait for the
    /// host's spawn of this machine's player and the ground around it, fading in once it is loaded. When the session
    /// ends before the player is placed it warns and leaves the gameplay scene without a world, and the session's
    /// lifecycle takes the game back to the menu.
    /// </summary>
    public Awaitable StartJoinedGameAsync(CancellationToken cancellation = default)
    {
        return RunTransitionAsync(StartJoinedGameCoreAsync, cancellation);
    }

    /// <summary>
    /// Takes the local player on a trip to <paramref name="world"/>'s entry <paramref name="entryName"/>, once the
    /// trip's authority has approved it and put the player in that world: fades out, has the gameplay scope's
    /// <see cref="IWorldSession"/> move the local player to the entry and wait for the ground around it, and fades in.
    /// The world becomes <see cref="CurrentWorld"/> and the entry the one a restart uses. Gameplay input is off for the
    /// whole trip, as in every transition, but game time runs on (<see cref="IsTravelling"/>). Logs an error and does
    /// nothing outside a world session.
    /// </summary>
    public async Awaitable TravelAsync(WorldDefinition world, string entryName, CancellationToken cancellation = default)
    {
        if (world == null || CurrentWorld == null || !TryGetWorldSession(out _))
        {
            Debug.LogError($"{nameof(GameFlow)} can only travel to a world during a world session.");
            return;
        }

        await RunTransitionAsync(token => TravelCoreAsync(world, entryName, token), cancellation, true);
    }

    /// <summary>
    /// Starts the running session over where the local player is: the current world (the one it last travelled to) at
    /// the entry it arrived by, or where the session started when it has not travelled, or the current area at the
    /// starting spawn id.
    /// </summary>
    public Awaitable RestartAsync(CancellationToken cancellation = default)
    {
        return CurrentWorld != null
            ? StartWorldGameAsync(CurrentWorld, restartPosition, cancellation)
            : StartNewGameAsync(CurrentArea, null, cancellation);
    }

    /// <summary>
    /// Swaps the current area for <paramref name="area"/>, entering at <paramref name="spawnId"/>, while keeping the
    /// gameplay scene (and the players in it). Logs an error and does nothing when no game is running or the session is
    /// in a world.
    /// </summary>
    public async Awaitable ChangeAreaAsync(SceneDefinition area, string spawnId, CancellationToken cancellation = default)
    {
        if (!IsLoaded(gameplayScene))
        {
            Debug.LogError($"{nameof(GameFlow)} cannot change area while no gameplay session is running.");
            return;
        }

        if (CurrentWorld != null)
        {
            Debug.LogError($"{nameof(GameFlow)} cannot change area during a session in the world {CurrentWorld.name}.");
            return;
        }

        await RunTransitionAsync(token => LoadAreaCoreAsync(area, spawnId, token), cancellation);
    }

    /// <summary>
    /// Runs <paramref name="transition"/> between a fade out and a fade in, raising <see cref="TransitionStarted"/>
    /// and, whatever happens, <see cref="TransitionFinished"/>, with <see cref="IsTravelling"/> set for a trip
    /// (<paramref name="travel"/>). Cancellation (also on application exit) ends it quietly; other exceptions are
    /// logged and rethrown. Warns and does nothing while another transition runs.
    /// </summary>
    private async Awaitable RunTransitionAsync(Func<CancellationToken, Awaitable> transition, CancellationToken cancellation, bool travel = false)
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
        IsTravelling = travel;
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
            IsTravelling = false;
            TransitionFinished?.Invoke();
        }
    }

    /// <summary>
    /// Unloads the area and gameplay scenes and unused assets, loads the menu under the main scope unless it is
    /// already loaded, and makes it the active scene.
    /// </summary>
    private async Awaitable ShowMainMenuCoreAsync(CancellationToken cancellation)
    {
        await LeaveWorldAsync(cancellation);
        await UnloadAreaAsync(cancellation);
        await UnloadGameplayAsync(cancellation);
        await sceneLoader.UnloadUnusedAssetsAsync(cancellation);

        if (!IsLoaded(menuScene))
        {
            menuScene = await LoadUnderScopeAsync(gameScenes.MainMenu, mainScope, cancellation);
        }

        SceneManager.SetActiveScene(menuScene);
    }

    /// <summary>
    /// Unloads the menu and any running game, loads the gameplay scene under the main scope, finds its scope, and
    /// loads <paramref name="area"/> under it.
    /// </summary>
    private async Awaitable StartNewGameCoreAsync(SceneDefinition area, string spawnId, CancellationToken cancellation)
    {
        if (IsLoaded(menuScene))
        {
            await sceneLoader.UnloadAsync(menuScene, cancellation);
            menuScene = default;
        }

        await LeaveWorldAsync(cancellation);
        await UnloadAreaAsync(cancellation);
        await UnloadGameplayAsync(cancellation);
        gameplayScene = await LoadUnderScopeAsync(gameScenes.Gameplay, mainScope, cancellation);
        gameplayScope = LifetimeScope.Find<LifetimeScope>(gameplayScene);

        await LoadAreaCoreAsync(area, spawnId, cancellation);
    }

    /// <summary>
    /// Unloads the menu and any running game, loads the gameplay scene under the main scope, makes it the active scene
    /// and enters <paramref name="world"/> at <paramref name="position"/> through its scope's
    /// <see cref="IWorldSession"/>. Logs an error and leaves the gameplay scene without a world when the scope provides
    /// none.
    /// </summary>
    private async Awaitable StartWorldGameCoreAsync(WorldDefinition world, Vector2 position, CancellationToken cancellation)
    {
        if (IsLoaded(menuScene))
        {
            await sceneLoader.UnloadAsync(menuScene, cancellation);
            menuScene = default;
        }

        await LeaveWorldAsync(cancellation);
        await UnloadAreaAsync(cancellation);
        await UnloadGameplayAsync(cancellation);
        await sceneLoader.UnloadUnusedAssetsAsync(cancellation);
        gameplayScene = await LoadUnderScopeAsync(gameScenes.Gameplay, mainScope, cancellation);
        gameplayScope = LifetimeScope.Find<LifetimeScope>(gameplayScene);
        SceneManager.SetActiveScene(gameplayScene);

        if (!TryGetWorldSession(out IWorldSession session))
        {
            Debug.LogError($"The gameplay scene's scope provides no {nameof(IWorldSession)}; cannot enter {world.name}.");
            return;
        }

        CurrentWorld = world;
        restartPosition = position;
        await session.EnterAsync(world, position, cancellation);
    }

    /// <summary>
    /// Unloads the menu and any running game, loads the gameplay scene under the main scope, makes it the active scene
    /// and joins the host's world through its scope's <see cref="IWorldSession"/>, recording the world the host put the
    /// local player in as <see cref="CurrentWorld"/>.
    /// </summary>
    private async Awaitable StartJoinedGameCoreAsync(CancellationToken cancellation)
    {
        if (IsLoaded(menuScene))
        {
            await sceneLoader.UnloadAsync(menuScene, cancellation);
            menuScene = default;
        }

        await LeaveWorldAsync(cancellation);
        await UnloadAreaAsync(cancellation);
        await UnloadGameplayAsync(cancellation);
        await sceneLoader.UnloadUnusedAssetsAsync(cancellation);
        gameplayScene = await LoadUnderScopeAsync(gameScenes.Gameplay, mainScope, cancellation);
        gameplayScope = LifetimeScope.Find<LifetimeScope>(gameplayScene);
        SceneManager.SetActiveScene(gameplayScene);

        if (!TryGetWorldSession(out IWorldSession session))
        {
            Debug.LogError($"The gameplay scene's scope provides no {nameof(IWorldSession)}; cannot join the host's world.");
            return;
        }

        WorldDefinition world = await session.JoinAsync(cancellation);
        if (world == null)
        {
            Debug.LogWarning("The session ended before the host placed this player in a world.");
            return;
        }

        CurrentWorld = world;
        restartPosition = world.EntryPosition(null);
    }

    /// <summary>
    /// Records <paramref name="world"/> and <paramref name="entryName"/> as where the session is, then has the world
    /// session bring the local player to that entry once the screen is dark. Logs an error when the session went
    /// meanwhile.
    /// </summary>
    private async Awaitable TravelCoreAsync(WorldDefinition world, string entryName, CancellationToken cancellation)
    {
        if (CurrentWorld == null || !TryGetWorldSession(out IWorldSession session))
        {
            Debug.LogError($"{nameof(GameFlow)} lost the world session before the trip to {world.name}.");
            return;
        }

        CurrentWorld = world;
        restartPosition = world.EntryPosition(entryName);
        await session.ArriveAsync(world, entryName, cancellation);
    }

    /// <summary>
    /// Ends the world session, if one runs: raises <see cref="WorldSessionEnding"/>, then unloads its worlds and chunks
    /// before anything else unloads.
    /// </summary>
    private async Awaitable LeaveWorldAsync(CancellationToken cancellation)
    {
        bool inWorld = CurrentWorld != null;
        if (inWorld)
        {
            WorldSessionEnding?.Invoke();
        }

        CurrentWorld = null;
        if (TryGetWorldSession(out IWorldSession session) && (inWorld || session.IsActive))
        {
            await session.LeaveAsync(cancellation);
        }
    }

    /// <summary>
    /// The gameplay scope's <see cref="IWorldSession"/>; false when no gameplay scene is loaded or its scope provides
    /// none.
    /// </summary>
    private bool TryGetWorldSession(out IWorldSession session)
    {
        session = null;
        return IsLoaded(gameplayScene) && gameplayScope != null && gameplayScope.Container != null
            && gameplayScope.Container.TryResolve(out session);
    }

    /// <summary>
    /// Unloads the current area and unused assets, loads <paramref name="area"/> under the gameplay scope with an
    /// <see cref="AreaEntryRequest"/> for <paramref name="spawnId"/> registered into its scope, makes it the active
    /// scene and enters it.
    /// </summary>
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

    /// <summary>
    /// Loads <paramref name="definition"/> additively with <paramref name="parent"/> queued as the parent of the
    /// scopes the scene creates.
    /// </summary>
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

    /// <summary>
    /// Unloads the area scene, if loaded, forgetting it and <see cref="CurrentArea"/> before the unload starts.
    /// </summary>
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

    /// <summary>
    /// Unloads the gameplay scene, if loaded, forgetting it and its scope before the unload starts.
    /// </summary>
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

    /// <summary>
    /// Calls <see cref="IAreaEntry.Enter"/> from the area scene's scope; warns and leaves the players where they are
    /// when the scene has no scope providing one.
    /// </summary>
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

    /// <summary>
    /// Whether <paramref name="scene"/> is a valid, fully loaded scene; false for the default handle.
    /// </summary>
    private static bool IsLoaded(Scene scene)
    {
        return scene.IsValid() && scene.isLoaded;
    }
}
