using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VContainer.Unity;
using Object = UnityEngine.Object;

/// <summary>
/// Runs URP's 2D light and shadow code once as the gameplay scope starts, behind the load (Docs/LightingGaps.md,
/// Phase 5): a point light casting hard shadows beside a caster that leaves itself lit, as the prop lights and the
/// props' casters are written, far from any world, drawn by a hidden camera into a small texture once both have had a
/// frame to build their meshes, then destroyed. Mono compiles that code the first time it
/// runs, which a development player otherwise did in the frames the night's lights first burnt near casters, 12 to 15
/// ms each.
/// </summary>
public sealed class LightWarmUp : IStartable, ITickable, IDisposable
{
    private const int RenderTick = 2;
    private const int TargetSize = 32;
    private static readonly Vector3 Place = new(-4096f, -4096f, 0f);

    private GameObject root;
    private Camera camera;
    private RenderTexture target;
    private int ticks;

    /// <summary>
    /// Whether the warm-up has drawn its light and caster.
    /// </summary>
    internal bool Rendered { get; private set; }

    /// <summary>
    /// Whether its light, caster and camera are still about.
    /// </summary>
    internal bool Running => root != null;

    /// <summary>
    /// Makes the hidden camera, the casting light and the caster.
    /// </summary>
    public void Start()
    {
        root = new GameObject("Light Warm-Up") { hideFlags = HideFlags.HideAndDontSave };
        root.transform.position = Place;
        camera = new GameObject("Camera") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Camera>();
        camera.transform.SetParent(root.transform, false);
        camera.transform.localPosition = new Vector3(0f, 0f, -10f);
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = 2f;
        Light2D light = new GameObject("Light") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Light2D>();
        light.transform.SetParent(root.transform, false);
        light.lightType = Light2D.LightType.Point;
        light.pointLightOuterRadius = 3f;
        light.shadowsEnabled = true;
        light.shadowIntensity = 1f;
        light.shadowSoftness = 0f;
        ShadowCaster2D caster = new GameObject("Caster") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<ShadowCaster2D>();
        caster.transform.SetParent(root.transform, false);
        caster.transform.localPosition = new Vector3(1f, 0f, 0f);
        caster.castingOption = ShadowCaster2D.ShadowCastingOptions.CastShadow;
        target = new RenderTexture(TargetSize, TargetSize, 24) { hideFlags = HideFlags.HideAndDontSave };
    }

    /// <summary>
    /// Draws the light and caster once both have built their meshes, and destroys everything the tick after.
    /// </summary>
    public void Tick()
    {
        if (root == null)
        {
            return;
        }

        ticks++;
        if (ticks == RenderTick)
        {
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            Rendered = true;
        }
        else if (ticks > RenderTick)
        {
            Release();
        }
    }

    /// <summary>
    /// Destroys whatever is left of the warm-up.
    /// </summary>
    public void Dispose()
    {
        Release();
    }

    /// <summary>
    /// Destroys the light, the caster, the camera and the texture.
    /// </summary>
    private void Release()
    {
        if (root != null)
        {
            DestroyObject(root);
            root = null;
            camera = null;
        }

        if (target != null)
        {
            target.Release();
            DestroyObject(target);
            target = null;
        }
    }

    /// <summary>
    /// Destroys <paramref name="made"/> in play, or at once outside it (the edit-mode tests).
    /// </summary>
    private static void DestroyObject(Object made)
    {
        if (Application.isPlaying)
        {
            Object.Destroy(made);
        }
        else
        {
            Object.DestroyImmediate(made);
        }
    }
}
