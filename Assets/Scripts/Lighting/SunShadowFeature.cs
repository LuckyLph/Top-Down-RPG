using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// The 2D renderer's feature that draws the sun shadow mask (Docs/Lighting.md, Phase 4) before each frame
/// (<see cref="SunShadowPass"/>): the casters' own sprite renderers drawn with the caster material, each sprite laid on
/// the ground away from the sun from its feet, into a <see cref="SunShadowMask"/> per camera aligned to the world's art
/// grid. Casters are sprites whose rendering layers include the props' layers (static: their shadows are drawn again
/// only when the sun or the view has moved enough, or a scene loaded or unloaded) or the units' layers (drawn every
/// frame), and terrain chunks on the cliffs' layers, whose raised ground's cliffs are drawn with the props by the
/// cliffs' shader over the chunks' own material, and the clouds' shadows over the whole mask by the cloud shader every
/// frame (Docs/Lighting.md, Phase 8). It draws nothing while the sun casts nothing (<see cref="SunShadowGlobals"/>), so
/// the lit shaders then take no sun away anywhere.
/// </summary>
[DisallowMultipleRendererFeature("Sun Shadows")]
public sealed class SunShadowFeature : ScriptableRendererFeature
{
    [SerializeField, Tooltip("The material that lays a caster's sprite on the ground away from the sun (TopDownRPG/Sun Shadow Caster).")]
    private Material casterMaterial;
    [SerializeField, Tooltip("Rendering layers of the static casters, the props, whose shadows are drawn again only when the sun or the view has moved enough.")]
    private RenderingLayerMask propLayers;
    [SerializeField, Tooltip("Rendering layers of the casters that move, the units, whose shadows are drawn every frame.")]
    private RenderingLayerMask unitLayers;
    [SerializeField, Tooltip("The shader that draws the shadows raised ground's cliffs cast, with a terrain chunk's own material (TopDownRPG/Terrain/Sun Shadow Cliffs).")]
    private Shader cliffShader;
    [SerializeField, Tooltip("Rendering layers of the terrain chunks with raised ground, whose cliffs' shadows are drawn with the props'.")]
    private RenderingLayerMask cliffLayers;
    [SerializeField, Tooltip("The shader that draws the clouds' shadows over the mask (Hidden/TopDownRPG/Sun Shadow Clouds).")]
    private Shader cloudShader;
    [SerializeField, Min(0f), Tooltip("The tallest caster's height above its feet, in units: how far below the view casters may stand and still be seen, and how far their shadows reach.")]
    private float tallestCaster = 5f;
    [SerializeField, Min(0f), Tooltip("How far the mask reaches past what the view needs, in units, so the camera can move that far before the props' shadows are drawn again.")]
    private float margin = 4f;
    [SerializeField, Min(0.1f), Tooltip("How far the tip of the tallest caster's shadow may move, in art pixels, before the props' shadows are drawn again.")]
    private float redrawPixels = 1f;

    private readonly List<SunShadowMask> masks = new();
    private SunShadowPass pass;
    private Material cloudMaterial;

    public Material CasterMaterial => casterMaterial;
    public RenderingLayerMask PropLayers => propLayers;
    public RenderingLayerMask UnitLayers => unitLayers;
    public Shader CliffShader => cliffShader;
    public RenderingLayerMask CliffLayers => cliffLayers;
    public Shader CloudShader => cloudShader;

    /// <summary>
    /// The material the clouds are drawn with, made from the cloud shader the first time; null without one.
    /// </summary>
    internal Material CloudMaterial
    {
        get
        {
            if (cloudMaterial == null && cloudShader != null)
            {
                cloudMaterial = CoreUtils.CreateEngineMaterial(cloudShader);
            }

            return cloudMaterial;
        }
    }

    /// <summary>
    /// Creates the pass and follows the cameras starting to render and the scenes loading and unloading.
    /// </summary>
    public override void Create()
    {
        pass = new SunShadowPass(this);
        RenderPipelineManager.beginCameraRendering -= HandleBeginCamera;
        RenderPipelineManager.beginCameraRendering += HandleBeginCamera;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        SceneManager.sceneUnloaded += HandleSceneUnloaded;
    }

    /// <summary>
    /// Adds the pass to a game or scene camera's frame.
    /// </summary>
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (casterMaterial != null && IsDrawn(renderingData.cameraData.camera))
        {
            renderer.EnqueuePass(pass);
        }
    }

    /// <summary>
    /// The mask <paramref name="camera"/> draws this frame, or null.
    /// </summary>
    internal SunShadowMask MaskOf(Camera camera)
    {
        for (int i = 0; i < masks.Count; i++)
        {
            if (ReferenceEquals(masks[i].Camera, camera))
            {
                return masks[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Stops following the cameras and scenes and releases every mask and the clouds' material.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        RenderPipelineManager.beginCameraRendering -= HandleBeginCamera;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        for (int i = 0; i < masks.Count; i++)
        {
            masks[i].Dispose();
        }

        masks.Clear();
        CoreUtils.Destroy(cloudMaterial);
        cloudMaterial = null;
    }

    /// <summary>
    /// Readies a camera's mask before it renders, with the sun as last set: releases the masks of destroyed cameras,
    /// then culls the casters whose shadows can reach this one's area (which needs the render context, so it is done
    /// here rather than in the pass).
    /// </summary>
    private void HandleBeginCamera(ScriptableRenderContext context, Camera camera)
    {
        ReleaseLostMasks();
        if (casterMaterial == null || !IsDrawn(camera))
        {
            return;
        }

        Vector2 shadow = SunShadowGlobals.Shadow(out bool casts);
        SunShadowMask mask = MaskOf(camera);
        if (mask == null)
        {
            if (!casts)
            {
                return;
            }

            mask = new SunShadowMask(camera);
            masks.Add(mask);
        }

        mask.Prepare(context, shadow, casts, tallestCaster, margin, redrawPixels);
    }

    /// <summary>
    /// Casters may have come with the scene: the props' shadows are drawn again.
    /// </summary>
    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        InvalidateAll();
    }

    /// <summary>
    /// Casters may have gone with the scene: the props' shadows are drawn again.
    /// </summary>
    private void HandleSceneUnloaded(Scene scene)
    {
        InvalidateAll();
    }

    /// <summary>
    /// Marks every mask's props' shadows out of date.
    /// </summary>
    private void InvalidateAll()
    {
        for (int i = 0; i < masks.Count; i++)
        {
            masks[i].Invalidate();
        }
    }

    /// <summary>
    /// Releases the masks of cameras destroyed since.
    /// </summary>
    private void ReleaseLostMasks()
    {
        for (int i = masks.Count - 1; i >= 0; i--)
        {
            if (masks[i].Camera == null)
            {
                masks[i].Dispose();
                masks.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="camera"/> draws a mask: game and scene cameras do, previews and reflections do not.
    /// </summary>
    private static bool IsDrawn(Camera camera)
    {
        return camera.cameraType == CameraType.Game || camera.cameraType == CameraType.SceneView;
    }
}
