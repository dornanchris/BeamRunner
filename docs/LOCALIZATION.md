# Localization

BeamRunner ships in twelve languages. English is the source; every other table is a **machine translation
(AI, 2026-09; level, field and control strings 2026-10) that no native speaker has reviewed yet**: see *Before
release* below.

| Code | Language | Script / font set | Plural forms (CLDR) | Numbers |
|---|---|---|---|---|
| `en` | English (source) | Latin: Audiowide / Rajdhani | one, other | 12,345 · 1.5 |
| `es` | Spanish (neutral, Latin-America friendly) | Latin | one, many, other | 1234 · 12.345 · 1,5 |
| `pt-BR` | Portuguese (Brazil) | Latin | one, many, other | 12.345 · 1,5 |
| `fr` | French | Latin | one, many, other | 12 345 (NBSP) · 1,5 |
| `de` | German | Latin | one, other | 12.345 · 1,5 |
| `it` | Italian | Latin | one, many, other | 12.345 · 1,5 |
| `ru` | Russian | Cyrillic: Neo Title Cyrillic / Tektur | one, few, many, other | 12 345 (NBSP) · 1,5 |
| `tr` | Turkish | Latin | other (+ one) | 12.345 · 1,5 |
| `pl` | Polish | Latin | one, few, many, other | 1234 · 12 345 (NBSP) · 1,5 |
| `ja` | Japanese | Noto Sans JP | other | 12,345 · 1.5 |
| `ko` | Korean | Noto Sans KR | other | 12,345 · 1.5 |
| `zh-Hans` | Chinese (Simplified) | Noto Sans SC | other | 12,345 · 1.5 |
| `qps-ploc` | pseudo-localization (Editor / development builds only) | Latin | as English | as English |

## How it works

* **Tables**: `Assets/BeamRunner/Resources/BeamRunner/Localization/<code>.json` — one flat object per language,
  key → text, or key → `{ "one": …, "few": …, "many": …, "other": … }` for counted texts. `_meta` holds the language
  code, English and native names, `machineTranslated`, `translator` and a note. Strict JSON (no comments, no trailing
  commas, no duplicate keys).
* **Core** (`Scripts/Core/Localization`, pure C#, no Unity): `LocJson` (strict reader), `StringTable`, `PluralRules`
  (CLDR cardinal rules per language, integer counts), `LocFormat` (named arguments, per-language digit grouping),
  `Localizer` (current + English table, fallbacks), `Loc` (static facade), `Languages` (the language list, number
  separators, scripts), `PseudoLocalizer`.
* **Runtime**: `LocalizationService` loads English plus the current language at boot (before the UI is built) and on
  a switch. Language = the player's choice (Settings ▸ LANGUAGE, stored per device in `DevicePrefs`,
  `BeamRunner.Language`; empty = automatic) or else `Application.systemLanguage` (Chinese of any variant → `zh-Hans`,
  Portuguese → `pt-BR`, unsupported → English).
* **Switching** without a restart: `SetChoice` raises `Loc.LanguageChanged`; `GameUI` rebuilds the whole UI behind a
  0.16 s fade once the menu or hangar is idle, re-opening Settings. A rebuild (instead of re-applying each label)
  re-resolves every text — most are composed from state (numbers, plurals, conditions) — and the fonts, which change
  with the script, through the same code as boot, so nothing can be missed (pooled dialogs and toasts included).
  The Language row is hidden during runs.
* **Fallbacks**: missing key in a language → English → the key itself. Each miss is logged once per language in the
  Editor/development builds. A table that fails to parse logs an error and the game shows English.
* **Numbers**: `Loc.N(value)` (integers; doubles are floored) and `Loc.N(value, decimals)` use the language's
  separators from `Languages.cs` (CLDR: Spanish and Polish group only from five digits). Never `ToString("N0")`,
  never the device culture. Saves, JSON and prefs stay culture-invariant (`JsonUtility`, `SaveEnvelope`,
  `InvariantCulture` dates) — covered by culture-independence tests (de-DE, tr-TR, fr-FR, ar-SA, hi-IN).

## Writing code

```csharp
using BeamRunner.Core.Localization;

label.text = Loc.T("pause.title");                                   // "PAUSED"
label.text = Loc.T("hud.ghost_ahead", ("n", meters));                // "{n} M AHEAD" → "1,234 M AHEAD"
label.text = Loc.T("mission.gems_run", ("n", target));             // plural: picks one/few/many/other from "n"
label.text = Loc.T("menu.rank_missions", ("done", 2), ("total", 3)); // several arguments
string s = Loc.N(12345);                                             // "12,345" / "12.345" / "12 345"
```

