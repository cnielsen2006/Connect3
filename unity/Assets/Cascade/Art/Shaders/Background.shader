Shader "Cascade/Background"
{
    // Pale-sand vertical gradient painted at the far plane. Draw it with a quad whose renderer bounds are huge
    // (StyleApplier does); vertices are written straight to clip space, so it fills any camera.
    Properties
    {
        _TopColor ("Top", Color) = (0.937, 0.91, 0.859, 1)
        _BottomColor ("Bottom", Color) = (0.875, 0.831, 0.757, 1)
        _Vignette ("Vignette", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Background" }
        ZWrite Off ZTest LEqual Cull Off

        Pass
        {
            Name "Backdrop"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _TopColor;
                float4 _BottomColor;
                float _Vignette;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float t : TEXCOORD0; float2 xy : TEXCOORD1; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float2 xy = IN.positionOS.xz * 2.0;   // the unit XZ quad spans -0.5..0.5
                OUT.positionCS = float4(xy.x, xy.y, UNITY_RAW_FAR_CLIP_VALUE, 1.0);
                OUT.xy = xy;
                OUT.t = saturate(xy.y * _ProjectionParams.x * 0.5 + 0.5);   // 1 at the top of the image
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 c = lerp(_BottomColor.rgb, _TopColor.rgb, IN.t);
                c *= 1.0 - _Vignette * smoothstep(0.35, 1.25, length(IN.xy));   // the spotlight cone of the studio set
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
