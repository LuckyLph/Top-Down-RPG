using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Replacement inspector for every <see cref="Tilemap"/> that locks the anchor and orientation of dual-grid render
/// tilemaps, which the <see cref="DualGridTilemapModule"/> manages.
/// </summary>
[CanEditMultipleObjects]
[CustomEditor(typeof(Tilemap))]
public class RestrictedTilemapEditor : UnityEditor.Editor
{
    private bool showInfoFoldout = true;

    /// <summary>
    /// Draws the frame rate and color fields, the anchor and orientation, the orientation matrix (editable only for a
    /// single Custom-oriented tilemap, like Unity's inspector) and a foldout listing the tiles and sprites in use.
    /// Anchor, orientation and matrix are locked while any selected tilemap is a render tilemap.
    /// </summary>
    public override void OnInspectorGUI()
    {
        serializedObject.UpdateIfRequiredOrScript();
        Tilemap tilemap = (Tilemap)target;

        SerializedProperty animationFrameRate = serializedObject.FindProperty("m_AnimationFrameRate");
        SerializedProperty color = serializedObject.FindProperty("m_Color");
        SerializedProperty tileAnchor = serializedObject.FindProperty("m_TileAnchor");
        SerializedProperty orientation = serializedObject.FindProperty("m_TileOrientation");

        EditorGUILayout.PropertyField(animationFrameRate, new GUIContent("Animation Frame Rate"));
        EditorGUILayout.PropertyField(color, new GUIContent("Color"));

        bool isRenderTilemap = HasRenderTilemapTarget();
        if (isRenderTilemap)
        {
            GUILayout.Space(2);
            EditorGUILayout.HelpBox("Editing the position and orientation of a RenderTilemap is restricted.", MessageType.Info);
            GUILayout.Space(2);
        }

        using (new EditorGUI.DisabledScope(isRenderTilemap))
        {
            EditorGUILayout.PropertyField(tileAnchor, new GUIContent("Tile Anchor"));
            EditorGUILayout.PropertyField(orientation, new GUIContent("Orientation"));
        }

        serializedObject.ApplyModifiedProperties();

        bool canEditOrientationMatrix = !isRenderTilemap && targets.Length == 1 && tilemap.orientation == Tilemap.Orientation.Custom;
        using (new EditorGUI.DisabledScope(!canEditOrientationMatrix))
        {
            DrawOrientationMatrix(tilemap);
        }

        showInfoFoldout = EditorGUILayout.Foldout(showInfoFoldout, "Info");
        if (showInfoFoldout)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                DisplayTilemapInfo(tilemap);
            }
        }
    }

    /// <summary>
    /// Whether any selected tilemap is a render tilemap of a <see cref="DualGridTilemapModule"/>.
    /// </summary>
    private bool HasRenderTilemapTarget()
    {
        foreach (Object selected in targets)
        {
            if (((Tilemap)selected).GetComponentInImmediateParent<DualGridTilemapModule>() != null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Draws the offset, rotation and scale of <paramref name="tilemap"/>'s orientation matrix, and writes an edit
    /// back with undo as long as no scale axis is zero.
    /// </summary>
    private static void DrawOrientationMatrix(Tilemap tilemap)
    {
        Matrix4x4 matrix = tilemap.orientationMatrix;

        EditorGUI.BeginChangeCheck();
        Vector3 offset = EditorGUILayout.Vector3Field("Offset", matrix.GetColumn(3));
        Vector3 rotation = EditorGUILayout.Vector3Field("Rotation", matrix.rotation.eulerAngles);
        Vector3 scale = EditorGUILayout.Vector3Field("Scale", matrix.lossyScale);
        if (EditorGUI.EndChangeCheck() && scale.x != 0f && scale.y != 0f && scale.z != 0f)
        {
            Undo.RecordObject(tilemap, "Tilemap orientation change");
            tilemap.orientationMatrix = Matrix4x4.TRS(offset, Quaternion.Euler(rotation), scale);
        }
    }

    /// <summary>
    /// Lists the distinct tiles and sprites used by <paramref name="tilemap"/> as read-only object fields.
    /// </summary>
    private void DisplayTilemapInfo(Tilemap tilemap)
    {
        TileBase[] uniqueTiles = GetUniqueTilesFromTilemap(tilemap);
        Sprite[] uniqueSprites = GetUniqueSpritesFromTilemap(tilemap);

        EditorGUILayout.LabelField("Tiles", EditorStyles.boldLabel);
        foreach (var tile in uniqueTiles)
        {
            EditorGUILayout.ObjectField(tile, typeof(TileBase), false);
        }

        EditorGUILayout.LabelField("Sprites", EditorStyles.boldLabel);
        foreach (var sprite in uniqueSprites)
        {
            EditorGUILayout.ObjectField(sprite, typeof(Sprite), false);
        }
    }

    private TileBase[] usedTilesCache;
    /// <summary>
    /// The distinct tiles in <paramref name="tilemap"/>, written into a cached array that is reallocated only when the
    /// count changes.
    /// </summary>
    private TileBase[] GetUniqueTilesFromTilemap(Tilemap tilemap)
    {
        int usedTilesCount = tilemap.GetUsedTilesCount();
        if (usedTilesCache == null || usedTilesCache.Length != usedTilesCount)
        {
            usedTilesCache = new TileBase[usedTilesCount];
        }
        tilemap.GetUsedTilesNonAlloc(usedTilesCache);

        return usedTilesCache;
    }

    private Sprite[] usedSpritesCache;
    /// <summary>
    /// The distinct sprites in <paramref name="tilemap"/>, written into a cached array that is reallocated only when
    /// the count changes.
    /// </summary>
    private Sprite[] GetUniqueSpritesFromTilemap(Tilemap tilemap)
    {
        int usedSpritesCount = tilemap.GetUsedSpritesCount();
        if (usedSpritesCache == null || usedSpritesCache.Length != usedSpritesCount)
        {
            usedSpritesCache = new Sprite[usedSpritesCount];
        }
        tilemap.GetUsedSpritesNonAlloc(usedSpritesCache);

        return usedSpritesCache;
    }
}
