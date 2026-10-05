using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// A reference to one scene that survives moves and renames: it stores the scene asset's GUID and the path resolved
/// from it in the editor, since runtime code loads scenes by path.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Scenes/Scene Definition", fileName = "SceneDefinition")]
public class SceneDefinition : ScriptableObject
{
    [SerializeField] private string sceneGuid;
    [SerializeField] private string scenePath;

    public string SceneGuid => sceneGuid;
    public string ScenePath => scenePath;
    public string SceneName => string.IsNullOrEmpty(scenePath) ? string.Empty : Path.GetFileNameWithoutExtension(scenePath);
    public bool IsValid => !string.IsNullOrEmpty(scenePath);

    /// <summary>
    /// Whether <paramref name="scene"/> is valid and is this definition's scene, compared by path.
    /// </summary>
    public bool Matches(Scene scene)
    {
        return scene.IsValid() && scene.path == scenePath;
    }

    /// <summary>
    /// Whether <paramref name="path"/> is this definition's scene path exactly; false for an empty path.
    /// </summary>
    public bool Matches(string path)
    {
        return !string.IsNullOrEmpty(path) && path == scenePath;
    }

    /// <summary>
    /// Creates an unsaved in-memory definition for the scene at <paramref name="path"/>, with no GUID, for scenes
    /// that are not in <see cref="GameScenes"/> (such as an area opened in the editor before pressing Play).
    /// </summary>
    public static SceneDefinition CreateTransient(string path)
    {
        SceneDefinition definition = CreateInstance<SceneDefinition>();
        definition.scenePath = path;
        definition.name = definition.SceneName;
        definition.hideFlags = HideFlags.DontSave;
        return definition;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editor only: points this definition at <paramref name="sceneAsset"/> (or clears it for null), storing its
    /// GUID and path and marking the asset dirty.
    /// </summary>
    public void SetScene(SceneAsset sceneAsset)
    {
        string path = sceneAsset != null ? AssetDatabase.GetAssetPath(sceneAsset) : string.Empty;
        sceneGuid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
        scenePath = path;
        EditorUtility.SetDirty(this);
    }

    /// <summary>
    /// Re-resolves the path from the GUID; returns true when the stored path changed.
    /// </summary>
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

    /// <summary>
    /// Keeps the stored path in step with the scene asset after it was moved or renamed.
    /// </summary>
    private void OnValidate()
    {
        RefreshPath();
    }
#endif
}
