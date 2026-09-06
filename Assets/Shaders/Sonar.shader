// 맹인 유물 — 화면을 청백 모노톤(음파 시야)으로 합성하는 풀스크린 셰이더.
// 전역 _SonarAmount(0..1)로 원본↔흑백 블렌드. 시야 공간 마스킹은 PlayerVisionOverlay가 담당하므로
// 이 패스는 위치·반경을 모른 채 '보이는 픽셀'만 청백으로 물들인다(XRay.shader와 동일 구조).
Shader "Custom/Sonar"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "SonarMono"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _SonarAmount;

            half4 Frag(Varyings input) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                half lum = dot(col.rgb, half3(0.299h, 0.587h, 0.114h));
                // 청백 톤: 밝기에 파랑기 도는 흰색을 곱해 음파로 스캔한 듯한 모노톤.
                // lum≈0(=시야 밖 검정)은 그대로 검정으로 남아 '완전 암흑'을 보존한다.
                half3 tint = half3(0.80h, 0.95h, 1.25h);
                half3 mono = lum * tint;
                col.rgb = lerp(col.rgb, mono, saturate(_SonarAmount));
                return col;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
