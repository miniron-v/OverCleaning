// 벽 전용 셰이더. 내 캐릭터를 중심으로 한 구(전역 _WallFadeCenter, w=반경) 안에 든
// 픽셀만 반투명하게 그린다. 구 밖은 불투명이라 벽이 통째로 사라지지 않는다.
Shader "OverCleaning/WallSphereFade"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _FadedAlpha ("Faded Alpha", Range(0, 1)) = 0.2
        _FadeSoftness ("Fade Softness", Range(0.01, 1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            // 반투명이지만 깊이를 써서, 구멍 너머로 같은 벽의 뒷면이 비치지 않게 한다.
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _FadedAlpha;
                half _FadeSoftness;
            CBUFFER_END

            // 내 캐릭터 위치(xyz)와 반경(w). 모든 벽이 같은 값을 읽으므로 전역으로 둔다.
            // 값이 없으면(0) 모든 픽셀이 구 밖이라 불투명하게 그려진다.
            float4 _WallFadeCenter;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight();
                half3 lighting = saturate(dot(normalWS, mainLight.direction)) * mainLight.color +
                    SampleSH(normalWS);
                half3 color = _BaseColor.rgb * lighting;

                float radius = _WallFadeCenter.w;
                float distanceToPlayer = distance(input.positionWS, _WallFadeCenter.xyz);
                // 구 안은 반투명, 경계는 부드럽게 잇는다.
                half fade = smoothstep(radius * (1.0 - _FadeSoftness), radius, distanceToPlayer);
                half alpha = lerp(_FadedAlpha, _BaseColor.a, fade);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
