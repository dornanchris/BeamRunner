// BeamRunner — shared shader code.
//
// Every world shader includes this file. It declares the global properties written by
// BeamRunner.Rendering.FieldGlobals (docs/ARCHITECTURE.md §4) and the helpers for distance fog, the fake key
// light and anti-aliased procedural lines.
//
// Conventions
//  * Flight axis = world +Z. The craft sits at z = 0 inside the 18 m × 18 m play field (x, y in −9 … +9); the
//    world streams toward it. The camera chases from a few metres behind.
//  * There is no curved-world bend: what you see is where the collision is (gates are aimed at from far away).
//  * Fog is radial: it grows with the distance from the camera, so the floor fades out evenly in every direction
//    and meets the sky's horizon band.
//  * Global colours arrive already in the active colour space (FieldGlobals converts them); they are LDR
//    (0..1) and shaders scale them into HDR themselves so bloom can catch the lines.
//  * Positions / UVs are always float (large track coordinates); colours may be half.

#ifndef FIELD_COMMON_INCLUDED
#define FIELD_COMMON_INCLUDED

#include "UnityCG.cginc"

// ---------------------------------------------------------------------------------------------------
// Globals (see FieldGlobals.cs)
// ---------------------------------------------------------------------------------------------------

float4 _FieldColorA;      // level primary neon
float4 _FieldColorB;      // level secondary neon
float4 _FieldColorC;      // level accent neon
float4 _FieldBaseColor;   // dark floor / body base
float4 _FieldFogColor;    // distance fade colour (also the sky's horizon and the camera clear colour)
float4 _FieldFogParams;   // x = fog start, y = fog end (metres from the camera), z = horizon glow, w = horizon band
float4 _FieldLightDir;    // xyz = unit direction toward the fake key light (world space)
float _FieldScroll;       // track distance mod 1000
float _FieldScrollDelta;  // metres travelled since the previous frame (motion blur of the grid)
float _FieldBeat;         // 0..1 music beat pulse (every beat)
float _FieldBeatRing;     // 0..1 beat pulse on ring beats only (every FieldGlobals.BeatRingEvery beats)
float _FieldSpeed01;      // normalised speed (can exceed 1 in Overdrive)
float _FieldFlash;        // 0..1 world flash

// ---------------------------------------------------------------------------------------------------
// Transforms
// ---------------------------------------------------------------------------------------------------

float3 FieldObjectToWorld(float4 vertex)
{
    return mul(unity_ObjectToWorld, float4(vertex.xyz, 1.0)).xyz;
}

float4 FieldWorldToClip(float3 worldPos)
{
    return mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
}

// Object → world → clip, the standard vertex path of every BeamRunner shader.
float4 FieldObjectToClip(float4 vertex, out float3 worldPos)
{
    worldPos = FieldObjectToWorld(vertex);
    return FieldWorldToClip(worldPos);
}

// ---------------------------------------------------------------------------------------------------
// Fog
// ---------------------------------------------------------------------------------------------------

// Distance from the camera (the fog coordinate).
float FieldFogDistance(float3 worldPos)
{
    return length(worldPos - _WorldSpaceCameraPos.xyz);
}

// 0 = clear, 1 = fully fogged, from a distance in metres. Disabled (0) when the fog range is not set.
float FieldFogFactor(float distance)
{
    float range = _FieldFogParams.y - _FieldFogParams.x;
    float f = saturate((distance - _FieldFogParams.x) / max(range, 0.001));
    f *= step(0.001, range);
    return f * f * (3.0 - 2.0 * f);
}

// Fog factor of a world position.
float FieldFogAt(float3 worldPos)
{
    return FieldFogFactor(FieldFogDistance(worldPos));
}

// Opaque surfaces fade toward the fog colour.
half3 FieldFogBlend(half3 color, float fog)
{
    return lerp(color, (half3)_FieldFogColor.rgb, (half)fog);
}

// Additive surfaces fade to black (adding nothing) in the fog.
half3 FieldFogFade(half3 color, float fog)
{
    return color * (half)(1.0 - fog);
}

// Extra light where the floor meets the fog: the horizon glows instead of simply disappearing. The sky adds the
// same term at its horizon (FieldSkyColor), so floor and sky join without a seam.
half3 FieldEndGlow(float fog)
{
    half3 glow = lerp((half3)_FieldFogColor.rgb, (half3)_FieldColorA.rgb, (half)0.45);
    return glow * (half)(_FieldFogParams.z * fog * fog * fog);
}

