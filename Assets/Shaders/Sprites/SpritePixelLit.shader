Shader "TopDownRPG/Sprite Pixel Lit"
{
    Properties
    {
        _MainTex("Diffuse", 2D) = "white" {}
        [NoScaleOffset] _GlowTex("Glow", 2D) = "black" {}
        _FlashColor("Flash Color", Color) = (1,1,1,1)
        _FlashAmount("Flash Amount", Range(0, 1)) = 0

        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex LitVertex
            #pragma fragment LitFragment

            #include "Assets/Shaders/Include/PixelLight.hlsl"

            #pragma multi_compile_instancing
            #pragma multi_compile _ USE_SHAPE_LIGHT_TYPE_0

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 positionWS : TEXCOORD1;
                nointerpolation half shadowed : TEXCOORD2;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_GlowTex);
            SAMPLER(sampler_GlowTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _FlashColor;
                half _FlashAmount;
            CBUFFER_END

            Varyings LitVertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                o.positionCS = TransformObjectToHClip(input.positionOS);
                o.positionWS = TransformObjectToWorld(input.positionOS).xy;
                o.shadowed = (GetMeshRenderingLayer() & _SunShadowFeetLayers) != 0u ? SunShadowUnder(TransformObjectToWorld(float3(0.0, 0.0, 0.0)).xy) : -1.0;
                o.uv = input.uv;
                o.color = input.color * _Color * unity_SpriteColor;
                return o;
            }

            half4 LitFragment(Varyings input) : SV_Target
            {
                float2 texels;
                _MainTex.GetDimensions(texels.x, texels.y);
                float2 centre = PixelLightTexelCentre(input.uv, texels, input.positionWS);
                half4 main = input.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                if (main.a == 0.0)
                {
                    discard;
                }

                half shadowed = input.shadowed >= 0.0 ? input.shadowed : SunShadowAt(floor(centre * ART_PIXELS_PER_UNIT));
                half4 glow = SAMPLE_TEXTURE2D(_GlowTex, sampler_GlowTex, input.uv) * (half)(1.0 - _GlowFlicker.z);
                main.rgb = PixelGlowColour(main.rgb, glow, input.color.rgb, floor(input.uv * texels));
                half4 lit = half4(PixelGlowLit(PixelLitShadowed(main.rgb, centre, shadowed), main.rgb, glow.a), main.a);
                lit.rgb = lerp(lit.rgb, _FlashColor.rgb, _FlashAmount);
                return lit;
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" "Queue"="Transparent" "RenderType"="Transparent"}

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment

            #pragma multi_compile_instancing

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _FlashColor;
                half _FlashAmount;
            CBUFFER_END

            Varyings UnlitVertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                o.positionCS = TransformObjectToHClip(input.positionOS);
                o.uv = input.uv;
                o.color = input.color * _Color * unity_SpriteColor;
                return o;
            }

            half4 UnlitFragment(Varyings input) : SV_Target
            {
                half4 unlit = input.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                unlit.rgb = lerp(unlit.rgb, _FlashColor.rgb, _FlashAmount);
                return unlit;
            }
            ENDHLSL
        }
    }
}
