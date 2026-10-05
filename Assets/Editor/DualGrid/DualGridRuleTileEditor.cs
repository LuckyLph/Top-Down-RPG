using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using static UnityEngine.Tilemaps.Tile;

/// <summary>
/// Inspector for <see cref="DualGridRuleTile"/> assets: texture setup by drag and drop, default tile settings, a live
/// tilemap preview and a 2x2 neighbor matrix per tiling rule, with multi-object editing.
/// </summary>
[CanEditMultipleObjects]
[CustomEditor(typeof(DualGridRuleTile), true)]
public class DualGridRuleTileEditor : RuleTileEditor
{
    /// <summary>
    /// Labels, tooltips and text styles used by the inspector.
    /// </summary>
    private static class Styles
    {
        public static readonly GUIContent DefaultSprite = EditorGUIUtility.TrTextContent("Default Sprite", "The default sprite will be used as a last resort when no tiling rules are valid.");
        public static readonly GUIContent DefaultGameObject = EditorGUIUtility.TrTextContent("GameObject", "Depending on the configuration on the Dual Grid Tilemap Module, this GameObject will be used for every tile.");
        public static readonly GUIContent DefaultCollider = EditorGUIUtility.TrTextContent("Collider", "The collider type that will be used for this Dual Grid Rule Tile.");

        public static readonly GUIContent OriginalTexture = EditorGUIUtility.TrTextContent("Original Texture", "The original Texture2D associated with this Dual Grid Rule Tile. Only textures splitted in 16 pieces are considered valid.");

        public static readonly GUIContent TilingRules = EditorGUIUtility.TrTextContent("Tiling Rules List");
        public static readonly GUIContent TilingRulesGameObject = EditorGUIUtility.TrTextContent("GameObject", "Depending on the configuration on the Dual Grid Tilemap Module, this GameObject will be used for this specific Tiling Rule.");
        public static readonly GUIContent TilingRulesCollider = EditorGUIUtility.TrTextContent("Collider", "Colliders per Tiling Rule are not supported. They are set for the entire Dual Grid Rule Tile.");
        public static readonly GUIContent TilingRulesOutput = EditorGUIUtility.TrTextContent("Output", "The Output for the tile which fits this Rule. Each Output type has its own properties.");

        public static readonly GUIContent TilingRulesNoise = EditorGUIUtility.TrTextContent("Noise", "The Perlin noise factor when placing the tile.");
        public static readonly GUIContent TilingRulesShuffle = EditorGUIUtility.TrTextContent("Shuffle", "The randomized transform given to the tile when placing it.");
        public static readonly GUIContent TilingRulesRandomSize = EditorGUIUtility.TrTextContent("Size", "The number of Sprites to randomize from.");

        public static readonly GUIContent TilingRulesMinSpeed = EditorGUIUtility.TrTextContent("Min Speed", "The minimum speed at which the animation is played.");
        public static readonly GUIContent TilingRulesMaxSpeed = EditorGUIUtility.TrTextContent("Max Speed", "The maximum speed at which the animation is played.");
        public static readonly GUIContent TilingRulesAnimationSize = EditorGUIUtility.TrTextContent("Size", "The number of Sprites in the animation.");

        public static readonly GUIStyle extendNeighborsLightStyle = new GUIStyle()
        {
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold,
            fontSize = 10,
            normal = new GUIStyleState()
            {
                textColor = Color.black
            }
        };

        public static readonly GUIStyle extendNeighborsDarkStyle = new GUIStyle()
        {
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold,
            fontSize = 10,
            normal = new GUIStyleState()
            {
                textColor = Color.white
            }
        };
    }

    private DualGridRuleTile targetDualGridRuleTile;

    private const string PreviewActiveStatusKey = "PreviewActiveStatusKey";
    private bool isPreviewActive;
    private ReorderableList tilingRulesReorderableList;

    private bool hasMultipleTargets = false;
    private List<DualGridRuleTile> targetDualGridRuleTiles = new();

