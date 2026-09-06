// URP 2D 라이트 텍스처 픽셀화 셰이더
// _ShapeLightTexture를 Point 필터로 업샘플 → 블럭 픽셀 유지
Shader "Custom/PixelateLight"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "PixelatePoint"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half4 Frag(Varyings input) : SV_Target
            {
                // Point 샘플링 → 블럭 픽셀 유지
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord);
            }
            ENDHLSL
        }
    }
}
