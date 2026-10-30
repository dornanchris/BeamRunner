// BeamRunner — additive glow (particles, ribbons, engine glows, beams, speed lines, play-area frame, hole highlights).
//
// output = _MainTex.rgb * _MainTex.a * vertexColor.rgb * vertexColor.a * tint * _Intensity
// Alpha is folded into the colour because the blend is One One (so vertex / particle alpha fades work).
// tint = FieldPaletteColor(_Palette, _TintColor): the default palette weights (0, 0, 0, 1) use _TintColor.
// Fades to black (adds nothing) in the radial distance fog, scaled by _FogAmount (below 1 for glows that must stay
// visible deeper into the fog, such as the highlight frames around gate openings).
Shader "BeamRunner/Additive"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _TintColor ("Tint", Color) = (1, 1, 1, 1)
        _Palette ("Palette Weights (A, B, C, Custom)", Vector) = (0, 0, 0, 1)
        _Intensity ("Intensity", Float) = 1
        _FogAmount ("Fog Amount", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        LOD 100

        Pass
        {
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "FieldCommon.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _TintColor;
            float4 _Palette;
            float _Intensity;
            float _FogAmount;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : TEXCOORD1;
                float fog : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 worldPos;
                o.pos = FieldObjectToClip(v.vertex, worldPos);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = (half4)v.color;
                o.fog = FieldFogAt(worldPos) * _FogAmount;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 tex = (half4)tex2D(_MainTex, i.uv);
                half3 tint = FieldPaletteColor(_Palette, (half3)_TintColor.rgb);
                half3 col = tex.rgb * tex.a * i.color.rgb * i.color.a * tint * (half)_Intensity;
                col = FieldFogFade(col, i.fog);
                return half4(col, 0.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
