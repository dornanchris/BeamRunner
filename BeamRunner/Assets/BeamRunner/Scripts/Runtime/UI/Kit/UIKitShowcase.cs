using BeamRunner.Core;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Add to an empty scene and press Play to see every widget, icon, dialog and overlay of the UI kit.
    /// Uses only the public kit API, so it doubles as a reference for building screens.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIKitShowcase : MonoBehaviour
    {
        private UIRoot _root;
        private UIScreen _main;
        private UIScreen _settings;
        private UIScreen _hud;
        private ScrollList _list;
        private CurrencyPill _currency;
        private Badge _badge;
        private ProgressBar _progress;
        private RingProgress _ring;
        private NumberTicker _score;
        private ProgressBar _meter;
        private Text _multiplier;
        private float _hudTimer;
        private long _scoreValue;
        private int _toastIndex;
        private int _zone;
        private int _badgeCount = 3;

        private void Start()
        {
            if (Camera.main == null)
            {
                var camGo = new GameObject("ShowcaseCamera");
                camGo.tag = "MainCamera";
                Camera cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = UITheme.Background;
            }

            _root = UIRoot.Create();
            UIFactory.Backdrop(_root.GetLayer(UILayer.Background), 1f, true);
            _root.Stack.BackAtRoot = () => Dialogs.Confirm("QUIT?", "Leave the showcase?", "Quit", "Stay", ok =>
            {
                if (ok)
                {
                    Toasts.Show("Bye! (Application.Quit in a build)", IconId.Home, UITheme.AccentYellow);
                }
            });

            BuildMain();
            BuildSettings();
            BuildHud();
            _root.Stack.SetRoot(_main);
            UIFactory.StaggerIn(_list.Content, 0.2f, 0.035f);
            _root.Fader.SetInstant(1f);
            _root.Fader.FadeIn(0.6f);
        }

        // ------------------------------------------------------------------ main screen

        private void BuildMain()
        {
            _main = UIScreen.Create<UIScreen>(_root.GetLayer(UILayer.Screens), "ShowcaseMain");
            RectTransform column = UIFactory.ContentColumn(_main.Rect, UITheme.ContentMaxWidth, 32f);

            // Top bar: currency (left) and settings (right).
            RectTransform top = UIFactory.CreateRect(column, "TopBar");
            UIFactory.Anchor(top, AnchorPreset.StretchTop, new Vector2(0f, -24f));
            top.sizeDelta = new Vector2(0f, 140f);
            _currency = UIFactory.CurrencyPill(top, 1250, IconId.Gem, null, () => Toasts.Show("Shop would open here", IconId.Cart, UITheme.GemColor));
            UIFactory.Anchor(_currency.Rect, AnchorPreset.MiddleLeft);
            NeonButton settings = UIFactory.IconButton(top, IconId.Settings, () => _root.Stack.Push(_settings));
            UIFactory.Anchor(settings.Rect, AnchorPreset.MiddleRight);
            _badge = UIFactory.Badge(settings.Rect, _badgeCount);

            _list = UIFactory.ScrollList(column, UITheme.SpaceLg, UIFactory.Padding(36, 36, 24, 160), 48f);
            UIFactory.Stretch(_list.Rect, 0f, 180f, 0f, 0f);
            RectTransform c = _list.Content;

            UIFactory.NeonTitle(c, "BEAMRUNNER", 96);
            UIFactory.Label(c, "UI KIT SHOWCASE", UITheme.TextSmall, UITheme.TextMuted, TextAnchor.MiddleCenter, UIFont.Bold);

            // Buttons
            Section(c, "BUTTONS", IconId.Play);
            UIFactory.Button(c, "Play", ButtonStyle.Primary, () => ShowHud(), IconId.Play);
            UIFactory.Button(c, "Remove Ads", ButtonStyle.Primary, () => Toasts.Show("Purchased!", IconId.Check, UITheme.Success), IconId.Crown, "$2.99");
            UIFactory.Button(c, "Hangar", ButtonStyle.Secondary, () => Toasts.Show("Hangar", IconId.Hangar, UITheme.Accent), IconId.Hangar);
            UIFactory.Button(c, "Reset progress", ButtonStyle.Danger, ConfirmReset, IconId.Warning);
            UIFactory.Button(c, "Privacy policy", ButtonStyle.Ghost, () => Toasts.Show("Opens the privacy URL"));
            NeonButton disabled = UIFactory.Button(c, "Locked", ButtonStyle.Secondary, null, IconId.Lock, "Reach 5,000 m");
            disabled.SetInteractable(false);
            NeonButton attention = UIFactory.Button(c, "Claim reward", ButtonStyle.Primary, () => Toasts.Show("+100 gems", IconId.Gift, UITheme.GemColor), IconId.Gift);
            attention.SetAttention(true);

            HorizontalLayoutGroup icons = UIFactory.HStack(c, UITheme.SpaceMd);
            UIFactory.SetLayout(icons, -1f, UITheme.IconButtonSize + 40f);
            UIFactory.IconButton(icons.transform, IconId.Pause, null);
            UIFactory.IconButton(icons.transform, IconId.Retry, null);
            UIFactory.IconButton(icons.transform, IconId.Home, null, UITheme.IconButtonSize, ButtonStyle.Ghost);
            UIFactory.IconButton(icons.transform, IconId.Play, null, UITheme.IconButtonSize, ButtonStyle.Primary);
            UIFactory.IconButton(icons.transform, IconId.Close, null, UITheme.IconButtonSize, ButtonStyle.Danger);

            // Controls
            Section(c, "CONTROLS", IconId.Gamepad);
            UIFactory.ToggleRow(c, "Music", true, on => Toasts.Show(on ? "Music on" : "Music off", on ? IconId.Music : IconId.SoundOff, UITheme.Accent), IconId.Music);
            UIFactory.ToggleRow(c, "Vibration", false, null, IconId.Vibration);
            UIFactory.SliderRow(c, "Effects", 0.8f, null, IconId.SoundOn);
            UIFactory.SegmentedControl(c, new[] { "DRAG", "TILT", "BUTTONS" }, 0, i => Toasts.Show("Control mode " + i, IconId.Hand, UITheme.Accent));
            _progress = UIFactory.ProgressBar(c, 0.35f, 28f);
            RectTransform ringRow = UIFactory.HStack(c, UITheme.SpaceXl).GetComponent<RectTransform>();
            UIFactory.SetLayout(ringRow, -1f, 240f);
            _ring = UIFactory.RingProgress(ringRow, 220f, 0.72f, 18f);
            _ring.Label.text = "72%";
            RingProgress ring2 = UIFactory.RingProgress(ringRow, 160f, 0.4f, 12f, UITheme.AccentYellow);
            ring2.Label.text = "x3";
            UIFactory.Button(c, "Randomise progress", ButtonStyle.Secondary, RandomiseProgress, IconId.Stats);

            // Cards
            Section(c, "CARDS", IconId.Missions);
            Card missions = UIFactory.Card(c, "Daily missions", IconId.Calendar);
            UIFactory.Badge(missions.Rect, 2);
            UIFactory.StatRow(missions.Body, "Clear 3 levels", "2 / 3", IconId.Speed);
            UIFactory.ProgressBar(missions.Body, 0.66f, 16f);
            UIFactory.StatRow(missions.Body, "Collect 200 gems", "200 / 200", IconId.Gem).Value.color = UITheme.Success;
            UIFactory.StatRow(missions.Body, "Near-miss 25 gates", "11 / 25", IconId.Bolt, false);

            Card results = UIFactory.Card(c, "Run results", IconId.Trophy, UITheme.AccentYellow);
            KeyValueGrid grid = UIFactory.KeyValueGrid(results.Body, 2);
            grid.Add("Score", "48,210");
            grid.Add("Distance", "3,982 m");
            grid.Add("Gems", "+146", UITheme.GemColor);
            grid.Add("Best combo", "x12", UITheme.AccentMagenta);

            // Icons
            Section(c, "ICONS", IconId.Star);
            RectTransform gridRt = UIFactory.CreateRect(c, "IconGrid");
            GridLayoutGroup g = gridRt.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(150f, 178f);
            g.spacing = new Vector2(16f, 16f);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 5;
            g.childAlignment = TextAnchor.UpperCenter;
            var ids = (IconId[])System.Enum.GetValues(typeof(IconId));
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] == IconId.None)
                {
                    continue;
                }

                RectTransform cell = UIFactory.CreateRect(gridRt, "Cell_" + ids[i]);
                Image plate = UIFactory.RoundedImage(cell, UITheme.RadiusMd, UITheme.PanelGlassLight, "Plate");
                UIFactory.Stretch(plate.rectTransform, 0f, 0f, 0f, 40f);
                Image icon = UIFactory.Icon(cell, ids[i], 76f, i % 3 == 0 ? UITheme.AccentCyan : (i % 3 == 1 ? UITheme.TextPrimary : UITheme.AccentMagenta));
                UIFactory.Anchor(icon.rectTransform, AnchorPreset.TopCenter, new Vector2(0f, -30f));
                Text name = UIFactory.LabelLine(cell, ids[i].ToString(), 26, UITheme.TextMuted, TextAnchor.MiddleCenter, UIFont.Bold);
                UIFactory.Anchor(name.rectTransform, AnchorPreset.StretchBottom);
                name.rectTransform.sizeDelta = new Vector2(0f, 36f);
            }

            // Overlays
            Section(c, "OVERLAYS", IconId.Info);
            UIFactory.Button(c, "Toast", ButtonStyle.Secondary, NextToast, IconId.Info);
            UIFactory.Button(c, "Alert", ButtonStyle.Secondary, () => Dialogs.Alert("NEW RECORD", "You flew 3,982 m — your best run yet!", "Nice", null, IconId.Trophy));
            UIFactory.Button(c, "Choice", ButtonStyle.Secondary, () => Dialogs.Choice("CONTINUE?", "Watch a short video to revive, or spend gems.",
                new[] { "Watch video", "Use 200 gems", "No thanks" }, i => Toasts.Show("Chose option " + i), 2, IconId.Heart));
            UIFactory.Button(c, "Fade through", ButtonStyle.Secondary, () => _root.Fader.FadeThrough(() => Toasts.Show("Swapped while black", IconId.Check, UITheme.Success)), IconId.Restore);
            UIFactory.Button(c, "Floating text", ButtonStyle.Secondary, SpawnFloating, IconId.Plus);
            UIFactory.Button(c, "Next level accent", ButtonStyle.Secondary, NextZone, IconId.Speed);
            UIFactory.ToggleRow(c, "FPS counter", false, on => _root.ShowFps(on), IconId.Stats);
            UIFactory.ToggleRow(c, "Reduced motion", false, on => UITheme.ReducedMotion = on, IconId.Info);
        }

        private static void Section(Transform parent, string text, IconId icon)
        {
            UIFactory.Spacer(parent, 12f);
            RectTransform row = UIFactory.CreateRect(parent, "Section_" + text);
            HorizontalLayoutGroup h = UIFactory.HStack(row, UITheme.SpaceSm, null, TextAnchor.MiddleLeft, true);
            h.childForceExpandWidth = false;
            UIFactory.SetLayout(row, -1f, 64f);
            Image ic = UIFactory.Icon(row, icon, 44f, UITheme.Accent);
            UIAccentFollower.Attach(ic, AccentSlot.Primary);
            UIFactory.LabelLine(row, text, UITheme.TextLead, UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Title, TextGlow.Glow);
            UIFactory.Divider(parent, UITheme.Alpha(UITheme.Accent, 0.35f));
        }

        // ------------------------------------------------------------------ settings screen (stack demo)

        private void BuildSettings()
        {
            _settings = UIScreen.Create<UIScreen>(_root.GetLayer(UILayer.Screens), "ShowcaseSettings");
            _settings.Transition = ScreenTransition.SlideLeft;
            RectTransform column = UIFactory.ContentColumn(_settings.Rect, 960f, 40f);

            NeonButton back = UIFactory.IconButton(column, IconId.Back, () => _root.Stack.Pop());
            UIFactory.Anchor(back.Rect, AnchorPreset.TopLeft, new Vector2(0f, -24f));
            NeonTitle title = UIFactory.NeonTitle(column, "SETTINGS", UITheme.TextH1);
            UIFactory.Anchor(title.Rect, AnchorPreset.StretchTop, new Vector2(0f, -170f));
            title.Rect.sizeDelta = new Vector2(0f, 130f);

            GlassPanel panel = UIFactory.GlassPanel(column);
            UIFactory.Stretch(panel.Rect, 0f, 340f, 0f, 200f);
            VerticalLayoutGroup v = UIFactory.VStack(panel.Content, UITheme.SpaceSm, null, TextAnchor.UpperCenter, true);
            v.childForceExpandWidth = true;
            UIFactory.SliderRow(panel.Content, "Music", 0.7f, null, IconId.Music);
            UIFactory.SliderRow(panel.Content, "Effects", 0.9f, null, IconId.SoundOn);
            UIFactory.Divider(panel.Content);
            UIFactory.ToggleRow(panel.Content, "Vibration", true, null, IconId.Vibration);
            UIFactory.ToggleRow(panel.Content, "Reduce flashing", false, null, IconId.Bolt);
            UIFactory.Divider(panel.Content);
            UIFactory.Label(panel.Content, "Steering", UITheme.TextBody, UITheme.TextSecondary, TextAnchor.MiddleLeft, UIFont.Bold);
            UIFactory.SegmentedControl(panel.Content, new[] { "DRAG", "TILT" }, 0, null);
            UIFactory.Spacer(panel.Content, 8f);
            UIFactory.Button(panel.Content, "Restore purchases", ButtonStyle.Ghost, () => Toasts.Show("Nothing to restore", IconId.Restore, UITheme.Accent), IconId.Restore);

            NeonButton done = UIFactory.Button(column, "Done", ButtonStyle.Primary, () => _root.Stack.Pop(), IconId.Check);
            UIFactory.Anchor(done.Rect, AnchorPreset.BottomCenter, new Vector2(0f, 40f));
            _settings.DefaultSelection = done.gameObject;
        }

        // ------------------------------------------------------------------ HUD demo

        private void BuildHud()
        {
            _hud = UIScreen.Create<UIScreen>(_root.GetLayer(UILayer.HUD), "ShowcaseHud");
            _hud.Transition = ScreenTransition.Fade;
            RectTransform r = _hud.Rect;

            Text score = UIFactory.LabelLine(r, "0", 96, UITheme.TextPrimary, TextAnchor.UpperLeft, UIFont.Title, TextGlow.Neon);
            UIFactory.Anchor(score.rectTransform, AnchorPreset.TopLeft, new Vector2(40f, -30f));
            score.rectTransform.sizeDelta = new Vector2(700f, 120f);
            _score = NumberTicker.Attach(score, 0);

            _multiplier = UIFactory.LabelLine(r, "x1", 56, UITheme.AccentMagenta, TextAnchor.UpperLeft, UIFont.Title, TextGlow.Glow);
            UIFactory.Anchor(_multiplier.rectTransform, AnchorPreset.TopLeft, new Vector2(44f, -150f));
            _multiplier.rectTransform.sizeDelta = new Vector2(300f, 80f);

            NeonButton pause = UIFactory.IconButton(r, IconId.Pause, () =>
            {
                _hud.Hide();
                _root.Stack.SetRoot(_main);
            });
            UIFactory.Anchor(pause.Rect, AnchorPreset.TopRight, new Vector2(-32f, -32f));

            _meter = UIFactory.ProgressBar(r, 0f, 26f);
            UIFactory.Anchor(_meter.Rect, AnchorPreset.StretchBottom, new Vector2(0f, 80f));
            _meter.Rect.offsetMin = new Vector2(120f, _meter.Rect.offsetMin.y);
            _meter.Rect.offsetMax = new Vector2(-120f, _meter.Rect.offsetMax.y);
            _meter.Rect.sizeDelta = new Vector2(-240f, 26f);
            Text meterLabel = UIFactory.LabelLine(r, "OVERDRIVE", 30, UITheme.TextSecondary, TextAnchor.MiddleCenter, UIFont.Bold);
            UIFactory.Anchor(meterLabel.rectTransform, AnchorPreset.BottomCenter, new Vector2(0f, 118f));
        }

        private void ShowHud()
        {
            _root.Stack.Clear();
            _hud.Show();
            _scoreValue = 0;
            _score.SetValue(0, false);
            _meter.SetValue(0f, false);
            _hudTimer = 0f;
        }

        private void Update()
        {
            if (_hud == null || !_hud.IsVisible)
            {
                return;
            }

            _hudTimer += Time.unscaledDeltaTime;
            if (_hudTimer < 0.7f)
            {
                return;
            }

            _hudTimer = 0f;
            int gain = Random.Range(50, 400);
            _scoreValue += gain;
            _score.SetValue(_scoreValue);
            float m = _meter.Value + 0.12f;
            _meter.SetValue(m > 1f ? 0f : m);
            int mult = 1 + (int)(_scoreValue / 1500) % 8;
            _multiplier.text = "x" + mult;
            UITween.Punch(_multiplier.rectTransform, 0.2f, 0.3f);
            Vector2 pos = new Vector2(Random.Range(-250f, 250f), Random.Range(-200f, 200f));
            FloatingText.Spawn(_root.GetLayer(UILayer.Popups), "+" + gain, gain > 300 ? UITheme.AccentYellow : UITheme.AccentCyan, pos, gain > 300 ? 1.3f : 1f);
        }

        // ------------------------------------------------------------------ actions

        private void ConfirmReset()
        {
            Dialogs.Confirm("RESET PROGRESS?", "This erases your ships, gems and records. It cannot be undone.", "Reset", "Cancel", ok =>
            {
                if (ok)
                {
                    Toasts.Show("Progress reset", IconId.Warning, UITheme.Danger);
                }
            }, true, IconId.Warning);
        }

        private void RandomiseProgress()
        {
            _progress.SetValue(Random.value);
            float v = Random.value;
            _ring.SetValue(v);
            _ring.Label.text = Mathf.RoundToInt(v * 100f) + "%";
            _currency.SetAmount(_currency.Amount + Random.Range(10, 500));
            _badgeCount = (_badgeCount + 1) % 5;
            _badge.SetCount(_badgeCount);
        }

        private void NextToast()
        {
            IconId[] icons = { IconId.Trophy, IconId.Gem, IconId.Shield, IconId.Magnet, IconId.Bolt };
            Color[] colors = { UITheme.AccentYellow, UITheme.GemColor, UITheme.AccentCyan, UITheme.AccentMagenta, UITheme.Success };
            string[] texts = { "Achievement unlocked: Speed Demon", "+50 gems", "Shield ready", "Magnet upgraded to level 3", "Overdrive charged!" };
            int i = _toastIndex++ % icons.Length;
            Toasts.Show(texts[i], icons[i], colors[i]);
        }

        private void SpawnFloating()
        {
            RectTransform layer = _root.GetLayer(UILayer.Popups);
            string[] words = { "NEAR MISS!", "+250", "LEVEL 4", "x4 COMBO" };
            for (int i = 0; i < words.Length; i++)
            {
                Vector2 pos = new Vector2(Random.Range(-300f, 300f), Random.Range(-300f, 300f));
                FloatingText.Spawn(layer, words[i], i % 2 == 0 ? UITheme.AccentMagenta : UITheme.AccentCyan, pos, 1f + i * 0.15f);
            }
        }

        private void NextZone()
        {
            _zone++;
            ZoneTheme z = ZoneThemes.Get(_zone);
            UITheme.SetAccent(z.Primary, z.Secondary);
            Toasts.Show("Palette " + (_zone % ZoneThemes.Count + 1), IconId.Speed, UITheme.ToColor(z.Primary)); // l10n-ok: debug showcase
        }
    }
}
