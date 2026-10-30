// BeamRunner — neon solid (obstacles, pickups, procedural ships).
//
// Dark, faintly self-lit body with razor-sharp glowing edges, a fresnel rim and a white-hot hit flash.
// Opaque, unlit. A fake key light (_FieldLightDir, set by FieldGlobals.SetLightDirection) and a "faces the camera" term
// shade the body, so tumbling cubes and wall blocks read as solids without real lights. Radial distance fog toward what
// is behind (FieldFogOpaque: the fogged floor below the horizon, the sky above it), so far obstacles melt into the
// background instead of standing out as dark silhouettes when they spawn deep in the fog.
//
// FIELD_DITHER (local multi_compile; FieldMaterials.ObstacleDithered) is the fade-out variant for obstacles the craft
// has passed: a screen-space dithered clip by _Fade (per renderer) and by the distance to the camera (gone within
// 0.8 m), so nothing the chase camera flies past fills the screen. Only fading renderers use it: discard costs early-Z
// on mobile GPUs, so the plain variant stays the default.
//
// Edge contract (written by MeshBuilder):
//   TEXCOORD1 (float4) = metric distances (metres) from the fragment to up to four edges of its face;
//                        the edge glow uses min(x, y, z, w), so widths are uniform in metres on every face.
//                        Components that are not real edges hold a large value (1000).
//   TEXCOORD0          = per-face UV where 0 / 1 are the face borders. Used as the edge source (in UV
//                        units, _UVEdgeWidth) when a mesh carries no TEXCOORD1 data.
//   COLOR              = per-vertex edge tint (white = palette colour unchanged).
// _Palette = (weight A, weight B, weight C, weight custom) mixes the level palette (_FieldColorA/B/C) with
// _EdgeColor / _RimColor so obstacles follow level cross-fades without CPU work.
// Edge widths are metres in MESH space: a scaled renderer scales its glow too, so obstacles bake their size into
// the mesh (MeshFactory.Box caches one per quantised size) instead of scaling a unit cube.
Shader "BeamRunner/NeonSolid"
{
    Properties
    {
        _BaseColor ("Body Color", Color) = (0.035, 0.035, 0.06, 1)
        _EdgeColor ("Edge Color", Color) = (0, 0.9, 1, 1)
        _RimColor ("Rim Color", Color) = (0, 0.9, 1, 1)
        _Palette ("Palette Weights (A, B, C, Custom)", Vector) = (0, 0, 0, 1)
        _EdgeWidth ("Edge Width (m)", Float) = 0.05
        _EdgeIntensity ("Edge Intensity", Float) = 3
        _RimIntensity ("Rim Intensity", Float) = 0.8
        _BodyGlow ("Body Glow", Float) = 0.1
        _Glow ("Glow Multiplier", Float) = 1
        _Flash ("Hit Flash", Range(0, 1)) = 0
        _StripeAmount ("Hazard Stripes", Float) = 0
        _UVEdgeWidth ("Fallback UV Edge Width", Float) = 0.04
        _Fade ("Dither Fade (FIELD_DITHER)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        LOD 100

        Pass
        {
            Cull Back
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local __ FIELD_DITHER

            #include "UnityCG.cginc"
            #include "FieldCommon.cginc"

            float4 _BaseColor;
            float4 _EdgeColor;
            float4 _RimColor;
            float4 _Palette;
            float _EdgeWidth;
            float _EdgeIntensity;
            float _RimIntensity;
            float _BodyGlow;
            float _Glow;
            float _Flash;
            float _StripeAmount;
            float _UVEdgeWidth;
            float _Fade;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float4 edge : TEXCOORD1;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 edge : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
                float3 worldNormal : TEXCOORD3;
                float2 misc : TEXCOORD4;        // x = fog, y = hazard stripe coordinate
                half4 color : TEXCOORD5;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 worldPos;
                o.pos = FieldObjectToClip(v.vertex, worldPos);
                o.worldPos = worldPos;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.uv = v.uv;
                o.edge = v.edge;
                o.color = (half4)v.color;
                o.misc = float2(FieldFogAt(worldPos), dot(v.vertex.xyz, float3(1.6, 1.6, 1.6)));
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 toCamera = _WorldSpaceCameraPos.xyz - i.worldPos;
                float cameraDistance = max(length(toCamera), 1e-4);
                float3 viewDir = toCamera / cameraDistance;

            #ifdef FIELD_DITHER
                // Dissolve: per-renderer fade, and anything within ~3.5 m of the camera thins out (gone at 0.8 m).
                float fade = _Fade * saturate((cameraDistance - 0.8) / 2.7);
                clip(fade - FieldDitherNoise(i.pos.xy) * 0.999 - 0.0005);
            #endif

                float3 n = normalize(i.worldNormal);
                float ndv = saturate(dot(n, viewDir));

                // Fake lighting: faces turned toward the global key light and toward the craft / camera (−Z)
                // read brighter, so shapes stay legible without real lights.
                float facing = FieldKeyLight(n);
                float front = saturate(-n.z);
                float lightTerm = 0.45 + 0.4 * facing + 0.3 * front;

                half3 edgeCol = FieldPaletteColor(_Palette, (half3)_EdgeColor.rgb) * i.color.rgb;
                half3 rimCol = FieldPaletteColor(_Palette, (half3)_RimColor.rgb);

                // Body: dark with a faint internal gradient and a little self-illumination in the edge colour.
                half3 col = (half3)_BaseColor.rgb * (half)(lightTerm * lerp(0.8, 1.25, saturate(i.uv.y)));
                col += edgeCol * (half)(_BodyGlow * (0.6 + 0.4 * facing));

                // Edge distance: metric (TEXCOORD1) when present, per-face UV otherwise.
                float4 e = i.edge;
                float metricD = min(min(e.x, e.y), min(e.z, e.w));
                float hasMetric = step(1e-6, max(max(e.x, e.y), e.z));
                float2 uvE = min(i.uv, 1.0 - i.uv);
                float d = lerp(min(uvE.x, uvE.y), metricD, hasMetric);
                float width = lerp(_UVEdgeWidth, _EdgeWidth, hasMetric);

                // Anti-aliased edge that never drops below a pixel (dims instead of aliasing).
                float fw = max(fwidth(d), 1e-5);
                float w = max(width, fw * 0.75);
                float edge = saturate((w - d) / fw + 0.5) * (width / w);
                float halo = exp2(-d / max(width * 2.5, 1e-4));

                col += edgeCol * (half)((edge + halo * 0.22) * _EdgeIntensity * _Glow);
                col += (half)(edge * _EdgeIntensity * 0.15 * _Glow); // white-hot core

                float rim = 1.0 - ndv;
                rim *= rim * rim;
                col += rimCol * (half)(rim * _RimIntensity * _Glow);

                // Hazard stripes (hopping-hole gates): diagonal accent bands in object space.
                float sc = i.misc.y;
                float sfw = max(fwidth(sc), 1e-4);
                float sd = abs(frac(sc) - 0.5) - 0.25;
                float stripe = saturate(-sd / sfw + 0.5) * saturate(1.5 - sfw * 2.0);
                col += (half3)_FieldColorC.rgb * (half)(stripe * _StripeAmount * lightTerm);

                // World flash and white-hot hit flash.
                col += edgeCol * (half)(_FieldFlash * 0.5);
                col = lerp(col, edgeCol * (half)0.4 + (half)2.5, (half)_Flash);

                col = FieldFogOpaque(col, i.misc.x, -viewDir.y);
                return half4(col, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
