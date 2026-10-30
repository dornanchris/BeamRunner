namespace BeamRunner.Core
{
    /// <summary>Colour palette of a level: three neons, the dark base and the distance fog.</summary>
    public sealed class ZoneTheme
    {
        public readonly ColorRGB Primary;
        public readonly ColorRGB Secondary;
        public readonly ColorRGB Accent;
        public readonly ColorRGB Base;
        public readonly ColorRGB Fog;

        public ZoneTheme(string primary, string secondary, string accent, string baseColor, string fog)
        {
            Primary = ColorRGB.Hex(primary);
            Secondary = ColorRGB.Hex(secondary);
            Accent = ColorRGB.Hex(accent);
            Base = ColorRGB.Hex(baseColor);
            Fog = ColorRGB.Hex(fog);
        }
    }

    /// <summary>Level palettes: every level has its own, cycling after the last one.</summary>
    public static class ZoneThemes
    {
        private static readonly ZoneTheme[] Themes =
        {
            new ZoneTheme("#FF2BD6", "#00E5FF", "#FFE45C", "#07030F", "#1A0630"),
            new ZoneTheme("#00E5FF", "#7CFF3A", "#FF2BD6", "#020A0D", "#062430"),
            new ZoneTheme("#FF6A00", "#FFC400", "#FF2E4D", "#0D0402", "#2A0B06"),
            new ZoneTheme("#9D4DFF", "#3D7BFF", "#FF4DF0", "#05020F", "#140833"),
            new ZoneTheme("#7CFF3A", "#00FFA8", "#F4FF52", "#020A05", "#062414"),
            new ZoneTheme("#FF1744", "#FF9100", "#FFD6E0", "#0F0204", "#2E050C"),
            new ZoneTheme("#7FF6FF", "#E8FDFF", "#4DA6FF", "#020812", "#07223A"),
        };

        public static int Count
        {
            get { return Themes.Length; }
        }

        /// <summary>Palette by 0-based index (cycles).</summary>
        public static ZoneTheme Get(int index)
        {
            if (index < 0)
            {
                index = 0;
            }

            return Themes[index % Themes.Length];
        }

        /// <summary>Palette of level <paramref name="level"/> (1-based).</summary>
        public static ZoneTheme ForLevel(int level)
        {
            return Get(level - 1);
        }
    }
}
