#!/usr/bin/env python3
"""unity-compile-check driver.

    python3 ucc.py setup [--force]                (called by setup.sh)
    python3 ucc.py check [--project PATH] ...     (called by check.sh)

Everything that is downloaded, generated or built lives in the cache dir (default
~/.cache/unity-compile-check, override with UCC_CACHE), never in the repo; git clones live under
UCC_SRC_ROOT (see _src_root) or the UCC_*_DIR overrides and are only needed by `setup`.
"""
from __future__ import annotations

import argparse
import contextlib
import fcntl
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
import zipfile
from collections import Counter, OrderedDict
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parent
REPO_ROOT = TOOL_DIR.parent.parent            # tools/unity-compile-check -> repository root
CACHE = Path(os.environ.get("UCC_CACHE", "~/.cache/unity-compile-check")).expanduser()


def _src_root() -> Path:
    """Parent folder of the git clones: $UCC_SRC_ROOT; else /home/user when it is a writable directory (the Claude
    Code container layout, where the clones are shared with other tools); else $UCC_CACHE/src (e.g. CI runners)."""
    if os.environ.get("UCC_SRC_ROOT"):
        return Path(os.environ["UCC_SRC_ROOT"]).expanduser()
    home_user = Path("/home/user")
    if home_user.is_dir() and os.access(home_user, os.W_OK):
        return home_user
    return CACHE / "src"


def _dir_env(name: str, default: Path) -> Path:
    return Path(os.environ[name]).expanduser() if os.environ.get(name) else default


SRC_ROOT = _src_root()
NEEDLE_DIR = _dir_env("UCC_NEEDLE_DIR", SRC_ROOT / "needle-mirror")
CSREF_DIR = _dir_env("UCC_CSREF_DIR", SRC_ROOT / "unity-technologies" / "unitycsreference")
URL_DIR = _dir_env("UCC_URL_DIR", SRC_ROOT / "lavagang" / "unity-runtime-libraries")
GRAPHICS_DIR = _dir_env("UCC_GRAPHICS_DIR", SRC_ROOT / "unity-technologies" / "graphics")
DEFAULT_PROJECT = REPO_ROOT / "BeamRunner"

# Bump when the layout / build recipe of the cache changes (forces a rebuild of libs on next setup).
RECIPE_VERSION = "5"

# ----------------------------------------------------------------------------------------------
# Pinned inputs
# ----------------------------------------------------------------------------------------------

# The project's editor version (ProjectSettings/ProjectVersion.txt). Used for the define ladder, for asmdef
# versionDefines ("Unity" entries) and as the UnityCsReference tag of the API audit index.
UNITY_VERSION = "6000.3.25f1"
UNITY_VERSION_NUM = "6000.3.25"

# Real Unity 6 player managed assemblies (UnityEngine*.dll). LavaGang/Unity-Runtime-Libraries has no 6000.1+ zip;
# 6000.0.58 is the newest one there, so it is the engine reference set (6.3-only engine APIs are classified
# against the 6000.3.25f1 UnityCsReference index instead, see classify_unity6 / the API audit).
ENGINE_REF_VERSION = "6000.0.58"
URL_COMMIT = "499ad151080eed6125db0cbfb42440ba75a5a006"
URL_ZIP = ("6000.0.58.zip", "68e7a741887ebce419ed875453805cca8d11c17be18066a2df1890125f62d369")
# Unity-Technologies/Graphics, branch 6000.3/staging HEAD as of 2026-07-01 (URP/Core package.json = 17.3.0; the
# 6000.3 core packages keep 17.3.0 for the whole 6000.3 cycle), i.e. the SRP sources of the 6000.3.25f1 era.
GRAPHICS_COMMIT = "2d2e78cc9d6254bc6e7c9c5552cea053508e86cb"
# getsentry/unity (UPM distribution of Sentry for Unity): tag 4.10.3 = commit 0f9db212 (2026-09-24)
SENTRY_TAG = "4.10.3"
GRAPHICS_SPARSE = [
    "/Packages/com.unity.render-pipelines.core/package.json",
    "/Packages/com.unity.render-pipelines.core/Runtime/",
    "/Packages/com.unity.render-pipelines.core/Runtime-PrivateShared/",
    "/Packages/com.unity.render-pipelines.universal/package.json",
    "/Packages/com.unity.render-pipelines.universal/Runtime/",
    "/Packages/com.unity.render-pipelines.universal-config/package.json",
    "/Packages/com.unity.render-pipelines.universal-config/Runtime/",
    "/Packages/com.unity.shadergraph/package.json",
] + [f"/Packages/{pkg}/{d}/{pat}" for pkg, d in (("com.unity.render-pipelines.core", "ShaderLibrary"),
                                                    ("com.unity.render-pipelines.universal", "ShaderLibrary"),
                                                    ("com.unity.render-pipelines.universal", "Shaders"),
                                                    ("com.unity.shadergraph", "ShaderGraphLibrary"))
     for pat in ("**/*.cs", "**/*.cs.meta", "*.asmdef", "*.asmdef.meta")]

NUGET = OrderedDict([
    # id (lowercase): (version, sha256 of the .nupkg)
    ("unity3d.sdk", ("2021.1.14.1", "c8c455eaf62dfc915fe316cf5fede8530ec56dfcfcf05df1e74b80ca23b61b62")),
    ("netstandard.library.ref", ("2.1.0", "46ea2fcbd10a817685b85af7ce0c397d12944bdc81209e272de1e05efd33c78a")),
    ("nunit", ("3.5.0", "a040473a34901526aae1af7e02f7ec733657206d36e5aae1827098544aa03b3b")),
])

# Sparse-checkout patterns (non-cone, gitignore syntax) for FRESH clones of tag sources: everything except hidden
# ("." prefix) and "~" folders, which Unity - and this tool (collect_sources / find_precompiled) - ignore anyway. The
# big win is com.unity.burst: its hidden .Runtime/ folder (native compiler binaries) is ~670 MB of the ~680 MB.
# Existing (full) clones are left as they are.
NEEDLE_SPARSE = ["/*", "!.*/", "!*~/"]
# UnityCsReference: only what `UccTool index` reads (*.cs under these roots).
CSREF_SPARSE = [f"/{d}/**/*.cs" for d in ("Runtime", "Modules", "Editor", "artifacts/generated")]


def _needle(name, tag):
    return (name, {"url": f"https://github.com/needle-mirror/{name}", "tag": tag, "sparse": NEEDLE_SPARSE,
                   "dest": NEEDLE_DIR / name})


GIT_SOURCES = OrderedDict([
    # name: {url, tag | commit, sparse patterns, dest}. Fresh clones are shallow (depth 1), blob-less partial and
    # sparse (only the blobs of the checked-out paths are downloaded); commit sources also re-apply their patterns
    # to existing clones.
    _needle("com.unity.ugui", "1.0.0/Unity-2021.1.17f1"),   # newest mirrored uGUI; 2.0.0 is not mirrored
    _needle("com.unity.inputsystem", "1.20.0"),
    _needle("com.unity.services.core", "1.18.0"),
    _needle("com.unity.purchasing", "5.4.3"),
    _needle("com.unity.ads.ios-support", "1.2.0"),
    _needle("com.unity.mobile.notifications", "2.5.0"),   # local reminders (Unity 6.0+; released 2026-09-21)
    _needle("com.unity.mathematics", "1.3.2"),
    _needle("com.unity.burst", "1.6.0-pre.2"),   # newest mirrored Burst (Unity 6.3 ships 1.8.x)
    _needle("com.unity.collections", "2.5.1"),
    # Unity's Json.NET 13.0.2 build (has Newtonsoft.Json.Utilities.AotHelper, used by IAP 5); AOT dll for players
    _needle("com.unity.nuget.newtonsoft-json", "3.2.2"),
    # PlayMode smoke tests: the Unity Test Framework (the manifest's version) for UnityEngine.TestRunner, and Unity's
    # custom NUnit 3.5 build it is compiled against (2.0.5 = what Unity 6 resolves; see setup_test_framework)
    _needle("com.unity.test-framework", "1.4.6"),
    _needle("com.unity.ext.nunit", "2.0.5"),
    # Google Play Games plugin for Unity (only distributed as a .unitypackage; the project vendors its runtime under
    # Assets/GooglePlayGames): the Google.Play.Games runtime asmdef is built from the release tag's real sources
    ("play-games-plugin-for-unity", {"url": "https://github.com/playgameservices/play-games-plugin-for-unity",
                                     "tag": "v2.2.1", "dest": SRC_ROOT / "playgameservices" / "play-games-plugin-for-unity",
                                     "sparse": ["/Assets/Public/GooglePlayGames/com.google.play.games/Runtime/",
                                                "/Assets/Public/GooglePlayGames/com.google.play.games/package.json",
                                                "/LICENSE"]}),
    ("UnityCsReference", {"url": "https://github.com/Unity-Technologies/UnityCsReference", "tag": UNITY_VERSION,
                          "sparse": CSREF_SPARSE, "dest": CSREF_DIR}),
    ("Unity-Runtime-Libraries", {"url": "https://github.com/LavaGang/Unity-Runtime-Libraries", "commit": URL_COMMIT,
                                 "sparse": ["/" + URL_ZIP[0]], "dest": URL_DIR}),
    ("Graphics", {"url": "https://github.com/Unity-Technologies/Graphics", "commit": GRAPHICS_COMMIT,
                  "sparse": GRAPHICS_SPARSE, "dest": GRAPHICS_DIR}),
    # Sentry for Unity (crash reporting): the official UPM repo ships the SDK as precompiled assemblies - only the
    # runtime DLLs are checked out (the full tag is ~300 MB of native plugins and sentry-cli binaries)
    ("io.sentry.unity", {"url": "https://github.com/getsentry/unity", "tag": SENTRY_TAG,
                         "sparse": ["/package.json", "/Runtime/*.dll", "/Runtime/*.dll.meta"],
                         "dest": _dir_env("UCC_SENTRY_DIR", SRC_ROOT / "getsentry" / "unity")}),
])

# Precompiled package assemblies used as-is (auto-referenced by Assembly-CSharp in Unity, "Any Platform" in their
# .meta): (GIT_SOURCES name, sub folder, DLL file names). Copied into libs/unity6/{player,player-ios} by setup_libs,
# so every Assembly-CSharp stage (and the editor stages, which read libs/player) compiles against the real API.
# Sentry's platform-specific DLLs (Sentry.Unity.Android/iOS/Native*) are internal plumbing and left out.
PRECOMPILED_PACKAGES = [
    ("io.sentry.unity", "Runtime", ["Sentry.dll", "Sentry.Unity.dll", "Sentry.Microsoft.Bcl.AsyncInterfaces.dll",
                                    "Sentry.System.Collections.Immutable.dll", "Sentry.System.Reflection.Metadata.dll",
                                    "Sentry.System.Runtime.CompilerServices.Unsafe.dll",
                                    "Sentry.System.Text.Encodings.Web.dll", "Sentry.System.Text.Json.dll"]),
]

TEST_PACKAGES = {"NUnit": "3.14.0", "NUnit3TestAdapter": "4.6.0", "Microsoft.NET.Test.Sdk": "17.11.1"}

# Package versions used to evaluate asmdef "versionDefines" when building package assemblies (mirrors
# BeamRunner/Packages/manifest.json + the versions Unity 6000.3.25f1 resolves for their dependencies).
# com.unity.services.analytics / .authentication are deliberately absent: they are not in the project, so IAP is
# built without its optional analytics / authentication integrations (as Unity would).
PACKAGE_VERSIONS = {
    "com.unity.ugui": "2.0.0", "com.unity.inputsystem": "1.20.0", "com.unity.purchasing": "5.4.3",
    "com.unity.services.core": "1.18.0", "com.unity.nuget.newtonsoft-json": "3.2.2",
    "com.unity.ads.ios-support": "1.2.0", "com.unity.services.levelplay": "9.5.1",
    "com.unity.render-pipelines.core": "17.3.0", "com.unity.render-pipelines.universal": "17.3.0",
    "com.unity.test-framework": "1.4.6", "com.unity.mathematics": "1.3.2",
    "com.unity.collections": "2.5.1", "com.unity.burst": "1.8.25", "com.unity.shadergraph": "17.3.0",
    "com.unity.render-pipelines.universal-config": "17.0.3", "com.unity.searcher": "4.9.3",
    "com.unity.mobile.notifications": "2.5.0", "io.sentry.unity": SENTRY_TAG,
    "com.google.play.games": "2.2.1",
}
for _m in ("accessibility ai androidjni animation assetbundle audio cloth director imageconversion imgui jsonserialize "
           "particlesystem physics physics2d screencapture terrain terrainphysics tilemap ui uielements umbra "
           "unityanalytics unitywebrequest unitywebrequestassetbundle unitywebrequestaudio unitywebrequesttexture "
           "unitywebrequestwww vehicles video vr wind xr").split():
    PACKAGE_VERSIONS["com.unity.modules." + _m] = "1.0.0"

