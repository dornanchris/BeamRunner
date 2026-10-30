using System;
using System.Collections.Generic;
using BeamRunner.Core.Localization;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using BeamRunner.Core.Diagnostics;
#endif

namespace BeamRunner.Services
{
    /// <summary>
    /// Loads the string tables (<c>Resources/BeamRunner/Localization/&lt;code&gt;.json</c>, English always) into
    /// <see cref="Loc"/> and picks the language: the player's Settings choice (per device, <see cref="DevicePrefs"/>
    /// key <see cref="PrefKey"/>; empty = automatic) or else the device language (<see cref="Application.systemLanguage"/>,
    /// every Chinese variant → Simplified Chinese). The pseudo-localization is offered in the Editor and development
    /// builds only. <see cref="SetChoice"/> switches at runtime and raises <see cref="Loc.LanguageChanged"/> (the UI
    /// rebuilds). Never throws: a missing or broken table falls back to English, a missing English table to the keys.
    /// </summary>
    public static class LocalizationService
    {
        public const string ResourceFolder = "BeamRunner/Localization/";

        /// <summary>DevicePrefs key of the player's language choice (a language code; "" or absent = automatic).</summary>
        public const string PrefKey = "BeamRunner.Language";

        private static bool s_initialized;
        private static StringTable s_english;

        /// <summary>True once <see cref="Initialize"/> ran.</summary>
        public static bool IsInitialized
        {
            get { return s_initialized; }
        }

        /// <summary>
        /// The language the device asks for (a shipped language; English when unsupported). Editor and development
        /// builds: <c>TestLaunchOptions.Language</c> stands in for it when set.
        /// </summary>
        public static LanguageInfo DeviceLanguage
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LanguageInfo forced = Languages.Find(TestLaunchOptions.Language);
                if (forced != null)
                {
                    return forced;
                }
#endif
                SystemLanguage system;
                try
                {
                    system = Application.systemLanguage;
                }
                catch (Exception)
                {
                    system = SystemLanguage.English;
                }

                return Languages.FromSystemLanguage(system.ToString());
            }
        }

        /// <summary>The player's explicit choice, or null for automatic (device language).</summary>
        public static LanguageInfo Choice
        {
            get
            {
                LanguageInfo chosen = Languages.Find(DevicePrefs.GetString(PrefKey, string.Empty));
                return chosen != null && (!chosen.IsPseudo || PseudoAvailable) ? chosen : null;
            }
        }

        /// <summary>The pseudo-localization can be picked (Editor and development builds).</summary>
        public static bool PseudoAvailable
        {
            get { return Debug.isDebugBuild; }
        }

        /// <summary>Languages the Settings picker offers: the shipped ones, plus the pseudo-localization in debug builds.</summary>
        public static IReadOnlyList<LanguageInfo> Selectable
        {
            get
            {
                var list = new List<LanguageInfo>(Languages.Shipped);
                if (PseudoAvailable)
                {
                    list.Add(Languages.Pseudo);
                }

                return list;
            }
        }

        /// <summary>Loads English and the chosen/device language into <see cref="Loc"/> (idempotent).</summary>
        public static void Initialize()
        {
            if (s_initialized)
            {
                return;
            }

            s_initialized = true;
            try
            {
                string error;
                s_english = LoadTable(Languages.English, out error);
                if (s_english == null)
                {
                    ServiceLog.Error("Localization: the English string table did not load (" + error + "); showing keys.");
                }

                var localizer = new Localizer(s_english);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                localizer.MissingKey = (language, key) => ServiceLog.Warn("Localization: \"" + key + "\" is missing in " + language + ".");
#endif
                Loc.Install(localizer);
                Apply(Resolve(), false);
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Localization setup", e);
            }
        }

        /// <summary>
        /// Stores the player's choice (null = automatic, i.e. the device language) and switches to it now; the UI rebuilds
        /// on <see cref="Loc.LanguageChanged"/>.
        /// </summary>
        public static void SetChoice(LanguageInfo language)
        {
            Initialize();
            if (language != null && language.IsPseudo && !PseudoAvailable)
            {
                language = null;
            }

            DevicePrefs.SetString(PrefKey, language != null ? language.Code : string.Empty);
            Apply(Resolve(), true);
        }

        /// <summary>The language to show now: the player's choice, else the device language.</summary>
        private static LanguageInfo Resolve()
        {
            return Choice ?? DeviceLanguage;
        }

        private static void Apply(LanguageInfo language, bool notify)
        {
            language = language ?? Languages.English;
            StringTable table = null;
            if (!language.IsPseudo && !ReferenceEquals(language, Languages.English))
            {
                string error;
                table = LoadTable(language, out error);
                if (table == null)
                {
                    ServiceLog.Error("Localization: the " + language.Code + " string table did not load (" + error + "); using English.");
                    language = Languages.English;
                }
            }

            if (notify)
            {
                Loc.SetLanguage(language, table);
            }
            else
            {
                Loc.Current.SetLanguage(language, table);
            }

            ServiceLog.Info("Localization: " + language.Code + " (device " + DeviceLanguage.Code + ").");
        }

        private static StringTable LoadTable(LanguageInfo language, out string error)
        {
            TextAsset asset = null;
            try
            {
                asset = Resources.Load<TextAsset>(ResourceFolder + language.Code);
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }

            if (asset == null)
            {
                error = "no " + ResourceFolder + language.Code + ".json";
                return null;
            }

            StringTable table = StringTable.TryFromJson(language.Code, asset.text, out error);
            Resources.UnloadAsset(asset);
            return table;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_initialized = false;
            s_english = null;
            Loc.Reset();
        }
    }
}
