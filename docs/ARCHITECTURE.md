# BeamRunner — Architecture & Contracts

BeamRunner (working title) is a neon, synthwave **3-lane, 3-layer runner where the track runs out and you build the
rest**, for mobile (portrait) with desktop/gamepad support. It is a fork of **LightLancer** (`main`), itself a fork of
CubeField 3D and Neo Slipstream, and shares their infrastructure, world look, ships and skins. The ship rides forward
on **rails of light** — three lanes on each of three layers — and the player swipes left/right to change lane, **taps to
build a bridge** and **swipes up/down to build a ramp** on the next tile, paying with **fuel** collected along the way.
Missing a build drops the ship a layer (a hull point); two layers, the void under the bottom layer and the ceiling over
the top one kill. Each level ends at a **wall**: the track turns up it and the next level climbs it, without stopping (a
perk is picked for the pilot on the way up). Gems buy **ship upgrades** (fuel
tank, build efficiency, starting fuel, power-up time, hull) between runs; power-ups (shield, magnet, Overload, instant
build, repair) and a hidden **adaptive difficulty** rating tune every run. The design doc (`Builder Runner – Design
Doc.docx` at the repository root) is the product brief.

This document is the contract every module is built against. If you change a contract, update it here. Sections 6–7
(Unity modules, game flow) are largely inherited from LightLancer / CubeField 3D; the rules (§§4–5), the track
rendering, the bent track space and the environment are BeamRunner's own (§8).

## 1. Principles

* **Everything is generated in code.** Meshes, textures, materials, particles, audio and UI are
  created procedurally at runtime. The only authored assets are: shaders, two fonts, the main scene
  (one GameObject with `GameBootstrap`), the app icon / promo PNGs and the loading screen's splash art
  (`Resources/BeamRunner/Splash`; all rendered by the Python scripts in `Art/Source~`), and project settings. This
  keeps the project robust (no fragile hand-edited prefabs) and tiny.
* **Pure simulation core.** All game rules live in `BeamRunner.Core` (asmdef,
  `noEngineReferences: true`): level plan, field generation, collision, scoring, power-ups, missions,
  economy, save model. It is deterministic given a seed and fully unit-tested outside Unity.
  The Unity layer only renders state, plays feedback, and feeds input.
* **Pipeline-agnostic rendering.** All shaders are unlit `CGPROGRAM` passes with **no `LightMode`
  tag** so they render in both URP (renders untagged passes as `SRPDefaultUnlit`) and the Built-in
  pipeline. URP (the project default) adds Bloom etc. via a runtime Volume.
* **Graceful degradation.** Missing ads/IAP configuration, missing shaders, missing fonts, missing
  save files, missing URP — every one of these must fall back to something that works, never throw.

## 2. Unity/C# constraints (all runtime code)

* Unity **6.3 LTS (6000.3.25f1)**, URP 17.3, C# **9**, API level .NET Standard 2.1. Platform floor: Android
  API 24 (required by the Google Play Games plugin; target API 36, a Google Play requirement), iOS 15 (built with Xcode 26 / the iOS 26 SDK, an App Store
  requirement).
* **Do not use:** records, `init` accessors, default interface methods, `System.Text.Json`,
  `async void` (except Unity event handlers), `dynamic`, TextMeshPro (no TMP essentials in project),
  legacy `UnityEngine.Input` (use the Input System package), `Object.FindObjectOfType`/`FindObjectsOfType`
  (obsolete in Unity 6 → use explicit references; `Object.FindFirstObjectByType`/`FindAnyObjectByType`
  only if unavoidable), `Rigidbody.velocity` (renamed), `GraphicsSettings.renderPipelineAsset` (obsolete),
  `Resources.GetBuiltinResource<Font>("Arial.ttf")` (removed — use `"LegacyRuntime.ttf"`), physics
  (we do analytic collision), `Camera.main` in per-frame code (cache it).
* The headless compile check (`tools/unity-compile-check`) compiles against Unity 6000.0.58 engine assemblies
  and UnityEditor 2021.1, and classifies uses of 6000.1–6000.3 (and newer editor) APIs as "unity6-only" by
  looking them up in the UnityCsReference 6000.3.25f1 index — reported, not failures. Verify any such API with
  `tools/unity-compile-check/audit-unity6-api.py --lookup Type.Member` and mention it in your report.
* Namespaces: `BeamRunner.Core` (+ `.Core.Audio`, `.Core.Meta`, `.Core.Track`),
  `BeamRunner.Rendering`, `BeamRunner.Audio`, `BeamRunner.UI`, `BeamRunner.Services`,
  `BeamRunner.Game`, `BeamRunner.EditorTools`. One public type per file where sensible.
* Style: 4-space indent, braces on new lines, `_camelCase` private fields, `PascalCase` members,
  XML-doc comments on public types, no `Debug.Log` spam in per-frame paths (use `FieldLog` from
  `BeamRunner.Game`, compiled out of release builds).
* No `.meta` files need to be hand-written except where noted — a generator script
  (`tools/gen-metas.py`) creates them. **Exception:** `GameBootstrap.cs.meta` has a fixed GUID
  `8d61718f508b4ae1bdf7d76af57545dc` because the scene references it.

## 3. Folder layout (`BeamRunner/Assets/BeamRunner/`)

