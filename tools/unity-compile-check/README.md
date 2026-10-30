# unity-compile-check

Headless compile check + unit-test runner for the BeamRunner Unity project (Unity **6000.3.25f1** = Unity 6.3 LTS,
C# 9, .NET Standard 2.1 API level), for environments where the Unity Editor cannot be installed.
It compiles the project's C# with Roslyn against **real Unity 6000.0.58 engine assemblies** (the newest available) and
real package sources (uGUI, Input System 1.20, **URP/Core RP 17.3.0**, IAP 5.4.3, Services Core 1.18.0, iOS 14
Advertising Support 1.2.0, Mobile Notifications 2.5.0), Sentry for Unity 4.10.3's real precompiled assemblies, a
**LevelPlay 9.5.1 stub**, runs the pure-C# NUnit tests with `dotnet test`, and audits
engine/editor API usage against **UnityCsReference 6000.3.25f1**.

```bash
bash tools/unity-compile-check/setup.sh      # once per container (idempotent: ~1.5-2 min fresh, ~1 min when libs rebuild, ~5 s no-op)
bash tools/unity-compile-check/check.sh      # every time; exit code 0 = all stages pass
bash tools/unity-compile-check/check.sh --project /path/to/OtherUnityProject
python3 tools/unity-compile-check/audit-unity6-api.py                       # text scan for 6000.3-obsolete APIs
python3 tools/unity-compile-check/audit-unity6-api.py --lookup Object.GetEntityId --lookup Light.cookieSize
```

`check.sh` options: `--project <root>` (default `$UCC_PROJECT`, else `<repo>/BeamRunner`, derived from the script location),
`--stages core,tests,player,editor-runtime,editor,playmode`, `--define SYMBOL` (extra define for the Assembly-CSharp
stages, e.g. `--define UNITY_PURCHASING`), `--no-tests`, `--strict-unity6` (count "unity6-only" errors as
failures), `--no-unity6-audit`, `-v` (print every warning). `--legacy-refs` (2021.3 engine refs + URP stubs) was
**dropped** with the 6.3 upgrade: IAP 5 / Input System 1.20 / URP 17.3 do not build against 2021.3 refs.

## Stages

| # | Stage | Sources | References / defines |
|---|-------|---------|----------------------|
| 1 | `BeamRunner.Core` | `Assets/BeamRunner/Scripts/Core/**/*.cs` | netstandard 2.1 **only** – proves `noEngineReferences` purity |
| 2 | `BeamRunner.Core.Tests` | `Assets/BeamRunner/Tests/EditMode/**/*.cs` | compiled like Unity (netstandard 2.1 + NUnit **3.5** + Core, `UNITY_INCLUDE_TESTS`), then **run** with `dotnet test` (net8.0, NUnit 3.14, NUnit3TestAdapter) on a generated project that includes the same files; prints pass/fail counts and failing test messages |
| 3 | `Assembly-CSharp` player: **Android** and **iOS** | every `.cs` under `Assets/` except `Editor` folders (any depth), folders owned by an `.asmdef`/`.asmref`, hidden/`~` folders | UnityEngine 6000.0.58 + the package assemblies **built for that platform** (`libs/unity6/player` resp. `player-ios`, see below) + Newtonsoft (Unity's AOT build) + Core; define ladder `UNITY_5_3_OR_NEWER … UNITY_2023_3_OR_NEWER, UNITY_6000_0_OR_NEWER … UNITY_6000_3_OR_NEWER, UNITY_6000, UNITY_6000_3, UNITY_6000_3_25`, `ENABLE_INPUT_SYSTEM`, `ENABLE_LEGACY_INPUT_MANAGER`, `UNITY_ANDROID/PLATFORM_ANDROID/UNITY_MOBILE/ENABLE_IL2CPP` resp. `UNITY_IOS/UNITY_IPHONE/PLATFORM_IOS`, `NET_STANDARD_2_1`, `CSHARP_7_3_OR_NEWER`, `TRACE`; no `UNITY_EDITOR` |
| 4 | `Assembly-CSharp` editor flavour | same sources | + `UnityEditor.dll`, `UNITY_EDITOR`, `UNITY_EDITOR_64`, `UNITY_EDITOR_LINUX`, standalone-Linux defines, `DEBUG`, `UNITY_ASSERTIONS`, `UNITY_INCLUDE_TESTS`; packages: editor builds where they exist (uGUI, iOS Advertising Support), else the Android player build |
| 5 | `Assembly-CSharp-Editor` | `.cs` inside `Editor` folders (not asmdef-owned) | editor refs + `UnityEditor.UI` + stage-4 Assembly-CSharp |
| 6 | `playmode`: `BeamRunner.PlayMode.Tests` + Assembly-CSharp development player | `Assets/BeamRunner/Tests/PlayMode/**` (asmdef) as an Editor build and as a development-player build; plus Assembly-CSharp as an **Android `DEVELOPMENT_BUILD`** player (the game probe is compiled without UnityEditor) | `UnityEngine.TestRunner` built from the real Unity Test Framework 1.4.6 sources (editor + android, in `libs/unity6/test-framework/`), Unity's custom `nunit.framework.dll` (`com.unity.ext.nunit` 2.0.5) and Core only – a reference to Assembly-CSharp, uGUI or UnityEditor fails like in Unity |

**Per-platform package builds.** Every package asmdef is compiled once per player platform with that platform's
defines and Unity's `includePlatforms`/`excludePlatforms` filtering, so the Android stage and the iOS stage see
what Unity would give them: `Unity.Advertisement.IosSupport` (`includePlatforms: [Editor, iOS]`) exists for the
iOS and editor stages only (using `ATTrackingStatusBinding` outside `#if UNITY_IOS` fails the Android stage);
IAP uses `Unity.Purchasing.Apple` on iOS and `Unity.Purchasing.AppleStub` on Android.

Also honoured: `Assets/csc.rsp` (`-define:`/`-nowarn:`), `scriptingDefineSymbols` per platform, PlayerSettings
"Suppress Common Warnings" (CS0169/CS0649) and "Allow unsafe code", `allowUnsafeCode` of the Core/Tests asmdefs,
`UNITY_ANALYTICS` only if Analytics is enabled in `UnityConnectSettings.asset`.
Empty folders are fine (a stage with 0 files passes). Other asmdefs under `Assets/` are reported but not compiled.

Output: every error as `path(line,col): error CSxxxx: …` (paths relative to the repo), warnings summarised as
counts per id with **every CS0618/CS0619/CS0612 (obsolete) warning printed in full** (engine obsoletes of 6000.0
come from the real `[Obsolete]` attributes, package obsoletes – e.g. IAP 4-style `UnityPurchasing`/`IDetailedStoreListener`
in IAP 5 – from the real package sources), then the Unity 6000.3 API audit and a summary with per-stage timings.
All stages compile in one Roslyn process (`UccTool`) using `Compilation.GetDiagnostics()`, so method-body errors are
reported even when declarations have errors, and a downstream stage is still checked when an upstream one fails.

## What is real and what is stubbed

| Component | Status |
|-----------|--------|
| UnityEngine (all 74 modules) | **Real Unity 6000.0.58 player managed assemblies** from `LavaGang/Unity-Runtime-Libraries` (`6000.0.58.zip`, commit-pinned + SHA-256). That repo has no 6000.1+ zip, so 6000.0.58 is the closest to the project's 6000.3.25f1. Full implementation assemblies (not publicized, `[Obsolete]` attributes intact). Engine APIs added in 6000.1–6000.3 are therefore missing from the refs; uses are classified against the 6000.3.25f1 index (below). They are a *player* build: editor-only engine members are absent, and a few platform-specific classes (`Handheld`, `UnityEngine.iOS.Device`) are missing → shims. |
| API index | **UnityCsReference tag `6000.3.25f1`** (the project's exact version): `[Obsolete]`/removed/6.3-only verdicts of the audit and of the "unity6-only" classification are against 6.3. |
| UnityEditor | Real `UnityEditor.dll` **2021.1.14.1** (NuGet `Unity3D.SDK`) – still the newest publicly available. Newer editor APIs are reported as `unity6-only`. |
| URP 17.3.0 / Core RP 17.3.0 (+ GPUDriven, UnifiedRayTracing, Universal.Config, 2D, ShaderLibrary asmdefs) | **Compiled from real sources**: `Unity-Technologies/Graphics` branch `6000.3/staging`, commit `2d2e78cc` (2026-07-01, package.json 17.3.0). Player build (no `UNITY_EDITOR`). Because 17.3 targets the 6000.3 engine: (1) **body stripping** – member bodies/initializers that use engine APIs missing from the 6000.0.58 refs (VRS/shading-rate, `EntityId` engine overloads, mesh LOD, XR scaling, …) are replaced with `throw null`/`default` by UccTool (`BodyStripper`), iterating until no body has an error; declarations, signatures and `[Obsolete]` attributes stay real (81 bodies; list in `$UCC_CACHE/libs/unity6/stripped-bodies.<platform>.txt`, all in `XRSystem`, VRS, RTHandle/TextureDesc, NativePassCompiler, GPU-resident drawer, a few URP render passes – nothing in Volume/post-processing/camera data); (2) 6000.1–6000.3 engine **types** used in declarations are shims (below); (3) two source patches (`ucc.py` `PATCHES`): `UniversalRenderPipelineAsset.requiresCompatibleRenderPipelineGlobalSettings` is not an `override` (the 6000.0 base lacks the virtual), `UniversalRenderPipeline` gets an empty `Render(ScriptableRenderContext, Camera[])` override (abstract in 6000.0, virtual in 6000.3). |
| Input System 1.20.0 | **Real sources** (needle-mirror tag; versionDefines evaluated for 6000.3.25; `UNITY_INPUT_SYSTEM_PROJECT_WIDE_ACTIONS` on; `…INPUT_MODULE_SCROLL_DELTA`, `…SENDPOINTERHOVERTOPARENT`, `…INPUT_MODULE_NAVIGATION_DEVICE_TYPE` dropped because uGUI 1.0 lacks the base members; `…PLATFORM_POLLING_FREQUENCY` dropped because its native API is not in 6000.0.58 – all internal, no public API change). Player build only. |
| Unity IAP 5.4.3 | **Real sources**, all runtime asmdefs per platform (Apple on iOS, AppleStub on Android; `TransactionVerifier` skipped by its `IAP_TX_VERIFIER_ENABLED` constraint like in Unity) → exact public API incl. the real IAP 5 `[Obsolete]`s on the IAP 4 API. Without the optional Analytics/Authentication/GDK integrations (not in the project). No patches. |
| Unity Services Core 1.18.0 | **Real sources** (all runtime asmdefs except `Registration`, which needs unpublished asmdefs). |
| Newtonsoft Json | **Real** `com.unity.nuget.newtonsoft-json` 3.2.2 (needle-mirror): `Runtime/AOT/Newtonsoft.Json.dll` for players, `Runtime/Newtonsoft.Json.dll` for the editor (Unity's build has `AotHelper`, used by IAP 5). |
| iOS 14 Advertising Support 1.2.0 (`Unity.Advertisement.IosSupport`) | **Real sources** (needle-mirror), runtime asmdef only, iOS player + editor builds (see per-platform builds). |
| Mobile Notifications 2.5.0 (`Unity.Notifications.Android` / `.iOS` / `.Unified`) | **Real sources** (needle-mirror tag 2.5.0), runtime asmdefs per platform: Android [Android, Editor], iOS [Editor, iOS], Unified [all]. The Editor asmdef (settings UI, Gradle/Xcode post-processing) is not built (needs Unity 6 editor APIs). |
| Google Play Games plugin for Unity 2.2.1 (`Google.Play.Games`) | **Real sources**: `playgameservices/play-games-plugin-for-unity` tag `v2.2.1` (sparse checkout of the package's `Runtime/`), runtime asmdef only (includePlatforms [Android, Editor] ⇒ Android stage). The project vendors the same files under `Assets/GooglePlayGames` (the plugin is only distributed as a `.unitypackage`); those asmdefs are reported as "not compiled by this tool". |
| Sentry for Unity 4.10.3 (`io.sentry.unity`: `Sentry`, `Sentry.Unity` + their bundled `Sentry.System.*` / `Sentry.Microsoft.*` dependencies) | **Real precompiled assemblies** from the official UPM repo `getsentry/unity` tag `4.10.3` (sparse checkout of `Runtime/*.dll` only; `PRECOMPILED_PACKAGES` copies them into the player lib folders like Unity auto-references them). Platform plumbing DLLs (`Sentry.Unity.Android/iOS/Native*`) are left out. |
| **Unity LevelPlay 9.5.1** (`com.unity.services.levelplay`, assembly `Unity.LevelPlay`, namespace `Unity.Services.LevelPlay`) | **Stub** (`stubs/Unity.Services.LevelPlay/LevelPlay.cs`) – the package is not publicly mirrored. Small public API subset; every member is tagged `[usage]` (shape verified by real usage in `wagenheimer/UnityLevelPlayHelper` 2.12.0, which depends on levelplay 9.5.1: `LevelPlay.Init/OnInitSuccess/OnInitFailed/SetMetaData/LaunchTestSuite`, `LevelPlayPrivacySettings.SetGDPRConsent/SetCCPA/SetCOPPA`, `LevelPlayRewardedAd`/`LevelPlayInterstitialAd`/`LevelPlayBannerAd` + all their events, `LevelPlayAdInfo`, `LevelPlayAdError`, `LevelPlayReward`, `LevelPlayImpressionData`, `LevelPlayConfiguration`, `LevelPlayInitError`, `LevelPlayBannerPosition`) or `[docs]` (from the docs only: `ValidateIntegration`, `SetPauseGame`, `Config.Builder.SetBidFloor`, `IsPlacementCapped`, `LevelPlayAdFormat`, …). Removed-in-9.x APIs (`IronSource.Agent`, `LevelPlay.SetConsent`, `LevelPlayAdDisplayInfoError`, `com.unity3d.mediation`) are deliberately absent; global `LevelPlay.OnImpressionDataReady` is `[Obsolete]` (deprecated in 9.5.0). Bodies do nothing. |
| Unity Ads 4.x (`UnityEngine.Advertisements`) | **Removed** (the legacy Ads stub is gone, like `com.unity.ads` from the manifest). |
| uGUI (`UnityEngine.UI`, `UnityEditor.UI`) | **Compiled from real sources** – needle-mirror `com.unity.ugui` **1.0.0 @ Unity-2021.1.17f1** (uGUI 2.0.0 ships inside the editor and is mirrored nowhere) + patches (`ucc.py` `PATCHES`): `PointerEventData.fullyExited/reentered/displayIndex`; Unity 6 UIToolkit adaptations in `PanelEventHandler`; `CanvasRenderer.onRequestRebuild` (editor-only) removed. uGUI 2.0-only API (TextMeshPro, …) is missing. |
| Mathematics 1.3.2, Collections 2.5.1, Burst runtime | Real sources (needle-mirror). Burst: only `1.6.0-pre.2` is mirrored – only its small `Unity.Burst` runtime API is compiled. |
| Shims (`unity6-shims/common/`, assembly `UnityEngine.Unity6Shims`, referenced by the packages and Assembly-CSharp) | NEW types missing from the player engine assemblies, surface copied from UnityCsReference: `Handheld`, `UnityEngine.iOS.Device`, `DeviceGeneration` (incl. 6.3 values), movie/indicator enums (`PlatformApis.cs`); 6000.1–6000.3 types used in SRP 17.3 declarations: `UnityEngine.EntityId`, `ShadingRateFragmentSize/Combiner/CombinerStage`, internal GPU-driven mesh-LOD structs (`Unity6000_3Types.cs`). Members of existing engine types are never shimmed. |
| NUnit | 3.5.0 (≈ Unity's `com.unity.ext.nunit` 2.0.5) for the EditMode compile check; 3.14.0 for running; Unity's real `com.unity.ext.nunit` 2.0.5 `nunit.framework.dll` for the PlayMode stage. |
| Unity Test Framework 1.4.6 | **Real sources** (needle-mirror); `UnityEngine.TestRunner` is built during setup (editor + android flavours) for the PlayMode stage. |
| netstandard 2.1 | `NETStandard.Library.Ref` 2.1.0. |

## "unity6-only" classification and the Unity 6000.3 API audit

* The engine refs (6000.0.58) are older than the project (6000.3.25f1), UnityEditor is 2021.1 and both lack
  editor-only members. An error for a missing member/type that **exists in UnityCsReference 6000.3.25f1** is printed as
  `unity6-only CSxxxx … [X exists in Unity 6000.3.25f1]` and does not fail the stage (`--strict-unity6` to fail).
  Player stages only accept runtime (non-editor) APIs here, so an editor-only member used outside `#if UNITY_EDITOR`
  stays a real error. Caveat: the rest of such an expression is not type-checked (e.g. `obj.GetEntityId()`).
* **API audit** (end of check output): every symbol resolving into a UnityEngine*/UnityEditor* assembly is looked up
  in the 6000.3.25f1 index: `[Obsolete]` in 6000.3 (e.g. `Light.cookieSize`) → listed (obsolete-*error* fails the
  run); "not found in UnityCsReference" → listed for review only.
* Shims may only add NEW types – setup fails if a shim duplicates a type of the reference assemblies.

## Known gaps

* Engine refs are 6000.0.58, the project is 6000.3.25f1: 6.1–6.3 engine APIs are only *classified* (not type-checked),
  and 6000.0 APIs removed later would only show up as "not found" in the audit review list.
* UnityEditor is 2021.1 (editor-only engine members missing too) – see above.
* SRP 17.3: 81 internal member bodies are stripped (see above) – irrelevant for API checking, but the URP/Core
  assemblies are not runnable. URP editor assemblies are not built.
* LevelPlay is a stub: `[docs]` members are unverified; anything not in the stub is a (possibly false) error.
* URP/Core/uGUI/Input System/IAP are **player** builds: their `#if UNITY_EDITOR`-only API is missing in the editor
  stages (which use the Android build of the packages).
* uGUI is 1.0.0 + patches (see table); Burst is 1.6.0-pre.2's runtime API.
* Timeline, AI Navigation, Visual Scripting, TextMeshPro, Analytics, Authentication are not referenced – using them
  is a (false) error. `Assets/Plugins`/`Standard Assets` (Assembly-CSharp-firstpass) are merged into Assembly-CSharp.
* The editor stages use standalone-Linux defines (no `UNITY_ANDROID`/`UNITY_IOS`), so code under
  `#if UNITY_ANDROID || UNITY_IOS` is checked by the player stages only.
* Roslyn 4.8 (SDK 8.0) vs Unity's bundled Roslyn may differ in a few warnings.
* `audit-unity6-api.py` (text scan) is heuristic; `--all` shows low-confidence matches. `--lookup` answers "does X
  exist / is X obsolete in 6000.3.25f1 / is it in the refs check.sh uses".

## Files

* `setup.sh` – ensures .NET 8 SDK (`dotnet` on PATH → `~/.dotnet` via dotnet-install.sh → `apt-get install
  dotnet-sdk-8.0` fallback), then `ucc.py setup` (`--force` rebuilds everything).
* `check.sh` – wrapper for `ucc.py check`.
* `ucc.py` – driver: pinned inputs (NuGet ids + SHA-256, git tags/commits, sparse patterns), downloads, package
  builds (one global asmdef graph across packages: GUID/name references, versionDefines, platforms,
  defineConstraints, precompiled DLLs; per platform), patches, check stages, output.
* `src/UccTool/` – Roslyn host (C#): `compile` (plan → diagnostics JSON + API audit; `BodyStripper` for SRP),
  `index` (UnityCsReference → API/[Obsolete] TSV), `dump-refs`.
* `stubs/Unity.Services.LevelPlay/` – LevelPlay 9.5.1 stub. `unity6-shims/common/` – engine type shims.
* `audit-unity6-api.py` – text-based obsolete scan and `--lookup`.

Several agents can use the tool at the same time: `setup` takes an exclusive `flock` on the cache, `check`
a shared one (so it waits while setup rebuilds), test runs are serialised, and compile plans are per-process.
To try a completely fresh install without touching the shared cache/clones, point `UCC_CACHE` and `UCC_SRC_ROOT`
(or `UCC_NEEDLE_DIR`, `UCC_CSREF_DIR`, `UCC_URL_DIR` and `UCC_GRAPHICS_DIR`) at empty directories.

## Paths and environment variables

No path is tied to a particular `$HOME` or checkout location (`/home/user/...` is only a default, see `UCC_SRC_ROOT`):

| Variable | Default | Used for |
|----------|---------|----------|
| `UCC_PROJECT` | `<repo>/BeamRunner` (from the script location) | Unity project of `check.sh` / `audit-unity6-api.py` (`--project` wins) |
| `UCC_CACHE` | `~/.cache/unity-compile-check` | everything downloaded / built (layout below) |
| `UCC_SRC_ROOT` | `/home/user` if that is a writable directory (Claude Code containers, clones shared with other tools), else `$UCC_CACHE/src` | parent of the four clone folders below |
| `UCC_NEEDLE_DIR` | `$UCC_SRC_ROOT/needle-mirror` | needle-mirror package clones (`<dir>/<package>`) |
| `UCC_CSREF_DIR` | `$UCC_SRC_ROOT/unity-technologies/unitycsreference` | UnityCsReference clone |
| `UCC_URL_DIR` | `$UCC_SRC_ROOT/lavagang/unity-runtime-libraries` | LavaGang/Unity-Runtime-Libraries clone |
| `UCC_GRAPHICS_DIR` | `$UCC_SRC_ROOT/unity-technologies/graphics` | Unity-Technologies/Graphics clone |
| `DOTNET` | `dotnet` on `PATH`, else `~/.dotnet/dotnet` | .NET 8+ SDK |

**Clones are only needed by `setup`**, and only while something has to be (re)built: `check` reads nothing but
`$UCC_CACHE` (the Newtonsoft.Json DLLs are copied to `refs/newtonsoft-json-3.2.2/`). When the setup stamp matches
and the cache holds the libs, the API index, the engine refs and that copy, `setup` skips every clone that does not
exist (existing ones are still switched to the pinned tag/commit) - this is how CI runs with a restored cache
without cloning anything. If a later step still needs a skipped source (e.g. only the UnityEngine.TestRunner build is
stale), setup fetches the skipped clones and retries that step (`with_skipped_clones`). Fresh clones are small: shallow (depth 1), blob-less partial, sparse checkouts (needle-mirror
packages without hidden/`~` folders - Burst's `.Runtime/` alone is ~670 MB -, UnityCsReference `*.cs` under
`Runtime/`, `Modules/`, `Editor/`, `artifacts/generated/`, Graphics / Unity-Runtime-Libraries as before): about
40 MB of git objects in total, ~200 MB on disk. Existing full clones are left as they are.

Measured in a fresh container directory (all paths above + `HOME` pointed at empty folders): cold setup 98-106 s
(~30 s of it cloning), `check.sh` 23-31 s, repeated setup 5-8 s; with the cache restored from a tarball and no clones:
setup 6-7 s. The cache is ~315 MB (~130 MB gzip) without `src/`, `src-patched/` and `work/`, which CI does not cache.

Cache layout (`$UCC_CACHE`, default `~/.cache/unity-compile-check`): `downloads/` nupkgs, `pkgs/` unpacked,
`refs/unityengine-6000.0.58/` engine refs, `src-patched/unity6/` patched package sources,
`libs/unity6/{player,player-ios,editor}/` built package DLLs (+ `stripped-bodies.*.txt`), `index/` API indexes
(`unity-6000.3.25f1-api.tsv`, `reference-assemblies-api.unity6.tsv`), `tooling/` UccTool build, `testrunner/`
dotnet-test projects, `nuget-packages/`, `work/` last compile plans/results, `refs/newtonsoft-json-3.2.2/`
Newtonsoft.Json copies, `src/` the clones when `UCC_SRC_ROOT` falls back to it.
Clones (all `GIT_LFS_SKIP_SMUDGE=1`; existing clones are switched to the pinned tag in place; see the table above for
the locations): `needle-mirror/<pkg>` (tags), `unity-technologies/unitycsreference` (tag 6000.3.25f1),
`lavagang/unity-runtime-libraries` and `unity-technologies/graphics` (pinned commits); fresh clones are shallow,
blob-less partial and sparse.

## CI

`.github/workflows/checks.yml` runs `setup.sh`, `check.sh` and `tools/gen-metas.py --check` on every push / pull
request (ubuntu-latest, no Unity license needed). It caches `$UCC_CACHE` (minus `src/`, `src-patched/`, `work/`)
keyed on `hashFiles('tools/unity-compile-check/**')` with a prefix fallback, puts the clones in
`$RUNNER_TEMP/ucc-src` (`UCC_SRC_ROOT`) and uploads the setup/check logs when it fails. See `docs/CI.md`.
