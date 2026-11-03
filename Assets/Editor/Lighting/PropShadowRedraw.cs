using UnityEditor;

/// <summary>
/// Draws the props' sun shadows again whenever anything changes in the open scenes from the editor
/// (Docs/LightingGaps.md, Phase 3). The props' shadows are drawn again only when the sun or the view has moved enough or
/// a scene has loaded or unloaded, since props never move in play, so a prop moved, added or removed by hand in play mode
/// kept its old shadow until then; each change the editor publishes now marks every camera's props' shadows out of date
/// (<see cref="SunShadowGlobals.RedrawPropShadows"/>). Nothing of it reaches a build.
/// </summary>
[InitializeOnLoad]
public static class PropShadowRedraw
{
    static PropShadowRedraw()
    {
        ObjectChangeEvents.changesPublished -= HandleChanges;
        ObjectChangeEvents.changesPublished += HandleChanges;
    }

    /// <summary>
    /// Draws the props' shadows again after the editor publishes changes to the open scenes.
    /// </summary>
    private static void HandleChanges(ref ObjectChangeEventStream stream)
    {
        if (stream.length > 0)
        {
            SunShadowGlobals.RedrawPropShadows();
        }
    }
}
