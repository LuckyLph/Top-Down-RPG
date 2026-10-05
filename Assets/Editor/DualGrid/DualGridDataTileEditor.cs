using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Read-only inspector for <see cref="DualGridDataTile"/> sub-assets: shows their collider and GameObject with a
/// warning not to change them, since they are generated from the owning rule tile.
/// </summary>
[CanEditMultipleObjects]
[CustomEditor(typeof(DualGridDataTile), true)]
public class DualGridDataTileEditor : UnityEditor.Editor
{
    private DualGridDataTile targetDualGridRuleTile;

    private bool hasMultipleTargets = false;
    private List<DualGridDataTile> targetDualGridRuleTiles = new();

    /// <summary>
    /// Caches the inspected data tiles so the inspector can show mixed values across a multi-selection.
    /// </summary>
    public void OnEnable()
    {
        targetDualGridRuleTile = (DualGridDataTile)target;
        hasMultipleTargets = targets.Length > 1;

        if (hasMultipleTargets) targetDualGridRuleTiles = targets.Cast<DualGridDataTile>().ToList();
        else targetDualGridRuleTiles = new List<DualGridDataTile>() { target as DualGridDataTile };
    }

    /// <summary>
    /// Draws the do-not-edit warning and the disabled collider and GameObject fields of the first selected tile.
    /// </summary>
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
