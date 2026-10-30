# First open in Unity — checklist

## 0. BeamRunner (lane shooter) — what was verified and what to look at first
Forked from CubeField 3D `main` on 2026-10-29. Verified on this machine with Unity 6000.3.25f1 in batchmode: the whole
project compiles with 0 errors / 0 warnings, all 314 EditMode tests pass (chunk fairness, generator, combat, upgrades,
adaptive difficulty, saves, merge, localization) and all 15 PlayMode smoke tests pass (boot → menu → runs with the
autopilot → crash / revive → results → Daily Run). A few gameplay frames were rendered offscreen to check the camera and
block sizes. **Not yet checked by a person:**
- [ ] Feel: swipe threshold, lane-switch speed (0.17 s), camera height/FOV (`CameraRig.RunPose`, `BaseFov` 42°).
- [ ] Combat readability: bolts, hit flash, damage glow, armored hazard stripes, block-break SFX (rate-limited).
- [ ] Balance: runs of 1–3 min early, one upgrade every 1–3 runs (`balance.json`; ship prices are still CubeField's).
- [ ] Upgrade shop layout on a phone (portrait) and in landscape; the results screen's next-upgrade bar and button.
- [ ] Footage build (`BEAMRUNNER_FOOTAGE` define): autopilot, no crashes, only the watermark on screen.
- [ ] Non-English tables still call the currency by CubeField's word (Splitter, fragmentos, éclats …); English says gems.
- [ ] Store identity: new bundle id (`com.cddevs.beamrunner`), LevelPlay app keys / ad units and iosAppId were blanked
      (CubeField's), product ids are `beamrunner.*`; the privacy/terms URLs in `monetization.json` are still CubeField's.

The rest of this list is CubeField 3D's and still applies to the shared infrastructure.

BeamRunner was forked from Neo Slipstream and rebuilt **without a Unity Editor**: all C# compiles with 0 errors and
0 warnings against real Unity 6000.0.58 player assemblies, real URP/Core 17.3.0, Input System 1.20.0 and IAP 5.4.3
sources, with every newer API checked against Unity's 6000.3.25f1 C# source, and the Core NUnit tests pass (the field
generator's fairness and a bot surviving every level are among them). The infrastructure (UI kit, services, saves,
audio) is Neo Slipstream's, which was itself only ever compiled and reviewed, never run; the **new parts — the field
rendering, the chase camera, the 2D controls, the HUD level banner and the start-level picker — have never been seen
on a screen**. Expect visual tuning (camera distance, sizes, colours, densities). LevelPlay 9.5.1 was compiled against
a signature stub, so it is the least-verified integration.
Work through this list once; each item names what to look for.

## 1. Import & setup (≈10 min)
- [ ] Install **Unity 6000.3.25f1** (Unity 6.3 LTS) with Android Build Support (+ OpenJDK, SDK & NDK) and iOS
      Build Support, then open `BeamRunner/`. Expect a full reimport (the project moved from 6000.0 to 6.3 and
      switched to Linear colour space) and the Package Manager resolving the new packages (URP 17.3, Input System
      1.20, IAP 5.4.3, LevelPlay 9.5.1, iOS 14 Advertising Support). Accept any automatic package upgrades Unity
      offers for its built-in core packages.
- [ ] Console after import: **no compile errors**, and no shader errors for `BeamRunner/*` shaders
      (also after switching the build target to Android and iOS).
- [ ] Console after the LevelPlay import: accept the **External Dependency Manager / Mobile Dependency
      Resolver** prompts; *Ads Mediation ▸ Network Manager* opens and lists Unity Ads (bundled in the "LevelPlay SDK" section).
- [ ] Run **BeamRunner ▸ Setup Project** (should report nothing or only a few changes), then
      **BeamRunner ▸ Validate Project** (warnings expected: empty LevelPlay keys / ad units, placeholder
      URLs, keystore). *Player ▸ Android*: Target API Level 36, Minimum API Level 23; *Player ▸ iOS*: Target
      minimum iOS 15.0.
- [ ] *Window ▸ General ▸ Test Runner ▸ EditMode* → run all: every test passes (the same suite `check.sh` runs).
- [ ] *Test Runner ▸ PlayMode* → Run All: every test passes (8 smoke tests + the save-format tests) (~3–5 min; hands off mouse, keyboard and
      gamepad). They boot the real Main scene with a throwaway save in the temp folder (your save and settings
      are never touched), simulated ads and no store, and drive splash → consent → daily reward → a tutorial run
      flown by an autopilot → crash → revive offer (NO THANKS and a simulated ad) → results → menu, pause /
      settings / back / resume, every meta screen, a second launch and a save reload — failing on any Console
      error. Each failure message ends with the probe's `state=…` summary. If a run hangs before the first test,
      check that *BeamRunner ▸ Play Mode Starts From Main Scene* is not forcing the scene (it should step
      aside for the Test Runner).
- [ ] *Project Settings ▸ Audio*: DSP Buffer = Good latency. *Graphics*: PC_RPAsset assigned.
      *Player ▸ Icon*: default icon + Android adaptive layers (background/foreground not swapped).

## 2. Press Play (Game view 1080×1920 portrait, then 16:9 landscape)
- [ ] Splash → **consent dialog** (both buttons equally styled and visible) → menu with music within ~4 s,
      clean Console.
- [ ] **Daily reward** popup appears after consent; CLAIM visible; the menu behind is dimmed and locked.
- [ ] Menu background: the autopilot flies a random level (cubes or gates) through the field; bloom visible; cubes
      and gate walls are not inside-out; obstacles fade in from the fog instead of popping in at ~190 m.
- [ ] Main menu layout in both orientations (logo not behind PLAY in landscape).

## 3. A run
- [ ] First run is the tutorial: coaching cards appear (fly — per control mode —, gates, Overdrive, near miss).
- [ ] **Camera and scale** (never seen on a screen): in portrait the whole 9×9 gate ahead is visible from far away
      and you can tell which square is open in time; the ship stays on screen at all four field edges; the field
      boundary reads (frame / floor grid lanes line up with the gate cells); the ship looks about two-thirds of a
      cell wide. Tune `CameraRig` (distance, follow, FOV) and the ship scale first if not.
- [ ] **Controls**: drag (mouse drag in the Editor) moves the ship like a trackpad on both axes at the same
      sensitivity; W A S D / arrows, gamepad stick; holding the mouse through the 3-2-1 flies smoothly from GO.
      Settings: STICK shows a ring + knob under the finger on a device (and steers by deflection), TILT works on both
      axes after re-centring, *Invert up / down* flips all of them. At sensitivity 1.0 a drag across about 60 % of the
      screen width crosses the field (`InputRouter.DragMetresPerScreenWidth` = 28); a fast flick keeps at most
      `RunSimulation.MaxLead` (4.5 m) ahead of the ship, so judge whether quick dodges feel short and tune both
      together. Swap thumbs mid-drag (put the other thumb down, lift the first): steering continues with the second
      thumb; a thumb held on PAUSE / OVERDRIVE never steers.
- [ ] **Levels**: start runs at levels 1–14 from the ◀ ▶ picker beside PLAY (all levels are pickable in the Editor):
      the level banner ("LEVEL 4" + name) shows in the obstacle-free intro, "LEVEL n CLEAR" + bonus at the end; the
      level-progress bar under the score fills; each level matches `docs/ARCHITECTURE.md` §5 (cubes / open gates /
      tumbling cubes / spiral / drifting cubes / single-entrance gates / debris / sliding gates / crossfire / sliding
      needle / gauntlet / wandering hole), the palette changes per level, and the hole of a gliding-hole gate visibly
      settles before you reach it. Difficulty should feel steady from level 13 on. Levels 1–4 are a quarter shorter
      (26–27 s) and every level's intro / exit gap is a third shorter (1.9 s / 0.8 s): the level banner and "LEVEL n
      CLEAR" must still read in the shorter gaps. In levels 1 and 3, hugging the floor (or any edge) should no longer be
      a free ride: cubes sit along the edges too.
- [ ] **Spiral** (level 4): every wall blocks about three quarters of the field from a random side (never the same
      side twice running), leaving a 2-cell strip along the opposite edge; dashing to the opposite edge in time should
      feel tense but fair (walls now about 1.9–2.0 s apart, down from 2.4 s: each one just as far as its dash needs at a
      9.2 m/s fair speed, `LevelSpec.DashScale`). If it is too much, lower `DashScale` toward 1 in `Levels.Build`. The wall's inner edge gets the opening highlight; skimming it should give
      gate near misses (Overdrive charge). Tuning: `Levels.Build` (`GateInterval`, `SideWallCells` = 7). The level is
      still named SPIRAL though it no longer turns round: rename `level.spiral` in the 12 tables if it reads wrong.
- [ ] **Edge fence** (level 5, Drift, and its plateau repeats; new without an Editor): during the level banner a red
      ring-and-rail fence closes in from the field's sides to 1 m inside them, pushing a craft at the edge inward;
      about 0.7 s later it starts to throb (steady with reduced flashing) and touching it crashes (a shield bounces you
      back in). Check that it reads as "don't touch" without cluttering the view (the regular white frame dims while it
      stands), that it flares as you near it (now from 4.5 m: a tester flying low died "out of nowhere" on the bottom
      bar, so make sure a floor-hugger sees it coming), that the bottom bar floats clearly above the floor, and that it
      opens again at the level end. Tuning: `LevelSpec.EdgeInset`, `Course.EdgeClose*/EdgeArmAt`, `SpaceView` (`FenceColor`,
      `FenceWidth`, `FenceWarnDistance`).
- [ ] **Fairness by eye**: every gate has a reachable opening; drifting cubes never close the only way through; the
      gem trails lead through openings (after the 4th run some lead into the wall: lures).
- [ ] **Gems and power-ups** (changed without an Editor): no gems in levels 1–2, dense gem trails (3 m apart)
      from level 3 on about 29 % of segments; collecting a whole trail shows "FULL TRAIL +n", a gold popup with a score
      punch, an accent burst off the craft, a light haptic and the multiplier sting a fifth up — check it feels
      rewarding and that the sting sits well on top of the gem chain. Watch the gem draw calls (2 per gem) in
      debris levels on a low-end phone. Power-ups about once a level from level 1, alone on the line (also between gates and among
      cubes, never inside a wall). From the 5th run, level 6 on: now and then a second (from level 9 a third) trail
      forks off or crosses the line's trail and runs into cubes or a wall — check it reads as a plausible trail, that
      the gems inside a cube or wall look acceptable, and that the magnet still pulls rivals in from the line. Tune
      `Difficulty.RivalChance` / `FirstGemLevel`, `FieldGenerator.TrailStartChance` if it feels too sparse or mean,
      and re-check gem income against ship prices (ARCHITECTURE, ship unlock pacing).
- [ ] HUD: score, "LEVEL n · m", level bar, multiplier pips, Overdrive meter and button (try both sides in
      Settings); rotate the Game view mid-run — the meter must not overlap the button.
- [ ] Overdrive (charged by near misses, gates, level clears; Space / button): speed-up, bloom punch, cubes and gate
      walls smash, lead synth line comes in on the beat, the wind loop rushes.
- [ ] Colours in Linear space: ship trails, the magnet pickup and particle tints match the glow tints
      (particle colour conversion is the one unverified assumption).
- [ ] Pause (Esc): fits the screen, shows "LEVEL n · distance", button row centred; Settings from Pause and back
      returns to Pause; music ducks while paused.
- [ ] Crash: slow-motion, explosion, revive offer fits; the simulated ad works; press Esc during the ad on
      the Results "DOUBLE" button — it must be ignored; Results scrolls (stats: level, gates, cubes dodged…);
      RETRY / MENU.

## 3a. Rendering (rebuilt for open space without an Editor — never seen on a screen)

What was verified without Unity: all C# compiles (0 errors / 0 warnings, Android, iOS, editor, development player);
the five shaders' CGPROGRAM bodies (every keyword variant: grid with / without `FIELD_LITE`, NeonSolid with / without
`FIELD_DITHER`) pass a Slang front-end type check against a stubbed `UnityCG.cginc` (catches undefined identifiers and
type errors, not Unity's own HLSL / GLSL / Metal compilation); the chase-camera framing was checked numerically (craft
and gate corners projected for 9:19.5, 9:16 and 16:9); the fog maths (obstacles at full fog = the floor / sky behind
them) and the sun's position outside the far field's footprint were worked out by hand. Nothing was rendered.

