using System;
using System.Globalization;
using BeamRunner.Core.Localization;
using BeamRunner.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>Shared builders, formatting and navigation shortcuts for the meta screens.</summary>
    internal static class MetaUI
    {
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Height of the screen header (back button + title).</summary>
        public const float HeaderHeight = 132f;

        /// <summary>Top inset where content starts below the header.</summary>
        public const float HeaderBottom = 190f;

        // ------------------------------------------------------------------ formatting

        /// <summary>Grouped per language: 12,345 · 12.345 · 12 345.</summary>
        public static string N(long value)
        {
            return Loc.N(value);
        }

        /// <summary>Whole part (floored, never negative), grouped per language.</summary>
        public static string N(double value)
        {
            return Loc.N(value);
        }

        private static readonly string[] MonthKeys =
        {
            "date.month_1", "date.month_2", "date.month_3", "date.month_4", "date.month_5", "date.month_6",
            "date.month_7", "date.month_8", "date.month_9", "date.month_10", "date.month_11", "date.month_12",
        };

        /// <summary>
        /// Short calendar date per language ("10 MAR 2026", "10. MÄRZ 2026", "2026年3月10日": CJK month names are "3月").
        /// </summary>
        public static string ShortDate(DateTime d)
        {
            return Loc.T("date.short", ("d", d.Day.ToString(Inv)), ("month", Loc.T(MonthKeys[Mathf.Clamp(d.Month, 1, 12) - 1])),
                ("y", d.Year.ToString(Inv)));
        }

        /// <summary>"1,234 m" (unit and order per language).</summary>
        public static string Meters(double metres)
        {
            return Loc.T("unit.meters", ("value", N(metres)));
        }

        /// <summary>"12.3 km" / "12,3 km" (one decimal below 100 km).</summary>
        public static string Kilometers(double metres)
        {
            double km = Math.Max(0.0, metres) / 1000.0;
            return Loc.T("unit.kilometers", ("value", km >= 100.0 ? Loc.N(km, 0) : Loc.N(km, 1)));
        }

        /// <summary>"2h 05m", "12m 30s" or "45s" (unit letters per language).</summary>
        public static string Duration(double seconds)
        {
            long s = (long)Math.Floor(Math.Max(0.0, seconds));
            long h = s / 3600;
            long m = (s / 60) % 60;
            long sec = s % 60;
            if (h > 0)
            {
                return Loc.T("time.duration_hm", ("h", h.ToString(Inv)), ("m", m.ToString("00", Inv)));
            }

            if (m > 0)
            {
                return Loc.T("time.duration_ms", ("m", m.ToString(Inv)), ("s", sec.ToString("00", Inv)));
            }

            return Loc.T("time.duration_s", ("s", sec.ToString(Inv)));
        }

        public static string Hex(Color c)
        {
            return ColorUtility.ToHtmlStringRGB(c);
        }

        /// <summary>Wraps text in a rich-text colour tag.</summary>
        public static string Tint(string text, Color c)
        {
            return "<color=#" + Hex(c) + ">" + text + "</color>";
        }

        /// <summary>
        /// Upper-case name of the online platform for card titles: Google's localized product name from the string tables
        /// ("GOOGLE PLAY GAMES", "GOOGLE PLAY SPIELE"…); Apple keeps "GAME CENTER" in every language.
        /// </summary>
        public static string PlatformTitle(IOnlineService online)
        {
            if (online == null)
            {
                return string.Empty;
            }

            switch (online.Platform)
            {
                case OnlinePlatform.GooglePlayGames:
                    return Loc.T("online.platform_play_games");
                case OnlinePlatform.GameCenter:
                    return Loc.T("online.platform_game_center");
                case OnlinePlatform.Simulated:
                    return Loc.T("online.platform_simulated");
                default:
                    return string.Empty;
            }
        }

        // ------------------------------------------------------------------ navigation (GameUI is the flow owner)

        public static void ShowMissions()
        {
            GameUI ui = GameUI.Instance;
            if (ui != null)
            {
                ui.ShowMissions();
            }
        }

        public static void ShowUpgrades()
        {
            GameUI ui = GameUI.Instance;
            if (ui != null)
            {
                ui.ShowUpgrades();
            }
        }

        public static void ShowRecords()
        {
            GameUI ui = GameUI.Instance;
            if (ui != null)
            {
                ui.ShowRecords();
            }
        }

        public static void ShowSettings()
        {
            GameUI ui = GameUI.Instance;
            if (ui != null)
            {
                ui.ShowSettings();
            }
        }

        public static void ShowDailyReward()
        {
            GameUI ui = GameUI.Instance;
            if (ui != null)
            {
                ui.ShowDailyReward();
            }
        }

        public static void CloseTop()
        {
            GameUI ui = GameUI.Instance;
            if (ui != null)
            {
                ui.CloseTop();
                return;
            }

            UIRoot root = UIRoot.Instance;
            if (root != null)
            {
                root.Stack.Pop();
            }
        }

        public static void Toast(string text, IconId icon = IconId.Info)
        {
            GameUI ui = GameUI.Instance;
            if (ui != null)
            {
                ui.Toast(text, icon);
                return;
            }

            Toasts.Show(text, icon, UITheme.Accent);
        }

        // ------------------------------------------------------------------ builders

        /// <summary>Full-screen dim layer that also covers the unsafe edges. Blocks raycasts.</summary>
        public static Image Dimmer(RectTransform parent, float alpha, bool raycast = true)
        {
            Image img = UIFactory.CreateImage(parent, null, Color.white, "Dimmer", raycast);
            UIFactory.Stretch(img.rectTransform, -600f, -600f, -600f, -600f);
            Color top = UITheme.Alpha(UITheme.Background, Mathf.Clamp01(alpha * 1.05f));
            Color bottom = UITheme.Alpha(UITheme.BackgroundAlt, Mathf.Clamp01(alpha * 0.92f));
            UIGradient.Apply(img, top, bottom, GradientDirection.Vertical);
            img.transform.SetAsFirstSibling();
            return img;
        }

        /// <summary>Vertical fade from dark to transparent along the top or bottom edge (text legibility over 3D).</summary>
        public static Image Scrim(RectTransform parent, bool top, float height, float alpha)
        {
            Image img = UIFactory.CreateImage(parent, null, Color.white, top ? "ScrimTop" : "ScrimBottom");
            RectTransform rt = img.rectTransform;
            rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
            // Extend past the safe area (notches / home indicator) and the sides.
            rt.offsetMin = new Vector2(-600f, top ? -height : -400f);
            rt.offsetMax = new Vector2(600f, top ? 400f : height);
            Color dark = UITheme.Alpha(UITheme.Background, alpha);
            Color clear = UITheme.Alpha(UITheme.Background, 0f);
            UIGradient.Apply(img, top ? dark : clear, top ? clear : dark, GradientDirection.Vertical);
            img.transform.SetAsFirstSibling();
            return img;
        }

        /// <summary>Header row: back button on the left, neon title centred. Returns the header rect.</summary>
        public static RectTransform Header(RectTransform parent, string title, Action onBack, out NeonTitle titleText,
            IconId backIcon = IconId.Back)
        {
            RectTransform bar = UIFactory.CreateRect(parent, "Header");
            bar.sizeDelta = new Vector2(0f, HeaderHeight);
            UIFactory.Anchor(bar, AnchorPreset.StretchTop, new Vector2(0f, -24f));

            titleText = UIFactory.NeonTitle(bar, title, UITheme.TextH2);
            RectTransform trt = titleText.Rect;
            UIFactory.Stretch(trt, 150f, 0f, 150f, 0f);

            NeonButton back = UIFactory.IconButton(bar, backIcon, onBack, 120f);
            UIFactory.Anchor(back.Rect, AnchorPreset.MiddleLeft, new Vector2(0f, 0f));

            // Gamepad/keyboard entry point: without a DefaultSelection the stack focuses nothing when this overlay opens
            // (the previous screen's button is hidden), so d-pad / arrows did nothing and the overlay was unreachable.
            // includeInactive: deferred screens are built while inactive (UIScreen.CreateDeferred).
            UIScreen screen = parent.GetComponentInParent<UIScreen>(true);
            if (screen != null && screen.DefaultSelection == null)
            {
                screen.DefaultSelection = back.gameObject;
            }

            return bar;
        }

        /// <summary>Section caption with icon and accent underline (for scroll lists).</summary>
        public static void Section(Transform parent, string text, IconId icon, Color? color = null)
        {
            RectTransform row = UIFactory.CreateRect(parent, "Section_" + text);
            HorizontalLayoutGroup h = UIFactory.HStack(row, UITheme.SpaceSm, UIFactory.Padding(8, 8, 12, 0), TextAnchor.MiddleLeft, true);
            h.childForceExpandWidth = false;
            UIFactory.SetLayout(row, -1f, 72f);
            Image ic = UIFactory.Icon(row, icon, 44f, color ?? UITheme.Accent);
            if (!color.HasValue)
            {
                UIAccentFollower.Attach(ic, AccentSlot.Primary);
            }

            UIFactory.LabelLine(row, text, UITheme.TextSmall, UITheme.TextSecondary, TextAnchor.MiddleLeft, UIFont.Title, TextGlow.Shadow);
        }

        /// <summary>[gem icon][amount] inline group. Returns the amount label.</summary>
        public static Text GemAmount(Transform parent, string amount, int size, Color? textColor = null, float iconScale = 1f,
            UIFont font = UIFont.Bold, TextAnchor align = TextAnchor.MiddleLeft)
        {
            RectTransform row = UIFactory.CreateRect(parent, "GemAmount");
            row.sizeDelta = new Vector2(600f, Mathf.Ceil(size * 1.3f));
            HorizontalLayoutGroup h = UIFactory.HStack(row, Mathf.Round(size * 0.14f), null, align, true);
            h.childForceExpandWidth = false;
            float iconSize = Mathf.Round(size * 0.95f * iconScale);
            UIFactory.Icon(row, IconId.Gem, iconSize, UITheme.GemColor);
            Text t = UIFactory.LabelLine(row, amount, size, textColor ?? UITheme.TextPrimary, TextAnchor.MiddleLeft, font, TextGlow.Shadow);
            UIFactory.SetLayout(row, -1f, Mathf.Ceil(size * 1.3f));
            return t;
        }

        /// <summary>Small glass chip with a gem icon and "+amount" (mission/achievement rewards).</summary>
        public static Text RewardChip(Transform parent, int amount, out Image background)
        {
            RectTransform chip = UIFactory.CreateRect(parent, "RewardChip");
            background = UIFactory.PillImage(chip, UITheme.PanelGlassLight, "Plate");
            UIFactory.IgnoreLayout(background);
            HorizontalLayoutGroup h = UIFactory.HStack(chip, 6f, UIFactory.Padding(18, 22, 0, 0), TextAnchor.MiddleCenter, true);
            h.childForceExpandWidth = false;
            UIFactory.SetLayout(chip, -1f, 72f);
            UIFactory.Icon(chip, IconId.Gem, 40f, UITheme.GemColor);
            Text t = UIFactory.LabelLine(chip, "+" + N(amount), 38, UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Bold);
            return t;
        }

        /// <summary>Round emblem (glow, glass disc, accent ring) for icons and numbers.</summary>
        public static RectTransform Emblem(Transform parent, float size, Color ring, out Image ringImage, out Image glow)
        {
            RectTransform e = UIFactory.CreateRect(parent, "Emblem");
            e.sizeDelta = new Vector2(size, size);
            UIFactory.SetLayout(e, size, size);
            glow = UIFactory.CreateImage(e, UISprites.Glow, UITheme.Alpha(ring, 0.45f), "Glow");
            UIFactory.Stretch(glow.rectTransform, -size * 0.35f, -size * 0.35f, -size * 0.35f, -size * 0.35f);
            Image disc = UIFactory.CreateImage(e, UISprites.Circle, UITheme.PanelGlass, "Disc");
            UIFactory.Stretch(disc.rectTransform);
            ringImage = UIFactory.CreateImage(e, UISprites.Ring(14), ring, "Ring");
            UIFactory.Stretch(ringImage.rectTransform);
            return e;
        }

        /// <summary>Converts a widget's position into <paramref name="space"/>'s local coordinates (centre-pivot space).</summary>
        public static Vector2 LocalPoint(RectTransform space, Transform widget)
        {
            if (space == null || widget == null)
            {
                return Vector2.zero;
            }

            Vector3 world = widget.position;
            RectTransform wrt = widget as RectTransform;
            if (wrt != null)
            {
                world = wrt.TransformPoint(wrt.rect.center);
            }

            Vector3 local = space.InverseTransformPoint(world);
            Vector2 pivotOffset = (space.pivot - new Vector2(0.5f, 0.5f)) * space.rect.size;
            return new Vector2(local.x, local.y) + pivotOffset;
        }

        /// <summary>Screen flash (skipped with reduced flashing).</summary>
        public static void Flash(Color color, float peak, bool reduceFlashing)
        {
            if (reduceFlashing)
            {
                return;
            }

            UIRoot root = UIRoot.Instance;
            if (root != null && root.Fader != null)
            {
                root.Fader.Flash(color, peak, 0.35f);
            }
        }
    }
}
