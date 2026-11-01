// Ghosts (the move-preview landing and the last-seen spot): a see-through outline of the figure. Only the
// silhouette's rim shows, brightest where the surface turns away from the camera; the middle is clear.
Shader "RailTracer/GhostOutline"
{
    Properties
    {
        _Color ("Colour (alpha = strength)", Color) = (0.95, 0.94, 0.92, 0.8)
        _Power ("Rim tightness", Range(0.5, 8)) = 3
        _Fill ("Faint fill inside the rim", Range(0, 0.3)) = 0.04
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "GhostOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Power, _Fill;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(posWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewWS = GetWorldSpaceViewDir(posWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half facing = saturate(dot(normalize(i.normalWS), normalize(i.viewWS)));
                half rim = pow(1 - facing, _Power);
                return half4(_Color.rgb, _Color.a * saturate(rim + _Fill));
            }
            ENDHLSL
        }
    }
}