- [ ] **Shaders compile in Unity**: no errors or warnings for `BeamRunner/Grid` (both variants), `BeamRunner/Sky`,
      `BeamRunner/NeonSolid` (both variants — `FIELD_DITHER` is a `multi_compile_local`), `BeamRunner/Additive`,
      `BeamRunner/Hologram` — also after switching to Android (GLES3 and Vulkan) and iOS (Metal). `FieldCommon.cginc`
      changed for every shader (no bend; radial fog; `_FieldLightDir`; `FieldSkyColor` / `FieldFogOpaque`).
- [ ] **Floor grid**: looking straight ahead from the centre, the lane lines (x = ±1, ±3 … ±9 m) run exactly through the
      gaps between a gate's blocks; the field's side rails (x = ±9) are brighter and carry light pulses; no moiré or
      shimmer far away at grazing angles; the 4 m / 20 m cross lines never appear to crawl backwards at top speed in
      Overdrive, at 60 and at 30 fps (motion blur from `_FieldScrollDelta`).
- [ ] **Horizon**: the floor melts into the fog with a glow and the sky's horizon colour matches it with **no visible
      seam or floor edge**, in every level palette (levels 1–7 cycle) — fly in the bottom row too (the floor's far edge is
      then only ~1° below the horizon; the sky's band starts exactly at the horizon so that strip must match the floor),
      and watch the Menu camera swing sideways.
- [ ] **Sky**: stars above the horizon, twinkling but not busy; no seam directly behind the camera (azimuth wrap of the
      star grid); the synthwave sun sits low **to the left** (16° azimuth, 7° up — moved off the vanishing point so far
      gates never sit on it as dark squares), fully visible in landscape and in portrait (except when the craft is at the
      far left and the camera yaws right) and does not wash out the field (tune `_SunIntensity` / `_SunSize` on
      `FieldMaterials.Sky`). The sky never shows as a small ball (that would mean the Sky shader fell back) and is never
      clipped by the far plane (300 m; sky radius 265 m).
- [ ] **Ceiling**: a dim see-through grid 3 m above the field; does not crowd the view with the craft at the top edge;
      Y-sliding walls whose margin rows rise above the field look acceptable through it; quality LOW hides it.
- [ ] **Play-area frame**: the light arches every 20 m sit exactly on the floor's 20 m major lines (watch a long run —
      no drift), fade out before they pass the camera, and the top-corner rails do not streak distractingly while flying
      in the top corners. The field boundary reads at a glance.
