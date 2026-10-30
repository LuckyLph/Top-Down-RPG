Shader "TopDownRPG/Terrain/Sun Shadow Cliffs"
{
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "SunShadowCliffs"
            Tags { "LightMode" = "SunShadowCliffs" }

            Blend Off
            Cull Off
            ZWrite Off
            ZTest Always
            ColorMask R

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
            #include "Assets/Shaders/Terrain/TerrainBlendRaised.hlsl"
            #include "Assets/Shaders/Include/SunShadow.hlsl"

            #pragma vertex CliffVertex
            #pragma fragment CliffFragment

            #define CLIFF_COARSE_STEP 4.0

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 positionWS : TEXCOORD0;
            };

            TEXTURE2D(_TerrainLut);
            SAMPLER(sampler_TerrainLut);
            float4 _ControlRect;

            // The renderer's inputs the raised ground's shape needs: the lookup texture and the control map's area.
            TBInputs CliffInputs()
            {
                TBInputs ins;
                ins.lut = UnityBuildTexture2DStructNoScaleNoTexelSize(_TerrainLut);
                ins.rect = _ControlRect;
                ins.layers = 0u;
                ins.globals = float4(0.0, 0.0, 0.0, 0.0);
                ins.time = 0.0;
                ins.bridges = 0u;
                return ins;
            }

            // A terrain chunk's quad grown toward where its cliffs' shadows fall, as far as a cliff's height times the
            // sun's shadow vector, drawn into the sun shadow mask.
            Varyings CliffVertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                TBInputs ins = CliffInputs();
                float height = TBLut(ins, TB_SHARED_CLIFF_COLUMN, TB_SHARED_ROW).x / ART_PIXELS_PER_UNIT;
                float2 reach = _SunShadow.xy * height;
                float2 world = TransformObjectToWorld(input.positionOS).xy;
                world.x += input.uv.x > 0.5 ? max(reach.x, 0.0) : min(reach.x, 0.0);
                world.y += input.uv.y > 0.5 ? max(reach.y, 0.0) : min(reach.y, 0.0);
                o.positionCS = mul(_SunShadowMaskMatrix, float4(world, 0.0, 1.0));
                o.positionWS = world;
                return o;
            }

            // Whether a map pixel lies on the ground under raised ground's top: on raised ground with raised ground the
            // cliff's height above it, where its top is drawn.
            bool CliffUnderTop(TBInputs ins, float4 cliff, float2 pixel)
            {
                return TBRaisedCellAt(ins, pixel) != TB_RAMP && TBRaisedAt(ins, cliff, pixel) < 0.5 && TBRaisedAt(ins, cliff, pixel + float2(0.0, cliff.x)) < 0.5;
            }

            // Whether a map pixel's cell and the cell the cliff's height above it are raised ground: a quick test before
            // the exact one.
            bool CliffMayBeUnderTop(TBInputs ins, float height, float2 pixel)
            {
                return TBRaisedCellAt(ins, pixel) == TB_RAISED && TBRaisedCellAt(ins, pixel + float2(0.0, height)) != TB_LEVEL;
            }

            // Shadow on level ground where raised ground stands between it and the sun: some point toward the sun, up to
            // the cliff's height times the sun's shadow vector away, lies under raised ground's top. Searched in steps of
            // a few pixels by the cells, then pixel by pixel over a step whose cells are raised. Raised ground itself, its
            // cliff faces included, takes no shadow from its own cliffs.
            half4 CliffFragment(Varyings input) : SV_Target
            {
                TBInputs ins = CliffInputs();
                float4 cliff = TBLut(ins, TB_SHARED_CLIFF_COLUMN, TB_SHARED_ROW);
                float height = cliff.x;
                float2 pixel = floor(input.positionWS * ART_PIXELS_PER_UNIT) - _ControlRect.xy * ART_PIXELS_PER_UNIT;
                if (height <= 0.0 || _SunShadow.z < 0.5 || TBRaisedAt(ins, cliff, pixel) < 0.5)
                {
                    discard;
                }

                float2 shadow = _SunShadow.xy;
                float reach = height * length(shadow);
                float steps = ceil(reach / CLIFF_COARSE_STEP);
                float fine = CLIFF_COARSE_STEP;
                bool shadowed = false;
                [loop]
                for (float k = 1.0; k <= steps && !shadowed; k += 1.0)
                {
                    float t = height * k / steps;
                    [branch]
                    if (CliffMayBeUnderTop(ins, height, pixel - t * shadow) || CliffMayBeUnderTop(ins, height, pixel - height * (k - 1.0) / steps * shadow))
                    {
                        [loop]
                        for (float j = 1.0; j <= fine && !shadowed; j += 1.0)
                        {
                            float u = height * (k - 1.0 + j / fine) / steps;
                            shadowed = CliffUnderTop(ins, cliff, floor(pixel - u * shadow + 0.5));
                        }
                    }
                }

                if (!shadowed)
                {
                    discard;
                }

                return half4(1.0, 1.0, 1.0, 1.0);
            }
            ENDHLSL
        }
    }
}
