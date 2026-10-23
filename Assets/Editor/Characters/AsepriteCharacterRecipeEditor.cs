using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector of <see cref="AsepriteCharacterRecipe"/>: its settings, then a preview of one clip, picked by name, read
/// from its file as Generate reads it (<see cref="AsepriteClipReader"/>): its problems, or its whole sheet (a row per
/// direction, east at the top) with one direction playing beside it at the file's frame durations. The clip is read
/// again whenever the recipe or the file changes. Below it, Generate writes every sheet and the animation set through
/// <see cref="AsepriteCharacterWriter"/> once the inspector is drawn, logs what it wrote and shows the problems it
/// found until the next run.
/// </summary>
[CustomEditor(typeof(AsepriteCharacterRecipe))]
public sealed class AsepriteCharacterRecipeEditor : Editor
{
    private const float PlayScale = 3f;
    private static readonly Color32 PreviewBackground = new(70, 104, 52, 255);

    private Texture2D preview;
    private AsepriteClipFrames frames;
    private string previewKey;
    private string previewProblem;
    private string generateProblems;
    private int clipIndex;
    private int playDirection = 6;
    private int shownFrame = -1;
    private double playStart;

    internal AsepriteClipFrames PreviewFrames => frames;
    internal string PreviewProblem => previewProblem;

    /// <summary>
    /// Starts following the editor's updates while the inspector shows, so the playing direction moves.
    /// </summary>
    private void OnEnable()
    {
        playStart = EditorApplication.timeSinceStartup;
        EditorApplication.update += Animate;
    }

    /// <summary>
    /// Stops following the editor's updates and destroys the preview texture.
    /// </summary>
    private void OnDisable()
    {
        EditorApplication.update -= Animate;
        DestroyPreview();
    }

    /// <summary>
    /// Repaints when the playing direction should show another frame.
    /// </summary>
    private void Animate()
    {
        if (frames != null && frames.IsValid && PlayingFrame() != shownFrame)
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

        if (previewProblem != null)
        {
            EditorGUILayout.HelpBox(previewProblem, MessageType.Warning);
        }
        else if (preview != null && frames != null && frames.IsValid)
        {
            DrawPreview();
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

        AsepriteCharacterReport report = AsepriteCharacterWriter.Generate(recipe);
        generateProblems = string.Join("\n", report.Problems);
        string written = report.Written.Count > 0 ? string.Join(", ", report.Written) : "nothing changed";
        if (report.Problems.Count > 0)
        {
            Debug.LogWarning($"Generated {recipe.name} ({written}) with {report.Problems.Count} problems:\n{generateProblems}", recipe);
        }
        else
        {
            Debug.Log($"Generated {recipe.name}: {written}.", recipe);
        }

        Repaint();
    }

    /// <summary>
    /// Reads the picked clip again when the recipe or its file changed since the last read.
    /// </summary>
    internal void RebuildIfChanged(AsepriteCharacterRecipe recipe)
    {
        AsepriteClip clip = recipe.Clips[clipIndex];
        string path = clip?.FilePath;
        bool exists = !string.IsNullOrEmpty(path) && File.Exists(path);
        string stamp = exists ? $"{File.GetLastWriteTimeUtc(path).Ticks}|{new FileInfo(path).Length}" : "";
        string key = $"{clipIndex}|{EditorJsonUtility.ToJson(recipe)}|{path}|{stamp}";
        if (key == previewKey)
        {
            return;
        }

        previewKey = key;
        frames = null;
        previewProblem = null;
        if (!exists)
        {
            previewProblem = "Pick the clip's .aseprite file to preview it.";
            return;
        }

        frames = AsepriteClipReader.Read(path, clip.Gait, clip.Feet);
        if (!frames.IsValid)
        {
            previewProblem = string.Join("\n", frames.Problems);
            return;
        }

        if (preview == null || preview.width != frames.SheetWidth || preview.height != frames.SheetHeight)
        {
            DestroyPreview();
            preview = new Texture2D(frames.SheetWidth, frames.SheetHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        preview.SetPixels32(frames.Pixels);
        preview.Apply();
    }

    /// <summary>
    /// Draws the playing direction, the whole sheet and the line under them.
    /// </summary>
    private void DrawPreview()
    {
        Vector2Int size = frames.FrameSize;
        int frame = PlayingFrame();
        shownFrame = frame;
        RectInt frameRect = CharacterSheet.FrameRect(playDirection, frame, size);
        Rect play = GUILayoutUtility.GetRect(size.x * PlayScale, size.y * PlayScale, GUILayout.Width(size.x * PlayScale), GUILayout.Height(size.y * PlayScale));
        EditorGUI.DrawRect(play, PreviewBackground);
        Rect coords = new(frameRect.x / (float)frames.SheetWidth, frameRect.y / (float)frames.SheetHeight, size.x / (float)frames.SheetWidth, size.y / (float)frames.SheetHeight);
        GUI.DrawTextureWithTexCoords(play, preview, coords);

        float width = Mathf.Min(EditorGUIUtility.currentViewWidth - 40f, frames.SheetWidth * 2f);
        float height = width * frames.SheetHeight / frames.SheetWidth;
        Rect area = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
        EditorGUI.DrawRect(area, PreviewBackground);
        GUI.DrawTexture(area, preview, ScaleMode.ScaleToFit);
        string contact = frames.ContactFrame >= 0 ? $", contact on frame {frames.ContactFrame + 1}" : "";
        EditorGUILayout.LabelField($"{frames.FrameCount} frames of {size.x} x {size.y} px a direction, {frames.FrameSeconds.Sum():0.###} s{contact}", EditorStyles.miniLabel);
    }

    /// <summary>
    /// The frame of the previewed clip showing now, looping at its frame durations.
    /// </summary>
    private int PlayingFrame()
    {
        float total = frames.FrameSeconds.Sum();
        float time = total > 0f ? (float)((EditorApplication.timeSinceStartup - playStart) % total) : 0f;
        for (int i = 0; i < frames.FrameCount; i++)
        {
            time -= frames.FrameSeconds[i];
            if (time < 0f)
            {
                return i;
            }
        }

        return 0;
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
}