- [ ] **Speed lines** appear only outside the field (left, right and above), never inside the play area or under the floor.
- [ ] **Fog-in, no silhouettes**: new walls and cubes spawn ~197 m from the camera; there they must be invisible against
      the fogged floor, the horizon band and the sky (NeonSolid fades toward the same colours: `FieldFogOpaque`) and
      appear gradually — never as dark squares or dots on the glowing horizon, in every palette. Pickups too.
- [ ] **Gate walls**: blocks are not inside-out; the 0.14 m gaps between neighbouring blocks read as a grid, while the
      sides of an opening are flush with the true cell boundary (an opening is drawn exactly 2 m wide, as it collides);
      7 cm neon edges look right near and far; sliding walls show their margin blocks sliding in from outside the field;
      edges warm up as walls approach (190 → 120 m).
- [ ] **The camera never flies through anything**: after threading a single-entrance hole (levels 6, 10, 12), ducking under
      or beside a cube, or missing a power-up by 1.5–2 m, the wall / cube dissolves (screen-space dither) within a few
      metres behind the craft and is gone before it reaches the chase camera — no screen-filling dark face, no
      full-screen halo flash; the craft stays visible. Check the menu's orbit camera too (it sits beside the craft:
      obstacles near it dissolve when closer than ~3.5 m). The dither should read as a quick dissolve, not as noise; it
      uses the `FIELD_DITHER` variant only while fading.
