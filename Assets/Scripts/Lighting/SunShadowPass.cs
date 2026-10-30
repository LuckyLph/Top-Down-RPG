using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// <see cref="SunShadowFeature"/>'s pass, before the 2D renderer draws anything (Docs/Lighting.md, Phase 4): draws the
/// camera's <see cref="SunShadowMask"/>, the props' shadows into the props' texture when they are out of date, then the
/// props' texture and the units' shadows into the frame's, and hands the frame's texture, its area, its projection and
/// the casters' rendering layers to the shaders as globals. With no mask to draw the area is set empty, so the lit
/// shaders read no shadow.
/// </summary>
public sealed class SunShadowPass : ScriptableRenderPass2D
{
    private static readonly ShaderTagId CasterPassTag = new("Universal2D");

    private readonly SunShadowFeature feature;

    /// <summary>
    /// What one of the pass's draws reads and writes.
    /// </summary>
    private sealed class DrawData
    {
        internal RendererListHandle casters;
        internal RendererListHandle cliffs;
        internal bool hasCliffs;
        internal TextureHandle under;
        internal bool hasUnder;
    }

    /// <summary>
    /// Creates the pass of <paramref name="owner"/>, run before the 2D renderer draws anything.
    /// </summary>
    public SunShadowPass(SunShadowFeature owner)
    {
        feature = owner;
        renderPassEvent2D = RenderPassEvent2D.BeforeRendering;
    }

    /// <summary>
    /// Records the mask's draws for the camera rendering, as its mask was prepared for this frame.
    /// </summary>
    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        Camera camera = frameData.Get<UniversalCameraData>().camera;
        SunShadowMask mask = feature.MaskOf(camera);
        if (mask == null || !mask.IsCasting)
        {
            Shader.SetGlobalVector(SunShadowGlobals.MaskRectId, Vector4.zero);
            return;
        }

        TextureHandle props = renderGraph.ImportTexture(mask.Props);
        TextureHandle frame = renderGraph.ImportTexture(mask.Frame);
        Shader.SetGlobalMatrix(SunShadowGlobals.MaskMatrixId, mask.DrawMatrix);
        if (!mask.PropsCurrent)
        {
            AddDraw(renderGraph, camera, mask, feature.PropLayers, props, TextureHandle.nullHandle, "Sun Shadows: Props", true);
            mask.MarkPropsDrawn();
        }

        AddDraw(renderGraph, camera, mask, feature.UnitLayers, frame, props, "Sun Shadows: Units", false);
        Shader.SetGlobalTexture(SunShadowGlobals.MaskId, mask.Frame.rt);
        Shader.SetGlobalVector(SunShadowGlobals.MaskRectId, mask.ShaderRect);
        Shader.SetGlobalInteger(SunShadowGlobals.FeetLayersId, (int)(feature.PropLayers.value | feature.UnitLayers.value));
    }

    /// <summary>
    /// Records a draw into <paramref name="target"/>: cleared, or a copy of <paramref name="under"/> when it is valid,
    /// then the shadows of the culled casters on <paramref name="layers"/>, and with <paramref name="withCliffs"/> those
    /// of the culled terrain chunks' cliffs.
    /// </summary>
    private void AddDraw(RenderGraph renderGraph, Camera camera, SunShadowMask mask, RenderingLayerMask layers, TextureHandle target, TextureHandle under, string name, bool withCliffs)
    {
        using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(name, out DrawData data);
        SortingSettings sorting = new(camera) { criteria = SortingCriteria.None };
        DrawingSettings drawing = new(CasterPassTag, sorting)
        {
            overrideMaterial = feature.CasterMaterial,
            overrideMaterialPassIndex = 0,
            enableDynamicBatching = false,
            perObjectData = PerObjectData.None
        };
        FilteringSettings filtering = new(RenderQueueRange.all, -1, layers.value) { sortingLayerRange = SortingLayerRange.all };
        data.casters = renderGraph.CreateRendererList(new RendererListParams(mask.Culling, drawing, filtering));
        builder.UseRendererList(data.casters);
        data.hasCliffs = withCliffs && feature.CliffShader != null;
        if (data.hasCliffs)
        {
            DrawingSettings cliffDrawing = new(CasterPassTag, sorting)
            {
                overrideShader = feature.CliffShader,
                overrideShaderPassIndex = 0,
                enableDynamicBatching = false,
                perObjectData = PerObjectData.None
            };
            FilteringSettings cliffFiltering = new(RenderQueueRange.all, -1, feature.CliffLayers.value) { sortingLayerRange = SortingLayerRange.all };
            data.cliffs = renderGraph.CreateRendererList(new RendererListParams(mask.Culling, cliffDrawing, cliffFiltering));
            builder.UseRendererList(data.cliffs);
        }
        data.hasUnder = under.IsValid();
        if (data.hasUnder)
        {
            data.under = under;
            builder.UseTexture(under);
        }

        builder.SetRenderAttachment(target, 0, AccessFlags.Write);
        builder.AllowPassCulling(false);
        builder.SetRenderFunc(static (DrawData pass, RasterGraphContext context) =>
        {
            if (pass.hasUnder)
            {
                Blitter.BlitTexture(context.cmd, pass.under, new Vector4(1f, 1f, 0f, 0f), 0f, false);
            }
            else
            {
                context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1f, 0);
            }

            context.cmd.DrawRendererList(pass.casters);
            if (pass.hasCliffs)
            {
                context.cmd.DrawRendererList(pass.cliffs);
            }
        });
    }
}
