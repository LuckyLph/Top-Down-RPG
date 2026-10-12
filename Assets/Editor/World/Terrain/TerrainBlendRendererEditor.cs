using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector of <see cref="TerrainBlendRenderer"/>: its settings, with the navigation priorities kept in step with the
/// draw order. A ground layer's navigation priority is its index in the layers list (bottom first), so whenever the
/// inspector changes the renderer (reordering, adding or removing layers), each layer's navigation source
/// (<see cref="TerrainBlendRenderer.NavigationSourceOf"/>) gets its layer's priority, in the same undo step. When they
/// disagree anyway (a script or an edited scene file set them), a message and a button set them from the draw order.
/// </summary>
[CustomEditor(typeof(TerrainBlendRenderer))]
[CanEditMultipleObjects]
public sealed class TerrainBlendRendererEditor : Editor
{
    /// <summary>
    /// Draws the settings, then keeps the navigation priorities in step with the draw order, or offers to.
    /// </summary>
    public override void OnInspectorGUI()
    {
        int group = Undo.GetCurrentGroup();
        if (DrawDefaultInspector())
        {
            foreach (Object edited in targets)
            {
                SyncNavigationPriorities((TerrainBlendRenderer)edited);
            }

            Undo.CollapseUndoOperations(group);
        }

        foreach (Object shown in targets)
        {
            TerrainBlendRenderer renderer = (TerrainBlendRenderer)shown;
            if (renderer.HasNavigationPrioritiesInStep())
            {
                continue;
            }

            EditorGUILayout.HelpBox($"{renderer.name}: the layers' navigation priorities are not their draw order, so navigation and the drawing disagree on which layer is on top.", MessageType.Error);
            if (GUILayout.Button("Set Navigation Priorities From Draw Order"))
            {
                SyncNavigationPriorities(renderer);
            }
        }
    }

    /// <summary>
    /// Sets the priority of each layer's navigation source to its layer's through a serialized object, with undo, and
    /// returns how many changed.
    /// </summary>
    public static int SyncNavigationPriorities(TerrainBlendRenderer renderer)
    {
        int changed = 0;
        for (int i = 0; i < renderer.Layers.Count; i++)
        {
            NavigationTerrainSource2D source = renderer.NavigationSourceOf(i);
            if (source == null)
            {
                continue;
            }

            SerializedObject serialized = new(source);
            SerializedProperty priority = serialized.FindProperty("priority");
            int wanted = TerrainBlendRenderer.NavigationPriorityOf(i);
            if (priority.intValue != wanted)
            {
                priority.intValue = wanted;
                serialized.ApplyModifiedProperties();
                changed++;
            }
        }

        return changed;
    }
}
