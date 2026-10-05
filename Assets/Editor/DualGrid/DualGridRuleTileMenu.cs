using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Assets/Create menu entry for new <see cref="DualGridRuleTile"/> assets, set up from the selected texture when there
/// is one.
/// </summary>
public static class DualGridRuleTileMenu
{
    /// <summary>
    /// Creates a rule tile asset in the selected folder or next to the selected asset, applying the selected texture
    /// first (aborting if it is rejected), then adds its data tile as a sub-asset and selects the new tile.
    /// </summary>
    [MenuItem("Assets/Create/2D/Tiles/Dual Grid Rule Tile", false, 50)]
    private static void CreateDualGridRuleTile()
    {
        bool isSelectedObjectTexture2d = TryGetSelectedTexture2D(out Texture2D selectedTexture);

        DualGridRuleTile newRuleTile = ScriptableObject.CreateInstance<DualGridRuleTile>();

        if (isSelectedObjectTexture2d)
        {
            bool wasTextureApplied = newRuleTile.TryApplyTexture2D(selectedTexture);
            if (!wasTextureApplied) return;
        }

        string activeAssetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
        string assetName = isSelectedObjectTexture2d ? selectedTexture.name + "_DualGridRuleTile.asset" : "DualGridRuleTile.asset";
        string assetPath = Path.Combine(AssetDatabase.IsValidFolder(activeAssetPath) ? activeAssetPath : Path.GetDirectoryName(activeAssetPath), assetName);

        AssetDatabase.CreateAsset(newRuleTile, AssetDatabase.GenerateUniqueAssetPath(assetPath));
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var newDataTile = newRuleTile.RefreshDataTile();
        newDataTile.name = isSelectedObjectTexture2d ? selectedTexture.name + "_DualGridDataTile" : "DualGridDataTile";
        AssetDatabase.AddObjectToAsset(newDataTile, newRuleTile);
        AssetDatabase.SaveAssets();

        Selection.activeObject = newRuleTile;
    }

    /// <summary>
    /// Whether the active selection is a <see cref="Texture2D"/>; <paramref name="selectedTexture2d"/> is null if not.
    /// </summary>
    private static bool TryGetSelectedTexture2D(out Texture2D selectedTexture2d)
    {
        if (Selection.activeObject is Texture2D texture2d)
        {
            selectedTexture2d = texture2d;
            return true;
        }
        else
        {
            selectedTexture2d = null;
            return false;
        }
    }
}
