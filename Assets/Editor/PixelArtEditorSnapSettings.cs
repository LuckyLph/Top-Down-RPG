using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps the scene view move snap at 1/32 unit, one pixel at 32 pixels per unit, so dragged objects stay pixel
/// aligned.
/// </summary>
[InitializeOnLoad]
public static class PixelArtEditorSnapSettings
{
    private const float MoveSnap = 1f / 32f;
    private static readonly Vector3 DesiredMoveSnap = new(MoveSnap, MoveSnap, MoveSnap);

    /// <summary>
    /// Applies the pixel move snap on every domain load.
    /// </summary>
    static PixelArtEditorSnapSettings()
    {
        ApplyMoveSnap();
    }

    /// <summary>
    /// Applies the pixel move snap on demand and logs the resulting setting.
    /// </summary>
    [MenuItem("Tools/Pixel Art/Apply Move Snap (1/32)")]
    private static void ApplyMoveSnapMenu()
    {
        ApplyMoveSnap();
        Debug.Log($"Editor move snap set to {EditorSnapSettings.move} (1 / 32).");
    }

    /// <summary>
    /// Sets the editor move snap to 1/32 on every axis, leaving it untouched when it already matches.
    /// </summary>
    private static void ApplyMoveSnap()
    {
        if (Approximately(EditorSnapSettings.move, DesiredMoveSnap))
        {
            return;
        }

        EditorSnapSettings.move = DesiredMoveSnap;
    }

    /// <summary>
    /// Whether every component of <paramref name="a"/> and <paramref name="b"/> differs by less than
    /// <see cref="Mathf.Epsilon"/>.
    /// </summary>
    private static bool Approximately(Vector3 a, Vector3 b)
    {
        return Mathf.Abs(a.x - b.x) < Mathf.Epsilon
            && Mathf.Abs(a.y - b.y) < Mathf.Epsilon
            && Mathf.Abs(a.z - b.z) < Mathf.Epsilon;
    }
}
