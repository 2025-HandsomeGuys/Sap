Shader "Custom/PixelationShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        // ★ [핵심] 픽셀의 밀도를 결정합니다. 값이 클수록 도트가 촘촘해집니다.
        _PixelSize ("Pixel Density", Float) = 50000
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha // 투명도 적용
        
        // ★ [추가됨] 면이 뒤집혀도 렌더링되도록 뒷면 자르기(Culling)를 끕니다.
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR; // 스프라이트 렌더러의 Color(알파 포함) 값을 받기 위함
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _PixelSize;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color; // 컬러 전달
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // uv 좌표를 픽셀화 단위로 끊어줍니다. (핵심 로직)
                float2 pixelatedUV = round(i.uv * _PixelSize) / _PixelSize;
                
                // 픽셀화된 UV로 텍스처에서 색상을 가져옵니다.
                fixed4 col = tex2D(_MainTex, pixelatedUV);
                
                // 스프라이트 렌더러의 알파값이나 색상을 곱해줍니다.
                col *= i.color;
                
                return col;
            }
            ENDCG
        }
    }
}