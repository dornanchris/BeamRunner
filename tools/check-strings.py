#!/usr/bin/env python3
"""Localization check for BeamRunner (runs in CI next to gen-metas; no Unity needed).

Fails (exit 1) on:
  * user-facing string literals in game code that do not go through the localizer (Loc.T) — prose-looking literals
    ("Store unavailable.", "PLAY") in Scripts/Runtime and in the Core catalogs (Scripts/Core/Meta, Scripts/Core/Util),
    outside diagnostics (Debug.Log, FieldLog, ServiceLog, exceptions, PurchaseResult messages), GameObject names and
    allow-listed developer-only files; a line can opt out with a `// l10n-ok: <reason>` comment (brand names, support
    diagnostics);
  * runtime case changes (ToUpper/ToLower…) and culture-dependent number formats ("N0") in that code;
  * Loc.T keys used in code but missing from en.json, keys in en.json never referenced by code (unused), and Loc.T calls
    whose named arguments differ from the English placeholders (plural entries need "n");
  * any language table that is not strict JSON, misses keys or has extra ones, uses a different placeholder set than
    English, has wrong plural categories for its CLDR rules, unbalanced/different rich-text tags, or lacks the
    machine-translation marker in _meta;
  * glyph coverage: a string (every plural form, with numbers formatted the way the game formats them) of any
    language, or a language's own name in the picker, containing a character its font set cannot render. Latin
    languages and the pseudo-localization use Audiowide + Rajdhani; the others use the subset fonts that
    tools/build-fonts.py generates (run it after changing a table).

Usage:  python3 tools/check-strings.py [--verbose] [--no-glyphs]
"""
import argparse
import json
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(REPO, "BeamRunner", "Assets", "BeamRunner")
SCRIPTS = os.path.join(ASSETS, "Scripts")
LOC_DIR = os.path.join(ASSETS, "Resources", "BeamRunner", "Localization")
FONT_DIR = os.path.join(ASSETS, "Resources", "BeamRunner", "Fonts")
LANGUAGES_CS = os.path.join(SCRIPTS, "Core", "Localization", "Languages.cs")

# Must match Core/Localization/Languages.cs (checked below) and UIFonts.cs.
LANGUAGES = ["en", "es", "pt-BR", "fr", "de", "it", "ru", "tr", "pl", "ja", "ko", "zh-Hans"]
SCRIPT_OF = {"ru": "Cyrillic", "ja": "Japanese", "ko": "Korean", "zh-Hans": "SimplifiedChinese"}
FONTS = {
    "Latin": ["Audiowide-Regular.ttf", "Rajdhani-SemiBold.ttf", "Rajdhani-Bold.ttf"],
    "Cyrillic": ["FieldTitleCyrillic-Regular.ttf", "Tektur-Condensed-Medium.ttf", "Tektur-Condensed-SemiBold.ttf"],
    "Japanese": ["NotoSansJP-Medium.ttf", "NotoSansJP-Bold.ttf"],
    "Korean": ["NotoSansKR-Medium.ttf", "NotoSansKR-Bold.ttf"],
    "SimplifiedChinese": ["NotoSansSC-Medium.ttf", "NotoSansSC-Bold.ttf"],
}
# Number formats of LanguageInfo (group separator, decimal separator).
NUMBER_FORMAT = {
    "en": (",", "."), "es": (".", ","), "pt-BR": (".", ","), "fr": ("\u00a0", ","), "de": (".", ","), "it": (".", ","),
    "ru": ("\u00a0", ","), "tr": (".", ","), "pl": ("\u00a0", ","), "ja": (",", "."), "ko": (",", "."),
    "zh-Hans": (",", "."),
}
# Characters code puts next to localized text in every language (separators, signs, digits, RunUi.Dot / Dash, "x"
# multiplier, "#", "?" fallback). Every font set must render them.
CODE_GLYPHS = "0123456789+-x/#%:.,|?!()[]~ ·—\u00a0"

