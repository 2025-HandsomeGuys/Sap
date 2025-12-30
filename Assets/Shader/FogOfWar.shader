Shader "Custom/FogOfWar"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Fog Color", Color) = (0, 0, 0, 1)
        _Radius ("Radius", Float) = 0.3 // Normalized screen space radius (0 to 0.5)
        _Softness ("Softness", Float) = 5// Normalized screen space softness
        _FogYLimit ("Fog Y Limit", Float) = 1.0 // Screen space Y limit (0 to 1), pixels above this are clear
        _FogDarkness ("Fog Darkness", Range(0, 1)) = 0.8 // 0: Invisible (Black), 1: No Fog (Bright)
        _PlayerDir ("Player Direction", Vector) = (1, 0, 0, 0) // Direction toward mouse
        _SightAngle ("Sight Angle (Cos)", Range(-1, 1)) = 0.5 // Cone width
        _SightDistance ("Sight Distance", Float) = 0.5 // Range of the flashlight
        _FlashlightSoftness ("Flashlight Softness", Float) = 0.1 // Softness of the flashlight edge
        _PlayerScreenPos ("Player Screen Position", Vector) = (0.5, 0.5, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100

        // 스텐실 설정 추가: 1이 아닌 곳만 안개를 그림
        Stencil
        {
            Ref 1
            Comp NotEqual
            Pass Keep
        }

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
            float _FogYLimit;
            float _FogDarkness;
            float2 _PlayerDir;
            float _SightAngle;
            float _SightDistance;
            float _FlashlightSoftness;
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

                // Y 좌표 제한 확인
                if (i.uv.y > _FogYLimit) return originalColor;

                // 1. 거리 계산
                float2 playerPos = _PlayerScreenPos.xy;
                float2 diff = i.uv - playerPos;
                
                // 화면 비율 보정 (각도 계산용)
                float aspectRatio = _ScreenParams.x / _ScreenParams.y;
                
                // 거리 계산용 보정 (왜곡된 상수 *2, /0.5 제거하고 정석대로 수정)
                float2 diffAdjusted = diff;
                diffAdjusted.x = diff.x * aspectRatio;
                diffAdjusted.y = diff.y;
                float dist = length(diffAdjusted);

                // 2. 각도 계산 (손전등 효과)
                float2 dirToPixel = diff;
                dirToPixel.x *= aspectRatio; 
                dirToPixel = normalize(dirToPixel);
                
                float2 playerDir = normalize(_PlayerDir);
                float dotVal = dot(dirToPixel, playerDir);
                
                float angleVisibility = smoothstep(_SightAngle, _SightAngle + 0.1, dotVal);

                // 3. 거리 가시성 (손전등 전용 거리 감쇄)
                // _FlashlightSoftness를 사용하여 부드러움 조절
                float coneStart = max(0, _SightDistance - _FlashlightSoftness);
                float coneDistVisibility = 1.0 - smoothstep(coneStart, _SightDistance, dist);
                
                // 4. 기본 원형 시야 (플레이어 주변)
                // _Radius를 기준으로 감쇄 처리
                float baseCircleVisibility = 1.0 - smoothstep(_Radius, _Radius + _Softness, dist);

                // 5. 최종 가시성 결합
                // 손전등 효과: 거리 내에 있고(AND) 각도 내에 있어야(AND) 보임
                float cone = coneDistVisibility * angleVisibility;
                
                // 최종 결과 = 기본 원형 시야 OR 손전등 시야
                float visibility = max(baseCircleVisibility, cone);

                // 밝기 계산
                float brightness = lerp(1.0 - _FogDarkness, 1.0, visibility);
                return fixed4(originalColor.rgb * brightness, originalColor.a);
            }
            ENDCG
        }
    }
}