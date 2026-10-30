#!/usr/bin/env python3
"""Builds the non-Latin UI fonts (Resources/BeamRunner/Fonts) as subsets of pinned Google Fonts sources.

The Latin set (Audiowide, Rajdhani) is shipped as-is. Russian, Japanese, Korean and Simplified Chinese get their own
font set (see Scripts/Runtime/UI/Kit/UIFonts.cs); a full CJK font is 10-18 MB, so each font here is cut down to the
characters its languages actually show:

  * every character of the script's string tables (Localization/<lang>.json, all plural forms, native name),
  * printable ASCII (numbers, English fallback text, Latin brand/ship names, version strings), NBSP, common
    punctuation and currency signs (store prices come from the store in the device's currency),
  * for Cyrillic, the whole Russian alphabet (keyboard key names such as "Ф"/"В" come from the OS layout),
  * for CJK, the common full-width punctuation.

Variable sources are instanced to one static weight (fontTools.varLib.instancer), layout tables Unity's legacy
text renderer never reads (GSUB/GPOS/BASE/vertical metrics) are dropped, and the files are written with fixed
timestamps so a rebuild from unchanged tables is byte-identical.

Run after editing any ru/ja/ko/zh-Hans string (tools/check-strings.py fails on glyphs a font cannot render):

    pip install fonttools
    python3 tools/build-fonts.py                       # downloads the pinned sources once (~38 MB, cached)
    python3 tools/build-fonts.py --source-dir ~/google-fonts   # or use a google/fonts checkout
    python3 tools/gen-metas.py                         # only when a font file is new

Licences: every source family is SIL OFL 1.1. Russo One carries the Reserved Font Name "Russo", so its modified
(subset) version is renamed "Neo Title Cyrillic" as the OFL requires; Noto's RFN "Source" and Tektur (no RFN) are
not affected. Fonts/OFL.txt must hold each family's copyright + licence; this script appends a missing section.
"""
import argparse
import hashlib
import io
import json
import os
import sys
import urllib.request

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(REPO, "BeamRunner", "Assets", "BeamRunner")
LOC_DIR = os.path.join(ASSETS, "Resources", "BeamRunner", "Localization")
FONT_DIR = os.path.join(ASSETS, "Resources", "BeamRunner", "Fonts")
LICENCE = os.path.join(FONT_DIR, "OFL.txt")

# google/fonts commit the sources are taken from, and the SHA-256 of every file used.
GOOGLE_FONTS_COMMIT = "23e54b51ddffbc7713c583748e3bd86f62b1fa4a"
RAW_URL = "https://raw.githubusercontent.com/google/fonts/%s/%s"
SOURCES = {
    "russoone": ("ofl/russoone/RussoOne-Regular.ttf",
                 "bc0abcc660bd8b7ad3000ecb2898a27c58a29a50f7ec81652fa12e75148d09df"),
    "tektur": ("ofl/tektur/Tektur[wdth,wght].ttf",
               "a4c451e5336874811267c2a514efa09bca7bc8cabf32a310f0b44e111eacb933"),
    "notosansjp": ("ofl/notosansjp/NotoSansJP[wght].ttf",
                   "c2f3b4d463500a2ddcd3849cded1fceeb9fd6d1c32e6cbecd568453ba50fc68f"),
    "notosanskr": ("ofl/notosanskr/NotoSansKR[wght].ttf",
                   "194018e6b2b293a7964f037b25c0249ce1418bc9ab3c971060a03aa57861e252"),
    "notosanssc": ("ofl/notosanssc/NotoSansSC[wght].ttf",
                   "a3041811a78c361b1de50f953c805e0244951c21c5bd412f7232ef0d899af0da"),
}
LICENCES = {
    "russoone": ("ofl/russoone/OFL.txt", "3ac1301549523d9861fedca12871f24e575fbd26d520632fc00ba849b471d275"),
    "tektur": ("ofl/tektur/OFL.txt", "ff4bb37f716836feb5038e1bf5c47e5a1946717fcfabb175086ed49f85ea47fd"),
    "notosansjp": ("ofl/notosansjp/OFL.txt", "1c05c68c34f9708415aada51f17e1b0092d2cea709bf4a94cd38114f9e73d7d9"),
}
LICENCE_SECTIONS = [
    # (source licence, section title, files, note)
    ("russoone", "Russo One (as \"Neo Title Cyrillic\")", "FieldTitleCyrillic-Regular.ttf",
     "Modified Version: a glyph subset of Russo One (google/fonts ofl/russoone), renamed \"Neo Title Cyrillic\"\n"
     "because \"Russo\" is a Reserved Font Name. Built by tools/build-fonts.py."),
    ("tektur", "Tektur", "Tektur-Condensed-Medium.ttf, Tektur-Condensed-SemiBold.ttf",
     "Modified Version: static instances (width 85, weights 450 / 600) of the Tektur variable font, subset to the\n"
     "characters the game shows. Built by tools/build-fonts.py."),
    ("notosansjp", "Noto Sans JP / Noto Sans KR / Noto Sans SC",
     "NotoSansJP-Medium.ttf, NotoSansJP-Bold.ttf, NotoSansKR-Medium.ttf, NotoSansKR-Bold.ttf,\n"
     "  NotoSansSC-Medium.ttf, NotoSansSC-Bold.ttf",
     "Modified Versions: static instances (weights 500 / 700) of the Noto Sans JP, KR and SC variable fonts, subset\n"
     "to the characters the game shows. Built by tools/build-fonts.py. (The three families share this licence.)"),
]