# CLDR plural rules per language (see Core/Localization/PluralRules.cs): required and allowed categories.
PLURAL_REQUIRED = {
    "en": {"one", "other"}, "de": {"one", "other"}, "es": {"one", "other"}, "it": {"one", "other"},
    "fr": {"one", "other"}, "pt-BR": {"one", "other"}, "ru": {"one", "few", "many", "other"},
    "pl": {"one", "few", "many", "other"}, "tr": {"other"}, "ja": {"other"}, "ko": {"other"}, "zh-Hans": {"other"},
}
PLURAL_ALLOWED = {
    "en": {"one", "other"}, "de": {"one", "other"}, "es": {"one", "many", "other"}, "it": {"one", "many", "other"},
    "fr": {"one", "many", "other"}, "pt-BR": {"one", "many", "other"}, "ru": {"one", "few", "many", "other"},
    "pl": {"one", "few", "many", "other"}, "tr": {"one", "other"}, "ja": {"other"}, "ko": {"other"},
    "zh-Hans": {"other"},
}
# Languages whose "one" category also covers numbers other than 1 (fr/pt-BR: 0; ru: 21, 31…): every form of theirs must
# show the count.
ONE_IS_NOT_JUST_ONE = {"fr", "pt-BR", "ru"}
OTHER_ONLY = {"ja", "ko", "zh-Hans", "tr"}

# Code scanned for user-facing literals: in the UI and the Core catalogs every prose-looking literal must go through Loc;
# everywhere in Scripts/Runtime and Scripts/Core, literals passed to text sinks (UI factory, text setters, dialogs, toasts,
# notifications, RaiseNotify) must.
PROSE_DIRS = [os.path.join(SCRIPTS, "Runtime", "UI"), os.path.join(SCRIPTS, "Core", "Meta"), os.path.join(SCRIPTS, "Core", "Util")]
SINK_DIRS = [os.path.join(SCRIPTS, "Runtime"), os.path.join(SCRIPTS, "Core")]
# Code scanned for key usage (every key must be referenced somewhere here).
KEY_DIRS = [os.path.join(SCRIPTS, "Runtime"), os.path.join(SCRIPTS, "Core"), os.path.join(SCRIPTS, "Editor")]
# Keys whose texts the OS renders (iOS InfoPlist.strings, written by Editor/Ios/FieldIosLocalizationPlist.cs): not drawn
# with the bundled fonts, so they are left out of the glyph check (and of tools/build-fonts.py's subsets).
OS_RENDERED_PREFIXES = ("ios.",)
# Developer-only files: their texts never reach players (Editor / development builds only, or debug tooling).
ALLOWED_FILES = {
    "UIKitShowcase.cs": "UI kit demo scene (development only)",
    "SimulatedAdOverlay.cs": "simulated ads: Editor/development builds only",
    "RenderingSandbox.cs": "rendering test bed (development only)",
    "GameProbe.cs": "test probe (Editor/development builds only)",
    "AppResetHooks.cs": "test hooks (Editor/development builds only)",
    "SimulatedOnlineService.cs": "simulated online service: Editor/development builds only",
    "PrivacyRegion.cs": "ISO region / time-zone codes (data, never shown)",
}
# Calls whose string arguments are never shown to players.
DIAGNOSTIC_CALLS = re.compile(
    r"\b(Debug\.Log\w*|Debug\.Assert\w*|FieldLog\.\w+|ServiceLog\.\w+|ServiceLogInfo|ServiceLogWarn|Log\w*|"
    r"PurchaseResult\.\w+|HandleProductsFetchFailure|HandleDisconnected|FinishRestore|RunGuarded|Guard|Safe\w*|"
    r"RunBootStep|nameof|Exception|Fail|Warn|Info|Error|Report\w*|Breadcrumb\w*|AddBreadcrumb|SetTag|SetExtra)\s*\(")
