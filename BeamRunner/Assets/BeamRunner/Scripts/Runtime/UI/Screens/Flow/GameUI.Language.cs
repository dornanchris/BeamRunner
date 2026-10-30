using BeamRunner.Core.Localization;
using BeamRunner.Game;
using UnityEngine;

namespace BeamRunner.UI
{
    /// <summary>
    /// Runtime language switching: on <see cref="Loc.LanguageChanged"/> the whole UI is rebuilt behind a short fade.
    /// <para>Why a rebuild rather than re-applying text per label: most texts are composed at refresh time from state
    /// (numbers, plurals, conditional phrases), the font asset changes with the script (Latin → Cyrillic → CJK) and
    /// widths/best-fit depend on the text. Re-running the normal build path (the same one boot uses, screens built lazily
    /// by the build queue) re-resolves every string and font in one place, so nothing can be missed — including pooled
    /// dialog buttons and toasts, which live on the old root and go with it. Cost: one short hitch behind the fade, for a
    /// rare Settings action.</para>
    /// <para>The rebuild waits for the menu or hangar (the Language setting is hidden during runs; a switch made elsewhere,
    /// e.g. by the test probe, applies when the player is back there) and for open dialogs to close. Kept
    /// across it: whether the daily popup was already offered this session, and an open Settings overlay.</para>
    /// </summary>
    public sealed partial class GameUI
    {
        private const float LanguageFadeOut = 0.16f;
        private const float LanguageFadeIn = 0.28f;

        private bool _languageDirty;
        private bool _rebuilding;

        /// <summary>True while a language change is waiting for (or running) the UI rebuild.</summary>
        public bool LanguageRebuildPending
        {
            get { return _languageDirty || _rebuilding; }
        }

        private void OnLanguageChanged()
        {
            _languageDirty = true;
        }

        /// <summary>Starts the rebuild when the UI is idle in the menu or hangar (called from Update).</summary>
        private void UpdateLanguage()
        {
            if (!_languageDirty || _rebuilding || _game == null)
            {
                return;
            }

            UIScreen top = _root.Stack.Top;
            if (_consentOpen || Dialogs.IsOpen || _splash.IsVisible || _splash.IsTransitioning || (top != null && top.IsTransitioning) ||
                (_root.Fader != null && _root.Fader.IsBusy))
            {
                return;
            }

            _languageDirty = false;
            _rebuilding = true;
            bool reopenSettings = _root.Stack.Contains(_settings);
            ScreenFader fader = _root.Fader;
            if (fader != null && !UITheme.ReducedMotion)
            {
                fader.FadeOut(LanguageFadeOut, UITheme.Background, () => RebuildForLanguage(reopenSettings));
            }
            else
            {
                RebuildForLanguage(reopenSettings);
            }
        }

        private void RebuildForLanguage(bool reopenSettings)
        {
            GameController game = _game;
            if (game == null || this == null)
            {
                return;
            }

            bool dailyShown = _dailyShownThisSession || (_daily != null && _daily.IsVisible);

            // Hand over now, in this frame: the old UI stops listening and gives up the singletons (Destroy is deferred
            // to the end of the frame, before rendering, so the old and new UI never draw together).
            Unsubscribe();
            _game = null;
            if (Instance == this)
            {
                Instance = null;
            }

            if (_root != null)
            {
                _root.Retire();
            }

            Destroy(gameObject);

            GameUI ui = Create(game);
            if (ui != null && ui != this)
            {
                ui.RestoreAfterLanguageRebuild(dailyShown, reopenSettings);
            }
        }

        private void RestoreAfterLanguageRebuild(bool dailyShown, bool reopenSettings)
        {
            if (dailyShown)
            {
                _dailyShownThisSession = true;
                _dailyCheckAt = -1f;
            }

            if (reopenSettings && _game != null && _game.State == GameState.Menu && _settings != null)
            {
                _root.Stack.Push(_settings, true);
            }

            ScreenFader fader = _root.Fader;
            if (fader != null && !UITheme.ReducedMotion)
            {
                fader.SetInstant(1f, UITheme.Background);
                fader.FadeIn(LanguageFadeIn);
            }
        }

        private void Unsubscribe()
        {
            Loc.LanguageChanged -= OnLanguageChanged;
            if (_subscribed && _game != null)
            {
                _game.StateChanged -= OnStateChanged;
                _game.RunEventRaised -= OnRunEvent;
                _game.LiveMissionCompleted -= OnLiveMissionCompleted;
                _game.RunFinished -= OnRunFinished;
                _game.CountdownTicked -= OnCountdownTicked;
                _game.Notify -= OnNotify;
                _game.ConsentRequested -= OnConsentRequested;
            }

            _subscribed = false;
            if (_root != null && _root.Stack != null)
            {
                _root.Stack.Changed -= OnStackChanged;
                _root.Stack.BackAtRoot = null;
            }
        }
    }
}
