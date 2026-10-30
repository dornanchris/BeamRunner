using System;
using BeamRunner.Core.Balance;
using BeamRunner.Core.Meta;

namespace BeamRunner.Game
{
    /// <summary>
    /// Upgrade shop commands (the rules are in <c>ProfileService.Upgrades.cs</c>): buying a level with gems and the daily
    /// free level through a rewarded ad. Both save at once and play the purchase feedback.
    /// </summary>
    public sealed partial class GameController
    {
        /// <summary>Raised after an upgrade level was bought or granted (id, new level).</summary>
        public event Action<UpgradeId, int> UpgradeBought;

        /// <summary>True if the daily free upgrade can be offered now (a rewarded ad is ready and today's token is unused).</summary>
        public bool FreeUpgradeOffered
        {
            get { return Profile != null && Ads != null && Ads.RewardedReady && Profile.FreeUpgradeAvailable(DateTime.UtcNow); }
        }

        /// <summary>Buys the next level of <paramref name="id"/> with gems.</summary>
        public UpgradePurchaseResult BuyUpgrade(UpgradeId id)
        {
            UpgradePurchaseResult result = Profile.BuyUpgrade(id);
            if (result == UpgradePurchaseResult.Purchased)
            {
                Store.SaveNow(true);
                PresentPurchase();
                RaiseUpgradeBought(id);
            }
            else
            {
                PresentError();
            }

            return result;
        }

        /// <summary>Watches a rewarded ad for one free level of <paramref name="id"/> (once per UTC day).</summary>
        public void FreeUpgradeWithAd(UpgradeId id, Action<bool> onDone)
        {
            if (Ads == null || !Profile.FreeUpgradeAvailable(DateTime.UtcNow) || !Profile.UpgradeUnlocked(id) ||
                Profile.UpgradeMaxed(id))
            {
                onDone?.Invoke(false);
                return;
            }

            Ads.ShowRewarded(completed =>
            {
                bool granted = completed && Profile.GrantFreeUpgrade(id, DateTime.UtcNow);
                if (granted)
                {
                    Store.SaveNow(true);
                    PresentReward();
                    RaiseUpgradeBought(id);
                }

                onDone?.Invoke(granted);
            });
        }

        private void RaiseUpgradeBought(UpgradeId id)
        {
            try
            {
                UpgradeBought?.Invoke(id, Profile.UpgradeLevel(id));
            }
            catch (Exception e)
            {
                FieldLog.Exception("UpgradeBought handler", e);
            }
        }
    }
}