NAME_CALLS = re.compile(
    r"\b(CreateRect|CreateFill|CreateImage|PillImage|RoundedImage|MakeText|new\s+GameObject|BuildChip|Slot|CreateText|"
    r"CreateButton|Find|FindChild|Resources\.Load\w*|GetString|SetString|GetInt|SetInt|GetLong|SetLong|HasKey|DeleteKey|"
    r"PlayerPrefs\.\w+|DevicePrefs\.\w+|Shader\.\w+|PropertyToID|SetFloat|SetColor|SetVector|SetTexture|EnableKeyword|"
    r"DisableKeyword|GetComponent\w*|AddComponent\w*|Invoke|StartsWith|EndsWith|IndexOf|Contains|Equals|Replace|Split|"
    r"TryParseExact|ParseExact|ToString|DllImport|Guid|FromJson|ToJson|Combine|WriteAllText|Append)\s*\(")

LITERAL = re.compile(r'@"(?:[^"]|"")*"|\$@?"(?:[^"\\]|\\.)*"|"(?:[^"\\\n]|\\.)*"')
PROSE_WORDS = re.compile(r"[A-Za-zÀ-ɏЀ-ӿ]{2,}")


def strip_comments(src):
    """Blanks out comments (keeps line structure and string literals)."""
    out = []
    i = 0
    n = len(src)
    while i < n:
        c = src[i]
        if c == '"' or (c in "@$" and i + 1 < n and src[i + 1] in '"@$'):
            m = LITERAL.match(src, i)
            if m:
                out.append(m.group(0))
                i = m.end()
                continue
        if c == "'" and i + 2 < n:
            m = re.match(r"'(?:[^'\\]|\\.)+'", src[i:])
            if m:
                out.append(m.group(0))
                i += m.end()
                continue
        if src.startswith("//", i):
            j = src.find("\n", i)
            j = n if j < 0 else j
            comment = src[i:j]
            # Keep the opt-out marker visible to the scanner.
            out.append("//l10n-ok" if "l10n-ok" in comment else "")
            i = j
            continue
        if src.startswith("/*", i):
            j = src.find("*/", i + 2)
            j = n if j < 0 else j + 2
            out.append(re.sub(r"[^\n]", " ", src[i:j]))
            i = j
            continue
        out.append(c)
        i += 1
    return "".join(out)


def literal_value(tok):
    if tok.startswith("@"):
        return tok[2:-1].replace('""', '"')
    if tok.startswith("$"):
        return tok[tok.index('"') + 1:-1]
    body = tok[1:-1]
    if "\\" not in body:
        return body
    return re.sub(r"\\(u[0-9a-fA-F]{4}|.)", lambda m: unescape(m.group(1)), body)


def unescape(esc):
    if esc.startswith("u") and len(esc) == 5:
        return chr(int(esc[1:], 16))
    return {"n": "\n", "t": "\t", "r": "\r", "0": "\0"}.get(esc, esc)


def is_prose(value):
    """Does this literal look like text a player would read?"""
    v = value.strip()
    if not v or not PROSE_WORDS.search(v):
        return False
    if re.fullmatch(r"[a-z0-9_]+(\.[a-z0-9_]+)+", v):
        return False  # string-table key, e.g. "menu.play"
    if "://" in v or re.fullmatch(r"[^\s@]+@[^\s@]*", v):
        return False  # URLs, e-mail addresses
    if re.search(r'"\s*:', v) or v.startswith(('{"', ',"', '"')):
        return False  # JSON fragments
    if re.fullmatch(r"[A-Za-z0-9_.:/\\\-\[\]<>=#{}()]+", v) and not re.fullmatch(r"[A-Z][A-Z!?.\-']{1,}", v):
        # identifiers, paths, format strings, rich-text tags, GameObject names ("Button_", "BeamRunner.Language",
        # "yyyy-MM-dd", "<color=#") — but an ALL-CAPS word ("PLAY", "OK") is a label.
        return False
    return True


