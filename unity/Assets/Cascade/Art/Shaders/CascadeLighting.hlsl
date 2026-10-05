#ifndef CASCADE_LIGHTING_INCLUDED
#define CASCADE_LIGHTING_INCLUDED

// The K: Pebbles light rig (overcast sky + low warm sun), published by PieceStyle.ApplyLightGlobals().
// All maths is done in display ("gamma") space like the prototype so the look is identical in a Gamma or a
// Linear colour-space project; CascadeFromInput/CascadeToOutput convert at the edges when needed.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    float4 _CascadeAmbient;   // flat ambient
    float4 _CascadeSky;       // soft light straight from above
    float4 _CascadeSunColor;
    float4 _CascadeSunSpec;
    float4 _CascadeSunDir;    // xyz: unit vector pointing towards the sun (world space)

// Board clip: the board is only drawn inside the layout's BoardSlot (so a zoomed board never runs under the HUD text).
// _CascadeClip is the slot as a rectangle in the camera's logical NDC (xmin, ymin, xmax, ymax, y up); _CascadeClipVP is the
// camera's logical (not GPU-flipped) view-projection matrix, set by BoardCameraRig every frame. Both are globals, so they cost no
// per-material data and keep the SRP Batcher happy. The default rectangle covers the whole screen.
float4 _CascadeClip;
float4x4 _CascadeClipVP;

float3 CascadeClipXYW(float3 positionWS)
{
    float4 c = mul(_CascadeClipVP, float4(positionWS, 1.0));
    return float3(c.x, c.y, c.w);
}

void CascadeClipTest(float3 clipXYW)
{
    float2 ndc = clipXYW.xy / clipXYW.z;
    clip(float4(ndc - _CascadeClip.xy, _CascadeClip.zw - ndc));
}

float3 CascadeFromInput(float3 c)
{
#if defined(UNITY_COLORSPACE_GAMMA)
    return c;
#else
    return pow(max(c, 0.0001), 1.0 / 2.2);
#endif
}

float3 CascadeToOutput(float3 c)
{
#if defined(UNITY_COLORSPACE_GAMMA)
    return c;
#else
    return pow(max(c, 0.0), 2.2);
#endif
}

// albedo in display space; returns display-space colour.
float3 CascadeShade(float3 albedo, float3 N, float3 V, float sheen, float shininess, float rimStrength, float rimPower)
{
    float3 L = _CascadeSunDir.xyz;
    float ndl = saturate(dot(N, L));
    float up = saturate(N.y);
    float3 light = _CascadeAmbient.rgb + _CascadeSky.rgb * up + _CascadeSunColor.rgb * ndl;
    float3 col = albedo * light;

    // worn-clay sheen: a faint broad sky highlight on the top face plus a soft sun glint
    float3 H = normalize(L + V);
    float glint = pow(saturate(dot(N, H)), shininess);
    col += _CascadeSunSpec.rgb * (glint * sheen);
    col += (_CascadeAmbient.rgb + _CascadeSky.rgb) * (pow(up, 5.0) * sheen * 0.8);

    // soft rim light
    float fres = pow(1.0 - saturate(dot(N, V)), rimPower);
    col += lerp(albedo, float3(1.0, 1.0, 1.0), 0.35) * (_CascadeAmbient.rgb + _CascadeSky.rgb) * (fres * rimStrength);
    return col;
}

#endif
