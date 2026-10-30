# BeamRunner — notes for Claude

Portrait mobile neon runner where the track runs out and you build the rest (working title BeamRunner): the ship
rides rails of light on 3 lanes × 3 layers; the player swipes left/right to change lane, taps to build a bridge and swipes
up/down to build a ramp on the next tile, paying with fuel; one-layer falls cost hull, two layers / the void / the
ceiling kill; each level ends at a wall checkpoint (perk pick); gems buy ship upgrades between runs. Design doc:
`Builder Runner – Design Doc.docx` (repo root). One codebase for Google Play and the App Store. Unity **6000.3.25f1**
(6.3 LTS), URP 17.3, Input System, uGUI (no TextMeshPro), Unity IAP 5, Unity LevelPlay (ads).
Forked from **LightLancer** `main` (repo `LightBreaker`, itself a fork of CubeField 3D and Neo Slipstream): the
infrastructure (UI kit, services, saves, audio synth, tooling, ships/skins) is shared; the game rules
(`Scripts/Core/Field`, `Scripts/Core/Sim`, `Scripts/Core/Balance`, `Scripts/Core/Diagnostics/RunAutopilot.cs`), the
track rendering (`TrackRenderer`), input, HUD and checkpoint are this game's own. Don't modify the LightLancer,
CubeField or Neo Slipstream repos from here.

## This machine (Windows, Unity installed)
Unlike the cloud sessions the notes below were written for, this checkout has the real editor at
`D:\6000.3.25f1\Editor\Unity.exe`, so verify with Unity itself (close any open editor on this project first):
- Compile: `Unity.exe -batchmode -nographics -projectPath BeamRunner -quit -logFile <log>`, then grep the log for
  `error CS` / `warning CS`.
- EditMode tests: `-runTests -testPlatform EditMode -testResults <xml>`; PlayMode smoke tests (boot the real scene):
  `-runTests -testPlatform PlayMode` (without `-nographics`).
- Python is `py` (3.11); `py -I -X utf8 tools/check-strings.py` (UTF-8 mode, or printing CJK errors crashes).
- Android builds: Gradle fails here with `java.io.IOException: Unable to establish loopback connection` (the JDK
  cannot create its AF_UNIX pipe socket in the user's `%TEMP%`). Unity's own Gradle step hits it; the IL2CPP part
  still finishes and leaves the Gradle project in `Library/Bee/Android/Prj/IL2CPP/Gradle`. Finish the build there with
  Unity's JDK: `java -classpath <Editor>/Data/PlaybackEngines/AndroidPlayer/Tools/gradle/lib/gradle-launcher-*.jar
  org.gradle.launcher.GradleMain "-Dorg.gradle.jvmargs=-Xmx4096m -Djdk.net.unixdomain.tmpdir=D:\jt" --no-daemon
  assembleDebug` from its `launcher` folder (with `JAVA_TOOL_OPTIONS=-Djdk.net.unixdomain.tmpdir=D:\jt` and an existing
  `D:\jt`, outside the sandbox) → `launcher/build/outputs/apk/debug/launcher-debug.apk`. adb is in
  `<Editor>/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools`.

## Layout
- The Unity project is **`BeamRunner/`**, not the repo root. Game code: `BeamRunner/Assets/BeamRunner/`
  (`Scripts/Core` = pure C# rules asmdef, `Scripts/Runtime` = Assembly-CSharp, `Scripts/Editor`, `Tests/EditMode`,
  `Tests/PlayMode`, `Resources/BeamRunner/{Shaders,Fonts,Config}`).
- Docs: `docs/ARCHITECTURE.md` is the **binding design contract** (principles, C# constraints, module contracts,
  game flow) — read it before changing code. Also `docs/UI_KIT_API.md`, `docs/UI_SCREENS.md`,
  `Scripts/Runtime/Services/README.md`, `docs/CI.md`, `docs/FIRST_RUN_CHECKLIST.md`, `README.md` (release checklist).
- Tools: `tools/unity-compile-check/` (headless compile + tests), `tools/gen-metas.py` (.meta files),
  `tools/check-16kb.py` (Android 16 KB page size), `tools/audio-preview/`.

## There is no Unity Editor in cloud sessions
Nothing here can press Play. Verification is:
1. `bash tools/unity-compile-check/check.sh` — must end `RESULT: PASS` with 0 errors and 0 warnings (Core purity,
   EditMode tests run with `dotnet test`, Assembly-CSharp for Android/iOS/editor, Editor scripts, PlayMode tests +
   development player). "unity6-only" lines are APIs newer than the reference assemblies: fine once verified.
   The SessionStart hook runs `setup.sh`; if it failed, run `bash tools/unity-compile-check/setup.sh`. The toolchain
   cache (`~/.cache/unity-compile-check`) is shared with other repos in the same container and `setup.sh` writes the
   test-runner project with this repo's assembly names: if `dotnet test` fails to see `internal` Core members, re-run
   `setup.sh` from this repo.
2. `python3 tools/gen-metas.py --check` — every new file/folder under `Assets/` needs a `.meta`. Create them with
   `python3 tools/gen-metas.py` (deterministic GUIDs); never regenerate or edit existing metas.
   `GameBootstrap.cs.meta` has a fixed GUID the scene references. Plugin files (e.g. `.xcprivacy`, native libs)
   need a hand-written `PluginImporter` meta (see `Assets/Plugins/iOS/PrivacyInfo.xcprivacy.meta`).
3. `python3 tools/check-strings.py` — localization tables, no stray UI literals, font glyph coverage (needs
   fontTools; the SessionStart hook installs it, otherwise the glyph check is skipped with a warning).
4. Verify Unity and package APIs against source, never from memory: UnityCsReference 6000.3.25f1
   (`python3 tools/unity-compile-check/audit-unity6-api.py --lookup Type.Member`) and package sources (the
   toolchain's clones / `~/.cache/unity-compile-check/src-patched`). docs.unity3d.com is usually unreachable here.
5. Code under `#if UNITY_IOS` in `Scripts/Editor` is not compiled by check.sh (editor stages use standalone
   defines): compile it separately against a stub, or review it very carefully.