    /// <summary>
    /// Caches the selected rule tiles, restores the preview toggle from editor prefs and builds the fixed-size
    /// reorderable list of tiling rules before the base setup.
    /// </summary>
    public override void OnEnable()
    {
        targetDualGridRuleTile = (DualGridRuleTile)target;
        hasMultipleTargets = targets.Length > 1;

        if (hasMultipleTargets) targetDualGridRuleTiles = targets.Cast<DualGridRuleTile>().ToList();
        else targetDualGridRuleTiles = new List<DualGridRuleTile>() { target as DualGridRuleTile };

        isPreviewActive = EditorPrefs.GetBool(PreviewActiveStatusKey);

        tilingRulesReorderableList = new ReorderableList(tile != null ? tile.m_TilingRules : null, typeof(RuleTile.TilingRule), true, true, false, false);
        tilingRulesReorderableList.drawHeaderCallback = OnDrawHeader;
        tilingRulesReorderableList.drawElementCallback = OnDrawElement;
        tilingRulesReorderableList.elementHeightCallback = GetElementHeight;
        tilingRulesReorderableList.onChangedCallback = ListUpdated;

        base.OnEnable();
    }

    /// <summary>
    /// Records undo for the selection and draws the inspector sections in order, stopping at the first that asks to.
    /// On any change, saves the tiles and refreshes each one's data tile, adding it as a sub-asset when it is new.
    /// </summary>
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        if (hasMultipleTargets) Undo.RecordObjects(targetDualGridRuleTiles.ToArray(), $"Updated {targetDualGridRuleTiles.Count} Dual Grid Rule Tiles");
        else Undo.RecordObject(targetDualGridRuleTile, $"Updated '{targetDualGridRuleTile.name}' Dual Grid Rule Tile");

        EditorGUI.BeginChangeCheck();

        var shouldContinue = DrawRuleTileOriginalTexture();
        if (shouldContinue) shouldContinue = DrawRuleTileSettings();
        if (shouldContinue) shouldContinue = DrawRuleTileTools();
        if (shouldContinue) shouldContinue = DrawRuleTilePreview();
        if (shouldContinue) shouldContinue = DrawTilingRulesList();