# Keys the OS renders (iOS InfoPlist.strings), not the game's fonts; must match tools/check-strings.py.
OS_RENDERED_PREFIXES = ("ios.",)

ASCII = "".join(chr(c) for c in range(0x20, 0x7F))
PUNCTUATION = "  …–—‘’“”·•×«»„№°"
CURRENCY = "¢£¥€₩₽₺₹₴₫₱₪฿₸￥￦"
RUSSIAN = "".join(chr(c) for c in range(0x0410, 0x0450)) + "Ёё"
CJK_PUNCTUATION = ("　、。「」『』【】《》・ー"
                   "！（），．：；？～")


class Build(object):
    def __init__(self, out, source, langs, axes=None, family=None, style="Regular", extra=""):
        self.out = out            # file name without extension (UIFonts.Names)
        self.source = source      # key of SOURCES
        self.langs = langs        # string tables whose characters the font must hold
        self.axes = axes          # variable-font location to instance, or None for a static source
        self.family = family      # new family name (None: keep the source's)
        self.style = style
        self.extra = extra


BUILDS = [
    Build("FieldTitleCyrillic-Regular", "russoone", ["ru"], family="Neo Title Cyrillic", extra=RUSSIAN),
    Build("Tektur-Condensed-Medium", "tektur", ["ru"], {"wdth": 85, "wght": 450},
          family="Tektur Condensed Medium", extra=RUSSIAN),
    Build("Tektur-Condensed-SemiBold", "tektur", ["ru"], {"wdth": 85, "wght": 600},
          family="Tektur Condensed SemiBold", extra=RUSSIAN),
    Build("NotoSansJP-Medium", "notosansjp", ["ja"], {"wght": 500}, family="Noto Sans JP Medium",
          extra=CJK_PUNCTUATION),
    Build("NotoSansJP-Bold", "notosansjp", ["ja"], {"wght": 700}, family="Noto Sans JP", style="Bold",
          extra=CJK_PUNCTUATION),
    Build("NotoSansKR-Medium", "notosanskr", ["ko"], {"wght": 500}, family="Noto Sans KR Medium",
          extra=CJK_PUNCTUATION),
    Build("NotoSansKR-Bold", "notosanskr", ["ko"], {"wght": 700}, family="Noto Sans KR", style="Bold",
          extra=CJK_PUNCTUATION),
    Build("NotoSansSC-Medium", "notosanssc", ["zh-Hans"], {"wght": 500}, family="Noto Sans SC Medium",
          extra=CJK_PUNCTUATION),
    Build("NotoSansSC-Bold", "notosanssc", ["zh-Hans"], {"wght": 700}, family="Noto Sans SC", style="Bold",
          extra=CJK_PUNCTUATION),
]

