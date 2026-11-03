// BeamRunner — sky backdrop.
//
// Deep space around the structure: FieldSkyColor (FieldCommon.cginc) — the haze the fog fades into at the far end of the
// track, deepening to the void elsewhere, fixed to the view so fogged things always melt into it — plus, turning with the
// world (_FieldSkyRotation: the turn of every wall climbed, and of the one being climbed), a two-tone nebula of the
// level palette, a field of twinkling stars and one bright beacon star, so climbing a wall reads as the whole world
// rotating and each level faces a different part of the sky. No fog: it is infinitely far away.
//
// Mesh contract: any closed mesh around the origin (MeshFactory.SkySphere). The vertex stage only uses the
// direction of each vertex and re-centres the sphere on the camera at _Radius metres, so the object's transform does
// not matter (keep its bounds huge so it is never frustum-culled). _Radius must stay inside the camera's far plane and
// beyond everything else in the world (the wall ahead stands up to ~600 m away).
// Drawn after the opaque geometry (queue Geometry+450, ZWrite Off, ZTest LEqual): pixels already covered by the
// surface or the cubes are rejected by the depth test, so only the visible sky pays for this shader.
// FIELD_LITE (quality tier 0): one nebula octave instead of two.
Shader "BeamRunner/Sky"
{
    Properties
    {
        _Radius ("Radius (m)", Float) = 900
        _StarDensity ("Star Density", Float) = 1
        _StarIntensity ("Star Intensity", Float) = 1
        _NebulaIntensity ("Nebula Intensity", Float) = 1
        _BeaconIntensity ("Beacon Intensity", Float) = 1
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
            #pragma multi_compile __ FIELD_LITE
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "FieldCommon.cginc"

            float _Radius;
            float _StarDensity;
            float _StarIntensity;
            float _NebulaIntensity;
            float _BeaconIntensity;

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

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            // Smooth 3D value noise in [0, 1].
            float Noise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = lerp(Hash31(i), Hash31(i + float3(1, 0, 0)), f.x);
                float b = lerp(Hash31(i + float3(0, 1, 0)), Hash31(i + float3(1, 1, 0)), f.x);
                float c = lerp(Hash31(i + float3(0, 0, 1)), Hash31(i + float3(1, 0, 1)), f.x);
                float e = lerp(Hash31(i + float3(0, 1, 1)), Hash31(i + float3(1, 1, 1)), f.x);
                return lerp(lerp(a, b, f.y), lerp(c, e, f.y), f.z);
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 s = normalize(mul((float3x3)_FieldSkyRotation, d)); // the turning sky's direction

                half3 colA = (half3)_FieldColorA.rgb;
                half3 colB = (half3)_FieldColorB.rgb;
                half3 colC = (half3)_FieldColorC.rgb;

                // ---- the haze at the end of the track (fixed to the view; the fog target)
                half3 col = FieldSkyColor(d);
                float haze = saturate(d.z);
                float open = 1.0 - 0.7 * haze * haze * haze; // the sky's details give way to the haze straight ahead

                // ---- nebula: two broad clouds of the palette with darker lanes through them
                {
                    float n = Noise3(s * 2.1 + 3.7);
                    #ifndef FIELD_LITE
                    n = n * 0.65 + Noise3(s * 5.3 + 11.2) * 0.35;
                    #endif
                    float m = Noise3(s * 1.3 + 27.1);
                    float cloud = smoothstep(0.42, 0.85, n);
                    cloud *= cloud;
                    half3 tint = lerp(colB, colC, (half)smoothstep(0.3, 0.7, m));
                    col += tint * (half)(cloud * 0.06 * _NebulaIntensity * open);
                    col += lerp(tint, colA, (half)0.5) * (half)(smoothstep(0.7, 0.95, n) * 0.05 * _NebulaIntensity * open);
                }

                // ---- stars: one candidate per cell of a cube projection of the sky (no pinching at the poles)
                {
                    float3 a = abs(s);
                    float2 fuv;
                    float face;
                    if (a.x >= a.y && a.x >= a.z)
                    {
                        fuv = s.yz / a.x;
                        face = s.x > 0.0 ? 1.0 : 2.0;
                    }
                    else if (a.y >= a.z)
                    {
                        fuv = s.xz / a.y;
                        face = s.y > 0.0 ? 3.0 : 4.0;
                    }
                    else
                    {
                        fuv = s.xy / a.z;
                        face = s.z > 0.0 ? 5.0 : 6.0;
                    }

                    float2 sp = fuv * 44.0;
                    float2 cellId = floor(sp) + face * 131.0;
                    float2 f = frac(sp) - 0.5;
                    float h1 = FieldHash21(cellId + 11.0);
                    float h2 = FieldHash21(cellId + 29.7);
                    float h3 = FieldHash21(cellId + 47.3);
                    float h4 = FieldHash21(cellId + 71.1);
                    float present = step(1.0 - 0.12 * saturate(_StarDensity), h1);
                    float2 offset = (float2(h2, h3) - 0.5) * 0.6;
                    float r = length(f - offset);
                    float fw = max(fwidth(sp.x) + fwidth(sp.y), 1e-4);
                    float size = 0.05 + 0.07 * h4;
                    float w = max(size, fw);
                    float st = saturate(1.0 - r / w);
                    float star = st * st * (size / w) * (size / w); // dims instead of aliasing when sub-pixel
                    float twinkle = 0.65 + 0.35 * sin(_Time.y * (1.5 + 3.0 * h2) + h3 * 6.2831853);
                    star *= present * twinkle * (0.4 + 1.2 * h4 * h4) * open;
                    col += lerp(half3(1.0, 1.0, 1.0), colB, (half)(h3 * 0.5)) * (half)(star * 1.6 * _StarIntensity);
                }

                // ---- the beacon: one bright star with a soft glow, a landmark that swings round on every wall
                {
                    const float3 beaconDir = float3(0.42, 0.55, 0.72);
                    float c = saturate(dot(s, normalize(beaconDir)));
                    float ang = sqrt(saturate(2.0 * (1.0 - c))); // ≈ angle (rad)
                    float core = exp2(-ang * 160.0);
                    float glow = exp2(-ang * 22.0);
                    half3 beacon = lerp(colC, half3(1.0, 1.0, 1.0), (half)0.6);
                    col += beacon * (half)((core * 2.2 + glow * 0.18) * _BeaconIntensity * open);
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
