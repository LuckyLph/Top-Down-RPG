using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Outside Play Mode, hands the terrain renderer of every world chunk opened in the editor (by the World Editor, the
/// World Generator's writes and whole-world steps, the tests, or by hand) one set of texture arrays per style, shared
/// with the other chunks open (<see cref="SharedTerrainArrays"/>), as a chunk's scope hands it the Gameplay scope's in
/// play (Docs/KnownGaps.md, Phase 5): a renderer building its own takes about 38 MB of the editor's memory for a chunk of
/// the Overworld, the shared set 113 MB for every chunk. A renderer builds its own as its scene opens and draws with the
/// shared arrays from the end of that open, so at most one chunk's own arrays are alive at a time. Renderers given arrays
/// of their own by a caller (tests, scratch scenes) and chunks built in memory are left as they are. The arrays go with
/// the last renderer drawing with them, and every one before an assembly reload; after one, and back from Play Mode,
/// the open chunks are handed the new set.
/// </summary>
[InitializeOnLoad]
public static class EditModeTerrainArrays
{
    private static SharedTerrainArrays shared = new();

    /// <summary>
    /// Follows scenes opening, Play Mode and assembly reloads, and hands the chunks already open the shared arrays.
    /// </summary>
    static EditModeTerrainArrays()
    {
        EditorSceneManager.sceneOpened += HandleSceneOpened;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        AssemblyReloadEvents.beforeAssemblyReload += Dispose;
        EditorApplication.delayCall += ShareOpenChunks;
    }

    /// <summary>
    /// The arrays the open chunks share.
    /// </summary>
    internal static SharedTerrainArrays Arrays => shared;

    /// <summary>
    /// Hands the renderers of the world chunks in <paramref name="scene"/> the shared arrays, outside Play Mode; a
    /// renderer drawing with shared arrays already keeps them.
    /// </summary>
    internal static void Share(Scene scene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !scene.IsValid() || !scene.isLoaded)
        {
            return;
        }

        foreach (WorldChunk chunk in SceneQuery.FindAll<WorldChunk>(scene))
        {
            if (chunk.World != null && chunk.TryGetComponent(out TerrainBlendRenderer renderer) && !renderer.SharesArrays)
            {
                renderer.Construct(shared);
            }
        }
    }

    /// <summary>
    /// Hands the shared arrays to a world chunk's scene as it opens.
    /// </summary>
    private static void HandleSceneOpened(Scene scene, OpenSceneMode mode)
    {
        Share(scene);
    }

    /// <summary>
    /// Hands the shared arrays to the chunks of every open scene.
    /// </summary>
    private static void ShareOpenChunks()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Share(SceneManager.GetSceneAt(i));
        }
    }

    /// <summary>
    /// Back in Edit Mode, hands the shared arrays to the chunks open again.
    /// </summary>
    private static void HandlePlayModeStateChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.delayCall += ShareOpenChunks;
        }
    }

    /// <summary>
    /// Destroys every shared array before an assembly reload, which builds the renderers again; a fresh set follows it.
    /// </summary>
    private static void Dispose()
    {
        shared.Dispose();
        shared = new SharedTerrainArrays();
    }
}