# Tables Unity's legacy (FreeType) text renderer does not use.
DROP_TABLES = ["GSUB", "GPOS", "GDEF", "BASE", "JSTF", "MATH", "STAT", "vhea", "vmtx", "VORG", "DSIG", "FFTM",
               "meta", "morx", "kerx", "feat"]


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def fetch(rel_path, digest, source_dir, cache_dir):
    """The bytes of a google/fonts file: from --source-dir, the cache, or downloaded (always hash-checked)."""
    candidates = []
    if source_dir:
        candidates.append(os.path.join(source_dir, rel_path))
    cached = os.path.join(cache_dir, GOOGLE_FONTS_COMMIT[:12], rel_path)
    candidates.append(cached)
    for path in candidates:
        if os.path.exists(path):
            with open(path, "rb") as f:
                data = f.read()
            if sha256(data) == digest:
                return data
            print("warning: %s does not match the pinned SHA-256, ignored" % path, file=sys.stderr)
    url = RAW_URL % (GOOGLE_FONTS_COMMIT, rel_path.replace("[", "%5B").replace("]", "%5D").replace(",", "%2C"))
    print("download %s" % url)
    with urllib.request.urlopen(url, timeout=120) as r:
        data = r.read()
    if sha256(data) != digest:
        raise SystemExit("error: %s: SHA-256 %s, expected %s" % (url, sha256(data), digest))
    os.makedirs(os.path.dirname(cached), exist_ok=True)
    with open(cached, "wb") as f:
        f.write(data)
    return data


def table_text(code):
    """Every character a string table can show (values of all plural forms + the native name)."""
    with open(os.path.join(LOC_DIR, code + ".json"), encoding="utf-8") as f:
        data = json.load(f)
    chars = []
    for key, value in data.items():
        if key == "_meta":
            chars.append(value.get("nativeName", ""))
            continue
        if key.startswith("_") or key.startswith(OS_RENDERED_PREFIXES):
            continue
        if isinstance(value, dict):
            chars.extend(v for v in value.values() if isinstance(v, str))
        elif isinstance(value, str):
            chars.append(value)
    return "".join(chars)


def rename(font, build):
    """Sets the family/style names (Windows + Mac records) and drops unused axis-value names."""
    name = font["name"]
    family = build.family
    style = build.style
    full = family if style == "Regular" else family + " " + style
    ps = build.out
    keep = {0, 7, 8, 9, 10, 11, 12, 13, 14}
    name.names = [n for n in name.names if n.nameID in keep]
    for platform in ((3, 1, 0x409), (1, 0, 0)):
        name.setName(family, 1, *platform)
        name.setName(style, 2, *platform)
        name.setName("%s;%s" % (ps, "BeamRunner-subset"), 3, *platform)
        name.setName(full, 4, *platform)
        name.setName(ps, 6, *platform)
    if build.source == "russoone":
        # Keep the original description (it carries a copyright line) and say what was changed.
        original = name.getDebugName(10) or ""
        note = ("Neo Title Cyrillic is a subset of Russo One by Jovanny Lemonad, renamed as required by the SIL "
                "Open Font License (Reserved Font Name \"Russo\").")
        name.removeNames(nameID=10)
        name.setName((original + " " + note).strip(), 10, 3, 1, 0x409)
    # Keep the style bits consistent with the new names (Unity reads the family name only, but tools may not).
    bold = style == "Bold"
    os2 = font["OS/2"]
    os2.fsSelection = (os2.fsSelection & ~0b1100001) | (0b100000 if bold else 0b1000000)
    font["head"].macStyle = 1 if bold else 0


