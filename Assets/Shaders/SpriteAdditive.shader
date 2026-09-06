// 스프라이트용 가산 합성 셰이더.
// Blend One One 이므로 알파는 "투명도"가 아니라 "밝기"로 쓰인다 (frag에서 rgb에 곱한다).
// 어두운 지하에서 빛기둥·먼지 같은 발광 연출에 쓴다.
//
// Shader.Find("Custom/SpriteAdditive") 로 해석되므로, 빌드에 포함시키려면
// 사용하는 컴포넌트의 인스펙터 슬롯에 직접 연결하거나
// Project Settings > Graphics > Always Included Shaders 에 추가할 것.
Shader "Custom/SpriteAdditive"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Blend One One
        Cull Off
        Lighting Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _Color;

            v2f vert (appdata_t IN)
            {
                v2f OUT;
                OUT.vertex   = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color    = IN.color * _Color;
                return OUT;
            }

            fixed4 frag (v2f IN) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, IN.texcoord) * IN.color;
                // 가산 블렌딩은 알파를 무시하므로 직접 곱해 "알파 = 밝기"로 만든다.
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }

    // 셰이더를 못 찾는 환경에서도 스프라이트가 분홍색으로 깨지지 않게.
    Fallback "Sprites/Default"
}
