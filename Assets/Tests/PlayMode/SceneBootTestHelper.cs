using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Boots the game the same way a build does (Main scene -&gt; GameFlow), so PlayMode tests exercise
/// the real additive scene composition instead of a single hand-loaded scene. Every boot pins the starting world: the
/// Main scope gets a copy of the project's <see cref="GameScenes"/> starting at Testlands' <c>start</c> (or wherever a
/// test names), destroyed with that scope, so no test starts, hosts or places a joining player in the project's own
/// starting world by accident. The user settings start past the first run (Docs/UiRevamp.md, Phase 11), so the main
/// menu opens on its panel, unless a test gives settings of its own.
/// </summary>
public static class SceneBootTestHelper
{
    public const string MainScenePath = "Assets/Scenes/Main.unity";
    public const string GameScenesPath = "Assets/Data/Scenes/GameScenes.asset";
    public const string TestlandsRootPath = "Assets/Worlds/Testlands/World_Testlands.unity";
    public const string TestlandsStartEntry = "start";
    private const float TimeoutSeconds = 15f;
    public const string FirstRunDoneSettings = "{\"version\":1,\"firstRunDone\":true}";

    /// <summary>
    /// Boots into a new game in Testlands, the hand-painted test world, at its default entry, backed by
    /// <paramref name="saves"/> or a fresh in-memory save folder.
    /// </summary>
    public static IEnumerator BootIntoTestlands(ISaveFolder saves = null)
    {
        yield return BootIntoWorld(TestlandsRootPath, saves);
    }

    /// <summary>
    /// Boots into a new game in the world whose root or chunk scene is <paramref name="worldScenePath"/>, at its
    /// default entry, starting a new game in the starting world (Testlands' <c>start</c>) if the boot lands in the
    /// menu, and asserts it ends in game. Any world's scene can be booted into in the editor, listed in GameScenes or
    /// not (see BootFlow).
    /// </summary>
    public static IEnumerator BootIntoWorld(string worldScenePath, ISaveFolder saves = null)
    {
#if UNITY_EDITOR
        EditorBootRequest.Set(new[] { worldScenePath });
#endif
        yield return LoadMainScene(saves, TestlandsRootPath, TestlandsStartEntry);
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
    /// Clears any editor boot request and boots Main until it settles in the main menu, with the starting world pinned
    /// to the entry <paramref name="startingEntry"/> of the world whose root or chunk scene is
    /// <paramref name="startingWorldPath"/> (Testlands' <c>start</c> by default), or with the project's own starting
    /// world when <paramref name="startingWorldPath"/> is null; the user settings are <paramref name="settings"/>, or
    /// settings past the first run.
    /// </summary>
    public static IEnumerator BootIntoMainMenu(ISaveFolder saves = null, string startingWorldPath = TestlandsRootPath, string startingEntry = TestlandsStartEntry, ISaveStore settings = null)
    {
#if UNITY_EDITOR
        EditorBootRequest.Clear();
#endif
        yield return LoadMainScene(saves, startingWorldPath, startingEntry, settings);
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
    /// Loads Main alone with its scope given <paramref name="saves"/> (or a fresh in-memory folder) as the save
    /// folder, <paramref name="settings"/> (or an in-memory user settings store past the first run) and, unless <paramref name="startingWorldPath"/> is null, a copy of the
    /// project's <see cref="GameScenes"/> starting at that world's <paramref name="startingEntry"/>, registered after the
    /// asset the scope registers, so it is the one resolved, and destroyed with the scope; then waits for the scope to
    /// build.
    /// </summary>
    private static IEnumerator LoadMainScene(ISaveFolder saves, string startingWorldPath, string startingEntry, ISaveStore settings = null)
    {
        ISaveFolder folder = saves ?? new MemorySaveFolder();
        ISaveStore settingsStore = settings ?? new MemorySaveStore { Contents = FirstRunDoneSettings };
        GameScenes scenes = startingWorldPath != null ? CopyScenesStartingAt(startingWorldPath, startingEntry) : null;
        using (LifetimeScope.Enqueue(builder =>
               {
                   builder.RegisterInstance<ISaveFolder>(folder);
                   builder.RegisterInstance(settingsStore).Keyed(UserSettings.FileName);
                   if (scenes != null && builder.Exists(typeof(GameScenes)))
                   {
                       builder.RegisterInstance(scenes);
                       builder.RegisterDisposeCallback(_ => DestroyCopy(scenes));
                   }
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

    /// <summary>
    /// A copy of the project's <see cref="GameScenes"/> starting at the entry <paramref name="entry"/> of the world it
    /// lists whose root or chunk scene is <paramref name="worldScenePath"/>; fails the test when it lists none. Null
    /// outside the editor, where the asset cannot be loaded by path.
    /// </summary>
    private static GameScenes CopyScenesStartingAt(string worldScenePath, string entry)
    {
#if UNITY_EDITOR
        GameScenes source = AssetDatabase.LoadAssetAtPath<GameScenes>(GameScenesPath);
        Assert.That(source != null, Is.True, $"There is a {nameof(GameScenes)} asset at {GameScenesPath}.");
        WorldDefinition world = source.FindWorldOwning(worldScenePath);
        Assert.That(world != null, Is.True, $"{nameof(GameScenes)} lists the world of {worldScenePath}.");
        return GameScenes.CopyStartingAt(source, world, entry);
#else
        return null;
#endif
    }

    /// <summary>
    /// Destroys a copy of <see cref="GameScenes"/> made for a boot, unless it is gone already.
    /// </summary>
    private static void DestroyCopy(GameScenes copy)
    {
        if (copy != null)
        {
            UnityEngine.Object.DestroyImmediate(copy);
        }
    }
}
