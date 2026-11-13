using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One render of a <see cref="TitleViewRecipe"/>'s view (Docs/UiRevamp.md, Phase 11): made, it opens the world's root
/// scene and the chunk scenes under the view and lights them at the recipe's phase through <see cref="NightPreview"/>;
/// <see cref="Render"/>, from a later editor update (chunks built in the update they open draw nothing), draws the view
/// through <see cref="TerrainBlendProbe.Render"/> at one pixel per art px with the terrain's animation stopped; disposed,
/// it puts the night preview and the open scenes back as they were (an empty scene for an untitled one).
/// </summary>
public sealed class TitleViewRender : IDisposable
{
    private readonly TitleViewRecipe recipe;
    private readonly SceneSetup[] setup;
    private readonly bool hadUntitled;
    private readonly bool previewShown;
    private readonly float previewPhase;
    private bool disposed;

    /// <summary>
    /// Opens and lights <paramref name="viewRecipe"/>'s view. Throws when an open scene has unsaved changes, which
    /// opening the world would lose.
    /// </summary>
    public TitleViewRender(TitleViewRecipe viewRecipe)
    {
        recipe = viewRecipe;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene open = SceneManager.GetSceneAt(i);
            if (open.isDirty)
            {
                throw new InvalidOperationException($"The open scene '{open.name}' has unsaved changes; save or discard them before rendering the title view.");
            }

            hadUntitled |= string.IsNullOrEmpty(open.path);
        }

        setup = EditorSceneManager.GetSceneManagerSetup();
        previewShown = NightPreview.instance.Shown;
        previewPhase = NightPreview.instance.Phase;
        ChunkScenes = ChunkScenesUnder(recipe);
        EditorSceneManager.OpenScene(recipe.World.RootScene.ScenePath, OpenSceneMode.Single);
        foreach (string path in ChunkScenes)
        {
            EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        }

        NightPreview.instance.Show(recipe.Phase);
    }

    /// <summary>
    /// The chunk scenes the view lies over.
    /// </summary>
    public IReadOnlyList<string> ChunkScenes { get; }

    /// <summary>
    /// The paths of <paramref name="viewRecipe"/>'s world's chunk scenes under its view.
    /// </summary>
    public static List<string> ChunkScenesUnder(TitleViewRecipe viewRecipe)
    {
        WorldDefinition world = viewRecipe.World;
        Rect view = viewRecipe.ViewRect();
        Vector2Int min = ChunkCoords.ChunkOf(ChunkCoords.CellOf(world.ToLocal(view.min)));
        Vector2Int max = ChunkCoords.ChunkOf(ChunkCoords.CellOf(world.ToLocal(view.max - new Vector2(0.001f, 0.001f))));
        List<string> paths = new();
        for (int y = min.y; y <= max.y; y++)
        {
            for (int x = min.x; x <= max.x; x++)
            {
                if (world.TryGetChunkScene(new Vector2Int(x, y), out SceneDefinition scene) && scene.IsValid)
                {
                    paths.Add(scene.ScenePath);
                }
            }
        }

        return paths;
    }

    /// <summary>
    /// Draws the view, the terrain's animated materials at the recipe's time; the caller destroys the texture.
    /// </summary>
    public Texture2D Render()
    {
        TerrainBlendRenderer[] grounds = UnityEngine.Object.FindObjectsByType<TerrainBlendRenderer>();
        try
        {
            foreach (TerrainBlendRenderer ground in grounds)
            {
                ground.SetFixedTime(recipe.TerrainTime);
            }

            return TerrainBlendProbe.Render(grounds.Length > 0 ? grounds[0] : null, recipe.ViewRect(), 1, false, false);
        }
        finally
        {
            foreach (TerrainBlendRenderer ground in grounds)
            {
                if (ground != null)
                {
                    ground.SetFixedTime(null);
                }
            }
        }
    }

    /// <summary>
    /// Puts the night preview and the open scenes back as they were.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (previewShown)
        {
            NightPreview.instance.Show(previewPhase);
        }
        else
        {
            NightPreview.instance.Hide();
        }

        if (hadUntitled || setup.Length == 0)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
        else
        {
            EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }
}