* The count that selects a plural form is **always** the argument named `n` (an integer). Plural keys need `{n}` in
  English; languages whose "one" also covers other numbers (fr 0–1, pt-BR 0–1, ru 21/31…) show `{n}` in every form.
* The tables hold the final case (titles and buttons upper-case in the table). **No `ToUpper`/`ToLower` on text at
  runtime** (Turkish i/İ, German ß…); the kit's button/grid captions show text as given.
* Don't concatenate sentences from fragments; add a key with arguments. Word order is the translator's.
* Brand and debug-only literals: end the line with `// l10n-ok` (the brand "BEAMRUNNER" is never translated;
  its logo keeps Audiowide in every language).
* Rich-text tags (`<color=…>`) are allowed in a text; translations must keep the same tags.

## Checks

`python3 tools/check-strings.py` (CI job *Localization strings + font glyphs*; needs `pip install fonttools` for the
glyph part, `--no-glyphs` skips it) fails on:

* invalid JSON, missing/extra keys in any language, `_meta` problems, native names that differ from `Languages.cs`;
* placeholders that differ from English, plural categories a language does not use or lacks, `{n}` missing where
  required, rich-text tags that differ;
* keys used in code but not in `en.json`, keys never referenced in `Scripts/` (unused), `Loc.T` argument names that
  do not match the English placeholders;
* user-facing literals: prose passed to UI factories / text setters / dialogs / toasts, or any prose literal in
  `Scripts/Runtime/UI`, `Core/Meta`, `Core/Util` without `Loc`; `ToUpper`/`ToLower` or culture number formats on text;
* glyphs: every character of every language (with numbers formatted in that language, and the pseudo-localization)
  must exist in that script's bundled fonts.

EditMode tests (`Tests/EditMode/Core/LocalizationTests.cs`): plural rules per language at edge numbers, formatting,
fallbacks, JSON parsing, every shipped table (same keys, placeholders and plural categories as English, no raw keys in
any catalog text, in every language). PlayMode: the smoke tests force English (`TestLaunchOptions.Language`) and
`Settings_LanguageSwitch_RebuildsTheUiWithoutRestart` switches to French and back at runtime.

## Editing or adding strings

1. Add/edit the key in `en.json` (the source), then in **every** other table (the check fails otherwise; copy the
   English text if no translation is available yet — it is then shown in English).
2. Russian, Japanese, Korean or Chinese changed → `python3 tools/build-fonts.py` (downloads the pinned sources once,
   ~38 MB, into `~/.cache/beamrunner-fonts`; `--source-dir` takes a google/fonts checkout) → commit the fonts.
3. `python3 tools/check-strings.py` and the compile check / tests.
4. Short labels (buttons, segments, HUD, chips): check the length against English (see *Layout*).

**Adding a language**: an entry in `Languages.cs` (code, names, script, separators, grouping), its CLDR rule in
`PluralRules.cs` (+ tests), the table, `tools/check-strings.py` (`LANGUAGES`, `SCRIPT_OF`, `NUMBER_FORMAT`,
`PLURAL_REQUIRED`/`ALLOWED`), and for a new script a font set in `UIFonts`, `tools/build-fonts.py` and
`FieldEditorConstants.RequiredFonts`. iOS picks the language up automatically (`CFBundleLocalizations`).

## Fonts

