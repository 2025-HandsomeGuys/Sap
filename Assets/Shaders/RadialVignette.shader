Shader "Custom/RadialVignette"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _VisionRadius ("Vision Radius", Float) = 10.0
        _FalloffRange ("Falloff Range", Float) = 3.0
        _DarknessAlpha ("Darkness Alpha", Range(0, 1)) = 0.3
        _DarknessColor ("Darkness Color", Color) = (0, 0, 0, 1)
        _PlayerPosition ("Player Position (World)", Vector) = (0, 0, 0, 0)
        _CameraPosition ("Camera Position", Vector) = (0, 0, 0, 0)
        _CameraSize ("Camera Orthographic Size", Float) = 5.0
        
        // 스텐실 및 듀얼 패스 옵션
        _Stencil ("Stencil ID", Float) = 1
        _StencilComp ("Stencil Comparison", Float) = 8
        _StencilOp ("Stencil Operation", Float) = 0
        _UseCone ("Use Cone SDF", Float) = 0
    }
    
    SubShader
    {
        Tags 
        { 
            "Queue"="Transparent" 
            "IgnoreProjector"="True" 
            "RenderType"="Transparent" 
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Stencil
            {
                Ref [_Stencil]
                Comp [_StencilComp]
                Pass [_StencilOp]
            }

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
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 worldPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _VisionRadius;
            float _FalloffRange;
            float _DarknessAlpha;
            float4 _DarknessColor;
            float4 _PlayerPosition;
            float4 _CameraPosition;
            float _CameraSize;
            float _UseCone;
            
            // 손전등 글로벌 변수
            float3 _FlashlightDirection;
            float _FlashlightDistance;
            float _FlashlightInnerRadius;
            float _FlashlightAngle;
            float _FlashlightActive;

            // 두 SDF 형태를 부드럽게 결합하는 Smin 함수 (다항식 방식)
            float smin(float a, float b, float k)
            {
                float h = clamp(0.5 + 0.5 * (b - a) / k, 0.0, 1.0);
                return lerp(b, a, h) - k * h * (1.0 - h);
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                // WorldSpace Canvas이므로 월드 좌표 계산
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 현재 픽셀의 월드 좌표
                float2 currentPos = i.worldPos.xy;
                
                // 플레이어 위치 (월드 좌표)
                float2 playerPos = _PlayerPosition.xy;

                // 플레이어로부터의 거리 계산 (월드 단위)
                float pixelDist = length(currentPos - playerPos);

                // 1. 원형 시야 SDF 계산 (결과가 0 이하면 시야 내부)
                float circle_SDF = pixelDist - _VisionRadius;
                
                // 기본 형태는 원형 시야 하나
                float combined_SDF = circle_SDF;
                
                // 2. _UseCone이 활성화되어 있고 손전등이 켜져있을 경우에만 결합 (Raycast 장애물 처리는 스텐실이 담당)
                if (_UseCone > 0.5 && _FlashlightActive > 0.5 && pixelDist > 0.01)
                {
                    float2 flashDir = _FlashlightDirection.xy;
                    
                    // 손전등 시작점을 플레이어 중심보다 약간만 뒤로 뺌
                    // (원뿔의 꼭짓점이 플레이어 위치가 되도록 하여 각도 계산의 정확도 향상)
                    float2 flashlightOrigin = playerPos; 
                    
                    float2 toPixel = currentPos - flashlightOrigin;
                    float pixelDistFromFlashlight = length(toPixel);
                    float2 pixelDir = toPixel / max(pixelDistFromFlashlight, 0.0001);
                    
                    // 손전등 방향과의 각도 계산 (dot product)
                    float dotProduct = dot(pixelDir, flashDir);
                    float angleToPixel = acos(clamp(dotProduct, -1.0, 1.0)); // 0 ~ PI
                    float halfConeAngle = _FlashlightAngle * 0.5 * 3.14159 / 180.0;
                    
                    // 원뿔의 측면 가장자리까지의 각도 기반 거리(SDF) 근사치 계산
                    float cone_SDF = (angleToPixel - halfConeAngle) * pixelDistFromFlashlight;
                    
                    // 1) 외경(끝자락) 제한 (원거리 페이드)
                    float end_SDF = pixelDistFromFlashlight - (_FlashlightDistance - _FalloffRange);
                    
                    // 2) 내경(시작지점) 제한 (플레이어 근처 페이드 - 도넛형)
                    // 내경 근처에서 부드럽게 시작하기 위해 SDF 반전 사용
                    float start_SDF = _FlashlightInnerRadius - pixelDistFromFlashlight;
                    
                    // 차집합/교집합 연산: 외경 안쪽이고 내경 바깥쪽이며 각도 안쪽인 영역
                    // max(a, b)는 두 SDF의 교집합 효과(둘 다 만족해야 0 이하)
                    float cone_donut_SDF = max(cone_SDF, max(end_SDF, start_SDF));
                    
                    // 합집합: 기존 원형 시야와 손전등 시야를 smin으로 물방울처럼 완벽하게 부드럽게 결합
                    // blendWidth를 조절하여 원형 시야와 손전등이 만나는 지점의 부드러움 결정
                    float blendWidth = max(_VisionRadius * 0.3, 0.5); 
                    
                    // 각도 페이드: 스텐실 마스크 경계면에서의 계단 현상 방지
                    float angleFade = smoothstep(halfConeAngle, halfConeAngle * 0.8, angleToPixel);
                    
                    float sminResult = smin(circle_SDF, cone_donut_SDF, blendWidth);
                    
                    // 3) 조건부 전체 사거리 감쇠 (1/3 지점부터 0%까지)
                    // 전체 거리(내경~외경) 중 현재 위치의 비율 (0~1)
                    float distRange = max(0.001, _FlashlightDistance - _FlashlightInnerRadius);
                    float dRatio = saturate((pixelDistFromFlashlight - _FlashlightInnerRadius) / distRange);
                    
                    // 1/3 지점(0.33)부터 감쇠 시작, 1.0 지점에서 완전 어둠(0%)
                    float fadeStart = 0.333;
                    float fadeRatio = saturate((dRatio - fadeStart) / (1.0 - fadeStart));
                    
                    // 감쇠 계수 계산 (smoothstep을 사용하여 더 부드럽게 전환)
                    float darknessWeight = smoothstep(0.0, 1.0, fadeRatio);
                    
                    // 감쇠를 SDF에 반영: 가중치에 따라 점진적으로 어둡게 처리
                    // _FalloffRange를 충분히 더해 어둠으로 완전히 전환되도록 함
                    sminResult += darknessWeight * _FalloffRange * 2.0;

                    // 각도에 따라 smin 결과(밝음)에서 circle_SDF(어두움)으로 부드럽게 전환
                    combined_SDF = lerp(circle_SDF, sminResult, angleFade);
                }
                
                // 3. 부드러운 테두리(Falloff) 적용
                float finalVignette = 1.0;
                
                if (_FalloffRange > 0.001)
                {
                    // SDF가 0 이하이면 0(완전 밝음), _FalloffRange 이상이면 1(완전 어두움)
                    float t = clamp(combined_SDF / _FalloffRange, 0.0, 1.0);
                    finalVignette = smoothstep(0.0, 1.0, t);
                }
                else // Falloff가 없을 경우 (하드 엣지)
                {
                    finalVignette = combined_SDF > 0.0 ? 1.0 : 0.0;
                }

                // 최종 색상 계산 및 적용
                fixed4 darknessCol = _DarknessColor;
                darknessCol.a = finalVignette * _DarknessAlpha;

                return darknessCol;
            }
            ENDCG
        }
    }
}
