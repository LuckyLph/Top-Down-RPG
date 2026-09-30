using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(menuName = "TopDownRPG/Scenes/Scene Definition", fileName = "SceneDefinition")]
public class SceneDefinition : ScriptableObject
{
    [SerializeField] private string sceneGuid;
    [SerializeField] private string scenePath;

    public string SceneGuid => sceneGuid;
    public string ScenePath => scenePath;
    public string SceneName => string.IsNullOrEmpty(scenePath) ? string.Empty : Path.GetFileNameWithoutExtension(scenePath);
    public bool IsValid => !string.IsNullOrEmpty(scenePath);

    public bool Matches(Scene scene)
    {
        return scene.IsValid() && scene.path == scenePath;
    }

    public bool Matches(string path)
    {
        return !string.IsNullOrEmpty(path) && path == scenePath;
    }

    public static SceneDefinition CreateTransient(string path)
    {
        SceneDefinition definition = CreateInstance<SceneDefinition>();
        definition.scenePath = path;
        definition.name = definition.SceneName;
        definition.hideFlags = HideFlags.DontSave;
        return definition;
    }

#if UNITY_EDITOR
    public void SetScene(SceneAsset sceneAsset)
    {
        string path = sceneAsset != null ? AssetDatabase.GetAssetPath(sceneAsset) : string.Empty;
        sceneGuid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
        scenePath = path;
        EditorUtility.SetDirty(this);
    }

    // Re-resolves the path from the GUID; returns true when the stored path changed.
    public bool RefreshPath()
    {
        if (string.IsNullOrEmpty(sceneGuid))
        {
            return false;
        }

        string resolvedPath = AssetDatabase.GUIDToAssetPath(sceneGuid);
        if (string.IsNullOrEmpty(resolvedPath) || resolvedPath == scenePath)
        {
            return false;
        }

        scenePath = resolvedPath;
        EditorUtility.SetDirty(this);
        return true;
    }

    private void OnValidate()
    {
        RefreshPath();
    }
#endif
}
