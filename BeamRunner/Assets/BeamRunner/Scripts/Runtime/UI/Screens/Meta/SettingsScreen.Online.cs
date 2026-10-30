using System;
using BeamRunner.Core.Localization;
using BeamRunner.Game;
using BeamRunner.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Settings, online part: one card titled with the platform (GOOGLE PLAY GAMES / GAME CENTER) showing the sign-in
    /// state with a SIGN IN button where the platform allows it, and the cloud save status with SYNC NOW. Hidden when
    /// nothing online is configured; SYNC NOW is hidden during a run (Settings is reachable from Pause).
    /// </summary>
    public sealed partial class SettingsScreen
    {
        private Card _onlineCard;
        private RectTransform _accountBlock;
        private Text _accountText;
        private NeonButton _signIn;
        private RectTransform _cloudBlock;
        private Text _cloudText;
        private NeonButton _syncNow;
        private bool _signingIn;
        private bool _syncing;
        private GameController _onlineSubscribed;

        private void OnOnlineChanged()
        {
            if (this != null && IsVisible)
            {
                MarkDirty();
            }
        }

        private void BuildOnline(RectTransform c)
        {
            _onlineCard = UIFactory.Card(c, Loc.T("online.title"), IconId.Trophy);
            RectTransform body = _onlineCard.Body;

            _accountBlock = UIFactory.CreateRect(body, "Account");
            VerticalLayoutGroup av = UIFactory.VStack(_accountBlock, UITheme.SpaceSm, null, TextAnchor.UpperLeft, true);
            av.childForceExpandWidth = true;
            _accountText = Hint(_accountBlock, string.Empty);
            RectTransform accountButtons = ButtonRow(_accountBlock);
            _signIn = UIFactory.Button(accountButtons, Loc.T("online.sign_in"), ButtonStyle.Secondary, OnSignIn, IconId.Play);

            _cloudBlock = UIFactory.CreateRect(body, "Cloud");
            VerticalLayoutGroup cv = UIFactory.VStack(_cloudBlock, UITheme.SpaceSm, null, TextAnchor.UpperLeft, true);
            cv.childForceExpandWidth = true;
            Caption(_cloudBlock, Loc.T("online.cloud_save"));
            _cloudText = Hint(_cloudBlock, string.Empty);
            RectTransform cloudButtons = ButtonRow(_cloudBlock);
            _syncNow = UIFactory.Button(cloudButtons, Loc.T("online.sync_now"), ButtonStyle.Secondary, OnSyncNow, IconId.Restore);
        }

        private void RefreshOnline()
        {
            if (_onlineCard == null)
            {
                return;
            }

            if (Game != null && _onlineSubscribed != Game)
            {
                // Subscribed lazily (the controller and this screen live for the whole session).
                _onlineSubscribed = Game;
                Game.OnlineChanged += OnOnlineChanged;
            }

            IOnlineService online = Game != null ? Game.Online : null;
            bool available = online != null && online.IsAvailable;
            _onlineCard.gameObject.SetActive(available);
            if (!available)
            {
                return;
            }

            _onlineCard.SetTitle(MetaUI.PlatformTitle(online));

            bool account = online.UsesAccount;
            _accountBlock.gameObject.SetActive(account);
            if (account)
            {
                _accountText.text = AccountText(online);
                bool canSignIn = !_signingIn && online.CanSignInManually;
                _signIn.transform.parent.gameObject.SetActive(canSignIn);
            }

            CloudSync cloud = Game.CloudSave;
            bool hasCloud = cloud != null && cloud.IsAvailable;
            _cloudBlock.gameObject.SetActive(hasCloud);
            if (hasCloud)
            {
                _cloudText.text = CloudText(cloud);
                bool inRun = InRun();
                _syncNow.transform.parent.gameObject.SetActive(!inRun && !cloud.NeedsSignIn);
                _syncNow.SetInteractable(!_syncing && !cloud.IsSyncing);
                _syncNow.SetLabel(_syncing || cloud.IsSyncing ? Loc.T("online.syncing_button") : Loc.T("online.sync_now"));
            }
        }

        private static string AccountText(IOnlineService online)
        {
            switch (online.SignInState)
            {
                case OnlineSignInState.SignedIn:
                    return string.IsNullOrEmpty(online.PlayerName) ? Loc.T("online.signed_in") : Loc.T("online.signed_in_as", ("name", online.PlayerName));
                case OnlineSignInState.SigningIn:
                    return Loc.T("online.signing_in");
                case OnlineSignInState.NotStarted:
                    return Loc.T("online.not_started");
                default:
                    if (online.CanSignInManually)
                    {
                        return Loc.T("online.sign_in_hint");
                    }

                    return online.Platform == OnlinePlatform.GameCenter
                        ? Loc.T("online.not_signed_in_game_center")
                        : Loc.T("online.not_signed_in");
            }
        }

        private static string CloudText(CloudSync cloud)
        {
            switch (cloud.State)
            {
                case CloudSaveState.Syncing:
                    return Loc.T("online.cloud_syncing");
                case CloudSaveState.Synced:
                    return Loc.T("online.cloud_synced", ("ago", Ago(cloud.LastSyncUtc)));
                case CloudSaveState.Offline:
                    return Loc.T("online.cloud_offline");
                case CloudSaveState.AccountConflict:
                    return Loc.T("online.cloud_conflict", ("button", Loc.T("online.sync_now")));
                case CloudSaveState.NeedsUpdate:
                    return Loc.T("online.cloud_needs_update");
                default:
                    if (cloud.NeedsSignIn)
                    {
                        return Loc.T("online.cloud_needs_sign_in");
                    }

                    DateTime? last = cloud.LastSyncUtc;
                    return last.HasValue ? Loc.T("online.cloud_last_synced", ("ago", Ago(last))) : Loc.T("online.cloud_not_synced");
            }
        }

        private static string Ago(DateTime? utc)
        {
            if (!utc.HasValue)
            {
                return Loc.T("time.never");
            }

            TimeSpan age = DateTime.UtcNow - utc.Value;
            if (age.TotalMinutes < 1.0)
            {
                return Loc.T("time.just_now");
            }

            if (age.TotalHours < 1.0)
            {
                return Loc.T("time.minutes_ago", ("n", (int)age.TotalMinutes));
            }

            if (age.TotalDays < 1.0)
            {
                return Loc.T("time.hours_ago", ("n", (int)age.TotalHours));
            }

            return Loc.T("time.days_ago", ("n", (int)age.TotalDays));
        }

        private void OnSignIn()
        {
            if (Game == null || _signingIn)
            {
                return;
            }

            _signingIn = true;
            MarkDirty();
            Game.SignInOnline(ok =>
            {
                _signingIn = false;
                if (this != null)
                {
                    MarkDirty();
                }
            });
        }

        private void OnSyncNow()
        {
            if (Game == null || Game.CloudSave == null || _syncing || InRun())
            {
                return;
            }

            if (Game.CloudSave.State == CloudSaveState.AccountConflict)
            {
                Dialogs.Confirm(Loc.T("online.combine_title"), Loc.T("online.combine_body"), Loc.T("online.combine_confirm"),
                    Loc.T("common.cancel"), ok =>
                    {
                        if (ok)
                        {
                            StartSync(true);
                        }
                    }, false, IconId.Restore);
                return;
            }

            StartSync(false);
        }

        private void StartSync(bool mergeAccounts)
        {
            if (Game == null || _syncing)
            {
                return;
            }

            _syncing = true;
            MarkDirty();
            Game.SyncCloudNow(mergeAccounts, ok =>
            {
                _syncing = false;
                if (this == null)
                {
                    return;
                }

                MarkDirty();
                if (ok)
                {
                    MetaUI.Toast(Loc.T("toast.synced"), IconId.Check);
                }
                else if (Game != null && Game.CloudSave != null && Game.CloudSave.State == CloudSaveState.Offline)
                {
                    MetaUI.Toast(Loc.T("toast.sync_failed"), IconId.Warning);
                }
            });
        }
    }
}
