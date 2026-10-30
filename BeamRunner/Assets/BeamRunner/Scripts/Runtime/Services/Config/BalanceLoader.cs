using System;
using BeamRunner.Core.Balance;
using UnityEngine;

namespace BeamRunner.Services
{
    /// <summary>
    /// Loads <c>Resources/BeamRunner/Config/balance.json</c> into <see cref="BalanceConfig.Current"/> (call once at boot,
    /// before the profile is loaded: upgrade maxima clamp saved levels). Fields missing from the file keep their code
    /// defaults; a missing or broken file keeps every default. Never throws.
    /// </summary>
    public static class BalanceLoader
    {
        public const string ResourcePath = "BeamRunner/Config/balance";

        public static void Load()
        {
            var config = new BalanceConfig();
            try
            {
                var asset = Resources.Load<TextAsset>(ResourcePath);
                if (asset == null)
                {
                    ServiceLog.Warn("balance.json not found in Resources; using built-in defaults.");
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(asset.text))
                    {
                        JsonUtility.FromJsonOverwrite(asset.text, config);
                    }

                    Resources.UnloadAsset(asset);
                }
            }
            catch (Exception e)
            {
                ServiceLog.Warn("balance.json could not be parsed (" + e.Message + "); using built-in defaults.");
                config = new BalanceConfig();
            }

            BalanceConfig.Current = config; // sanitized by the setter
        }
    }
}