def line_of(src, pos):
    return src.count("\n", 0, pos) + 1


def enclosing_call(src, pos):
    """Name of the innermost call whose parentheses contain pos (best effort)."""
    depth = 0
    i = pos - 1
    while i >= 0:
        c = src[i]
        if c == ")":
            depth += 1
        elif c == "(":
            if depth == 0:
                j = i - 1
                while j >= 0 and src[j].isspace():
                    j -= 1
                k = j
                while k >= 0 and (src[k].isalnum() or src[k] in "_.<>"):
                    k -= 1
                name = src[k + 1:j + 1]
                pre = src[max(0, k - 8):k + 1]
                if pre.strip().endswith("new"):
                    name = "new " + name
                return name, i
            depth -= 1
        elif c in ";{}" and depth == 0:
            return None, -1
        i -= 1
    return None, -1


def call_chain(src, pos):
    """Every enclosing call name, innermost first."""
    names = []
    p = pos
    for _ in range(8):
        name, at = enclosing_call(src, p)
        if name is None:
            break
        names.append(name)
        p = at
    return names


def iter_cs(dirs, include_editor=False):
    for d in dirs:
        for root, _, files in os.walk(d):
            if not include_editor and os.sep + "Editor" in root:
                continue
            for f in sorted(files):
                if f.endswith(".cs"):
                    yield os.path.join(root, f)


def rel(path):
    return os.path.relpath(path, REPO)


# ----------------------------------------------------------------------------------------------- tables

def load_json_strict(path):
    def hook(pairs):
        d = {}
        for k, v in pairs:
            if k in d:
                raise ValueError("duplicate key %r" % k)
            d[k] = v
        return d
    with open(path, encoding="utf-8") as f:
        text = f.read()
    if text.startswith("﻿"):
        raise ValueError("UTF-8 BOM (save without BOM)")
    return json.loads(text, object_pairs_hook=hook)


PLACEHOLDER = re.compile(r"\{([A-Za-z_][A-Za-z0-9_]*)\}")
TAG = re.compile(r"</?(?:b|i|color|size)(?:=[^>]*)?>")


def forms_of(value):
    return value if isinstance(value, dict) else {"other": value}


