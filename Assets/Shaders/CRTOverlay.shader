// @tags: crt, monitor, scanline, overlay, ui, market, retro, shader, vignette
//
// 마켓(주식·코인) 단말기 화면용 은은한 브라운관(CRT) 오버레이 UI 셰이더.
// 화면 콘텐츠를 grab하지 않고(=Screen Space Overlay 호환), 풀스크린 Image 위에
// 주사선 / 새도우마스크 / 비네팅 / 플리커를 "절차적"으로 그려 곱셈(Blend DstColor Zero)으로 덧입힌다.
// 곱셈이라 밝은 곳은 거의 그대로 두고 어두운 띠만 살짝 깔려, 가독성을 크게 해치지 않는다.
//
// 곡률/색수차처럼 뒤 콘텐츠 샘플링이 필요한 효과는 카메라+RenderTexture가 필요하므로 의도적으로 제외.
// (둥근 브라운관 느낌은 비네팅으로 대체)
Shader "UI/CRTOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _Intensity ("Effect Intensity", Range(0,1)) = 0.7

        [Header(Scanlines)]
        _ScanPeriod        ("Scanline Period (device px)", Float)   = 3
        _ScanlineDarkness  ("Scanline Darkness", Range(0,1))        = 0.10
        _ScanlineSharpness ("Scanline Sharpness", Range(0.1,4))     = 1.4

        [Header(Aperture Mask)]
        _MaskStrength ("Aperture Mask Strength", Range(0,0.3)) = 0.05
        _MaskPeriod   ("Aperture Triad (device px)", Float)    = 3

        [Header(Vignette)]
        _Vignette      ("Vignette", Range(0,1))        = 0.18
        _VignettePower ("Vignette Power", Range(0.5,6)) = 2.5

        [Header(Flicker)]
        _Flicker      ("Flicker Amount", Range(0,0.1)) = 0.015
        _FlickerSpeed ("Flicker Speed (Hz)", Float)    = 8

        [Header(Roll Band (off when speed 0))]
        _RollSpeed    ("Roll Speed", Float)            = 0
        _RollDarkness ("Roll Darkness", Range(0,0.5))  = 0.06
        _RollHeight   ("Roll Height", Range(0.01,0.5)) = 0.08

        // --- 표준 UI 플러밍 (uGUI / 마스크 호환) ---
        _StencilComp      ("Stencil Comparison", Float) = 8
        _Stencil          ("Stencil ID", Float)         = 0
        _StencilOp        ("Stencil Operation", Float)  = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask  ("Stencil Read Mask", Float)  = 255
        _ColorMask        ("Color Mask", Float)         = 15

        [Toggle(UNITY_UI_CLIP_RECT)] _UseUIClipRect ("Use Clip Rect", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue"             = "Overlay"
            "IgnoreProjector"   = "True"
            "RenderType"        = "Transparent"
            "PreviewType"       = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref       [_Stencil]
            Comp      [_StencilComp]
            Pass      [_StencilOp]
            ReadMask  [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        ColorMask [_ColorMask]
        Blend DstColor Zero          // 곱셈: dst = dst * src.rgb (어둡게만, 절대 워시아웃 안 됨)

        Pass
        {
            Name "CRT"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float4 screenPos     : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float4 _ClipRect;

            float _Intensity;
            float _ScanPeriod, _ScanlineDarkness, _ScanlineSharpness;
            float _MaskStrength, _MaskPeriod;
            float _Vignette, _VignettePower;
            float _Flicker, _FlickerSpeed;
            float _RollSpeed, _RollDarkness, _RollHeight;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex   = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.screenPos = ComputeScreenPos(OUT.vertex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // 디바이스 픽셀 좌표 (해상도 무관하게 주사선 간격을 px 단위로 고정)
                float2 px = IN.screenPos.xy / max(IN.screenPos.w, 1e-5) * _ScreenParams.xy;

                // --- 주사선: device px 기준 사인 (가운데 밝음=1, 골=0) ---
                float sp   = max(_ScanPeriod, 1.0);
                float s    = 0.5 + 0.5 * cos(px.y * (6.2831853 / sp));
                float scan = lerp(1.0 - _ScanlineDarkness, 1.0, pow(saturate(s), _ScanlineSharpness));

                // --- 새도우(어퍼처) 마스크: 세로 R/G/B 삼색 줄무늬 ---
                float  mp  = max(_MaskPeriod, 1.0);
                float  m   = frac(px.x / mp) * 3.0;                       // 0..3
                float3 sub = float3(step(m, 1.0),
                                    step(1.0, m) * step(m, 2.0),
                                    step(2.0, m));                        // 현재 컬럼이 강조하는 채널
                float3 mask = 1.0 - _MaskStrength * (1.0 - sub);          // 나머지 두 채널만 살짝 감쇠

                // --- 비네팅: 오버레이 rect(uv) 기준 → 모니터 가장자리만 어둑 ---
                float2 vu  = IN.texcoord - 0.5;
                float  r   = saturate(length(vu) * 1.41421356);
                float  vig = 1.0 - _Vignette * pow(r, _VignettePower);

                // --- 플리커: 시간 기반 미세 밝기 진동 (_Time는 UI에도 공급됨) ---
                float fl = 1.0 - _Flicker * (0.5 + 0.5 * sin(_Time.y * _FlickerSpeed * 6.2831853));

                // --- 롤 밴드: 천천히 아래로 흐르는 어두운 띠 (기본 비활성: speed 0) ---
                float roll = 1.0;
                if (abs(_RollSpeed) > 0.0001)
                {
                    float rp   = frac(IN.texcoord.y - _Time.y * _RollSpeed);
                    float band = smoothstep(0.0, _RollHeight, rp)
                               * (1.0 - smoothstep(_RollHeight, _RollHeight * 2.0, rp));
                    roll = 1.0 - _RollDarkness * band;
                }

                float3 crt = mask * (scan * vig * fl * roll);
                // Intensity 0 → 효과 전무(흰색 곱). 편차를 강도로 스케일.
                crt = lerp(float3(1, 1, 1), crt, saturate(_Intensity));

                fixed4 col = IN.color;
                col.rgb *= crt;
                col.rgb *= tex2D(_MainTex, IN.texcoord).rgb;   // 선택적 스프라이트/텍스처(기본 white=무영향)

                #ifdef UNITY_UI_CLIP_RECT
                // 곱셈 블렌드라 클립 밖은 alpha가 아니라 rgb를 흰색(=무영향)으로 보내야 한다.
                float clip = UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                col.rgb = lerp(float3(1, 1, 1), col.rgb, clip);
                #endif

                col.a = 1.0;   // 곱셈 블렌드는 alpha를 무시하지만 정의는 해 둔다
                return col;
            }
        ENDCG
        }
    }
}
