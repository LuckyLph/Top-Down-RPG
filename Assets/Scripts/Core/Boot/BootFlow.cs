using System.Threading;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Entry point of the main scope that picks the first screen once the app has booted: the main menu, or in the
/// editor the scenes that were open when Play was pressed (see <c>EditorBootRequest</c>).
/// </summary>
public sealed class BootFlow : IAsyncStartable
{
    private readonly GameFlow gameFlow;
    private readonly GameScenes gameScenes;
    private readonly SessionSave sessionSave;

    /// <summary>
    /// Creates the boot flow that drives <paramref name="gameFlow"/>, resolves editor scene paths against
    /// <paramref name="gameScenes"/> and starts a world booted into as a new game in <paramref name="sessionSave"/>.
    /// </summary>
    public BootFlow(GameFlow gameFlow, GameScenes gameScenes, SessionSave sessionSave)
    {
        this.gameFlow = gameFlow;
        this.gameScenes = gameScenes;
        this.sessionSave = sessionSave;
    }

    /// <summary>
    /// Consumes any pending editor boot request and boots into those scenes; falls back to showing the main menu
    /// when there is none or it names nothing this flow can load.
    /// </summary>
    public async Awaitable StartAsync(CancellationToken cancellation)
    {
#if UNITY_EDITOR
        if (EditorBootRequest.TryConsume(out string[] scenePaths) && await TryBootFromEditorScenesAsync(scenePaths, cancellation))
        {
            return;
        }
#endif
        await gameFlow.ShowMainMenuAsync(cancellation);
    }

#if UNITY_EDITOR
    /// <summary>
    /// Boots into the scenes the editor had open: a world's root or chunk scene starts a new game in that world at its
    /// default entry (whose first save replaces the save file, as New Game's does); the gameplay scene without a world
    /// starts a new game in the starting world at its entry; the menu scene shows the main menu. Any other scene is no
    /// world: it boots to the main menu with a warning. Returns false when <paramref name="scenePaths"/> gives nothing
    /// to boot into.
    /// </summary>
    private async Awaitable<bool> TryBootFromEditorScenesAsync(string[] scenePaths, CancellationToken cancellation)
    {
        WorldDefinition world = null;
        string entryName = null;
        bool wantsGameplay = false;
        bool wantsMenu = false;

        foreach (string scenePath in scenePaths)
        {
            if (gameScenes.MainMenu != null && gameScenes.MainMenu.Matches(scenePath))
            {
                wantsMenu = true;
            }
            else if (gameScenes.Gameplay != null && gameScenes.Gameplay.Matches(scenePath))
            {
                wantsGameplay = true;
            }
            else if (FindWorldOwning(scenePath) is WorldDefinition owner)
            {
                if (world == null)
                {
                    world = owner;
                }
            }
            else
            {
                Debug.LogWarning($"{scenePath} is not a world's scene; booting to the main menu.");
                wantsMenu = true;
            }
        }

        if (world == null && wantsGameplay)
        {
            world = gameScenes.StartingWorld;
            entryName = gameScenes.StartingWorldEntry;
            if (world == null)
            {
                Debug.LogError($"{gameScenes.name} has no starting world to boot the gameplay scene into.", gameScenes);
            }
        }

        if (world != null)
        {
            sessionSave.StartNew();
            await gameFlow.StartWorldGameAsync(world, entryName, cancellation);
            return true;
        }

        if (wantsMenu)
        {
            await gameFlow.ShowMainMenuAsync(cancellation);
            return true;
        }

        return false;
    }

    /// <summary>
    /// The world whose root or chunk scene is at <paramref name="scenePath"/>: a world <see cref="GameScenes"/> lists,
    /// or else any world asset in the project, so a world not listed yet can still be played from the editor; null
    /// when no world owns the scene.
    /// </summary>
    private WorldDefinition FindWorldOwning(string scenePath)
    {
        WorldDefinition listed = gameScenes.FindWorldOwning(scenePath);
        if (listed != null)
        {
            return listed;
        }

        foreach (string guid in UnityEditor.AssetDatabase.FindAssets($"t:{nameof(WorldDefinition)}"))
        {
            WorldDefinition world = UnityEditor.AssetDatabase.LoadAssetAtPath<WorldDefinition>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (world != null && world.OwnsScene(scenePath))
            {
                return world;
            }
        }

        return null;
    }
#endif
}
