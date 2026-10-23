using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector of <see cref="AsepriteProjectileRecipe"/>: its settings, then a preview of its file read as Generate reads
/// it (<see cref="AsepriteClipReader.ReadDirections"/>, through <see cref="AsepritePreview"/>): its problems, or one
/// direction, picked, drawn large with the tip crossed and its angle named, every direction in a row, and a warning
/// when its frames are not as many as the recipe's directions. The file is read again whenever the recipe or the file
/// changes. Below it, Generate writes the sheet and its direction set through <see cref="AsepriteProjectileWriter"/>
/// once the inspector is drawn, logs what it wrote and shows the problems it found until the next run.
/// </summary>
[CustomEditor(typeof(AsepriteProjectileRecipe))]
public sealed class AsepriteProjectileRecipeEditor : Editor
{
    private AsepritePreview preview;
    private string generateProblems;
    private int shownDirection;

    internal AsepriteClipFrames PreviewFrames => preview.Frames;
    internal string PreviewProblem => preview.Problem;

    /// <summary>
    /// Makes the preview.
    /// </summary>
    private void OnEnable()
    {
        preview = new AsepritePreview();
    }

    /// <summary>
    /// Destroys the preview's texture.
    /// </summary>
    private void OnDisable()
    {
        preview.Destroy();
    }

    /// <summary>
    /// Draws the settings, the preview, Generate and the last run's problems.
    /// </summary>
    public override void OnInspectorGUI()
    {
        AsepriteProjectileRecipe recipe = (AsepriteProjectileRecipe)target;
        DrawDefaultInspector();
        EditorGUILayout.Space();
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
            shownDirection = EditorGUILayout.IntSlider("Direction", shownDirection, 0, frames.FrameCount - 1);
            EditorGUILayout.LabelField($"Frame {shownDirection + 1}: {360f * shownDirection / frames.FrameCount:0.##} degrees counterclockwise from east", EditorStyles.miniLabel);
            preview.DrawFrame(0, shownDirection, recipe.Tip);
            preview.DrawSheet($"{frames.FrameCount} directions of {frames.FrameSize.x} x {frames.FrameSize.y} px, east first");
            if (frames.FrameCount != recipe.Directions)
            {
                EditorGUILayout.HelpBox($"The file has {frames.FrameCount} frames, but the recipe draws {recipe.Directions} directions, a frame each.", MessageType.Warning);
            }
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
    /// Writes the sheet of <paramref name="recipe"/> and its direction set, logs what was written and keeps the
    /// problems found to show them.
    /// </summary>
    private void GenerateNow(AsepriteProjectileRecipe recipe)
    {
        if (recipe == null)
        {
            return;
        }

        generateProblems = AsepriteProjectileWriter.Generate(recipe).LogGenerated(recipe);
        Repaint();
    }

    /// <summary>
    /// Reads the file again when the recipe or the file changed since the last read.
    /// </summary>
    internal void RebuildIfChanged(AsepriteProjectileRecipe recipe)
    {
        string path = recipe.FilePath;
        preview.Refresh(EditorJsonUtility.ToJson(recipe), path, $"Pick {recipe.FileName} to preview it.", () => AsepriteClipReader.ReadDirections(path, recipe.Tip));
    }
}
