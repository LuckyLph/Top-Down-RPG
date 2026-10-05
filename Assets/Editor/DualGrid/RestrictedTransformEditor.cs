using UnityEditor;
using UnityEngine;

/// <summary>
/// Replacement inspector for every <see cref="Transform"/> that wraps Unity's built-in transform inspector and
/// disables it on dual-grid render tilemaps, whose transform the <see cref="DualGridTilemapModule"/> manages.
/// </summary>
[CanEditMultipleObjects]
[CustomEditor(typeof(Transform))]
public class RestrictedTransformEditor : UnityEditor.Editor
{
    private UnityEditor.Editor defaultEditor;

    /// <summary>
    /// Creates Unity's internal TransformInspector for the same targets so it can be drawn inside this editor.
    /// </summary>
    private void OnEnable()
    {
        defaultEditor = CreateEditor(targets, typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.TransformInspector"));
    }

    /// <summary>
    /// Draws the built-in transform inspector, greyed out with an explanation when the target is a render tilemap.
    /// </summary>
    public override void OnInspectorGUI()
    {
        Transform transform = (Transform)target;

        if (transform.GetComponentInImmediateParent<DualGridTilemapModule>() != null)
        {
            EditorGUILayout.HelpBox($"Editing is disabled on a RenderTilemap. The transform is managed by the {nameof(DualGridTilemapModule)}.", MessageType.Info);
            GUI.enabled = false;
            defaultEditor.OnInspectorGUI();
            GUI.enabled = true;
        }
        else
        {
            defaultEditor.OnInspectorGUI();
        }
    }

    /// <summary>
    /// Destroys the wrapped built-in inspector.
    /// </summary>
    private void OnDisable()
    {
        if (defaultEditor != null)
        {
            DestroyImmediate(defaultEditor);
        }
    }
}