Legacy uGUI `Text` has no fallback chain we control, so each script has a complete set, loaded lazily (only the
current script's fonts are in memory). Non-Latin fonts are **subsets** made by `tools/build-fonts.py` from pinned
google/fonts sources (commit `23e54b51`, SHA-256 checked): all characters of that script's tables, printable ASCII,
NBSP, common punctuation, currency signs and — Cyrillic — the whole Russian alphabet (keyboard key names). Variable
sources are instanced to one static weight; GSUB/GPOS (unused by legacy Text) are dropped. Licences:
`Fonts/OFL.txt` (all SIL OFL 1.1).

| Script | Title (display, buttons) | Bold | Body | Size |
|---|---|---|---|---|
| Latin | Audiowide | Rajdhani Bold | Rajdhani SemiBold | unchanged |
| Cyrillic | Neo Title Cyrillic (Russo One subset) | Tektur Condensed SemiBold (wdth 85, wght 600) | Tektur Condensed Medium (wdth 85, wght 450) | 15 + 11 + 11 KB |
| Japanese | Noto Sans JP Bold | Noto Sans JP Bold | Noto Sans JP Medium | 2 × 115 KB |
| Korean | Noto Sans KR Bold | Noto Sans KR Bold | Noto Sans KR Medium | 2 × 72 KB |
| Simplified Chinese | Noto Sans SC Bold | Noto Sans SC Bold | Noto Sans SC Medium | 2 × 158 KB |

Why: Audiowide and Rajdhani have no Cyrillic or CJK. Russo One is the closest wide, geometric, techno display face
with Cyrillic (its Reserved Font Name "Russo" forces the rename of the modified subset). Tektur's octagonal
construction matches Rajdhani's squared techno look, and its width axis at 85 keeps Russian (≈ 15–25 % longer than
English) as narrow as Rajdhani. Noto Sans CJK is the only complete, open, well-hinted family for all three CJK
scripts with matching weights; Medium/Bold keep the weight contrast of the Latin set. Full CJK fonts are 10–18 MB
each — the nine subsets total ≈ 730 KB.

Text that is not in the tables — player names from Game Center / Play Games, store prices in an unexpected currency —
can contain characters a subset lacks; Unity's dynamic fonts then fall back to the OS font for those characters.

## Layout

* Single-line labels (`UIFactory.LabelLine`, every button label, segment, chip and HUD label) have `UITextFit`: a
  text wider than its rect at the design size shrinks down to 62 % of it; text that fits keeps its exact size, so
  English looks as before. `NeonTitle` shrinks to 50 %. Free-standing buttons grow with their label up to their
  parent's width. Dialogs with two long button labels stack them. Toasts shrink a text that needs more than two
  lines (CJK lines are ~13 % taller). HUD popups (`FloatingText`) stay on screen (shrunk and pulled in from the edge).
* Offline width check (hmtx advances + `kern` pairs at the real type sizes, estimated container widths at
  1080 × 1920), every key × language: the riskiest remaining spots are Spanish/Portuguese/French screen titles
  (66–72 %), pack names in it/pt-BR (~72 %), NO ADS tag (es/pt-BR ~65 %). Level names in the level banner (`NeonTitle`,
  78 pt, 1000 units) fit at ≥ 90 % (longest: de GLEITENDES NADELÖHR); "LEVEL {n} · {name}" under PLAY stays below
  ~430 of 600 units. Shortened for fit: de/pt-BR "no ads" tag, it daily-reward title, the d-pad and tilt tutorial
  details in de/es/fr/it/pt-BR/pl (the tutorial hint panel does not shrink; its lines must stay under ~880 units).
* Keep new HUD/segment/button texts short; test with the pseudo-localization (35 % longer, accented) first.

## Pseudo-localization

Settings ▸ LANGUAGE ▸ `[PŠÉÚÐÖ]` (Editor and development builds): every English text becomes
`[Ŝéţţîñĝš ~~~]` — accented look-alikes plus ~35 % padding in brackets. Plain English on screen was never localized;
a missing `]` means the text is clipped.

## Glossary

Brand: **BeamRunner** is never translated. Ship names (ARROW, OG SHUTTLE, MANTA, VIPER, HALO, PHANTOM, NOVA) stay
Latin except ja/ko (katakana/hangul transliterations). Platform names follow the platform's own localization (Google
Play Games → GOOGLE PLAY JEUX, GRY GOOGLE PLAY, Google Play ゲーム …); GAME CENTER stays as is.