// The sky at view-direction elevation h (the normalised view direction's y), without stars and sun: the horizon colour
// (fog colour + full horizon glow, exactly what the fully fogged floor shows) falling off to a deep zenith, plus a thin
// glowing band and a wider haze. Band and haze start exactly at the horizon (h = 0): below it the sky equals the fogged
// floor, so the floor's far edge never shows, whatever the camera height.
half3 FieldSkyColor(float h)
{
    half3 fogCol = (half3)_FieldFogColor.rgb;
    half3 colA = (half3)_FieldColorA.rgb;
    half3 colC = (half3)_FieldColorC.rgb;
    half3 horizon = fogCol + FieldEndGlow(1.0);
    half3 zenith = (half3)_FieldBaseColor.rgb * (half)0.7 + (half3)_FieldColorB.rgb * (half)0.025;
    float t = saturate(h * 2.2);
    t = 1.0 - (1.0 - t) * (1.0 - t) * (1.0 - t);
    half3 col = lerp(horizon, zenith, (half)t);

    float above = smoothstep(-0.002, 0.002, h);
    float band = exp2(-abs(h) * 40.0) * above;
    float haze = exp2(-max(h, 0.0) * 7.0) * above;
    float strength = _FieldFogParams.w;
    col += lerp(colA, colC, (half)0.3) * (half)(band * 0.35 * strength);
    col += lerp(fogCol, colA, (half)0.5) * (half)(haze * 0.12 * strength);
    return col;
}

// Fog for opaque things floating in the field (walls, cubes, pickups, the craft): they fade toward what is behind them,
// so a far obstacle melts into the floor or the sky instead of standing out as a dark silhouette. h = elevation of the
// view direction (see FieldSkyColor). Below the horizon this is exactly FieldFogFloor (fog colour + glow · fog³);
// above it the target bends toward the sky colour, reaching it at full fog.
half3 FieldFogOpaque(half3 color, float fog, float h)
{
    half3 target = lerp((half3)_FieldFogColor.rgb, FieldSkyColor(h), (half)(fog * fog));
    return lerp(color, target, (half)fog);
}

// The floor's fog: FieldFogOpaque for h < 0, written out (the floor is always below the camera, below the horizon).
half3 FieldFogFloor(half3 color, float fog)
{
    return FieldFogBlend(color, fog) + FieldEndGlow(fog);
}

// Interleaved gradient noise in [0, 1) from a pixel position: the threshold pattern of dithered fades.
float FieldDitherNoise(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

// ---------------------------------------------------------------------------------------------------
// Lighting (fake: there are no real lights)
// ---------------------------------------------------------------------------------------------------

// 0..1: how much a world normal faces the global key light.
float FieldKeyLight(float3 worldNormal)
{
    return saturate(dot(worldNormal, _FieldLightDir.xyz));
}

// ---------------------------------------------------------------------------------------------------
// Palette
// ---------------------------------------------------------------------------------------------------

// Mixes the level palette with a custom colour: palette = (weight A, weight B, weight C, weight custom).
// Materials tinted this way follow level-theme cross-fades with no CPU work.
half3 FieldPaletteColor(float4 palette, half3 custom)
{
    return (half3)(_FieldColorA.rgb * palette.x + _FieldColorB.rgb * palette.y + _FieldColorC.rgb * palette.z)
        + custom * (half)palette.w;
}

// ---------------------------------------------------------------------------------------------------
// Procedural pattern helpers
// ---------------------------------------------------------------------------------------------------

// Hashes without sin() (stable on mobile GPUs for moderate inputs).
float FieldHash11(float p)
{
    p = frac(p * 0.1031);
    p *= p + 33.33;
    p *= p + p;
    return frac(p);
}

float FieldHash21(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// Anti-aliased periodic line. x is measured in cells (lines at integers), halfWidth and blur in cells.
// Energy preserving: a line thinner than a pixel dims instead of shimmering, and once cells shrink below
// ~2 px the pattern fades to its average coverage (no moiré on a floor seen at a grazing angle). blur widens the
// line along its axis (motion blur) while keeping its total energy.
float FieldLine(float x, float halfWidth, float blur)
{
    float fw = max(fwidth(x), 1e-4);
    float d = abs(frac(x + 0.5) - 0.5);
    float w = max(halfWidth + blur, fw * 0.5);
    float a = saturate((w - d) / fw + 0.5) * (halfWidth / w);
    return lerp(a, 2.0 * halfWidth, saturate(fw * 2.0 - 0.5));
}

// Soft glow around periodic lines (a cheap in-shader bloom so neon still reads without post-processing).
float FieldLineGlow(float x, float radius)
{
    float fw = fwidth(x);
    float d = abs(frac(x + 0.5) - 0.5);
    float g = saturate(1.0 - d / radius);
    g *= g;
    // Average of g over a cell is 2r/3: fade to it when the glow can no longer be resolved.
    return lerp(g, radius * 0.6667, saturate(fw / radius - 0.5));
}

#endif // FIELD_COMMON_INCLUDED
