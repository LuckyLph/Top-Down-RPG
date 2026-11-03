using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// <see cref="SunShadowFeature"/>'s pass, before the 2D renderer draws anything (Docs/Lighting.md, Phase 4): draws the
/// camera's <see cref="SunShadowMask"/>, when the props' shadows are out of date the ground the raised terrain chunks
/// stand on into the ground's texture (Phase 11) and then the props' shadows into the props' texture, then the props'
/// texture, the units' shadows and the clouds' (Phase 8, while any cloud takes the sun away) into the frame's, and hands
/// the frame's texture, the ground's, their area, their projection, the tallest caster's height and the casters'
/// rendering layers to the shaders as globals.
/// With no mask to draw the area is set empty, so the lit shaders read no shadow.
/// </summary>
public sealed class SunShadowPass : ScriptableRenderPass2D
{
    private static readonly ShaderTagId CasterPassTag = new("Universal2D");
    private const int CliffPass = 0;
    private const int GroundPass = 1;

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
        internal Material clouds;
    }

    /// <summary>
    /// What the ground's draw writes.
    /// </summary>
    private sealed class GroundData
    {
        internal RendererListHandle chunks;
        internal bool hasChunks;
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
        TextureHandle ground = renderGraph.ImportTexture(mask.Ground);
        Shader.SetGlobalMatrix(SunShadowGlobals.MaskMatrixId, mask.DrawMatrix);
        Shader.SetGlobalTexture(SunShadowGlobals.GroundId, mask.Ground.rt);
        Shader.SetGlobalVector(SunShadowGlobals.MaskRectId, mask.ShaderRect);
        Shader.SetGlobalFloat(SunShadowGlobals.TallestId, feature.TallestCaster);
        if (!mask.PropsCurrent)
        {
            AddGround(renderGraph, camera, mask, ground);
            AddDraw(renderGraph, camera, mask, feature.PropLayers, props, TextureHandle.nullHandle, ground, "Sun Shadows: Props", true, null);
            mask.MarkPropsDrawn();
        }

        AddDraw(renderGraph, camera, mask, feature.UnitLayers, frame, props, ground, "Sun Shadows: Units", false, SunShadowGlobals.Cloudy() ? feature.CloudMaterial : null);
        Shader.SetGlobalTexture(SunShadowGlobals.MaskId, mask.Frame.rt);
        Shader.SetGlobalTexture(SunShadowGlobals.PropsMaskId, mask.Props.rt);
        Shader.SetGlobalInteger(SunShadowGlobals.FeetLayersId, (int)(feature.PropLayers.value | feature.UnitLayers.value));
    }

    /// <summary>
    /// Records the ground's draw into <paramref name="target"/>: cleared, then what the ground is under the culled
    /// terrain chunks on the cliffs' layers, by the cliffs' shader's second pass over each chunk's own material.
    /// </summary>
    private void AddGround(RenderGraph renderGraph, Camera camera, SunShadowMask mask, TextureHandle target)
    {
        using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass("Sun Shadows: Ground", out GroundData data);
        data.hasChunks = feature.CliffShader != null;
        if (data.hasChunks)
        {
            SortingSettings sorting = new(camera) { criteria = SortingCriteria.None };
            DrawingSettings drawing = new(CasterPassTag, sorting)
            {
                overrideShader = feature.CliffShader,
                overrideShaderPassIndex = GroundPass,
                enableDynamicBatching = false,
                perObjectData = PerObjectData.None
            };
            FilteringSettings filtering = new(RenderQueueRange.all, -1, feature.CliffLayers.value) { sortingLayerRange = SortingLayerRange.all };
            data.chunks = renderGraph.CreateRendererList(new RendererListParams(mask.Culling, drawing, filtering));
            builder.UseRendererList(data.chunks);
        }

        builder.SetRenderAttachment(target, 0, AccessFlags.Write);
        builder.AllowPassCulling(false);
        builder.SetRenderFunc(static (GroundData pass, RasterGraphContext context) =>
        {
            context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1f, 0);
            if (pass.hasChunks)
            {
                context.cmd.DrawRendererList(pass.chunks);
            }
        });
    }

    /// <summary>
    /// Records a draw into <paramref name="target"/>: cleared, or a copy of <paramref name="under"/> when it is valid,
    /// then the shadows of the culled casters on <paramref name="layers"/>, with <paramref name="withCliffs"/> those of
    /// the culled terrain chunks' cliffs, and with <paramref name="clouds"/> the clouds' over the whole mask, each texel
    /// keeping the most shadow of either; the casters read the ground they stand on from <paramref name="ground"/>.
    /// </summary>
    private void AddDraw(RenderGraph renderGraph, Camera camera, SunShadowMask mask, RenderingLayerMask layers, TextureHandle target, TextureHandle under, TextureHandle ground, string name, bool withCliffs, Material clouds)
    {
        using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(name, out DrawData data);
        builder.UseTexture(ground);
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
                overrideShaderPassIndex = CliffPass,
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

        data.clouds = clouds;
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

            if (pass.clouds != null)
            {
                context.cmd.DrawProcedural(Matrix4x4.identity, pass.clouds, 0, MeshTopology.Triangles, 3);
            }
        });
    }
}
