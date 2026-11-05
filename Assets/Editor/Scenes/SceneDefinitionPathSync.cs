using System.Linq;
using UnityEditor;

/// <summary>
/// Keeps SceneDefinition paths in sync when scenes are moved or renamed.
/// </summary>
public class SceneDefinitionPathSync : AssetPostprocessor
{
    /// <summary>
    /// After an import batch that moved any scene file, re-resolves every <see cref="SceneDefinition"/> path from its
    /// GUID and saves the ones that changed.
    /// </summary>
    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (!moved.Any(path => path.EndsWith(".unity")))
        {
            return;
        }

        foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(SceneDefinition)}"))
        {
            SceneDefinition definition = AssetDatabase.LoadAssetAtPath<SceneDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (definition != null && definition.RefreshPath())
            {
                AssetDatabase.SaveAssetIfDirty(definition);
            }
        }
    }
}
