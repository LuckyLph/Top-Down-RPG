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
    static DualGridTilemapPersistentListener()
    {
        Tilemap.tilemapPositionsChanged += HandleTilemapPositionsChanged;
    }

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