- [ ] **Gate room (tilt)**: with tilt steering, levels 2, 4, 6, 8, 10, 11, 12 feel passable — no opening narrower than
      2 × 2 cells, a beat to line up between gates; Overdrive (8 s) feels like a reward, not a blip. If gate levels
      now fill the meter too slowly (fewer gates, fewer accidental gate near misses), raise `GateCharge` /
      `NearMissCharge` in `RunSimulation`.
- [ ] **Visual = collision**: fly slowly through single-entrance (2 × 2) openings (levels 6, 10, 12) and graze block edges — near
      misses, crashes and smashes happen exactly where the drawn blocks are, for static, sliding and gliding-hole walls and
      for spinning / drifting cubes (the renderer poses everything on `sim.GateClock`; `Quat` → `Quaternion` convention).
- [ ] **Opening highlights**: the frame around each opening is readable from ~150–180 m (it fades in before the wall
      does), outlines a 2×2 window as one window, follows a gliding hole, and fades out 14 → 4 m before the craft so it
      never flashes across the camera at the moment of passage.
- [ ] **Gliding hole** (level 12, new without an Editor): the wall never moves; a 2 × 2 hole slides smoothly from cell
      to cell inside it (easing in and out of each cell, mostly straight on), the blocks on its edges cut back to the
      hole's edge as it passes with clean edge glow on the cut faces, and its highlight frame moves with it; no blocks
      fly away or slide in, and no flicker while the wall mesh is rebuilt every frame of the glide (`MeshFactory.
      BuildGlideWall`); the hole settles (locks) before the craft arrives; these walls show diagonal accent hazard
      stripes. It should now read as predictable: you can follow the hole and line up early. Tuning: `HopSeconds`,
      `HopSteps*` in `Levels.Build`, `HoleStraightChance` in `FieldGenerator`.
- [ ] **Cubes**: edge glow width is the same on 0.8 m and 4 m cubes (cached mesh per 2 cm size; the residual ≤ 1 % scale
      is invisible); static cubes vary between the primary and secondary neons, drifting / spinning cubes use the accent
      and read as "moving".
- [ ] **Smash** (Overdrive / shield / revive): bursts go off at the wall blocks nearest the craft (4) or at the cube
      (3 for big cubes), then the obstacle disappears; a debris storm smashed in Overdrive does not exhaust the spark
      emitter (900 particles) or stutter.
- [ ] **Near miss**: sparks on the craft's flank facing the grazed block / cube, that obstacle flashes briefly (dimmer
      with *Reduce flashing*) and the flash fades even if you crash right after (it decays on frame time, not on the
      frozen obstacle clock).
- [ ] **Collision-radius ring**: a faint ring of exactly the 0.42 m collision radius around the craft (it does not roll
      with the hull) flares on a near miss. The hull is ±0.12 m thick but collision is a 0.42 m sphere: check whether the
      ring makes vertical grazes feel honest without cluttering the view; set `ShipVisual.HitRingIntensity` to 0 to keep
      only the near-miss flare, or remove the ring.
