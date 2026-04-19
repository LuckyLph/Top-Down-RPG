using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class PixelArtEditorSnapSettings
{
    private const float MoveSnap = 1f / 32f;
    private static readonly Vector3 DesiredMoveSnap = new(MoveSnap, MoveSnap, MoveSnap);

    static PixelArtEditorSnapSettings()
    {
        ApplyMoveSnap();
    }

    [MenuItem("Tools/Pixel Art/Apply Move Snap (1/32)")]
    private static void ApplyMoveSnapMenu()
    {
        ApplyMoveSnap();
        Debug.Log($"Editor move snap set to {EditorSnapSettings.move} (1 / 32).");
    }

    private static void ApplyMoveSnap()
    {
        if (Approximately(EditorSnapSettings.move, DesiredMoveSnap))
        {
            return;
        }

        EditorSnapSettings.move = DesiredMoveSnap;
    }

    private static bool Approximately(Vector3 a, Vector3 b)
    {
        return Mathf.Abs(a.x - b.x) < Mathf.Epsilon
            && Mathf.Abs(a.y - b.y) < Mathf.Epsilon
            && Mathf.Abs(a.z - b.z) < Mathf.Epsilon;
    }
}