6. Anything that can only be confirmed in Unity or on a device goes into `docs/FIRST_RUN_CHECKLIST.md`.
   Say plainly what was verified and what wasn't.

## Rules that are easy to break
- C# 9 / .NET Standard 2.1; the "Do not use" list in ARCHITECTURE.md §2 (no records, `init`, TMP, legacy Input,
  `FindObjectOfType`, physics, …).
- `BeamRunner.Core` has `noEngineReferences`: all game rules live there, deterministic, with NUnit tests.
  The Unity layer renders state and feeds input.
- Everything is generated in code (meshes, textures, audio, UI, icons). Don't add authored assets or prefabs
  unless there is no alternative.
- Graceful degradation: missing config/ads/IAP/fonts/shaders/save must fall back, never throw.
- Test seam: PlayMode tests reach the game only through `GameProbeRegistry` (Core/Diagnostics); the probe and
  every `TestLaunchOptions` check are compiled only under `UNITY_EDITOR || DEVELOPMENT_BUILD`.
- Save data: keep old saves loadable (versioned profile + migration + a test). Saves are HMAC-signed: never change
  `SaveIntegrityKey` (a golden-vector test pins it) and keep new profile fields mergeable in `ProfileMerge`.
- `Assets/GooglePlayGames/` is a vendored third-party plugin (2.2.1): don't edit it, update it from upstream.
  Online features are configured in `Resources/BeamRunner/Config/online.json` (empty ids = off).
- All player-facing text goes through `Loc.T("key", …)` with the string in all 12 tables under
  `Resources/BeamRunner/Localization/` (see docs/LOCALIZATION.md); `python3 tools/check-strings.py` must pass
  (it rejects literals in UI calls, key/placeholder/plural mismatches and glyphs a font lacks). After changing
  ru/ja/ko/zh-Hans text run `python3 tools/build-fonts.py`. Never `ToUpper`/`ToLower` localized text.
- UI fonts cover limited glyphs: use `UIIcons` for symbols, not Unicode characters.
- Privacy: `Assets/Plugins/iOS/PrivacyInfo.xcprivacy` declares the game's required-reason APIs — update it when
  code starts using another one (e.g. disk space). Don't seed RNGs from `Environment.TickCount` (boot clock).
- Store requirements live in README "Release checklist" (target API 36, Billing 8+, 16 KB pages, Xcode 26).

## Workflow
- Keep docs in step with code: ARCHITECTURE (contracts), UI_KIT_API / UI_SCREENS (UI), Services README,
  FIRST_RUN_CHECKLIST (what to check in Unity), README (release checklist).
- CI (`.github/workflows/checks.yml`) runs the same check + `.meta` check on every push; `unity-build.yml` runs
  real Unity via GameCI once license secrets exist.
- Never commit secrets, keystores, `Library/`, `Builds/` or generated project files (see `.gitignore`).
