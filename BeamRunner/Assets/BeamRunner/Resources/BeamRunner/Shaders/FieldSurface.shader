// BeamRunner — the beam the track runs along.
//
// The face of the vast dark beam under the rails (far below the bottom layer), the quarter pipe where it turns up at the
// end of a level, and the wall ahead — the next beam — that the next level climbs (SurfaceView.cs builds the path).
// Opaque and unlit: a dark body shaded by the fake key light, glowing edges, the faint seams of one of three surface
// patterns, sparse lit windows and, up the wall ahead, three lane guides with light pulses racing up them. Along both
// edges rise the tunnel's side walls (color.b = 1, uv.x = height up the wall): near-black void with faint depth bands
// and a thin, faint rim along their top, so the track runs down a dark channel that turns up with it.
//
// Mesh contract: uv0 = (x across, v along the path) in metres (v grows toward the wall and up it); color.r = face
// (0 = the face under the track, which wears the level palette; ≥ 0.5 = the wall ahead and beyond, which wear the next
// level's palette, _FieldNextColor*); color.g = 0 on the floor … 1 on the wall (the wall is fogged less, so it looms
// through the fog from the start of the level); normal = toward the track.
// Patterns (_FieldSurfacePattern.x for face 0, .y for the rest): 0 = panels, 1 = ribs, 2 = diamonds.
// FIELD_LITE (quality tier 0) drops the windows.
Shader "BeamRunner/Surface"
{
    Properties
    {
        _HalfWidth ("Half Width (m)", Float) = 14
        _SeamIntensity ("Seam Intensity", Float) = 0.17
        _GuideIntensity ("Lane Guide Intensity", Float) = 0.45
        _EdgeIntensity ("Edge Intensity", Float) = 1.4
        _WindowIntensity ("Window Intensity", Float) = 0.25
        _WallFog ("Fog Distance Scale on the Wall", Float) = 0.5
        _SideHeight ("Side Wall Height (m)", Float) = 40
        _SideRim ("Side Wall Rim Intensity", Float) = 0.12
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        LOD 100

        Pass
        {
            Cull Off
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile __ FIELD_LITE
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "FieldCommon.cginc"

            float _HalfWidth;
            float _SeamIntensity;
            float _GuideIntensity;
            float _EdgeIntensity;
            float _WindowIntensity;
            float _WallFog;
            float _SideHeight;
            float _SideRim;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wp : TEXCOORD1;
                float4 misc : TEXCOORD2; // face, wall, key light, side wall
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 wp;
                o.pos = FieldObjectToClip(v.vertex, wp);
                o.wp = wp;
                o.uv = v.uv;
                float3 n = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                o.misc = float4(v.color.r, v.color.g, FieldKeyLight(n), v.color.b);
                return o;
            }

            // Seam coverage of a surface pattern at (u, v) metres. Lines along the path stay clear of the strip under the
            // lanes (|u| < 6): seen from the rails they would read as track on a layer below.
            float Seams(float pattern, float u, float v)
            {
                float outside = step(6.0, abs(u));
                if (pattern < 0.5)
                {
                    // Panels: big plates.
                    return max(FieldLine(u / 7.0, 0.012, 0.0) * outside, FieldLine(v / 24.0, 0.006, 0.0));
                }

                if (pattern < 1.5)
                {
                    // Ribs: cross ribs every 6 m, banded every 48 m.
                    return max(FieldLine(v / 6.0, 0.02, 0.0) * 0.6, FieldLine(v / 48.0, 0.008, 0.0));
                }

                // Diamonds: a diagonal lattice.
                return max(FieldLine((u + v) / 14.0, 0.014, 0.0), FieldLine((u - v) / 14.0, 0.014, 0.0));
            }

            half4 frag(v2f i) : SV_Target
            {
                float next = step(0.5, i.misc.x);
                float wall = i.misc.y;
                half3 colA = (half3)lerp(_FieldColorA.rgb, _FieldNextColorA.rgb, next);
                half3 colB = (half3)lerp(_FieldColorB.rgb, _FieldNextColorB.rgb, next);
                half3 colC = (half3)lerp(_FieldColorC.rgb, _FieldNextColorC.rgb, next);
                half3 baseCol = (half3)lerp(_FieldBaseColor.rgb, _FieldNextBaseColor.rgb, next);
                float pattern = lerp(_FieldSurfacePattern.x, _FieldSurfacePattern.y, next);
                float u = i.uv.x;
                float v = i.uv.y;
                float d = FieldFogDistance(i.wp);
                float3 dir = (i.wp - _WorldSpaceCameraPos.xyz) / max(d, 1e-3);

                if (i.misc.w > 0.5)
                {
                    // ---- a tunnel side wall: the void, faintly ribbed, with a faint rim along its top
                    float up = saturate(u / max(_SideHeight, 1.0));
                    half3 voidBody = baseCol * (half)(0.25 + 0.25 * i.misc.z) * (half)(1.0 - 0.6 * up);
                    float bands = FieldLine(v / 16.0, 0.008, 0.0) * (1.0 - up) * 0.5;
                    float ufw = max(fwidth(u), 1e-4);
                    float rimDist = _SideHeight - u;
                    float rim = saturate((0.35 - rimDist) / ufw + 0.5) + 0.1 * exp2(-max(rimDist, 0.0) * 0.8);
                    float foot = exp2(-u * 0.35) * 0.35;
                    half3 sideEmit = colB * (half)(bands * _SeamIntensity) + lerp(colA, colC, (half)0.3) * (half)(rim * _SideRim)
                        + colA * (half)(foot * 0.25);
                    float sideFog = FieldFogFactor(d * lerp(1.0, _WallFog, wall));
                    float sideEmitFog = FieldFogFactor(d * lerp(1.0, _WallFog * 0.6, wall));
                    return half4(FieldFogOpaque(voidBody, sideFog, dir) + sideEmit * (half)(1.0 - sideEmitFog), 1.0);
                }

                // ---- body: the structure's dark plating, lit by the fake key light
                half3 body = baseCol * (half)(0.5 + 0.7 * i.misc.z) + colB * (half)0.01;

                // ---- emissive: seams, windows, lane guides
                half3 emit = colB * (half)(Seams(pattern, u, v) * _SeamIntensity);

                #ifndef FIELD_LITE
                {
                    float2 sp = float2(u / 3.0, v / 6.0);
                    float2 cell = floor(sp);
                    float h = FieldHash21(cell + pattern * 17.0);
                    float lit = step(0.975, h) * (1.0 - 0.8 * wall);
                    float2 fc = abs(frac(sp) - 0.5);
                    float fw = fwidth(sp.x) + fwidth(sp.y);
                    float box = saturate((0.3 - max(fc.x, fc.y * 0.9)) / max(fw, 1e-4) + 0.5);
                    float resolve = saturate(1.5 - fw * 3.0); // tiny in the distance: their average glow instead
                    float window = lit * lerp(0.2, box, resolve);
                    emit += lerp(colA, colC, (half)frac(h * 37.0)) * (half)(window * _WindowIntensity);
                }
                #endif

                {
                    // Only up the wall ahead (where they show the next level's lanes climbing): on the face below
                    // they would read as rails of a lower layer.
                    float lanes = FieldLine(u / 3.0, 0.035, 0.0) * step(abs(u), 4.6) * smoothstep(0.3, 0.9, wall);
                    float p = frac((v - _Time.y * 36.0) / 48.0);
                    p = p * p * p * p * p * p; // a bright head racing toward the wall, with a long tail behind it
                    emit += colA * (half)(lanes * _GuideIntensity * (0.3 + 2.2 * p + 0.3 * _FieldBeat));
                }

                // ---- the beam's glowing edges (a hot line and a soft glow inward, and a fainter inner line)
                {
                    float e = _HalfWidth - abs(u);
                    float efw = max(fwidth(u), 1e-4);
                    float edge = saturate((0.3 - e) / efw + 0.5) + 0.3 * exp2(-max(e, 0.0) * 1.1);
                    float inner = FieldLine((e - 1.6) / 4.0, 0.02, 0.0) * step(e, 3.6);
                    emit += lerp(colA, colC, (half)0.25) * (half)(edge * _EdgeIntensity) + colB * (half)(inner * 0.5);
                }

                emit += colA * (half)(_FieldFlash * 0.1);

                // ---- fog: the wall ahead is fogged less (it looms from the start of the level), its lights least
                float fogBody = FieldFogFactor(d * lerp(1.0, _WallFog, wall));
                float fogEmit = FieldFogFactor(d * lerp(1.0, _WallFog * 0.6, wall));
                half3 col = FieldFogOpaque(body, fogBody, dir) + emit * (half)(1.0 - fogEmit);
                return half4(col, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
