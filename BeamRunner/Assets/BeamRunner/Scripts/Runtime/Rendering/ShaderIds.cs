using UnityEngine;

namespace BeamRunner.Rendering
{
    /// <summary>Cached <see cref="Shader.PropertyToID"/> values for every property the BeamRunner shaders use.</summary>
    public static class ShaderIds
    {
        // Globals (FieldCommon.cginc)
        public static readonly int FieldColorA = Shader.PropertyToID("_FieldColorA");
        public static readonly int FieldColorB = Shader.PropertyToID("_FieldColorB");
        public static readonly int FieldColorC = Shader.PropertyToID("_FieldColorC");
        public static readonly int FieldBaseColor = Shader.PropertyToID("_FieldBaseColor");
        public static readonly int FieldFogColor = Shader.PropertyToID("_FieldFogColor");
        public static readonly int FieldFogParams = Shader.PropertyToID("_FieldFogParams");
        public static readonly int FieldLightDir = Shader.PropertyToID("_FieldLightDir");
        public static readonly int FieldScroll = Shader.PropertyToID("_FieldScroll");
        public static readonly int FieldScrollDelta = Shader.PropertyToID("_FieldScrollDelta");
        public static readonly int FieldBeat = Shader.PropertyToID("_FieldBeat");
        public static readonly int FieldBeatRing = Shader.PropertyToID("_FieldBeatRing");
        public static readonly int FieldSpeed01 = Shader.PropertyToID("_FieldSpeed01");
        public static readonly int FieldFlash = Shader.PropertyToID("_FieldFlash");

        // Per-material / per-instance
        public static readonly int MainTex = Shader.PropertyToID("_MainTex");
        public static readonly int MainTexST = Shader.PropertyToID("_MainTex_ST");
        public static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        public static readonly int Color = Shader.PropertyToID("_Color");
        public static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        public static readonly int TintColor = Shader.PropertyToID("_TintColor");
        public static readonly int EdgeColor = Shader.PropertyToID("_EdgeColor");
        public static readonly int RimColor = Shader.PropertyToID("_RimColor");
        public static readonly int Palette = Shader.PropertyToID("_Palette");
        public static readonly int EdgeWidth = Shader.PropertyToID("_EdgeWidth");
        public static readonly int EdgeIntensity = Shader.PropertyToID("_EdgeIntensity");
        public static readonly int RimIntensity = Shader.PropertyToID("_RimIntensity");
        public static readonly int RimPower = Shader.PropertyToID("_RimPower");
        public static readonly int BodyGlow = Shader.PropertyToID("_BodyGlow");
        public static readonly int Glow = Shader.PropertyToID("_Glow");
        public static readonly int Flash = Shader.PropertyToID("_Flash");
        public static readonly int StripeAmount = Shader.PropertyToID("_StripeAmount");
        public static readonly int UVEdgeWidth = Shader.PropertyToID("_UVEdgeWidth");
        public static readonly int Fade = Shader.PropertyToID("_Fade");
        public static readonly int Intensity = Shader.PropertyToID("_Intensity");
        public static readonly int FogAmount = Shader.PropertyToID("_FogAmount");
        public static readonly int FresnelPower = Shader.PropertyToID("_FresnelPower");
        public static readonly int ScanDensity = Shader.PropertyToID("_ScanDensity");
        public static readonly int ScanSpeed = Shader.PropertyToID("_ScanSpeed");
        public static readonly int Flicker = Shader.PropertyToID("_Flicker");
        public static readonly int Cull = Shader.PropertyToID("_Cull");
        public static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");
        public static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        public static readonly int Ambient = Shader.PropertyToID("_Ambient");
        public static readonly int GridIntensity = Shader.PropertyToID("_GridIntensity");
        public static readonly int StreakIntensity = Shader.PropertyToID("_StreakIntensity");

        // Grid (floor / ceiling)
        public static readonly int RailIntensity = Shader.PropertyToID("_RailIntensity");
        public static readonly int PulseIntensity = Shader.PropertyToID("_PulseIntensity");
        public static readonly int BaseAmount = Shader.PropertyToID("_BaseAmount");
        public static readonly int CellSize = Shader.PropertyToID("_CellSize");
        public static readonly int HalfExtent = Shader.PropertyToID("_HalfExtent");
        public static readonly int SrcBlend = Shader.PropertyToID("_SrcBlend");
        public static readonly int DstBlend = Shader.PropertyToID("_DstBlend");
        public static readonly int ZWrite = Shader.PropertyToID("_ZWrite");

        // Sky
        public static readonly int Radius = Shader.PropertyToID("_Radius");
        public static readonly int StarDensity = Shader.PropertyToID("_StarDensity");
        public static readonly int StarIntensity = Shader.PropertyToID("_StarIntensity");
        public static readonly int SunIntensity = Shader.PropertyToID("_SunIntensity");
        public static readonly int SunSize = Shader.PropertyToID("_SunSize");

        /// <summary>Palette weights selecting the level primary colour (_FieldColorA).</summary>
        public static readonly Vector4 PalettePrimary = new Vector4(1f, 0f, 0f, 0f);

        /// <summary>Palette weights selecting the level secondary colour (_FieldColorB).</summary>
        public static readonly Vector4 PaletteSecondary = new Vector4(0f, 1f, 0f, 0f);

        /// <summary>Palette weights selecting the level accent colour (_FieldColorC).</summary>
        public static readonly Vector4 PaletteAccent = new Vector4(0f, 0f, 1f, 0f);

        /// <summary>Palette weights using only the material's custom colour.</summary>
        public static readonly Vector4 PaletteCustom = new Vector4(0f, 0f, 0f, 1f);

        /// <summary>Palette weights for a level palette slot (0 = primary, 1 = secondary, 2 = accent).</summary>
        public static Vector4 PaletteSlot(int slot)
        {
            switch (slot)
            {
                case 1:
                    return PaletteSecondary;
                case 2:
                    return PaletteAccent;
                default:
                    return PalettePrimary;
            }
        }
    }
}
