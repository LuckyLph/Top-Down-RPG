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

    /// <summary>
    /// Creates the boot flow that drives <paramref name="gameFlow"/> and resolves editor scene paths against
    /// <paramref name="gameScenes"/>.
    /// </summary>
    public BootFlow(GameFlow gameFlow, GameScenes gameScenes)
    {
        this.gameFlow = gameFlow;
        this.gameScenes = gameScenes;
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
    /// Boots into the scenes the editor had open: the first one that is neither the menu nor the gameplay scene is
    /// treated as an area (a transient definition when <see cref="GameScenes"/> does not list it) and a new game
    /// starts there; the gameplay scene without an area starts a new game in the starting area; the menu scene on
    /// its own shows the main menu. Returns false when <paramref name="scenePaths"/> gives nothing to boot into.
    /// </summary>
    private async Awaitable<bool> TryBootFromEditorScenesAsync(string[] scenePaths, CancellationToken cancellation)
    {
        SceneDefinition area = null;
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
            else if (area == null)
            {
                area = gameScenes.FindArea(scenePath) ?? SceneDefinition.CreateTransient(scenePath);
            }
        }

        if (area != null || wantsGameplay)
        {
            await gameFlow.StartNewGameAsync(area, null, cancellation);
            return true;
        }

        if (wantsMenu)
        {
            await gameFlow.ShowMainMenuAsync(cancellation);
            return true;
        }

        return false;
    }
#endif
}
