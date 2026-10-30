# Continuous integration

Two GitHub Actions workflows live in `.github/workflows/`:

| Workflow | Runs on | Needs secrets | What it does | Typical time |
|----------|---------|---------------|--------------|--------------|
| **Checks** (`checks.yml`) | every push (all branches), every pull request, manual | none | headless compile check of all C# + the EditMode NUnit tests (`tools/unity-compile-check`), `.meta` file check (`tools/gen-metas.py --check`) | ~1-2 min warm, ~3-4 min cold (estimate) |
| **Unity build** (`unity-build.yml`) | tags `v*`, manual | Unity license (Android signing optional) | real Unity 6000.3.25f1 in GameCI Docker images: EditMode + PlayMode tests, Android App Bundle (.aab), iOS Xcode project | ~1-2 h per run (estimate, see below) |

Without Unity secrets the Unity build workflow still runs its first job, which posts a notice
("Unity build skipped") and skips everything else, so the run stays green.

---

## Checks (`checks.yml`)

Three jobs on `ubuntu-latest`:

1. **Compile check + unit tests** – `bash tools/unity-compile-check/setup.sh`, then `bash tools/unity-compile-check/check.sh`.
   `check.sh` compiles `BeamRunner.Core`, the EditMode tests, `Assembly-CSharp` for Android and iOS, the editor
   flavour and the Editor scripts with Roslyn against Unity reference assemblies and runs the tests with
   `dotnet test` (details: `tools/unity-compile-check/README.md`). New stages added to `check.sh` are picked up
   automatically. The stage summary and test counts go to the job summary; on failure the setup and check logs are
   uploaded as the `unity-compile-check-logs` artifact (14 days).
2. **Unity .meta files** – `python3 tools/gen-metas.py --check` (missing, orphaned or duplicate `.meta` files).
3. **Localization strings + font glyphs** – `python3 tools/check-strings.py` with fontTools 4.66.0 (in a venv):
   string tables consistent with English (keys, placeholders, CLDR plural forms), no user-facing literals outside
   the tables, and every character of every language renders with its bundled font. Locally:
   `pip install fonttools`; after editing ru/ja/ko/zh-Hans strings run `python3 tools/build-fonts.py` first.

Caching: the toolchain cache (`~/.cache/unity-compile-check`, ~315 MB, ~130 MB compressed) is keyed on
`hashFiles('tools/unity-compile-check/**')`. An unchanged toolchain restores it exactly and `setup.sh` is a no-op
that does not clone anything (~10 s); after a toolchain change the newest older cache is restored and only what
changed is rebuilt. The cache is saved right after setup, so a red check still leaves a warm cache. A cold run
clones the pinned sources shallow, blob-less and sparse (~40 MB download) and builds everything (~100 s in setup).

Concurrency: a new push to the same branch / PR cancels the running check.

Measured locally in a fresh directory (not yet on a GitHub runner): cold setup 98 s, check 23-28 s, warm setup 5-7 s.

---

## Unity build (`unity-build.yml`)

Triggers: pushing a tag `v*` (e.g. `git tag v1.0.0 && git push origin v1.0.0`) or **Actions → Unity build → Run
workflow** with these inputs:

| Input | Default | Meaning |
|-------|---------|---------|
| `platforms` | `android-and-ios` | `android`, `ios` or both |
| `run_tests` | on | run the EditMode + PlayMode tests first; the builds only start when they pass |
| `version` | blank | bundleVersion for this build (blank: the tag on a `v*` tag, else ProjectSettings' `1.0.0`) |
| `android_version_code` | blank | Android versionCode (blank: derived from the version, see Versioning) |

Jobs:

1. **Preflight** – checks which secrets exist (never prints them), picks the platforms, and warns early when
   GameCI has no Docker image for the editor version in `ProjectSettings/ProjectVersion.txt`.
2. **Tests** – `game-ci/unity-test-runner@v4`, `testMode: all` (PlayMode + EditMode, every test assembly in the
   project, including `Tests/PlayMode`). Image `unityci/editor:ubuntu-6000.3.25f1-linux-il2cpp-3`. Results are
   posted as the **Unity test results** check run and uploaded as the `unity-test-results` artifact. Code coverage
   is off (the project has no Code Coverage package). PlayMode tests run in a headless batch-mode editor: no GPU.
3. **Build** – `game-ci/unity-builder@v6`, one platform at a time (matrix `max-parallel: 1`, so only one Unity
   seat is ever in use), images `unityci/editor:ubuntu-6000.3.25f1-android-3` / `-ios-3`:
   * It calls the project's own build entry point, `BeamRunner.EditorTools.FieldBuildMenu.BuildFromCommandLine`
     (`-fieldBuild aab|ios`), so CI builds exactly what **BeamRunner → Build** builds: the project validator
     runs first (errors fail the build), the Main scene is put first, the same build options are used. The method
     maps GameCI's arguments: `-customBuildPath` (output), `-buildVersion`, `-androidVersionCode` and the keystore
     arguments, and restores the Player Settings afterwards.
   * Android: `build/Android/BeamRunner.aab`, then `python3 tools/check-16kb.py build/Android/BeamRunner.aab`
     (fails the job when a 64-bit native library is not 16 KB page-size aligned – Google Play rejects those uploads
     for apps targeting Android 15+).
   * iOS: the Xcode project `build/iOS/BeamRunner/`. The Linux runner cannot run CocoaPods, so on a Mac run
     `pod install` in that folder (LevelPlay's iOS SDKs come as pods via the External Dependency Manager), open
     `Unity-iPhone.xcworkspace`, pick the team, Product → Archive. Signing / archiving an .ipa needs macOS and Apple
     certificates and is not done in CI.
   * Artifacts `BeamRunner-Android` / `BeamRunner-iOS` (14 days; Burst "DoNotShip" folders excluded).

Caching: `BeamRunner/Library` is cached per job (`unity-library-tests-…`, `unity-library-Android-…`,
`unity-library-iOS-…`), keyed on the hash of `Assets/`, `Packages/` and `ProjectSettings/` with a prefix fallback, so
later runs only re-import what changed. Each Library cache is typically 1-3 GB (the repository has 10 GB of cache).

Concurrency: one Unity build run at a time for the whole repository, queued, never cancelled – a job killed
half-way can leave a Unity Personal seat activated (see Troubleshooting). GitHub keeps only one pending run: a newer
queued run replaces an older queued one.

Pinned versions: `game-ci/unity-test-runner@v4`, `game-ci/unity-builder@v6` and the `game-ci/cli` release they run,
`GAMECI_CLI_VERSION: v0.1.69` (both actions default to "latest"; bump deliberately). The editor version is read from
`ProjectVersion.txt` (only the `m_EditorVersion` line is needed; no changeset / `m_EditorVersionWithRevision` line
is required for the Linux Docker images). The images `unityci/editor:ubuntu-6000.3.25f1-{linux-il2cpp,android,ios}-3`
were confirmed on Docker Hub (pushed 2026-09-24, ~5.8 / 8.7 / 5.8 GB compressed); the preflight job re-checks this
when the Unity version changes. Each Unity job first deletes preinstalled SDKs on the runner (.NET, Android SDK,
GHC, CodeQL) to make room for the image.

### Versioning

| Run | bundleVersion | Android versionCode |
|-----|---------------|---------------------|
| tag `v1.2.3` | `1.2.3` (GameCI `Tag`) | `1002003` = major×1 000 000 + minor×1 000 + patch |
| manual, `version` = `1.2.3` | `1.2.3` (GameCI `Custom`) | `1002003`, or `android_version_code` when given |
| manual, no `version` | ProjectSettings (`1.0.0`, GameCI `None`) | ProjectSettings (`1`), or `android_version_code` when given |

Google Play needs a strictly increasing versionCode: the derived code grows with every semver bump as long as
minor and patch stay below 1000. Keep ProjectSettings' `bundleVersion` in step with the tags for local builds.
iOS: only the version (CFBundleShortVersionString) is set; the build number (CFBundleVersion) stays the
ProjectSettings value – bump it in Xcode or Player Settings before each App Store upload. (GameCI's `buildVersion` /
`androidVersionCode` step outputs are not populated by game-ci/cli v0.1.69; the Build log line
`[BeamRunner] Command-line build: …` shows the values actually used.)

---

## Secrets

**Settings → Secrets and variables → Actions → New repository secret** (or `gh secret set NAME`). Names are
case-sensitive.

| Secret | Needed for | Notes |
|--------|------------|-------|
| `UNITY_EMAIL` | Unity build | Unity ID e-mail |
| `UNITY_PASSWORD` | Unity build | Unity ID password |
| `UNITY_SERIAL` | optional | Unity Pro/Plus serial; with it the seat is a paid one |
| `UNITY_LICENSE` | optional | contents of a `.ulf` file (Enterprise/Industry offline activation only) |
| `ANDROID_KEYSTORE_BASE64` | optional | release/upload keystore, base64 |
| `ANDROID_KEYSTORE_PASS` | with the keystore | keystore password |
| `ANDROID_KEYALIAS_NAME` | with the keystore | key alias |
| `ANDROID_KEYALIAS_PASS` | with the keystore | key password |

The Unity jobs run when `UNITY_EMAIL` + `UNITY_PASSWORD` (or `UNITY_LICENSE`) are set. Android signing is used only
when all four `ANDROID_*` secrets are set; otherwise the .aab is signed with Unity's debug keystore (fine for
testing, not accepted by Google Play) and the preflight job says so.

### Unity license (Unity Personal)

GameCI activates Unity inside the job with the account and returns the seat when the job ends.

1. Use a Unity ID that signs in with e-mail + password (not Google/Apple/Facebook sign-in) and **has two-factor
   authentication off** – headless activation cannot answer a 2FA or new-device challenge. A separate CI-only
   Unity account (free) is recommended; sign in with it once in Unity Hub and accept the Personal license terms.
2. Add `UNITY_EMAIL` and `UNITY_PASSWORD`. That is all a Personal license needs.
3. Unity Pro/Plus: also add `UNITY_SERIAL` (the serial from id.unity.com → Subscriptions).

Unity no longer offers manual (offline, `.alf` → `.ulf`) activation for Personal licenses, so the old GameCI
"activation file" route is gone for Personal: only add `UNITY_LICENSE` (the full text of a `Unity_lic.ulf`) if you
have an Enterprise/Industry offline license. Personal has very few concurrent seats; the workflow never runs two
Unity jobs at once, but do not run other CI on the same account at the same time.

### Android upload keystore

With Play App Signing (the default for new apps) this is the **upload** key; Google holds the app signing key.
Create it once and keep a backup outside the repository – `*.keystore` must never be committed:

```bash
keytool -genkeypair -v -keystore beamrunner-upload.keystore -alias upload \
  -keyalg RSA -keysize 2048 -validity 10000          # prompts for passwords and the certificate name

base64 -w0 beamrunner-upload.keystore > keystore.b64     # Linux
base64 -i beamrunner-upload.keystore | tr -d '\n' > keystore.b64   # macOS
# Windows PowerShell:
# [Convert]::ToBase64String([IO.File]::ReadAllBytes("beamrunner-upload.keystore")) | Out-File -NoNewline keystore.b64

gh secret set ANDROID_KEYSTORE_BASE64 < keystore.b64
gh secret set ANDROID_KEYSTORE_PASS      # prompts
gh secret set ANDROID_KEYALIAS_NAME --body upload
gh secret set ANDROID_KEYALIAS_PASS      # prompts
rm keystore.b64
```

In CI GameCI writes it to `BeamRunner/beamrunner-ci.keystore` inside the job's checkout (never committed) and
passes it to `BuildFromCommandLine`. The same keystore can be used locally in Player Settings → Publishing Settings.

---

## Expected run times and cost

* **Checks** (estimate from local measurements): ~1-2 min with a warm cache (restore ~10 s, setup ~10 s, check
  ~30 s plus runner start-up), ~3-4 min cold.
* **Unity build** (estimates, not measured yet, for the standard `ubuntu-latest` runner of a private repository -
  2 vCPU / 7 GB RAM when this was written):
  image pull 3-6 min per job; tests 20-40 min on the first run (full import), 10-15 min with a Library cache;
  Android IL2CPP .aab 30-60 min first run, 15-30 min cached; iOS Xcode project 20-40 min / 10-20 min. A full run
  (tests + both builds, sequential) is roughly 1-2 hours.
* Private repositories on GitHub Free get 2,000 Linux minutes/month and 500 MB artifact storage: a full Unity run
  uses a large part of that, and an iOS Xcode project artifact can be several hundred MB (artifacts expire after
  14 days; delete old ones under Actions if storage runs out). Larger runners (Settings → Actions → Runners) make
  the Unity jobs much faster; change `runs-on` in `unity-build.yml` to use one.

---

## Running locally

Headless checks (no Unity, Linux/macOS with git, curl, python3; installs a .NET 8 SDK if needed):

```bash
bash tools/unity-compile-check/setup.sh     # first time ~2 min, then a ~5 s no-op
bash tools/unity-compile-check/check.sh     # exit 0 = pass
python3 tools/gen-metas.py --check
```

With Unity 6000.3.25f1 installed (Android / iOS Build Support modules), the same entry points CI uses:

```bash
UNITY=/path/to/Unity   # e.g. "/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity"
"$UNITY" -batchmode -projectPath BeamRunner -runTests -testPlatform EditMode -testResults editmode.xml
"$UNITY" -batchmode -projectPath BeamRunner -runTests -testPlatform PlayMode -testResults playmode.xml
"$UNITY" -batchmode -quit -projectPath BeamRunner \
  -executeMethod BeamRunner.EditorTools.FieldBuildMenu.BuildFromCommandLine -fieldBuild aab   # or apk / ios
python3 tools/check-16kb.py BeamRunner/Builds/Android/*.aab
```

Without `-customBuildPath` the output goes to `BeamRunner/Builds/` like the editor menu. For a local release
build pass `-androidKeystoreName <path relative to BeamRunner/>` and the `-androidKeystorePass`,
`-androidKeyaliasName`, `-androidKeyaliasPass` arguments (or the `ANDROID_KEYSTORE_NAME`, `ANDROID_KEYSTORE_PASS`,
`ANDROID_KEYALIAS_NAME`, `ANDROID_KEYALIAS_PASS` environment variables); `-buildVersion 1.2.3` and
`-androidVersionCode 1002003` set the version for that build only.

---

## Troubleshooting

* **"Unity build skipped"** – no Unity secrets (see Secrets). Nothing is wrong with the code.
* **Activation fails with a credentials / 2FA message** – check the e-mail/password, turn off 2FA for the CI
  account, and look for a "new device" confirmation e-mail from Unity (hosted runners change IP every run).
* **"no available Personal seats"** – a previous job was killed before returning its seat, or another machine
  uses the account. Sign in at id.unity.com and release the seat, then re-run.
* **No space left on device** – the Unity images are 6-9 GB compressed; the jobs already free ~20 GB. Use a larger
  runner if it still happens.
* **GameCI image not found** after changing the Unity version – the preflight job warns; GameCI publishes images
  for new editor versions a few days after release (hub.docker.com/r/unityci/editor/tags).
* **Build fails in "Validation found N error(s)"** – the project validator (BeamRunner → Validate Project) found
  a release blocker; the log lists it.
* **Build fails with "This Android/iOS release build is not ready for the store"** – `FieldMonetizationBuildGuard`:
  `monetization.json` has no LevelPlay app key / ad unit ids for that platform (the build would show no ads) or the
  privacy policy URL is still a placeholder. Fill them in, or set `"allowIncompleteReleaseBuild": true` for a
  test-track build (back to false before the store release).
* **16 KB page-size check fails** – a native plug-in (usually an ad-network SDK) ships a 64-bit `.so` that is not
  16 KB aligned: update that SDK.
* **Checks fail but pass locally** – download the `unity-compile-check-logs` artifact; to reproduce a cold CI run
  locally, point `UCC_CACHE` and `UCC_SRC_ROOT` at empty directories.