def placeholders(text):
    return set(PLACEHOLDER.findall(text))


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--verbose", "-v", action="store_true")
    ap.add_argument("--no-glyphs", action="store_true", help="skip the font coverage check")
    args = ap.parse_args()

    errors = []
    warnings = []

    def err(msg):
        errors.append(msg)

    # -- tables
    tables = {}
    for code in LANGUAGES:
        path = os.path.join(LOC_DIR, code + ".json")
        if not os.path.exists(path):
            err("%s: missing string table" % rel(path))
            continue
        try:
            data = load_json_strict(path)
        except Exception as e:  # noqa: BLE001
            err("%s: not strict JSON: %s" % (rel(path), e))
            continue
        tables[code] = data
    extra_files = [f for f in os.listdir(LOC_DIR) if f.endswith(".json") and f[:-5] not in LANGUAGES] \
        if os.path.isdir(LOC_DIR) else []
    for f in extra_files:
        err("%s: a table for a language Languages.cs does not ship" % f)
    if "en" not in tables:
        report(errors, warnings)
        return 1

    en = {k: v for k, v in tables["en"].items() if not k.startswith("_")}
    en_ph = {k: set().union(*[placeholders(t) for t in forms_of(v).values()]) for k, v in en.items()}

    for code, data in tables.items():
        meta = data.get("_meta")
        if not isinstance(meta, dict) or meta.get("language") != code:
            err("%s.json: _meta.language must be %r" % (code, code))
        elif code != "en" and meta.get("machineTranslated") is not True and not meta.get("reviewedBy"):
            err("%s.json: _meta.machineTranslated must be true until a native speaker reviewed it (then set reviewedBy)" % code)
        entries = {k: v for k, v in data.items() if not k.startswith("_")}
        for k, v in entries.items():
            if isinstance(v, dict):
                for cat, form in v.items():
                    if not isinstance(form, str):
                        err("%s.json: %s.%s is not a string" % (code, k, cat))
            elif not isinstance(v, str):
                err("%s.json: %s is neither a string nor a plural object" % (code, k))
        if code == "en":
            continue
        missing = [k for k in en if k not in entries]
        extra = [k for k in entries if k not in en]
        for k in missing:
            err("%s.json: missing key %s" % (code, k))
        for k in extra:
            err("%s.json: key %s is not in en.json" % (code, k))
        for k, v in entries.items():
            if k not in en:
                continue
            check_entry(code, k, en[k], v, en_ph[k], err)

    # en itself: plural categories and tags
    for k, v in en.items():
        if isinstance(v, dict):
            cats = set(v)
            if cats != PLURAL_REQUIRED["en"]:
                err("en.json: %s needs exactly one/other (has %s)" % (k, sorted(cats)))
            if "n" not in en_ph[k]:
                err("en.json: plural %s never shows {n} (its count)" % k)

    # The English ATT prompt in Info.plist (Editor/Ios/FieldIosTrackingPlist.cs) and en.json's copy used for
    # InfoPlist.strings must say the same.
    ios_plist = os.path.join(SCRIPTS, "Editor", "Ios", "FieldIosTrackingPlist.cs")
    if "ios.tracking_usage" in en and os.path.exists(ios_plist):
        m = re.search(r'UsageDescription\s*=\s*((?:"(?:[^"\\]|\\.)*"\s*\+?\s*)+);', open(ios_plist, encoding="utf-8").read())
        if m:
            constant = "".join(literal_value(x) for x in re.findall(r'"(?:[^"\\]|\\.)*"', m.group(1)))
            if constant != en["ios.tracking_usage"]:
                err("en.json: ios.tracking_usage differs from FieldIosTrackingPlist.UsageDescription (%r)" % constant)

    # _meta native names must match Languages.cs
    native_cs = parse_languages_cs()
    for code, data in tables.items():
        meta = data.get("_meta") or {}
        if native_cs.get(code) and meta.get("nativeName") != native_cs[code]:
            err("%s.json: _meta.nativeName %r != Languages.cs %r" % (code, meta.get("nativeName"), native_cs.get(code)))

    # -- code
    used_literals = set()
    for path in iter_cs(KEY_DIRS, include_editor=True):
        src = strip_comments(open(path, encoding="utf-8").read())
        for m in LITERAL.finditer(src):
            used_literals.add(literal_value(m.group(0)))
        check_loc_calls(path, src, en, en_ph, err)

    for k in en:
        if k not in used_literals:
            err("en.json: key %s is never referenced in Scripts/ (unused: remove it, or reference it literally)" % k)

    prose_files = set(iter_cs(PROSE_DIRS))
    for path in iter_cs(SINK_DIRS):
        scan_literals(path, path in prose_files, err)

    # -- glyphs
    if not args.no_glyphs:
        check_glyphs(tables, native_cs, err, warnings)

    return report(errors, warnings)


