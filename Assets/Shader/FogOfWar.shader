Shader "Custom/FogOfWar"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Fog Color", Color) = (0, 0, 0, 1)
        _Radius ("Radius", Float) = 0.3 // Normalized screen space radius (0 to 0.5)
        _Softness ("Softness", Float) = 0.15 // Normalized screen space softness
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

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
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float _Radius;
            float _Softness;
            float4 _PlayerScreenPos; // Player position in screen UV (0-1 range)

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 originalColor = tex2D(_MainTex, i.uv);

                // Calculate distance in screen UV space
                // UV 좌표는 (0,0)이 왼쪽 아래, (1,1)이 오른쪽 위입니다
                float2 playerPos = _PlayerScreenPos.xy;
                
                // 거리 벡터를 먼저 계산 (좌표 변환 대신 거리 벡터를 보정)
                float2 diff = i.uv - playerPos;
                
                // 화면 종횡비를 고려하여 보정
                // _ScreenParams.xy는 화면의 width, height입니다
                float aspectRatio = _ScreenParams.x / _ScreenParams.y;
                
                // 거리 벡터의 x, y 성분을 보정
                // 양옆을 좁히려면: diff.x를 크게 만들어야 함 (곱하기)
                // 위아래로 3배 더 길게: diff.y를 작게 만들어야 함 (나누기)
                diff.x = diff.x * aspectRatio * 2 ;  // x 방향 거리를 크게 (양옆 좁히기)
                diff.y = diff.y / 0.5;                 // y 방향 거리를 작게 (위아래 넓히기)
                
                // 보정된 거리 벡터의 길이 계산
                float dist = length(diff);

                // smoothstep for soft falloff
                // dist가 _Radius보다 작으면 1 (보임), _Radius + _Softness보다 크면 0 (안개로 가려짐)
                // 반대로 계산하여 주인공 주변만 보이도록 함
                float visibility = 1.0 - smoothstep(_Radius, _Radius + _Softness, dist);

                // 기본적으로 검은색 안개, 주인공 주변만 원래 색상이 보임
                return lerp(_Color, originalColor, visibility);
            }
            ENDCG
        }
    }
}