ASMDEF_ALIASES = {"Unity.ugui": "UnityEngine.UI"}   # uGUI 2.0.0 asmdef name used by Input System 1.12

# ----------------------------------------------------------------------------------------------
# Define symbols
# ----------------------------------------------------------------------------------------------


def version_ladder(major_minor_list):
    return [f"UNITY_{a}_{b}_OR_NEWER" for a, b in major_minor_list]


_LADDER = [(5, 3), (5, 4), (5, 5), (5, 6)] + [(y, m) for y in (2017, 2018, 2019, 2020) for m in (1, 2, 3, 4) if not (y == 2020 and m == 4)] + \
    [(2021, 1), (2021, 2), (2021, 3), (2022, 1), (2022, 2), (2022, 3), (2023, 1), (2023, 2), (2023, 3),
     (6000, 0), (6000, 1), (6000, 2), (6000, 3)]

# What Unity 6000.3.25f1 defines for every compilation (packages and Assembly-CSharp alike):
# UNITY_<major>_<minor>_OR_NEWER ladder up to UNITY_6000_3_OR_NEWER, plus UNITY_6000, UNITY_6000_3, UNITY_6000_3_25.
_maj, _min, _pat = UNITY_VERSION_NUM.split(".")
UNITY_VERSION_DEFINES = version_ladder(_LADDER) + [f"UNITY_{_maj}", f"UNITY_{_maj}_{_min}", f"UNITY_{_maj}_{_min}_{_pat}"]
ENGINE_LABEL = f"UnityEngine {ENGINE_REF_VERSION} (real player assemblies; project is {UNITY_VERSION})"

COMMON_DEFINES = [
    "TRACE", "CSHARP_7_OR_LATER", "CSHARP_7_3_OR_NEWER", "NET_STANDARD_2_0", "NET_STANDARD_2_1", "NET_STANDARD",
    "NETSTANDARD2_1", "NETSTANDARD", "ENABLE_INPUT_SYSTEM", "ENABLE_LEGACY_INPUT_MANAGER",
    "ENABLE_AUDIO", "ENABLE_CACHING", "ENABLE_CLOTH", "ENABLE_EVENT_QUEUE", "ENABLE_MICROPHONE", "ENABLE_MULTIPLE_DISPLAYS",
    "ENABLE_PHYSICS", "ENABLE_TEXTURE_STREAMING", "ENABLE_VIRTUALTEXTURING", "ENABLE_LZMA", "ENABLE_UNITYEVENTS",
    "ENABLE_VR", "ENABLE_AR", "ENABLE_WEBCAM", "ENABLE_UNITYWEBREQUEST", "ENABLE_WWW", "ENABLE_CLOUD_SERVICES",
    "ENABLE_CLOUD_SERVICES_ADS", "ENABLE_CLOUD_SERVICES_USE_WEBREQUEST", "ENABLE_CLOUD_SERVICES_CRASH_REPORTING",
    "ENABLE_CLOUD_SERVICES_PURCHASING", "ENABLE_CLOUD_SERVICES_ANALYTICS", "ENABLE_CLOUD_SERVICES_BUILD",
    "ENABLE_CLOUD_LICENSE", "ENABLE_DIRECTOR", "ENABLE_LOCALIZATION", "ENABLE_SPRITES", "ENABLE_TERRAIN", "ENABLE_TILEMAP",
    "ENABLE_TIMELINE", "ENABLE_NETWORK", "ENABLE_RUNTIME_GI", "ENABLE_VIDEO", "ENABLE_ACCESSIBILITY", "ENABLE_CUSTOM_RENDER_TEXTURE",
    "ENABLE_MANAGED_JOBS", "ENABLE_MANAGED_TRANSFORM_JOBS", "ENABLE_MANAGED_ANIMATION_JOBS", "ENABLE_MANAGED_AUDIO_JOBS",
    "ENABLE_MANAGED_UNITYTLS", "ENABLE_BURST_AOT", "TEXTCORE_1_0_OR_NEWER", "PLATFORM_ARCH_64", "UNITY_64",
    "INCLUDE_DYNAMIC_GI", "ENABLE_MONO_BDWGC", "ENABLE_SCRIPTING_GC_WBARRIERS", "RENDER_SOFTWARE_CURSOR",
]

PLATFORM_DEFINES = {
    "android": ["UNITY_ANDROID", "UNITY_ANDROID_API", "PLATFORM_ANDROID", "UNITY_MOBILE", "ENABLE_IL2CPP", "ENABLE_EGL",
                "ENABLE_ETC_COMPRESSION", "ENABLE_ONSCREEN_KEYBOARD", "ENABLE_UNITYADS_RUNTIME", "UNITY_UNITYADS_API",
                "PLATFORM_SUPPORTS_ADS_ID", "UNITY_CAN_SHOW_SPLASH_SCREEN", "UNITY_HAS_GOOGLEVR", "ENABLE_SPATIALTRACKING",
                "PLATFORM_EXTENDS_VULKAN_DEVICE", "PLATFORM_HAS_MULTIPLE_SWAPCHAINS", "UNITY_ANDROID_SUPPORTS_SHADOWFILES",
                "ENABLE_CRUNCH_TEXTURE_COMPRESSION"],
    "ios": ["UNITY_IOS", "UNITY_IPHONE", "UNITY_IPHONE_API", "PLATFORM_IOS", "UNITY_MOBILE", "ENABLE_IL2CPP",
            "ENABLE_GAMECENTER", "ENABLE_IOS_ON_DEMAND_RESOURCES", "ENABLE_IOS_APP_SLICING", "ENABLE_ONSCREEN_KEYBOARD",
            "ENABLE_UNITYADS_RUNTIME", "UNITY_UNITYADS_API", "PLATFORM_SUPPORTS_ADS_ID", "UNITY_CAN_SHOW_SPLASH_SCREEN",
            "ENABLE_SPATIALTRACKING", "ENABLE_CRUNCH_TEXTURE_COMPRESSION"],
    "standalone": ["UNITY_STANDALONE_LINUX", "UNITY_STANDALONE", "PLATFORM_STANDALONE_LINUX", "PLATFORM_STANDALONE", "ENABLE_MONO"],
}

EDITOR_DEFINES = ["UNITY_EDITOR", "UNITY_EDITOR_64", "UNITY_EDITOR_LINUX", "DEBUG", "UNITY_ASSERTIONS", "ENABLE_PROFILER",
                  "ENABLE_UNITY_COLLECTIONS_CHECKS", "ENABLE_BURST_AOT", "UNITY_INCLUDE_TESTS", "ENABLE_MONO",
                  "UNITY_TEAM_LICENSE", "ENABLE_CLOUD_SERVICES_COLLAB", "ENABLE_CLOUD_SERVICES_COLLAB_SOFTLOCKS",
                  "UNITY_PRO_LICENSE"]

PROJECT_SETTINGS_PLATFORM_KEYS = {"android": "Android", "ios": "iPhone", "standalone": "Standalone"}

# Warnings Unity itself suppresses / that are pure noise for reference-assembly unification.
NOWARN = ["CS1701", "CS1702", "CS1705", "CS8021"]

OBSOLETE_IDS = {"CS0618", "CS0619", "CS0612", "CS0672", "CS0809"}

# ----------------------------------------------------------------------------------------------
# helpers
# ----------------------------------------------------------------------------------------------

IS_TTY = sys.stdout.isatty() and os.environ.get("NO_COLOR") is None


def c(text, code):
    return f"\033[{code}m{text}\033[0m" if IS_TTY else text


def red(t): return c(t, "31;1")
def green(t): return c(t, "32;1")
def yellow(t): return c(t, "33;1")
def bold(t): return c(t, "1")
def dim(t): return c(t, "2")


def log(msg):
    print(msg, flush=True)


