using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps animated ground moving in the Scene view outside play mode (Docs/TerrainBlending.md, Phase 5 notes). The Scene
/// view only redraws when something changes, so animated materials (water) would stand still there; while an enabled
/// <see cref="TerrainBlendRenderer"/> draws one, this repaints every Scene view at twice the fastest animated
/// material's frames per second, so each animation frame shows on time whichever window has focus. It does nothing in
/// play mode, where every frame is drawn anyway, and nothing when turned off from its menu item, an editor preference
/// that is on by default. While Unity itself is in the background the editor throttles its updates, so the animation
/// slows or stops there.
/// </summary>
[InitializeOnLoad]
public static class TerrainSceneViewAnimation
{
    private const string MenuPath = "Tools/TopDownRPG/World/Terrain/Animate In Scene View";
    private const string EnabledPreference = "TopDownRPG.Terrain.AnimateInSceneView";
    private const double IdleCheckInterval = 0.5;

    private static double nextUpdate;

    /// <summary>
    /// Follows the editor's updates for the whole session, on every domain load.
    /// </summary>
    static TerrainSceneViewAnimation()
    {
        EditorApplication.update += HandleEditorUpdate;
    }

    /// <summary>
    /// Whether animated ground moves in the Scene view, for this user of the editor.
    /// </summary>
    public static bool Enabled
    {
        get => EditorPrefs.GetBool(EnabledPreference, true);
        set => EditorPrefs.SetBool(EnabledPreference, value);
    }

    /// <summary>
    /// The frames per second the Scene view must follow for <paramref name="renderers"/>: the fastest animation among
    /// those enabled and built, 0 when none animates.
    /// </summary>
    internal static float FramesPerSecond(IReadOnlyList<TerrainBlendRenderer> renderers)
    {
        float fastest = 0f;
        foreach (TerrainBlendRenderer renderer in renderers)
        {
            if (renderer != null && renderer.isActiveAndEnabled)
            {
                fastest = Mathf.Max(fastest, renderer.AnimationFramesPerSecond);
            }
        }

        return fastest;
    }

    /// <summary>
    /// Turns the Scene view animation on or off.
    /// </summary>
    [MenuItem(MenuPath)]
    private static void Toggle()
    {
        Enabled = !Enabled;
    }

    /// <summary>
    /// Shows whether the Scene view animation is on.
    /// </summary>
    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }

    /// <summary>
    /// Repaints the Scene views when the next half frame of the fastest animated ground is due, and otherwise looks
    /// for animated ground twice a second.
    /// </summary>
    private static void HandleEditorUpdate()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now < nextUpdate)
        {
            return;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneView.sceneViews.Count == 0 || !Enabled)
        {
            nextUpdate = now + IdleCheckInterval;
            return;
        }

        float framesPerSecond = FramesPerSecond(Object.FindObjectsByType<TerrainBlendRenderer>());
        nextUpdate = now + (framesPerSecond > 0f ? 0.5 / framesPerSecond : IdleCheckInterval);
        if (framesPerSecond > 0f)
        {
            SceneView.RepaintAll();
        }
    }
}
