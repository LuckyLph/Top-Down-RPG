using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CanEditMultipleObjects]
[CustomEditor(typeof(DualGridDataTile), true)]
public class DualGridDataTileEditor : UnityEditor.Editor
{
    private DualGridDataTile targetDualGridRuleTile;

    private bool hasMultipleTargets = false;
    private List<DualGridDataTile> targetDualGridRuleTiles = new();

    public void OnEnable()
    {
        targetDualGridRuleTile = (DualGridDataTile)target;
        hasMultipleTargets = targets.Length > 1;

        if (hasMultipleTargets) targetDualGridRuleTiles = targets.Cast<DualGridDataTile>().ToList();
        else targetDualGridRuleTiles = new List<DualGridDataTile>() { target as DualGridDataTile };
    }

    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("This Dual Grid Data Tile contains important metadata for the correct usage of Dual Grid Tilemaps. Do not update these values.", MessageType.Warning);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUI.showMixedValue = targets.Length > 1 && targetDualGridRuleTiles.HasDifferentValues(dualGridRuleTile => dualGridRuleTile.colliderType);
            EditorGUILayout.EnumPopup(new GUIContent("Collider"), targetDualGridRuleTiles.First().colliderType);

            EditorGUI.showMixedValue = targets.Length > 1 && targetDualGridRuleTiles.HasDifferentValues(dualGridRuleTile => dualGridRuleTile.gameObject);
            EditorGUILayout.ObjectField(new GUIContent("GameObject"), targetDualGridRuleTiles.First().gameObject, typeof(GameObject), false);
        }
    }
}
