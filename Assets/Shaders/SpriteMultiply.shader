Shader "Custom/SpriteMultiply"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
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
        // Multiply Blend Mode: DstColor * SrcColor
        Blend DstColor Zero

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ PIXELSNAP_ON
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

            fixed4 _Color;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;
                #ifdef PIXELSNAP_ON
                OUT.vertex = UnityPixelSnap (OUT.vertex);
                #endif
                return OUT;
            }

            sampler2D _MainTex;
            sampler2D _AlphaTex;
            float _AlphaSplitEnabled;

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, IN.texcoord) * IN.color;
                // Using Multiply, we rely on RGB. 
                // White (1,1,1) * BG = BG
                // Black (0,0,0) * BG = Black
                // Transparent (0,0,0,0)?
                // If texture has alpha, we might need to be careful?
                // But Multiply blend ignores Alpha channel of SRC usually in this formula.
                // Wait, Blend DstColor Zero uses (R_src, G_src, B_src, A_src).
                // If Src is transparent (0,0,0,0), then RGB is 0 -> Result 0 (Black).
                // So "Transparent" implies "Black" in Multiply.
                // WE WANT "Transparent" to mean "No Effect" (White).
                // So clear color should be White.
                
                return c;
            }
            ENDCG
        }
    }
}