def check_entry(code, k, en_value, value, en_union, err):
    en_plural = isinstance(en_value, dict)
    plural = isinstance(value, dict)
    if plural and not en_plural:
        err("%s.json: %s is plural but English is a simple string (add one/other in English first)" % (code, k))
        return
    if en_plural and not plural and code not in OTHER_ONLY:
        err("%s.json: %s must be a plural object (%s)" % (code, k, "/".join(sorted(PLURAL_REQUIRED[code]))))
        return
    forms = forms_of(value)
    if plural:
        cats = set(forms)
        need = PLURAL_REQUIRED[code]
        if not need <= cats:
            err("%s.json: %s lacks plural forms %s" % (code, k, sorted(need - cats)))
        bad = cats - PLURAL_ALLOWED[code]
        if bad:
            err("%s.json: %s has plural forms %s that %s never uses" % (code, k, sorted(bad), code))
    union = set()
    for cat, text in forms.items():
        ph = placeholders(text)
        union |= ph
        unknown = ph - en_union
        if unknown:
            err("%s.json: %s[%s] uses placeholders %s that English does not have" % (code, k, cat, sorted(unknown)))
        if cat == "other" and ph != en_union:
            err("%s.json: %s[other] placeholders %s != English %s" % (code, k, sorted(ph), sorted(en_union)))
        if code in ONE_IS_NOT_JUST_ONE and ph != en_union:
            err("%s.json: %s[%s] must show every placeholder %s (%s's \"one\" also covers other numbers)"
                % (code, k, cat, sorted(en_union), code))
        if sorted(TAG.findall(text)) != sorted(TAG.findall(forms_of(en_value).get("other", ""))):
            err("%s.json: %s[%s] rich-text tags differ from English" % (code, k, cat))
    if union != en_union:
        err("%s.json: %s placeholders %s != English %s" % (code, k, sorted(union), sorted(en_union)))


def parse_languages_cs():
    names = {}
    try:
        src = open(LANGUAGES_CS, encoding="utf-8").read()
    except OSError:
        return names
    for m in re.finditer(r'new LanguageInfo\("([^"]+)",\s*"([^"]+)"', src):
        names[m.group(1)] = m.group(2)
    m = re.search(r'English = new LanguageInfo\(EnglishCode,\s*"([^"]+)"', src)
    if m:
        names["en"] = m.group(1)
    return names


LOC_CALL = re.compile(r"\bLoc\.T\s*\(")


def split_args(src, open_paren):
    """Top-level arguments of the call whose '(' is at open_paren; returns (list of arg texts, end index)."""
    depth = 0
    args = []
    start = open_paren + 1
    i = open_paren
    n = len(src)
    while i < n:
        c = src[i]
        if c == '"' or (c in "@$" and i + 1 < n and src[i + 1] in '"@$'):
            m = LITERAL.match(src, i)
            if m:
                i = m.end()
                continue
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
            if depth == 0:
                args.append(src[start:i].strip())
                return args, i
        elif c == "," and depth == 1:
            args.append(src[start:i].strip())
            start = i + 1
        i += 1
    return args, n


def check_loc_calls(path, src, en, en_ph, err):
    for m in LOC_CALL.finditer(src):
        open_paren = m.end() - 1
        call_args, _ = split_args(src, open_paren)
        if not call_args or not call_args[0]:
            continue
        km = re.fullmatch(r'"([^"\\]+)"', call_args[0])
        if not km:
            continue  # key from a variable/array: checked by the unused-key scan instead
        key = km.group(1)
        where = "%s:%d" % (rel(path), line_of(src, m.start()))
        if key not in en:
            err("%s: Loc.T key %s is not in en.json" % (where, key))
            continue
        names = set()
        for a in call_args[1:]:
            tm = re.match(r'\(\s*"([A-Za-z_][A-Za-z0-9_]*)"\s*,', a)
            if tm:
                names.add(tm.group(1))
            else:
                names.add("?")
        if "?" in names:
            continue
        expected = en_ph[key]
        if names != expected:
            err("%s: Loc.T(\"%s\") passes %s but the English text uses %s" % (where, key, sorted(names), sorted(expected)))
        if isinstance(en[key], dict) and "n" not in names:
            err("%s: Loc.T(\"%s\") is plural: pass (\"n\", count)" % (where, key))


CASE_CALL = re.compile(r"\.(ToUpper|ToLower|ToUpperInvariant|ToLowerInvariant)\s*\(")
CULTURE_FORMAT = re.compile(r'\.ToString\(\s*"(?:N\d+|C\d*|P\d+)"\s*\)')


