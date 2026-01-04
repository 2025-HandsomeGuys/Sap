Shader "Custom/TileSpotlight"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
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

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float3 worldPos : TEXCOORD1; // Check custom
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _MainTex_ST; // Tiling/Offset

            // Global Properties
            float4 _PlayerWorldPos;
            float _SightRadiusWorld;
            float _SightSoftnessWorld;
            
            // Flashlight Properties
            float2 _PlayerWorldDir;        // 플레이어가 바라보는 방향 (월드 좌표계, normalized)
            float _FlashlightAngle;        // 손전등 각도 (도 단위)
            float _FlashlightDistance;     // 손전등 거리
            float _FlashlightBrightness;   // 손전등 밝기
            float _FlashlightSoftness;     // 손전등 부드러움

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = TRANSFORM_TEX(IN.texcoord, _MainTex);
                OUT.color = IN.color * _Color; // Vertex Color * Tint
                OUT.worldPos = mul(unity_ObjectToWorld, IN.vertex).xyz;
                
                #ifdef PIXELSNAP_ON
                OUT.vertex = UnityPixelSnap (OUT.vertex);
                #endif

                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, IN.texcoord) * IN.color;

                // Circular Spotlight Logic
                // We typically assume 2D game on XY plane
                float dist = distance(IN.worldPos.xy, _PlayerWorldPos.xy);
                
                // Visibility: 1 (Near) -> 0 (Far)
                // Use default values if globals are not set (e.g. 0)
                float radius = _SightRadiusWorld > 0 ? _SightRadiusWorld : 5.0; 
                float softness = _SightSoftnessWorld > 0 ? _SightSoftnessWorld : 1.0;
                
                float spotlight = 1.0 - smoothstep(radius, radius + softness, dist);
                
                // Flashlight (Triangle/Cone) Logic
                float flashlight = 0.0;
                
                // 조건 체크
                float dirLength = length(_PlayerWorldDir.xy);
                bool condition1 = _FlashlightDistance > 0;
                bool condition2 = _FlashlightBrightness > 0;
                bool condition3 = dirLength > 0.001;
                
                if (condition1 && condition2 && condition3)
                {
                    // 플레이어에서 타일로의 방향 벡터 계산
                    float2 dirFromPlayer = IN.worldPos.xy - _PlayerWorldPos.xy;
                    float distToTile = length(dirFromPlayer);
                    
                    // 0으로 나누기 방지
                    if (distToTile > 0.001)
                    {
                        dirFromPlayer = normalize(dirFromPlayer);
                        float2 playerDir = normalize(_PlayerWorldDir.xy);
                        
                        // 내적을 사용하여 각도 체크
                        float dotVal = dot(dirFromPlayer, playerDir);
                        
                        // 각도 체크 (cos 값으로 비교)
                        // _FlashlightAngle은 도 단위이므로 반각을 라디안으로 변환
                        float halfAngleRad = radians(_FlashlightAngle * 0.5);
                        float angleThreshold = cos(halfAngleRad);
                        
                        // dotVal이 angleThreshold 이상이면 손전등 범위 내
                        // dotVal이 1에 가까울수록 (같은 방향) 더 밝음
                        float angleSoftness = 0.15; // 각도 가장자리 부드러움
                        
                        // dotVal >= angleThreshold이면 확실히 1
                        // dotVal < angleThreshold이면 부드러운 전환
                        float angleVisibility;
                        if (dotVal >= angleThreshold)
                        {
                            angleVisibility = 1.0;
                        }
                        else
                        {
                            // 부드러운 가장자리: angleThreshold - softness부터 angleThreshold까지
                            angleVisibility = smoothstep(angleThreshold - angleSoftness, angleThreshold, dotVal);
                        }
                        
                        // 거리 체크
                        float distStart = max(0.0, _FlashlightDistance - _FlashlightSoftness);
                        float distVisibility = 1.0 - smoothstep(distStart, _FlashlightDistance, distToTile);
                        
                        // 삼각형 손전등 최종 값
                        flashlight = angleVisibility * distVisibility * _FlashlightBrightness;
                    }
                }
                
                // 원형 spotlight와 삼각형 손전등 결합
                float finalLight = max(spotlight, flashlight);
                
                // Apply lighting
                c.rgb *= finalLight;
                
                // Optional: Fade out alpha too?
                // c.a *= finalLight; 
                
                c.rgb *= c.a; // Premultiplied Alpha for One OneMinusSrcAlpha
                return c;
            }
        ENDCG
        }
    }
}