| English | es | pt-BR | fr | de | it | ru | tr | pl | ja | ko | zh-Hans |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Overdrive | OVERDRIVE | OVERDRIVE | OVERDRIVE | OVERDRIVE | OVERDRIVE | ОВЕРДРАЙВ | OVERDRIVE | OVERDRIVE | オーバードライブ | 오버드라이브 | 超速 |
| Daily Run | CARRERA DIARIA | CORRIDA DIÁRIA | COURSE DU JOUR | TAGESRENNEN | CORSA DEL GIORNO | ЗАБЕГ ДНЯ | GÜNLÜK KOŞU | LOT DNIA | デイリーラン | 데일리 런 | 每日挑战 |
| gems | fragmentos | fragmentos | éclats | Splitter | frammenti | осколки | kristal | odłamki | シャード | 샤드 | 碎片 |
| near miss | roce | raspão | frôlement (HUD: FRÔLÉ) | haarscharfes Manöver (HUD: HAARSCHARF) | sfiorata (HUD: PER UN PELO) | впритирку | kıl payı | mijanka o włos (HUD: O WŁOS) | ニアミス | 니어미스 | 极限闪避 |
| gate (wall of blocks with open squares) | barrera | barreira | barrière | Tor | barriera | барьер | kapı | bramka | ゲート | 게이트 | 闸门 |
| level (LEVEL {n}) | nivel | nível | niveau | Level | livello | уровень | seviye | poziom | レベル | 레벨 | 关卡 (第 {n} 关) |
| field (the play area; the Daily Run's field) | campo | campo | champ | Feld | campo | поле | alan | pole | フィールド | 필드 | 场地 |
| cube | cubo | cubo | cube | Würfel | cubo | куб | küp | sześcian | キューブ | 큐브 | 方块 |
| dodge (a cube) | esquivar | desviar de | esquiver | ausweichen | schivare | облететь | atlatmak | ominąć | かわす | 회피 | 躲避 |
| obstacle (cube or gate) | obstáculo | obstáculo | obstacle | Hindernis | ostacolo | препятствие | engel | przeszkoda | 障害物 | 장애물 | 障碍物 |
| stick (touch joystick) | stick | analógico | stick | Stick | levetta | стик | joystick | gałka | スティック | 스틱 | 摇杆 |
| run (one game) | carrera | corrida | course | Lauf | corsa | забег | koşu | lot | プレイ | 판 | 局 |
| ghost | fantasma | fantasma | fantôme | Geist | fantasma | призрак | hayalet | duch | ゴースト | 고스트 | 幽灵 |
| streak | racha | sequência | série | Serie | serie | серия | seri | seria | 連続日数 | 연속 | 连续天数 |
| pilot rank | rango de piloto | patente de piloto | rang de pilote | Pilotenrang | grado pilota | ранг пилота | pilot rütbesi | ranga pilota | パイロットランク | 파일럿 등급 | 飞行员等级 |
| hangar | hangar | hangar | hangar | Hangar | hangar | ангар | hangar | hangar | 格納庫 | 격납고 | 机库 |
| missions | misiones | missões | missions | Missionen | missioni | задания | görevler | misje | ミッション | 미션 | 任务 |
| Settings | Ajustes | Configurações | Paramètres | Einstellungen | Impostazioni | Настройки | Ayarlar | Ustawienia | 設定 | 설정 | 设置 |
| multiplier | multiplicador | multiplicador | multiplicateur | Multiplikator | moltiplicatore | множитель | çarpan | mnożnik | 倍率 | 배율 | 倍率 |
| shield / magnet | escudo / imán | escudo / ímã | bouclier / aimant | Schild / Magnet | scudo / magnete | щит / магнит | kalkan / mıknatıs | tarcza / magnes | シールド / マグネット | 실드 / 자석 | 护盾 / 磁铁 |

Tone: short, punchy arcade copy; informal address everywhere (es *tú* without *vosotros*, pt-BR *você*, fr *tu*, de
*du*, it *tu*, ru *ты*, tr *sen*, pl *ty*, ko 해요체, zh 你); Japanese uses plain noun phrases for labels and です/ます
in sentences. Units: m / km / s as in the tables (Russian м / с; Chinese prose 米 / 秒 / 分钟; Japanese m with 秒 / 分).

## Before release

* A native speaker reviews each language in context (screenshots or a development build with the language picked),
  highest priority: consent and privacy (`consent.*`, `settings.personalised_hint`, `settings.crash_hint`,
  `ios.tracking_usage`), store (`store.*`), reminders (`reminder.*`, `notifications.*`), then all UI. Then set
  `_meta.machineTranslated` to false and name the reviewer in `_meta.translator`.
* After review edits: `tools/build-fonts.py`, `tools/check-strings.py`, and a device pass per script (Latin, Cyrillic,
  Japanese, Korean, Chinese): every screen, a tutorial run, revive, results, store, a reminder notification, the iOS
  ATT prompt text.
* Store listings are English only (`docs/STORE_LISTING.md`); translate them with the same glossary when the game is
  released in those storefronts.