        if (EditorGUI.EndChangeCheck())
        {
            SaveSelectedTiles();

            foreach (var dualGridRuleTile in targetDualGridRuleTiles)
            {
                var dualGridDataTile = dualGridRuleTile.RefreshDataTile();

                if (!AssetDatabase.Contains(dualGridDataTile))
                {
                    dualGridDataTile.name = "DualGridDataTile";
                    AssetDatabase.AddObjectToAsset(dualGridDataTile, dualGridRuleTile);
                    AssetDatabase.SaveAssets();
                }
            }
        }
    }

    /// <summary>
    /// Draws the original texture field, or a drop area when a selected tile has neither texture nor rules. A new
    /// texture is applied to every selected tile, stopping at the first that rejects it.
    /// </summary>
    /// <returns>If the Inspector should continue the drawing pipeline.</returns>
    protected virtual bool DrawRuleTileOriginalTexture()
    {
        EditorGUILayout.LabelField("Dual Grid Settings", EditorStyles.boldLabel);

        if (targetDualGridRuleTiles.Any(dualGridRuleTile => dualGridRuleTile.OriginalTexture == null && dualGridRuleTile.m_TilingRules.Count == 0))
        {
            DrawDragAndDropArea();
            return false;
        }

        EditorGUI.BeginChangeCheck();
        EditorGUI.showMixedValue = hasMultipleTargets && targetDualGridRuleTiles.HasDifferentValues(dualGridRuleTile => dualGridRuleTile.OriginalTexture);
        Texture2D appliedTexture = EditorGUILayout.ObjectField(Styles.OriginalTexture, targetDualGridRuleTile.OriginalTexture, typeof(Texture2D), false) as Texture2D;
        if (EditorGUI.EndChangeCheck())
        {
            foreach (var dualGridRuleTile in targetDualGridRuleTiles)
            {
                bool wasTextureApplied = dualGridRuleTile.TryApplyTexture2D(appliedTexture);
                if (wasTextureApplied == false) break;
            }
        }

        EditorGUI.showMixedValue = false;
        if (appliedTexture == null) return false;

        return true;
    }

    /// <summary>
    /// Draws the texture drop area for a single tile, or an error box when several are selected; the first dropped
    /// object goes to <see cref="OnDropObjectInTextureArea"/>.
    /// </summary>
    private void DrawDragAndDropArea()
    {
        if (hasMultipleTargets)
        {
            EditorGUILayout.HelpBox("At least one of the selected Dual Grid Rule Tiles are missing an Original Texture.\n" +
                "Please select the individual empty Dual Grid Rule Tile to set the Texture.", MessageType.Error);
        }
        else
        {
            Rect dropArea = GUILayoutUtility.GetRect(0, 100, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "", EditorStyles.helpBox);
            GUI.Box(dropArea, "Drag and drop a texture\nto start creating this Dual Grid Rule Tile", EditorStyles.centeredGreyMiniLabel);

            Event evt = Event.current;
            if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
            {
                if (dropArea.Contains(evt.mousePosition))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();

                        foreach (UnityEngine.Object draggedObject in DragAndDrop.objectReferences)
                        {
                            OnDropObjectInTextureArea(draggedObject);
                            break;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Applies a dropped <see cref="Texture2D"/> to every selected tile and repaints, stopping without a repaint at the
    /// first tile that rejects it. Other object types are ignored.
    /// </summary>
    protected virtual void OnDropObjectInTextureArea(UnityEngine.Object draggedObject)
    {
        if (draggedObject is Texture2D texture)
        {
            foreach (var dualGridRuleTile in targetDualGridRuleTiles)
            {
                bool wasTextureApplied = dualGridRuleTile.TryApplyTexture2D(texture);
                if (wasTextureApplied == false) return;
            }
            Repaint();
        }
    }

    /// <summary>
    /// Draws the default sprite, GameObject and collider for all selected tiles. When one changes, refreshes every
    /// selected tile's data tile and rebuilds the colliders and render tilemaps of every module using a selected tile.
    /// </summary>
    /// <returns>If the Inspector should continue the drawing pipeline.</returns>
    protected virtual bool DrawRuleTileSettings()
    {
        EditorGUILayout.LabelField("Rule Tile Settings", EditorStyles.boldLabel);

        var shouldUpdateAffectedModules = false;

        EditorGUI.BeginChangeCheck();
        EditorGUI.showMixedValue = hasMultipleTargets && targetDualGridRuleTiles.HasDifferentValues(dualGridRuleTile => dualGridRuleTile.m_DefaultSprite);
        var defaultSprite = EditorGUILayout.ObjectField(Styles.DefaultSprite, targetDualGridRuleTiles.First().m_DefaultSprite, typeof(Sprite), false) as Sprite;
        if (EditorGUI.EndChangeCheck())
        {
            targetDualGridRuleTiles.ForEach(dualGridRuleTile => dualGridRuleTile.m_DefaultSprite = defaultSprite);
            shouldUpdateAffectedModules = true;
        }

        EditorGUI.BeginChangeCheck();
        EditorGUI.showMixedValue = hasMultipleTargets && targetDualGridRuleTiles.HasDifferentValues(dualGridRuleTile => dualGridRuleTile.m_DefaultGameObject);
        var defaultGameObject = EditorGUILayout.ObjectField(Styles.DefaultGameObject, tile.m_DefaultGameObject, typeof(GameObject), false) as GameObject;
        if (EditorGUI.EndChangeCheck())
        {
            targetDualGridRuleTiles.ForEach(dualGridRuleTile => dualGridRuleTile.m_DefaultGameObject = defaultGameObject);
            shouldUpdateAffectedModules = true;
        }

        EditorGUI.BeginChangeCheck();
        EditorGUI.showMixedValue = hasMultipleTargets && targetDualGridRuleTiles.HasDifferentValues(dualGridRuleTile => dualGridRuleTile.m_DefaultColliderType);
        var defaultColliderType = (ColliderType)EditorGUILayout.EnumPopup(Styles.DefaultCollider, tile.m_DefaultColliderType);
        if (EditorGUI.EndChangeCheck())
        {
            targetDualGridRuleTiles.ForEach(dualGridRuleTile => dualGridRuleTile.m_DefaultColliderType = defaultColliderType);
            shouldUpdateAffectedModules = true;
        }

        EditorGUI.showMixedValue = false;
        EditorGUILayout.Space();

        if (shouldUpdateAffectedModules)
        {
            targetDualGridRuleTiles.ForEach(dualGridRuleTile => dualGridRuleTile.RefreshDataTile());

            var dualGridModules = UnityEngine.Object.FindObjectsByType<DualGridTilemapModule>();
            foreach (var module in dualGridModules)
            {
                if (targetDualGridRuleTiles.Contains(module.RenderTile))
                {
                    DualGridTilemapModuleEditor.UpdateTilemapColliderComponents(module, shouldLogWarnings: false);
                    module.RefreshRenderTilemap();
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Draws the tools section, whose button copies each selected tile's default GameObject onto all its tiling rules.
    /// </summary>
    /// <returns>If the Inspector should continue the drawing pipeline.</returns>
    protected virtual bool DrawRuleTileTools()
    {
        EditorGUILayout.LabelField("Tools", EditorStyles.boldLabel);

        if (GUILayout.Button("Apply Default GameObject to all Tile Rules"))
        {
            targetDualGridRuleTiles.ForEach(dualGridRuleTile => dualGridRuleTile.m_TilingRules.ForEach(tilingRule => tilingRule.m_GameObject = dualGridRuleTile.m_DefaultGameObject));
        }

        EditorGUILayout.Space();

        return true;
    }

    /// <summary>
    /// Draws the show/hide preview toggle, remembered in editor prefs, and for a single selected tile renders it in the
    /// preview scene and shows the result scaled to the inspector width.
    /// </summary>
    /// <returns>If the Inspector should continue the drawing pipeline.</returns>
    protected virtual bool DrawRuleTilePreview()
    {
        EditorGUILayout.LabelField("Tilemap Preview", EditorStyles.boldLabel);

        if (!isPreviewActive)
        {
            if (GUILayout.Button("Show Preview"))
            {
                isPreviewActive = true;
                EditorPrefs.SetBool(PreviewActiveStatusKey, isPreviewActive);
            }
        }
        else
        {
            if (GUILayout.Button("Hide Preview"))
            {
                isPreviewActive = false;
                EditorPrefs.SetBool(PreviewActiveStatusKey, isPreviewActive);
            }
        }

        if (isPreviewActive)
        {
            if (hasMultipleTargets)
            {
                EditorGUILayout.LabelField("Preview not available when inspecting multiple Dual Grid Rule Tiles.", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                DualGridRuleTilePreviewer.LoadPreviewScene(targetDualGridRuleTile);

                DualGridRuleTilePreviewer.UpdateRenderTexture();
                RenderTexture previewTexture = DualGridRuleTilePreviewer.GetRenderTexture();

                if (previewTexture != null)
                {
                    float aspectRatio = (float)previewTexture.width / previewTexture.height;

                    float desiredWidth = EditorGUIUtility.currentViewWidth;
                    float desiredHeight = desiredWidth / aspectRatio;

                    GUILayout.Box(new GUIContent(previewTexture), GUILayout.Width(desiredWidth - 22), GUILayout.Height(desiredHeight - 3));
                }
                else
                {
                    EditorGUILayout.LabelField("Preview not available.", EditorStyles.centeredGreyMiniLabel);
                }
            }
        }

        EditorGUILayout.Space();

        return true;
    }

    /// <summary>
    /// Draws the undoable button that reapplies the texture and the automatic 16-rule preset to the selection, then the
    /// tiling rules list. Stops with an error when the tile does not have exactly 16 rules; a multi-selection only gets
    /// a warning instead of the list.
    /// </summary>
    /// <returns>If the Inspector should continue the drawing pipeline.</returns>
    protected virtual bool DrawTilingRulesList()
    {
        EditorGUILayout.LabelField("Dual Grid Tiling Rules", EditorStyles.boldLabel);

        if (GUILayout.Button("Apply Automatic Rule Tiling"))
        {
            if (hasMultipleTargets) Undo.RegisterCompleteObjectUndo(targetDualGridRuleTiles.ToArray(), $"Auto tiling {targetDualGridRuleTiles.Count} Dual Grid Rule Tiles");
            else Undo.RegisterCompleteObjectUndo(targetDualGridRuleTile, $"Auto tiling Dual Grid Rule Tile '{targetDualGridRuleTile.name}'");

            targetDualGridRuleTiles.ForEach(dualGridRuleTile => dualGridRuleTile.TryApplyTexture2D(dualGridRuleTile.OriginalTexture, ignoreAutoSlicePrompt: true));
            targetDualGridRuleTiles.ForEach(dualGridRuleTile => AutoDualGridRuleTileProvider.ApplyConfigurationPreset(ref dualGridRuleTile));
        }

        EditorGUILayout.Space();

        if (tile.m_TilingRules.Count != 16)
        {
            EditorGUILayout.HelpBox($"This Dual Grid Tile has {tile.m_TilingRules.Count} rules, but only exactly 16 is supported.\nPlease apply automatic rule tiling to fix it.", MessageType.Error);
            return false;
        }

        if (hasMultipleTargets) EditorGUILayout.HelpBox("Editing Tiling Rules manually is not supported when multiple Dual Grid Rule Tiles are selected", MessageType.Warning, true);
        else tilingRulesReorderableList?.DoLayoutList();

        return true;
    }

    /// <summary>
    /// Applies serialized changes, marks every selected tile dirty, repaints the scene views and updates the override
    /// tiles that derive from the selection.
    /// </summary>
    protected virtual void SaveSelectedTiles()
    {
        serializedObject.ApplyModifiedProperties();
        targetDualGridRuleTiles.ForEach(dualGridRuleTile => EditorUtility.SetDirty(dualGridRuleTile));
        SceneView.RepaintAll();

        targetDualGridRuleTiles.ForEach(dualGridRuleTile => UpdateAffectedOverrideTiles(dualGridRuleTile));
    }

    /// <summary>
    /// Draws a rule's neighbor matrix as a 2x2 grid whose cells are the four diagonal data tile neighbors.
    /// </summary>
    public override void RuleMatrixOnGUI(RuleTile tile, Rect rect, BoundsInt bounds, RuleTile.TilingRule tilingRule)
    {
        Handles.color = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.2f) : new Color(0f, 0f, 0f, 0.2f);
        float w = rect.width / bounds.size.x;
        float h = rect.height / bounds.size.y;

        for (int y = 0; y <= bounds.size.y; y++)
        {
            float top = rect.yMin + y * h;
            Handles.DrawLine(new Vector3(rect.xMin, top), new Vector3(rect.xMax, top));
        }
        for (int x = 0; x <= bounds.size.x; x++)
        {
            float left = rect.xMin + x * w;
            Handles.DrawLine(new Vector3(left, rect.yMin), new Vector3(left, rect.yMax));
        }
        Handles.color = Color.white;

        var neighbors = tilingRule.GetNeighbors();

        for (int y = -1; y < 1; y++)
        {
            for (int x = -1; x < 1; x++)
            {
                Vector3Int pos = new Vector3Int(x == 0 ? 1 : x, y == 0 ? 1 : y, 0);

                Rect r = new Rect(rect.xMin + (x - bounds.xMin) * w, rect.yMin + (-y + bounds.yMax - 1) * h, w - 1, h - 1);
                RuleMatrixIconOnGUI(tilingRule, neighbors, pos, r);
            }
        }
    }

    /// <summary>
    /// Always a 2x2 matrix, whatever neighbors the rule declares.
    /// </summary>
    public override BoundsInt GetRuleGUIBounds(BoundsInt bounds, RuleTile.TilingRule rule)
    {
        return new BoundsInt(-1, -1, 0, 2, 2, 0);
    }

    /// <summary>
    /// Sizes the neighbor matrix at 27 pixels per cell.
    /// </summary>
    public override Vector2 GetMatrixSize(BoundsInt bounds)
    {
        float matrixCellSize = 27;
        return new Vector2(bounds.size.x * matrixCellSize, bounds.size.y * matrixCellSize);
    }

    /// <summary>
    /// Draws one tiling rule row: the rule fields on the left, then the neighbor matrix and the sprite on the right.
    /// </summary>
    protected override void OnDrawElement(Rect rect, int index, bool isactive, bool isfocused)
    {
        RuleTile.TilingRule rule = tile.m_TilingRules[index];
        BoundsInt bounds = GetRuleGUIBounds(rule.GetBounds(), rule);

        float yPos = rect.yMin + 2f;
        float height = rect.height - k_PaddingBetweenRules;
        Vector2 matrixSize = GetMatrixSize(bounds);

        Rect spriteRect = new Rect(rect.xMax - k_DefaultElementHeight - 5f, yPos, k_DefaultElementHeight, k_DefaultElementHeight);
        Rect matrixRect = new Rect(rect.xMax - matrixSize.x - spriteRect.width - 10f, yPos, matrixSize.x, matrixSize.y);
        Rect inspectorRect = new Rect(rect.xMin, yPos, rect.width - matrixSize.x - spriteRect.width - 20f, height);

        DualGridRuleInspectorOnGUI(inspectorRect, rule);
        RuleMatrixOnGUI(tile, matrixRect, bounds, rule);
        SpriteOnGUI(spriteRect, rule);
    }

    /// <summary>
    /// Draws a rule's GameObject, its read-only collider, its output type and the fields of that output: animation
    /// speeds, random noise and shuffle, and the sprite list.
    /// </summary>
    protected virtual void DualGridRuleInspectorOnGUI(Rect rect, RuleTile.TilingRule tilingRule)
    {
        float y = rect.yMin;
        GUI.Label(new Rect(rect.xMin, y, k_LabelWidth, k_SingleLineHeight), Styles.TilingRulesGameObject);
        tilingRule.m_GameObject = (GameObject)EditorGUI.ObjectField(new Rect(rect.xMin + k_LabelWidth, y, rect.width - k_LabelWidth, k_SingleLineHeight), "", tilingRule.m_GameObject, typeof(GameObject), false);
        y += k_SingleLineHeight;

        using (new EditorGUI.DisabledScope(true))
        {
            GUI.Label(new Rect(rect.xMin, y, k_LabelWidth, k_SingleLineHeight), Styles.TilingRulesCollider);
            tilingRule.m_ColliderType = (ColliderType)EditorGUI.EnumPopup(new Rect(rect.xMin + k_LabelWidth, y, rect.width - k_LabelWidth, k_SingleLineHeight), tilingRule.m_ColliderType);
            y += k_SingleLineHeight;
        }

        GUI.Label(new Rect(rect.xMin, y, k_LabelWidth, k_SingleLineHeight), Styles.TilingRulesOutput);
        tilingRule.m_Output = (RuleTile.TilingRuleOutput.OutputSprite)EditorGUI.EnumPopup(new Rect(rect.xMin + k_LabelWidth, y, rect.width - k_LabelWidth, k_SingleLineHeight), tilingRule.m_Output);
        y += k_SingleLineHeight;

        if (tilingRule.m_Output == RuleTile.TilingRuleOutput.OutputSprite.Animation)
        {
            GUI.Label(new Rect(rect.xMin, y, k_LabelWidth, k_SingleLineHeight), Styles.TilingRulesMinSpeed);
            tilingRule.m_MinAnimationSpeed = EditorGUI.FloatField(new Rect(rect.xMin + k_LabelWidth, y, rect.width - k_LabelWidth, k_SingleLineHeight), tilingRule.m_MinAnimationSpeed);
            y += k_SingleLineHeight;
            GUI.Label(new Rect(rect.xMin, y, k_LabelWidth, k_SingleLineHeight), Styles.TilingRulesMaxSpeed);
            tilingRule.m_MaxAnimationSpeed = EditorGUI.FloatField(new Rect(rect.xMin + k_LabelWidth, y, rect.width - k_LabelWidth, k_SingleLineHeight), tilingRule.m_MaxAnimationSpeed);
            y += k_SingleLineHeight;
        }
        if (tilingRule.m_Output == RuleTile.TilingRuleOutput.OutputSprite.Random)
        {
            GUI.Label(new Rect(rect.xMin, y, k_LabelWidth, k_SingleLineHeight), Styles.TilingRulesNoise);
            tilingRule.m_PerlinScale = EditorGUI.Slider(new Rect(rect.xMin + k_LabelWidth, y, rect.width - k_LabelWidth, k_SingleLineHeight), tilingRule.m_PerlinScale, 0.001f, 0.999f);
            y += k_SingleLineHeight;

            GUI.Label(new Rect(rect.xMin, y, k_LabelWidth, k_SingleLineHeight), Styles.TilingRulesShuffle);
            tilingRule.m_RandomTransform = (RuleTile.TilingRuleOutput.Transform)EditorGUI.EnumPopup(new Rect(rect.xMin + k_LabelWidth, y, rect.width - k_LabelWidth, k_SingleLineHeight), tilingRule.m_RandomTransform);
            y += k_SingleLineHeight;
        }

        if (tilingRule.m_Output != RuleTile.TilingRuleOutput.OutputSprite.Single)
        {
            GUI.Label(new Rect(rect.xMin, y, k_LabelWidth, k_SingleLineHeight)
                , tilingRule.m_Output == RuleTile.TilingRuleOutput.OutputSprite.Animation ? Styles.TilingRulesAnimationSize : Styles.TilingRulesRandomSize);
            EditorGUI.BeginChangeCheck();
            int newLength = EditorGUI.DelayedIntField(new Rect(rect.xMin + k_LabelWidth, y, rect.width - k_LabelWidth, k_SingleLineHeight), tilingRule.m_Sprites.Length);
            if (EditorGUI.EndChangeCheck())
                Array.Resize(ref tilingRule.m_Sprites, Math.Max(newLength, 1));
            y += k_SingleLineHeight;
            for (int i = 0; i < tilingRule.m_Sprites.Length; i++)
            {
                tilingRule.m_Sprites[i] = EditorGUI.ObjectField(new Rect(rect.xMin + k_LabelWidth, y, rect.width - k_LabelWidth, k_SingleLineHeight), tilingRule.m_Sprites[i], typeof(Sprite), false) as Sprite;
                y += k_SingleLineHeight;
            }
        }
    }

    /// <summary>
    /// Labels the tiling rules list header.
    /// </summary>
    public new virtual void OnDrawHeader(Rect rect)
    {
        GUI.Label(rect, Styles.TilingRules);
    }

    /// <summary>
    /// Row height of the tiling rule at <paramref name="index"/>, as the base rule tile editor computes it.
    /// </summary>
    private float GetElementHeight(int index)
    {
        RuleTile.TilingRule rule = tile.m_TilingRules[index];
        return base.GetElementHeight(rule);
    }
}
