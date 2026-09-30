using System.Threading;
using UnityEngine;
using VContainer.Unity;

public sealed class BootFlow : IAsyncStartable
{
    private readonly GameFlow gameFlow;
    private readonly GameScenes gameScenes;

    public BootFlow(GameFlow gameFlow, GameScenes gameScenes)
    {
        this.gameFlow = gameFlow;
        this.gameScenes = gameScenes;
    }

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
