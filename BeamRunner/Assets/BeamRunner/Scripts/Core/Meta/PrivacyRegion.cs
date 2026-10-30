using System;
using System.Collections.Generic;

namespace BeamRunner.Core.Meta
{
    /// <summary>
    /// Where optional diagnostics (crash reports) must be opt-in rather than opt-out. The first-launch privacy dialog
    /// pre-sets its "Send crash reports" switch from this: <b>off</b> (opt-in) in the EEA, the UK, Switzerland and their
    /// GDPR-style territories — consent under ePrivacy Art. 5(3) / PECR — and whenever the region cannot be told;
    /// <b>on</b> (clearly shown, one tap to turn off) elsewhere. The region is only a guess from the device's country
    /// setting and time zone, so it errs towards opt-in: a European time zone <i>or</i> a European country is enough.
    /// Nothing is ever sent before the player has answered the dialog.
    /// </summary>
    public static class PrivacyRegion
    {
        // EU-27 + EEA (IS, LI, NO) + UK + CH, plus EU outermost regions / territories with their own ISO codes and the
        // UK Crown dependencies and Gibraltar (GDPR-equivalent laws).
        private static readonly HashSet<string> s_optInCountries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AT", "BE", "BG", "HR", "CY", "CZ", "DK", "EE", "FI", "FR", "DE", "GR", "EL", "HU", "IE", "IT", "LV", "LT",
            "LU", "MT", "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE",
            "IS", "LI", "NO",
            "GB", "UK", "CH",
            "AX", "GF", "GP", "MQ", "RE", "YT", "MF", "IC", "EA",
            "GI", "GG", "JE", "IM",
        };

        // Non-"Europe/" IANA zones of the places above.
        private static readonly HashSet<string> s_optInZones = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Atlantic/Canary", "Atlantic/Madeira", "Atlantic/Azores", "Atlantic/Reykjavik", "Atlantic/Faroe",
            "Atlantic/Faeroe", "Arctic/Longyearbyen", "Indian/Reunion", "Indian/Mayotte", "America/Guadeloupe",
            "America/Martinique", "America/Cayenne", "America/Marigot", "Africa/Ceuta", "GB", "GB-Eire", "Eire",
            "Iceland", "Poland", "Portugal", "CET", "EET", "WET", "MET",
        };

        /// <summary>
        /// True when crash reporting should default to off (opt-in). <paramref name="countryCode"/>: ISO 3166 alpha-2
        /// of the device's region setting (null/empty/invalid = unknown). <paramref name="timeZoneId"/>: the device's
        /// time zone id (IANA, e.g. "Europe/Berlin"; Windows ids containing "Europe" are also recognised; null = unknown).
        /// </summary>
        public static bool RequiresOptIn(string countryCode, string timeZoneId)
        {
            string country = NormalizeCountry(countryCode);
            string zone = string.IsNullOrWhiteSpace(timeZoneId) ? null : timeZoneId.Trim();

            if (country != null && s_optInCountries.Contains(country))
            {
                return true;
            }

            if (zone != null && IsEuropeanZone(zone))
            {
                return true;
            }

            // Nothing known at all: be conservative.
            return country == null && (zone == null || IsNeutralZone(zone));
        }

        /// <summary>The default of the "Send crash reports" switch: the opposite of <see cref="RequiresOptIn"/>.</summary>
        public static bool DefaultCrashReportsOn(string countryCode, string timeZoneId)
        {
            return !RequiresOptIn(countryCode, timeZoneId);
        }

        private static string NormalizeCountry(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return null;
            }

            code = code.Trim();
            if (code.Length != 2 || !char.IsLetter(code[0]) || !char.IsLetter(code[1]))
            {
                return null;
            }

            code = code.ToUpperInvariant();

            // "Invariant" / user-assigned / unknown codes .NET or the OS may report.
            return code == "IV" || code == "ZZ" || code == "XX" || code == "AA" ? null : code;
        }

        private static bool IsEuropeanZone(string zone)
        {
            return zone.StartsWith("Europe/", StringComparison.OrdinalIgnoreCase) ||
                   s_optInZones.Contains(zone) ||
                   zone.IndexOf("Europe", StringComparison.OrdinalIgnoreCase) >= 0 || // Windows ids ("W. Europe Standard Time")
                   zone.StartsWith("GMT Standard Time", StringComparison.OrdinalIgnoreCase) || // Windows: London/Dublin/Lisbon
                   zone.StartsWith("Romance Standard Time", StringComparison.OrdinalIgnoreCase) || // Windows: Paris/Madrid
                   zone.StartsWith("GTB Standard Time", StringComparison.OrdinalIgnoreCase) || // Windows: Athens/Bucharest
                   zone.StartsWith("FLE Standard Time", StringComparison.OrdinalIgnoreCase); // Windows: Helsinki/Riga
        }

        private static bool IsNeutralZone(string zone)
        {
            // Zones that say nothing about the place (UTC/GMT offsets, "Etc/...").
            return zone.Equals("UTC", StringComparison.OrdinalIgnoreCase) ||
                   zone.Equals("GMT", StringComparison.OrdinalIgnoreCase) ||
                   zone.Equals("Z", StringComparison.OrdinalIgnoreCase) ||
                   zone.StartsWith("Etc/", StringComparison.OrdinalIgnoreCase) ||
                   zone.StartsWith("UTC", StringComparison.OrdinalIgnoreCase) ||
                   zone.StartsWith("GMT+", StringComparison.OrdinalIgnoreCase) ||
                   zone.StartsWith("GMT-", StringComparison.OrdinalIgnoreCase) ||
                   zone.Equals("Coordinated Universal Time", StringComparison.OrdinalIgnoreCase);
        }
    }
}