def build_font(build, source_bytes, text):
    from fontTools import subset
    from fontTools.ttLib import TTFont

    font = TTFont(io.BytesIO(source_bytes))
    created = font["head"].created
    options = subset.Options()
    options.layout_features = []
    options.name_IDs = ["*"]
    options.name_languages = ["*"]
    options.name_legacy = True
    options.notdef_outline = True
    options.glyph_names = False
    options.drop_tables = list(options.drop_tables) + DROP_TABLES
    options.hinting = True
    subsetter = subset.Subsetter(options)
    subsetter.populate(text=text)
    subsetter.subset(font)

    if build.axes:
        from fontTools.varLib import instancer
        font = instancer.instantiateVariableFont(font, dict(build.axes), updateFontNames=False)
        for tag in ("STAT", "avar", "HVAR", "MVAR", "cvar"):
            if tag in font:
                del font[tag]
        # Static instance: the OS/2 weight class says what was instanced.
        font["OS/2"].usWeightClass = int(build.axes.get("wght", font["OS/2"].usWeightClass))
        if "wdth" in build.axes:
            font["OS/2"].usWidthClass = 3 if build.axes["wdth"] <= 87.5 else 5

    if build.family:
        rename(font, build)

    font.recalcTimestamp = False
    font["head"].created = created
    font["head"].modified = created
    out = io.BytesIO()
    font.save(out)
    return out.getvalue(), font


def ensure_licences(source_dir, cache_dir):
    with open(LICENCE, encoding="utf-8") as f:
        text = f.read()
    changed = False
    for key, title, files, note in LICENCE_SECTIONS:
        rel_path, digest = LICENCES[key]
        body = fetch(rel_path, digest, source_dir, cache_dir).decode("utf-8").replace("\r\n", "\n").strip("\n")
        copyright_line = body.splitlines()[0]
        if copyright_line in text:
            continue
        section = "\n".join([
            "",
            "=" * 64,
            title,
            "=" * 64,
            "Files: " + files,
            note,
            "",
            body,
            "",
        ])
        text = text.rstrip("\n") + "\n" + section
        changed = True
        print("OFL.txt: added %s" % title)
    if changed:
        with open(LICENCE, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--source-dir", help="a google/fonts checkout (at %s) to read the sources from"
                    % GOOGLE_FONTS_COMMIT[:12])
    ap.add_argument("--cache-dir", default=os.path.join(os.path.expanduser("~"), ".cache", "beamrunner-fonts"),
                    help="download cache (default ~/.cache/beamrunner-fonts)")
    ap.add_argument("--only", nargs="*", help="build only these outputs (file names without .ttf)")
    args = ap.parse_args()
    try:
        import fontTools  # noqa: F401
        from fontTools.varLib import instancer  # noqa: F401
    except ImportError:
        raise SystemExit("error: fontTools is required (pip install fonttools)")

    sources = {}
    total = 0
    for build in BUILDS:
        if args.only and build.out not in args.only:
            continue
        if build.source not in sources:
            rel_path, digest = SOURCES[build.source]
            sources[build.source] = fetch(rel_path, digest, args.source_dir, args.cache_dir)
        text = ASCII + PUNCTUATION + CURRENCY + build.extra
        for code in build.langs:
            text += table_text(code)
        chars = sorted(set(c for c in text if c not in "\n\r\t"))
        data, font = build_font(build, sources[build.source], "".join(chars))
        cmap = font.getBestCmap()
        missing = [c for c in chars if ord(c) not in cmap and c not in CURRENCY + PUNCTUATION + CJK_PUNCTUATION]
        if missing:
            raise SystemExit("error: %s lacks %s" % (build.out, " ".join("U+%04X" % ord(c) for c in missing)))
        path = os.path.join(FONT_DIR, build.out + ".ttf")
        old = None
        if os.path.exists(path):
            with open(path, "rb") as f:
                old = f.read()
        if old != data:
            with open(path, "wb") as f:
                f.write(data)
        total += len(data)
        print("%-30s %6.1f KB  %5d glyphs  %s" % (build.out + ".ttf", len(data) / 1024.0, len(font.getGlyphOrder()),
                                                  "unchanged" if old == data else "written"))
    ensure_licences(args.source_dir, args.cache_dir)
    print("total %.1f KB" % (total / 1024.0))
    return 0


if __name__ == "__main__":
    sys.exit(main())