def run(cmd, cwd=None, check=True, capture=False, env=None, timeout=None):
    kw = dict(cwd=cwd, env=env, timeout=timeout, text=True)
    if capture:
        kw.update(stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    p = subprocess.run(cmd, **kw)
    if check and p.returncode != 0:
        out = p.stdout if capture else ""
        raise SystemExit(f"command failed ({p.returncode}): {' '.join(map(str, cmd))}\n{out or ''}")
    return p


def dotnet_exe():
    for cand in (os.environ.get("DOTNET"), shutil.which("dotnet"), str(Path.home() / ".dotnet" / "dotnet")):
        if cand and Path(cand).exists():
            return cand
    raise SystemExit("dotnet not found - run tools/unity-compile-check/setup.sh first")


def dotnet_env():
    env = dict(os.environ)
    env.setdefault("DOTNET_CLI_TELEMETRY_OPTOUT", "1")
    env.setdefault("DOTNET_NOLOGO", "1")
    env.setdefault("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1")
    env.setdefault("MSBUILDDISABLENODEREUSE", "1")
    # keep every NuGet package this toolchain restores inside the cache dir (reproducible, easy to wipe)
    env.setdefault("NUGET_PACKAGES", str(CACHE / "nuget-packages"))
    return env


@contextlib.contextmanager
def cache_lock(name: str, exclusive: bool, what: str):
    """Several agents may run setup/check concurrently on the same cache: serialise with flock."""
    CACHE.mkdir(parents=True, exist_ok=True)
    f = open(CACHE / f".{name}.lock", "a+")
    mode = fcntl.LOCK_EX if exclusive else fcntl.LOCK_SH
    try:
        fcntl.flock(f, mode | fcntl.LOCK_NB)
    except BlockingIOError:
        log(dim(f"  (waiting for another {what} using {CACHE} ...)"))
        fcntl.flock(f, mode)
    try:
        yield
    finally:
        fcntl.flock(f, fcntl.LOCK_UN)
        f.close()


def sha256(path: Path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def hidden(part: str):
    return part.startswith(".") or part.endswith("~") or part.lower() == "cvs"


# ----------------------------------------------------------------------------------------------
# Paths inside the cache
# ----------------------------------------------------------------------------------------------

class P:
    downloads = CACHE / "downloads"
    pkgs = CACHE / "pkgs"
    tooling = CACHE / "tooling"
    tool_dll = CACHE / "tooling" / "bin" / "UccTool" / "UccTool.dll"
    libs = CACHE / "libs" / "unity6"
    libs_player = CACHE / "libs" / "unity6" / "player"          # player build, Android (UNITY_ANDROID, platform "Android")
    libs_player_ios = CACHE / "libs" / "unity6" / "player-ios"  # player build, iOS (UNITY_IOS, platform "iOS")
    libs_editor = CACHE / "libs" / "unity6" / "editor"          # UNITY_EDITOR builds (only packages with "editor": True)
    testfw = CACHE / "libs" / "unity6" / "test-framework"       # UnityEngine.TestRunner builds + Unity's nunit (PlayMode stage)
    work = CACHE / "work"
    index = CACHE / "index"
    csref_index = CACHE / "index" / f"unity-{UNITY_VERSION}-api.tsv"
    refs_index = CACHE / "index" / "reference-assemblies-api.unity6.tsv"
    testproj = CACHE / "testrunner"
    stamp = CACHE / "setup.stamp.unity6.json"
    unityengine_dir = CACHE / "refs" / f"unityengine-{ENGINE_REF_VERSION}"
    # copy of com.unity.nuget.newtonsoft-json's Runtime/ DLLs, so `check` never needs the git clones
    newtonsoft_dir = CACHE / "refs" / f"newtonsoft-json-{GIT_SOURCES['com.unity.nuget.newtonsoft-json']['tag']}"
    NEWTONSOFT_DLLS = ("Newtonsoft.Json.dll", "AOT/Newtonsoft.Json.dll")

    @staticmethod
    def libs_dir(flavour: str):
        return {"android": P.libs_player, "ios": P.libs_player_ios, "editor": P.libs_editor}[flavour]

    @staticmethod
    def netstandard_refs():
        d = P.pkgs / "netstandard.library.ref.2.1.0" / "ref" / "netstandard2.1"
        return sorted(str(p) for p in d.glob("*.dll"))

    @staticmethod
    def unityengine_refs():
        return sorted(str(p) for p in P.unityengine_dir.glob("*.dll"))

    @staticmethod
    def unityeditor_refs():
        return [str(P.pkgs / "unity3d.sdk.2021.1.14.1" / "lib" / "UnityEditor.dll")]

    @staticmethod
    def nunit_ref():
        return [str(P.pkgs / "nunit.3.5.0" / "lib" / "net45" / "nunit.framework.dll")]

    @staticmethod
    def newtonsoft_ref(flavour: str = "android"):
        # com.unity.nuget.newtonsoft-json ships Runtime/AOT/Newtonsoft.Json.dll for players (editor excluded) and
        # Runtime/Newtonsoft.Json.dll for the editor. The cache copy (setup_newtonsoft) is preferred; the clone is the
        # fallback for caches set up before the copy existed.
        sub = P.NEWTONSOFT_DLLS[0] if flavour == "editor" else P.NEWTONSOFT_DLLS[1]
        for d in (P.newtonsoft_dir, GIT_SOURCES["com.unity.nuget.newtonsoft-json"]["dest"] / "Runtime"):
            if (d / sub).exists():
                return [str(d / sub)]
        return [str(P.newtonsoft_dir / sub)]


# ----------------------------------------------------------------------------------------------
# asmdef discovery (used both for packages and for the Unity project)
# ----------------------------------------------------------------------------------------------

def load_json_lenient(path: Path):
    txt = path.read_text(encoding="utf-8-sig")
    txt = re.sub(r"//[^\n]*", "", txt)             # Unity tolerates comments in asmdefs
    txt = re.sub(r",(\s*[}\]])", r"\1", txt)         # ... and trailing commas
    return json.loads(txt)


def read_meta_guid(path: Path):
    meta = Path(str(path) + ".meta")
    if meta.exists():
        m = re.search(r"^guid:\s*([0-9a-f]+)", meta.read_text(errors="replace"), re.M)
        if m:
            return m.group(1)
    return None


class AsmDef:
    def __init__(self, path: Path):
        self.path = path
        self.dir = path.parent
        self.data = load_json_lenient(path)
        self.name = self.data.get("name") or path.stem
        self.guid = read_meta_guid(path)
        self.extra_dirs: list[Path] = []   # folders added via .asmref

    def __repr__(self):
        return f"AsmDef({self.name})"


def find_assembly_roots(root: Path):
    """Return (asmdefs, asmref_dirs) under root, skipping hidden folders."""
    asmdefs, asmrefs = [], []
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = sorted(d for d in dirnames if not hidden(d))
        for f in sorted(filenames):
            if f.endswith(".asmdef"):
                asmdefs.append(AsmDef(Path(dirpath) / f))
            elif f.endswith(".asmref"):
                asmrefs.append(Path(dirpath) / f)
    return asmdefs, asmrefs


def resolve_asmrefs(asmdefs, asmrefs):
    by_name = {a.name: a for a in asmdefs}
    by_guid = {a.guid: a for a in asmdefs if a.guid}
    unresolved = []
    for r in asmrefs:
        try:
            ref = load_json_lenient(r).get("reference", "")
        except Exception:
            ref = ""
        target = by_guid.get(ref[5:]) if ref.startswith("GUID:") else by_name.get(ref)
        if target:
            target.extra_dirs.append(r.parent)
        else:
            unresolved.append(r)
    return unresolved


def collect_sources(start: Path, owner_dirs: set[Path], skip_editor=False, only_editor=False, inside_editor=False):
    """All .cs below `start`, not descending into folders owned by another asmdef/asmref."""
    out = []
    if not start.exists():
        return out
    for dirpath, dirnames, filenames in os.walk(start):
        d = Path(dirpath)
        keep = []
        for dn in sorted(dirnames):
            if hidden(dn):
                continue
            if (d / dn) in owner_dirs:
                continue
            keep.append(dn)
        dirnames[:] = keep
        rel_parts = d.relative_to(start).parts
        in_editor = inside_editor or any(p == "Editor" for p in rel_parts)
        if skip_editor and in_editor:
            continue
        if only_editor and not in_editor:
            continue
        for f in sorted(filenames):
            if f.endswith(".cs"):
                out.append(d / f)
    return out


def asmdef_sources(a: AsmDef, owner_dirs: set[Path]):
    srcs = []
    for d in [a.dir] + a.extra_dirs:
        srcs += collect_sources(d, owner_dirs - {d})
    return srcs


def parse_version(v: str):
    return [int(x) for x in re.findall(r"\d+", v)[:4]]


def version_matches(ver: str, expr: str):
    expr = (expr or "").strip()
    if not expr:
        return True
    v = parse_version(ver)
    if expr[0] in "[(":
        lo_inc, hi_inc = expr[0] == "[", expr[-1] == "]"
        inner = expr[1:-1]
        lo, _, hi = inner.partition(",")
        if "," not in inner:           # exact "[1.2.3]"
            return v == parse_version(lo)
        if lo.strip():
            lv = parse_version(lo)
            if v < lv or (v == lv and not lo_inc):
                return False
        if hi.strip():
            hv = parse_version(hi)
            if v > hv or (v == hv and not hi_inc):
                return False
        return True
    return v >= parse_version(expr)


def eval_define_constraint(expr: str, defines: set[str]):
    # Unity supports "A", "!A", and "A || B" in defineConstraints
    for alt in expr.split("||"):
        alt = alt.strip()
        if alt.startswith("!"):
            if alt[1:].strip() not in defines:
                return True
        elif alt in defines:
            return True
    return False


def platform_included(a: AsmDef, platform: str):
    inc = a.data.get("includePlatforms") or []
    exc = a.data.get("excludePlatforms") or []
    if inc:
        return platform in inc
    return platform not in exc


# ----------------------------------------------------------------------------------------------
# UccTool invocation
# ----------------------------------------------------------------------------------------------

def run_tool_plan(plan: dict, name: str):
    P.work.mkdir(parents=True, exist_ok=True)
    plan_path = P.work / f"{name}.{os.getpid()}.plan.json"
    res_path = P.work / f"{name}.{os.getpid()}.result.json"
    plan_path.write_text(json.dumps(plan, indent=1))
    if res_path.exists():
        res_path.unlink()
    p = subprocess.run([dotnet_exe(), str(P.tool_dll), "compile", str(plan_path), str(res_path)],
                       env=dotnet_env(), text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if p.returncode != 0 or not res_path.exists():
        sys.stderr.write(p.stdout + p.stderr)
        raise SystemExit(f"UccTool failed for plan {plan_path}")
    result = json.loads(res_path.read_text())
    # keep a copy of the latest plan/result per kind for debugging; per-process files are removed
    shutil.copyfile(plan_path, P.work / f"{name}.last.plan.json")
    shutil.copyfile(res_path, P.work / f"{name}.last.result.json")
    with contextlib.suppress(OSError):
        plan_path.unlink(); res_path.unlink()
    return result, p.stderr


def spec(name, sources, refs=(), comp_refs=(), defines=(), unsafe=False, out=None, audit=False, assembly_name=None, strip=False):
    return {"stripBodiesOnError": strip,
        "name": name, "assemblyName": assembly_name or name,
        "sources": [str(s) for s in sources], "references": list(dict.fromkeys(str(r) for r in refs)),
        "compilationReferences": list(comp_refs), "defines": sorted(set(defines)), "allowUnsafe": unsafe,
        "langVersion": "9.0", "outputPath": str(out) if out else None, "nowarn": NOWARN, "unity6Audit": audit,
    }


# ----------------------------------------------------------------------------------------------
# setup
# ----------------------------------------------------------------------------------------------

def download(url: str, dest: Path):
    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_suffix(dest.suffix + ".part")
    for attempt in range(3):
        p = subprocess.run(["curl", "-fsSL", "--retry", "3", "-o", str(tmp), url], text=True, capture_output=True)
        if p.returncode == 0:
            tmp.replace(dest)
            return
        time.sleep(2)
    raise SystemExit(f"download failed: {url}\n{p.stderr}")


def setup_nuget():
    for pid, (ver, digest) in NUGET.items():
        nupkg = P.downloads / f"{pid}.{ver}.nupkg"
        if not nupkg.exists():
            log(f"  downloading {pid} {ver}")
            download(f"https://api.nuget.org/v3-flatcontainer/{pid}/{ver}/{pid}.{ver}.nupkg", nupkg)
        if digest and sha256(nupkg) != digest:
            raise SystemExit(f"sha256 mismatch for {nupkg} (expected {digest}, got {sha256(nupkg)})")
        dest = P.pkgs / f"{pid}.{ver}"
        if not (dest / ".unpacked").exists():
            shutil.rmtree(dest, ignore_errors=True)
            with zipfile.ZipFile(nupkg) as z:
                z.extractall(dest)
            (dest / ".unpacked").write_text(ver)


def git_out(dest: Path, *args):
    p = subprocess.run(["git", "-C", str(dest), *args], capture_output=True, text=True)
    return p.stdout.strip() if p.returncode == 0 else None


def git_sparse_patterns(dest: Path):
    f = dest / ".git" / "info" / "sparse-checkout"
    return [l.strip() for l in f.read_text().splitlines() if l.strip()] if f.exists() else None


def git_partial_checkout(dest: Path, url: str, sparse: list, fetch_ref: list, checkout_ref: str, env: dict):
    """Shallow (depth 1), blob-less partial fetch of one ref + non-cone sparse checkout: git downloads the commit,
    its trees and only the blobs of the checked-out paths."""
    if not (dest / ".git").exists():
        shutil.rmtree(dest, ignore_errors=True)
        dest.mkdir(parents=True)
        run(["git", "init", "-q", str(dest)], env=env)
        run(["git", "-C", str(dest), "remote", "add", "origin", url], env=env)
    run(["git", "-C", str(dest), "config", "gc.auto", "0"], env=env)
    run(["git", "-C", str(dest), "config", "core.sparseCheckout", "true"], env=env)
    run(["git", "-C", str(dest), "config", "core.sparseCheckoutCone", "false"], env=env)
    (dest / ".git" / "info").mkdir(parents=True, exist_ok=True)
    (dest / ".git" / "info" / "sparse-checkout").write_text("\n".join(sparse) + "\n")
    run(["git", "-C", str(dest), "fetch", "-q", "--filter=blob:none", "--depth", "1", "origin", *fetch_ref], env=env, timeout=900)
    run(["git", "-C", str(dest), "-c", "advice.detachedHead=false", "checkout", "-q", "-f", checkout_ref], env=env, timeout=900)


SKIPPED_CLONES = []   # sources setup_git(needed=False) did not clone (see with_skipped_clones)


def setup_git(needed: bool = True):
    """Clone / update the pinned sources. needed=False (the cache already holds everything built from them): missing
    clones are not fetched at all - e.g. a CI run whose cache was restored - while existing ones are still switched
    to the pinned tag/commit as usual."""
    env = dict(os.environ, GIT_LFS_SKIP_SMUDGE="1", GIT_TERMINAL_PROMPT="0")
    for name, src in GIT_SOURCES.items():
        dest, url = src["dest"], src["url"]
        if not needed and not (dest / ".git").exists():
            log(dim(f"  {name}: no clone at {dest} - not needed, the cache is up to date"))
            SKIPPED_CLONES.append(name)
            continue
        if "tag" in src:
            tag = src["tag"]
            if (dest / ".git").exists():
                # existing clone (shared with other tools/agents): fetch the tag if needed and switch to it in place
                want = git_out(dest, "rev-parse", "-q", "--verify", f"refs/tags/{tag}^{{commit}}")
                if want is None:
                    log(f"  fetching {name} tag {tag}")
                    run(["git", "-C", str(dest), "fetch", "-q", "--depth", "1", "origin", "tag", tag], env=env, timeout=900)
                    want = git_out(dest, "rev-parse", "-q", "--verify", f"refs/tags/{tag}^{{commit}}")
                if want and git_out(dest, "rev-parse", "HEAD") == want:
                    continue
                if want:
                    log(f"  checking out {name} @ {tag}")
                    run(["git", "-C", str(dest), "-c", "advice.detachedHead=false", "checkout", "-q", "-f", f"refs/tags/{tag}"],
                        env=env, timeout=900)
                    continue
                log(f"  {dest}: cannot resolve tag {tag} - re-cloning")
            shutil.rmtree(dest, ignore_errors=True)
            if src.get("sparse"):
                log(f"  cloning {name} @ {tag} (shallow partial clone, sparse checkout)")
                git_partial_checkout(dest, url, src["sparse"], ["tag", tag], f"refs/tags/{tag}", env)
            else:
                log(f"  cloning {name} @ {tag}")
                dest.parent.mkdir(parents=True, exist_ok=True)
                run(["git", "-c", "advice.detachedHead=false", "clone", "-q", "--depth", "1", "--branch", tag, url, str(dest)],
                    env=env, timeout=900)
            continue
        # pinned commit: blob-less partial fetch of exactly that commit + sparse checkout of the needed paths only
        commit, sparse = src["commit"], src["sparse"]
        if dest.exists() and git_out(dest, "rev-parse", "HEAD") == commit and git_sparse_patterns(dest) == sparse:
            continue
        log(f"  fetching {name} @ {commit[:10]} (partial clone, sparse checkout of {len(sparse)} path pattern(s))")
        git_partial_checkout(dest, url, sparse, [commit], commit, env)


def with_skipped_clones(step):
    """Runs a setup step that may need clones; if it fails while setup_git skipped missing clones (cache judged up to
    date), fetches them and runs the step once more - so a cache that is only partly up to date still sets up."""
    try:
        return step()
    except SystemExit:
        if not SKIPPED_CLONES:
            raise
        log(yellow(f"  {step.__name__} needs sources that were not cloned - fetching them"))
        SKIPPED_CLONES.clear()
        setup_git(needed=True)
        return step()


def setup_newtonsoft():
    """Copy the Newtonsoft.Json DLLs out of the com.unity.nuget.newtonsoft-json clone into the cache (see P.newtonsoft_ref)."""
    src = GIT_SOURCES["com.unity.nuget.newtonsoft-json"]["dest"] / "Runtime"
    for sub in P.NEWTONSOFT_DLLS:
        s, d = src / sub, P.newtonsoft_dir / sub
        if not s.exists():
            if d.exists():
                continue    # clone skipped (cache up to date)
            raise SystemExit(f"setup: {s} not found")
        if not d.exists() or sha256(d) != sha256(s):
            d.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(s, d)


def setup_tool(force=False):
    src = TOOL_DIR / "src" / "UccTool"
    dst = P.tooling / "UccTool"
    digest = hashlib.sha256()
    for f in sorted(src.glob("*")):
        digest.update(f.name.encode()); digest.update(f.read_bytes())
    stamp = P.tooling / "UccTool.stamp"
    if not force and P.tool_dll.exists() and stamp.exists() and stamp.read_text() == digest.hexdigest():
        return
    log("  building UccTool (Roslyn host)")
    shutil.rmtree(dst, ignore_errors=True)
    shutil.copytree(src, dst)
    run([dotnet_exe(), "build", str(dst / "UccTool.csproj"), "-c", "Release", "-nologo", "-v", "q",
         "-o", str(P.tool_dll.parent)], env=dotnet_env(), capture=True)
    stamp.write_text(digest.hexdigest())


def setup_index(force=False):
    P.index.mkdir(parents=True, exist_ok=True)
    if force or not P.csref_index.exists():
        log(f"  indexing UnityCsReference {UNITY_VERSION}")
        run([dotnet_exe(), str(P.tool_dll), "index", str(CSREF_DIR), str(P.csref_index)], env=dotnet_env(), capture=True)


def setup_engine_refs(force=False):
    """Real UnityEngine player assemblies (not publicized, [Obsolete] attributes intact)."""
    marker = P.unityengine_dir / ".done"
    if not force and marker.exists():
        return
    z = URL_DIR / URL_ZIP[0]
    if sha256(z) != URL_ZIP[1]:
        raise SystemExit(f"sha256 mismatch for {z}")
    log(f"  unpacking {z}")
    shutil.rmtree(P.unityengine_dir, ignore_errors=True)
    with zipfile.ZipFile(z) as zf:
        zf.extractall(P.unityengine_dir)
    marker.write_text("ok")


# --- patches applied to package sources (copied into the cache first) -------------------------

PATCHES = {
    "com.unity.ugui": [
        # uGUI 2.0.0 members that InputSystem 1.12 (and game code) may use but that are missing from the newest
        # mirrored uGUI sources (1.0.0 @ 2021.1.17f1).
        ("Runtime/EventSystem/EventData/PointerEventData.cs",
         "        public bool dragging { get; set; }\n",
         "        public bool dragging { get; set; }\n\n"
         "        // ucc-patch: present in uGUI 2.0.0 / 2021.3+, missing from the 2021.1 mirror\n"
         "        public bool fullyExited { get; set; }\n"
         "        public bool reentered { get; set; }\n"
         "        public int displayIndex { get; set; }\n"),
        # CanvasRenderer.onRequestRebuild is an editor-only engine API that the (player) 2021.3 reference
        # assemblies do not contain. Only affects the UNITY_EDITOR build of UnityEngine.UI.
        ("Runtime/UI/Core/GraphicRebuildTracker.cs",
         "CanvasRenderer.onRequestRebuild += OnRebuildRequested;",
         "/* ucc-patch: CanvasRenderer.onRequestRebuild is editor-only and missing from the player engine assemblies */"),
        # Unity 6 UIToolkit: IPointerEvent gained tilt/penStatus, NavigationTabEvent was replaced by
        # NavigationMoveEvent(Direction.Next/Previous) - what uGUI 2.0.0 does.
        ("Runtime/EventSystem/UIElements/PanelEventHandler.cs",
         "            public float twist { get; private set; }\n",
         "            public float twist { get; private set; }\n"
         "            public Vector2 tilt { get; private set; }   // ucc-patch (Unity 6 IPointerEvent)\n"
         "            public PenStatus penStatus { get; private set; }   // ucc-patch (Unity 6 IPointerEvent)\n"),
        ("Runtime/EventSystem/UIElements/PanelEventHandler.cs",
         "using (var ev = NavigationTabEvent.GetPooled(direction))",
         "using (var ev = NavigationMoveEvent.GetPooled(direction > 0 ? NavigationMoveEvent.Direction.Next : NavigationMoveEvent.Direction.Previous))  // ucc-patch"),
    ],
    # SRP 17.3 vs. the 6000.0.58 engine base classes (declaration-level differences the body stripping cannot handle;
    # bodies that use missing engine APIs are stripped automatically, see PACKAGES "strip").
    "com.unity.render-pipelines": [
        # 6000.3 RenderPipelineAsset has `protected internal virtual bool requiresCompatibleRenderPipelineGlobalSettings`,
        # 6000.0 does not: keep URP's member, but not as an override.
        ("Packages/com.unity.render-pipelines.universal/Runtime/Data/UniversalRenderPipelineAsset.cs",
         "        protected override bool requiresCompatibleRenderPipelineGlobalSettings => true;",
         "        protected bool requiresCompatibleRenderPipelineGlobalSettings => true;   // ucc-patch: not virtual in 6000.0"),
        # 6000.0 RenderPipeline.Render(ScriptableRenderContext, Camera[]) is abstract (6000.3: virtual, empty) - URP 17.3
        # no longer overrides it.
        ("Packages/com.unity.render-pipelines.universal/Runtime/UniversalRenderPipeline.cs",
         "        protected override void Render(ScriptableRenderContext renderContext, List<Camera> cameras)",
         "        protected override void Render(ScriptableRenderContext renderContext, Camera[] cameras) { }   // ucc-patch: abstract in 6000.0\n\n"
         "        protected override void Render(ScriptableRenderContext renderContext, List<Camera> cameras)"),
    ],
}


def patched_copy(name: str, src: Path):
    dst = CACHE / "src-patched" / "unity6" / name
    shutil.rmtree(dst, ignore_errors=True)
    shutil.copytree(src, dst, ignore=shutil.ignore_patterns(".git", "Documentation~", "Samples~", "Tests"))
    for patch in PATCHES.get(name, []):
        rel, old, new = patch
        f = dst / rel
        if old is None:          # add a new file
            f.write_text(new, encoding="utf-8")
            continue
        txt = f.read_text(encoding="utf-8-sig")
        if old not in txt:
            raise SystemExit(f"patch for {name}/{rel} no longer applies (anchor not found)")
        f.write_text(txt.replace(old, new, 1), encoding="utf-8")
    return dst


# --- package builds -------------------------------------------------------------------------

GRAPHICS_ASSEMBLIES = ["Unity.RenderPipelines.Core.Runtime.Shared", "Unity.RenderPipelines.Core.Runtime",
                       "Unity.RenderPipelines.GPUDriven.Runtime", "Unity.RenderPipelines.Core.ShaderLibrary",
                       "Unity.RenderPipelines.Universal.Config.Runtime", "Unity.RenderPipeline.Universal.ShaderLibrary",
                       "Unity.RenderPipelines.Universal.Shaders", "Unity.RenderPipelines.ShaderGraph.ShaderGraphLibrary",
                       "Unity.RenderPipelines.Universal.Runtime", "Unity.RenderPipelines.Universal.2D.Runtime",
                       "Unity.UnifiedRayTracing.Runtime"]

# Package assemblies compiled from real sources, in dependency order of the packages. Keys: src (GIT_SOURCES
# name), sub (sub folder), only (asmdef names to build), skip, force/drop (defines), editor (also build a
# UNITY_EDITOR flavour; otherwise the editor stages use the Android player build).
# Every package is built per player platform ("android" -> libs/player, "ios" -> libs/player-ios) with that
# platform's defines and asmdef includePlatforms/excludePlatforms filtering, like Unity does per build target.
PACKAGES = [
    ("com.unity.ugui", {"src": "com.unity.ugui", "only": ["UnityEngine.UI", "UnityEditor.UI"], "editor": True}),
    ("com.unity.mathematics", {"src": "com.unity.mathematics", "only": ["Unity.Mathematics"]}),
    ("com.unity.burst", {"src": "com.unity.burst", "only": ["Unity.Burst"]}),
    ("com.unity.collections", {"src": "com.unity.collections", "only": ["Unity.Collections"]}),
    ("com.unity.inputsystem", {
        "src": "com.unity.inputsystem", "only": ["Unity.InputSystem"],
        "force": ["UNITY_INPUT_SYSTEM_PROJECT_WIDE_ACTIONS"],
        # these need uGUI 2.0.0 BaseInputModule / event-data members that the mirrored uGUI 1.0.0 lacks
        # (no public Input System API change)
        "drop": ["UNITY_INPUT_SYSTEM_INPUT_MODULE_SCROLL_DELTA", "UNITY_INPUT_SYSTEM_SENDPOINTERHOVERTOPARENT",
                 "UNITY_INPUT_SYSTEM_INPUT_MODULE_NAVIGATION_DEVICE_TYPE",
                 # 6000.3 native polling-frequency API (NativeInputSystem.GetPollingFrequency) is missing from the
                 # 6000.0.58 refs; the define only switches internal InputManager/NativeInputRuntime code
                 "UNITY_INPUT_SYSTEM_PLATFORM_POLLING_FREQUENCY"],
    }),
    ("com.unity.services.core", {"src": "com.unity.services.core",
                                 # all runtime asmdefs except Registration (needs the unpublished *.Releases asmdefs)
                                 "only": ["Unity.Services.Core", "Unity.Services.Core.Internal", "Unity.Services.Core.Environments",
                                          "Unity.Services.Core.Environments.Internal", "Unity.Services.Core.Configuration",
                                          "Unity.Services.Core.Device", "Unity.Services.Core.Threading",
                                          "Unity.Services.Core.Scheduler", "Unity.Services.Core.Telemetry",
                                          "Unity.Services.Core.Networking", "Unity.Services.Core.Analytics",
                                          "Unity.Services.Core.Components"]}),
    ("com.unity.purchasing", {"src": "com.unity.purchasing", "skip": ["IAPResolver"]}),
    # runtime asmdef includePlatforms ["Editor", "iOS"]: iOS player + editor stages see it, the Android stage does not
    ("com.unity.ads.ios-support", {"src": "com.unity.ads.ios-support", "only": ["Unity.Advertisement.IosSupport"], "editor": True}),
    # Mobile Notifications 2.5.0 runtime asmdefs: Android [Android, Editor], iOS [Editor, iOS], Unified [all three]
    # (not the Editor asmdef: it needs Unity 6 editor APIs missing from the 2021.1 UnityEditor.dll)
    ("com.unity.mobile.notifications", {"src": "com.unity.mobile.notifications", "editor": True,
                                        "only": ["Unity.Notifications.Android", "Unity.Notifications.iOS",
                                                 "Unity.Notifications.Unified"]}),
    # Google Play Games 2.2.1 runtime (includePlatforms ["Android", "Editor"]): the Android stage sees it, iOS does not
    ("com.google.play.games", {"src": "play-games-plugin-for-unity", "sub": "Assets/Public/GooglePlayGames/com.google.play.games",
                               "only": ["Google.Play.Games"]}),
    # nunit: Core's LensFlareDataSRP.cs has a (stray) `using NUnit.Framework;`
    # strip: SRP 17.3 targets the 6000.3 engine; bodies that use engine APIs missing from the 6000.0.58 player
    # assemblies are replaced by `throw null` (UccTool BodyStripper) - the public API surface stays the real one.
    ("com.unity.render-pipelines", {"src": "Graphics", "sub": "Packages", "only": GRAPHICS_ASSEMBLIES, "extra_refs": ["nunit"],
                                    "strip": True}),
]

STUB_ASSEMBLIES = [
    # (assembly name, folder under stubs/, stub dependencies) - built for both player platforms
    ("Unity.LevelPlay", "Unity.Services.LevelPlay", []),
]

FLAVOUR_PLATFORM = {"android": "Android", "ios": "iOS", "editor": "Editor"}   # asmdef platform names


def pkg_defines(flavour: str):
    d = list(UNITY_VERSION_DEFINES) + COMMON_DEFINES
    if flavour == "editor":
        d += EDITOR_DEFINES + PLATFORM_DEFINES["standalone"]
    else:
        d += PLATFORM_DEFINES[flavour]
    return d


def package_roots():
    """[(package name, cfg, root dir)] (patched copies where needed)."""
    out = []
    for pkg, cfg in PACKAGES:
        src = GIT_SOURCES[cfg["src"]]["dest"]
        root = patched_copy(pkg, src) if pkg in PATCHES else src
        if cfg.get("sub"):
            root = root / cfg["sub"]
        out.append((pkg, cfg, root))
    return out


def find_precompiled(roots):
    """Precompiled managed DLLs shipped inside the packages (e.g. Unity.Burst.Unsafe.dll) -> {stem: path}."""
    out = {}
    for _pkg, _cfg, root in roots:
        for dirpath, dirnames, filenames in os.walk(root):
            dirnames[:] = [d for d in dirnames if not hidden(d) and d not in ("Editor", "Tests", "Samples", "Plugins")]
            for f in filenames:
                if f.endswith(".dll"):
                    out.setdefault(f[:-4], Path(dirpath) / f)
    return out


def plan_package_libs(flavour: str, specs: list, built: dict, notes: list, roots, precompiled):
    """Append compile specs for all real package assemblies (one global asmdef graph across packages).
    flavour: "android" | "ios" | "editor". built: asm name -> spec name (for in-memory compilation references)."""
    editor = flavour == "editor"
    platform = FLAVOUR_PLATFORM[flavour]
    base_refs = P.netstandard_refs() + P.unityengine_refs() + P.newtonsoft_ref(flavour) + (P.unityeditor_refs() if editor else [])
    out_dir = P.libs_dir(flavour)

    all_defs, owner_dirs, cfg_of = [], set(), {}
    for pkg, cfg, root in roots:
        asmdefs, asmrefs = find_assembly_roots(root)
        asmdefs = [a for a in asmdefs if not any(part in ("Tests", "Samples~", "Documentation~") for part in a.dir.relative_to(root).parts)]
        resolve_asmrefs(asmdefs, asmrefs)
        owner_dirs |= {a.dir for a in asmdefs} | {r.parent for r in asmrefs}
        for a in asmdefs:
            cfg_of[a.name] = cfg
        all_defs += asmdefs
    by_name = {a.name: a for a in all_defs}
    by_guid = {a.guid: a.name for a in all_defs if a.guid}

    def wanted(a):
        cfg = cfg_of[a.name]
        if editor and not cfg.get("editor"):
            return False
        return (cfg.get("only") is None or a.name in cfg["only"]) and a.name not in cfg.get("skip", [])

    def ref_names(a):
        out = []
        for r in a.data.get("references", []) or []:
            n = by_guid.get(r[5:], r) if r.startswith("GUID:") else r
            out.append(ASMDEF_ALIASES.get(n, n))
        return out

    order, visiting = [], set()

    def visit(a):
        if a.name in visiting:
            return
        visiting.add(a.name)
        for n in ref_names(a):
            if n in by_name and wanted(by_name[n]):
                visit(by_name[n])
        order.append(a)

    for a in all_defs:
        if wanted(a):
            visit(a)

    for a in order:
        cfg = cfg_of[a.name]
        if not platform_included(a, platform):
            continue
        defines = set(pkg_defines(flavour))
        for vd in a.data.get("versionDefines", []) or []:
            name = vd.get("name")
            ver = UNITY_VERSION_NUM if name == "Unity" else PACKAGE_VERSIONS.get(name)
            if ver is not None and version_matches(ver, vd.get("expression", "")):
                defines.add(vd["define"])
        defines |= set(cfg.get("force", []))
        defines -= set(cfg.get("drop", []))
        constraints = a.data.get("defineConstraints", []) or []
        if not all(eval_define_constraint(x, defines) for x in constraints):
            notes.append(f"{flavour}: skipped {a.name} (defineConstraints {constraints} not met)")
            continue
        refs = list(base_refs) if not a.data.get("noEngineReferences") else P.netstandard_refs()
        if "nunit" in cfg.get("extra_refs", []):
            refs += P.nunit_ref()
        # precompiled DLLs: all of them unless overrideReferences, then only the listed ones
        if a.data.get("overrideReferences"):
            refs += [str(precompiled[x[:-4]]) for x in a.data.get("precompiledReferences") or [] if x[:-4] in precompiled]
        else:
            refs += [str(x) for x in precompiled.values()]
        comp_refs = []
        # 6000.1+ engine types missing from the 6000.0.58 player assemblies (unity6-shims) - see plan_shim_lib
        if "UnityEngine.Unity6Shims" in built:
            comp_refs.append(built["UnityEngine.Unity6Shims"])
        elif (P.libs_player / "UnityEngine.Unity6Shims.dll").exists():
            refs.append(str(P.libs_player / "UnityEngine.Unity6Shims.dll"))
        # Unity implicitly references UnityEngine.UI (+ UnityEditor.UI in the editor) from package asmdefs,
        # e.g. IAP's FakeStore UI uses uGUI without listing it in its asmdef.
        if a.name != "UnityEngine.UI" and "UnityEngine.UI" in built and "UnityEngine.UI" not in ref_names(a):
            comp_refs.append(built["UnityEngine.UI"])
        for n in ref_names(a):
            if n in built:
                comp_refs.append(built[n])
            elif n in by_name:
                pass  # excluded for this platform / flavour
            elif n in precompiled:
                refs.append(str(precompiled[n]))
            else:
                dll = P.libs_player / f"{n}.dll"   # editor flavour: fall back to the (Android) player build
                if editor and dll.exists():
                    refs.append(str(dll))
        sources = asmdef_sources(a, owner_dirs)
        sname = f"{flavour}:{a.name}"
        specs.append(spec(sname, sources, refs=refs, comp_refs=comp_refs, defines=defines,
                          unsafe=bool(a.data.get("allowUnsafeCode")), out=out_dir / f"{a.name}.dll", assembly_name=a.name,
                          strip=bool(cfg.get("strip"))))
        built[a.name] = sname


def plan_shim_lib(flavour, specs, built):
    """UnityEngine.Unity6Shims: engine types missing from the player assemblies (built first; packages reference it)."""
    base_refs = P.netstandard_refs() + P.unityengine_refs()
    shim_sources = sorted((TOOL_DIR / "unity6-shims" / "common").rglob("*.cs"))
    specs.append(spec(f"{flavour}:UnityEngine.Unity6Shims", shim_sources, refs=base_refs, defines=pkg_defines(flavour),
                      out=P.libs_dir(flavour) / "UnityEngine.Unity6Shims.dll", assembly_name="UnityEngine.Unity6Shims",
                      unsafe=True))
    built["UnityEngine.Unity6Shims"] = f"{flavour}:UnityEngine.Unity6Shims"


def plan_stub_libs(flavour, specs, built):
    base_refs = P.netstandard_refs() + P.unityengine_refs()
    out_dir = P.libs_dir(flavour)
    for asm, folder, deps in STUB_ASSEMBLIES:
        sources = sorted((TOOL_DIR / "stubs" / folder).rglob("*.cs"))
        sname = f"{flavour}:{asm}"
        specs.append(spec(sname, sources, refs=base_refs, comp_refs=[built[d] for d in deps],
                          defines=pkg_defines(flavour), out=out_dir / f"{asm}.dll", assembly_name=asm))
        built[asm] = sname


def setup_libs():
    shutil.rmtree(P.libs, ignore_errors=True)
    for flavour in ("android", "ios", "editor"):
        P.libs_dir(flavour).mkdir(parents=True)
    notes = []
    roots = package_roots()
    precompiled = find_precompiled(roots)
    for name, path in precompiled.items():   # auto-referenced by Assembly-CSharp too, like in Unity
        for flavour in ("android", "ios"):
            shutil.copyfile(path, P.libs_dir(flavour) / f"{name}.dll")
    for src_name, sub, dlls in PRECOMPILED_PACKAGES:   # SDKs shipped as DLLs (e.g. Sentry)
        src_dir = GIT_SOURCES[src_name]["dest"] / sub
        for dll in dlls:
            if not (src_dir / dll).exists():
                raise SystemExit(red(f"setup: {src_dir / dll} is missing (sparse checkout of {src_name})"))
            for flavour in ("android", "ios"):
                shutil.copyfile(src_dir / dll, P.libs_dir(flavour) / dll)
    for flavour in ("android", "ios", "editor"):
        specs, built = [], {}
        if flavour != "editor":
            plan_shim_lib(flavour, specs, built)
        plan_package_libs(flavour, specs, built, notes, roots, precompiled)
        if flavour != "editor":
            plan_stub_libs(flavour, specs, built)
        log(f"  compiling {len(specs)} {flavour} package assemblies ({ENGINE_LABEL})")
        results, _ = run_tool_plan({"compilations": specs}, f"libs-{flavour}")
        failed = [r for r in results if not r["success"]]
        stripped = []
        for r in results:
            status = green("ok") if r["success"] else red("FAILED")
            extra = f", {len(r['stripped'])} bodies stripped" if r.get("stripped") else ""
            log(f"    {r['name']:<66} {status} {dim(str(r['sourceCount']) + ' files, ' + str(r['milliseconds']) + ' ms' + extra)}")
            stripped += [f"{r['name']}: {x}" for x in r.get("stripped") or []]
        if stripped:
            (P.libs / f"stripped-bodies.{flavour}.txt").write_text("\n".join(stripped) + "\n")
            log(dim(f"    ({len(stripped)} member bodies use engine APIs missing from the {ENGINE_REF_VERSION} refs and were "
                    f"replaced by `throw null`; list: {P.libs / f'stripped-bodies.{flavour}.txt'})"))
        if failed:
            for r in failed:
                for d in r["diagnostics"]:
                    if d["severity"] == "error":
                        log(f"      {d.get('file')}({d.get('line')},{d.get('col')}): error {d['id']}: {d['message']}")
                if r.get("fatal"):
                    log("      " + r["fatal"])
            raise SystemExit(red(f"setup: {len(failed)} {flavour} package assemblies failed to build"))
    for n in notes:
        log(dim("  note: " + n))
    # Public API dump of everything we compile against (used by audit --lookup)
    dlls = P.unityengine_refs() + P.unityeditor_refs() + sorted(str(p) for p in P.libs_player.glob("*.dll")) + \
        sorted(str(p) for p in P.libs_player_ios.glob("*.dll") if not (P.libs_player / p.name).exists()) + \
        sorted(str(p) for p in P.libs_editor.glob("UnityEditor*.dll"))
    run([dotnet_exe(), str(P.tool_dll), "dump-refs", str(P.refs_index)] + dlls, env=dotnet_env(), capture=True)
    # unity6-shims may only add NEW types: fail loudly if a shim duplicates a type of the reference assemblies
    engine_types, shim_types = set(), set()
    for line in P.refs_index.read_text(encoding="utf-8").splitlines():
        c = line.split("\t")
        if len(c) >= 4 and c[1] in ("T", "FWD"):
            full = f"{c[2]}.{c[3]}" if c[2] else c[3]
            (shim_types if c[0] == "UnityEngine.Unity6Shims" else engine_types).add(full)
    clash = sorted(shim_types & engine_types)
    if clash:
        raise SystemExit(red("setup: unity6-shims define types that already exist in the reference assemblies "
                             "(would cause ambiguity errors): " + ", ".join(clash)))


def setup_testrunner():
    d = P.testproj
    (d / "core").mkdir(parents=True, exist_ok=True)
    (d / "tests").mkdir(parents=True, exist_ok=True)
    common = """    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>
    <SatelliteResourceLanguages>en</SatelliteResourceLanguages>
    <NoWarn>$(NoWarn);CS1701;CS1702;NU1603;NU1701</NoWarn>
    <RootNamespace></RootNamespace>"""
    (d / "core" / "Core.csproj").write_text(f"""<Project Sdk="Microsoft.NET.Sdk">
  <!-- generated by tools/unity-compile-check (ucc.py); Compile items come from Sources.props -->
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <AssemblyName>BeamRunner.Core</AssemblyName>
{common}
  </PropertyGroup>
  <Import Project="Sources.props" Condition="Exists('Sources.props')" />
</Project>
""")
    pkgs = "\n".join(f'    <PackageReference Include="{k}" Version="{v}" />' for k, v in TEST_PACKAGES.items())
    (d / "tests" / "Tests.csproj").write_text(f"""<Project Sdk="Microsoft.NET.Sdk">
  <!-- generated by tools/unity-compile-check (ucc.py); Compile items come from Sources.props -->
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>BeamRunner.Core.Tests</AssemblyName>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <DefineConstants>$(DefineConstants);UNITY_INCLUDE_TESTS;UCC_DOTNET_TEST</DefineConstants>
{common}
  </PropertyGroup>
  <ItemGroup>
{pkgs}
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../core/Core.csproj" />
  </ItemGroup>
  <Import Project="Sources.props" Condition="Exists('Sources.props')" />
</Project>
""")
    for sub in ("core", "tests"):
        sp = d / sub / "Sources.props"
        if not sp.exists():
            sp.write_text("<Project />\n")
    log("  restoring test runner packages (NUnit, NUnit3TestAdapter, Microsoft.NET.Test.Sdk)")
    run([dotnet_exe(), "restore", str(d / "tests" / "Tests.csproj"), "-nologo", "-v", "q"], env=dotnet_env(), capture=True)
    # warm-up build so the first check is fast
    run([dotnet_exe(), "build", str(d / "tests" / "Tests.csproj"), "--no-restore", "-nologo", "-v", "q"],
        env=dotnet_env(), capture=True, check=False)


# --- Unity Test Framework (for the PlayMode smoke-test stage) ---------------------------------

# UnityEngine.TestRunner flavours: the Editor build (what the Test Runner window compiles the tests against) and an
# Android player build (tests built into a development player).
TEST_FRAMEWORK_FLAVOURS = ("editor", "android")


def test_framework_nunit_clone():
    return GIT_SOURCES["com.unity.ext.nunit"]["dest"] / "net40" / "unity-custom" / "nunit.framework.dll"


def plan_test_framework(flavour: str, nunit: str, out_dir: Path = None):
    """Compile spec for com.unity.test-framework's UnityEngine.TestRunner asmdef (overrideReferences: engine + Unity's
    nunit.framework.dll; versionDefines -> UNITY_TESTS_FRAMEWORK) for "editor" (+ UnityEditor) or "android"."""
    root = GIT_SOURCES["com.unity.test-framework"]["dest"] / "UnityEngine.TestRunner"
    asmdefs, asmrefs = find_assembly_roots(root)
    resolve_asmrefs(asmdefs, asmrefs)
    a = next(x for x in asmdefs if x.name == "UnityEngine.TestRunner")
    owner_dirs = {x.dir for x in asmdefs} | {r.parent for r in asmrefs}
    defines = set(pkg_defines(flavour))
    for vd in a.data.get("versionDefines", []) or []:
        ver = UNITY_VERSION_NUM if vd.get("name") == "Unity" else PACKAGE_VERSIONS.get(vd.get("name"))
        if ver is not None and version_matches(ver, vd.get("expression", "")):
            defines.add(vd["define"])
    refs = P.netstandard_refs() + P.unityengine_refs() + [nunit] + (P.unityeditor_refs() if flavour == "editor" else [])
    return spec(f"{flavour}:UnityEngine.TestRunner", asmdef_sources(a, owner_dirs), refs=refs, defines=defines,
                out=(out_dir / "UnityEngine.TestRunner.dll") if out_dir else None, assembly_name="UnityEngine.TestRunner")


def setup_test_framework():
    """Builds UnityEngine.TestRunner (editor + Android player) into the cache and copies Unity's nunit.framework.dll
    next to it, so the check's PlayMode stage never needs the clones. Rebuilt when its inputs change (P.testfw lives
    under P.libs, so a libs rebuild also rebuilds it)."""
    stamp_file = P.testfw / "stamp.json"
    inputs = {"recipe": RECIPE_VERSION, "engine": ENGINE_REF_VERSION, "tool": (P.tooling / "UccTool.stamp").read_text(),
              "test-framework": GIT_SOURCES["com.unity.test-framework"]["tag"],
              "nunit": GIT_SOURCES["com.unity.ext.nunit"]["tag"], "flavours": list(TEST_FRAMEWORK_FLAVOURS)}
    built = [P.testfw / "nunit.framework.dll"] + [P.testfw / f / "UnityEngine.TestRunner.dll" for f in TEST_FRAMEWORK_FLAVOURS]
    if stamp_file.exists() and json.loads(stamp_file.read_text()) == inputs and all(x.exists() for x in built):
        log("  UnityEngine.TestRunner up to date")
        return
    src = GIT_SOURCES["com.unity.test-framework"]["dest"] / "UnityEngine.TestRunner"
    nunit_src = test_framework_nunit_clone()
    if not src.is_dir() or not nunit_src.exists():
        raise SystemExit(f"setup: {src} or {nunit_src} not found (clones of com.unity.test-framework / com.unity.ext.nunit)")
    shutil.rmtree(P.testfw, ignore_errors=True)
    P.testfw.mkdir(parents=True)
    shutil.copyfile(nunit_src, P.testfw / "nunit.framework.dll")
    specs = [plan_test_framework(f, str(P.testfw / "nunit.framework.dll"), P.testfw / f) for f in TEST_FRAMEWORK_FLAVOURS]
    log(f"  compiling UnityEngine.TestRunner ({', '.join(TEST_FRAMEWORK_FLAVOURS)})")
    results, _ = run_tool_plan({"compilations": specs}, "libs-test-framework")
    failed = [r for r in results if not r["success"]]
    for r in results:
        log(f"    {r['name']:<66} {green('ok') if r['success'] else red('FAILED')} {dim(str(r['sourceCount']) + ' files, ' + str(r['milliseconds']) + ' ms')}")
    if failed:
        for r in failed:
            for d in r["diagnostics"]:
                if d["severity"] == "error":
                    log(f"      {d.get('file')}({d.get('line')},{d.get('col')}): error {d['id']}: {d['message']}")
        raise SystemExit(red("setup: UnityEngine.TestRunner failed to build"))
    stamp_file.write_text(json.dumps(inputs, indent=1))


def test_framework_for_check():
    """({flavour: (refs, comp_refs)}, extra in-memory specs, error or None) for the PlayMode stage: the cache build from
    setup, else (cache set up before this stage existed) UnityEngine.TestRunner compiled in memory from the clone."""
    nunit = P.testfw / "nunit.framework.dll"
    if not nunit.exists():
        nunit = test_framework_nunit_clone()
    src = GIT_SOURCES["com.unity.test-framework"]["dest"] / "UnityEngine.TestRunner"
    out, extra = {}, []
    for flavour in TEST_FRAMEWORK_FLAVOURS:
        dll = P.testfw / flavour / "UnityEngine.TestRunner.dll"
        if dll.exists() and nunit.exists():
            out[flavour] = ([str(nunit), str(dll)], [])
        elif src.is_dir() and nunit.exists():
            s = plan_test_framework(flavour, str(nunit))
            s["name"] = f"UnityEngine.TestRunner [{flavour}, from source]"
            extra.append(s)
            out[flavour] = ([str(nunit)], [s["name"]])
        else:
            return {}, [], "UnityEngine.TestRunner / Unity's nunit.framework.dll are not in this cache - run tools/unity-compile-check/setup.sh"
    return out, extra, None


def cmd_setup(args):
    with cache_lock("cache", exclusive=True, what="setup/check"):
        return _cmd_setup(args)


def _cmd_setup(args):
    t0 = time.time()
    if args.legacy_refs:
        log(yellow("[setup] --legacy-refs was dropped with the Unity 6.3 upgrade - ignoring it"))
    CACHE.mkdir(parents=True, exist_ok=True)
    log(bold(f"[setup] cache: {CACHE}"))
    # remove cache entries of older layouts (pre-refset 2021.3 layout, the dropped --legacy-refs set, 6000.0 refs/index)
    stale_entries = [CACHE / "libs" / "player", CACHE / "libs" / "editor", CACHE / "setup.stamp.json",
                     CACHE / "index" / "reference-assemblies-api.tsv", CACHE / "libs" / "legacy", CACHE / "setup.stamp.legacy.json",
                     CACHE / "index" / "reference-assemblies-api.legacy.tsv", CACHE / "src-patched" / "legacy",
                     CACHE / "refs" / "unityengine-2021.3.33-depublicized", CACHE / "pkgs" / "unityengine.modules.2021.3.33",
                     CACHE / "downloads" / "unityengine.modules.2021.3.33.nupkg",
                     CACHE / "pkgs" / "newtonsoft.json.13.0.2", CACHE / "downloads" / "newtonsoft.json.13.0.2.nupkg",
                     *[d for d in (CACHE / "src-patched").glob("com.*")],
                     *[d for d in (CACHE / "refs").glob("unityengine-6*") if d != P.unityengine_dir],
                     *[f for f in P.index.glob("unity-*-api.tsv") if f != P.csref_index]]
    for stale in stale_entries:
        if stale.is_dir():
            shutil.rmtree(stale, ignore_errors=True)
        elif stale.exists():
            stale.unlink()
    log(bold(f"[setup] engine reference set: {ENGINE_LABEL}"))
    log("[setup] NuGet packages"); setup_nuget()
    log("[setup] UccTool"); setup_tool(force=args.force)
    inputs = {
        "recipe": RECIPE_VERSION, "unity": UNITY_VERSION, "engine": URL_ZIP, "nuget": NUGET,
        "git": {k: v.get("tag") or v.get("commit") for k, v in GIT_SOURCES.items()},
        "tool": (P.tooling / "UccTool.stamp").read_text(),
        "stubs": hashlib.sha256(b"".join(p.read_bytes() for p in sorted((TOOL_DIR / "stubs").rglob("*.cs")) +
                                         sorted((TOOL_DIR / "unity6-shims").rglob("*.cs")))).hexdigest(),
        "patches": repr(PATCHES) + repr(PACKAGES) + repr(STUB_ASSEMBLIES) + repr(PRECOMPILED_PACKAGES),
        "defines": hashlib.sha256(repr((UNITY_VERSION_DEFINES, COMMON_DEFINES, PLATFORM_DEFINES, EDITOR_DEFINES,
                                        PACKAGE_VERSIONS)).encode()).hexdigest(),
    }
    stamp = json.loads(P.stamp.read_text()) if P.stamp.exists() else {}
    libs_ok = not args.force and stamp.get("inputs") == json.loads(json.dumps(inputs, default=str)) and \
        (P.libs_player / "UnityEngine.UI.dll").exists() and (P.libs_player_ios / "UnityEngine.UI.dll").exists()
    # Everything `check` needs from the clones is in the cache (e.g. a restored CI cache): missing clones are skipped.
    cache_complete = libs_ok and P.csref_index.exists() and (P.unityengine_dir / ".done").exists() and \
        all((P.newtonsoft_dir / sub).exists() for sub in P.NEWTONSOFT_DLLS)
    log("[setup] package / reference sources (git)"); setup_git(needed=not cache_complete)
    log(f"[setup] UnityCsReference {UNITY_VERSION} API index"); setup_index(force=args.force)
    log("[setup] UnityEngine reference assemblies"); setup_engine_refs(force=args.force)
    log("[setup] Newtonsoft.Json reference assemblies"); with_skipped_clones(setup_newtonsoft)
    if not libs_ok:
        log("[setup] building package / stub assemblies"); setup_libs()
    else:
        log("[setup] package / stub assemblies up to date")
    log("[setup] Unity Test Framework (PlayMode test stage)"); with_skipped_clones(setup_test_framework)
    P.stamp.write_text(json.dumps({"inputs": inputs, "time": time.strftime("%Y-%m-%d %H:%M:%S")}, indent=1, default=str))
    log("[setup] test runner project"); setup_testrunner()
    log(green(f"[setup] done in {time.time() - t0:.1f}s"))


# ----------------------------------------------------------------------------------------------
# check
# ----------------------------------------------------------------------------------------------

class ProjectLayout:
    def __init__(self, project: Path):
        self.project = project
        self.assets = project / "Assets"
        asmdefs, asmrefs = find_assembly_roots(self.assets)
        resolve_asmrefs(asmdefs, asmrefs)
        self.asmdefs = asmdefs
        self.owner_dirs = {a.dir for a in asmdefs} | {r.parent for r in asmrefs}
        self.core_dir = self.assets / "BeamRunner" / "Scripts" / "Core"
        self.tests_dir = self.assets / "BeamRunner" / "Tests" / "EditMode"
        self.playmode_dir = self.assets / "BeamRunner" / "Tests" / "PlayMode"
        # Core / tests: all .cs under the folder (including nested asmdef-less folders)
        self.core_sources = sorted(p for p in self.core_dir.rglob("*.cs") if not any(hidden(x) for x in p.relative_to(self.assets).parts)) if self.core_dir.exists() else []
        self.test_sources = sorted(p for p in self.tests_dir.rglob("*.cs") if not any(hidden(x) for x in p.relative_to(self.assets).parts)) if self.tests_dir.exists() else []
        # Predefined assemblies
        owned = set(self.owner_dirs)
        self.runtime_sources = [p for p in collect_sources(self.assets, owned, skip_editor=True) if not self._in_tests(p)]
        self.editor_sources = [p for p in collect_sources(self.assets, owned, only_editor=True) if not self._in_tests(p)]
        # asmdefs other than Core/Tests (reported, not compiled)
        self.other_asmdefs = [a for a in asmdefs if a.dir not in (self.core_dir, self.tests_dir, self.playmode_dir)]
        self.core_asmdef = next((a for a in asmdefs if a.dir == self.core_dir), None)
        self.tests_asmdef = next((a for a in asmdefs if a.dir == self.tests_dir), None)
        # PlayMode smoke tests (their own asmdef; everything below the folder)
        self.playmode_asmdef = next((a for a in asmdefs if a.dir == self.playmode_dir), None)
        self.playmode_sources = asmdef_sources(self.playmode_asmdef, self.owner_dirs) if self.playmode_asmdef else []

    def _in_tests(self, p: Path):
        rel = p.relative_to(self.assets).parts
        return len(rel) >= 2 and rel[0] == "BeamRunner" and rel[1] == "Tests"

    def extra_defines(self, platform: str):
        """Assets/csc.rsp -define: entries + ProjectSettings scriptingDefineSymbols for the platform."""
        out = []
        rsp = self.assets / "csc.rsp"
        if rsp.exists():
            for m in re.finditer(r"(?:-|/)d(?:efine)?:(\S+)", rsp.read_text(errors="replace")):
                out += [x for x in re.split(r"[;,]", m.group(1)) if x]
        ps = self.project / "ProjectSettings" / "ProjectSettings.asset"
        if ps.exists():
            txt = ps.read_text(errors="replace")
            m = re.search(r"^  scriptingDefineSymbols:\s*\n((?:    \S.*\n)*)", txt, re.M)
            if m:
                key = PROJECT_SETTINGS_PLATFORM_KEYS.get(platform)
                for line in m.group(1).splitlines():
                    k, _, v = line.strip().partition(":")
                    if k == key:
                        out += [x for x in v.strip().split(";") if x]
        # UNITY_ANALYTICS is only defined when Analytics is enabled in the Services settings
        ucs = self.project / "ProjectSettings" / "UnityConnectSettings.asset"
        if ucs.exists():
            m = re.search(r"^  UnityAnalyticsSettings:\s*\n    m_Enabled:\s*1", ucs.read_text(errors="replace"), re.M)
            if m:
                out.append("UNITY_ANALYTICS")
        return out

    def player_setting(self, key: str, default=None):
        ps = self.project / "ProjectSettings" / "ProjectSettings.asset"
        if ps.exists():
            m = re.search(rf"^  {re.escape(key)}:\s*(\S+)\s*$", ps.read_text(errors="replace"), re.M)
            if m:
                return m.group(1)
        return default

    def csc_rsp_nowarn(self):
        rsp = self.assets / "csc.rsp"
        out = []
        if rsp.exists():
            for m in re.finditer(r"(?:-|/)nowarn:(\S+)", rsp.read_text(errors="replace")):
                out += [x for x in re.split(r"[;,]", m.group(1)) if x]
        return out


def check_defines(target: str, extra: list[str]):
    d = list(UNITY_VERSION_DEFINES) + COMMON_DEFINES
    if target == "editor":
        d += EDITOR_DEFINES + PLATFORM_DEFINES["standalone"]
    else:
        d += PLATFORM_DEFINES[target]
    return sorted(set(d + extra))


class Unity6Lookup:
    """Loads the UnityCsReference (UNITY_VERSION) index to classify 'missing API' errors as Unity-6-only."""

    def __init__(self, path: Path):
        self.types = set()
        self.runtime_types = set()      # declared in at least one non-editor source file (and not in UnityEditor.*)
        self.namespaces = set()
        self.runtime_namespaces = set()
        self.members = {}
        if not path.exists():
            return
        with open(path, encoding="utf-8") as f:
            for line in f:
                if line.startswith("#"):
                    continue
                cols = line.rstrip("\n").split("\t")
                if len(cols) < 9:
                    continue
                kind, ns, typ, member, _ar, access, obs, err, msg = cols[:9]
                src_file = cols[9] if len(cols) > 9 else ""
                editor_file = ns.startswith("UnityEditor") or src_file.startswith("Editor/") or "/Editor/" in src_file
                full = f"{ns}.{typ}" if ns else typ
                if kind == "T":
                    if access != "partial":
                        self.types.add(full)
                        if not editor_file:
                            self.runtime_types.add(full)
                        parts = ns.split(".") if ns else []
                        for i in range(1, len(parts) + 1):
                            self.namespaces.add(".".join(parts[:i]))
                            if not editor_file:
                                self.runtime_namespaces.add(".".join(parts[:i]))
                    continue
                self.members.setdefault(full, {}).setdefault(member, []).append((obs == "1", err == "1", msg, editor_file))

    def member(self, types: list[str], member: str, editor: bool):
        for t in types or []:
            rows = self.members.get(t, {}).get(member)
            if not editor:
                rows = [r for r in rows or [] if not r[3]]
            if rows and t in (self.types if editor else self.runtime_types):
                return t, rows
        return None

    def type(self, name: str, namespaces: list[str], editor: bool):
        types = self.types if editor else self.runtime_types
        nss = self.namespaces if editor else self.runtime_namespaces
        for ns in namespaces or [""]:
            full = f"{ns}.{name}" if ns else name
            if full in types:
                return "type " + full
        for ns in namespaces or [""]:
            full = f"{ns}.{name}" if ns else name
            if full in nss and full.startswith("Unity") and full not in ("UnityEngine", "UnityEditor"):
                return "namespace " + full
        return None


def rel(path: str, base: Path):
    if not path:
        return "<no location>"
    try:
        return os.path.relpath(path, base)
    except ValueError:
        return path


def fmt_diag(d, base):
    return f"{rel(d.get('file'), base)}({d.get('line')},{d.get('col')}): {d['severity']} {d['id']}: {d['message']}"


def classify_unity6(diags, lookup: Unity6Lookup, mode: str):
    """Split errors into (real errors, Unity-6-only errors verified against the UNITY_VERSION index).
    mode: "none" (pure C# stages: nothing is Unity-6-only), "player" (UnityEngine runtime APIs only), "editor"."""
    real, u6 = [], []
    for d in diags:
        if d["severity"] != "error":
            continue
        hit = None
        if mode == "none":
            pass
        elif d.get("missingMember"):
            m = lookup.member(d.get("receiverTypes"), d["missingMember"], editor=mode == "editor")
            if m:
                t, rows = m
                if not all(r[0] and r[1] for r in rows):
                    obs = [r[2] for r in rows if r[0]]
                    hit = f"{t}.{d['missingMember']} exists in Unity {UNITY_VERSION}" + (f" (but [Obsolete]: {obs[0]})" if obs and len(obs) == len(rows) else "")
        elif d.get("missingType"):
            t = lookup.type(d["missingType"], d.get("candidateNamespaces"), editor=mode == "editor")
            if t:
                hit = f"{t} exists in Unity {UNITY_VERSION}"
        if hit:
            d["unity6"] = hit
            u6.append(d)
        else:
            real.append(d)
    return real, u6


def write_sources_props(path: Path, sources: list[Path]):
    items = "\n".join(f'    <Compile Include="{s}" />' for s in sources)
    path.write_text(f"<Project>\n  <ItemGroup>\n{items}\n  </ItemGroup>\n</Project>\n")


def run_tests(core_sources, test_sources, base: Path):
    d = P.testproj
    if not (d / "tests" / "Tests.csproj").exists():
        return False, "test runner project missing - run setup.sh", 0.0
    write_sources_props(d / "core" / "Sources.props", core_sources)
    write_sources_props(d / "tests" / "Sources.props", test_sources)
    trx_dir = d / "results"
    shutil.rmtree(trx_dir, ignore_errors=True)
    t0 = time.time()
    p = subprocess.run([dotnet_exe(), "test", str(d / "tests" / "Tests.csproj"), "--no-restore", "-nologo",
                        "-v", "q", "--logger", "trx;LogFileName=results.trx", "--results-directory", str(trx_dir)],
                       env=dotnet_env(), text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    dt = time.time() - t0
    trx = trx_dir / "results.trx"
    lines = []
    ok = p.returncode == 0
    if trx.exists():
        ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
        root = ET.parse(trx).getroot()
        counters = root.find(".//t:ResultSummary/t:Counters", ns)
        if counters is not None:
            a = counters.attrib
            lines.append(f"tests: total {a.get('total')}, passed {a.get('passed')}, failed {a.get('failed')}, "
                         f"skipped/not executed {int(a.get('notExecuted', 0)) + int(a.get('inconclusive', 0))}")
        for r in root.findall(".//t:UnitTestResult", ns):
            if r.attrib.get("outcome") == "Failed":
                msg = r.find(".//t:ErrorInfo/t:Message", ns)
                st = r.find(".//t:ErrorInfo/t:StackTrace", ns)
                lines.append(red(f"FAILED {r.attrib.get('testName')}"))
                if msg is not None and msg.text:
                    lines += ["    " + x for x in msg.text.strip().splitlines()]
                if st is not None and st.text:
                    lines += ["    " + x.replace(str(base) + os.sep, "") for x in st.text.strip().splitlines()[:6]]
    else:
        # build failure or runner failure: show the relevant output
        out = [l for l in p.stdout.splitlines() if "error" in l.lower() or "failed" in l.lower()]
        lines += (out or p.stdout.splitlines())[-40:]
    return ok, "\n".join(lines), dt


def cmd_check(args):
    with cache_lock("cache", exclusive=False, what="setup"):
        return _cmd_check(args)


def _cmd_check(args):
    t_start = time.time()
    if args.legacy_refs:
        raise SystemExit("--legacy-refs was dropped with the Unity 6.3 upgrade (IAP 5 / Input System 1.20 / URP 17.3 do not "
                         "build against the 2021.3 reference assemblies); the default real 6000.0.58 engine refs are used.")
    project = Path(args.project).resolve()
    if not (project / "Assets").is_dir():
        raise SystemExit(f"not a Unity project (no Assets/): {project}")
    if not P.tool_dll.exists() or not (P.libs_player / "UnityEngine.UI.dll").exists() or \
            not (P.libs_player_ios / "UnityEngine.UI.dll").exists():
        raise SystemExit("toolchain not set up (or set up by an older version) - run: bash tools/unity-compile-check/setup.sh")
    git_root = subprocess.run(["git", "-C", str(project), "rev-parse", "--show-toplevel"], capture_output=True, text=True)
    base = Path(git_root.stdout.strip()) if git_root.returncode == 0 else project.parent

    lay = ProjectLayout(project)
    lookup = Unity6Lookup(P.csref_index)
    extra_nowarn = lay.csc_rsp_nowarn()
    # PlayerSettings: "Suppress Common Warnings" (CS0169/CS0649) and "Allow 'unsafe' Code" for the predefined assemblies
    if lay.player_setting("suppressCommonWarnings", "1") == "1":
        extra_nowarn += ["CS0169", "CS0649"]
    ac_unsafe = lay.player_setting("allowUnsafeCode", "0") == "1"

    netstd = P.netstandard_refs()
    ue = P.unityengine_refs()
    ued = P.unityeditor_refs()
    player_libs = {"android": sorted(str(p) for p in P.libs_player.glob("*.dll")),
                   "ios": sorted(str(p) for p in P.libs_player_ios.glob("*.dll"))}
    editor_libs = {p.name: str(p) for p in P.libs_player.glob("*.dll")}
    editor_libs.update({p.name: str(p) for p in P.libs_editor.glob("*.dll")})   # editor builds win
    editor_libs = sorted(editor_libs.values())
    nunit = P.nunit_ref()
    newtonsoft = P.newtonsoft_ref()

    stages = OrderedDict()
    specs = []
    wanted = set(args.stages.split(",")) if args.stages else None

    def want(key):
        return wanted is None or key in wanted

    # 1. Core (pure C#)
    core_unsafe = bool(lay.core_asmdef and lay.core_asmdef.data.get("allowUnsafeCode"))
    tests_unsafe = bool(lay.tests_asmdef and lay.tests_asmdef.data.get("allowUnsafeCode"))
    if lay.core_asmdef and not lay.core_asmdef.data.get("noEngineReferences"):
        log(yellow(f"  note: {rel(str(lay.core_asmdef.path), base)} does not set noEngineReferences: true (it is still checked without Unity refs)"))
    specs.append(spec("BeamRunner.Core", lay.core_sources, refs=netstd, unsafe=core_unsafe,
                      defines=check_defines("android", []), audit=False))
    stages["core"] = {"title": "BeamRunner.Core (netstandard2.1, C# 9, NO Unity references)", "compile": ["BeamRunner.Core"], "u6": "none"}

    # 2. Tests
    test_defines = check_defines("editor", ["UNITY_INCLUDE_TESTS"])
    specs.append(spec("BeamRunner.Core.Tests", lay.test_sources, refs=netstd + nunit, comp_refs=["BeamRunner.Core"],
                      defines=test_defines, unsafe=tests_unsafe))
    stages["tests"] = {"title": "BeamRunner.Core.Tests (compile vs NUnit 3.5 like Unity, then run with dotnet test)",
                       "compile": ["BeamRunner.Core.Tests"], "run_tests": True, "u6": "none"}

    # 3. Assembly-CSharp player flavours (each against the package assemblies built for that platform)
    for target in ("android", "ios"):
        name = f"Assembly-CSharp [{target} player]"
        player_refs = netstd + ue + player_libs[target] + newtonsoft
        specs.append(spec(name, lay.runtime_sources, refs=player_refs, comp_refs=["BeamRunner.Core"],
                          defines=check_defines(target, lay.extra_defines(target) + args.define), unsafe=ac_unsafe,
                          audit=not args.no_unity6_audit, assembly_name="Assembly-CSharp"))
    stages["player"] = {"title": "Assembly-CSharp, player flavour (Android + iOS defines, no UNITY_EDITOR)",
                        "compile": ["Assembly-CSharp [android player]", "Assembly-CSharp [ios player]"],
                        # engine refs are 6000.0.58 while the project is 6000.3: a missing engine API that exists in
                        # the 6000.3.25f1 runtime sources is reported as "unity6-only" (not a failure); anything else
                        # (e.g. an editor-only member used outside #if UNITY_EDITOR) is a real error
                        "u6": "player"}

    # 4. Assembly-CSharp editor flavour
    editor_refs = netstd + ue + ued + editor_libs + P.newtonsoft_ref("editor")
    specs.append(spec("Assembly-CSharp [editor]", lay.runtime_sources, refs=editor_refs, comp_refs=["BeamRunner.Core"],
                      defines=check_defines("editor", lay.extra_defines("standalone") + args.define), unsafe=ac_unsafe,
                      audit=not args.no_unity6_audit, assembly_name="Assembly-CSharp"))
    stages["editor-runtime"] = {"title": "Assembly-CSharp, editor flavour (UNITY_EDITOR + standalone defines, UnityEditor referenced)",
                                "compile": ["Assembly-CSharp [editor]"], "u6": "editor"}

    # 5. Assembly-CSharp-Editor
    specs.append(spec("Assembly-CSharp-Editor", lay.editor_sources, refs=editor_refs,
                      comp_refs=["BeamRunner.Core", "Assembly-CSharp [editor]"],
                      defines=check_defines("editor", lay.extra_defines("standalone") + args.define), unsafe=ac_unsafe,
                      audit=not args.no_unity6_audit))
    stages["editor"] = {"title": "Assembly-CSharp-Editor (Editor folders)", "compile": ["Assembly-CSharp-Editor"], "u6": "editor"}

    # 6. PlayMode smoke tests (asmdef BeamRunner.PlayMode.Tests): an Editor build and a development-player build
    #    against UnityEngine.TestRunner (com.unity.test-framework) + Unity's custom nunit.framework.dll + Core only (test
    #    asmdefs cannot reference Assembly-CSharp); plus Assembly-CSharp as a development player, where the game's test
    #    probe (#if UNITY_EDITOR || DEVELOPMENT_BUILD) is compiled without UnityEditor.
    playmode_error = None
    if want("playmode") and lay.playmode_asmdef is not None:
        tf, tf_specs, playmode_error = test_framework_for_check()
        specs.extend(tf_specs)
        pm_unsafe = bool(lay.playmode_asmdef.data.get("allowUnsafeCode"))
        # (UnityEngine.TestRunner is only listed when it had to be compiled from the clone in this run)
        pm_names = [x["name"] for x in tf_specs]
        pm_modes = {x["name"]: ("editor" if "[editor" in x["name"] else "player") for x in tf_specs}
        for flavour, target, label, extra in (
                ("editor", "editor", "BeamRunner.PlayMode.Tests [editor]", ["UNITY_INCLUDE_TESTS"]),
                ("android", "android", "BeamRunner.PlayMode.Tests [android development player]",
                 ["UNITY_INCLUDE_TESTS", "DEVELOPMENT_BUILD"])):
            if flavour not in tf:
                continue
            tf_refs, tf_comp = tf[flavour]
            specs.append(spec(label, lay.playmode_sources, refs=netstd + ue + (ued if flavour == "editor" else []) + tf_refs,
                              comp_refs=["BeamRunner.Core"] + tf_comp, defines=check_defines(target, extra),
                              unsafe=pm_unsafe, audit=not args.no_unity6_audit, assembly_name="BeamRunner.PlayMode.Tests"))
            pm_names.append(label)
            pm_modes[label] = "editor" if flavour == "editor" else "player"
        dev = "Assembly-CSharp [android development player]"
        specs.append(spec(dev, lay.runtime_sources, refs=netstd + ue + player_libs["android"] + newtonsoft,
                          comp_refs=["BeamRunner.Core"],
                          defines=check_defines("android", lay.extra_defines("android") + args.define + ["DEVELOPMENT_BUILD"]),
                          unsafe=ac_unsafe, audit=not args.no_unity6_audit, assembly_name="Assembly-CSharp"))
        pm_names.append(dev)
        pm_modes[dev] = "player"
        stages["playmode"] = {"title": "PlayMode smoke tests + game test probe (Editor and DEVELOPMENT_BUILD flavours)",
                              "compile": pm_names, "u6": pm_modes, "error": playmode_error}

    for s in specs:
        s["nowarn"] = NOWARN + extra_nowarn
    plan = {"compilations": specs, "unity6Index": str(P.csref_index), "unity6Version": UNITY_VERSION,
            # the compiler already applies the real 6000.0.58 [Obsolete] attributes; the audit adds what changed up to
            # 6000.3.25f1 (UnityEngine) and everything newer than 2021.1 (UnityEditor)
            "auditAssemblyPrefixes": ["UnityEngine", "UnityEditor"]}
    log(bold(f"unity-compile-check: project {rel(str(project), base) if project != base else project}  (Unity {UNITY_VERSION}; "
             f"engine refs: {ENGINE_LABEL}; UnityEditor 2021.1; cache {CACHE})"))
    log(dim(f"  sources: core={len(lay.core_sources)} tests={len(lay.test_sources)} runtime={len(lay.runtime_sources)} "
            f"editor={len(lay.editor_sources)} playmode-tests={len(lay.playmode_sources)}"))
    for a in lay.other_asmdefs:
        log(yellow(f"  note: asmdef '{a.name}' at {rel(str(a.dir), base)} is not compiled by this tool (only BeamRunner.Core/.Tests are)"))
    t0 = time.time()
    results, _stderr = run_tool_plan(plan, "check")
    compile_wall = time.time() - t0
    by_name = {r["name"]: r for r in results}

    summary = []
    all_obsolete6 = []
    any_fail = False
    n = len([k for k in stages if want(k)])
    i = 0
    for key, st in stages.items():
        if not want(key):
            continue
        i += 1
        log("")
        log(bold(f"==> [{i}/{n}] {st['title']}"))
        stage_ok = True
        stage_ms = 0
        if st.get("error"):
            stage_ok = False
            log("  " + red("FAIL " + st["error"]))
        for cname in st["compile"]:
            r = by_name[cname]
            stage_ms += r["milliseconds"]
            diags = r["diagnostics"]
            errs, u6 = classify_unity6(diags, lookup, st["u6"] if isinstance(st["u6"], str) else st["u6"][cname])
            warns = [d for d in diags if d["severity"] == "warning"]
            if r.get("fatal"):
                errs.append({"severity": "error", "id": "UCC0001", "message": r["fatal"], "file": None})
            strict_fail = args.strict_unity6 and u6
            ok = not errs and not strict_fail
            stage_ok &= ok
            label = green("PASS") if ok else red("FAIL")
            log(f"  {label} {cname}: {r['sourceCount']} files, {len(errs)} errors"
                f"{', ' + str(len(u6)) + ' Unity-6-only' if u6 else ''}, {len(warns)} warnings ({r['milliseconds'] / 1000:.1f}s)")
            for d in errs:
                log("    " + red(fmt_diag(d, base)))
            # group Unity-6-only errors by the verified API so the list stays short
            groups = OrderedDict()
            for d in u6:
                groups.setdefault(d["unity6"], []).append(d)
            for hit, ds in groups.items():
                d = ds[0]
                log("    " + yellow(fmt_diag(d, base).replace(": error ", ": unity6-only ", 1)) + dim(f"  [{hit}]"))
                if len(ds) > 1:
                    more = ", ".join(f"{os.path.basename(x.get('file') or '?')}({x.get('line')},{x.get('col')})" for x in ds[1:])
                    log(dim(f"        + {len(ds) - 1} more use(s): {more}"))
            obs = [d for d in warns if d["id"] in OBSOLETE_IDS]
            for d in obs:
                log("    " + yellow(fmt_diag(d, base)))
            if warns:
                cnt = Counter(d["id"] for d in warns)
                log(dim("    warnings by id: " + ", ".join(f"{k}x{v}" for k, v in sorted(cnt.items(), key=lambda x: (-x[1], x[0])))))
                if args.verbose:
                    for d in warns:
                        if d["id"] not in OBSOLETE_IDS:
                            log(dim("    " + fmt_diag(d, base)))
            for u in r.get("unity6") or []:
                all_obsolete6.append((cname, u))
        if st.get("run_tests"):
            if args.no_tests:
                log(dim("  (test run skipped: --no-tests)"))
            elif not lay.test_sources:
                log(dim("  (no test sources - nothing to run)"))
            elif not stage_ok:
                log(red("  test run skipped because the test assembly does not compile"))
            else:
                with cache_lock("testrunner", exclusive=True, what="test run"):
                    ok, text, dt = run_tests(lay.core_sources, lay.test_sources, base)
                stage_ms += int(dt * 1000)
                label = green("PASS") if ok else red("FAIL")
                log(f"  {label} dotnet test (net8.0, NUnit {TEST_PACKAGES['NUnit']}) ({dt:.1f}s)")
                for line in text.splitlines():
                    log("    " + line)
                stage_ok &= ok
        any_fail |= not stage_ok
        summary.append((i, st["title"].split(" (")[0], stage_ok, stage_ms))

    # Unity 6 semantic API audit (obsolete / missing in the project's Unity version) - deduplicate across flavours
    if all_obsolete6 and not args.no_unity6_audit:
        seen = OrderedDict()
        for cname, u in all_obsolete6:
            k = (u["kind"], u["api"], u["file"], u["line"], u["col"])
            seen.setdefault(k, (u, []))[1].append(cname.replace("Assembly-CSharp ", "").strip("[]") if cname.startswith("Assembly-CSharp [") else cname)
        obsolete = [(u, fl) for (u, fl) in seen.values() if u["kind"] == "obsolete"]
        missing = [(u, fl) for (u, fl) in seen.values() if u["kind"] == "missing"]
        log("")
        scope = f"UnityEngine symbols (refs are {ENGINE_REF_VERSION}) + UnityEditor symbols (refs are 2021.1)"
        log(bold(f"==> Unity {UNITY_VERSION} API audit: {scope} checked against UnityCsReference {UNITY_VERSION}"))
        if obsolete:
            log(yellow(f"  {len(obsolete)} use(s) of APIs that are [Obsolete] in Unity {UNITY_VERSION}:"))
            for u, fl in obsolete:
                kind = "error" if u["error"] else "warning"
                log("    " + yellow(f"{rel(u['file'], base)}({u['line']},{u['col']}): unity6-obsolete-{kind}: {u['api']}: {u['message']}"))
            if any(u["error"] for u, _ in obsolete):
                any_fail = True
                log(red(f"  obsolete-as-error APIs will NOT compile in Unity {UNITY_VERSION} -> counted as failure"))
        if missing:
            log(dim(f"  {len(missing)} use(s) of APIs not found in UnityCsReference {UNITY_VERSION} (may be removed in Unity {UNITY_VERSION}, or native/unpublished - review):"))
            for u, fl in missing:
                log(dim(f"    {rel(u['file'], base)}({u['line']},{u['col']}): {u['api']}: {u['message']}"))
        if not obsolete and not missing:
            log(green(f"  no obsolete/missing Unity {UNITY_VERSION} API usage found"))

    log("")
    log(bold("==> Summary"))
    for i, title, ok, ms in summary:
        log(f"  [{i}] {green('PASS') if ok else red('FAIL')}  {title:<32} {ms / 1000:6.1f}s")
    log(dim(f"  compile wall time {compile_wall:.1f}s, total {time.time() - t_start:.1f}s"))
    log(red("RESULT: FAIL") if any_fail else green("RESULT: PASS"))
    return 1 if any_fail else 0


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    s = sub.add_parser("setup")
    s.add_argument("--force", action="store_true", help="rebuild tool, index and libs")
    s.add_argument("--legacy-refs", action="store_true", help=argparse.SUPPRESS)   # dropped (Unity 6.3 upgrade)
    c_ = sub.add_parser("check")
    c_.add_argument("--project", default=os.environ.get("UCC_PROJECT", str(DEFAULT_PROJECT)), help="Unity project root (contains Assets/)")
    c_.add_argument("--legacy-refs", action="store_true", help=argparse.SUPPRESS)   # dropped (Unity 6.3 upgrade)
    c_.add_argument("--stages", help="comma list of stages: core,tests,player,editor-runtime,editor,playmode (default: all)")
    c_.add_argument("--define", action="append", default=[], help="extra scripting define for the Assembly-CSharp stages (repeatable)")
    c_.add_argument("--no-tests", action="store_true", help="compile the tests but do not run them")
    c_.add_argument("--strict-unity6", action="store_true", help="treat errors for APIs missing from the refs but verified in UnityCsReference 6000.3.25f1 as failures")
    c_.add_argument("--no-unity6-audit", action="store_true", help="skip the semantic Unity 6 obsolete/missing API audit")
    c_.add_argument("-v", "--verbose", action="store_true", help="print every warning, not only the obsolete-API ones")
    args = ap.parse_args()
    if args.cmd == "setup":
        return cmd_setup(args) or 0
    return cmd_check(args)


if __name__ == "__main__":
    sys.exit(main())
