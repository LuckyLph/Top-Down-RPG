Shader "Hidden/TopDownRPG/Light Shadow Projected"
{
    Properties
    {
        [HideInInspector] _ShadowColorMask("__ShadowColorMask", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" }

        Cull Off
        BlendOp Max
        Blend One One, One One
        ZWrite Off
        ZTest Always

        Stencil
        {
            Ref 1
            Comp NotEqual
            Pass Keep
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShadowProjectVertex.hlsl"

        #define LIGHT_SHADOW_ART_PIXELS 32.0
        #define LIGHT_SHADOW_MARGIN (1.0 / 32.0)

        TEXTURE2D(_FalloffLookup);
        SAMPLER(sampler_FalloffLookup);
        half _ShadowSoftnessFalloffIntensity;

        struct ArtVaryings
        {
            float4 positionCS : SV_POSITION;
            float2 shadow : TEXCOORD0;
            float2 world : TEXCOORD1;
            nointerpolation float4 edge : TEXCOORD2;
        };

        // URP's projection of a caster's edge away from the light (ShadowProjectVertex.hlsl), with the hard shadow's
        // quad widened by an art pixel along its sides and toward the light, so every texel whose art pixel's centre the
        // shadow covers is drawn, and the edge's ends in the world, taken from the provoking vertex, which holds both
        // ends of its edge in either winding.
        ArtVaryings LightShadowVertex(Attributes input)
        {
            ArtVaryings output;
            float shadowType = input.packed0.x;
            float4 positionWS = ProjectShadowVertexToWS(input.vertex.xy, input.packed0.zw, 0, shadowType, _LightPos, _ShadowModelScale, _ShadowModelMatrix, _ShadowModelInvMatrix, _ShadowContractionDistance, _ShadowRadius, _SoftShadowAngle);
            float2 end = mul(_ShadowModelMatrix, float4(input.vertex.xy * _ShadowModelScale.xy, 0.0, 1.0)).xy;
            float2 other = mul(_ShadowModelMatrix, float4(input.packed0.zw * _ShadowModelScale.xy, 0.0, 1.0)).xy;
            if (shadowType < 0.5)
            {
                float2 away = normalize(end - _LightPos.xy);
                float2 side = float2(-away.y, away.x);
                side = dot(side, other - end) > 0.0 ? -side : side;
                positionWS.xy += LIGHT_SHADOW_MARGIN * side;
                if (shadowType < -0.5)
                {
                    positionWS.xy -= LIGHT_SHADOW_MARGIN * away;
                }
            }

            output.positionCS = mul(GetWorldToHClipMatrix(), positionWS);
            output.shadow = CalculateShadowValue(shadowType);
            output.world = positionWS.xy;
            output.edge = float4(end, other);
            return output;
        }

        // The z of the cross product of two vectors on the screen's plane.
        float LightShadowCross(float2 a, float2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        // A hard shadow covers a texel when its art pixel's centre lies behind the edge, seen from the light, so the
        // shadow changes only at whole art pixels and never moves against them as the camera does; a soft shadow's
        // penumbra is drawn as URP draws it.
        half4 LightShadowFragment(ArtVaryings input) : SV_Target
        {
            if (input.shadow.y < 0.001)
            {
                float2 centre = (floor(input.world * LIGHT_SHADOW_ART_PIXELS) + 0.5) / LIGHT_SHADOW_ART_PIXELS;
                float2 light = _LightPos.xy;
                float2 a = input.edge.xy;
                float2 b = input.edge.zw;
                float wedge = LightShadowCross(a - light, b - light);
                bool behind = LightShadowCross(b - a, light - a) * LightShadowCross(b - a, centre - a) < 0.0;
                bool within = LightShadowCross(a - light, centre - light) * wedge >= 0.0 && LightShadowCross(centre - light, b - light) * wedge >= 0.0;
                clip(behind && within ? 1.0 : -1.0);
                return half4(1.0, 1.0, 1.0, 1.0);
            }

            float lateral = clamp(input.shadow.y, MIN_SHADOW_Y, 1.0);
            half2 lookup = half2(1.0 - saturate(abs(input.shadow.x) / lateral), _ShadowSoftnessFalloffIntensity);
            half value = SAMPLE_TEXTURE2D(_FalloffLookup, sampler_FalloffLookup, lookup).r;
            return half4(value, value, value, value);
        }
        ENDHLSL

        Pass
        {
            Name "Projected Shadow (R)"
            ColorMask R

            HLSLPROGRAM
            #pragma vertex LightShadowVertex
            #pragma fragment LightShadowFragment
            ENDHLSL
        }

        Pass
        {
            Name "Projected Unshadow (G)"
            ColorMask G

            Stencil
            {
                Ref 1
                Comp Equal
                Pass Keep
            }

            HLSLPROGRAM
            #pragma vertex LightShadowVertex
            #pragma fragment LightShadowFragment
            ENDHLSL
        }
    }
}
