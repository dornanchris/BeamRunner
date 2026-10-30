using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BeamRunner.Services
{
    /// <summary>
    /// Short haptic cues. Main thread only; every call is cheap, rate-limited (a pulse within 40 ms of a stronger
    /// or equal one is dropped) and never throws.
    /// <list type="bullet">
    /// <item><b>Android</b>: the system <c>Vibrator</c> via JNI — <c>VibrationEffect.createOneShot</c> with
    /// per-cue amplitude on API 26+, <c>vibrate(ms)</c> below. The <c>android.permission.VIBRATE</c> permission is
    /// added to the manifest automatically because this class references <c>Handheld.Vibrate</c> (used as the
    /// fallback when the Vibrator service is unavailable) — keep that reference if you refactor.</item>
    /// <item><b>iOS</b>: no native plugin, so only <see cref="Heavy"/> and <see cref="Failure"/> produce a
    /// (long) <c>Handheld.Vibrate</c> buzz; lighter cues are silent.</item>
    /// <item><b>Gamepads</b> (any platform): a brief dual-motor rumble on <c>Gamepad.current</c>.</item>
    /// <item>Editor and desktop without a gamepad: no-op.</item>
    /// </list>
    /// Set <see cref="Enabled"/> from the player's settings.
    /// </summary>
    public static class Haptics
    {
        private const float MinIntervalSeconds = 0.04f;

        private struct Pulse
        {
            public int Rank;
            public int DurationMs;
            public int Amplitude;
            public float LowMotor;
            public float HighMotor;
            public float RumbleSeconds;

            public Pulse(int rank, int durationMs, int amplitude, float lowMotor, float highMotor, float rumbleSeconds)
            {
                Rank = rank;
                DurationMs = durationMs;
                Amplitude = amplitude;
                LowMotor = lowMotor;
                HighMotor = highMotor;
                RumbleSeconds = rumbleSeconds;
            }
        }

        private const int HeavyRank = 4;

        private static readonly Pulse SelectionPulse = new Pulse(1, 8, 40, 0f, 0.15f, 0.04f);
        private static readonly Pulse LightPulse = new Pulse(2, 14, 80, 0.1f, 0.25f, 0.06f);
        private static readonly Pulse MediumPulse = new Pulse(3, 24, 150, 0.3f, 0.45f, 0.09f);
        private static readonly Pulse SuccessPulse = new Pulse(3, 32, 170, 0.2f, 0.5f, 0.12f);
        private static readonly Pulse HeavyPulse = new Pulse(4, 45, 255, 0.7f, 0.8f, 0.18f);
        private static readonly Pulse FailurePulse = new Pulse(5, 80, 255, 0.9f, 0.6f, 0.3f);

        private static bool s_enabled = true;
        private static bool s_gamepadRumble = true;
        private static float s_lastPulseTime = float.NegativeInfinity;
        private static int s_lastPulseRank;

        private static Gamepad s_rumblePad;
        private static float s_rumbleStopAt;
        private static bool s_hooked;

        /// <summary>Master switch (bind to the "Haptics" setting). Turning it off also stops any rumble.</summary>
        public static bool Enabled
        {
            get => s_enabled;
            set
            {
                s_enabled = value;
                if (!value)
                {
                    StopRumble();
                }
            }
        }

        /// <summary>Whether gamepad rumble is used (in addition to device vibration). Default true.</summary>
        public static bool GamepadRumbleEnabled
        {
            get => s_gamepadRumble;
            set
            {
                s_gamepadRumble = value;
                if (!value)
                {
                    StopRumble();
                }
            }
        }

        /// <summary>True if this device (or a connected gamepad) can produce haptics at all.</summary>
        public static bool IsSupported
        {
            get
            {
                try
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                    if (EnsureAndroid())
                    {
                        return true;
                    }
#elif UNITY_IOS && !UNITY_EDITOR
                    if (SystemInfo.supportsVibration)
                    {
                        return true;
                    }
#endif
                    return Gamepad.current != null;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>Tiny tick for UI focus/selection changes and slider steps.</summary>
        public static void Selection()
        {
            Play(SelectionPulse);
        }

        /// <summary>Alias of <see cref="Selection"/> (ARCHITECTURE naming).</summary>
        public static void Tick()
        {
            Play(SelectionPulse);
        }

        /// <summary>Light tap: button presses, pickups, near misses.</summary>
        public static void Light()
        {
            Play(LightPulse);
        }

        /// <summary>Medium tap: shield break, overdrive ready, gate smash.</summary>
        public static void Medium()
        {
            Play(MediumPulse);
        }

        /// <summary>Heavy thump: crash, overdrive start.</summary>
        public static void Heavy()
        {
            Play(HeavyPulse);
        }

        /// <summary>Positive confirmation: purchase done, mission complete, new record.</summary>
        public static void Success()
        {
            Play(SuccessPulse);
        }

        /// <summary>Negative feedback: run over, action rejected.</summary>
        public static void Failure()
        {
            Play(FailurePulse);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_enabled = true;
            s_gamepadRumble = true;
            s_lastPulseTime = float.NegativeInfinity;
            s_lastPulseRank = 0;
            s_rumblePad = null;
            s_rumbleStopAt = 0f;
            s_hooked = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            s_androidInitialized = false;
            s_androidAvailable = false;
            s_vibrator = null;
            s_vibrationEffect = null;
#endif
        }

        private static void Play(Pulse pulse)
        {
            if (!s_enabled)
            {
                return;
            }

            float now;
            try
            {
                now = Time.realtimeSinceStartup;
            }
            catch (Exception)
            {
                return; // not on the main thread
            }

            if (now - s_lastPulseTime < MinIntervalSeconds && pulse.Rank <= s_lastPulseRank)
            {
                return;
            }

            s_lastPulseTime = now;
            s_lastPulseRank = pulse.Rank;

#if UNITY_ANDROID && !UNITY_EDITOR
            VibrateAndroid(pulse);
#elif UNITY_IOS && !UNITY_EDITOR
            if (pulse.Rank >= HeavyRank)
            {
                LegacyVibrate();
            }
#endif
            Rumble(pulse);
        }

#if UNITY_ANDROID || UNITY_IOS
        /// <summary>
        /// The one <c>Handheld.Vibrate</c> reference. Besides being the iOS/Android fallback, its presence makes
        /// Unity add <c>android.permission.VIBRATE</c> to the generated Android manifest, which the JNI vibrator
        /// calls require.
        /// </summary>
        private static void LegacyVibrate()
        {
            try
            {
                Handheld.Vibrate();
            }
            catch (Exception)
            {
                // Vibration is best-effort.
            }
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        private const int AndroidDefaultAmplitude = -1; // VibrationEffect.DEFAULT_AMPLITUDE

        private static AndroidJavaObject s_vibrator;
        private static AndroidJavaClass s_vibrationEffect;
        private static int s_sdkInt;
        private static bool s_hasAmplitudeControl;
        private static bool s_androidInitialized;
        private static bool s_androidAvailable;

        private static bool EnsureAndroid()
        {
            if (s_androidInitialized)
            {
                return s_androidAvailable;
            }

            s_androidInitialized = true;
            s_androidAvailable = false;
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    s_sdkInt = version.GetStatic<int>("SDK_INT");
                }

                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    if (activity != null)
                    {
                        s_vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                    }
                }

                if (s_vibrator == null || !s_vibrator.Call<bool>("hasVibrator"))
                {
                    ReleaseAndroid();
                    ServiceLog.Info("Haptics: this device has no vibrator.");
                    return false;
                }

                if (s_sdkInt >= 26)
                {
                    s_vibrationEffect = new AndroidJavaClass("android.os.VibrationEffect");
                    s_hasAmplitudeControl = s_vibrator.Call<bool>("hasAmplitudeControl");
                }

                s_androidAvailable = true;
            }
            catch (Exception e)
            {
                ServiceLog.Warn("Haptics: Android vibrator unavailable (" + e.Message + ").");
                ReleaseAndroid();
            }

            return s_androidAvailable;
        }

        private static void VibrateAndroid(Pulse pulse)
        {
            if (!EnsureAndroid())
            {
                if (pulse.Rank >= HeavyRank)
                {
                    LegacyVibrate();
                }

                return;
            }

            try
            {
                long durationMs = pulse.DurationMs;
                if (s_vibrationEffect != null)
                {
                    int amplitude = s_hasAmplitudeControl ? Mathf.Clamp(pulse.Amplitude, 1, 255) : AndroidDefaultAmplitude;
                    using (AndroidJavaObject effect =
                           s_vibrationEffect.CallStatic<AndroidJavaObject>("createOneShot", durationMs, amplitude))
                    {
                        s_vibrator.Call("vibrate", effect);
                    }
                }
                else
                {
                    s_vibrator.Call("vibrate", durationMs);
                }
            }
            catch (Exception e)
            {
                // Typically a missing VIBRATE permission (SecurityException). Stop trying.
                ServiceLog.Warn("Haptics: vibrate failed (" + e.Message + "); disabling device vibration.");
                ReleaseAndroid();
            }
        }

        private static void ReleaseAndroid()
        {
            s_androidAvailable = false;
            if (s_vibrator != null)
            {
                s_vibrator.Dispose();
                s_vibrator = null;
            }

            if (s_vibrationEffect != null)
            {
                s_vibrationEffect.Dispose();
                s_vibrationEffect = null;
            }
        }
#endif

        private static void Rumble(Pulse pulse)
        {
            if (!s_gamepadRumble)
            {
                return;
            }

            try
            {
                Gamepad pad = Gamepad.current;
                if (pad == null)
                {
                    return;
                }

                if (s_rumblePad != null && s_rumblePad != pad)
                {
                    StopRumble();
                }

                pad.SetMotorSpeeds(pulse.LowMotor, pulse.HighMotor);
                s_rumblePad = pad;
                s_rumbleStopAt = Time.realtimeSinceStartup + pulse.RumbleSeconds;
                Hook();
            }
            catch (Exception e)
            {
                ServiceLog.Warn("Haptics: gamepad rumble failed (" + e.Message + ").");
                s_rumblePad = null;
            }
        }

        private static void Hook()
        {
            if (s_hooked)
            {
                return;
            }

            if (ServiceRunner.Ensure() == null)
            {
                return;
            }

            s_hooked = true;
            ServiceRunner.Ticked += OnTick;
            ServiceRunner.PauseChanged += OnPauseChanged;
            ServiceRunner.FocusChanged += OnFocusChanged;
        }

        private static void OnTick()
        {
            if (s_rumblePad != null && Time.realtimeSinceStartup >= s_rumbleStopAt)
            {
                StopRumble();
            }
        }

        private static void OnPauseChanged(bool paused)
        {
            if (paused)
            {
                StopRumble();
            }
        }

        private static void OnFocusChanged(bool hasFocus)
        {
            if (!hasFocus)
            {
                StopRumble();
            }
        }

        private static void StopRumble()
        {
            Gamepad pad = s_rumblePad;
            s_rumblePad = null;
            if (pad == null)
            {
                return;
            }

            try
            {
                if (pad.added)
                {
                    pad.ResetHaptics();
                }
            }
            catch (Exception)
            {
                // Device vanished mid-rumble; nothing to stop.
            }
        }
    }
}
