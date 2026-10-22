using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector of <see cref="WeaponSpriteRecipe"/>: its settings, the icon and, for a bow, the arrow's directions,
/// baked again whenever the recipe, its weapon or its character changes (their serialized settings are compared on
/// each layout), and Generate, which writes them through <see cref="WeaponSpriteWriter"/> once the inspector is drawn.
/// </summary>
[CustomEditor(typeof(WeaponSpriteRecipe))]
public sealed class WeaponSpriteRecipeEditor : Editor
{
    private const float IconScale = 4f;
    private const float ArrowScale = 2f;
    private static readonly Color PreviewBackground = new Color32(70, 104, 52, 255);

    private Texture2D icon;
    private Texture2D arrow;
    private string previewKey;
    private string previewProblem;

    /// <summary>
    /// Destroys the preview textures.
    /// </summary>
    private void OnDisable()
    {
        Release(ref icon);
        Release(ref arrow);
    }

    /// <summary>
    /// Draws the settings, the previews and Generate.
    /// </summary>
    public override void OnInspectorGUI()
    {
        WeaponSpriteRecipe recipe = (WeaponSpriteRecipe)target;
        DrawDefaultInspector();
        EditorGUILayout.Space();
        if (Event.current.type == EventType.Layout)
        {
            RebuildIfChanged(recipe);
        }

        if (previewProblem != null)
        {
            EditorGUILayout.HelpBox(previewProblem, MessageType.Warning);
        }
        else
        {
            DrawTexture(icon, IconScale);
            DrawTexture(arrow, ArrowScale);
        }

        using (new EditorGUI.DisabledScope(previewProblem != null))
        {
            if (GUILayout.Button("Generate"))
            {
                EditorApplication.delayCall += () => GenerateNow(recipe);
            }
        }
    }

    /// <summary>
    /// Writes <paramref name="recipe"/>'s sprites and logs them.
    /// </summary>
    private static void GenerateNow(WeaponSpriteRecipe recipe)
    {
        if (recipe == null)
        {
            return;
        }

        IReadOnlyList<string> paths = WeaponSpriteWriter.Generate(recipe);
        if (paths != null)
        {
            Debug.Log($"Generated {string.Join(", ", paths)} from {recipe.name}.", recipe);
        }
    }

    /// <summary>
    /// Bakes the previews again when what they are made from changed since the last bake.
    /// </summary>
    private void RebuildIfChanged(WeaponSpriteRecipe recipe)
    {
        string key = $"{EditorJsonUtility.ToJson(recipe)}|{(recipe.Weapon != null ? EditorJsonUtility.ToJson(recipe.Weapon) : "")}|{(recipe.Character != null ? EditorJsonUtility.ToJson(recipe.Character) : "")}";
        if (key == previewKey)
        {
            return;
        }

        previewKey = key;
        if (!recipe.CanGenerate(out previewProblem))
        {
            return;
        }

        Fill(ref icon, recipe.IconSize, recipe.IconSize, WeaponSpriteWriter.BakeIcon(recipe));
        if (recipe.HasArrow)
        {
            Fill(ref arrow, recipe.ArrowFrame * recipe.ArrowDirections, recipe.ArrowFrame, WeaponSpriteWriter.BakeArrow(recipe));
        }
        else
        {
            Release(ref arrow);
        }
    }

    /// <summary>
    /// Puts <paramref name="pixels"/> on <paramref name="texture"/>, making it again when its size changed.
    /// </summary>
    private static void Fill(ref Texture2D texture, int width, int height, Color32[] pixels)
    {
        if (texture == null || texture.width != width || texture.height != height)
        {
            Release(ref texture);
            texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        texture.SetPixels32(pixels);
        texture.Apply();
    }

    /// <summary>
    /// Draws <paramref name="texture"/> at <paramref name="scale"/> on the preview background, shrunk to fit the
    /// inspector's width.
    /// </summary>
    private static void DrawTexture(Texture2D texture, float scale)
    {
        if (texture == null)
        {
            return;
        }

        float width = Mathf.Min(texture.width * scale, EditorGUIUtility.currentViewWidth - 40f);
        float height = width * texture.height / texture.width;
        Rect area = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
        EditorGUI.DrawRect(area, PreviewBackground);
        GUI.DrawTexture(area, texture, ScaleMode.ScaleToFit);
    }

    /// <summary>
    /// Destroys <paramref name="texture"/> and clears the reference.
    /// </summary>
    private static void Release(ref Texture2D texture)
    {
        if (texture != null)
        {
            DestroyImmediate(texture);
        }

        texture = null;
    }
}
