using System.Collections.Generic;
using BeamRunner.Core.Localization;
using BeamRunner.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Settings, language part: a LANGUAGE card (first in the list) whose button shows the current language and opens a
    /// picker — AUTOMATIC (follow the device language) plus every shipped language, each written in its own script and
    /// font, and the pseudo-localization in Editor/development builds. The choice is stored per device
    /// (<see cref="LocalizationService.SetChoice"/>); the UI then rebuilds itself in the new language (GameUI.Language).
    /// Hidden during a run (Settings is reachable from Pause): the rebuild only happens in the menu.
    /// </summary>
    public sealed partial class SettingsScreen
    {
        private const float LanguageButtonHeight = 104f;

        private Card _languageCard;
        private NeonButton _languageButton;
        private Text _languageHint;

        private void BuildLanguage(RectTransform c)
        {
            _languageCard = UIFactory.Card(c, Loc.T("settings.language"), IconId.Globe);
            RectTransform row = UIFactory.SettingRow(_languageCard.Body, Loc.T("settings.language_row"), IconId.Globe);
            _languageButton = UIFactory.Button(row, Loc.Language.DisplayName, ButtonStyle.Secondary, OpenLanguagePicker,
                IconId.ChevronRight, null, 96f, 240f);
            _languageHint = Hint(_languageCard.Body, Loc.T("settings.language_auto_hint"));
        }

        private void RefreshLanguage()
        {
            if (_languageCard == null)
            {
                return;
            }

            _languageCard.gameObject.SetActive(!InRun());
            _languageButton.SetLabel(Loc.Language.DisplayName);
            _languageHint.gameObject.SetActive(LocalizationService.Choice == null);
        }

        private void OpenLanguagePicker()
        {
            if (Dialogs.IsOpen || InRun())
            {
                return;
            }

            Dialogs.Custom(Loc.T("settings.language_title"), null, BuildLanguagePicker, new[] { Loc.T("common.cancel") },
                null, 0, IconId.Globe, null, new[] { ButtonStyle.Ghost });
        }

        private void BuildLanguagePicker(RectTransform body)
        {
            LanguageInfo chosen = LocalizationService.Choice;
            AddLanguageButton(body, null, Loc.T("settings.language_auto"), LocScript.Latin, chosen == null, true);

            IReadOnlyList<LanguageInfo> languages = LocalizationService.Selectable;
            RectTransform pair = null;
            for (int i = 0; i < languages.Count; i++)
            {
                if (i % 2 == 0)
                {
                    pair = UIFactory.CreateRect(body, "LanguageRow");
                    HorizontalLayoutGroup h = UIFactory.HStack(pair, UITheme.SpaceSm, null, TextAnchor.MiddleCenter, true);
                    h.childForceExpandWidth = true;
                    UIFactory.SetLayout(pair, -1f, LanguageButtonHeight);
                }

                LanguageInfo lang = languages[i];
                AddLanguageButton(pair, lang, lang.DisplayName, lang.Script, chosen != null && ReferenceEquals(chosen, lang), false);
            }
        }

        private void AddLanguageButton(RectTransform parent, LanguageInfo language, string label, LocScript script, bool selected,
            bool automatic)
        {
            ButtonStyle style = selected ? ButtonStyle.Primary : ButtonStyle.Secondary;
            NeonButton b = UIFactory.Button(parent, string.Empty, style, () => PickLanguage(language), IconId.None, null,
                LanguageButtonHeight, 0f);
            // Each language is written in its own script, so it gets that script's font (the name's glyphs are part of
            // that font's subset); AUTOMATIC is in the current language.
            b.Label.font = automatic ? UIFonts.Title : UIFonts.For(script, UIFont.Title);
            b.SetLabel(label);
            LayoutElement le = b.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.flexibleWidth = 1f;
                le.minWidth = 0f;
                le.preferredWidth = 0f;
            }

            if (automatic)
            {
                UIFactory.SetLayout(b, -1f, LanguageButtonHeight);
            }
        }

        private void PickLanguage(LanguageInfo language)
        {
            Dialogs.CloseAll();
            bool unchanged = ReferenceEquals(language, LocalizationService.Choice);
            if (unchanged)
            {
                return;
            }

            LocalizationService.SetChoice(language);
            MarkDirty();
        }
    }
}
