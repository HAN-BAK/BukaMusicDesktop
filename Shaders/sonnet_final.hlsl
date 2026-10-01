// Desktop twin of the Android LyricsGlView FINAL_FRAGMENT shader.
//
// Compiled with fxc twice (lib_4_0_ps_only -> ps_4_0, /D D2D_FUNCTION then
// /D D2D_FULL_SHADER, see Shaders/compile.ps1) so that Direct2D can link it as
// a custom image effect. The math is a direct port: radial lens distortion,
// full-screen chromatic dispersion, vignette, film grain, halftone and
// contrast, with the same constants the Android layer passes in.

#define D2D_INPUT_COUNT 1
#define D2D_INPUT0_COMPLEX
#include "d2d1effecthelpers.hlsli"

// x = distortion, y = dispersion, z = vignette, w = grain
float4 uParams0;
// x = halftone, y = contrast, z = rgbShift, w = time
float4 uParams1;
// xy = uv scale (kept at 1: the scene is rendered at the screen's shape)
float4 uParams2;
// xy = half a texel: sampling is clamped to the middle of the border texels.
// Without this, Direct2D's transparent padding around an effect input leaks in
// wherever the lens distortion pushes the sample onto the image border, which
// crushes the corners towards black.
float4 uParams3;

float2 ClampUv(float2 value)
{
    return clamp(value, uParams3.xy, 1.0 - uParams3.xy);
}

D2D_PS_ENTRY(main)
{
    float4 uvInfo = D2DGetInputCoordinate(0);
    float2 uv = uvInfo.xy;
    float2 centered = uv - 0.5;
    float r2 = dot(centered, centered);

    float distortion = uParams0.x;
    float dispersion = uParams0.y;
    float vignette = uParams0.z;
    float grain = uParams0.w;
    float halftone = uParams1.x;
    float contrast = uParams1.y;
    float rgbShift = uParams1.z;
    float time = uParams1.w;

    float2 base = uv + centered * (r2 * distortion);
    float d = dispersion;
    float2 redUv = uv + float2(rgbShift, 0.0) + centered * (r2 * distortion + d);
    float2 blueUv = uv - float2(rgbShift, 0.0) + centered * (r2 * distortion - d);

    // CLAMP_TO_EDGE, like the GL texture wrap mode.
    float3 color;
    color.r = D2DSampleInput(0, ClampUv(redUv)).r;
    color.g = D2DSampleInput(0, ClampUv(base)).g;
    color.b = D2DSampleInput(0, ClampUv(blueUv)).b;

    float vig = smoothstep(0.95, 0.15, length(centered) * 1.35);
    color *= lerp(1.0, vig, vignette);

    float noise = frac(sin(dot(uv * 1024.0 + time, float2(12.9898, 78.233))) * 43758.5453);
    color += (noise - 0.5) * grain;

    float dots = sin(base.x * 1500.0) * sin(base.y * 1500.0);
    color = lerp(color, color * (0.82 + 0.18 * step(0.0, dots)), halftone);

    color = (color - 0.5) * (1.0 + contrast) + 0.5;
    return float4(saturate(color), 1.0);
}
