using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Ensures that all <see cref="DualGridTilemapModule"/>s are updated when outside of PlayMode.
/// </summary>
[InitializeOnLoad]
public static class DualGridTilemapPersistentListener
{
    /// <summary>
    /// Subscribes to tilemap changes for the whole editor session, on every domain load.
    /// </summary>
    static DualGridTilemapPersistentListener()
    {
        Tilemap.tilemapPositionsChanged += HandleTilemapPositionsChanged;
    }

    /// <summary>
    /// Forwards a tilemap change to every module whose data tilemap is <paramref name="tilemap"/> so it rebuilds its
    /// render tiles. Ignored in Play Mode, where the modules listen themselves.
    /// </summary>
    private static void HandleTilemapPositionsChanged(Tilemap tilemap, NativeArray<Vector3Int> positions)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
            return;

        var dualGridModules = Object.FindObjectsByType<DualGridTilemapModule>();
        foreach (var module in dualGridModules)
        {
            if (module.DataTilemap == tilemap)
                module.HandleTilemapPositionsChanged(tilemap, positions);
        }
    }
}
