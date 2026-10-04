using UnityEditor;
using UnityEngine;

[CanEditMultipleObjects]
[CustomEditor(typeof(Transform))]
public class RestrictedTransformEditor : UnityEditor.Editor
{
    private UnityEditor.Editor defaultEditor;

    private void OnEnable()
    {
        defaultEditor = CreateEditor(targets, typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.TransformInspector"));
    }

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

    private void OnDisable()
    {
        if (defaultEditor != null)
        {
            DestroyImmediate(defaultEditor);
        }
    }
}
