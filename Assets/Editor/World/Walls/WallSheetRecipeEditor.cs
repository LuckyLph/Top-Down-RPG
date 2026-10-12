using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector of <see cref="WallSheetRecipe"/>: its settings, a preview of the sheet it generates for its seed,
/// regenerated after every change and after an undo or redo that changed the recipe, and Generate, which writes and
/// slices the sheet through <see cref="WallSheetWriter"/> once the inspector is drawn.
/// </summary>
[CustomEditor(typeof(WallSheetRecipe))]
public sealed class WallSheetRecipeEditor : Editor
{
    private const float MaxPreviewScale = 2f;
    private static readonly Color32 PreviewBackground = new(40, 44, 40, 255);

    private Texture2D preview;
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
    /// Stops following undo and destroys the preview texture.
    /// </summary>
    private void OnDisable()
    {
        Undo.undoRedoPerformed -= HandleUndoRedo;
        DestroyPreview();
    }

    /// <summary>
    /// Draws the settings, the preview and Generate.
    /// </summary>
    public override void OnInspectorGUI()
    {
        WallSheetRecipe recipe = (WallSheetRecipe)target;
        if (DrawDefaultInspector())
        {
            previewStale = true;
        }

        if (previewStale && Event.current.type == EventType.Layout)
        {
            RebuildPreview(recipe);
        }

        EditorGUILayout.Space();
        if (previewProblem != null)
        {
            EditorGUILayout.HelpBox(previewProblem, MessageType.Warning);
        }
        else if (preview != null)
        {
            float side = Mathf.Min(EditorGUIUtility.currentViewWidth - 40f, preview.width * MaxPreviewScale);
            Rect area = GUILayoutUtility.GetRect(side, side, GUILayout.Width(side), GUILayout.Height(side));
            EditorGUI.DrawRect(area, PreviewBackground);
            GUI.DrawTexture(area, preview, ScaleMode.ScaleToFit);
            EditorGUILayout.LabelField($"{preview.width} px, {WallSheetRecipe.PieceCount} pieces of {recipe.PieceSize} px at {recipe.PieceSize} pixels per unit", EditorStyles.miniLabel);
        }

        using (new EditorGUI.DisabledScope(recipe.Sheet == null || previewProblem != null))
        {
            if (GUILayout.Button("Generate"))
            {
                EditorApplication.delayCall += () => GenerateNow(recipe);
            }
        }
    }

    /// <summary>
    /// Writes <paramref name="recipe"/>'s sheet and logs it.
    /// </summary>
    private static void GenerateNow(WallSheetRecipe recipe)
    {
        if (recipe != null && WallSheetWriter.Generate(recipe))
        {
            Debug.Log($"Generated {recipe.Sheet.name} from {recipe.name} (seed {recipe.Seed}).", recipe.Sheet);
        }
    }

    /// <summary>
    /// Generates the sheet for the recipe's seed into the preview texture, or keeps the reason it cannot.
    /// </summary>
    private void RebuildPreview(WallSheetRecipe recipe)
    {
        previewStale = false;
        previewSettings = EditorJsonUtility.ToJson(recipe);
        int[] masks = WallSheetRecipe.PieceMasks(recipe.RuleTile, out previewProblem);
        if (masks == null)
        {
            return;
        }

        Color32[] pixels;
        try
        {
            pixels = recipe.Generate(recipe.Seed, masks);
        }
        catch (System.InvalidOperationException problem)
        {
            previewProblem = problem.Message;
            return;
        }

        int size = recipe.SheetSize;
        if (preview == null || preview.width != size)
        {
            DestroyPreview();
            preview = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        preview.SetPixels32(pixels);
        preview.Apply();
    }

    /// <summary>
    /// Destroys the preview texture.
    /// </summary>
    private void DestroyPreview()
    {
        if (preview != null)
        {
            DestroyImmediate(preview);
        }

        preview = null;
    }

    /// <summary>
    /// Regenerates the preview after an undo or redo that changed the recipe's settings.
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
