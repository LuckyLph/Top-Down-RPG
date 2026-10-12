using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector of every <see cref="TerrainMaterialRecipe"/>: its settings, previews of what it generates for its seed
/// (the albedo; the cell field, each chip or stone in its own colour, brighter where its alpha is larger, its centre
/// pixel white and pixels of no chip dark; the height's smooth red channel in grey), regenerated after every change and
/// after an undo or redo that changed the recipe, and Generate, which writes the textures as the material's through
/// <see cref="TerrainMaterialWriter"/> once the inspector is drawn and names the files it writes.
/// </summary>
[CustomEditor(typeof(TerrainMaterialRecipe), true)]
public sealed class TerrainMaterialRecipeEditor : Editor
{
    private const float MaxPreviewScale = 2f;
    private const float MinPreviewSide = 32f;
    private const float PreviewSpacing = 4f;
    private const int HueSalt = 17;

    private static readonly Color32 NoChipColour = new(28, 30, 34, 255);
    private static readonly Color32 CentreColour = new(255, 255, 255, 255);

    private TerrainTextures preview;
    private Texture2D albedoPreview;
    private Texture2D fieldPreview;
    private Texture2D heightPreview;
    private string previewSettings;
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
    /// Draws the settings, the previews with a summary, and Generate.
    /// </summary>
    public override void OnInspectorGUI()
    {
        TerrainMaterialRecipe recipe = (TerrainMaterialRecipe)target;
        if (DrawDefaultInspector())
        {
            previewStale = true;
        }

        EditorGUILayout.Space();
        DrawPreviews(recipe);
        EditorGUILayout.Space();
        DrawGenerate(recipe);
    }

    /// <summary>
    /// Draws the three previews side by side, rebuilt during the layout pass when the settings changed, and what was
    /// generated.
    /// </summary>
    private void DrawPreviews(TerrainMaterialRecipe recipe)
    {
        if (preview == null || (previewStale && Event.current.type == EventType.Layout))
        {
            RebuildPreviews(recipe);
        }

        float width = (EditorGUIUtility.currentViewWidth - 40f - 2f * PreviewSpacing) / 3f;
        float side = Mathf.Max(MinPreviewSide, Mathf.Min(width, preview.Size * MaxPreviewScale));
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawPreview("Albedo", albedoPreview, side);
            GUILayout.Space(PreviewSpacing);
            DrawPreview("Cell field", fieldPreview, side);
            GUILayout.Space(PreviewSpacing);
            DrawPreview("Height", heightPreview, side);
        }

