using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// The preview the Aseprite recipes' inspectors share (<see cref="AsepriteCharacterRecipeEditor"/>,
/// <see cref="AsepriteEffectRecipeEditor"/>, <see cref="AsepriteProjectileRecipeEditor"/>): one file read as Generate
/// reads it (<see cref="AsepriteClipReader"/>), read again only when the recipe's settings or the file (its write time
/// and length) changed; its problems, or its sheet in a texture: one frame drawn large on the grass's colour with the
/// pivot pixel's corner crossed, one direction playing at the file's frame durations, and the whole sheet.
/// </summary>
internal sealed class AsepritePreview
{
    private const float FrameScale = 3f;
    private static readonly Color32 Background = new(70, 104, 52, 255);
    private static readonly Color PivotColour = new(1f, 0.25f, 0.9f, 0.9f);

    private readonly double playStart = EditorApplication.timeSinceStartup;
    private Texture2D texture;
    private string key;
    private int shownFrame = -1;

    public AsepriteClipFrames Frames { get; private set; }
    public string Problem { get; private set; }
    public bool IsReady => Frames != null && Frames.IsValid && texture != null;

    /// <summary>
    /// Whether the playing direction should show another frame than it last drew, so the inspector repaints.
    /// </summary>
    public bool NeedsRepaint => IsReady && PlayingFrame() != shownFrame;

    /// <summary>
    /// Reads the file at <paramref name="path"/> through <paramref name="read"/> when <paramref name="recipeKey"/> (the
    /// recipe's settings and whatever else picks the file) or the file changed since the last read. Without a file the
    /// problem is <paramref name="missing"/>; a file with problems shows them.
    /// </summary>
    public void Refresh(string recipeKey, string path, string missing, Func<AsepriteClipFrames> read)
    {
        bool exists = !string.IsNullOrEmpty(path) && File.Exists(path);
        string stamp = exists ? $"{File.GetLastWriteTimeUtc(path).Ticks}|{new FileInfo(path).Length}" : "";
        string fresh = $"{recipeKey}|{path}|{stamp}";
        if (fresh == key)
        {
            return;
        }

        key = fresh;
        Frames = null;
        Problem = null;
        if (!exists)
        {
            Problem = missing;
            return;
        }

        Frames = read();
        if (!Frames.IsValid)
        {
            Problem = string.Join("\n", Frames.Problems);
            return;
        }

        if (texture == null || texture.width != Frames.SheetWidth || texture.height != Frames.SheetHeight)
        {
            Destroy();
            texture = new Texture2D(Frames.SheetWidth, Frames.SheetHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        texture.SetPixels32(Frames.Pixels);
        texture.Apply();
    }

    /// <summary>
    /// Draws direction <paramref name="direction"/> playing at its frame durations, looping, with
    /// <paramref name="pivot"/> crossed.
    /// </summary>
    public void DrawPlaying(int direction, Vector2Int pivot)
    {
        shownFrame = PlayingFrame();
        DrawFrame(direction, shownFrame, pivot);
    }

    /// <summary>
    /// Draws frame <paramref name="frame"/> of direction <paramref name="direction"/> large, with the corner of the
    /// <paramref name="pivot"/> pixel (from the frame's bottom left), where its sprites pivot, crossed.
    /// </summary>
    public void DrawFrame(int direction, int frame, Vector2Int pivot)
    {
        Vector2Int size = Frames.FrameSize;
        RectInt rect = Frames.FrameRect(direction, frame);
        Rect area = GUILayoutUtility.GetRect(size.x * FrameScale, size.y * FrameScale, GUILayout.Width(size.x * FrameScale), GUILayout.Height(size.y * FrameScale));
        EditorGUI.DrawRect(area, Background);
        Rect coords = new(rect.x / (float)Frames.SheetWidth, rect.y / (float)Frames.SheetHeight, size.x / (float)Frames.SheetWidth, size.y / (float)Frames.SheetHeight);
        GUI.DrawTextureWithTexCoords(area, texture, coords);
        float x = area.x + pivot.x * FrameScale;
        float y = area.yMax - pivot.y * FrameScale;
        EditorGUI.DrawRect(new Rect(x - 2f * FrameScale, y - 0.5f, 4f * FrameScale, 1f), PivotColour);
        EditorGUI.DrawRect(new Rect(x - 0.5f, y - 2f * FrameScale, 1f, 4f * FrameScale), PivotColour);
    }

    /// <summary>
    /// Draws the whole sheet as wide as the inspector allows (at most twice its size), then <paramref name="line"/>.
    /// </summary>
    public void DrawSheet(string line)
    {
        float width = Mathf.Min(EditorGUIUtility.currentViewWidth - 40f, Frames.SheetWidth * 2f);
        float height = width * Frames.SheetHeight / Frames.SheetWidth;
        Rect area = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
        EditorGUI.DrawRect(area, Background);
        GUI.DrawTexture(area, texture, ScaleMode.ScaleToFit);
        EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
    }

    /// <summary>
    /// The total of the frames' durations, in seconds.
    /// </summary>
    public float Seconds => Frames.FrameSeconds.Sum();

    /// <summary>
    /// Destroys the texture.
    /// </summary>
    public void Destroy()
    {
        if (texture != null)
        {
            Object.DestroyImmediate(texture);
        }

        texture = null;
    }

    /// <summary>
    /// The frame showing now, looping at the frames' durations.
    /// </summary>
    private int PlayingFrame()
    {
        float total = Seconds;
        float time = total > 0f ? (float)((EditorApplication.timeSinceStartup - playStart) % total) : 0f;
        for (int i = 0; i < Frames.FrameCount; i++)
        {
            time -= Frames.FrameSeconds[i];
            if (time < 0f)
            {
                return i;
            }
        }

        return 0;
    }
}
