using System;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>Widget builders (see the class summary in UIFactory.cs).</summary>
    public static partial class UIFactory
    {
        /// <summary>
        /// Pill button. Height defaults per style (Primary 152, Secondary/Danger 128, Ghost 96); width grows with the
        /// label (min 480 for Primary, at most the parent's width — then the label shrinks to fit) unless a layout group
        /// controls it. Labels are shown exactly as given: display styles expect upper-case text from the string tables
        /// (no runtime case changes, which break Turkish dotted I and German ß).
        /// </summary>
        public static NeonButton Button(Transform parent, string label, ButtonStyle style = ButtonStyle.Primary, Action onClick = null,
            IconId icon = IconId.None, string subLabel = null, float height = -1f, float minWidth = -1f)
        {
            if (height <= 0f)
            {
                height = style == ButtonStyle.Primary ? UITheme.ButtonHeightLarge : (style == ButtonStyle.Ghost ? UITheme.ButtonHeightSmall : UITheme.ButtonHeight);
            }

            if (minWidth < 0f)
            {
                minWidth = style == ButtonStyle.Primary ? 480f : (style == ButtonStyle.Ghost ? 0f : 320f);
            }

            RectTransform rt = CreateRect(parent, "Button_" + (label ?? string.Empty));
            rt.sizeDelta = new Vector2(Mathf.Max(minWidth, 200f), height);
            rt.gameObject.AddComponent<UIRaycastTarget>();
            NeonButton b = rt.gameObject.AddComponent<NeonButton>();
            b.Build(style, label, icon, subLabel, height, minWidth);
            b.OnClick(onClick);
            return b;
        }

        /// <summary>Round icon button (default 132 units — a comfortable touch target).</summary>
        public static NeonButton IconButton(Transform parent, IconId icon, Action onClick, float size = UITheme.IconButtonSize,
            ButtonStyle style = ButtonStyle.Secondary)
        {
            RectTransform rt = CreateRect(parent, "IconButton_" + icon);
            rt.gameObject.AddComponent<UIRaycastTarget>();
            NeonButton b = rt.gameObject.AddComponent<NeonButton>();
            b.BuildRound(style, icon, size);
            b.OnClick(onClick);
            return b;
        }

        /// <summary>Glass panel with neon frame; add children to <see cref="GlassPanel.Content"/>. Accent null = follow theme.</summary>
        public static GlassPanel GlassPanel(Transform parent, Color? accent = null, float radius = UITheme.RadiusLg,
            float padding = UITheme.SpaceXl, bool scanlines = true, bool shadow = true, bool frame = true)
        {
            RectTransform rt = CreateRect(parent, "GlassPanel");
            rt.sizeDelta = new Vector2(900f, 600f);
            GlassPanel p = rt.gameObject.AddComponent<GlassPanel>();
            p.Build(accent, radius, padding, scanlines, shadow, frame);
            return p;
        }

        /// <summary>Display title with layered neon glow and gentle breathing/flicker. Colours null = follow theme.</summary>
        public static NeonTitle NeonTitle(Transform parent, string text, int size = UITheme.TextH1, TextAnchor align = TextAnchor.MiddleCenter,
            Color? primary = null, Color? secondary = null)
        {
            RectTransform rt = CreateRect(parent, "NeonTitle");
            NeonTitle t = rt.gameObject.AddComponent<NeonTitle>();
            t.Build(text, size, align, primary, secondary);
            return t;
        }

        /// <summary>Neon slider (88 units tall, knob 60).</summary>
        public static NeonSlider Slider(Transform parent, float value, Action<float> onChanged, float min = 0f, float max = 1f,
            bool wholeNumbers = false, float width = 600f)
        {
            RectTransform rt = CreateRect(parent, "Slider");
            NeonSlider s = rt.gameObject.AddComponent<NeonSlider>();
            s.Build(value, min, max, wholeNumbers, onChanged, width);
            return s;
        }

        /// <summary>Pill switch (136×76).</summary>
        public static NeonToggle Toggle(Transform parent, bool value, Action<bool> onChanged)
        {
            RectTransform rt = CreateRect(parent, "Toggle");
            NeonToggle t = rt.gameObject.AddComponent<NeonToggle>();
            t.Build(value, onChanged);
            return t;
        }

        /// <summary>N-option selector with a sliding thumb.</summary>
        public static SegmentedControl SegmentedControl(Transform parent, string[] options, int selected, Action<int> onChanged,
            float height = 96f)
        {
            RectTransform rt = CreateRect(parent, "Segmented");
            SegmentedControl s = rt.gameObject.AddComponent<SegmentedControl>();
            s.Build(options ?? new string[0], selected, onChanged, height);
            return s;
        }

        /// <summary>Horizontal progress bar (width from layout/anchors). Colours null = follow theme.</summary>
        public static ProgressBar ProgressBar(Transform parent, float value = 0f, float height = 24f, Color? from = null, Color? to = null)
        {
            RectTransform rt = CreateRect(parent, "ProgressBar");
            ProgressBar p = rt.gameObject.AddComponent<ProgressBar>();
            p.Build(value, height, from, to);
            return p;
        }

        /// <summary>Radial progress ring with centre label.</summary>
        public static RingProgress RingProgress(Transform parent, float size = 220f, float value = 0f, float thickness = 16f, Color? color = null)
        {
            RectTransform rt = CreateRect(parent, "RingProgress");
            RingProgress r = rt.gameObject.AddComponent<RingProgress>();
            r.Build(size, thickness, value, color);
            return r;
        }

        /// <summary>Badge pinned to the top-right corner of <paramref name="parent"/>: -1 = dot, 0 = hidden, n = count.</summary>
        public static Badge Badge(Transform parent, int count = -1, Color? color = null)
        {
            RectTransform rt = CreateRect(parent, "Badge");
            Badge b = rt.gameObject.AddComponent<Badge>();
            b.Build(count, color);
            return b;
        }

        /// <summary>Currency pill (icon + animated count). Pass <paramref name="onPlus"/> to add a "+" (shop) button.</summary>
        public static CurrencyPill CurrencyPill(Transform parent, long amount, IconId icon = IconId.Gem, Color? iconColor = null,
            Action onPlus = null)
        {
            RectTransform rt = CreateRect(parent, "CurrencyPill");
            CurrencyPill c = rt.gameObject.AddComponent<CurrencyPill>();
            c.Build(amount, icon, iconColor, onPlus);
            return c;
        }

        /// <summary>Vertical scroll list; stretch or size its rect, then add items to <see cref="ScrollList.Content"/>.</summary>
        public static ScrollList ScrollList(Transform parent, float spacing = UITheme.SpaceMd, RectOffset padding = null, float fadeEdge = 40f)
        {
            RectTransform rt = CreateRect(parent, "ScrollList");
            ScrollList s = rt.gameObject.AddComponent<ScrollList>();
            s.Build(spacing, padding, fadeEdge);
            return s;
        }

        /// <summary>Glass card with header (icon/title/right slot) and body; height follows content.</summary>
        public static Card Card(Transform parent, string title, IconId icon = IconId.None, Color? accent = null, float padding = 40f)
        {
            RectTransform rt = CreateRect(parent, "Card");
            Card c = rt.gameObject.AddComponent<Card>();
            c.Build(title, icon, accent, padding);
            return c;
        }

        /// <summary>Label-left / value-right row (88 units) with optional icon and bottom divider.</summary>
        public static StatRow StatRow(Transform parent, string label, string value, IconId icon = IconId.None, bool divider = true,
            float height = 88f)
        {
            RectTransform rt = CreateRect(parent, "StatRow");
            StatRow s = rt.gameObject.AddComponent<StatRow>();
            s.Build(label, value, icon, divider, height);
            return s;
        }

        /// <summary>Grid of caption/value cells, <paramref name="columns"/> per row.</summary>
        public static KeyValueGrid KeyValueGrid(Transform parent, int columns = 2, int valueSize = 56)
        {
            RectTransform rt = CreateRect(parent, "KeyValueGrid");
            KeyValueGrid g = rt.gameObject.AddComponent<KeyValueGrid>();
            g.Build(columns, valueSize);
            return g;
        }

        /// <summary>Settings-style row: [icon] label ........ [switch]. Returns the switch.</summary>
        public static NeonToggle ToggleRow(Transform parent, string label, bool value, Action<bool> onChanged, IconId icon = IconId.None)
        {
            RectTransform row = SettingRow(parent, label, icon);
            return Toggle(row, value, onChanged);
        }

        /// <summary>Settings-style row with a slider on the right (label above-left on narrow widths). Returns the slider.</summary>
        public static NeonSlider SliderRow(Transform parent, string label, float value, Action<float> onChanged, IconId icon = IconId.None,
            float sliderWidth = 440f)
        {
            RectTransform row = SettingRow(parent, label, icon);
            return Slider(row, value, onChanged, 0f, 1f, false, sliderWidth);
        }

        /// <summary>Empty settings row ([icon] label, flexible gap); add a control as the last child.</summary>
        public static RectTransform SettingRow(Transform parent, string label, IconId icon = IconId.None, float height = UITheme.RowHeight)
        {
            RectTransform row = CreateRect(parent, "Row_" + label);
            row.sizeDelta = new Vector2(900f, height);
            HorizontalLayoutGroup h = HStack(row, UITheme.SpaceMd, Padding(8, 8, 0, 0), TextAnchor.MiddleLeft, true);
            h.childForceExpandWidth = false;
            SetLayout(row, -1f, height);
            if (icon != IconId.None)
            {
                Image ic = Icon(row, icon, UITheme.IconSizeSmall + 4f, UITheme.Accent);
                UIAccentFollower.Attach(ic, AccentSlot.Primary);
            }

            Text t = Label(row, label, UITheme.TextLead, UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Bold);
            SetLayout(t, -1f, -1f, 1f);
            return row;
        }

        /// <summary>
        /// Stretched child whose width is clamped to <paramref name="maxWidth"/> and centred — keeps portrait-designed
        /// content readable on landscape phones, tablets and desktop.
        /// </summary>
        public static RectTransform ContentColumn(Transform parent, float maxWidth = UITheme.ContentMaxWidth, float padding = UITheme.ScreenPadding)
        {
            RectTransform rt = CreateFill(parent, "Column");
            UIMaxWidth clamp = rt.gameObject.AddComponent<UIMaxWidth>();
            clamp.MinInset = padding;
            clamp.MaxWidth = maxWidth;
            return rt;
        }

        /// <summary>
        /// Full-screen synthwave backdrop: deep gradient, two slowly breathing accent glows and a vignette (about four
        /// full-screen layers of overdraw — use behind menus, not over gameplay). <paramref name="opacity"/> &lt; 1 lets the
        /// 3D scene show through; <paramref name="scanlines"/> adds a faint CRT texture.
        /// </summary>
        public static RectTransform Backdrop(Transform parent, float opacity = 1f, bool scanlines = false)
        {
            RectTransform root = CreateFill(parent, "Backdrop");
            CanvasGroup group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = Mathf.Clamp01(opacity);
            group.blocksRaycasts = false;
            group.interactable = false;

            Image bg = CreateImage(root, null, Color.white, "Gradient");
            Stretch(bg.rectTransform);
            UIGradient.Apply(bg, UITheme.BackgroundAlt, UITheme.Background, GradientDirection.Vertical);

            Image glowA = CreateImage(root, UISprites.Glow, UITheme.AccentAlt, "GlowA");
            RectTransform ga = glowA.rectTransform;
            ga.anchorMin = new Vector2(-0.35f, 0.55f);
            ga.anchorMax = new Vector2(0.65f, 1.1f);
            ga.offsetMin = Vector2.zero;
            ga.offsetMax = Vector2.zero;
            UIAccentFollower.Attach(glowA, AccentSlot.Secondary, 0.3f);
            Breathe(glowA, 0.55f, 1f, 7f);

            Image glowB = CreateImage(root, UISprites.Glow, UITheme.Accent, "GlowB");
            RectTransform gb = glowB.rectTransform;
            gb.anchorMin = new Vector2(0.4f, -0.1f);
            gb.anchorMax = new Vector2(1.35f, 0.4f);
            gb.offsetMin = Vector2.zero;
            gb.offsetMax = Vector2.zero;
            UIAccentFollower.Attach(glowB, AccentSlot.Primary, 0.22f);
            Breathe(glowB, 0.5f, 1f, 9f);

            if (scanlines)
            {
                Image scan = CreateImage(root, null, new Color(1f, 1f, 1f, 0.03f), "Scanlines");
                Stretch(scan.rectTransform);
                UISprites.ApplyTiled(scan, UISprites.Scanlines);
            }

            Image vignette = CreateImage(root, UISprites.Vignette, new Color(0f, 0f, 0.01f, 0.85f), "Vignette");
            Stretch(vignette.rectTransform);
            return root;
        }
    }
}
