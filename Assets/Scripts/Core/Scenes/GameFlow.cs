using System;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

/// <summary>
/// Moves the app between the main menu and sessions in seamless worlds (offline, hosted or joined) by loading and
/// unloading scenes additively under the right DI scopes (menu and gameplay under Main; a world's scenes through the
/// gameplay scope's <see cref="IWorldSession"/>), behind a screen fade. A trip of the local player between worlds, or
/// to a waystone as it respawns, is a transition too, flagged <see cref="IsTravelling"/>: a local fade only, which
/// loads and unloads nothing itself and never stops game time. Runs one transition at a time and ignores requests made
/// while one runs.
/// </summary>
public sealed class GameFlow
{
    private readonly SceneLoader sceneLoader;
    private readonly ScreenFader screenFader;
    private readonly GameScenes gameScenes;
    private readonly LifetimeScope mainScope;

    private Scene menuScene;
    private Scene gameplayScene;
    private LifetimeScope gameplayScope;
    private WorldDefinition enteringWorld;

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
    /// Raised as a world session is about to end (the menu, a new session), while its players are still in their worlds
    /// and before anything unloads.
    /// </summary>
    public event Action WorldSessionEnding;

    public bool IsTransitioning { get; private set; }
    public bool IsTravelling { get; private set; }
    public bool IsInMenu => IsLoaded(menuScene);
    public bool IsInGame => IsLoaded(gameplayScene) && CurrentWorld != null;
    public WorldDefinition CurrentWorld { get; private set; }
    public Scene GameplayScene => gameplayScene;
    public GameScenes Scenes => gameScenes;

    /// <summary>
    /// Unloads any gameplay scene and shows the main menu, loading it if needed.
    /// </summary>
    public Awaitable ShowMainMenuAsync(CancellationToken cancellation = default)
    {
        return RunTransitionAsync(ShowMainMenuCoreAsync, cancellation);
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
    /// trip's authority has approved it (or sent the dead player to a waystone) and put the player in that world: fades
    /// out, has the gameplay scope's <see cref="IWorldSession"/> move the local player to the entry and wait for the
    /// ground around it, and fades in. The world becomes <see cref="CurrentWorld"/>. Gameplay input is off for the
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
    /// Ends any world session, unloads the gameplay scene and unused assets, loads the menu under the main scope unless
    /// it is already loaded, and makes it the active scene.
    /// </summary>
    private async Awaitable ShowMainMenuCoreAsync(CancellationToken cancellation)
    {
        await LeaveWorldAsync(cancellation);
        await UnloadGameplayAsync(cancellation);
        await sceneLoader.UnloadUnusedAssetsAsync(cancellation);

        if (!IsLoaded(menuScene))
        {
            menuScene = await LoadUnderScopeAsync(gameScenes.MainMenu, mainScope, cancellation);
        }

        SceneManager.SetActiveScene(menuScene);
    }

    /// <summary>
    /// Unloads the menu and any running game, loads the gameplay scene under the main scope, makes it the active scene
    /// and enters <paramref name="world"/> at <paramref name="position"/> through its scope's
    /// <see cref="IWorldSession"/>. The world is held while unused assets unload, so one that no loaded object
    /// references (a world booted from the editor that <see cref="GameScenes"/> does not list) stays loaded. Logs an error
    /// and leaves the gameplay scene without a world when the scope provides none.
    /// </summary>
    private async Awaitable StartWorldGameCoreAsync(WorldDefinition world, Vector2 position, CancellationToken cancellation)
    {
        enteringWorld = world;
        try
        {
            await ReplaceWithGameplayAsync(cancellation);
        }
        finally
        {
            enteringWorld = null;
        }

        if (!TryGetWorldSession(out IWorldSession session))
        {
            Debug.LogError($"The gameplay scene's scope provides no {nameof(IWorldSession)}; cannot enter {world.name}.");
            return;
        }

        CurrentWorld = world;
        await session.EnterAsync(world, position, cancellation);
    }

    /// <summary>
    /// Unloads the menu and any running game, loads the gameplay scene under the main scope, makes it the active scene
    /// and joins the host's world through its scope's <see cref="IWorldSession"/>, recording the world the host put the
    /// local player in as <see cref="CurrentWorld"/>.
    /// </summary>
    private async Awaitable StartJoinedGameCoreAsync(CancellationToken cancellation)
    {
        await ReplaceWithGameplayAsync(cancellation);
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
    }

    /// <summary>
    /// Unloads the menu, ends any world session, unloads the gameplay scene and unused assets, then loads a fresh
    /// gameplay scene under the main scope, finds its scope and makes it the active scene.
    /// </summary>
    private async Awaitable ReplaceWithGameplayAsync(CancellationToken cancellation)
    {
        if (IsLoaded(menuScene))
        {
            await sceneLoader.UnloadAsync(menuScene, cancellation);
            menuScene = default;
        }

        await LeaveWorldAsync(cancellation);
        await UnloadGameplayAsync(cancellation);
        await sceneLoader.UnloadUnusedAssetsAsync(cancellation);
        gameplayScene = await LoadUnderScopeAsync(gameScenes.Gameplay, mainScope, cancellation);
        gameplayScope = LifetimeScope.Find<LifetimeScope>(gameplayScene);
        SceneManager.SetActiveScene(gameplayScene);
    }

    /// <summary>
    /// Records <paramref name="world"/> as where the session is, then has the world session bring the local player to
    /// its entry <paramref name="entryName"/> once the screen is dark. Logs an error when the session went meanwhile.
    /// </summary>
    private async Awaitable TravelCoreAsync(WorldDefinition world, string entryName, CancellationToken cancellation)
    {
        if (CurrentWorld == null || !TryGetWorldSession(out IWorldSession session))
        {
            Debug.LogError($"{nameof(GameFlow)} lost the world session before the trip to {world.name}.");
            return;
        }

        CurrentWorld = world;
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
    /// Whether <paramref name="scene"/> is a valid, fully loaded scene; false for the default handle.
    /// </summary>
    private static bool IsLoaded(Scene scene)
    {
        return scene.IsValid() && scene.isLoaded;
    }
}