# Calls/assignments that put their string arguments on screen (or into a notification / the OS).
SINK_CALLS = re.compile(
    r"^(?:UIFactory\.(?:Label|LabelLine|Button|NeonTitle|Card|StatRow|ToggleRow|SliderRow|SettingRow|SegmentedControl)|"
    r"MetaUI\.(?:Section|Header|GemAmount|Toast)|RunUi\.Caption|Dialogs\.\w+|Toasts\.Show|Toast|\w+\.Toast|"
    r"FloatingText\.Spawn|MetaFx\.FloatAt|SpawnAtShip|\w*\.?Set(?:Label|SubLabel|Title|Text|Status|Progress|Value)|"
    r"(?:g|grid|\w*Grid)\.Add|RaiseNotify|Notify\??\.Invoke|NumberTicker\.Attach|\w*\.?SetFormat|"
    r"new\s+(?:AndroidNotificationChannel|AndroidNotification|iOSNotification))$")
SINK_ASSIGN = re.compile(r"(?:\.text|\bTitle|\bBody|\bSubtitle|\bDescription)\s*\+?=\s*$")


def scan_literals(path, prose_everywhere, err):
    name = os.path.basename(path)
    raw = open(path, encoding="utf-8").read()
    src = strip_comments(raw)
    lines = src.split("\n")
    allowed_file = name in ALLOWED_FILES
    in_ui_rules = prose_everywhere or os.sep + "Game" + os.sep in path or os.sep + "Localization" in path
    if in_ui_rules and not allowed_file:
        for m in CASE_CALL.finditer(src):
            ln = line_of(src, m.start())
            if "l10n-ok" in lines[ln - 1]:
                continue
            err("%s:%d: runtime case change .%s() — store strings in their final case (Turkish I, German ß); "
                "mark non-text uses with // l10n-ok" % (rel(path), ln, m.group(1)))
        for m in CULTURE_FORMAT.finditer(src):
            ln = line_of(src, m.start())
            if "l10n-ok" in lines[ln - 1]:
                continue
            err("%s:%d: culture-dependent number format %s — use Loc.N (language-aware, device-culture independent)"
                % (rel(path), ln, m.group(0)))
    if allowed_file:
        return
    for m in LITERAL.finditer(src):
        value = literal_value(m.group(0))
        if not is_prose(value):
            continue
        ln = line_of(src, m.start())
        line = lines[ln - 1]
        if "l10n-ok" in line:
            continue
        stripped = line.strip()
        if stripped.startswith("[") or re.match(r"(case\s|#)", stripped):
            continue  # attributes, switch labels, preprocessor
        if re.search(r"\bthrow\s+new\b", line):
            continue
        chain = call_chain(src, m.start())
        if any(DIAGNOSTIC_CALLS.match(c + "(") for c in chain):
            continue
        sink = next((c for c in chain if SINK_CALLS.match(c)), None)
        before = src[max(0, m.start() - 200):m.start()]
        stmt = before[max(before.rfind(";"), before.rfind("{"), before.rfind("}")) + 1:]
        assigned = SINK_ASSIGN.search(re.sub(r'"(?:[^"\\]|\\.)*"|\([^()]*\)', "", stmt).rstrip(" +?:(")) is not None
        if sink is None and not assigned:
            if not prose_everywhere:
                continue
            if chain and NAME_CALLS.match(chain[0] + "("):
                continue
        where = (" passed to %s" % sink) if sink else (" assigned to a text" if assigned else "")
        err("%s:%d: user-facing literal %r%s — use Loc.T(\"key\") (brand/non-text: mark the line // l10n-ok)"
            % (rel(path), ln, value if len(value) < 70 else value[:67] + "...", where))


# ----------------------------------------------------------------------------------------------- glyphs

def font_cmaps():
    try:
        from fontTools.ttLib import TTFont  # noqa: F401
    except ImportError:
        return None
    from fontTools.ttLib import TTFont
    cmaps = {}
    for script, files in FONTS.items():
        sets = []
        for f in files:
            path = os.path.join(FONT_DIR, f)
            if not os.path.exists(path):
                sets.append(None)
                continue
            sets.append(set(TTFont(path, lazy=True).getBestCmap().keys()))
        cmaps[script] = (files, sets)
    return cmaps


