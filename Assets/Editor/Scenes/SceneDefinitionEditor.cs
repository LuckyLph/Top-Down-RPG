using System.Linq;
using UnityEditor;

/// <summary>
/// Inspector for <see cref="SceneDefinition"/> that picks the scene as an asset rather than a path.
/// </summary>
[CustomEditor(typeof(SceneDefinition))]
public class SceneDefinitionEditor : Editor
{
    /// <summary>
    /// Draws an undoable scene asset picker, the resolved path read-only, and a warning when the scene is not enabled
    /// in the build list.
    /// </summary>
    public override void OnInspectorGUI()
    {
        SceneDefinition definition = (SceneDefinition)target;
        SceneAsset current = definition.IsValid
            ? AssetDatabase.LoadAssetAtPath<SceneAsset>(definition.ScenePath)
            : null;

        EditorGUI.BeginChangeCheck();
        SceneAsset picked = (SceneAsset)EditorGUILayout.ObjectField("Scene", current, typeof(SceneAsset), false);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(definition, "Change Scene");
            definition.SetScene(picked);
        }

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.TextField("Path", definition.ScenePath);
        }

        if (definition.IsValid && !EditorBuildSettings.scenes.Any(scene => scene.enabled && scene.path == definition.ScenePath))
        {
            EditorGUILayout.HelpBox("This scene is not enabled in the build scene list.", MessageType.Warning);
        }
    }
}

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
