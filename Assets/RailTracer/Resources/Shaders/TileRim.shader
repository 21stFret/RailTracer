// Grid overlay: only the rim of each tile, inset from the edge so neighbouring tiles' rims never touch.
// The rim is crisp on its inner side (toward the middle) and fades out toward the tile's edge.
// The interior is fully see-through, so the carriage stays visible. Colour and alpha come from _Color
// (Game.cs sets it per tile); _Strength boosts the alpha so thin rims still read.
Shader "RailTracer/TileRim"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 1, 1, 0)
        _Inset ("Inset from the tile edge (0..0.5 of the tile)", Range(0, 0.4)) = 0.03
        _Width ("Rim width, fade included (of the tile)", Range(0.005, 0.25)) = 0.1
        _Soft ("Inner edge softness (antialiasing)", Range(0.001, 0.05)) = 0.006
        _Strength ("Alpha boost", Range(1, 4)) = 2
        _Opacity ("Opacity at the inner edge (the fade starts here)", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "TileRim"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Inset, _Width, _Soft, _Strength, _Opacity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 d = min(i.uv, 1 - i.uv);   // distance to the nearest edge, per axis
                float edge = min(d.x, d.y);
                // Inside the band [_Inset, _Inset + _Width] from the edge: full strength at its inner side, cut off
                // sharply there, fading to nothing at the outer side.
                half fade = saturate((edge - _Inset) / _Width);
                half outer = fade * fade;
                half inner = 1 - smoothstep(_Inset + _Width - _Soft, _Inset + _Width + _Soft, edge);
                return half4(_Color.rgb, saturate(_Color.a * _Strength) * _Opacity * outer * inner);
            }
            ENDHLSL
        }
    }
}
