using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Editor menu for checking runtime terrain edits in play mode (Docs/Multiplayer.md, Checking runtime terrain edits):
/// toggles the cell at the centre of the view (the Scene view's pivot, or the main camera's position when no Scene view
/// is open) on the selected tilemap through its area's <see cref="AreaTerrain"/>, as gameplay would, so on a host the
/// edit is recorded and reaches clients and late joiners, while a client is refused.
/// </summary>
public static class AreaTerrainDevMenu
{
    private const string MenuPath = ProjectScenes.MenuRoot + "Dev/Toggle Terrain Cell At View Centre";

    /// <summary>
    /// Fills <paramref name="cell"/> of <paramref name="layer"/> when it is empty and empties it when it is filled,
    /// through <paramref name="terrain"/>. Returns whether the terrain made the edit (it needs authority and the layer
    /// to be one of its own).
    /// </summary>
    public static bool Toggle(AreaTerrain terrain, Tilemap layer, Vector3Int cell)
    {
        return terrain.TrySetCells(layer, new[] { cell }, !layer.HasTile(cell));
    }

    /// <summary>
    /// Toggles the cell at the centre of the view on the selected tilemap through the area terrain of its scene, and
    /// logs the outcome.
    /// </summary>
    [MenuItem(MenuPath)]
    private static void ToggleAtViewCentre()
    {
        Tilemap layer = Selection.activeGameObject.GetComponent<Tilemap>();
        AreaTerrain terrain = SceneQuery.FindFirst<AreaTerrain>(layer.gameObject.scene);
        if (terrain == null)
        {
            Debug.LogWarning($"{layer.gameObject.scene.name} has no {nameof(AreaTerrain)} to edit {layer.name} through.", layer);
            return;
        }

        Vector3Int cell = layer.WorldToCell(ViewCentre());
        bool filled = !layer.HasTile(cell);
        Debug.Log(Toggle(terrain, layer, cell)
            ? $"{(filled ? "Filled" : "Emptied")} {layer.name} {cell} through {terrain.name}."
            : $"{terrain.name} refused to edit {layer.name} {cell}: only the host (or an offline game) edits, and only its own layers.", layer);
    }

    /// <summary>
    /// The menu works in play mode on a selected tilemap, with a Scene view open or a main camera.
    /// </summary>
    [MenuItem(MenuPath, true)]
    private static bool CanToggleAtViewCentre()
    {
        return EditorApplication.isPlaying && (SceneView.lastActiveSceneView != null || Camera.main != null)
            && Selection.activeGameObject != null && Selection.activeGameObject.TryGetComponent(out Tilemap _);
    }

    /// <summary>
    /// The last active Scene view's pivot, or the main camera's position, the centre of the Game view.
    /// </summary>
    private static Vector3 ViewCentre()
    {
        SceneView view = SceneView.lastActiveSceneView;
        return view != null ? view.pivot : Camera.main.transform.position;
    }
}
