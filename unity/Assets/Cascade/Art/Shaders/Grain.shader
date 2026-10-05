Shader "Cascade/Grain"
{
    // Full-screen film grain, used by a FullScreenPassRendererFeature on the URP renderer (Unity 6 RenderGraph).
    // Port of the prototype overlay: grey noise in [_GrainMin, _GrainMax] blended over the frame at _GrainAlpha.
    Properties
    {
        _GrainAlpha ("Grain Alpha (grain_alpha)", Range(0, 1)) = 0.16
        _GrainMin ("Noise min", Range(0, 1)) = 0.3
        _GrainMax ("Noise max", Range(0, 1)) = 0.7
        _GrainSize ("Grain size (pixels)", Range(1, 8)) = 1
        [Toggle] _GrainAnimate ("Animate", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "Grain"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _GrainAlpha;
                float _GrainMin;
                float _GrainMax;
                float _GrainSize;
                float _GrainAnimate;
            CBUFFER_END

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float2 px = floor(input.positionCS.xy / max(1.0, _GrainSize));
                px += _GrainAnimate * floor(_Time.y * 24.0) * 17.0;
                float g = lerp(_GrainMin, _GrainMax, Hash12(px));
            #if !defined(UNITY_COLORSPACE_GAMMA)
                g = pow(g, 2.2);
            #endif
                return half4(lerp(src.rgb, float3(g, g, g), _GrainAlpha), src.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
