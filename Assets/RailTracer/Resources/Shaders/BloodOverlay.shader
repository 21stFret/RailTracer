// Blood soaking over a character's own materials. Added as an extra material on each renderer, it draws the
// mesh again and MULTIPLIES a blood pattern onto what's already there, so the lighting underneath is kept.
// _Amount 0 = clean, 1 = soaked; the pattern grows from the darkest noise spots outward (UV space, so it
// sticks to the skin while the character animates).
Shader "RailTracer/BloodOverlay"
{
    Properties
    {
        _BloodColor ("Blood (multiplied)", Color) = (0.45, 0.03, 0.03, 1)
        _Amount ("Amount", Range(0, 1)) = 0
        _Scale ("Pattern scale", Float) = 6
        _Edge ("Edge softness", Range(0.001, 0.3)) = 0.06
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "BloodOverlay"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend DstColor Zero // multiply
            ZWrite Off
            ZTest LEqual
            Offset -1, -1       // same surface as the base pass: win the depth test
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BloodColor;
                half _Amount;
                float _Scale;
                half _Edge;
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

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * _Scale;
                float n = Noise(p) * 0.6 + Noise(p * 2.7 + 7.3) * 0.3 + Noise(p * 7.1 + 3.1) * 0.1;
                float threshold = 1.0 - _Amount;
                half mask = smoothstep(threshold - _Edge, threshold + _Edge, n) * step(0.001, _Amount);
                return half4(lerp(half3(1, 1, 1), _BloodColor.rgb, mask), 1);
            }
            ENDHLSL
        }
    }
}
