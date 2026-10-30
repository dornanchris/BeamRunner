// BeamRunner — hologram (shield bubble, Daily Run ghost).
//
// Transparent, additive-ish (Blend SrcAlpha One): fresnel-lit silhouette, moving scanlines, a slow
// bright sweep band and a subtle flicker. Scanlines run along the object's local up axis in metres, so
// they look the same at any object scale. Fades out in the radial distance fog.
Shader "BeamRunner/Hologram"
{
    Properties
    {
        _Color ("Color", Color) = (0, 0.9, 1, 1)
        _Palette ("Palette Weights (A, B, C, Custom)", Vector) = (0, 0, 0, 1)
        _Intensity ("Intensity", Float) = 1.5
        _FresnelPower ("Fresnel Power", Float) = 2.5
        _ScanDensity ("Scanlines Per Metre", Float) = 9
        _ScanSpeed ("Scan Speed", Float) = 1.2
        _Flicker ("Flicker", Range(0, 1)) = 0.12
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        LOD 100

        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull [_Cull]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "FieldCommon.cginc"

            float4 _Color;
            float4 _Palette;
            float _Intensity;
            float _FresnelPower;
            float _ScanDensity;
            float _ScanSpeed;
            float _Flicker;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float2 misc : TEXCOORD2; // x = fog, y = scan coordinate (metres along local up)
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 worldPos;
                o.pos = FieldObjectToClip(v.vertex, worldPos);
                o.worldPos = worldPos;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);

                float3 origin = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
                float3 upAxis = normalize(float3(unity_ObjectToWorld._m01, unity_ObjectToWorld._m11, unity_ObjectToWorld._m21) + 1e-6);
                o.misc = float2(FieldFogAt(worldPos), dot(worldPos - origin, upAxis));
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - i.worldPos);
                float ndv = abs(dot(n, viewDir));
                float fresnel = pow(saturate(1.0 - ndv), _FresnelPower);

                // Fine scanlines drifting along local up; fade to their average when sub-pixel.
                float scan = i.misc.y * _ScanDensity - _Time.y * _ScanSpeed;
                float sfw = fwidth(scan);
                float sf = frac(scan);
                float scanLine = smoothstep(0.0, 0.12, sf) * (1.0 - smoothstep(0.38, 0.5, sf));
                scanLine = lerp(scanLine, 0.35, saturate(sfw * 2.0 - 0.5));

                // A broad bright band sweeping through every ~1.4 s.
                float sweep = frac(i.misc.y * 0.6 - _Time.y * 0.7);
                sweep = pow(sweep, 10.0);

                float flicker = 1.0 - _Flicker * (0.5 + 0.5 * sin(_Time.y * 47.0 + sin(_Time.y * 13.0) * 2.0));

                half3 baseCol = FieldPaletteColor(_Palette, (half3)_Color.rgb);
                float alpha = saturate(0.12 + fresnel * 1.1 + scanLine * 0.22 + sweep * 0.45) * flicker * _Color.a;
                half3 col = baseCol * (half)(_Intensity * (1.0 + fresnel * 1.6 + sweep));
                col += (half)(fresnel * fresnel * _Intensity * 0.35); // white-hot silhouette
                col = FieldFogFade(col, i.misc.x);
                return half4(col, alpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
