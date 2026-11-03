Shader "TopDownRPG/Sun Shadow Caster"
{
    Properties
    {
        _MainTex("Sprite", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "SunShadowCaster"
            Tags { "LightMode" = "SunShadowCaster" }

            BlendOp Max
            Blend One One
            Cull Off
            ZWrite Off
            ZTest Always
            ColorMask RGB

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #include "Assets/Shaders/Include/SunShadow.hlsl"

            #pragma vertex CasterVertex
            #pragma fragment CasterFragment

            #pragma multi_compile_instancing

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float height : TEXCOORD1;
                nointerpolation float2 ground : TEXCOORD2;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // A caster's sprite laid on the ground away from the sun: each point as high above the sprite's pivot, its
            // feet, as height lands that height times the sun's shadow vector from the feet, at its own distance across,
            // so the silhouette is flipped at its base, stretched and leaned; drawn into the sun shadow mask's art pixels,
            // with the ground under its feet (SunShadowGroundAt: what it is, and the cliff's height near raised ground).
            Varyings CasterVertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                SetUpSpriteInstanceProperties();
                float3 local = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                float2 world = TransformObjectToWorld(local).xy;
                float2 feet = TransformObjectToWorld(float3(0.0, 0.0, 0.0)).xy;
                float height = world.y - feet.y;
                float2 ground = float2(world.x + height * _SunShadow.x, feet.y + height * _SunShadow.y);
                o.positionCS = mul(_SunShadowMaskMatrix, float4(ground, 0.0, 1.0));
                o.uv = input.uv;
                o.height = height;
                o.ground = SunShadowGroundAt(floor(feet * ART_PIXELS_PER_UNIT));
                return o;
            }

            // Shadow where the sprite is opaque above its feet; the part below them, which lies on the ground already,
            // casts nothing. A caster standing on raised ground's top casts into the mask's green, the tops' shadows; one
            // on level ground into its red, and its parts higher than a cliff near it into its blue too, the parts whose
            // shadow can clear a plateau's side and fall on its top (Docs/Lighting.md, Phase 11).
            half4 CasterFragment(Varyings input) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                if (input.height < 0.0 || alpha < 0.5)
                {
                    discard;
                }

                if (input.ground.x > 0.75)
                {
                    return half4(0.0, 1.0, 0.0, 1.0);
                }

                half tall = input.ground.y > 0.0 && input.height * ART_PIXELS_PER_UNIT > input.ground.y ? 1.0 : 0.0;
                return half4(1.0, 0.0, tall, 1.0);
            }
            ENDHLSL
        }
    }
}