- [ ] **Revive in a debris storm**: the revive smashes 15–20 obstacles in one step — the FOV punch stays ≤ 12° and the
      shake / haptic fire once, not once per obstacle.
- [ ] **Craft**: wingspan ≈ 1.3 m (two-thirds of a cell) for every design; banks into sideways moves, pitches with
      vertical moves; the flat trail ribbons read from the chase camera and follow the X/Y path smoothly; the soft glow on
      the floor below the craft is a useful height cue (bright and tight near the floor, faint at the top); shield bubble
      and engine flares scale with the hull.
- [ ] **Daily Run ghost**: banks / pitches with its own motion, dims within ~3.5 m of the player's craft, fades out when it
      falls behind (before it can fill the camera).
- [ ] **Camera — Run**: portrait 1080×1920 and a 9:19.5 phone: the craft sits ~40 % up the screen when centred and stays
      on screen at all four field edges and corners (expected ≈ 20–80 % across, 27–54 % up); a gate 25 m ahead fills
      roughly the middle two-thirds of the width. Landscape 16:9 too (craft lowest at ~14 % up in the bottom row — check
      it is not under the HUD). Roll / pitch from velocity are not nauseating; FOV kicks with speed and Overdrive.
      Tune `CameraRig.RunFollow` (0.85), `RunLookFollow` (0.65), `RunLookAhead` (27), back 7.5 m / height 1.45 m.
- [ ] **Camera — Menu / Hangar / Crash**: the menu orbit drifts after the autopilot craft and never dips through the floor
      or the ceiling; the hangar three-quarter view frames the craft at the field's centre; the crash camera pulls back
      and up and stays under the ceiling even after a crash in a top corner.
- [ ] **Levels**: the palette cross-fades over ~2 s when a level starts (bloom pulse), "level cleared" gives a small
      flash + accent burst at the craft (much dimmer with *Reduce flashing*).
- [ ] **Linear colour space**: fog / sky / grid colours match the level palettes (globals are converted by hand); trail
      and frame vertex alphas look right.
- [ ] **URP settings the shaders rely on**: keep *Depth Priming* **disabled** on the renderers (the untagged unlit shaders
      have no DepthOnly pass). Frame Debugger: the Sky draws in the opaque pass **after** the floor, walls and cubes
      (queue 2450) and before the transparent pass (additive ceiling, frame, highlights, particles).
- [ ] **Depth precision on GLES3** (Android is GLES3-only with a 24-bit, non-reversed depth buffer): the opening highlights
      (10 cm in front of the wall, near plane 0.3 m) do not speckle or shimmer at 150–190 m on an Android device. The
      near plane change must not clip the craft, the hangar close-up or the crash camera.
- [ ] **Fallbacks**: temporarily rename `FieldSky.shader` → no sky, the camera clears to the fog colour, no errors;
      rename `FieldGrid.shader` → a plain dark floor, no ceiling, no errors; rename `FieldAdditive.shader` → glows,
      particles, trail, frame and highlights become soft white `Sprites/Default` sprites (or disappear if that shader is
      not in the build) — **never opaque rectangles**; rename `FieldHologram.shader` → a faint see-through shield / ghost.
      Restore all of them. (`Sprites/Default` should be in *Graphics ▸ Always Included Shaders*: check.)
- [ ] **Track look**: rails of the layer you ride are brightest; built pieces snap in with a white glow; the ghost
      outline sits on the next tile and pulses on gaps; the orange warning marks the next gap; the ceiling stripes read
      from the top layer; obstacle cubes shrink away once passed; the wall rises from the void to above the ceiling.

## 4. Meta screens
- [ ] Hangar: all 14 ships (owned / buy / locked states) — the seven skins (Spark, Riptide, Venom, Eclipse,
      Wraith, Aurora, Solaris) must read as distinct from their base hull on the turntable and in a run (the pale
      Wraith/Solaris bodies too), and the 14 page dots must fit the card in portrait and landscape; a locked ship shows its requirement, the progress text
      ("31,200 / 50,000", "3 / 7") and "THEN … GEMS" under LOCKED without clipping — check German, Polish and
      Russian too; a run that meets a requirement shows the "NEW SHIP AVAILABLE" card on the results screen.
- [ ] Hangar: the **OG Shuttle** is now procedural (the licensed model was dropped) — check it reads as a shuttle.
      The 3D ship must sit above the bottom card. Locked ships show "REACH LEVEL 4" / "LEVEL 2 / 4" (Spark, Venom).
- [ ] Gem store (store unavailable is expected in the Editor without IAP setup), Missions, Records,
      Settings — with a gamepad too (focus should start on the back button).

