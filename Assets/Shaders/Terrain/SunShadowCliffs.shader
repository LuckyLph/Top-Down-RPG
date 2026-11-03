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

        Pass
        {
            Name "SunShadowGround"
            Tags { "LightMode" = "SunShadowGround" }

            BlendOp Max
            Blend One One
            Cull Off
            ZWrite Off
            ZTest Always
            ColorMask RG

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
            #include "Assets/Shaders/Terrain/TerrainBlendRaised.hlsl"
            #include "Assets/Shaders/Include/SunShadow.hlsl"

            #pragma vertex GroundVertex
            #pragma fragment GroundFragment

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
            float _SunShadowTallest;

            // The renderer's inputs the raised ground's shape needs: the lookup texture and the control map's area.
            TBInputs GroundInputs()
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

            // A terrain chunk's quad grown on every side by as far as a shadow of raised ground's casters can reach
            // from it: the tallest caster's shadow and a shadow's drop off a top (SunShadowDrop), drawn into the sun
            // shadow mask's ground.
            Varyings GroundVertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                TBInputs ins = GroundInputs();
                float cliff = TBLut(ins, TB_SHARED_CLIFF_COLUMN, TB_SHARED_ROW).x;
                float2 shadow = abs(_SunShadow.xy);
                float2 reach = shadow * _SunShadowTallest + abs(SunShadowDrop(cliff)) / ART_PIXELS_PER_UNIT + 1.0;
                float2 world = TransformObjectToWorld(input.positionOS).xy;
                world.x += input.uv.x > 0.5 ? reach.x : -reach.x;
                world.y += input.uv.y > 0.5 ? reach.y : -reach.y;
                o.positionCS = mul(_SunShadowMaskMatrix, float4(world, 0.0, 1.0));
                o.positionWS = world;
                return o;
            }

            // What the ground is at a map pixel (Docs/Lighting.md, Phase 11): raised ground's top where it lies more than
            // the cliff's height above its foot (its ledge and outline included), its cliff face within that height but
            // on a ramp's slope, which counts as level ground, and level ground elsewhere; with the cliff's height in art
            // pixels / 255 beside it, so the receivers and the casters near raised ground know how far a shadow drops.
            half4 GroundFragment(Varyings input) : SV_Target
            {
                TBInputs ins = GroundInputs();
                float4 cliff = TBLut(ins, TB_SHARED_CLIFF_COLUMN, TB_SHARED_ROW);
                float height = cliff.x;
                if (height <= 0.0 || _SunShadow.z < 0.5)
                {
                    discard;
                }

                float2 pixel = floor(input.positionWS * ART_PIXELS_PER_UNIT) - _ControlRect.xy * ART_PIXELS_PER_UNIT;
                half kind = SUN_SHADOW_LEVEL;
                [branch]
                if (TBRaisedAt(ins, cliff, pixel) < 0.5)
                {
                    float rows = TBRowsAboveFoot(ins, cliff, pixel, height);
                    if (rows > height)
                    {
                        kind = SUN_SHADOW_TOP;
                    }
                    else if (TBRaisedCellAt(ins, pixel - float2(0.0, rows - 1.0)) != TB_RAMP)
                    {
                        kind = SUN_SHADOW_FACE;
                    }
                }

                return half4(kind, height / 255.0, 0.0, 0.0);
            }
            ENDHLSL
        }
    }
}
