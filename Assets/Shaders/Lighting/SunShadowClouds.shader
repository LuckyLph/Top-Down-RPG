Shader "Hidden/TopDownRPG/Sun Shadow Clouds"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "SunShadowClouds"

            BlendOp Max
            Blend One One
            Cull Off
            ZWrite Off
            ZTest Always
            ColorMask R

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/Include/SunShadow.hlsl"

            #pragma vertex CloudVertex
            #pragma fragment CloudFragment

            #define CLOUD_OCTAVES 4

            // The clouds (Docs/Lighting.md, Phase 8), global and set by the day and night: _SunClouds.xy is how far the
            // clouds have drifted, in units, wrapped to the field's tile, z the noise level above which a cloud lies, w
            // the share of the sun a cloud takes away. _SunCloudShape.x is the clouds' size in units, y how far either side
            // of the level a cloud's edge fades, in noise, z how many of the noise's lattice cells its tile spans, a power
            // of two.
            float4 _SunClouds;
            float4 _SunCloudShape;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            // A triangle over the whole mask.
            Varyings CloudVertex(uint vertexID : SV_VertexID)
            {
                Varyings o;
                o.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                return o;
            }

            // A hash of a lattice cell.
            uint CloudHash(int2 cell)
            {
                uint h = asuint(cell.x) * 374761393u + asuint(cell.y) * 668265263u + 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return h ^ (h >> 16);
            }

            // The gradient of a lattice cell, the cells wrapped to a tile of period cells, a power of two, so the field
            // repeats.
            float2 CloudGradient(int2 cell, int period)
            {
                int2 wrapped = cell & (period - 1);
                float angle = CloudHash(wrapped) * (6.28318530718 / 4294967296.0);
                return float2(cos(angle), sin(angle));
            }

            // Gradient noise at p, repeating every period lattice cells, about -0.7 to 0.7.
            float CloudNoise(float2 p, int period)
            {
                float2 corner = floor(p);
                float2 f = p - corner;
                int2 cell = (int2)corner;
                float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
                float a = dot(CloudGradient(cell, period), f);
                float b = dot(CloudGradient(cell + int2(1, 0), period), f - float2(1.0, 0.0));
                float c = dot(CloudGradient(cell + int2(0, 1), period), f - float2(0.0, 1.0));
                float d = dot(CloudGradient(cell + int2(1, 1), period), f - float2(1.0, 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // The clouds' field at a point of the world in units: octaves of gradient noise, each twice as fine and half
            // as strong, over a tile of the shape's lattice cells.
            float CloudField(float2 world)
            {
                float2 p = (world + _SunClouds.xy) / max(_SunCloudShape.x, 1e-3);
                int period = max((int)_SunCloudShape.z, 1);
                float field = 0.0;
                float amplitude = 0.5;
                [unroll]
                for (int octave = 0; octave < CLOUD_OCTAVES; octave++)
                {
                    field += amplitude * CloudNoise(p, period);
                    p *= 2.0;
                    period *= 2;
                    amplitude *= 0.5;
                }

                return field * 2.0;
            }

            // The share of the sun the clouds take away at the mask's art pixel.
            half4 CloudFragment(Varyings input) : SV_Target
            {
                float2 worldPixel = _SunShadowRect.xy + floor(input.positionCS.xy);
                float field = CloudField((worldPixel + 0.5) / ART_PIXELS_PER_UNIT);
                float edge = max(_SunCloudShape.y, 1e-4);
                half cover = (half)smoothstep(_SunClouds.z - edge, _SunClouds.z + edge, field);
                return half4(cover * (half)_SunClouds.w, 0.0, 0.0, 0.0);
            }
            ENDHLSL
        }
    }
}
