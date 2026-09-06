// 엑스레이 유물 — 풀스크린 분위기 패스.
// 전역 _XRayAmount(0..1)로 원본↔효과 블렌드. URP Blitter 풀스크린 규약 사용.
//
// 톤(색) 결정은 이 패스가 하지 않는다. 지형·배경·오브젝트를 각각 단색으로 칠하는 일은
// Custom/XRayFlat 머티리얼 스왑(XRayController)이 담당한다. 여기서 다시 그레이스케일·
// 틴트를 합성하면 애써 분리한 3톤이 뭉개져 2톤처럼 보인다.
// 그래서 이 패스는 전체 감광 + 가장자리 비네트만 남긴다.
//
// _XRayTintColor / _XRayHighlightCut은 더 이상 소비하지 않는다(전역으로 계속 세팅돼도 무해).
Shader "Custom/XRay"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "XRayVignette"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _XRayAmount;   // 0..1 페이드
            float _XRayDim;      // 전체 감광 배수(1 = 감광 없음)

            half4 Frag(Varyings input) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);

                // 화면 중심에서 멀어질수록 어두워지는 비네트.
                float2 d = input.texcoord - 0.5;
                float  r = saturate(length(d) * 1.6);
                half   vig = 1.0h - smoothstep(0.45h, 1.0h, r) * 0.75h;

                half3 fx = col.rgb * _XRayDim * vig;
                col.rgb = lerp(col.rgb, fx, saturate(_XRayAmount));
                return col;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
