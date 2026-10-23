using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector of <see cref="AsepriteEffectRecipe"/>: its settings, then a preview of one variant, picked by name, read
/// from its file as Generate reads it (<see cref="AsepriteClipReader.ReadEffect"/>, through
/// <see cref="AsepritePreview"/>): its problems, or one direction playing at the file's frame duration with the pivot
/// crossed and the variant's whole sheet (a row per direction, east at the top). The variant is read again whenever the
/// recipe or the file changes. Below it, Generate writes the sheet and its sprite set through
/// <see cref="AsepriteEffectWriter"/> once the inspector is drawn (which also checks the variants against each other),
/// logs what it wrote and shows the problems it found until the next run.
/// </summary>
[CustomEditor(typeof(AsepriteEffectRecipe))]
public sealed class AsepriteEffectRecipeEditor : Editor
{
    private AsepritePreview preview;
    private string generateProblems;
    private int variantIndex;
    private int playDirection;

    internal AsepriteClipFrames PreviewFrames => preview.Frames;
    internal string PreviewProblem => preview.Problem;

    /// <summary>
    /// Starts following the editor's updates while the inspector shows, so the playing direction moves.
    /// </summary>
    private void OnEnable()
    {
        preview = new AsepritePreview();
        EditorApplication.update += Animate;
    }

    /// <summary>
    /// Stops following the editor's updates and destroys the preview's texture.
    /// </summary>
    private void OnDisable()
    {
        EditorApplication.update -= Animate;
        preview.Destroy();
    }

    /// <summary>
    /// Repaints when the playing direction should show another frame.
    /// </summary>
    private void Animate()
    {
        if (preview.NeedsRepaint)
        {
            Repaint();
        }
    }

    /// <summary>
    /// Draws the settings, the preview, Generate and the last run's problems.
    /// </summary>
    public override void OnInspectorGUI()
    {
        AsepriteEffectRecipe recipe = (AsepriteEffectRecipe)target;
        DrawDefaultInspector();
        EditorGUILayout.Space();

        string[] names = Enumerable.Range(0, recipe.VariantCount).Select(v => recipe.VariantName(v) ?? recipe.SheetName).ToArray();
        variantIndex = Mathf.Clamp(variantIndex, 0, names.Length - 1);
        if (names.Length > 1)
        {
            variantIndex = EditorGUILayout.Popup("Preview variant", variantIndex, names);
        }

        if (recipe.Directional)
        {
            playDirection = EditorGUILayout.Popup("Play direction", playDirection, Enumerable.Range(0, CharacterDirections.Count).Select(CharacterDirections.Name).ToArray());
        }

        if (Event.current.type == EventType.Layout)
        {
            RebuildIfChanged(recipe);
        }

        if (preview.Problem != null)
        {
            EditorGUILayout.HelpBox(preview.Problem, MessageType.Warning);
        }
        else if (preview.IsReady)
        {
            AsepriteClipFrames frames = preview.Frames;
            preview.DrawPlaying(Mathf.Min(playDirection, frames.DirectionCount - 1), recipe.Pivot);
            int milliseconds = Mathf.RoundToInt(frames.FrameSeconds[0] * 1000f);
            preview.DrawSheet($"{frames.FrameCount} frames of {frames.FrameSize.x} x {frames.FrameSize.y} px, {milliseconds} ms each, {frames.DirectionCount} {(frames.DirectionCount == 1 ? "direction" : "directions")}");
        }

        bool canGenerate = recipe.CanGenerate(out string problem);
        if (!canGenerate)
        {
            EditorGUILayout.HelpBox(problem, MessageType.Warning);
        }

        using (new EditorGUI.DisabledScope(!canGenerate))
        {
            if (GUILayout.Button("Generate"))
            {
                EditorApplication.delayCall += () => GenerateNow(recipe);
            }
        }

        if (!string.IsNullOrEmpty(generateProblems))
        {
            EditorGUILayout.HelpBox(generateProblems, MessageType.Warning);
        }
    }

    /// <summary>
    /// Writes the sheet of <paramref name="recipe"/> and its sprite set, logs what was written and keeps the problems
    /// found to show them.
    /// </summary>
    private void GenerateNow(AsepriteEffectRecipe recipe)
    {
        if (recipe == null)
        {
            return;
        }

        generateProblems = AsepriteEffectWriter.Generate(recipe).LogGenerated(recipe);
        Repaint();
    }

    /// <summary>
    /// Reads the picked variant again when the recipe or its file changed since the last read.
    /// </summary>
    internal void RebuildIfChanged(AsepriteEffectRecipe recipe)
    {
        variantIndex = Mathf.Clamp(variantIndex, 0, recipe.VariantCount - 1);
        string path = recipe.FilePathOf(variantIndex);
        preview.Refresh($"{variantIndex}|{EditorJsonUtility.ToJson(recipe)}", path, $"Pick {recipe.FileNameOf(variantIndex)} to preview it.", () => AsepriteClipReader.ReadEffect(path, recipe.Directional, recipe.Pivot));
    }
}