## 4a. Daily Run, reminders, reviews, online (new — changed without an Editor)
- [ ] Packages resolve, including Sentry (a git URL: needs git on the machine) and Mobile Notifications 2.5.0
      (fallback 2.4.3); *Project Settings ▸ Mobile Notifications*: iOS "Request Authorization on App Launch" off,
      Android small icon `field_notification_small`. *Tools ▸ Sentry* keeps Options Config =
      `FieldSentryOptionsConfiguration`. The vendored `Assets/GooglePlayGames` imports without errors.
- [ ] Daily Run generator version 5 (2: roomier gates; 3: the Spiral level; 4: gems from level 3, power-ups on their
      own schedule; 5: three-quarter Spiral walls from random sides, edge fence in Drift levels, dense trails; older
      ghosts are ignored): when a change to `Levels`/`FieldGenerator` alters generated
      fields, bump `DailyRuns.GeneratorVersion` (`DailyRunTests` fails until you update its golden fingerprint).
- [ ] DAILY RUN appears on the menu after the first flight, with a ticking countdown. First attempt: no revive
      offer; Results show the DAILY RUN card and the reward. Second attempt: a pale ghost ship flying your best
      attempt's path in the field, and the HUD ghost chip. *Settings ▸ Daily Run ghost* off hides both. Setting the device clock back a
      day shows "PRACTICE ONLY" on the menu and "NOT RECORDED" on the results.
- [ ] First launch: the consent dialog has the "Send crash reports" switch (off in Europe, on elsewhere).
      After the first daily claim the menu offers DAILY REMINDERS once; the OS prompt appears only after REMIND ME.
- [ ] **Rating prompt** (new): at the 3rd, 6th, 9th … launch (cold start) the menu, about 2 s after it appears and
      after the daily reward / reminder offer, asks for a rating. Android / Editor: the game's dialog RATE BEAMRUNNER
      with RATE NOW (opens the store page; never asked again), NOT NOW (also back; asked again 3 launches later) and
      DON'T ASK AGAIN. iOS: no game dialog, Apple's review sheet instead. Never during a run; a launch after an app
      crash is skipped. To test again, clear the app's data (PlayerPrefs keys `BeamRunner.Review.*`).
      *Settings ▸ Support ▸ RATE BEAMRUNNER* still opens the store page (hidden without a store link).
- [ ] With `online.json` empty: no account/cloud cards, no leaderboard buttons, nothing logged as an error.

## 4c. Languages (new — changed without an Editor)
- [ ] *Settings ▸ LANGUAGE*: pick FRANÇAIS, РУССКИЙ, 日本語, 한국어, 简体中文 in turn — the UI fades and rebuilds with no
      restart, Settings stays open, fonts change per script, no pink squares or clipped labels; CJK text is
      vertically centred in buttons. In a development build pick the pseudo-language `[PŠÉÚÐÖ]` and check every
      screen shows bracketed, accented text with no clipped `]` (anything plain English is an unlocalized string).
      AUTOMATIC returns to the OS language.
- [ ] Devices: CJK and Cyrillic alignment, tutorial hints, toasts, store prices in a local currency. iOS: the
      Settings app shows a Language entry for BeamRunner and the tracking prompt appears in the device language.

## 4b. Startup, gamepad and touch (changed without an Editor — check these carefully)
- [ ] Startup: the first rendered frame shows the splash (no empty frame, no flash of the 3D world; the Editor
      may briefly say "No cameras rendering"). Profiler: frame 1 ≲ 15 ms on desktop; during the splash
      `UIBuildQueue.Update` stays around 4 ms apart from single "warm" steps; open every overlay right after the
      menu appears — no hitch, no half-built screen.
- [ ] **Splash art** (new, changed without an Editor): the loading screen shows the art edge to edge (behind the notch
      and the home indicator too) without stretching — on a tall phone, an iPad (top and bottom cropped) and in the
      Editor's Game view; it fades in, then slowly zooms towards the gate's glowing cell. The logo and tagline read
      over the starry sky, the progress bar and tips over the dark grid floor; nothing overlaps the ship. Import
      settings of `SplashArt.png`: no mipmaps, Non-Power-of-2 = None, max size 4096 (the texture must stay
      1152×2496). Memory profiler: the texture is gone once the menu shows. Tweak the layout in `SplashScreen.Build`
      / the shades in `BuildArt`, the picture in `Art/Source~/make_splash.py`.
- [ ] Settings with a gamepad / keyboard: every control shows the neon focus frame; left/right steps a slider;
      Submit toggles and selects segments; the list keeps the focused control in view; the right stick and
      Page Up/Down scroll Records, Missions and Results.
- [ ] Settings on touch: a vertical swipe starting on a slider scrolls the list without changing the value; a
      horizontal drag moves the slider; a tap sets it on release; swipes starting on toggles, segments or buttons
      scroll without triggering them. Button widths look the same as before (screens are now built while
      inactive).

