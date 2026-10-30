using System;
using BeamRunner.Core.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// HUD part: the Daily Run ghost chip under the pause button — a "GHOST" caption over "42 M AHEAD" (you are ahead of
    /// where your best run was at this moment; chevron up), "17 M BEHIND" (chevron down), "EVEN", or "PASSED" once you
    /// flew past the point where that run ended. Shown only while a ghost is raced
    /// (<c>GameController.TryGetGhostGap</c>); change-detected, on its own canvas.
    /// </summary>
    public sealed partial class HudScreen
    {
        private const float GhostChipWidth = 320f;
        private const float GhostChipHeight = 88f;
        private const int GhostPassedKey = int.MaxValue;

        private static readonly Color GhostTint = UITheme.Hex(0xCCDBFF);

        private RectTransform _ghostChip;
        private Image _ghostIcon;
        private Text _ghostText;
        private int _ghostShown = int.MinValue;
        private bool _ghostOn;

        private void BuildGhostChip()
        {
            _ghostChip = UIFactory.CreateRect(_top, "GhostChip");
            UIFactory.SetSize(_ghostChip, GhostChipWidth, GhostChipHeight);
            UIFactory.Anchor(_ghostChip, AnchorPreset.TopLeft, new Vector2(Margin, -Margin - PauseSize - 18f));
            _ghostChip.gameObject.AddComponent<Canvas>();
            RunUi.PassiveGroup(_ghostChip);

            UIFactory.RoundedImage(_ghostChip, 28f, UITheme.PanelGlass, "Background");
            Image frame = UIFactory.CreateImage(_ghostChip, null, UITheme.Alpha(GhostTint, 0.7f), "Frame");
            UIFactory.Stretch(frame.rectTransform);
            UISprites.ApplyOutline(frame, 28f, 2f);

            _ghostIcon = UIFactory.Icon(_ghostChip, IconId.ChevronUp, 44f, UITheme.Success);
            UIFactory.Anchor(_ghostIcon.rectTransform, AnchorPreset.MiddleLeft, new Vector2(16f, 0f));

            Text caption = UIFactory.LabelLine(_ghostChip, Loc.T("hud.ghost"), 26, GhostTint, TextAnchor.MiddleLeft, UIFont.Title);
            UIFactory.SetSize(caption.rectTransform, GhostChipWidth - 84f, 32f);
            UIFactory.Anchor(caption.rectTransform, AnchorPreset.TopLeft, new Vector2(72f, -8f));

            _ghostText = UIFactory.LabelLine(_ghostChip, string.Empty, 36, UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Bold, TextGlow.Shadow);
            UIFactory.SetSize(_ghostText.rectTransform, GhostChipWidth - 84f, 44f);
            UIFactory.Anchor(_ghostText.rectTransform, AnchorPreset.BottomLeft, new Vector2(72f, 6f));
            _ghostChip.gameObject.SetActive(false);
        }

        private void ResetGhostChip()
        {
            _ghostOn = false;
            _ghostShown = int.MinValue;
            if (_ghostChip != null)
            {
                UITween.Kill(_ghostChip);
                _ghostChip.localScale = Vector3.one;
                _ghostChip.gameObject.SetActive(false);
            }
        }

        private void UpdateGhostChip()
        {
            if (_ghostChip == null || _game == null)
            {
                return;
            }

            double gap = 0.0;
            bool finished = false;
            bool on = _game.TryGetGhostGap(out gap, out finished);
            if (on != _ghostOn)
            {
                _ghostOn = on;
                _ghostShown = int.MinValue;
                _ghostChip.gameObject.SetActive(on);
                if (on && !UITheme.ReducedMotion)
                {
                    UIFactory.PopIn(_ghostChip);
                }
            }

            if (!on)
            {
                return;
            }

            int metres = (int)Math.Round(Math.Max(-99999.0, Math.Min(99999.0, gap)));
            int key = finished && metres >= 0 ? GhostPassedKey : metres;
            if (key == _ghostShown)
            {
                return;
            }

            bool passedNow = key == GhostPassedKey && _ghostShown != int.MinValue;
            _ghostShown = key;
            if (key == GhostPassedKey)
            {
                _ghostText.text = Loc.T("hud.ghost_passed");
                _ghostText.color = UITheme.Success;
                SetGhostIcon(IconId.Check, UITheme.Success);
                if (passedNow && !UITheme.ReducedMotion)
                {
                    UITween.Punch(_ghostChip, 0.25f, 0.45f);
                }
            }
            else if (metres > 0)
            {
                _ghostText.text = RunUi.Formatted("hud.ghost_ahead", metres);
                _ghostText.color = UITheme.TextPrimary;
                SetGhostIcon(IconId.ChevronUp, UITheme.Success);
            }
            else if (metres < 0)
            {
                _ghostText.text = RunUi.Formatted("hud.ghost_behind", -metres);
                _ghostText.color = UITheme.TextPrimary;
                SetGhostIcon(IconId.ChevronDown, UITheme.AccentMagenta);
            }
            else
            {
                _ghostText.text = Loc.T("hud.ghost_even");
                _ghostText.color = UITheme.TextSecondary;
                SetGhostIcon(IconId.Minus, GhostTint);
            }
        }

        private void SetGhostIcon(IconId icon, Color color)
        {
            UIFactory.SetIcon(_ghostIcon, icon);
            _ghostIcon.color = color;
        }
    }
}