```
Scripts/
  Core/                     asmdef BeamRunner.Core (pure C#, noEngineReferences)
    Field/                  FieldSpec (lanes, layers, tiles), TrackGrid (cells), Levels/LevelSpec/LevelKind (progression
                            + speed curve), Course, Pickup/GemTrail, Chunk/RouteBeats, RouteSolver, TrackGenerator
    Balance/                BalanceConfig (balance.json), ShipStats/UpgradeId, AdaptiveDifficulty/SkillState
    Sim/                    RunSimulation (phases, building, falls, the wall), RunEvent, RunTypes, GhostPath/GhostRecorder
    Meta/                   PlayerProfile, Settings, ShipCatalog, Missions, Achievements, Daily, Leaderboard,
                            DailyRuns/DailyRunState (UTC-day Daily Run), ReminderPlanner, ReviewPromptPolicy,
                            PrivacyRegion, ProfileMerge + WalletLedger + CloudSyncPolicy (cloud save),
                            SaveEnvelope/SaveRecovery/SaveIntegrityKey (signed saves)
    Audio/                  pure DSP synth: music + SFX rendering to float[]  (audio agent)
    Util/                   Rng (seeded), MathUtil, ColorRGB, ZoneThemes
    Diagnostics/            test seam: IGameProbe + GameProbeRegistry, TestLaunchOptions, RunAutopilot bots
    Localization/           Loc, Localizer, StringTable, PluralRules (CLDR), LocFormat, Languages, PseudoLocalizer
  Runtime/                  Assembly-CSharp (no asmdef)
    Boot/                   GameBootstrap (scene entry), GameRoot service locator
    Game/                   GameController (state machine), views that bind sim → scene, InputRouter
    Game/Diagnostics/       GameProbe, AppResetHooks (Editor + development builds only)
    Rendering/              shaders access, materials, meshes, textures, FX, post-FX, camera, ship visuals
    Audio/                  AudioManager (AudioSources, clip creation from Core.Audio)
    UI/Kit/                 fonts, procedural sprites/icons, tweens, widget factory
    UI/Screens/             menu, HUD, pause, game over, hangar, missions, stats, settings, dialogs
    Services/               save storage, ads, IAP, consent, haptics, platform helpers
  Editor/                   Assembly-CSharp-Editor: project setup menu, validators
Resources/
  BeamRunner/Shaders/    *.shader (Resources ⇒ always included in builds)
  BeamRunner/Fonts/      Audiowide-Regular.ttf, Rajdhani-SemiBold.ttf, Rajdhani-Bold.ttf (+ OFL.txt), plus
                            per-script subsets: FieldTitleCyrillic, Tektur-Condensed-*, NotoSans{JP,KR,SC}-*
  BeamRunner/Localization/  <lang>.json string tables (12 languages, identical keys)
  BeamRunner/Config/     monetization.json (ad game ids / product ids; editable without code)
Scenes/Main.unity           the only scene in Build Settings
Tests/EditMode/             asmdef BeamRunner.Core.Tests (NUnit, Core only)
Tests/PlayMode/             asmdef BeamRunner.PlayMode.Tests (UnityEngine.TestRunner + Core only): smoke tests
```

**Test seam.** Test assemblies cannot reference Assembly-CSharp, so PlayMode tests reach the game only through
`GameProbeRegistry.Current` (`IGameProbe`: read-only state + commands that call the same methods as the buttons).
`TestLaunchOptions` redirects the save to a temp folder and forces simulated ads / no store. The probe, the reset
hooks and every `TestLaunchOptions` check are compiled only under `UNITY_EDITOR || DEVELOPMENT_BUILD`; release
builds contain none of it.

## 4. World coordinates & the track (shared by Core and rendering)

* Ride axis = world **+Z** (forward). The craft stays at world `z = 0`; the world streams toward it. Track position `s`
  (metres, `double`) is the distance ridden in this run (`RunSimulation.Distance`, 0 at the start line). An object at
  track position `z` is rendered at world `z - s` (computed in double: `FieldMath.WorldZ`).
* The **track** is a grid of 3 lanes (X: `FieldSpec.LaneX` = −3, 0, +3) × 3 layers (Y: rails at `FieldSpec.RailY` =
  −4.5, −1.5, +1.5; the craft rides `RideHeight` 0.6 m above its rail) × **tiles** of `FieldSpec.TileLength` = 8 m on Z.
  Every (tile, lane, layer) cell is a `CellType`: `Empty` (a gap), `Track`, `RampUp`, `RampDown` (the craft ends the tile
  one layer up/down), `Obstacle` (a 2.4 m cube standing on a rail, centred in its tile) or `Wall` (the whole row that ends
  a level). The ceiling is at `FieldSpec.CeilingY` (+4.5, a layer above the top rail); the void is everything under the
  bottom rail (the floor grid is drawn far below, `FieldMath.FloorY` = −16).
* `TrackGrid` holds a window of `TrackRow`s by tile index (the generator appends ahead, the simulation drops rows
  behind and writes builds into them; `BuiltAt` marks player-built cells for the snap glow).
* **The bend (rendering only).** The simulation is a straight line, but the track is *drawn* bent (`TrackSpace`): each
  level lies flat along +Z in its own frame, and its wall tile is a quarter pipe around an axis at `TrackSpace.PivotY`
  (a metre above the ceiling) after which the next level runs straight up, `PivotY − y` in front of the wall. While the
  craft rides the bend the whole bent space (track, pickups, structure) turns about that axis — the craft and the camera
  stay in their usual frame — and the sky turns with it; at the top the next level becomes the frame. Everything drawn in
  track space goes through `TrackSpace.Map(x, y, s)` under the bend root; world-space things (FX, the ghost) through
  `TrackSpace.ToWorld`.
* The craft is a sphere of radius `FieldSpec.ShipRadius` = 0.5 m. It slides to its target lane with a capped speed
  (`MaxLateralSpeed` 26 m/s): a lane switch takes about 0.17 s.
* **Environment**: a dark beam of the structure (`SurfaceView`, 28 m wide, its face at `FieldMath.FloorY` = −36) runs
  under the rails between two void side walls 28 m high (their rim 3.5 m under the bottom rails) (`WallHeight`: near-black, faint depth bands, a faint rim on
  top) — a tunnel — turns up at the wall around the same axis, walls and all, and rises ahead as the next level's beam; deep space
  around it (`SpaceView`: haze at the far end of the track, a nebula, stars and a beacon star that turn with the world).
  **Fog** is radial and fades everything toward the sky colour of its view direction (`FieldSkyColor(dir)`); the wall
  ahead is fogged less so it is in view from the start of the level. **Near the camera**: `FieldMath.PassedFade`.

### Global shader properties (set by `FieldGlobals` in Runtime/Rendering)