## 5. Profiler (Editor, then a device)
- [ ] *Memory ▸ Audio*: ~43 MiB of AudioClip memory (float PCM; ~31 MiB at 32 kHz on <3 GB devices). About
      half of that means Unity stores these clips as 16-bit.
- [ ] Listen: menu music, the Run build-up as speed rises (arp, then lead), Overdrive and the pause duck. The
      always-on music layers are now pre-mixed into one clip; it should sound exactly as before.
- [ ] Switch the OS audio output device during play: audio recovers within ~2 s.
- [ ] **Draw calls** (Frame Debugger): environment ≈ 5 (floor, sky, ceiling, frame, speed lines), each gate wall 2
      (gliding-hole walls too), each cube 1, each pickup 2, craft ≈ 7 (with the collision ring); passed obstacles stop
      drawing once dissolved. Check a debris storm (level 7, up to ~60 cubes)
      and a gauntlet (level 11) on a low-end phone; aim for ≤ ~150. If cubes are too many, the next step is instanced
      cubes (`Graphics.RenderMeshInstanced`: instancing pragmas in NeonSolid, `enableInstancing`, Instancing Variants =
      Keep All in Graphics settings).
- [ ] **GPU fill**: the floor (opaque, roughly the lower half of the screen), the sky (only the pixels left uncovered,
      thanks to the depth test) and the additive ceiling are the full-screen costs; compare quality LOW (FIELD_LITE, no
      ceiling, 0.75 render scale) / MEDIUM / HIGH on a low-end phone. NeonSolid now computes the sky colour per pixel
      for its fog (a few `exp2`); the dithered variant (discard) is used only by obstacles fading out near the camera.
- [ ] **GC**: 0 B GC Alloc per frame during a run after the first seconds — including the frames where a gliding-hole
      wall rebuilds its mesh (every frame while the hole moves; also check the CPU cost of that rebuild on a low-end
      phone in levels 12 and Chaos) and where new gates / cubes / pickups are bound (cube meshes are prewarmed at boot).

## 6. On devices before release
- [ ] Android back button pauses/backs out correctly (arrives as `Keyboard.escapeKey`) — **also on an Android 16
      device**: target API 36 delivers back only via OnBackInvokedCallback, which relies on *Player ▸ Android ▸
      Predictive Back Support* (on; set by Setup Project). Check the merged manifest has
      `android:enableOnBackInvokedCallback="true"` and that Back still reaches the game as Escape.
- [ ] Android 16 on a tablet / foldable: target API 36 ignores the portrait lock on large screens, so the game
      runs in landscape there — the UI supports it; check a large-screen emulator.
- [ ] Vibration works (VIBRATE permission in the merged manifest).
- [ ] Tilt steering sign and gain (iOS and Android): re-centred at GO, up and down are equally responsive whether the
      phone is held at about 20°, 45° or 70° from upright (angles, `InputRouter.TiltFullDegrees` = 22 for full
      deflection); on a device without an accelerometer TILT falls back to dragging.
- [ ] Notch / safe area on a tall phone; 60 fps.
- [ ] Sandbox purchases (Play license tester / App Store sandbox account): localized prices; a gem pack grants
      once and is buyable again; Remove Ads stops interstitials; cancel shows no error; kill the app right
      after paying → the item arrives exactly once on the next launch; reinstall → Remove Ads comes back
      (iOS: also via Restore Purchases); airplane mode → "Store unavailable", recovers online. The full list is in
      `Scripts/Runtime/Services/README.md` › *Before release*.
- [ ] LevelPlay on test devices: the Test Suite opens in a development build with `testMode: true`; rewarded
      watched → reward, skipped → none; interstitial pacing; launching offline and coming back online.
