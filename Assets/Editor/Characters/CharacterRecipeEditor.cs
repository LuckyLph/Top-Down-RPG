using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector of <see cref="CharacterRecipe"/>: its settings, then a preview of one clip, picked by name: its whole
/// sheet (a row per direction, east at the top) and, beside it, one direction playing at the motion's frame durations;
/// a cast drawn over a gait plays its frames over one gait frame, picked (the first, the left foot planted, by default;
/// Docs/KnownGaps.md, Phase 7), rather than every column of the sheet in a row. The preview is baked again whenever
/// the recipe, the clip's motion or its weapon changes (their serialized settings are compared on each layout, so undo
/// and edits to the motion or weapon assets show too). Below it: the scale, the standing height, the frames whose
/// drawing reaches their edge, and Generate, which writes every sheet and the animation set through
/// <see cref="CharacterSheetWriter"/> once the inspector is drawn.
/// </summary>
[CustomEditor(typeof(CharacterRecipe), true)]
public sealed class CharacterRecipeEditor : Editor
{
    private const float PlayScale = 3f;
    private static readonly Color32 PreviewBackground = new(70, 104, 52, 255);

    private Texture2D preview;
    private CharacterSheet sheet;
    private string previewKey;
    private string previewProblem;
    private float scale;
    private int standingHeight;
    private int clipIndex;
    private int playDirection = 6;
    private int gaitFrame;
    private int shownFrame = -1;
    private double playStart;

