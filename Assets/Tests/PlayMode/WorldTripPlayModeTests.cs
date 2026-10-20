using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// PlayMode tests for trips between worlds offline: in the Sandbox the player walks into the exit on the road and
/// arrives in the SandboxInterior at its entry, its input off during the fade and game time running all along, the
/// lighting following it and the Sandbox kept loaded; walking back out within the empty-world grace reuses the
/// Sandbox's scenes, and the menu then unloads both worlds. Left alone in the interior past the grace, the Sandbox
/// unloads.
/// </summary>
public class WorldTripPlayModeTests
{
    private const string SandboxRootPath = "Assets/Worlds/Sandbox/World_Sandbox.unity";
    private const string WorldsFolder = "Assets/Worlds/";
    private const float TripTimeoutSeconds = 20f;

    /// <summary>
    /// The player walks east along the road into the Sandbox's exit and arrives at the SandboxInterior's entry: input
    /// is off and game time runs during the trip, the Sandbox stays loaded with its lighting off and the interior's on;
    /// walking into the interior's exit brings it back to the Sandbox's entry in front of the first exit, reusing the
    /// very scenes it left; the menu then unloads every world.
    /// </summary>
    [UnityTest]
    public IEnumerator WalkingIntoAnExit_TravelsToTheInterior_AndBackWithinTheGrace_ReusingTheSandbox()
    {
        yield return SceneBootTestHelper.BootIntoWorld(SandboxRootPath);

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        Worlds worlds = SceneBootTestHelper.ResolveFromGameplay<Worlds>();
        PlayerWorlds playerWorlds = SceneBootTestHelper.ResolveFromGameplay<PlayerWorlds>();
        WorldDefinition sandbox = gameFlow.CurrentWorld;
        WorldDefinition interior = FindWorld(gameFlow, "SandboxInterior");
        LocalPlayer local = SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current;
        Assert.That(worlds.TryGet(sandbox, out LoadedWorld sandboxLoaded), Is.True);
        Dictionary<Vector2Int, Scene> sandboxHandles = HandlesOf(sandboxLoaded);
        Assert.That(SceneBootTestHelper.ResolveFromGameplay<WorldExits>().Count, Is.GreaterThan(0), "The loaded chunk's exit is known.");

        Assert.That(local.Controller.GetComponent<PlayerMotor2D>().MoveTo(sandbox.ToWorld(new Vector2(38.5f, 42f))), Is.True);
        yield return TravelAndCheck(gameFlow, "the trip into the interior");

        Assert.That(playerWorlds.Get(local.Handle), Is.SameAs(interior));
        Assert.That(gameFlow.CurrentWorld, Is.SameAs(interior), "The game flow's world is the local player's.");
        Assert.That((Vector2)local.Transform.position, Is.EqualTo(interior.ToWorld(interior.FindEntry("start").Position)));
        Assert.That(worlds.TryGet(interior, out LoadedWorld interiorLoaded), Is.True);
        Assert.That(interiorLoaded.Chunks.ContainsKey(Vector2Int.zero), Is.True);
        Assert.That(worlds.TryGet(sandbox, out LoadedWorld kept), Is.True, "The Sandbox stays loaded for the grace.");
        Assert.That(kept, Is.SameAs(sandboxLoaded));
        Assert.That(HandlesOf(kept), Is.EqualTo(sandboxHandles), "The Sandbox's chunks stay loaded, untouched.");
        Assert.That(interiorLoaded.Lighting.gameObject.activeSelf, Is.True, "The lighting follows the player.");
        Assert.That(sandboxLoaded.Lighting.gameObject.activeSelf, Is.False);
        Assert.That(SceneQuery.FindFirst<WorldChunk>(interiorLoaded.Chunks[Vector2Int.zero]).IsDrawn, Is.True);
        Assert.That(SceneBootTestHelper.ResolveFromMain<PlayerInputService>().GameplayEnabled, Is.True, "Input comes back after the trip.");

        Assert.That(local.Controller.GetComponent<PlayerMotor2D>().MoveTo(interior.ToWorld(new Vector2(32f, 24.5f))), Is.True);
        yield return TravelAndCheck(gameFlow, "the trip back to the Sandbox");

        Assert.That(playerWorlds.Get(local.Handle), Is.SameAs(sandbox));
        Assert.That(gameFlow.CurrentWorld, Is.SameAs(sandbox));
        Assert.That((Vector2)local.Transform.position, Is.EqualTo(sandbox.ToWorld(sandbox.FindEntry("fromInterior").Position)));
        Assert.That(worlds.TryGet(sandbox, out kept), Is.True);
        Assert.That(kept, Is.SameAs(sandboxLoaded), "A trip back within the grace reuses the loaded world.");
        Assert.That(HandlesOf(kept), Is.EqualTo(sandboxHandles), "Nothing of the Sandbox was reloaded.");
        Assert.That(sandboxLoaded.Lighting.gameObject.activeSelf, Is.True);
        Assert.That(interiorLoaded.Lighting.gameObject.activeSelf, Is.False);
        Assert.That(worlds.TryGet(interior, out _), Is.True, "The interior now waits out its own grace.");

        for (int frame = 0; frame < 10; frame++)
        {
            yield return null;
        }

        Assert.That(gameFlow.IsTransitioning, Is.False, "Arriving next to the exit fires nothing.");

        gameFlow.ShowMainMenuAsync();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        Assert.That(gameFlow.IsInMenu, Is.True);
        Assert.That(LoadedWorldScenes(), Is.Empty, "Leaving the session unloads every world, the one left behind included.");
    }