        Color32 colour = preview.BaseColour;
        EditorGUILayout.LabelField($"{preview.Size} px ({preview.Size / TerrainStyle.PixelsPerCell} cells), base colour ({colour.r}, {colour.g}, {colour.b}), cells: {preview.CellMode}", EditorStyles.miniLabel);
    }

    /// <summary>
    /// Draws one preview square with its label above it, or an empty square for a texture the recipe does not make.
    /// </summary>
    private static void DrawPreview(string label, Texture2D texture, float side)
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(side)))
        {
            EditorGUILayout.LabelField(texture != null ? label : $"{label}: none", EditorStyles.miniLabel, GUILayout.Width(side));
            Rect area = GUILayoutUtility.GetRect(side, side, GUILayout.Width(side), GUILayout.Height(side));
            EditorGUI.DrawRect(area, NoChipColour);
            if (texture != null)
            {
                GUI.DrawTexture(area, texture, ScaleMode.ScaleToFit);
            }
        }
    }

    /// <summary>
    /// Draws Generate, disabled without a material, which writes the textures once this GUI pass is over, and says
    /// what it writes.
    /// </summary>
    private static void DrawGenerate(TerrainMaterialRecipe recipe)
    {
        if (recipe.Material == null)
        {
            EditorGUILayout.HelpBox("Pick the terrain material that Generate fills.", MessageType.Info);
        }
        else
        {
            (string albedo, string cellField, string height) = TerrainMaterialWriter.TargetPaths(recipe.Material, recipe.CellMode != TerrainMaterial.CellMode.None);
            string field = cellField != null ? $", {Path.GetFileName(cellField)}" : string.Empty;
            EditorGUILayout.HelpBox($"Generate writes {Path.GetFileName(albedo)}{field} and {Path.GetFileName(height)} into {Path.GetDirectoryName(albedo).Replace('\\', '/')} for seed {recipe.Seed}, and sets {recipe.Material.name}'s textures, base colour, repeat and cell mode.", MessageType.None);
        }

        using (new EditorGUI.DisabledScope(recipe.Material == null))
        {
            if (GUILayout.Button("Generate"))
            {
                EditorApplication.delayCall += () => GenerateNow(recipe);
            }
        }
    }

    /// <summary>
    /// Writes <paramref name="recipe"/>'s textures as its material's and logs it.
    /// </summary>
    private static void GenerateNow(TerrainMaterialRecipe recipe)
    {
        if (recipe != null && TerrainMaterialWriter.Generate(recipe))
        {
            Debug.Log($"Generated {recipe.Material.name}'s textures from {recipe.name} (seed {recipe.Seed}).", recipe.Material);
        }
    }

    /// <summary>
    /// Generates the textures for the recipe's seed and paints the three previews.
    /// </summary>
    private void RebuildPreviews(TerrainMaterialRecipe recipe)
    {
        previewStale = false;
        previewSettings = EditorJsonUtility.ToJson(recipe);
        preview = recipe.Generate(recipe.Seed);
        int size = preview.Size;
        Paint(ref albedoPreview, size, preview.Albedo);
        Paint(ref fieldPreview, size, preview.CellField != null ? FieldColours(preview.CellField, size) : null);
        Color32[] heights = new Color32[preview.Height.Length];
        for (int i = 0; i < heights.Length; i++)
        {
            byte grey = preview.Height[i].r;
            heights[i] = new Color32(grey, grey, grey, 255);
        }

        Paint(ref heightPreview, size, heights);
    }

    /// <summary>
    /// The cell field as colours: each chip or stone in a hue of its centre pixel, brighter where its alpha (reach or
    /// distance to its edge) is larger, its centre pixel white, and pixels of no chip dark.
    /// </summary>
    private static Color32[] FieldColours(Color32[] field, int size)
    {
        Color32[] colours = new Color32[field.Length];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                Color32 texel = field[i];
                if (texel.a == 0)
                {
                    colours[i] = NoChipColour;
                    continue;
                }

                int ox = texel.r - 128;
                int oy = texel.g - 128;
                if (ox == 0 && oy == 0)
                {
                    colours[i] = CentreColour;
                    continue;
                }

                int centreX = TerrainArt.Wrap(x + ox, size);
                int centreY = TerrainArt.Wrap(y + oy, size);
                float hue = (float)TerrainArt.Hash01(centreX, centreY, HueSalt);
                float value = 0.3f + 0.7f * Mathf.Clamp01(texel.a / 16f / 4f);
                colours[i] = Color.HSVToRGB(hue, 0.6f, value);
            }
        }

        return colours;
    }

    /// <summary>
    /// Writes <paramref name="pixels"/> into <paramref name="texture"/>, creating it (point filtered, never saved) or
    /// resizing it as needed, or destroys it when there are no pixels.
    /// </summary>
    private static void Paint(ref Texture2D texture, int size, Color32[] pixels)
    {
        if (pixels == null)
        {
            if (texture != null)
            {
                DestroyImmediate(texture);
            }

            texture = null;
            return;
        }

        if (texture == null || texture.width != size)
        {
            if (texture != null)
            {
                DestroyImmediate(texture);
            }

            texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        texture.SetPixels32(pixels);
        texture.Apply();
    }

    /// <summary>
    /// Destroys the preview textures.
    /// </summary>
    private void DestroyPreviews()
    {
        Paint(ref albedoPreview, 0, null);
        Paint(ref fieldPreview, 0, null);
        Paint(ref heightPreview, 0, null);
    }

    /// <summary>
    /// Regenerates the previews after an undo or redo that changed the recipe's settings; undos elsewhere in the editor
    /// leave them as they are.
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