    /// <summary>
    /// The gait frame a cast drawn over a gait plays over in the preview.
    /// </summary>
    internal int GaitFrame => gaitFrame;

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
        if (sheet != null && PlayingFrame(sheet.Clip.Motion, PlayTime(), gaitFrame) != shownFrame)
        {
            Repaint();
        }
    }

    /// <summary>
    /// Draws the settings, the preview and Generate.
    /// </summary>
    public override void OnInspectorGUI()
    {
        CharacterRecipe recipe = (CharacterRecipe)target;
        DrawDefaultInspector();
        EditorGUILayout.Space();

        string[] names = recipe.Clips.Select(c => c != null ? c.Name : "(none)").ToArray();
        if (names.Length == 0)
        {
            EditorGUILayout.HelpBox("Add a clip to preview it.", MessageType.Info);
            return;
        }

        clipIndex = Mathf.Clamp(clipIndex, 0, names.Length - 1);
        clipIndex = EditorGUILayout.Popup("Preview clip", clipIndex, names);
        playDirection = EditorGUILayout.Popup("Play direction", playDirection, Enumerable.Range(0, CharacterDirections.Count).Select(CharacterDirections.Name).ToArray());
        CharacterMotion motion = recipe.Clips[clipIndex] != null ? recipe.Clips[clipIndex].Motion : null;
        if (motion != null && motion.GaitFrames > 0)
        {
            gaitFrame = Mathf.Clamp(gaitFrame, 0, motion.GaitFrames - 1);
            gaitFrame = EditorGUILayout.Popup("Gait frame", gaitFrame, Enumerable.Range(0, motion.GaitFrames).Select(frame => frame.ToString()).ToArray());
        }

        if (Event.current.type == EventType.Layout)
        {
            RebuildIfChanged(recipe);
        }

        if (previewProblem != null)
        {
            EditorGUILayout.HelpBox(previewProblem, MessageType.Warning);
        }
        else if (preview != null && sheet != null)
        {
            DrawPreview();
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
    /// Writes every sheet of <paramref name="recipe"/> and its animation set, and logs them.
    /// </summary>
    private static void GenerateNow(CharacterRecipe recipe)
    {
        if (recipe == null)
        {
            return;
        }

        IReadOnlyList<string> paths = CharacterSheetWriter.Generate(recipe);
        if (paths != null)
        {
            Debug.Log($"Generated {paths.Count - 1} sheets and the animation set from {recipe.name}: {string.Join(", ", paths)}.", recipe);
        }
    }

    /// <summary>
    /// Bakes the picked clip again when what it is made from changed since the last bake.
    /// </summary>
    internal void RebuildIfChanged(CharacterRecipe recipe)
    {
        CharacterClip clip = recipe.Clips[clipIndex];
        string key = $"{clipIndex}|{EditorJsonUtility.ToJson(recipe)}|{(clip != null && clip.Motion != null ? EditorJsonUtility.ToJson(clip.Motion) : "")}|{(clip != null && clip.Weapon != null ? EditorJsonUtility.ToJson(clip.Weapon) : "")}";
        if (key == previewKey)
        {
            return;
        }

        previewKey = key;
        sheet = null;
        if (!recipe.CanGenerate(out previewProblem))
        {
            return;
        }

        CharacterBaker baker = new(recipe);
        scale = baker.Scale;
        standingHeight = baker.StandingHeight();
        sheet = baker.Bake(clip);
        if (preview == null || preview.width != sheet.Width || preview.height != sheet.Height)
        {
            DestroyPreview();
            preview = new Texture2D(sheet.Width, sheet.Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        preview.SetPixels32(sheet.Pixels);
        preview.Apply();
    }

    /// <summary>
    /// Draws the playing direction, the whole sheet and the lines under them.
    /// </summary>
    private void DrawPreview()
    {
        CharacterClip clip = sheet.Clip;
        Vector2Int size = clip.FrameSize;
        int frame = PlayingFrame(clip.Motion, PlayTime(), gaitFrame);
        shownFrame = frame;
        RectInt frameRect = CharacterSheet.FrameRect(playDirection, frame, size);
        Rect play = GUILayoutUtility.GetRect(size.x * PlayScale, size.y * PlayScale, GUILayout.Width(size.x * PlayScale), GUILayout.Height(size.y * PlayScale));
        EditorGUI.DrawRect(play, PreviewBackground);
        Rect coords = new(frameRect.x / (float)sheet.Width, frameRect.y / (float)sheet.Height, size.x / (float)sheet.Width, size.y / (float)sheet.Height);
        GUI.DrawTextureWithTexCoords(play, preview, coords);

        float width = Mathf.Min(EditorGUIUtility.currentViewWidth - 40f, sheet.Width * 2f);
        float height = width * sheet.Height / sheet.Width;
        Rect area = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
        EditorGUI.DrawRect(area, PreviewBackground);
        GUI.DrawTexture(area, preview, ScaleMode.ScaleToFit);
        EditorGUILayout.LabelField($"{sheet.Width} x {sheet.Height} px, {sheet.FrameCount} frames of {size.x} x {size.y} px, {scale:0.###} px per rig unit, standing {standingHeight} px tall", EditorStyles.miniLabel);
        if (sheet.FramesAtEdge.Count > 0)
        {
            EditorGUILayout.HelpBox($"Drawing reaches the frame's edge in {string.Join(", ", sheet.FramesAtEdge)}: make the frame larger or move the pivot.", MessageType.Warning);
        }
    }

    /// <summary>
    /// The seconds since the preview started playing.
    /// </summary>
    private float PlayTime()
    {
        return (float)(EditorApplication.timeSinceStartup - playStart);
    }

    /// <summary>
    /// The sheet column of <paramref name="motion"/> showing <paramref name="time"/> seconds into the preview, looping
    /// at its frame durations: a motion drawn over a gait (<see cref="CharacterMotion.GaitFrames"/>) loops its own frames
    /// over gait frame <paramref name="gait"/> alone (columns <c>gait * K</c> to <c>gait * K + K - 1</c> of its K frames),
    /// any other every frame in turn.
    /// </summary>
    internal static int PlayingFrame(CharacterMotion motion, float time, int gait)
    {
        int frames = motion.FrameCount;
        int first = 0;
        if (motion.GaitFrames > 0 && frames >= motion.GaitFrames)
        {
            frames /= motion.GaitFrames;
            first = Mathf.Clamp(gait, 0, motion.GaitFrames - 1) * frames;
        }

        float total = 0f;
        for (int i = first; i < first + frames; i++)
        {
            total += motion.FrameSeconds(i);
        }

        float left = total > 0f ? Mathf.Repeat(time, total) : 0f;
        for (int i = first; i < first + frames; i++)
        {
            left -= motion.FrameSeconds(i);
            if (left < 0f)
            {
                return i;
            }
        }

        return first;
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
