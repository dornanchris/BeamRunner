// Unity 6 / platform shims (compile checks only): these UnityEngine types are platform-specific and are missing
// from the (standalone-player) 6000.0.58 engine assemblies used by this toolchain, so they are provided here as NEW
// types. Signatures copied from UnityCsReference 6000.0.37f1 (+ the DeviceGeneration values added up to 6000.3.25f1):
//   Runtime/Export/Handheld/Handheld.bindings.cs, Runtime/Export/iOS/iOSDevice*.cs, Runtime/Export/iOS/iOSSystemGestureDeferMode.cs
using System;

namespace UnityEngine
{
    public enum FullScreenMovieControlMode
    {
        Full = 0,
        Minimal = 1,
        CancelOnInput = 2,
        Hidden = 3,
    }

    public enum FullScreenMovieScalingMode
    {
        None = 0,
        AspectFit = 1,
        AspectFill = 2,
        Fill = 3,
    }

    public enum AndroidActivityIndicatorStyle
    {
        DontShow = -1,
        Large = 0,
        InversedLarge = 1,
        Small = 2,
        InversedSmall = 3,
    }

    public class Handheld
    {
        public static bool PlayFullScreenMovie(string path, Color bgColor, FullScreenMovieControlMode controlMode, FullScreenMovieScalingMode scalingMode) => false;
        public static bool PlayFullScreenMovie(string path, Color bgColor, FullScreenMovieControlMode controlMode) => false;
        public static bool PlayFullScreenMovie(string path, Color bgColor) => false;
        public static bool PlayFullScreenMovie(string path) => false;
        public static void Vibrate() { }

        [Obsolete("Property Handheld.use32BitDisplayBuffer has been deprecated. Modifying it has no effect, use PlayerSettings instead.")]
        public static bool use32BitDisplayBuffer { get { return false; } set { } }

        public static void SetActivityIndicatorStyle(iOS.ActivityIndicatorStyle style) { }
        public static void SetActivityIndicatorStyle(AndroidActivityIndicatorStyle style) { }
        public static int GetActivityIndicatorStyle() => 0;
        public static void StartActivityIndicator() { }
        public static void StopActivityIndicator() { }
        public static void ClearShaderCache() { }
    }
}

namespace UnityEngine.iOS
{
    public enum DeviceGeneration
    {
        Unknown         = 0,
        iPhone          = 1,
        iPhone3G        = 2,
        iPhone3GS       = 3,
        iPodTouch1Gen   = 4,
        iPodTouch2Gen   = 5,
        iPodTouch3Gen   = 6,
        iPad1Gen        = 7,
        iPhone4         = 8,
        iPodTouch4Gen   = 9,
        iPad2Gen        = 10,
        iPhone4S        = 11,
        iPad3Gen        = 12,
        iPhone5         = 13,
        iPodTouch5Gen   = 14,
        iPadMini1Gen    = 15,
        iPad4Gen        = 16,
        iPhone5C        = 17,
        iPhone5S        = 18,
        iPadAir1        = 19,
        iPadMini2Gen    = 20,
        iPhone6         = 21,
        iPhone6Plus     = 22,
        iPadMini3Gen    = 23,
        iPadAir2        = 24,
        iPhone6S        = 25,
        iPhone6SPlus    = 26,
        iPadPro1Gen     = 27,
        iPadMini4Gen    = 28,
        iPhoneSE1Gen    = 29,
        iPadPro10Inch1Gen = 30,
        iPhone7         = 31,
        iPhone7Plus     = 32,
        iPodTouch6Gen   = 33,
        iPad5Gen        = 34,
        iPadPro2Gen     = 35,
        iPadPro10Inch2Gen = 36,
        iPhone8         = 37,
        iPhone8Plus     = 38,
        iPhoneX         = 39,
        iPhoneXS        = 40,
        iPhoneXSMax     = 41,
        iPhoneXR        = 42,
        iPadPro11Inch   = 43,
        iPadPro3Gen     = 44,
        iPad6Gen        = 45,
        iPadAir3Gen     = 46,
        iPadMini5Gen    = 47,
        iPhone11        = 48,
        iPhone11Pro     = 49,
        iPhone11ProMax  = 50,
        iPodTouch7Gen   = 51,
        iPad7Gen        = 52,
        iPhoneSE2Gen    = 53,
        iPadPro11Inch2Gen = 54,
        iPadPro4Gen     = 55,
        iPhone12Mini    = 56,
        iPhone12        = 57,
        iPhone12Pro     = 58,
        iPhone12ProMax  = 59,
        iPad8Gen        = 60,
        iPadAir4Gen     = 61,
        iPad9Gen        = 62,
        iPadMini6Gen    = 63,
        iPhone13        = 64,
        iPhone13Mini    = 65,
        iPhone13Pro     = 66,
        iPhone13ProMax  = 67,
        iPadPro5Gen     = 68,
        iPadPro11Inch3Gen = 69,
        iPhoneSE3Gen    = 70,
        iPadAir5Gen     = 71,
        iPhone14        = 72,
        iPhone14Plus    = 73,
        iPhone14Pro     = 74,
        iPhone14ProMax  = 75,
        iPadPro6Gen     = 76,
        iPadPro11Inch4Gen = 77,
        iPad10Gen       = 78,
        iPhone15        = 79,
        iPhone15Plus    = 80,
        iPhone15Pro     = 81,
        iPhone15ProMax  = 82,
        iPhone16        = 83,
        iPhone16Plus    = 84,
        iPhone16Pro     = 85,
        iPhone16ProMax  = 86,
        iPhone16e       = 87,   // 6000.3
        iPhone17        = 88,
        iPhoneAir       = 89,
        iPhone17Pro     = 90,
        iPhone17ProMax  = 91,
        iPhone17e       = 92,
        iPhoneUnknown       = 10001,
        iPadUnknown         = 10002,
        iPodTouchUnknown    = 10003,
    }

    public enum ActivityIndicatorStyle
    {
        DontShow = -1,
        [Obsolete("WhiteLarge Activity Indicator has been deprecated by Apple. Use Large instead (UnityUpgradable) -> Large", true)]
        WhiteLarge = 0,
        [Obsolete("White Activity Indicator has been deprecated by Apple. Use Medium instead (UnityUpgradable) -> Medium", true)]
        White = 1,
        [Obsolete("Gray Activity Indicator has been deprecated by Apple. Use Medium instead (UnityUpgradable) -> Medium", true)]
        Gray = 2,
        Medium = 100,
        Large = 101,
    }

    [Flags]
    public enum SystemGestureDeferMode : uint
    {
        None = 0,
        TopEdge = 1 << 0,
        LeftEdge = 1 << 1,
        BottomEdge = 1 << 2,
        RightEdge = 1 << 3,
        All = TopEdge | LeftEdge | BottomEdge | RightEdge
    }

    public sealed partial class Device
    {
        public static bool hideHomeButton { get; set; }
        public static bool wantsSoftwareDimming { get; set; }
        public static SystemGestureDeferMode deferSystemGesturesMode { get; set; }
        public static bool RequestStoreReview() => false;
        public static string systemVersion => "";
        public static DeviceGeneration generation => DeviceGeneration.Unknown;
        public static string vendorIdentifier => "";
        public static void SetNoBackupFlag(string path) { }
        public static void ResetNoBackupFlag(string path) { }
        public static bool lowPowerModeEnabled => false;
        public static bool advertisingTrackingEnabled => false;
        public static string advertisingIdentifier => "";
        public static bool iosAppOnMac => false;
        public static bool runsOnSimulator => false;
    }
}
