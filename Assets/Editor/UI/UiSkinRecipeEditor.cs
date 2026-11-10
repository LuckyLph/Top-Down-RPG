using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector of <see cref="UiSkinRecipe"/>: its settings, a preview of every piece it draws, enlarged on a mid grey and
/// redrawn after every change and after an undo or redo that changed the recipe, and Generate, which writes the
/// sprites through <see cref="UiSkinWriter"/> once the inspector is drawn.
/// </summary>
[CustomEditor(typeof(UiSkinRecipe))]
public sealed class UiSkinRecipeEditor : Editor
{
    private const int PreviewScale = 4;
    private static readonly Color PreviewBackground = new(0.35f, 0.38f, 0.35f, 1f);

    private readonly List<(UiSkinPiece piece, Texture2D texture)> previews = new();
    private string previewSettings;
    private string previewProblem;
    private bool previewStale = true;

    /// <summary>
    /// Starts following undo, which may change the recipe's settings.
    /// </summary>
    private void OnEnable()
    {
        previewStale = true;
        Undo.undoRedoPerformed += HandleUndoRedo;
    }

    /// <summary>
    /// Stops following undo and destroys the preview textures.
    /// </summary>
    private void OnDisable()
    {
        Undo.undoRedoPerformed -= HandleUndoRedo;
        DestroyPreviews();
    }

    /// <summary>
    /// Draws the settings, the previews and Generate.
    /// </summary>
    public override void OnInspectorGUI()
    {
        UiSkinRecipe recipe = (UiSkinRecipe)target;
        if (DrawDefaultInspector())
        {
            previewStale = true;
        }

        if (Event.current.type == EventType.Layout && previewStale)
        {
            RebuildPreviews(recipe);
        }

        EditorGUILayout.Space();
        if (previewProblem != null)
        {
            EditorGUILayout.HelpBox(previewProblem, MessageType.Warning);
        }

        foreach ((UiSkinPiece piece, Texture2D texture) in previews)
        {
            EditorGUILayout.LabelField($"{piece}: {texture.width} x {texture.height} px", EditorStyles.miniLabel);
            Rect area = GUILayoutUtility.GetRect(texture.width * PreviewScale, texture.height * PreviewScale, GUILayout.Width(texture.width * PreviewScale), GUILayout.Height(texture.height * PreviewScale));
            EditorGUI.DrawRect(area, PreviewBackground);
            GUI.DrawTexture(area, texture, ScaleMode.ScaleToFit);
        }

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(previewProblem != null))
        {
            if (GUILayout.Button("Generate"))
            {
                EditorApplication.delayCall += () => GenerateNow(recipe);
            }
        }
    }

    /// <summary>
    /// Writes <paramref name="recipe"/>'s sprites and logs it.
    /// </summary>
    private static void GenerateNow(UiSkinRecipe recipe)
    {
        if (recipe != null && UiSkinWriter.Generate(recipe))
        {
            Debug.Log($"Generated the UI skin in {UiSkinRecipe.Folder} from {recipe.name}.", recipe);
        }
    }

    /// <summary>
    /// Draws every piece into a preview texture, or keeps the reason the recipe cannot.
    /// </summary>
    internal void RebuildPreviews(UiSkinRecipe recipe)
    {
        previewStale = false;
        previewSettings = EditorJsonUtility.ToJson(recipe);
        DestroyPreviews();
        if (!recipe.CanGenerate(out previewProblem))
        {
            return;
        }

        foreach (UiSkinPiece piece in UiSkinRecipe.Pieces)
        {
            SkinSprite sprite = recipe.Generate(piece);
            Texture2D texture = new(sprite.Width, sprite.Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixels32(sprite.Pixels);
            texture.Apply();
            previews.Add((piece, texture));
        }
    }

    /// <summary>
    /// Destroys the preview textures.
    /// </summary>
    private void DestroyPreviews()
    {
        foreach ((UiSkinPiece _, Texture2D texture) in previews)
        {
            if (texture != null)
            {
                DestroyImmediate(texture);
            }
        }

        previews.Clear();
    }

    /// <summary>
    /// Redraws the previews after an undo or redo that changed the recipe's settings.
    /// </summary>
    private void HandleUndoRedo()
    {
        if (target != null && EditorJsonUtility.ToJson(target) != previewSettings)
        {
            previewStale = true;
            Repaint();
        }
    }
}
