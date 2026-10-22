using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector of the effect recipes (<see cref="ImpactEffectRecipe"/>, <see cref="PuffEffectRecipe"/>): their settings,
/// a preview of the first variant's first direction frame by frame, baked again whenever the recipe changes (its
/// serialized settings are compared on each layout), and Generate, which writes the sheet and sprite set through
/// <see cref="EffectSheetWriter"/> once the inspector is drawn.
/// </summary>
[CustomEditor(typeof(EffectRecipe), true)]
public sealed class EffectRecipeEditor : Editor
{
    private const float PreviewScale = 3f;
    private static readonly Color PreviewBackground = new Color32(70, 104, 52, 255);

    private Texture2D preview;
    private string previewKey;
    private string previewProblem;

    /// <summary>
    /// Destroys the preview texture.
    /// </summary>
    private void OnDisable()
    {
        if (preview != null)
        {
            DestroyImmediate(preview);
        }

        preview = null;
    }

    /// <summary>
    /// Draws the settings, the preview and Generate.
    /// </summary>
    public override void OnInspectorGUI()
    {
        EffectRecipe recipe = (EffectRecipe)target;
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
        else if (preview != null)
        {
            float width = Mathf.Min(preview.width * PreviewScale, EditorGUIUtility.currentViewWidth - 40f);
            float height = width * preview.height / preview.width;
            Rect area = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
            EditorGUI.DrawRect(area, PreviewBackground);
            GUI.DrawTexture(area, preview, ScaleMode.ScaleToFit);
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
    /// Writes <paramref name="recipe"/>'s sheet and sprite set and logs them.
    /// </summary>
    private static void GenerateNow(EffectRecipe recipe)
    {
        if (recipe == null)
        {
            return;
        }

        IReadOnlyList<string> paths = EffectSheetWriter.Generate(recipe);
        if (paths != null)
        {
            Debug.Log($"Generated {string.Join(", ", paths)} from {recipe.name}.", recipe);
        }
    }

    /// <summary>
    /// Bakes the first variant's first direction again, frame after frame in one row, when the recipe changed since
    /// the last bake.
    /// </summary>
    private void RebuildIfChanged(EffectRecipe recipe)
    {
        string key = EditorJsonUtility.ToJson(recipe);
        if (key == previewKey)
        {
            return;
        }

        previewKey = key;
        if (!recipe.CanGenerate(out previewProblem))
        {
            return;
        }

        int frameWidth = recipe.FrameWidth;
        int frameHeight = recipe.FrameHeight;
        int width = frameWidth * recipe.FrameCount;
        Color32[] pixels = new Color32[width * frameHeight];
        for (int frame = 0; frame < recipe.FrameCount; frame++)
        {
            Color32[] drawn = recipe.BakeFrame(0, 0, frame);
            for (int y = 0; y < frameHeight; y++)
            {
                System.Array.Copy(drawn, y * frameWidth, pixels, y * width + frame * frameWidth, frameWidth);
            }
        }

        if (preview == null || preview.width != width || preview.height != frameHeight)
        {
            if (preview != null)
            {
                DestroyImmediate(preview);
            }

            preview = new Texture2D(width, frameHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        preview.SetPixels32(pixels);
        preview.Apply();
    }
}
