using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps the meshes of URP's Shadow Caster 2Ds out of saved scenes (Docs/Lighting.md, Phase 7). In the editor every
/// caster makes an empty shadow mesh of its own as it wakes, even a disabled one, and saving a scene wrote each as an
/// object of its own, unreferenced, beside the casters (a prop's, every blocking prop's, a scene's walls'): a chunk of the
/// Overworld would gain hundreds. A caster builds its mesh again when it first casts, so before a scene is saved its
/// casters' meshes are marked not to be saved.
/// </summary>
[InitializeOnLoad]
public static class ShadowCasterSceneSaving
{
    static ShadowCasterSceneSaving()
    {
        EditorSceneManager.sceneSaving += (scene, _) => KeepMeshesOut(scene);
    }

    /// <summary>
    /// Marks the shadow meshes of <paramref name="scene"/>'s casters that are no asset not to be saved, returning how
    /// many it marked.
    /// </summary>
    public static int KeepMeshesOut(Scene scene)
    {
        int marked = 0;
        foreach (ShadowCaster2D caster in SceneQuery.FindAll<ShadowCaster2D>(scene))
        {
            Mesh mesh = caster.mesh;
            if (mesh != null && !EditorUtility.IsPersistent(mesh) && (mesh.hideFlags & HideFlags.DontSaveInEditor) == 0)
            {
                mesh.hideFlags |= HideFlags.DontSaveInEditor;
                marked++;
            }
        }

        return marked;
    }
}
