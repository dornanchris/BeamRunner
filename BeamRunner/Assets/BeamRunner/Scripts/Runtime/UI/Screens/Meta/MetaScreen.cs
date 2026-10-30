using System;
using BeamRunner.Core.Meta;
using BeamRunner.Game;
using BeamRunner.Services;
using UnityEngine;

namespace BeamRunner.UI
{
    /// <summary>
    /// Base for the meta screens (menu, hangar, missions, records, settings, daily reward). Holds the bound
    /// <see cref="GameController"/>, listens to <see cref="ProfileService.Changed"/> (and optionally store availability
    /// and orientation changes) only while visible, and coalesces refreshes to at most one per frame.
    /// <para>Contract with the flow owner: <c>GameUI</c> calls <see cref="Bind"/> once right after
    /// <see cref="UIScreen.Create{T}"/> and before the first show. <see cref="UIScreen.Show"/> may run before
    /// <see cref="Bind"/> in edge cases; every hook is null-safe.</para>
    /// </summary>
    public abstract class MetaScreen : UIScreen
    {
        private ProfileService _profileSub;
        private IIapService _iapSub;
        private UIRoot _layoutSub;
        private Action _profileHandler;
        private Action _storeHandler;
        private Action _layoutHandler;
        private bool _dirty;
        private bool _layoutApplied;
        private bool _landscape;

        /// <summary>The controller this screen reads from and sends commands to (null until bound).</summary>
        protected GameController Game { get; private set; }

        /// <summary>Profile service of the bound controller (null until bound).</summary>
        protected ProfileService Profile
        {
            get { return Game != null ? Game.Profile : null; }
        }

        /// <summary>True when bound to a controller with a profile (safe to read data).</summary>
        protected bool HasData
        {
            get { return Game != null && Game.Profile != null && Game.Profile.Profile != null; }
        }

        /// <summary>Current orientation as last applied to this screen.</summary>
        protected bool Landscape
        {
            get { return _landscape; }
        }

        /// <summary>Also refresh when the store's availability/prices change.</summary>
        protected virtual bool ListensToStore
        {
            get { return false; }
        }

        /// <summary>Binds the screen to the game (called once by the flow owner before the first show).</summary>
        public void Bind(GameController game)
        {
            Game = game;
            try
            {
                OnBind();
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }

            if (IsVisible)
            {
                Subscribe();
                _dirty = true;
            }
        }

        /// <summary>Called after <see cref="Bind"/> stored the controller.</summary>
        protected virtual void OnBind()
        {
        }

        /// <summary>Pushes current data into the widgets. <paramref name="showing"/> is true on the show pass.</summary>
        protected virtual void Refresh(bool showing)
        {
        }

        /// <summary>Called on show and whenever the orientation flips while visible.</summary>
        protected virtual void ApplyLayout(bool landscape)
        {
        }

        /// <summary>Requests a refresh at the end of this frame.</summary>
        protected void MarkDirty()
        {
            _dirty = true;
        }

        protected override void OnShow()
        {
            Subscribe();
            _dirty = false;
            UpdateLayout(true);
            SafeRefresh(true);
        }

        protected override void OnHide()
        {
            Unsubscribe();
        }

        protected virtual void OnDisable()
        {
            Unsubscribe();
        }

        protected virtual void OnDestroy()
        {
            Unsubscribe();
        }

        protected virtual void LateUpdate()
        {
            if (_dirty && IsVisible)
            {
                _dirty = false;
                SafeRefresh(false);
            }
        }

        /// <summary>Plays a UI sound through the controller (no-op when unbound).</summary>
        protected void Sfx(Core.Audio.SfxId id)
        {
            if (Game != null)
            {
                Game.PlayUiSfx(id);
            }
        }

        /// <summary>Light haptic through the controller (no-op when unbound).</summary>
        protected void Haptic()
        {
            if (Game != null)
            {
                Game.UiHaptic();
            }
        }

        /// <summary>True when the player asked for fewer flashes (skip screen flashes).</summary>
        protected bool ReduceFlashing
        {
            get { return HasData && Profile.Settings != null && Profile.Settings.ReduceFlashing; }
        }

        private void SafeRefresh(bool showing)
        {
            if (!HasData)
            {
                return;
            }

            try
            {
                Refresh(showing);
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }
        }

        private void UpdateLayout(bool force)
        {
            UIRoot root = UIRoot.Instance;
            bool landscape = root != null && root.IsLandscape;
            if (!force && _layoutApplied && landscape == _landscape)
            {
                return;
            }

            _layoutApplied = true;
            _landscape = landscape;
            try
            {
                ApplyLayout(landscape);
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }
        }

        private void Subscribe()
        {
            if (_profileHandler == null)
            {
                _profileHandler = MarkDirty;
                _storeHandler = MarkDirty;
                _layoutHandler = () => UpdateLayout(false);
            }

            ProfileService profile = Profile;
            if (profile != _profileSub)
            {
                if (_profileSub != null)
                {
                    _profileSub.Changed -= _profileHandler;
                }

                _profileSub = profile;
                if (_profileSub != null)
                {
                    _profileSub.Changed += _profileHandler;
                }
            }

            IIapService iap = ListensToStore && Game != null ? Game.Iap : null;
            if (iap != _iapSub)
            {
                if (_iapSub != null)
                {
                    _iapSub.AvailabilityChanged -= _storeHandler;
                }

                _iapSub = iap;
                if (_iapSub != null)
                {
                    _iapSub.AvailabilityChanged += _storeHandler;
                }
            }

            UIRoot root = UIRoot.Instance;
            if (root != _layoutSub)
            {
                if (_layoutSub != null)
                {
                    _layoutSub.LayoutChanged -= _layoutHandler;
                }

                _layoutSub = root;
                if (_layoutSub != null)
                {
                    _layoutSub.LayoutChanged += _layoutHandler;
                }
            }
        }

        private void Unsubscribe()
        {
            if (_profileSub != null)
            {
                _profileSub.Changed -= _profileHandler;
                _profileSub = null;
            }

            if (_iapSub != null)
            {
                _iapSub.AvailabilityChanged -= _storeHandler;
                _iapSub = null;
            }

            if (_layoutSub != null)
            {
                _layoutSub.LayoutChanged -= _layoutHandler;
                _layoutSub = null;
            }
        }
    }
}