- [ ] Monetization release guard (`FieldMonetizationBuildGuard`, compile-checked only): with the shipped
      placeholder privacy URL (or a platform's LevelPlay ids emptied), *Build ▸ Android App Bundle* stops with a
      dialog naming what is missing, *File ▸ Build*
      (release) fails with the same list, *Build ▸ Android APK (dev)* still builds; with
      `"allowIncompleteReleaseBuild": true` the release build succeeds with a Console warning. *Validate Project*
      reports "Unity Ads is among LevelPlay's native dependencies (<file>)" (Unity Ads is bundled in the Network
      Manager's "LevelPlay SDK" section); if it warns although the Network Manager lists Unity Ads as installed, find
      the `*Dependencies.xml` that declares it and adjust `FieldProjectValidator.CheckUnityAdsAdapter`.
- [ ] iOS: after choosing personalised ads the **ATT prompt** appears once (never before the consent dialog);
      "Ask App Not to Track" → ads stay non-personalised; the Xcode project's Info.plist contains
      `NSUserTrackingUsageDescription`. Build the `.xcworkspace` (CocoaPods) with Xcode 26.
- [ ] iOS build and CocoaPods: the Console may say "Podfile now uses the CocoaPods CDN instead of the git specs repo"
      (`FieldIosCocoaPodsCdn`: LevelPlay's IronSource dependency file lists `github.com/CocoaPods/Specs`, which made
      the External Dependency Manager's `pod install` clone that multi-GB repo while Unity sat frozen on a stale
      "Importing … PerformanceTestRunInfo.json" bar after "[LevelPlay]: IronSource build postprocessor finished").
      If a build ever hangs there: `ps ax -o pid,etime,command | grep -i pod` — a `git clone …CocoaPods/Specs`
      means the rewrite did not apply: `kill` those processes, `rm -rf ~/.cocoapods/repos/cocoapods` (the partial
      clone), set `source 'https://cdn.cocoapods.org/'` in the build folder's `Podfile` and run `pod install` there.
- [ ] iOS build: the build folder's `Podfile` lists `IronSourceSDK` and `IronSourceUnityAdsAdapter` pods. EDM4U 1.2.190
      would otherwise add LevelPlay as Swift packages, and that app crashes at launch in the LevelPlay SDK
      (`-[NSConcreteMutableData ISAES256EncryptWithKey:]: unrecognized selector`); `GvhProjectSettings.xml` turns
      `Google.IOSResolver.SwiftPackageManagerEnabled` off so it uses CocoaPods, as Neo Slipstream does.
- [ ] iOS build Console: "Podfile raises every pod's deployment target to iOS 15.0" (`FieldIosPodDeploymentTarget`), the
      build folder's `Podfile` ends with a `post_install` block, and Xcode no longer warns "The iOS deployment
      target 'IPHONEOS_DEPLOYMENT_TARGET' is set to 10.0/12.0/13.0 …" for the IronSource / UnityAds pods.
- [ ] iOS build Console: "iOS privacy manifest: all 3 declarations are in the Xcode project" (if it says it added
      the manifest to the app target instead, enable iOS in the Plugin Inspector of
      `Assets/Plugins/iOS/PrivacyInfo.xcprivacy`). In Xcode, *Product ▸ Archive ▸ Generate Privacy Report* lists
      the game's and the SDKs' declarations.
- [ ] Android: `python3 tools/check-16kb.py BeamRunner/Builds/<your>.aab` passes (all 64-bit libraries 16 KB
      aligned); Play Console's App bundle explorer shows no 16 KB warning after upload.
- [ ] Kill the app mid-save and relaunch: progress is recovered from the backup. Edit the save file by hand:
      the signed backup loads and a `.rejected` copy appears.
- [ ] Reminders: backgrounding schedules one notification for 19:00 (white ship icon); it's cancelled on return.
- [ ] Rating prompt on devices: Android at the 3rd launch shows the game's dialog and RATE NOW lands on the Play
      Store listing; iOS at the 3rd launch shows Apple's sheet (development build; never in TestFlight).
- [ ] Crash reporting (with a DSN): consent off → nothing reaches Sentry; on → a test exception and a native crash
      (`Utils.ForceCrash`) arrive with symbols.
- [ ] Online (once configured): sign in, submit a score, unlock an achievement, *Sync now* on two devices (gems
      add up, nothing lost), switch accounts (expect the COMBINE PROGRESS prompt).

## 7. CI (GitHub Actions, see [CI.md](CI.md))
- [ ] *Actions ▸ Checks* is green on the latest push (compile check, unit tests, `.meta` files, localization
      strings + font glyphs).
- [ ] GitHub *Settings ▸ Secrets and variables ▸ Actions*: `UNITY_EMAIL` + `UNITY_PASSWORD` (a Unity ID with a
      password login and 2FA off, ideally a dedicated CI account) and, for Play uploads, the four `ANDROID_*`
      keystore secrets. Then *Actions ▸ Unity build ▸ Run workflow*: tests green, `BeamRunner-Android` (16 KB
      check passed) and `BeamRunner-iOS` artifacts. The first real-Unity run shows import and compile problems
      the headless check can't see.
- [ ] Releases: tag `vX.Y.Z` → versionCode X·1000000 + Y·1000 + Z; keep *Player Settings ▸ Version* in step and
      bump the iOS build number in Xcode (CocoaPods / archiving happen on a Mac).

## Known open items (by design or needing a decision)
- ~43 MiB of synthesized audio in memory (down from 74 MiB by pre-mixing the always-on music layers). Further
  savings would need 16-bit or compressed clips written to disk on first boot and loaded back, which is only
  worth doing after confirming stem sync holds for such clips in Unity.
- Boot and synthesis threads: the audio render uses every core during the splash (`Parallel.For`); on 4-core
  phones it competes with the main and render threads. Watch for splash stutter on a low-end device.
- Store prices in currencies whose symbol isn't in Audiowide (₹ ₩ ₽ ₺) fall back to a system font.
- `ProjectVersion.txt` names 6000.3.25f1 without a changeset; Unity fills it in on first open (commit that).
- The music and sound mix was balanced from loudness measurements — give it a listening pass.
