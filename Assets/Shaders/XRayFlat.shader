// XRay 3톤 단색화 — SpriteRenderer에 물리는 언릿 플랫 셰이더.
// 스프라이트의 알파(실루엣)만 사용하고 RGB는 _FlatColor로 대체한다.
// 전역 _XRayAmount(0..1)로 원본 RGB ↔ _FlatColor를 블렌드하므로 페이드가 그대로 유지된다.
//
// 지형에서 이게 성립하는 근거: TerrainJobs의 다운샘플 단계가 판 픽셀을 alpha 0,
// 안 판 픽셀을 alpha 255로 굽는다. 즉 알파가 곧 '안 판 땅' 실루엣이라
// 판 굴은 자동으로 뚫려 뒤의 배경 톤이 보인다(3톤 분리가 마스크 없이 나온다).
//
// 언릿인 이유: 2D Light 영향을 받으면 조명 밝기에 따라 톤이 흔들려
// "균일한 단색"이라는 X-ray 인상이 깨진다.
Shader "Custom/XRayFlat"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _FlatColor ("Flat Color", Color) = (0.5, 0.5, 0.5, 1)
        _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha   // 스프라이트 표준 premultiplied 규약

        Pass
        {
            Name "XRayFlatSprite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _FlatColor;
                float4 _Color;
            CBUFFER_END

            float _XRayAmount;   // 전역(XRayController가 페이드)

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS);
                o.uv = TRANSFORM_TEX(input.uv, _MainTex);
                // _RendererColor는 곱하지 않는다. 인스턴싱이 꺼진 경로에서는
                // SpriteRenderer.color가 이미 버텍스 컬러로 들어오므로 색이 두 번 곱해진다.
                o.color = input.color * _Color;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 col = tex * input.color;

                // _XRayAmount=0 이면 원본과 동일, 1이면 완전 단색.
                half amt = saturate(_XRayAmount);
                col.rgb = lerp(col.rgb, _FlatColor.rgb, amt);

                col.rgb *= col.a;   // premultiply (Blend One OneMinusSrcAlpha 대응)
                return col;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