From CubeField 3D: `_FieldColorA/B/C`, `_FieldBaseColor`, `_FieldFogColor`, `_FieldFogParams`, `_FieldLightDir`,
`_FieldScroll`, `_FieldScrollDelta`, `_FieldBeat`, `_FieldBeatRing`, `_FieldSpeed01`, `_FieldFlash`; keyword `FIELD_LITE`,
material keyword `FIELD_DITHER`. BeamRunner's: `_FieldSkyRotation` (world → sky, `SetSkyRotation`),
`_FieldNextColorA/B/C`, `_FieldNextBaseColor` (the next level's palette, worn by the wall ahead, `SetNextTheme`),
`_FieldSurfacePattern` (x = the face under the track, y = the wall ahead; `SetSurfacePatterns`). Rails take the level
palette by layer: bottom = secondary (B), middle = primary (A), top = accent (C).

## 5. Core simulation (`BeamRunner.Core`)

### Levels (`Field/Levels.cs`, the one file to edit for pacing)

A run is a sequence of levels on the course; `Course(startLevel)` maps a run's distance and tiles onto it (start level
picker as in CubeField). Levels last 36 tiles (level 1), 44 (2–4), 52 (5–12) and 60 (13+) — a wall every 15–20 s —
each opening with `IntroTiles` (4; 6 on a run's first level) of plain track and closing with `RunwayTiles` (2) and the
**wall**. The planned speed rises linearly (in distance) from 19.8 m/s at level 1 to 30 m/s at level 13 and stays there;
the adaptive multiplier scales it. Each level has a kind (its banner name and which chunk tags it favours — its
signature tag dominates the weights, so consecutive levels play differently), a chunk tier cap, a chunk gap (plain
tiles between chunks), whether the top layer is open (from level 2) and whether obstacles appear (from level 5,
`Levels.ObstacleLevel`). Each level also looks different: its own palette (`ZoneThemes`) and one of three surface
patterns (`SurfaceView.PatternOf`), both already worn by its wall while the level before is ridden:

| Level | Kind | Teaches | Tier cap |
|---|---|---|---|
| 1 | First Bridges | tap to bridge (middle layer gaps over a bottom safety net) | 0 |
| 2 | Ramp Up | ramps and gem trails between layers (top layer opens) | 1 |
| 3 | The Edges | the ceiling and the void, next to a shield | 1 |
| 4 | Fuel Line | tighter fuel, longer gaps | 2 |
| 5–7 | Blockade, Switchback, Skyline | obstacles force new routes | 2 |
| 8–12 | Gauntlet, Blockade, Switchback, Skyline, Overload | everything | 3 |
| 13+ | plateau cycle: Gauntlet, Blockade, Switchback, Skyline, Overload | | 3 |

### The route and generation (`Field/Chunk.cs`, `Field/RouteBeats.cs`, `Field/TrackGenerator.cs`, `Field/RouteSolver.cs`)

* **A route and its branches.** A level is not the open 3 × 3 grid: it is one guaranteed route (a lane on a layer) with
  **branch rails** beside it (`TrackGenerator.OverlayBranch`: a lane over or a layer up/down, 5–14 tiles, an occasional
  gap, lane drift or ramp, often gems; 12 % of tiles start one in level 1, 30 % later) — never more than three rails at
  a tile, about 1.5 on average. A swipe builds a diagonal into the next lane, so a branch beside the craft is always
  reachable.
  Only the intro (all three lanes of the middle layer: the craft comes over the wall in any lane) and the runway (all
  three lanes of the route's layer) are wider.
* **Beats** (`RouteBeats`, built procedurally for the route's position, `TagOf` / `TierOf`): tier 0 — `Gap` (bridge 1
  tile; in levels 1–2 and the tutorial always, later 60 → 15 % of the time, a **net** one layer down catches a miss and
  ramps back up), `Shift` (the rail moves a lane; two tiles of overlap at tier 0, one later), `Fuel`, `Gems`; tier 1 —
  `ShiftGap`, `RampUp` (the rail goes on a layer up: build a ramp), `Drop` (it goes on a layer down: ramp down for 1 fuel
  or fall for a hull point), `RideRamp`, `Fork` (a side rail with a gap to bridge for gems or fuel), `SkyRun` (top layer
  under the ceiling), `VoidWalk` (bottom layer over the void); tier 2 — `Weave` (two shifts), `Dodge` (a cube on the
  route, a lane beside it); tier 3 — `Combo` (2–3 build/shift beats back to back). Every beat starts and ends with a
  plain tile of route rail. Beats are `Chunk`s (rows of cell codes, never mirrored): `.` gap, `=` rail, `^`/`v` ramp
  up/down, `#` obstacle, `o` optional obstacle, `g` rail + gems, `f` rail + fuel, `s` shield, `r` repair, `p` a
  power-up when one is due (gems otherwise). **Pickups only ride on rails, in clear sight**: never over a gap (where they
  read as track that isn't there), and never on a tile the track doesn't go on from — flat or rising — for
  `TrackGenerator.SightTiles` (2) tiles in that lane, so no gem, fuel cell or power-up stands in front of a gap, a drop, a
  ramp down or a cube and hides it. Rows' pickups are decided once those tiles exist (`Want` → `Finalize`); the solver
  frontier is carried over a row only then, with its real fuel, and a beat's check counts only fuel it is sure to keep.
* **Route solver**: a dynamic program over tiles whose state is (lane, layer, what was built on that cell) and whose
  value is the most fuel a pilot can hold there (−1 = unreachable). Every move is a build on the next tile — a bridge,
  a ramp up or down, or a diagonal into the next lane (free over rail, a bridge's price over a gap, never beside a cube)
  — or riding what is there; gaps, obstacles, the ceiling and the void end a route — a fair route never needs a fall.
* **Stringing** (`TrackGenerator`): every level = intro, then beats picked by the level's tag weights × (1 + 0.4 × tier)
  among those within its tier cap (never the same build beat twice running) joined by 0–1 extra tiles of route rail
  (`ChunkGap` − 1 ÷ the adaptive gap amount), then the runway and the wall. The generator carries the solver frontier
  along and **places a beat only if the route survives it to the beat's exit rail** (the design's "fuel provided before
  a chunk covers the fuel it needs"); below `LowFuel` (6) fuel beats are strongly favoured, and if nothing fits, route
  rail (which can carry fuel) is laid instead.
* **Pickups**: gems are worth `BalanceConfig.GemValue(level)` × the adaptive gem multiplier (stochastic rounding), two
  per gem cell; consecutive gem cells in a lane (across layers) of ≥ 4 gems form a `GemTrail` (all collected = full-line
  bonus). Fuel cells are kept with probability level `FuelScale` × adaptive fuel; plain track carries a fuel cell with
  `FillerFuelChance` or a gem cell. Power-ups keep their own schedule (every
  `PowerUpIntervalMin–Max` planned seconds ÷ the adaptive drops amount; weights shield 26, magnet 20, Overload 18,
  instant build 22, repair 8; no Overload before level 2, no repair before level 3), placed on the route rail.
* Deterministic from the seed (separate streams for layout, items and power-ups). The Daily Run fingerprint is pinned
  in `DailyRunTests` — bump `DailyRuns.GeneratorVersion` when generated tracks change.

### Riding, building and falling (`Sim/RunSimulation.cs`)

* `Step(dt, RunInput)` sub-steps at ≤ 1/120 s. `RunInput`: `LaneShift` (±1 per swipe/press: a switch build on the rails,
  steering in a fall), `Build` (`BuildKind.Bridge` / `RampUp` / `RampDown` / `SwitchLeft` / `SwitchRight`),
  `UseAbsoluteLane` + `AbsoluteLane` (tests; on the rails it builds the switch toward that lane).
* **Phases**: `Riding` (on a rail, or hovering in Overload), `Falling`, `Climbing` (the bend up the wall into the next
  level).
* **Builds** fill the **next tile** in the craft's target lane at the layer it will be on there (a ramp it is riding
  counts), always — whether or not there is a gap — and cost `BridgeCost` 2 / `RampUpCost` 3 / `RampDownCost` 1 fuel
  (× build efficiency, × 0 during instant build). Too little fuel: `BuildFailed` (red flash + "empty" sound), nothing
  built. A build onto an identical piece is wasted fuel (`FuelWasted`). In the `LateBuildSeconds` (0.08 s) after the
  craft rides onto a gap, a build fills that tile instead (a last-second build). Building into an obstacle crashes
  (Overload / a shield break it instead); a ramp up on the top layer crashes into the ceiling when ridden (a shield turns
  it into flat rail); a ramp down on the bottom layer leads into the void.
* **Lanes** change by building: a swipe left/right on the rails builds a **switch** (`CellType.SwitchLeft/Right`,
  `BuildKind.SwitchLeft/Right`) on the next tile — the same one-tile piece a tap builds, only it curves into the
  adjacent lane over the tile (the craft follows the S; `Cells.LaneDelta`), so the ship is always on light and new paths
  are built between beams rather than jumped. Free where it replaces rail; over a gap it is a bridge too (bridge price,
  bridge stats, `Built`); into a cube it crashes like any build. One switch a tile (a second swipe before the craft
  reaches it does nothing); on the field's side, nothing. In the air (falling) swipes still steer toward a lane to land.
* **Falls**: over a gap (after the late-build window) the craft falls under `Gravity` (38 m/s², ~0.4 s a layer). Landing
  on a rail of the layer below costs a hull point (a shield absorbs it; hull 0 = `CrashCause.HullBroken`); passing that
  layer without a rail is death (`LongFall`, or `Void` from the bottom layer). Overload and its `OverloadGrace` (0.8 s)
  hover over gaps instead.
* **Obstacles** crash the craft when it overlaps a cube (cell-based: its lane, its layer's height, the cube's span in the
  tile); Overload, invulnerability (2.5 s after a revive, 0.8 s after a shield hit) or an invincible run smash them into
  rail. A cube passed in a neighbouring lane/layer counts as dodged; one the craft left at the last moment is a near miss.
* **Scoring and chain**: metres × multiplier, gems (10 × multiplier), useful builds (a gap filled, 15 × multiplier), level
  clears (250 × level), full trails, smashes in Overload and **near misses** (last-second builds and last-moment dodges,
  50 × multiplier). Useful builds, full trails and near misses feed the chain: every 6 raise the multiplier (max ×8); a
  fall resets it; 8 s without one drops a level.
* **Power-ups**: shield (until used), magnet (pulls gems and fuel from every lane and the neighbouring layers),
  Overload (×1.35 speed, smashes obstacles, flies over gaps), instant build (free builds), repair (full hull); durations
  × the Power-up time upgrade and the Long Boosts perk.
* **The wall**: entering the wall row (any lane or layer) starts the climb: `WallReached`, then the craft rides the wall
  tile in `ClimbSeconds` (0.5 s, never stopping; `Speed` keeps its value for the effects), drifting onto the middle
  layer; no builds meanwhile. At the top: `LevelCleared` (250 × level), `PerkApplied` — a `CheckpointPerk` picked for the
  pilot (Patch when the hull is damaged, Refuel under half a tank, otherwise in turn Shield (Big Tank if one is up), Big
  Tank +5, Efficiency −15 %, Longer Boosts +25 %) — and `LevelStarted` on the next level's first tile, middle layer.
* **Revive** puts the craft back on a rail (the middle layer after a fall), lays track on its lane for the next tiles,
  clears obstacles on its layer there, restores the hull and at least half the starting fuel.
* `RunAutopilot.FollowLine` plays by the public state (once per tile it picks the lane and build that keep a solver
  route alive with the most fuel, with a pull toward gems and fuel); `SeekCrash` builds ramps down into the void.
* Events add `Built`, `BuildFailed`, `FellOff`, `Landed`, `Damaged`, `Repaired`, `LayerChanged`, `FuelLow`,
  `WallReached`, `PerkApplied`, `OverloadStarted/Ended` to CubeField's set. `RunResult` adds
  `Builds`, `Bridges`, `RampsUp`, `RampsDown`, `GapsBridged`, `FuelCollected/Spent/Wasted`, `EmptyBuilds`, `Falls`,
  `DamageTaken`, `Repairs`, `Overloads`, `TopLayerSeconds`, `Cause`.

### Balance, upgrades and adaptive difficulty (`Balance/`)

* `BalanceConfig` holds every tunable number; `Resources/BeamRunner/Config/balance.json` overrides any field at boot
  (`BalanceLoader`; missing fields keep the defaults, broken values are repaired by `Sanitized`). Design starting
  values: fuel costs 3/2/1, start fuel 12 (raised from the design's 10), max fuel 24, fuel cell 3, hull 3 (cap 5).
* `ShipStats.FromLevels(levels)`: max fuel +2 per Fuel tank level, build cost −4 % per Efficiency level, start fuel +1 per
  Starting fuel level, power-up time +8 % per Duration level, +1 hull per Hull level. Max levels 10/10/10/10/2; prices
  C0 × 1.2^n (C0 60, 90, 50, 70, 900); Duration unlocks at best level 2, Hull at 4.
* `ProfileService.Upgrades.cs`: `BuyUpgrade`, `GrantFreeUpgrade` (one rewarded-ad level per UTC day),
  `CheapestUpgrade` / `NextUpgradeProgress01` (results screen), `Ship` for the next run.
* **Adaptive difficulty** (`AdaptiveDifficulty`, `SkillState` in the profile): a hidden rating −2 … +2 (start 0) picks
  `DifficultyModifiers` from the design table — speed, gaps and layer changes, fuel pickups, obstacles, shield/repair
  drops, ghost-outline lead time, gem payout — interpolated between steps, fixed for the run. After each adaptive run,
  `Performance` (−1..1) compares the level reached with `ExpectedLevel` (3 + 0.12 per upgrade level bought), minus damage
  taken, a revive and wasted fuel; the rating moves by 0.25 × the mean of the last 5 performances, clamped, and two runs
  in a row ending within 15 s drop it 0.25 more. The tutorial plays at −1 and the Daily Run at Standard; neither moves
  the rating. Gem payout rises with the rating, so playing badly never pays better. Edge rules never change.
* Tuning targets (to verify in playtests): the autopilot clears levels 1–4 on every seed at Standard; a new player
  reaches level 2–3.

### Ship unlock pacing (`ShipCatalog`, `ProfileService`)

> BeamRunner: the catalogue, requirements and prices below are CubeField's, unchanged. Gems now also buy upgrades
> (tens to hundreds each), so re-tune ship prices against the shooter's gem income in playtests.

A ship = a requirement (`UnlockKind` + `UnlockValue`: `Rank`, `Level` = best level reached, `Score` = best single-run
score, `Distance` = best single-run distance (unused by the current catalogue), `DailyRunDays` = distinct UTC days with
a counted Daily Run, not a streak; `Gems` = none) **then** a gem `Price`. `BuyShip` returns `Locked` until the
requirement is met, then spends the price. A met requirement is reported once in `RunSummary.ShipsAvailable`
(results screen) by `ApplyRun`/`ApplyDailyRun`; only a price-0 gated ship is added automatically (`ShipsUnlocked`).
Ships owned in older saves stay owned whatever the current requirements.

Inherited from Neo Slipstream's month-long design, re-scaled for this game's income. The prices were set when the
line-following autopilot (which collects every honest gem and no near-miss bonus) earned about 145 gems per minute
of flying (≈400 gems through level 5 in ~3 min; ≈1,000 through level 12 in ~7 min; with the Spiral level ≈840).
Since gems start at level 3 at half that rate it earned about 45–50 per minute (≈130 through level 5; ≈340 through
level 12); with the dense trails (generator version 5) it earns about **70–80 per minute** (≈200 through level 5;
≈550 through level 12, 8 seeds), so the hangar takes roughly 2× as long as first tuned on pickups alone. Generator
version 6 halves the trails through cube fields and the intro spirals (gems placed in levels 3–20 fall from ≈1,420
to ≈910 per run, and the full-trail bonuses with them); prices are unchanged. **Re-measure with real players** before release
(the original was tuned with a scratch income model over daily rewards, missions, ranks and achievements).

| Ship | Hull | Requirement | Price |
|---|---|---|---|
| Arrow, OG Shuttle | | starter | – |
| Manta | manta | – | 3,000 |
| Spark | arrow | reach level 4 | 3,500 |
| Viper | viper | score 25,000 in one run | 4,500 |
| Riptide | manta | pilot rank 5 | 5,000 |
| Halo | halo | Daily Run on 7 days | 5,500 |
| Venom | viper | reach level 8 | 6,500 |
| Eclipse | halo | Daily Run on 14 days | 7,000 |
| Phantom | phantom | pilot rank 10 | 8,000 |
| Wraith | phantom | score 60,000 in one run | 9,000 |
| Aurora | viper | Daily Run on 21 days | 10,000 |
| Solaris | arrow | Daily Run on 25 days | 11,500 |
| Nova | nova | Daily Run on 30 days | 36,000 |

The whole hangar costs 109,500 gems; the 30-day Daily Run gate on Nova cannot be shortcut with gems or purchases.
`ShipUnlockTests` pins the contract (prices climb, the flagship needs 30 Daily Run days and ≥ 35k gems, the hangar
costs ≥ 100k, liveries are unique, level/score/rank/day gates behave). Keep gem-pack sizes (`monetization.json`) in
mind — a pack buys price, never a requirement.

## 6. Module contracts (Unity layer)

### Rendering (`BeamRunner.Rendering`)
* `FieldMath` — `WorldZ(trackZ, shipDistance)` (double subtraction), `CellCenter`, `CellPosition`, `Point`, `WarmUp`,
  `PassedFade(rearZ, frontZ, cameraZ)` (1 → 0 once the craft is through, 0 before the camera arrives); constants
  `HalfExtent`, `CellSize`, `VisibleLength` (190), `FloorY`, `CeilingY`, `DitherNearCamera`.
* `FieldShaders` — finds shaders by name (`"BeamRunner/Surface"`, `"BeamRunner/Sky"`, `"BeamRunner/NeonSolid"`,
  `"BeamRunner/Additive"`, `"BeamRunner/Hologram"`), falls back to built-ins, never null; `IsFallback(name)`. The
  transparent shaders fall back only to `Sprites/Default`; `CanDraw(name)` is false when no fallback would draw them
  correctly (never an opaque shader in place of an additive one).
* `FieldMaterials` — factory + cache: `Surface()`, `Sky(radius)`, `Obstacle()` (palette-driven) /
  `ObstacleDithered()` (the `FIELD_DITHER` fade-out variant) / `Obstacle(ColorRGB/Color edge, float glow)`,
  `Additive(Texture)`, `Glow()`, `Beam()`, `Ship(Color body, Color glow)`, `Hologram(Color[, CullMode])`, `Gem()`;
  `GuardAdditive(renderer)` / `GuardHologram(renderer)` (`forceRenderingOff` when the shader cannot be drawn).
* `FieldGlobals` — static setters for every global property above + `ApplyTheme(theme)` / `LerpTheme(from, to, t)`,
  `SetScroll(distance)` (also exposes `ScrollDistance`), `SetBeat`, `SetSpeed01`, `SetFlash`,
  `SetFog(start, end, endGlow, horizonBand)`, `SetLightDirection`, `SetQuality(0..2)`, `SetReducedFlashing(bool)`.
* `MeshFactory` — `GridPlane(...)`, `SkySphere()`, `AddBox(builder, centre, half, faces)`, `Cube(halfSize)` (cached per
  2 cm quantised size; `PrewarmCubes`), `Box(half)`, `BuildGateWall(...)` / `GateBlockExtents(...)` /
  `BuildOpeningFrame(...)` (merged wall of neon-edged blocks, gaps only between blocked neighbours, + the additive
  outline of its openings), `Octahedron`, `Torus`, `Magnet`, `Bolt`, `Chevron`, `Quad`, `FlareCross`, `Sphere`,
  `Ship(design)` / `ShipInfo(design)`.
* `ProceduralTextures` — `SoftDot`, `Streak`, `Ring`, `Sparkle`, `Noise`, `Beam`, `Trail` (cached `Texture2D`).
* `FxLibrary` — pooled emit-only particle systems: `Explosion`, `Sparkle`, `SmashBurst`, `PowerUpBurst`, `NearMissSpark`,
  `SetTrackSpeed` (particles drift toward −Z with the world), `SetReducedFlashing`, `Clear`.
* `TrackSpace` (static) — the bent space the track is drawn in (§4): `SetFor(sim)` / `Set(distance, inEnd, outStart)` /
  `SetFlat(distance)` once per frame, `ApplyTo(bendRoot)`, `Map(x, y, s)`, `Tilt(s)`, `ToWorld(...)`, `BendAngle` /
  `Bend01`, `WorldTurn` (the sky's turn), `InEnd` / `OutStart` / `NextOutStart`.
* `SpaceView` — world-space environment: the procedural sky (haze at the far end of the track; nebula, stars and a
  beacon turning with `_FieldSkyRotation`; radius `SkyRadius` 900 m, inside the 1000 m far plane) and `SpeedLines` in a
  box volume just outside the rails. `Create(parent)`, `Tick(dt, speed, speed01, overdrive)`, `SetVisible(bool)`,
  `SetQuality(0..2)` (tier 0: fewer streaks; the sky drops a nebula octave under `FIELD_LITE`).
* `SurfaceView` — the structure in track space: the beam under the rails, its turn up at the wall and the next level's
  beam rising ahead, up to that level's own turn (one mesh rebuilt per level). `Create(bendRoot)`,
  `Sync(distance, inEnd, outStart, nextOutStart)`, `SetVisible`, static `PatternOf(level)`.
* `SignageView` — the roadside brand sign (ported from LightLancer): a dark neon-edged board reading "BEAMRUNNER" in
  glowing dot lettering (its own 5 × 7 pixel font, generated texture) on two posts, beside the track and turned 22°
  toward it; placed in track space at Core's `Signs` position of the craft's level or the next (one pooled sign, three
  draw calls in view): `Create(bendRoot)`, `Sync(sim)` after `TrackSpace` is set (`null` hides it: hangar).
* `TrackRenderer` — the rail grid, ghost outline, gap warning, ceiling, obstacle cubes and each level's bend up its wall,
  all in track space (see §8): `Create(parent, fx)`, `Sync(sim, showGhost)` (`null` hides the track), `OnBuilt(world, layer)`,
  `OnSmashed(world, layer)`, `Clear()`; static `LayerSlot` / `LayerColor`.
* `FieldRenderer` — binds the simulation.s pickups to pooled views by id diff (zero steady-state allocation):
  `Create(parent, fx)`, `SetCamera(camera)` (drives the fade-out of passed pickups), `Sync(pickups, shipDistance)`,
  `OnPickupCollected(pickupId)` (idempotent), `Clear()`, `SetTheme(theme)`, `ActivePickupCount` / `CreatedViewCount`.
  Events are dispatched before `Sync`.
* `PickupView` (pooled) — gem crystal / fuel cell / shield torus / magnet / Overload bolt / repair cross / instant-build
  chevron at (x, y, z − s) with a halo; a missed
  pickup shrinks and its halo fades before the camera.
* `PostFx` — runtime URP Volume: bloom, tonemapping (None), vignette, chromatic aberration, lens distortion, colour
  adjustments; `SetQuality(0..2)`, `Pulse(NearMiss | Crash | Overdrive | LevelStart | LevelCleared | Hit)`,
  `SetOverdrive(bool)`, `SetReducedFlashing(bool)`, `ResetPulses()`; no-op if URP is absent.
* `CameraRig` — owns the main camera. `Tick(dt, shipX, shipY, velX, velY, speed01, overdrive)`; `SetMode(Run | Menu |
  Hangar | Crash, instant)`, `Shake(amount)`, `Kick(fovDelta)` (accumulated punch capped at `MaxKick` = 12°); near
  plane `NearClip` = 0.3 m, far plane 1000 m. Run: chase camera following half the craft.s lane offset
  and its height (with more lag on height), `RunBack` 7–7.6 m behind and `RunHeight` 1.5 m above — halfway to the
  layer above, so that layer's rails stay overhead instead of crossing the view (never above the ceiling) — looking
  26 m ahead, `RunLookDrop` 1.1 m below the craft and a little toward the field's centre, up = +Y, roll
  from sideways velocity, small pitch from vertical velocity, FOV kick with speed / Overdrive; portrait keeps ~45–52°
  horizontal FOV. Menu: slow orbit drifting after the attract-run craft. Hangar: three-quarter showcase. Crash: hold,
  then pull back and up.
* `ShipVisual` — builds a craft by `ShipDefinition` (design id + colours), hull scaled to a ~1.3 m wingspan;
  `SetPose(x, y, vx, vy, dt)` (bank from vx, pitch from vy, small yaw), `UpdateTrail(distance, speed)` (history of
  (distance, x, y)), `SetBoost`, `SetShield`, `SetBlink`, `Flash`, `PulseHitRing`, `Explode`, `SetVisible`,
  `SetShowcase`, `Position`; a soft glow on the rail below the craft (`GroundY`) cues its height, and a faint ring of exactly the
  0.42 m collision radius (flaring on near misses) shows the real hit area of the thin hull. Design ids:
  `arrow, manta, viper, halo, phantom, nova, shuttle`.
* `GhostView` — the Daily Run ghost: a translucent hologram of the day's best attempt (visual only: never collides).
  `Create(parent)`, `SetDesign(design)`, `Tick(x, y, worldZ, shipX, shipY, visible, dt)` (dims within ~3.5 m of the
  player's craft, fades out behind it), `Hide()`, `IsVisibleInView`, `WorldZ`.

### Audio (`BeamRunner.Audio` + `BeamRunner.Core.Audio`)
* `SfxId` / `MusicMode` enums live in `BeamRunner.Core.Audio`. The whole bank (3 arrangements as
  sample-locked stems, every SFX, engine + wind loops) is synthesised by `AudioBank.RenderStaged` on
  worker threads at boot — menu music first. Music is loaded in a playback layout: stems the game never fades on
  their own (`MusicLayers.IsFaded`) are pre-mixed into one `MusicStem.Base` clip, so Menu is 1 clip, Run is
  Base (drums+bass+pads) + Arp + Lead, GameOver is 1 clip. ≈43 MiB of AudioClip memory at 44.1 kHz, ≈31 MiB at
  32 kHz (devices with < 3 GB RAM). A new independently faded layer must be added to `MusicLayers`.
* `AudioManager`: `Initialize()`, `IsReady` (menu music + SFX + loops), `IsFullyLoaded`, `LoadProgress`,
  `Play(SfxId, volume, pitch)`, `PlayPanned(SfxId, pan, volume, pitch)`, `PlayGemChain()` (pentatonic
  climb), `ResetGemChain()`, `SetMusic(MusicMode)`, `SetIntensity(0..1)` (Run stems: drums+bass+pads → +arp →
  +lead), `SetOverdrive(bool)`, `SetEngine(speed01, active)`, `SetSlipstream(0..1)` (the airy wind loop, played as the rush of
  Overdrive), `SetDuck(bool)`,
  `SetVolumes(music, sfx)`, `BeatPulse` / `BeatPhase` / `BeatTime` (feed `FieldGlobals.SetBeat`). Wired in `GameController.Audio.cs`.

### UI (`BeamRunner.UI`)
* Kit: `UIFonts` (Title=Audiowide, Body=Rajdhani), `UISprites` (rounded rects, circles, glow,
  gradients), `UIIcons` (procedural SDF icons by `IconId`), `UITween` (unscaled-time tweens),
  `UIFactory` (panels, buttons, labels, sliders, toggles, progress bars, badges, scroll lists),
  `UIScreen` base (Show/Hide with transitions; `Create` builds immediately, `CreateDeferred` builds later —
  progressively via `UIRoot.BuildQueue`, a few ms per frame, or on first `Show`), `UIStack` navigation,
  `Toasts`, `Dialogs`, safe area, `UIFocusRing` (gamepad/keyboard focus frame), `ScrollAwareSlider`.
* Screens bind to `GameController`/`ProfileService` APIs; never hold game rules.

### Localization
* Every player-facing text is a key in the string tables, resolved by `Loc.T(key, ("n", x) …)` (Core, pure C#):
  named arguments, CLDR plural categories chosen by the `n` argument, `Loc.N` for numbers (per-language
  separators, never the device culture), fallback key → English → key. No runtime `ToUpper`/`ToLower` on text.
* `LocalizationService` picks the language before the UI is built (Settings choice stored per device in
  `DevicePrefs`, else the device language); a switch rebuilds the UI via `GameUI` behind a short fade. `UIFonts`
  selects the font set per script; `UITextFit` shrinks single-line labels that don't fit (English keeps its size).
* `tools/check-strings.py` (CI job) guards key/placeholder/plural consistency, forbids user-facing literals
  outside the tables, and checks every character renders with its font; `tools/build-fonts.py` rebuilds the
  font subsets after ru/ja/ko/zh-Hans text changes. Details and glossary: `docs/LOCALIZATION.md`.

### Services (`BeamRunner.Services`)
* `ISaveStorage` (`FileSaveStorage` atomic write + backup; `PlayerPrefsSaveStorage` fallback).
* `IAdsService` — `Enabled`, `RewardedReady`, `InterstitialReady`, `ShowRewarded(Action<bool>)`,
  `ShowInterstitial(Action)`, `SetConsent(bool)`; backed by **Unity LevelPlay** (the legacy direct Unity Ads SDK
  lost monetisation support on 31 Jan 2026); editor simulation when no app key is configured. iOS asks for App
  Tracking Transparency after the in-game consent dialog.
* `IIapService` (Unity IAP 5, Google Play Billing 9) — `Available`, `GetPrice(productId)`, `Purchase(productId, Action<PurchaseResult>)`,
  `Restore(Action<bool>)`, event `EntitlementGranted(productId)`; product ids in `ProductIds`.
* `Haptics` — `Tick/Light/Medium/Heavy` (respect settings), `Platform` helpers (store URL, privacy URL).
* Saves are HMAC-signed envelopes (`SaveEnvelope`/`SaveRecovery`, deterrence, not security): a legacy unsigned save
  is accepted once and re-signed; a tampered main file falls back to the signed backup and is kept as `.rejected`.
* `IOnlineService` (`ServiceHub.Online`, config `Resources/BeamRunner/Config/online.json`; empty ids = off):
  Google Play Games (Android, vendored plugin 2.2.1 in `Assets/GooglePlayGames`) and Game Center (iOS, native
  GameKit bridge `Assets/Plugins/iOS/FieldGameCenter.m`), with Null/Simulated fallbacks. `CloudSync` (Game) runs
  read → `ProfileMerge` (Core, pure, versioned rules) → write, guarded by `CloudSyncPolicy`; gems merge through a
  per-device earned/spent ledger (`WalletLedger`) so they are never duplicated or lost across devices.
* `LocalReminders` (Mobile Notifications 2.5: at most one pending reminder; OS permission only after the in-game
  offer), `StoreReview` (Apple's review sheet; the rating prompt every third launch, `ReviewPromptPolicy`: the game's own dialog on Android, Apple's sheet on iOS), `CrashReporting` (Sentry, consent-gated,
  opt-in by default in Europe via `PrivacyRegion`), `DevicePrefs` (per-device state, never in the profile/cloud).

## 7. Game flow

`Boot → (Consent dialog, first launch only) → Menu ⇄ Hangar/Missions/Stats/Settings → Countdown →
Running ⇄ Paused, Running → Crashed → (Revive → Running) → GameOver → Menu | Retry`. Level walls never leave Running:
the craft rides up them into the next level.

Start level: once a pilot has reached a later level, ◀ ▶ beside PLAY pick the level normal runs start at
(`ProfileService.StartLevel`, clamped to `MaxStartLevel` = best level reached; every level in the Editor and development
builds). The tutorial run and the Daily Run always start at level 1.

Daily Run (after the first run): Menu → Daily Run (today's UTC track; no tutorial, no revive, no rank bonus) →
… → Results (daily best, streak) → Retry stays in the Daily Run; a ghost of the day's best attempt rides along.
After the first daily-reward claim the menu offers daily reminders once (the OS prompt only on "Remind me").

Boot: frame 1 = profile/services, start of the audio worker, UI root + splash only; frame 2 = the world
(space environment, track and pickup renderers, camera, post-FX, ship); then services and the attract run (the menu
background: an invincible autopilot riding a random level 1–12 and up its walls, restarted
every 1.5 km). The other screens are built behind the splash while the audio renders (menu first), and icons are
painted on a worker thread.

App pause/focus loss during a run auto-pauses. Interstitials only between runs (never before run 4,
at most every 3rd game over, ≥ 120 s apart, never if "Remove Ads" owned).

## 8. BeamRunner additions

* **Track rendering**: `TrackRenderer` draws every rail (a flat ribbon plus a vertical fin), ramp, tile tick, the
  ceiling's hazard line and stripes, the **ghost outline** on the build target (pulsing on a gap, dim on rail, red when
  even a ramp down is unaffordable), the orange **gap warning** within the adaptive lead time and each wall tile's
  **bend** (every rail curving up, a bright bar where the turn starts), in one dynamic additive mesh in `TrackSpace`
  (pieces on a bend are split in 8; switches curve over their tile in 6); built pieces grow over `SnapSeconds` (0.2 s)
  with a white-hot glow; bright caps mark where a rail ends or starts; a **ramp** wears the colour of the layer it leads
  to and carries chevrons on both sides pointing up or down (`RampArrows`, lit in turn in the direction of travel); tile ticks only on the craft's layer; the ceiling's
  diagonal stripes only from the top layer. Obstacle cubes are pooled `Obstacle`-material renderers. The layer the craft
  rides is brightest, the others at 60–65 %; the layer above (and the ceiling) dims to 45 % overhead and is full from
  18 m ahead. The next level's rows up the wall wear its palette. `FieldRenderer` binds pickups only
  (gem, fuel cell, shield, magnet, Overload bolt, repair cross, instant-build chevron), placed in `TrackSpace`;
  `CameraRig` follows the craft's height with more lag than its lane (a drop reads as a fall) and never rises above the
  ceiling; `ShipVisual.GroundY` puts the glow pool on the rail under the craft.
* **Environment** (`SurfaceView`, `SpaceView`, Surface and Sky shaders): BeamRunner's own look (CubeField's grid floor,
  ceiling grid, light arches and synthwave sun are gone): rails over a dark beam with glowing edges, lane guides with
  light pulses racing toward the wall, faint seams (panels, ribs or diamonds by level) and sparse lit windows; the next
  level's beam rises ahead in its own palette and pattern; deep space around with a haze where the track converges. The
  theme cross-fades to the next level's over the bend (`ClimbSeconds`), so it matches the wall as the wall becomes the
  track.
* **Input** (`InputRouter`): `ControlMode.Swipe` (default): swipe left/right = a diagonal into the next lane on the next tile, swipe up/down = ramp, tap (a touch
  that barely moved, ≤ 0.3 s) = bridge; a swipe travels 10 % of the screen width (÷ the sensitivity setting) and does
  exactly one thing however far the finger slides; every finger is its own gesture. `ControlMode.TapSides`: taps on the outer thirds
  move a lane, the middle bridges. Keyboard A/D ←/→ lanes, W/↑ S/↓ ramps, Space bridge; gamepad d-pad/stick and south
  button. *Invert up / down* swaps the ramps.
* **HUD**: the **layer gauge** at the left edge (`HudScreen.Layers.cs`: three bars in the layers' rail colours, the
  craft's lit; a red ceiling line that throbs on the top layer; the layer below blinks in a fall), fuel gauge (value,
  low-fuel and free-build states, red shake on an empty build), hull hearts, chips for
  shield / magnet / Overload / instant build, "NO FUEL", "HULL HIT" and fuel popups, the level banner and coaching hints
  (bridge, ramps, fuel, edges, obstacles).
* **Level walls** never stop the run: the perk is picked by the simulation (above) and named in a HUD popup under the
  level-clear bonus (`HudScreen.Fx`, `PerkApplied`). The old checkpoint screen and its rewarded "take all three" are
  gone.
* **Upgrade shop**: `UpgradesScreen` (menu tile UPGRADES with an "affordable" badge, and from the results screen), daily
  free level via rewarded ad (`GameController.FreeUpgradeWithAd`). The results screen shows the next-upgrade bar.
* **Audio**: no new synthesis; builds play a pitched `ShotPing` + `BlockHit` "snap", empty builds `UiError`, falls and
  damage `ShotPing`/`BlockBreak` low, the wall `OverdriveReady` (see `GameController.Audio.cs`).
* **Background (footage) mode**: build with the scripting define `BEAMRUNNER_FOOTAGE` (`FootageMode.Enabled`): runs are
  ridden by the autopilot, never crash, and the HUD shows only a "BEAMRUNNER" watermark.
* **Roadside brand sign** (`Core/Field/Signs.cs`, `SignageView`): scenery the craft passes once per level (96 m after
  its start, alternating sides) in every run, the attract run and footage builds, so clips show the name inside the
  world rather than as a croppable overlay. Outside the lanes; never collides.
  Never ship a store build with it.
* **Not built yet** (after the MVP per the design doc): the emergency-fuel rewarded ad, the Scanner upgrade, a visible
  "Heat" meter, the "quit right after a death" rage signal, ring-segment obstacles, BeamRunner store feature graphics
  (`Art/Promo` is still LightLancer's; the app icon, adaptive layers and splash art are BeamRunner's, rendered by
  `Art/Source~/make_icon.py`, `make_adaptive.py`, `make_splash.py`), dedicated build/fall sound effects, and real-player
  tuning of every number above.
