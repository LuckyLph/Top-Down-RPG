using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector of <see cref="CharacterRecipe"/>: its settings, then a preview of one clip, picked by name: its whole
/// sheet (a row per direction, east at the top) and, beside it, one direction playing at the motion's frame durations.
/// The preview is baked again whenever the recipe, the clip's motion or its weapon changes (their serialized settings
/// are compared on each layout, so undo and edits to the motion or weapon assets show too). Below it: the scale, the
/// standing height, the frames whose drawing reaches their edge, and Generate, which writes every sheet through
/// <see cref="CharacterSheetWriter"/> once the inspector is drawn.
/// </summary>
[CustomEditor(typeof(CharacterRecipe))]
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
    private int shownFrame = -1;
    private double playStart;

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
        if (sheet != null && PlayingFrame(sheet.Clip.Motion) != shownFrame)
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
    /// Writes every sheet of <paramref name="recipe"/> and logs them.
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
            Debug.Log($"Generated {paths.Count} sheets from {recipe.name}: {string.Join(", ", paths)}.", recipe);
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
        int frame = PlayingFrame(clip.Motion);
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
    /// The frame of <paramref name="motion"/> showing now, looping at its frame durations.
    /// </summary>
    private int PlayingFrame(CharacterMotion motion)
    {
        float total = 0f;
        for (int i = 0; i < motion.FrameCount; i++)
        {
            total += motion.FrameSeconds(i);
        }

        float time = total > 0f ? (float)((EditorApplication.timeSinceStartup - playStart) % total) : 0f;
        for (int i = 0; i < motion.FrameCount; i++)
        {
            time -= motion.FrameSeconds(i);
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
