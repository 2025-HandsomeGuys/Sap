Shader "Custom/SpriteOutline"
{
    Properties
    {
        _MainTex      ("Sprite Texture", 2D)          = "white" {}
        _OutlineColor ("Outline Color",  Color)        = (0.4, 0.9, 1.0, 1.0)
        _OutlineWidth ("Outline Width",  Range(1, 4))  = 1.0
        _OutlineAlpha ("Outline Alpha",  Range(0, 1))  = 1.0
    }

    SubShader
    {
        Tags
        {
            "Queue"             = "Transparent"
            "RenderType"        = "Transparent"
            "IgnoreProjector"   = "True"
            "PreviewType"       = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv     : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4    _MainTex_TexelSize; // Unity가 자동 설정: (1/w, 1/h, w, h)
            fixed4    _OutlineColor;
            float     _OutlineWidth;
            float     _OutlineAlpha;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv     = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);

                // 스프라이트 몸통 픽셀 → 완전 투명 (메인 SpriteRenderer가 그림)
                if (col.a > 0.5)
                    return fixed4(0, 0, 0, 0);

                // 8방향 샘플링 — round()로 정수 텍셀 정렬 보장
                // _OutlineWidth를 정수로 반올림해 Point 필터와 정확히 맞춤
                float2 ts = _MainTex_TexelSize.xy * round(_OutlineWidth);
                float  nb = 0;

                nb = max(nb, tex2D(_MainTex, i.uv + float2(-1, -1) * ts).a);
                nb = max(nb, tex2D(_MainTex, i.uv + float2( 0, -1) * ts).a);
                nb = max(nb, tex2D(_MainTex, i.uv + float2( 1, -1) * ts).a);
                nb = max(nb, tex2D(_MainTex, i.uv + float2(-1,  0) * ts).a);
                nb = max(nb, tex2D(_MainTex, i.uv + float2( 1,  0) * ts).a);
                nb = max(nb, tex2D(_MainTex, i.uv + float2(-1,  1) * ts).a);
                nb = max(nb, tex2D(_MainTex, i.uv + float2( 0,  1) * ts).a);
                nb = max(nb, tex2D(_MainTex, i.uv + float2( 1,  1) * ts).a);

                if (nb > 0.5)
                    return fixed4(_OutlineColor.rgb, _OutlineColor.a * _OutlineAlpha);

                return fixed4(0, 0, 0, 0);
            }
            ENDCG
        }
    }
}