    /// <summary>
    /// After a trip into the interior, the Sandbox keeps its scenes until the empty-world grace of game time has run
    /// out since the player left it, then unloads its chunks and itself.
    /// </summary>
    [UnityTest]
    public IEnumerator TheWorldLeftBehind_UnloadsOnceTheGraceIsOver()
    {
        yield return SceneBootTestHelper.BootIntoWorld(SandboxRootPath);

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        Worlds worlds = SceneBootTestHelper.ResolveFromGameplay<Worlds>();
        IClock clock = SceneBootTestHelper.ResolveFromMain<IClock>();
        WorldDefinition sandbox = gameFlow.CurrentWorld;
        LocalPlayer local = SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current;
        float grace = 10f;

        local.Controller.GetComponent<PlayerMotor2D>().MoveTo(sandbox.ToWorld(new Vector2(38.5f, 42f)));
        yield return SceneBootTestHelper.WaitUntil(() => gameFlow.IsTravelling, "the player to walk into the exit");
        float leftAt = clock.Time;
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        float deadline = Time.realtimeSinceStartup + 40f;
        while (worlds.TryGet(sandbox, out _))
        {
            Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Timed out waiting for the Sandbox to unload.");
            yield return null;
        }

        Assert.That(clock.Time - leftAt, Is.GreaterThanOrEqualTo(grace - 0.1f), "The Sandbox waited out its grace.");
        Assert.That(LoadedWorldScenes().TrueForAll(name => name.StartsWith("World_SandboxInterior")), Is.True, string.Join(", ", LoadedWorldScenes()));
        Assert.That(gameFlow.CurrentWorld.name, Is.EqualTo("SandboxInterior"));
    }

    /// <summary>
    /// Waits for the player to walk into an exit and for its trip to end, checking on every frame of the trip that
    /// gameplay input is off, game time is not frozen, and the game clock moves on.
    /// </summary>
    private static IEnumerator TravelAndCheck(GameFlow gameFlow, string description)
    {
        PlayerInputService input = SceneBootTestHelper.ResolveFromMain<PlayerInputService>();
        TimeFreeze freeze = SceneBootTestHelper.ResolveFromMain<TimeFreeze>();
        IClock clock = SceneBootTestHelper.ResolveFromMain<IClock>();
        yield return SceneBootTestHelper.WaitUntil(() => gameFlow.IsTravelling, $"the player to walk into the exit for {description}");

        float startedAt = clock.Time;
        float deadline = Time.realtimeSinceStartup + TripTimeoutSeconds;
        int frames = 0;
        while (gameFlow.IsTransitioning)
        {
            Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), $"Timed out waiting for {description} to end.");
            Assert.That(gameFlow.IsTravelling, Is.True);
            Assert.That(input.GameplayEnabled, Is.False, "Gameplay input is off during the trip.");
            Assert.That(freeze.IsFrozen, Is.False, "A trip never freezes game time.");
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            frames++;
            yield return null;
        }

        Assert.That(frames, Is.GreaterThan(1));
        Assert.That(clock.Time, Is.GreaterThan(startedAt), "Game time ran during the trip.");
    }

    private static WorldDefinition FindWorld(GameFlow gameFlow, string worldName)
    {
        foreach (WorldDefinition world in gameFlow.Scenes.Worlds)
        {
            if (world != null && world.name == worldName)
            {
                return world;
            }
        }

        Assert.Fail($"Game Scenes lists no world named {worldName}.");
        return null;
    }

    /// <summary>
    /// Each loaded chunk scene of <paramref name="world"/> by its coordinates, and its root scene at (-1, -1); scenes
    /// compare by their handles, so a scene loaded again differs.
    /// </summary>
    private static Dictionary<Vector2Int, Scene> HandlesOf(LoadedWorld world)
    {
        Dictionary<Vector2Int, Scene> handles = new();
        foreach (KeyValuePair<Vector2Int, Scene> chunk in world.Chunks)
        {
            handles[chunk.Key] = chunk.Value;
        }

        handles[new Vector2Int(-1, -1)] = world.Scene;
        return handles;
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
