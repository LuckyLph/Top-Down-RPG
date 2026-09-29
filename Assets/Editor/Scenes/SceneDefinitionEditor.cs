using System.Linq;
using UnityEditor;

[CustomEditor(typeof(SceneDefinition))]
public class SceneDefinitionEditor : Editor
{
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

// Keeps SceneDefinition paths in sync when scenes are moved or renamed.
public class SceneDefinitionPathSync : AssetPostprocessor
{
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
