using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector of <see cref="NavigationTerrainSource2D"/>: its settings, with the priority read-only when a
/// <see cref="TerrainBlendRenderer"/> draws the source's tilemap as a ground layer
/// (<see cref="TerrainBlendRenderer.FindLayerDrawing"/>), since that layer's place in the renderer's layers, its draw
/// order, sets it (see <see cref="TerrainBlendRendererEditor"/>).
/// </summary>
[CustomEditor(typeof(NavigationTerrainSource2D))]
[CanEditMultipleObjects]
public sealed class NavigationTerrainSource2DEditor : Editor
{
    private const string PriorityProperty = "priority";
    private const string ScriptProperty = "m_Script";

    /// <summary>
    /// Draws the settings, the priority read-only with a note when a terrain renderer's layer order sets it.
    /// </summary>
    public override void OnInspectorGUI()
    {
        TerrainBlendRenderer drawnBy = null;
        int layer = -1;
        foreach (Object shown in targets)
        {
            TerrainBlendRenderer renderer = TerrainBlendRenderer.FindLayerDrawing(((NavigationTerrainSource2D)shown).DataTilemap, out int index);
            if (renderer != null)
            {
                drawnBy = renderer;
                layer = index;
            }
        }

        serializedObject.Update();
        SerializedProperty property = serializedObject.GetIterator();
        for (bool enterChildren = true; property.NextVisible(enterChildren); enterChildren = false)
        {
            using (new EditorGUI.DisabledScope(property.propertyPath == ScriptProperty || (drawnBy != null && property.propertyPath == PriorityProperty)))
            {
                EditorGUILayout.PropertyField(property, true);
            }
        }

        serializedObject.ApplyModifiedProperties();
        if (drawnBy != null)
        {
            EditorGUILayout.HelpBox($"The priority is this tilemap's place in the layers of {drawnBy.name}'s Terrain Blend Renderer, its draw order (layer {layer}). Reorder the layers there to change it.", MessageType.Info);
        }
    }
}
