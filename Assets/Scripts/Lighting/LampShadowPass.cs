using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// <see cref="SunShadowFeature"/>'s passes for the lamps' shadows on the casters (Docs/LightingGaps.md, Phase 2). The
/// props' Shadow Caster 2Ds shadow only the ground's sorting layers, so URP draws the casters' sorting layer's light in
/// a batch of its own without them; the first pass, before that sorting layer's sprites, hands the sprite shader the
/// ground's light texture (blend style 0's of the first batch, which holds the ground's sorting layers, Default and
/// Terrain, the first sorting layers, lit alike by every light) and the rendering layers of the casters that read it
/// and of the units among them, which read it by column; the second, after those sprites, only reads the texture, so
/// the render graph keeps it until the sprites have drawn. Where every sorting layer shares one batch (no prop casts)
/// the texture handed over is the sprites' own, and nothing is shadowed.
/// </summary>
public sealed class LampShadowPass : ScriptableRenderPass2D
{
    public static readonly int GroundLightId = Shader.PropertyToID("_LampShadowGround");
    public static readonly int CasterLayersId = Shader.PropertyToID("_LampShadowCasters");
    public static readonly int UnitLayersId = Shader.PropertyToID("_LampShadowUnits");

    private const int GroundBatch = 0;
    private readonly SunShadowFeature feature;
    private readonly bool handsOver;

    /// <summary>
    /// What a pass's render function sets.
    /// </summary>
    private sealed class PassData
    {
        internal bool handsOver;
        internal int casterLayers;
        internal int unitLayers;
    }

    /// <summary>
    /// The pass of <paramref name="owner"/> on the sorting layer <paramref name="sortingLayerId"/>: the one that hands
    /// the ground's light over before its sprites when <paramref name="handOver"/>, else the one after them that keeps
    /// it.
    /// </summary>
    public LampShadowPass(SunShadowFeature owner, int sortingLayerId, bool handOver)
    {
        feature = owner;
        handsOver = handOver;
        renderPassEvent2D = handOver ? RenderPassEvent2D.BeforeRenderingSprites : RenderPassEvent2D.AfterRenderingSprites;
        renderPassSortingLayerID = sortingLayerId;
    }

    /// <summary>
    /// Records the pass when the frame has light textures: reads the ground's, and when handing it over sets it and the
    /// casters' rendering layers as globals for the sprites drawn next.
    /// </summary>
    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        TextureHandle[][] lights = frameData.Get<Universal2DResourceData>().lightTextures;
        if (lights.Length <= GroundBatch || lights[GroundBatch] == null || lights[GroundBatch].Length == 0 || !lights[GroundBatch][0].IsValid())
        {
            return;
        }

        TextureHandle ground = lights[GroundBatch][0];
        using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(handsOver ? "Lamp Shadows: Ground Light" : "Lamp Shadows: Keep Ground Light", out PassData data);
        builder.UseTexture(ground);
        builder.AllowPassCulling(false);
        data.handsOver = handsOver;
        if (handsOver)
        {
            data.casterLayers = (int)(feature.PropLayers.value | feature.UnitLayers.value);
            data.unitLayers = (int)feature.UnitLayers.value;
            builder.SetGlobalTextureAfterPass(ground, GroundLightId);
            builder.AllowGlobalStateModification(true);
        }

        builder.SetRenderFunc(static (PassData pass, RasterGraphContext context) =>
        {
            if (pass.handsOver)
            {
                context.cmd.SetGlobalInteger(CasterLayersId, pass.casterLayers);
                context.cmd.SetGlobalInteger(UnitLayersId, pass.unitLayers);
            }
        });
    }
}
