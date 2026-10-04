using UnityEditor;
using UnityEngine;

public static class DualGridTilemapMenu
{
    [MenuItem("GameObject/2D Object/Tilemap/Dual Grid Tilemap ", false, 0)]
    private static void CreateDualGridTilemapMenu()
    {
        GameObject selected = Selection.activeGameObject;
        Grid selectedGrid = selected == null ? null : selected.GetComponent<Grid>();

        var newDualGridTilemapModule = DualGridTilemapModuleEditor.CreateNewDualGridTilemap(selectedGrid);

        Selection.activeGameObject = newDualGridTilemapModule.gameObject;
    }
}
