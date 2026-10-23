using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector of <see cref="AsepriteCharacterRecipe"/>: its settings, then a preview of one clip, picked by name, read
/// from its file as Generate reads it (<see cref="AsepriteClipReader"/>, through <see cref="AsepritePreview"/>): its
/// problems, or one direction playing at the file's frame durations with the feet crossed and the whole sheet (a row
/// per direction, east at the top). The clip is read again whenever the recipe or the file changes. Below it, Generate
/// writes every sheet and the animation set through <see cref="AsepriteCharacterWriter"/> once the inspector is drawn,
/// logs what it wrote and shows the problems it found until the next run.
/// </summary>
[CustomEditor(typeof(AsepriteCharacterRecipe))]
public sealed class AsepriteCharacterRecipeEditor : Editor
{
    private AsepritePreview preview;
    private string generateProblems;
    private int clipIndex;
    private int playDirection = 6;

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
        AsepriteCharacterRecipe recipe = (AsepriteCharacterRecipe)target;
        DrawDefaultInspector();
        EditorGUILayout.Space();

        if (recipe.Clips.Count == 0)
        {
            EditorGUILayout.HelpBox("Add a clip to preview it.", MessageType.Info);
            return;
        }

        string[] names = recipe.Clips.Select((c, i) => recipe.ClipName(c) ?? $"(clip {i + 1})").ToArray();
        clipIndex = Mathf.Clamp(clipIndex, 0, names.Length - 1);
        clipIndex = EditorGUILayout.Popup("Preview clip", clipIndex, names);
        playDirection = EditorGUILayout.Popup("Play direction", playDirection, Enumerable.Range(0, CharacterDirections.Count).Select(CharacterDirections.Name).ToArray());
        if (Event.current.type == EventType.Layout)
        {
            RebuildIfChanged(recipe);
        }

        AsepriteClip clip = recipe.Clips[clipIndex];
        if (preview.Problem != null)
        {
            EditorGUILayout.HelpBox(preview.Problem, MessageType.Warning);
        }
        else if (preview.IsReady && clip != null)
        {
            AsepriteClipFrames frames = preview.Frames;
            preview.DrawPlaying(playDirection, clip.Feet);
            string contact = frames.ContactFrame >= 0 ? $", contact on frame {frames.ContactFrame + 1}" : "";
            preview.DrawSheet($"{frames.FrameCount} frames of {frames.FrameSize.x} x {frames.FrameSize.y} px a direction, {preview.Seconds:0.###} s{contact}");
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
    /// Writes every sheet of <paramref name="recipe"/> and its animation set, logs what was written and keeps the
    /// problems found to show them.
    /// </summary>
    private void GenerateNow(AsepriteCharacterRecipe recipe)
    {
        if (recipe == null)
        {
            return;
        }

        generateProblems = AsepriteCharacterWriter.Generate(recipe).LogGenerated(recipe);
        Repaint();
    }

    /// <summary>
    /// Reads the picked clip again when the recipe or its file changed since the last read.
    /// </summary>
    internal void RebuildIfChanged(AsepriteCharacterRecipe recipe)
    {
        AsepriteClip clip = recipe.Clips[clipIndex];
        string path = clip?.FilePath;
        preview.Refresh($"{clipIndex}|{EditorJsonUtility.ToJson(recipe)}", path, "Pick the clip's .aseprite file to preview it.", () => AsepriteClipReader.Read(path, clip.Gait, clip.Feet));
    }
}
