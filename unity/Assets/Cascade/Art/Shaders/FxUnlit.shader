Shader "Cascade/FxUnlit"
{
    // Unlit translucent (or opaque) effect material: soft contact shadow, selection halo, ripple, swap arrow,
    // bomb hole. Colour = _Color * _MainTex * vertex colour (alpha ramps on the ring meshes give soft edges).
    Properties
    {
        [MainColor] _Color ("Color", Color) = (1, 1, 1, 1)
        [NoScaleOffset] _MainTex ("Texture", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
        [Toggle] _ZWrite ("ZWrite", Float) = 0
        _OffsetFactor ("Depth offset factor", Float) = -1
        _OffsetUnits ("Depth offset units", Float) = -1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "Fx"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull Off
            Offset [_OffsetFactor], [_OffsetUnits]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Assets/Cascade/Art/Shaders/CascadeLighting.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _SrcBlend;
                float _DstBlend;
                float _ZWrite;
                float _OffsetFactor;
                float _OffsetUnits;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; float3 clipXYW : TEXCOORD1; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.clipXYW = CascadeClipXYW(positionWS);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                CascadeClipTest(IN.clipXYW);
                return half4(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * _Color * IN.color);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
