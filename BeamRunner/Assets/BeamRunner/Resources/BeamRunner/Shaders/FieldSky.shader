// BeamRunner — sky backdrop.
//
// A procedural sky on a sphere centred on the camera: FieldSkyColor (FieldCommon.cginc) — a gradient from the level's
// fog colour at the horizon (plus the same horizon glow the fogged floor ends in, so floor and sky meet without a seam)
// up to a deep near-black zenith, with a thin glowing band and a haze that start exactly at the horizon — plus
// hash-based twinkling stars and a synthwave sun with the classic horizontal cuts. No fog: it is infinitely far away.
// Obstacles fade toward the same FieldSkyColor, so they come out of the fog without a silhouette. The sun therefore
// sits off to the left (16° azimuth, 7° up): straight ahead it would sit behind every far gate and cube, which would
// show as dark shapes on it while still deep in the fog.
//
// Mesh contract: any closed mesh around the origin (MeshFactory.SkySphere). The vertex stage only uses the
// direction of each vertex and re-centres the sphere on the camera at _Radius metres, so the object's transform does
// not matter (keep its bounds huge so it is never frustum-culled). _Radius must stay inside the camera's far plane and
// beyond everything else in the world.
// Drawn after the opaque geometry (queue Geometry+450, ZWrite Off, ZTest LEqual): pixels already covered by the
// floor, gates or cubes are rejected by the depth test, so only the visible sky pays for this shader.
Shader "BeamRunner/Sky"
{
    Properties
    {
        _Radius ("Radius (m)", Float) = 260
        _StarDensity ("Star Density", Float) = 1
        _StarIntensity ("Star Intensity", Float) = 1
        _SunIntensity ("Sun Intensity", Float) = 0.85
        _SunSize ("Sun Angular Radius (rad)", Float) = 0.11
    }

    SubShader
    {
        Tags { "Queue" = "Geometry+450" "RenderType" = "Background" "IgnoreProjector" = "True" "PreviewType" = "Skybox" }
        LOD 100

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "FieldCommon.cginc"

            float _Radius;
            float _StarDensity;
            float _StarIntensity;
            float _SunIntensity;
            float _SunSize;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 dir = normalize(v.vertex.xyz + float3(0.0, 1e-6, 0.0));
                float3 worldPos = _WorldSpaceCameraPos.xyz + dir * max(_Radius, 1.0);
                o.pos = FieldWorldToClip(worldPos);
                o.dir = dir;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;

                half3 colA = (half3)_FieldColorA.rgb;
                half3 colB = (half3)_FieldColorB.rgb;
                half3 colC = (half3)_FieldColorC.rgb;

                // ---- gradient, horizon band and haze (shared with the obstacles' fog target)
                half3 col = FieldSkyColor(h);
                float above = smoothstep(-0.002, 0.002, h);

                // ---- stars: one candidate per ~1.3° cell of (azimuth, elevation), twinkling, faded near the horizon
                {
                    const float cellSize = 0.022;
                    float az = atan2(d.x, d.z);
                    float el = asin(clamp(h, -1.0, 1.0));
                    float2 sp = float2(az, el) / cellSize;
                    float2 cellId = floor(sp);
                    float2 f = frac(sp) - 0.5;
                    float h1 = FieldHash21(cellId + 11.0);
                    float h2 = FieldHash21(cellId + 29.7);
                    float h3 = FieldHash21(cellId + 47.3);
                    float h4 = FieldHash21(cellId + 71.1);
                    float present = step(1.0 - 0.16 * saturate(_StarDensity), h1);
                    float2 offset = (float2(h2, h3) - 0.5) * 0.6;
                    float r = length(f - offset);
                    float fw = max(fwidth(sp.x) + fwidth(sp.y), 1e-4);
                    float size = 0.05 + 0.07 * h4;
                    float w = max(size, fw);
                    float s = saturate(1.0 - r / w);
                    float star = s * s * (size / w) * (size / w); // dims instead of aliasing when sub-pixel
                    float twinkle = 0.65 + 0.35 * sin(_Time.y * (1.5 + 3.0 * h2) + h3 * 6.2831853);
                    star *= present * twinkle * (0.4 + 1.2 * h4 * h4) * smoothstep(0.02, 0.25, h);
                    col += lerp(half3(1.0, 1.0, 1.0), colB, (half)(h3 * 0.5)) * (half)(star * 1.6 * _StarIntensity);
                }

                // ---- synthwave sun, low over the horizon to the left of the field (outside the far field's footprint,
                //      ±7° around straight ahead), cut by horizontal bands in its lower half
                {
                    const float3 sunDir = float3(-0.2735828, 0.1218693, 0.9540966); // azimuth −16°, elevation 7°
                    float sunR = max(_SunSize, 0.01);
                    float r = length(d - sunDir) / sunR;   // chord ≈ angle for a small disc: 0 centre … 1 rim
                    float rfw = max(fwidth(r), 1e-4);
                    float disc = saturate((1.0 - r) / rfw + 0.5);
                    float yRel = (h - sunDir.y) / sunR;     // −1 bottom … +1 top
                    float sy = yRel * 4.5;
                    float sb = frac(sy + 0.25);
                    float gap = saturate(-yRel) * 0.55;     // the cuts widen toward the bottom
                    float sfw = max(fwidth(sy), 1e-4);
                    float cut = lerp(1.0, saturate((sb - gap) / sfw + 0.5), step(yRel, 0.0));
                    half3 sunCol = lerp(colA, colC, (half)saturate(yRel * 0.5 + 0.5));
                    col += sunCol * (half)(disc * cut * _SunIntensity);
                    float glow = exp2(-max(r - 1.0, 0.0) * 2.5) * (1.0 - disc);
                    col += lerp(colA, colC, (half)0.5) * (half)(glow * 0.18 * _SunIntensity * above);
                }

                // World flash (hit / level change).
                col += colA * (half)(_FieldFlash * 0.15);
                return half4(col, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
