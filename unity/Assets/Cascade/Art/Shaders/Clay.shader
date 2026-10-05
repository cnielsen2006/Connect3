Shader "Cascade/Clay"
{
    Properties
    {
        [MainColor] _BaseColor ("Base Color", Color) = (0.8, 0.7, 0.55, 1)
        _Brightness ("Brightness", Range(0, 2)) = 1
        [NoScaleOffset] _FleckTex ("Fleck (R dark, G light)", 2D) = "white" {}
        _FleckParams ("Fleck offset XY, dark strength, light strength", Vector) = (0, 0, 0.55, 0.45)
        _Sheen ("Sheen", Range(0, 1)) = 0.1
        _Shininess ("Shininess", Range(1, 128)) = 18
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.1
        _RimPower ("Rim Power", Range(0.5, 8)) = 3
        [Toggle(_BANDS)] _UseBands ("Banded agate (colour from _BandTex by UV.x)", Float) = 0
        [NoScaleOffset] _BandTex ("Band ramp", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _BANDS
            #include "Assets/Cascade/Art/Shaders/CascadeLighting.hlsl"

            TEXTURE2D(_FleckTex); SAMPLER(sampler_FleckTex);
            TEXTURE2D(_BandTex);  SAMPLER(sampler_BandTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Brightness;
                float4 _FleckParams;
                float _Sheen;
                float _Shininess;
                float _RimStrength;
                float _RimPower;
                float _UseBands;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 color : COLOR;
                float3 clipXYW : TEXCOORD3;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                OUT.clipXYW = CascadeClipXYW(OUT.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                CascadeClipTest(IN.clipXYW);
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetCameraPositionWS() - IN.positionWS);
            #if defined(_BANDS)
                float3 albedo = CascadeFromInput(SAMPLE_TEXTURE2D(_BandTex, sampler_BandTex, float2(IN.uv.x, 0.5)).rgb);
            #else
                float3 albedo = CascadeFromInput(_BaseColor.rgb);
            #endif
                albedo *= _Brightness * IN.color.rgb;
                float4 fl = SAMPLE_TEXTURE2D(_FleckTex, sampler_FleckTex, IN.uv + _FleckParams.xy);
                albedo *= lerp(1.0, fl.r, _FleckParams.z);
                albedo = lerp(albedo, float3(1.0, 1.0, 1.0), fl.g * _FleckParams.w);
                float3 col = CascadeShade(albedo, N, V, _Sheen, _Shininess, _RimStrength, _RimPower);
                return half4(CascadeToOutput(col), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Brightness;
                float4 _FleckParams;
                float _Sheen;
                float _Shininess;
                float _RimStrength;
                float _RimPower;
                float _UseBands;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }
            half frag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
