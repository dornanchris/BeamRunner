// BeamRunner — shared shader code.
//
// Every world shader includes this file. It declares the global properties written by
// BeamRunner.Rendering.FieldGlobals (docs/ARCHITECTURE.md §4) and the helpers for distance fog, the fake key
// light and anti-aliased procedural lines.
//
// Conventions
//  * Flight axis = world +Z. The craft sits at z = 0 inside the 9 m × 9 m rail grid (x, y in −4.5 … +4.5); the
//    world streams toward it. The camera chases from a few metres behind.
//  * The track bends up each level's wall (TrackSpace.cs): the C# side places everything; shaders see plain world
//    positions. While the craft rides a bend the world turns about the bend's axis, and the sky turns with it
//    (_FieldSkyRotation), so "up" in world space means nothing to the shaders: no horizon, no floor-vs-sky split.
//  * Fog is radial: it grows with the distance from the camera, and everything fades toward FieldSkyColor of its
//    view direction (the haze at the far end of the track, the void elsewhere), so nothing leaves a silhouette.
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
float4x4 _FieldSkyRotation; // world → sky directions (the turns of the walls climbed so far)
float4 _FieldNextColorA;  // the next level's palette (the wall ahead wears it)
float4 _FieldNextColorB;
float4 _FieldNextColorC;
float4 _FieldNextBaseColor;
float4 _FieldSurfacePattern; // x = pattern of the face under the track, y = of the wall ahead (Surface shader)

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

// Extra light in the fog: what is fully fogged glows a little instead of simply disappearing (FieldSkyColor adds the
// same term, so a fogged surface and the sky behind it match).
half3 FieldEndGlow(float fog)
{
    half3 glow = lerp((half3)_FieldFogColor.rgb, (half3)_FieldColorA.rgb, (half)0.45);
    return glow * (half)(_FieldFogParams.z * fog * fog * fog);
}

// The sky without stars and nebula, for a world view direction d (normalised): the haze the fog fades into fills the
// far end of the track (+Z, where every rail converges) and deepens toward the void at the sides and behind, with a
// soft glow at the very end of the track. It depends only on the view direction relative to the ride axis, so it stays
// put while the world turns on a bend (the rotating part of the sky is drawn on top by the Sky shader).
half3 FieldSkyColor(float3 d)
{
    half3 fogCol = (half3)_FieldFogColor.rgb;
    half3 colA = (half3)_FieldColorA.rgb;
    half3 haze = fogCol + FieldEndGlow(1.0) * (half)0.18;
    half3 deep = (half3)_FieldBaseColor.rgb * (half)0.7 + (half3)_FieldColorB.rgb * (half)0.025;
    float f = saturate(d.z);
    float k = saturate((1.0 - f) * 7.0); // the haze fills ~25° around the end of the track
    k = k * k * (3.0 - 2.0 * k);
    half3 col = lerp(haze, deep, (half)k);
    float core = exp2(-(1.0 - f) * 18.0);
    col += lerp(fogCol, colA, (half)0.5) * (half)(core * 0.1 * _FieldFogParams.w);
    return col;
}

// Fog for opaque things (cubes, pickups, the craft, the surface): they fade toward the sky behind them (FieldSkyColor
// of the view direction dir, from the camera to the fragment), reaching it exactly at full fog, so a far obstacle
// melts into its background instead of standing out as a dark silhouette.
half3 FieldFogOpaque(half3 color, float fog, float3 dir)
{
    half3 target = lerp((half3)_FieldFogColor.rgb, FieldSkyColor(dir), (half)(fog * fog));
    return lerp(color, target, (half)fog);
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