SAMPLE_NUMBERS = [0, 1, 2, 5, 21, 1234, 12345, 1000000]


def format_number(value, code):
    group, _ = NUMBER_FORMAT.get(code, (",", "."))
    digits = str(abs(value))
    min_group = 2 if code in ("es", "pl") else 1
    if len(digits) >= 3 + min_group:
        parts = []
        while len(digits) > 3:
            parts.insert(0, digits[-3:])
            digits = digits[:-3]
        parts.insert(0, digits)
        digits = group.join(parts)
    return digits


def rendered_samples(text, code):
    """The text as the game can show it: placeholders filled with formatted numbers (every separator)."""
    out = []
    for n in SAMPLE_NUMBERS:
        out.append(PLACEHOLDER.sub(format_number(n, code), text))
    _, dec = NUMBER_FORMAT.get(code, (",", "."))
    out.append(dec)
    return out


PSEUDO_UPPER = "ÀßÇÐÉFĜĦÎĴĶĿMÑÖÞQŔŠŢÛVŴXŸŽ"
PSEUDO_LOWER = "àbçðéfĝĥîĵķľmñöþqŕšţûvŵxÿž"


def pseudo(text):
    out = []
    for ch in text:
        if "A" <= ch <= "Z":
            out.append(PSEUDO_UPPER[ord(ch) - 65])
        elif "a" <= ch <= "z":
            out.append(PSEUDO_LOWER[ord(ch) - 97])
        else:
            out.append(ch)
    return "[" + "".join(out) + " ~]"


def check_glyphs(tables, native_cs, err, warnings):
    cmaps = font_cmaps()
    if cmaps is None:
        warnings.append("fontTools not installed: glyph coverage NOT checked (pip install fonttools)")
        return
    reported = set()

    def need(script, text, what):
        files, sets = cmaps[script]
        for f, cps in zip(files, sets):
            if cps is None:
                if (f, "missing") not in reported:
                    reported.add((f, "missing"))
                    err("Fonts/%s is missing (run python3 tools/build-fonts.py)" % f)
                continue
            missing = sorted({c for c in text if ord(c) not in cps and c not in "\n\r\t"})
            if missing:
                key = (f, what, "".join(missing))
                if key in reported:
                    continue
                reported.add(key)
                err("glyphs: %s cannot render %s in %s" % (f, " ".join("U+%04X(%s)" % (ord(c), c) for c in missing), what))

    for code, data in tables.items():
        script = SCRIPT_OF.get(code, "Latin")
        need(script, CODE_GLYPHS, "%s code glyphs" % code)
        for k, v in data.items():
            if k.startswith("_") or k.startswith(OS_RENDERED_PREFIXES):
                continue
            for cat, text in forms_of(v).items():
                if not isinstance(text, str):
                    continue
                for sample in rendered_samples(text, code):
                    need(script, TAG.sub("", sample), "%s.json %s[%s]" % (code, k, cat))
        name = native_cs.get(code)
        if name:
            need(script, name, "%s's picker name" % code)
    # pseudo-localization (Latin fonts), generated from English at runtime
    for k, v in tables.get("en", {}).items():
        if k.startswith("_") or k.startswith(OS_RENDERED_PREFIXES):
            continue
        for cat, text in forms_of(v).items():
            need("Latin", pseudo(PLACEHOLDER.sub("0", TAG.sub("", text))), "pseudo %s[%s]" % (k, cat))
    need("Latin", "[PŠÉÚÐÖ]", "pseudo picker name")


def report(errors, warnings):
    for w in warnings:
        print("warning: " + w)
    for e in errors:
        print("error: " + e)
    if errors:
        print("check-strings: FAIL (%d error(s))" % len(errors))
        return 1
    print("check-strings: OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
