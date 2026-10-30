// BeamRunner — neon grid planes (the floor under the play field and the ceiling above it).
//
// A near-black plane carrying a razor-sharp neon grid. Lane lines run along +Z on the cell boundaries, so the
// field's 9 lanes line up with the gates' cells; the field's side edges are brighter rails carrying light pulses that
// race away from you. Cross lines every 4 m and major lines every 20 m scroll toward the craft with the track
// (_FieldScroll) and are motion-blurred by the per-frame travel (_FieldScrollDelta) so they never strobe backwards.
// A beat ring flies away every other bar of the music, speed streaks slide along the lanes as speed rises, and the far
// floor melts into the fog with a horizon glow that the sky continues. Outside the field the grid dims with distance.
//
// One shader, two looks (set per material by FieldMaterials):
//   floor:   _BaseAmount = 1, Blend One Zero, ZWrite On, opaque queue — dark body + lines, fogs to the fog colour;
//   ceiling: _BaseAmount = 0, Blend One One, ZWrite Off, transparent queue — dim lines only (the starry sky shows
//            through), fading to black in the fog.
// Mesh contract (MeshFactory.GridPlane): a horizontal plane; the pattern is computed from the world position, so the
// mesh needs no UVs. Every pattern period along Z divides 1000 m, so the _FieldScroll wrap (distance mod 1000) is
// seamless. FIELD_LITE (quality tier 0) drops the lane pulses and the speed streaks.
Shader "BeamRunner/Grid"
{
    Properties
    {
        _GridIntensity ("Grid Intensity", Float) = 1
        _RailIntensity ("Rail Intensity", Float) = 1
        _PulseIntensity ("Light Pulses", Float) = 1
        _StreakIntensity ("Speed Streaks", Float) = 1
        _BaseAmount ("Base Amount (1 = opaque floor, 0 = additive lines)", Float) = 1
        _CellSize ("Cell Size (m)", Float) = 2
        _HalfExtent ("Field Half Extent (m)", Float) = 9
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 1
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        LOD 100

        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile __ FIELD_LITE

            #include "UnityCG.cginc"
            #include "FieldCommon.cginc"

            float _GridIntensity;
            float _RailIntensity;
            float _PulseIntensity;
            float _StreakIntensity;
            float _BaseAmount;
            float _CellSize;
            float _HalfExtent;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 worldPos;
                o.pos = FieldObjectToClip(v.vertex, worldPos);
                o.worldPos = worldPos;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 wp = i.worldPos;
                float x = wp.x;
                float zw = wp.z;                   // metres ahead of the craft
                float v = zw + _FieldScroll;       // track metres (1000 m periodic)
                float fog = FieldFogAt(wp);        // per pixel: the plane's triangles are huge
                float speed01 = saturate(_FieldSpeed01);
                float motion = _FieldScrollDelta;  // metres moved this frame
                float fieldHalf = max(_HalfExtent, 0.5);
                float cell = max(_CellSize, 0.1);

                float ax = abs(x);
                float inField = 1.0 - smoothstep(fieldHalf, fieldHalf + 0.6, ax);
                float outside = exp2(-max(ax - fieldHalf, 0.0) * 0.05); // halves every 20 m beyond the field

                // Two colour lobes drifting slowly across and along the floor (500 m period along Z).
                float swirl = 0.5 + 0.5 * sin(x * 0.07 + v * 0.01256637);
                half3 colA = (half3)_FieldColorA.rgb;
                half3 colB = (half3)_FieldColorB.rgb;
                half3 colC = (half3)_FieldColorC.rgb;
                half3 lineCol = lerp(colA, colB, (half)swirl);
                half3 ringCol = lerp(colB, colA, (half)swirl);

                // ---- lane lines along +Z on the cell boundaries (x = ±1, ±3 … ±9 m with 2 m cells)
                float lx = (x + fieldHalf) / cell;
                float lanes = FieldLine(lx, 0.012, 0.0);
                float laneW = lerp(0.4 * outside, 1.0, inField);
                float parity = frac(floor(lx) * 0.5) * 2.0; // alternate lanes, for a faint lane shading

                // ---- rails: the field's side edges, repeating every field width outside as a coarse super-grid
                float rx = (x + fieldHalf) / (2.0 * fieldHalf);
                float rails = FieldLine(rx, 0.0035, 0.0);
                float railGlow = FieldLineGlow(rx, 0.03);
                float railW = lerp(0.35 * outside, 1.0, step(ax, fieldHalf + 1.0));

                // ---- cross lines (4 m) and major lines (20 m). Crisp while the per-frame travel is well below the
                //      spacing; approaching the strobing limit they are motion-blurred along Z and the 4 m lines fade.
                float ry = v * 0.25;
                float rings = FieldLine(ry, 0.012, max(motion - 1.4, 0.0) * 0.5 * 0.25);
                rings *= saturate(2.2 - motion * 0.55);
                float my = v * 0.05;
                float major = FieldLine(my, 0.004, max(motion - 7.0, 0.0) * 0.5 * 0.05);
                float majorGlow = FieldLineGlow(my, 0.03);
                float crossW = lerp(0.45 * outside + 0.08, 1.0, inField);

                // ---- light pulses racing away along the rails (world space, independent of speed)
                float railIndex = floor(rx + 0.5);
                float railPhase = FieldHash11(railIndex - 8.0 * floor(railIndex / 8.0) + 3.0);
                float pf = frac(zw * (1.0 / 70.0) - _Time.y * 1.1 + railPhase);
                float pulse = pf * pf;
                pulse *= pulse;
                pulse *= pulse;                            // pf^8: bright head, long tail toward the craft
                pulse *= 1.0 - smoothstep(0.975, 1.0, pf);  // crisp but anti-aliased head
                pulse *= (rails * 2.5 + railGlow * 1.2) * railW;

                // ---- beat ring: launched at the craft on ring beats (every other bar), flies off ahead as the pulse
                //      decays. The lane lines still throb on every beat (_FieldBeat, below).
                float beatZ = 5.0 + (1.0 - _FieldBeatRing) * 110.0;
                float beatRing = exp2(-abs(zw - beatZ) * 0.8) * _FieldBeatRing * _FieldBeatRing * crossW;

                // ---- detail: lane pulses and speed streaks (skipped on the lite quality tier)
                float lanePulse = 0.0;
                float streak = 0.0;
            #ifndef FIELD_LITE
                {
                    float laneIndex = floor(lx + 0.5);
                    float lp = frac(zw * (1.0 / 90.0) - _Time.y * 0.8 + FieldHash11(laneIndex + 31.0));
                    float lpulse = lp * lp;
                    lpulse *= lpulse;
                    lpulse *= lpulse;
                    lpulse *= 1.0 - smoothstep(0.97, 1.0, lp);
                    lanePulse = lpulse * lanes * 3.0 * inField;

                    // Streaks: 0.5 m columns, 8 m cells (125 per 1000 m), sliding toward the craft 25 m/s faster
                    // than the floor. The time term wraps by exactly 1000 m every 40 s.
                    float sx = x * 2.0;
                    float sv = v + frac(_Time.y * 0.025) * 1000.0;
                    float column = floor(sx);
                    float cellZ = floor(sv * 0.125);
                    float2 cid = float2(column, cellZ - 125.0 * floor(cellZ / 125.0));
                    float h1 = FieldHash21(cid);
                    float h2 = FieldHash21(cid + 17.31);
                    float present = step(1.0 - (0.05 + 0.4 * saturate(_FieldSpeed01 * 0.8)), h1);
                    float lenF = 0.3 + 0.6 * h2;
                    float t = (frac(sv * 0.125) - (0.5 - 0.5 * lenF)) / lenF; // 0 = head (craft side), 1 = tail
                    float along = step(0.0, t) * step(t, 1.0) * (1.0 - t);
                    float fwx = max(fwidth(sx), 1e-4);
                    float across = saturate((max(0.07, fwx * 0.5) - abs(frac(sx) - 0.5)) / fwx + 0.5);
                    across *= 0.07 / max(0.07, fwx * 0.5);
                    streak = present * along * across * saturate(1.5 - fwx * 1.5);
                    streak *= _FieldSpeed01 * _FieldSpeed01 * _StreakIntensity * (1.0 - smoothstep(20.0, 30.0, ax));
                }
            #endif

                // ---- compose (HDR). The grid is dimmed behind the craft so the eye goes forward.
                float nearDim = 0.55 + 0.45 * smoothstep(-8.0, 10.0, zw);
                half3 white = half3(1.0, 1.0, 1.0);
                half3 baseCol = (half3)_FieldBaseColor.rgb * (half)(1.0 + inField * (0.45 + 0.2 * parity))
                    + lineCol * (half)(0.02 + 0.03 * inField);
                half3 col = baseCol * (half)_BaseAmount;
                col += lineCol * (half)(lanes * laneW * 0.9 * nearDim * _GridIntensity);
                col += lineCol * (half)((rails * 2.3 + railGlow * 0.25) * railW * nearDim * _RailIntensity);
                col += ringCol * (half)(rings * 1.3 * crossW * nearDim * _GridIntensity);
                col += colC * (half)((major * 2.4 + majorGlow * 0.25) * crossW * nearDim * _GridIntensity);
                col += lerp(lineCol, white, (half)0.35)
                    * (half)((pulse + lanePulse) * _PulseIntensity * (0.45 + 0.55 * speed01));
                col += colC * (half)(beatRing * 2.4 * _GridIntensity)
                    + lineCol * (half)(lanes * laneW * _FieldBeat * 0.6 * _GridIntensity);
                col += lerp(lineCol, white, (half)0.5) * (half)(streak * 2.2);

                // World flash (hit / level change): the whole grid flares.
                col += lineCol * (half)(_FieldFlash * (0.25 * _BaseAmount + (lanes * laneW + rings + rails) * 2.0) * _GridIntensity);

                half3 opaque = FieldFogFloor(col, fog);
                half3 additive = FieldFogFade(col, fog);
                col = lerp(additive, opaque, (half)saturate(_BaseAmount));
                return half4(col, saturate(_BaseAmount));
            }
            ENDCG
        }
    }

    Fallback Off
}
