using System;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>Anchor presets for <see cref="UIFactory.Anchor"/> (also set the pivot to match).</summary>
    public enum AnchorPreset
    {
        TopLeft = 0,
        TopCenter,
        TopRight,
        MiddleLeft,
        Center,
        MiddleRight,
        BottomLeft,
        BottomCenter,
        BottomRight,
        /// <summary>Full width along the top edge (height from sizeDelta.y).</summary>
        StretchTop,
        /// <summary>Full width, vertically centred.</summary>
        StretchMiddle,
        StretchBottom,
        /// <summary>Full height along the left edge (width from sizeDelta.x).</summary>
        StretchLeft,
        /// <summary>Full height, horizontally centred.</summary>
        StretchCenter,
        StretchRight,
        StretchAll,
    }

    /// <summary>Text treatments applied with Shadow/Outline mesh effects.</summary>
    public enum TextGlow
    {
        None = 0,

        /// <summary>Soft dark drop shadow for readability over busy backgrounds.</summary>
        Shadow = 1,

        /// <summary>Coloured halo (accent by default).</summary>
        Glow = 2,

        /// <summary>Coloured halo plus dark drop shadow — titles and big numbers.</summary>
        Neon = 3,
    }

    /// <summary>
    /// Static builders for the BeamRunner UI kit. Every builder parents the new object under <c>parent</c>, uses the
    /// bundled fonts and theme tokens, and returns the component a screen needs to bind data. Widgets carry a
    /// <see cref="LayoutElement"/> with sensible preferred sizes so they work both inside layout groups and with anchors.
    /// Conventions: sizes are canvas units on the 1080×1920 reference; decoration children never block raycasts.
    /// </summary>
    public static partial class UIFactory
    {
        /// <summary>Invoked on every button/toggle/segment click. The audio layer assigns this.</summary>
        public static Action OnClickSound;

        /// <summary>Invoked on every click for a light haptic tick. The haptics layer assigns this.</summary>
        public static Action OnHaptic;

        /// <summary>Raises <see cref="OnClickSound"/> and/or <see cref="OnHaptic"/> safely.</summary>
        public static void PlayClickFeedback(bool sound = true, bool haptic = true)
        {
            if (sound)
            {
                SafeInvoke(OnClickSound);
            }

            if (haptic)
            {
                SafeInvoke(OnHaptic);
            }
        }

        // ------------------------------------------------------------------ primitives

        /// <summary>Empty RectTransform (anchors centred, size 100×100).</summary>
        public static RectTransform CreateRect(Transform parent, string name = "Rect")
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            if (parent != null)
            {
                rt.SetParent(parent, false);
            }

            go.layer = parent != null ? parent.gameObject.layer : 5;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(100f, 100f);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        /// <summary>Full-size child rect (stretched over the parent).</summary>
        public static RectTransform CreateFill(Transform parent, string name = "Fill")
        {
            return Stretch(CreateRect(parent, name));
        }

        /// <summary>Image with a sprite (null = plain white) and colour. Decoration by default (no raycasts).</summary>
        public static Image CreateImage(Transform parent, Sprite sprite, Color color, string name = "Image", bool raycast = false)
        {
            RectTransform rt = CreateRect(parent, name);
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        /// <summary>Stretched rounded-rect image (radius in units).</summary>
        public static Image RoundedImage(Transform parent, float radius, Color color, string name = "Rounded", bool raycast = false)
        {
            Image img = CreateImage(parent, null, color, name, raycast);
            Stretch(img.rectTransform);
            UIShape.Rounded(img, radius);
            return img;
        }

        /// <summary>Stretched pill image (radius follows the rect's shorter side).</summary>
        public static Image PillImage(Transform parent, Color color, string name = "Pill", bool raycast = false)
        {
            Image img = CreateImage(parent, null, color, name, raycast);
            Stretch(img.rectTransform);
            UIShape.MakePill(img);
            return img;
        }

        /// <summary>Icon image (preserves aspect). Default colour: primary text.</summary>
        public static Image Icon(Transform parent, IconId id, float size = UITheme.IconSize, Color? color = null)
        {
            Image img = CreateImage(parent, UIIcons.Get(id), color ?? UITheme.TextPrimary, "Icon_" + id);
            img.preserveAspect = true;
            img.rectTransform.sizeDelta = new Vector2(size, size);
            SetLayout(img, size, size);
            img.enabled = id != IconId.None;
            return img;
        }

        /// <summary>Changes the sprite of an icon image created by <see cref="Icon"/>.</summary>
        public static void SetIcon(Image image, IconId id)
        {
            if (image == null)
            {
                return;
            }

            image.sprite = UIIcons.Get(id);
            image.enabled = id != IconId.None;
        }

        /// <summary>
        /// Text label using the kit fonts. Wraps horizontally, overflows vertically (never clipped), best-fit off,
        /// no raycasts. Default colour: primary text.
        /// </summary>
        public static Text Label(Transform parent, string text, int size = UITheme.TextBody, Color? color = null,
            TextAnchor align = TextAnchor.MiddleLeft, UIFont font = UIFont.Body, TextGlow glow = TextGlow.None)
        {
            RectTransform rt = CreateRect(parent, "Label");
            Text t = rt.gameObject.AddComponent<Text>();
            t.font = UIFonts.Get(font);
            t.fontSize = size;
            t.fontStyle = FontStyle.Normal;
            t.color = color ?? UITheme.TextPrimary;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.resizeTextForBestFit = false;
            t.supportRichText = true;
            t.lineSpacing = font == UIFont.Title ? 1.05f : 0.95f;
            t.raycastTarget = false;
            t.text = text ?? string.Empty;
            rt.sizeDelta = new Vector2(800f, Mathf.Ceil(size * 1.35f));
            if (glow != TextGlow.None)
            {
                AddGlow(t, glow);
            }

            return t;
        }

        /// <summary>
        /// Single-line label (no wrapping) — for buttons, numbers and chips. With <paramref name="fit"/> (default) a
        /// <see cref="UITextFit"/> shrinks the font when the text is wider than the label's rect (longer translations);
        /// text that fits keeps its design size.
        /// </summary>
        public static Text LabelLine(Transform parent, string text, int size = UITheme.TextBody, Color? color = null,
            TextAnchor align = TextAnchor.MiddleCenter, UIFont font = UIFont.Bold, TextGlow glow = TextGlow.None, bool fit = true)
        {
            Text t = Label(parent, text, size, color, align, font, glow);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            if (fit)
            {
                UITextFit.Attach(t);
            }

            return t;
        }

        /// <summary>Adds Shadow/Outline layers to a text. <paramref name="color"/> defaults to the current accent.</summary>
        public static void AddGlow(Text text, TextGlow glow, Color? color = null)
        {
            if (text == null || glow == TextGlow.None)
            {
                return;
            }

            Color c = color ?? UITheme.Accent;
            if (glow == TextGlow.Glow || glow == TextGlow.Neon)
            {
                Outline halo = text.gameObject.AddComponent<Outline>();
                halo.effectColor = UITheme.Alpha(c, 0.38f);
                float d = Mathf.Clamp(text.fontSize * 0.035f, 1.5f, 4f);
                halo.effectDistance = new Vector2(d, -d);
                halo.useGraphicAlpha = true;
            }

            if (glow == TextGlow.Shadow || glow == TextGlow.Neon)
            {
                Shadow drop = text.gameObject.AddComponent<Shadow>();
                drop.effectColor = new Color(0f, 0f, 0.02f, 0.55f);
                float d = Mathf.Clamp(text.fontSize * 0.05f, 2f, 6f);
                drop.effectDistance = new Vector2(0f, -d);
                drop.useGraphicAlpha = true;
            }
        }

        /// <summary>Plain rounded panel (solid/translucent fill, no frame).</summary>
        public static Image Panel(Transform parent, Color? color = null, float radius = UITheme.RadiusLg)
        {
            RectTransform rt = CreateRect(parent, "Panel");
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = color ?? UITheme.PanelGlass;
            img.raycastTarget = true;
            UIShape.Rounded(img, radius);
            return img;
        }

        /// <summary>Horizontal hairline with faded ends (use in vertical layouts). Default: white 16%.</summary>
        public static Image Divider(Transform parent, Color? color = null, float thickness = 2f)
        {
            Image img = CreateImage(parent, UISprites.FadeLine, color ?? new Color(1f, 1f, 1f, 0.16f), "Divider");
            img.type = Image.Type.Simple;
            img.rectTransform.sizeDelta = new Vector2(800f, thickness);
            LayoutElement le = SetLayout(img, -1f, thickness);
            le.minHeight = thickness;
            le.flexibleWidth = 1f;
            return img;
        }

        /// <summary>Empty layout element of fixed size (or flexible filler).</summary>
        public static LayoutElement Spacer(Transform parent, float size, bool flexible = false)
        {
            RectTransform rt = CreateRect(parent, "Spacer");
            LayoutElement le = rt.gameObject.AddComponent<LayoutElement>();
            le.minWidth = size;
            le.minHeight = size;
            le.preferredWidth = size;
            le.preferredHeight = size;
            if (flexible)
            {
                le.flexibleWidth = 1f;
                le.flexibleHeight = 1f;
            }

            rt.sizeDelta = new Vector2(size, size);
            return le;
        }

        // ------------------------------------------------------------------ layout groups

        /// <summary>
        /// Vertical stack: controls child width (stretched) and height (preferred), no force-expand. Put it on an existing
        /// rect with <paramref name="onSelf"/> or create a child container.
        /// </summary>
        public static VerticalLayoutGroup VStack(Transform parent, float spacing = UITheme.SpaceMd, RectOffset padding = null,
            TextAnchor align = TextAnchor.UpperCenter, bool onSelf = false)
        {
            GameObject go = onSelf ? parent.gameObject : CreateRect(parent, "VStack").gameObject;
            VerticalLayoutGroup v = go.GetComponent<VerticalLayoutGroup>();
            if (v == null)
            {
                v = go.AddComponent<VerticalLayoutGroup>();
            }

            v.spacing = spacing;
            v.padding = padding ?? new RectOffset();
            v.childAlignment = align;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childScaleWidth = false;
            v.childScaleHeight = false;
            return v;
        }

        /// <summary>Horizontal stack: controls child sizes (preferred), vertically centred, no force-expand.</summary>
        public static HorizontalLayoutGroup HStack(Transform parent, float spacing = UITheme.SpaceMd, RectOffset padding = null,
            TextAnchor align = TextAnchor.MiddleCenter, bool onSelf = false)
        {
            GameObject go = onSelf ? parent.gameObject : CreateRect(parent, "HStack").gameObject;
            HorizontalLayoutGroup h = go.GetComponent<HorizontalLayoutGroup>();
            if (h == null)
            {
                h = go.AddComponent<HorizontalLayoutGroup>();
            }

            h.spacing = spacing;
            h.padding = padding ?? new RectOffset();
            h.childAlignment = align;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.childScaleWidth = false;
            h.childScaleHeight = false;
            return h;
        }

        /// <summary>Adds a ContentSizeFitter (preferred size on the chosen axes).</summary>
        public static ContentSizeFitter FitContent(Component c, bool horizontal, bool vertical)
        {
            ContentSizeFitter f = c.GetComponent<ContentSizeFitter>();
            if (f == null)
            {
                f = c.gameObject.AddComponent<ContentSizeFitter>();
            }

            f.horizontalFit = horizontal ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            f.verticalFit = vertical ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            return f;
        }

        /// <summary>Gets or adds a LayoutElement and sets the given values (negative = leave unset).</summary>
        public static LayoutElement SetLayout(Component c, float preferredWidth = -1f, float preferredHeight = -1f,
            float flexibleWidth = -1f, float flexibleHeight = -1f)
        {
            LayoutElement le = c.GetComponent<LayoutElement>();
            if (le == null)
            {
                le = c.gameObject.AddComponent<LayoutElement>();
            }

            if (preferredWidth >= 0f)
            {
                le.preferredWidth = preferredWidth;
            }

            if (preferredHeight >= 0f)
            {
                le.preferredHeight = preferredHeight;
                le.minHeight = preferredHeight;
            }

            if (flexibleWidth >= 0f)
            {
                le.flexibleWidth = flexibleWidth;
            }

            if (flexibleHeight >= 0f)
            {
                le.flexibleHeight = flexibleHeight;
            }

            return le;
        }

        /// <summary>Excludes <paramref name="c"/> from its parent's layout group (decoration layers).</summary>
        public static LayoutElement IgnoreLayout(Component c)
        {
            LayoutElement le = c.GetComponent<LayoutElement>();
            if (le == null)
            {
                le = c.gameObject.AddComponent<LayoutElement>();
            }

            le.ignoreLayout = true;
            return le;
        }

        public static RectOffset Padding(int all)
        {
            return new RectOffset(all, all, all, all);
        }

        public static RectOffset Padding(int horizontal, int vertical)
        {
            return new RectOffset(horizontal, horizontal, vertical, vertical);
        }

        public static RectOffset Padding(int left, int right, int top, int bottom)
        {
            return new RectOffset(left, right, top, bottom);
        }

        // ------------------------------------------------------------------ rect helpers

        /// <summary>Stretches over the parent with insets (units).</summary>
        public static RectTransform Stretch(RectTransform rt, float left = 0f, float top = 0f, float right = 0f, float bottom = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>Uniform inset of a stretched rect.</summary>
        public static RectTransform Pad(RectTransform rt, float all)
        {
            return Stretch(rt, all, all, all, all);
        }

        public static RectTransform Pad(RectTransform rt, float horizontal, float vertical)
        {
            return Stretch(rt, horizontal, vertical, horizontal, vertical);
        }

        /// <summary>Applies an anchor preset (anchors + matching pivot) and positions at <paramref name="offset"/>.</summary>
        public static RectTransform Anchor(RectTransform rt, AnchorPreset preset, Vector2 offset = default)
        {
            Vector2 min;
            Vector2 max;
            Vector2 pivot;
            switch (preset)
            {
                case AnchorPreset.TopLeft:
                    min = max = pivot = new Vector2(0f, 1f);
                    break;
                case AnchorPreset.TopCenter:
                    min = max = pivot = new Vector2(0.5f, 1f);
                    break;
                case AnchorPreset.TopRight:
                    min = max = pivot = new Vector2(1f, 1f);
                    break;
                case AnchorPreset.MiddleLeft:
                    min = max = pivot = new Vector2(0f, 0.5f);
                    break;
                case AnchorPreset.MiddleRight:
                    min = max = pivot = new Vector2(1f, 0.5f);
                    break;
                case AnchorPreset.BottomLeft:
                    min = max = pivot = new Vector2(0f, 0f);
                    break;
                case AnchorPreset.BottomCenter:
                    min = max = pivot = new Vector2(0.5f, 0f);
                    break;
                case AnchorPreset.BottomRight:
                    min = max = pivot = new Vector2(1f, 0f);
                    break;
                case AnchorPreset.StretchTop:
                    min = new Vector2(0f, 1f);
                    max = new Vector2(1f, 1f);
                    pivot = new Vector2(0.5f, 1f);
                    break;
                case AnchorPreset.StretchMiddle:
                    min = new Vector2(0f, 0.5f);
                    max = new Vector2(1f, 0.5f);
                    pivot = new Vector2(0.5f, 0.5f);
                    break;
                case AnchorPreset.StretchBottom:
                    min = new Vector2(0f, 0f);
                    max = new Vector2(1f, 0f);
                    pivot = new Vector2(0.5f, 0f);
                    break;
                case AnchorPreset.StretchLeft:
                    min = new Vector2(0f, 0f);
                    max = new Vector2(0f, 1f);
                    pivot = new Vector2(0f, 0.5f);
                    break;
                case AnchorPreset.StretchCenter:
                    min = new Vector2(0.5f, 0f);
                    max = new Vector2(0.5f, 1f);
                    pivot = new Vector2(0.5f, 0.5f);
                    break;
                case AnchorPreset.StretchRight:
                    min = new Vector2(1f, 0f);
                    max = new Vector2(1f, 1f);
                    pivot = new Vector2(1f, 0.5f);
                    break;
                case AnchorPreset.StretchAll:
                    min = Vector2.zero;
                    max = Vector2.one;
                    pivot = new Vector2(0.5f, 0.5f);
                    break;
                default:
                    min = max = pivot = new Vector2(0.5f, 0.5f);
                    break;
            }

            Vector2 size = rt.sizeDelta;
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.pivot = pivot;
            // Stretched axes start flush with the parent; fixed axes keep their size.
            if (!Mathf.Approximately(min.x, max.x))
            {
                size.x = 0f;
            }

            if (!Mathf.Approximately(min.y, max.y))
            {
                size.y = 0f;
            }

            rt.sizeDelta = size;
            rt.anchoredPosition = offset;
            return rt;
        }

        /// <summary>Sets sizeDelta (the size on non-stretched axes; the inset on stretched ones).</summary>
        public static RectTransform SetSize(RectTransform rt, float width, float height)
        {
            rt.sizeDelta = new Vector2(width, height);
            return rt;
        }

        // ------------------------------------------------------------------ motion helpers

        /// <summary>Staggered entrance for every active child of <paramref name="container"/> (fade + springy scale).</summary>
        public static void StaggerIn(Transform container, float delay = 0f, float step = 0.045f, float duration = 0.38f)
        {
            if (container == null)
            {
                return;
            }

            int n = 0;
            for (int i = 0; i < container.childCount; i++)
            {
                Transform child = container.GetChild(i);
                if (!child.gameObject.activeSelf)
                {
                    continue;
                }

                LayoutElement le = child.GetComponent<LayoutElement>();
                if (le != null && le.ignoreLayout)
                {
                    continue;
                }

                CanvasGroup g = child.GetComponent<CanvasGroup>();
                if (g == null)
                {
                    g = child.gameObject.AddComponent<CanvasGroup>();
                }

                float d = delay + n * step;
                UITween.Fade(g, 0f, 1f, duration * 0.7f).SetDelay(d).SetEase(UIEase.OutCubic);
                UITween.Scale(child, 0.9f, 1f, duration).SetDelay(d).SetEase(UIEase.OutBack);
                n++;
            }
        }

        /// <summary>Springy pop-in from zero scale.</summary>
        public static void PopIn(Transform target, float delay = 0f, float duration = 0.42f)
        {
            if (target == null)
            {
                return;
            }

            UITween.Scale(target, 0.001f, 1f, duration).SetDelay(delay).SetEase(UIEase.OutBack);
        }

        /// <summary>Endless alpha breathing on a graphic's CanvasRenderer (no mesh rebuilds). Returns the tween id.</summary>
        public static int Breathe(Graphic graphic, float minAlpha, float maxAlpha, float period)
        {
            if (graphic == null)
            {
                return 0;
            }

            graphic.canvasRenderer.SetAlpha(minAlpha);
            return UITween.Value(graphic, minAlpha, maxAlpha, period * 0.5f, a =>
                {
                    graphic.canvasRenderer.SetAlpha(a);
                })
                .SetEase(UIEase.InOutSine)
                .SetLoops(-1, true)
                .Id;
        }

        internal static void SafeInvoke(Action a)
        {
            if (a == null)
            {
                return;
            }

            try
            {
                a();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnClickSound = null;
            OnHaptic = null;
        }
    }
}
