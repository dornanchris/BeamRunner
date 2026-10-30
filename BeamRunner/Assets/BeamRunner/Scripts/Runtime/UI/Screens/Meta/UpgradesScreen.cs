using System;
using System.Collections;
using BeamRunner.Core.Audio;
using BeamRunner.Core.Balance;
using BeamRunner.Core.Localization;
using BeamRunner.Core.Meta;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Upgrade shop overlay (from the menu's UPGRADES tile and the results screen): the gem balance, the daily free
    /// upgrade (rewarded ad, when one is ready and today's token is unused), and one card per craft upgrade (fuel tank, build efficiency, starting fuel, power-up duration, hull) — level,
    /// what the next level changes, and a buy button with its gem price (or MAX / the level that unlocks it).
    /// The rules live in <c>ProfileService.Upgrades.cs</c>; this screen only shows them and calls
    /// <see cref="Game.GameController.BuyUpgrade"/> / <see cref="Game.GameController.FreeUpgradeWithAd"/>.
    /// </summary>
    public sealed class UpgradesScreen : MetaScreen
    {
        private sealed class UpgradeCard
        {
            public UpgradeId Id;
            public Card Card;
            public Image Ring;
            public Image Glow;
            public Text Name;
            public Text Level;
            public Text Effect;
            public ProgressBar Bar;
            public NeonButton Buy;
            public NeonButton Free;
            public int ShownLevel = -1;
        }

        private static readonly IconId[] Icons = { IconId.Fuel, IconId.Speed, IconId.Play, IconId.Bolt, IconId.Heart };

        // Literals so tools/check-strings.py sees every key used (indexed by UpgradeId).
        private static readonly string[] NameKeys =
        {
            "upgrade.fuel_tank",
            "upgrade.efficiency",
            "upgrade.start_fuel",
            "upgrade.duration",
            "upgrade.hull",
        };

        private readonly UpgradeCard[] _cards = new UpgradeCard[ShipStats.UpgradeCount];
        private ScrollList _list;
        private CurrencyPill _pill;
        private Text _freeHint;
        private Text _dps;
        private bool _adBusy;

        protected override IEnumerator BuildSteps()
        {
            Transition = ScreenTransition.SlideUp;
            RectTransform r = Rect;
            MetaUI.Dimmer(r, 0.72f);
            RectTransform column = UIFactory.ContentColumn(r, 1000f, 32f);
            NeonTitle title;
            RectTransform header = MetaUI.Header(column, Loc.T("upgrades.title"), MetaUI.CloseTop, out title);
            _pill = UIFactory.CurrencyPill(header, 0, IconId.Gem, null, null);
            UIFactory.Anchor(_pill.GetComponent<RectTransform>(), AnchorPreset.MiddleRight, new Vector2(-12f, 0f));

            _list = UIFactory.ScrollList(column, UITheme.SpaceMd, UIFactory.Padding(32, 32, 24, 80), 40f);
            UIFactory.Stretch(_list.Rect, -32f, MetaUI.HeaderBottom, -32f, 0f);
            RectTransform c = _list.Content;

            _dps = UIFactory.LabelLine(c, string.Empty, 40, UITheme.TextSecondary, TextAnchor.MiddleCenter, UIFont.Bold, TextGlow.Shadow);
            UIFactory.SetLayout(_dps, -1f, 56f);
            _freeHint = UIFactory.Label(c, Loc.T("upgrades.free_hint"), UITheme.TextSmall, UITheme.Success, TextAnchor.MiddleCenter, UIFont.Body);
            UIFactory.SetLayout(_freeHint, -1f, -1f);
            yield return null;

            MetaUI.Section(c, Loc.T("upgrades.ship"), IconId.Hangar);
            for (int i = 0; i < _cards.Length; i++)
            {
                _cards[i] = BuildCard(c, (UpgradeId)i);
            }

            Text tip = UIFactory.Label(c, Loc.T("upgrades.tip"), UITheme.TextSmall, UITheme.TextMuted, TextAnchor.MiddleCenter, UIFont.Body);
            UIFactory.SetLayout(tip, -1f, -1f);
        }

        private UpgradeCard BuildCard(RectTransform parent, UpgradeId id)
        {
            var u = new UpgradeCard { Id = id };
            u.Card = UIFactory.Card(parent, null, IconId.None, null, 32f);
            RectTransform row = UIFactory.CreateRect(u.Card.Body, "Row");
            HorizontalLayoutGroup h = UIFactory.HStack(row, UITheme.SpaceMd, null, TextAnchor.MiddleLeft, true);
            h.childForceExpandWidth = false;

            RectTransform emblem = MetaUI.Emblem(row, 112f, UITheme.Accent, out u.Ring, out u.Glow);
            UIFactory.Icon(emblem, Icons[(int)id], 56f, UITheme.TextPrimary);

            RectTransform texts = UIFactory.CreateRect(row, "Texts");
            VerticalLayoutGroup v = UIFactory.VStack(texts, 6f, null, TextAnchor.MiddleLeft, true);
            v.childForceExpandWidth = true;
            UIFactory.SetLayout(texts, 0f, -1f, 1f);
            u.Name = UIFactory.LabelLine(texts, Loc.T(NameKeys[(int)id]), 44, UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Title, TextGlow.Neon);
            u.Level = UIFactory.LabelLine(texts, string.Empty, 32, UITheme.TextSecondary, TextAnchor.MiddleLeft, UIFont.Bold);
            u.Effect = UIFactory.Label(texts, string.Empty, 32, UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Body);
            u.Bar = UIFactory.ProgressBar(texts, 0f, 14f);
            UIFactory.SetLayout(u.Bar, -1f, 14f);

            RectTransform buttons = UIFactory.CreateRect(row, "Buttons");
            VerticalLayoutGroup bv = UIFactory.VStack(buttons, 10f, null, TextAnchor.MiddleCenter, true);
            bv.childForceExpandWidth = true;
            UIFactory.SetLayout(buttons, 260f, -1f);
            UpgradeId captured = id;
            u.Buy = UIFactory.Button(buttons, string.Empty, ButtonStyle.Primary, () => OnBuy(captured), IconId.Gem, null,
                UITheme.ButtonHeight, 260f);
            u.Free = UIFactory.Button(buttons, Loc.T("upgrades.free"), ButtonStyle.Secondary, () => OnFree(captured), IconId.Ad,
                null, UITheme.ButtonHeightSmall, 260f);
            return u;
        }

        protected override void OnShow()
        {
            base.OnShow();
            _list.ScrollToTop();
            if (!UITheme.ReducedMotion)
            {
                UIFactory.StaggerIn(_list.Content, 0.08f, 0.06f);
            }
        }

        protected override void Refresh(bool showing)
        {
            ProfileService p = Profile;
            _pill.SetAmount(p.Gems, !showing);
            bool free = !_adBusy && Game.FreeUpgradeOffered;
            _freeHint.gameObject.SetActive(free);
            ShipStats ship = p.Ship;
            _dps.text = Loc.T("upgrades.summary", ("fuel", Mathf.RoundToInt(ship.MaxFuel)), ("start", Mathf.RoundToInt(ship.StartFuel)),
                ("hull", ship.MaxHull));
            for (int i = 0; i < _cards.Length; i++)
            {
                if (_cards[i] != null)
                {
                    BindCard(_cards[i], p, free, showing);
                }
            }
        }

        private static void BindCard(UpgradeCard u, ProfileService p, bool freeOffered, bool showing)
        {
            UpgradeId id = u.Id;
            int level = p.UpgradeLevel(id);
            int max = Math.Max(1, p.UpgradeMaxLevel(id));
            bool maxed = p.UpgradeMaxed(id);
            bool unlocked = p.UpgradeUnlocked(id);
            long cost = p.UpgradeCost(id);
            bool affordable = unlocked && !maxed && p.Gems >= cost;

            u.Level.text = maxed ? Loc.T("upgrades.level_max") : Loc.T("upgrades.level", ("level", level), ("max", max));
            u.Effect.text = unlocked ? Effect(id, level, maxed) : Loc.T("upgrades.locked", ("level", p.UpgradeUnlockLevel(id)));
            u.Effect.color = unlocked ? UITheme.TextPrimary : UITheme.TextMuted;
            u.Bar.SetValue(level / (float)max, !showing);

            if (maxed)
            {
                u.Buy.SetLabel(Loc.T("upgrades.max"));
                u.Buy.SetIcon(IconId.Check);
            }
            else if (!unlocked)
            {
                u.Buy.SetLabel(Loc.T("upgrades.level_short", ("level", p.UpgradeUnlockLevel(id))));
                u.Buy.SetIcon(IconId.Lock);
            }
            else
            {
                u.Buy.SetLabel(MetaUI.N(cost));
                u.Buy.SetIcon(IconId.Gem);
            }

            u.Buy.SetInteractable(affordable);
            u.Buy.SetAttention(affordable);
            u.Free.gameObject.SetActive(freeOffered && unlocked && !maxed);

            Color accent = maxed ? UITheme.Success : affordable ? UITheme.Accent : UITheme.TextMuted;
            u.Ring.color = accent;
            u.Glow.color = UITheme.Alpha(accent, affordable ? 0.5f : 0.2f);
            u.Card.Panel.SetAccent(affordable ? UITheme.Accent : (Color?)null);
            if (u.ShownLevel >= 0 && level > u.ShownLevel && !showing)
            {
                UITween.Punch(u.Card.Rect, 0.06f, 0.4f);
            }

            u.ShownLevel = level;
        }

        /// <summary>"what the next level does" line: current → next value.</summary>
        private static string Effect(UpgradeId id, int level, bool maxed)
        {
            BalanceConfig b = BalanceConfig.Current;
            var now = new int[ShipStats.UpgradeCount];
            var next = new int[ShipStats.UpgradeCount];
            now[(int)id] = level;
            next[(int)id] = maxed ? level : level + 1;
            ShipStats a = ShipStats.FromLevels(now, b);
            ShipStats c = ShipStats.FromLevels(next, b);
            switch (id)
            {
                case UpgradeId.FuelTank:
                    return Loc.T("upgrades.effect_tank", ("from", Mathf.RoundToInt(a.MaxFuel)), ("to", Mathf.RoundToInt(c.MaxFuel)));
                case UpgradeId.Efficiency:
                    return Loc.T("upgrades.effect_efficiency", ("from", Pct(1f - a.BridgeCost / b.BridgeCost)),
                        ("to", Pct(1f - c.BridgeCost / b.BridgeCost)));
                case UpgradeId.StartFuel:
                    return Loc.T("upgrades.effect_start", ("from", Mathf.RoundToInt(a.StartFuel)), ("to", Mathf.RoundToInt(c.StartFuel)));
                case UpgradeId.Duration:
                    return Loc.T("upgrades.effect_duration", ("from", Pct(a.DurationScale - 1f)), ("to", Pct(c.DurationScale - 1f)));
                default:
                    return Loc.T("upgrades.effect_hull", ("from", a.MaxHull), ("to", c.MaxHull));
            }
        }

        private static int Pct(float fraction)
        {
            return Mathf.RoundToInt(fraction * 100f);
        }

        private void OnBuy(UpgradeId id)
        {
            if (Game == null)
            {
                return;
            }

            Game.BuyUpgrade(id);
            MarkDirty();
        }

        private void OnFree(UpgradeId id)
        {
            if (Game == null || _adBusy)
            {
                return;
            }

            _adBusy = true;
            MarkDirty();
            Sfx(SfxId.UiClick);
            Game.FreeUpgradeWithAd(id, granted =>
            {
                _adBusy = false;
                MarkDirty();
            });
        }
    }
}
