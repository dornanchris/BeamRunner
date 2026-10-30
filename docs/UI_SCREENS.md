# BeamRunner — UI Screens Spec

Reference resolution 1080×1920 portrait (the kit's `CanvasScaler` handles other aspects; every screen must
also hold up in 16:9 landscape on desktop and on tall 19.5:9 phones, inside the safe area). Visual language:
dark glass panels, crisp neon frames with soft glow, Audiowide for display text, Rajdhani for body (Latin
languages; Russian, Japanese, Korean and Chinese use their own font set — `UIFonts`), the
current level's palette as accent (`UITheme.SetAccent`). Everything animates in (staggered pop/slide, unscaled
time) and every button gives press feedback + click sound + light haptic.

The UI never implements rules: it reads `GameController` / `ProfileService` state and calls controller
commands. Screens refresh from state when shown and on `ProfileService.Changed`.

## Flow owner: `GameUI`

`BeamRunner.UI.GameUI : MonoBehaviour` — created once by `GameController` (`GameUI.Create(GameController)`).
Builds the `UIRoot` and the splash (so the splash is in the first rendered frame), creates every other screen empty and
queues its build on `UIRoot.BuildQueue` (a few ms per frame behind the splash, the menu first; a screen shown before its
turn is built on the spot), and subscribes to controller events:

| Controller signal | UI reaction |
|---|---|
| `StateChanged(prev, next)` | show/hide screens per state (table below) |
| `RunEventRaised(e)` | forward to HUD (popups, banners, hints, meter FX) |
| `LiveMissionCompleted(m)` | toast "MISSION COMPLETE" with description |
| `CountdownTicked(n)` | countdown overlay pop (3, 2, 1, GO!) |
| `RunFinished(summary)` | results screen data |
| `Notify(msg)` | toast |
| `ConsentRequested(cb)` | consent dialog → `cb(personalised)` |
| `ProfileService.Changed` | refresh visible screen data (currency pills etc.) |

| State | Visible |
|---|---|
| Boot | Splash (logo, progress bar from `BootProgress`, rotating tips) |
| Menu | Main menu (+ daily reward popup auto-opens once per day) |
| Hangar | Hangar |
| Countdown | HUD + countdown overlay |
| Running | HUD (+ tutorial hints on tutorial runs) |
| Paused | HUD (dimmed) + Pause |
| Checkpoint | HUD (dimmed) + Checkpoint (level wall: stats, perk pick) |
| Crashed | HUD fades; revive offer when `ReviveOfferActive` (never in a Daily Run) |
| GameOver | Results |

Back (Android back / Escape / gamepad B): closes the top overlay; Running → pause; Paused → resume;
Hangar → menu; Menu → "Quit game?" confirm on Android/desktop builds (`Application.Quit`).

Language (`GameUI.Language.cs`): every text comes from the string tables (`Loc.T`, see `docs/LOCALIZATION.md`).
On `Loc.LanguageChanged` (Settings ▸ LANGUAGE) the whole UI is rebuilt behind a short fade — once the menu or hangar is
idle (no dialog, splash or transition) — through the normal build path, so every text, number format and font is
resolved again; an open Settings overlay is re-opened and the daily popup is not offered twice. No restart.

## Screens

### Splash (Boot)
Full-screen splash art (`Resources/BeamRunner/Splash/SplashArt.png`, 1152×2496, rendered by
`Art/Source~/make_splash.py`: the ship flying at a 9×9 gate's glowing open cell, neon cubes, starry sky, grid floor)
behind everything — over the notches too (the frame measures the root canvas, undoing the safe-area inset),
aspect-filled through its UV rect (wider screens crop top and bottom, taller ones the sides), fading in over 0.45 s and
slowly pushing in (×1.08 over 14 s) on the gate's opening; background-coloured shades darken its top third and bottom
fifth. Over the sky at the top: neon logo "BEAMRUNNER" (NeonTitle, flicker-on) and the tagline. Over the floor at the
bottom: thin progress bar and a tip line cycling every 2.5 s ("Skim cubes and gate edges for near misses", "Every gate
has at least one open square…", …). Without the art the synthwave backdrop shows instead. Fades out into the menu;
the art texture is unloaded once the splash is hidden.

### Main menu
* Background: the live autopilot run (3D; a random level 1–12 each time) — keep the centre of the screen clear.
* Top bar: pilot-rank badge (rank number, "n/3 missions"), gem pill, settings icon button, gift button
  (badge when `Profile.DailyRewardAvailable`), "no ads" button if IAP available and not owned.
* Title logo ("BEAMRUNNER" over "3D", Audiowide in every language) in the upper third; subtle breathing glow.
* Big pulsing PLAY button (lower third) with "BEST 12,345" beneath. Once a later level has been reached (and the
  tutorial is done) ◀ ▶ icon buttons either side of PLAY pick the start level, shown as PLAY's sub-label
  ("LEVEL 6 · EYE OF THE NEEDLE"); `ProfileService.SetStartLevel`, clamped to `MaxStartLevel`, persisted like a
  setting. Every level up to the first plateau cycle is pickable in the Editor and development builds.
* Daily Run entry just above PLAY (`DailyRunCard`), shown once the first flight unlocked it
  (`GameController.DailyRunAvailable`) — it takes the first-run hint's spot, and the hint hides while it shows.
  Secondary "DAILY RUN" button with the calendar icon → `StartDailyRun()`; sub-label: "BEST 12,345 | STREAK 3 |
  NEW TRACK IN 05:42" once flown today, "KEEP YOUR 3-DAY STREAK | 05:42 LEFT" or "SAME TRACK FOR EVERY PILOT |
  05:42 LEFT" before, "PRACTICE ONLY - CHECK THE DEVICE CLOCK" while the clock is behind the last recorded day.
  Attention halo + dot while today's field is unflown. Countdown = time to 00:00 UTC as HH:MM, rounded up,
  re-checked every second while visible.
* Bottom nav: HANGAR (→ `EnterHangar()`), MISSIONS, RECORDS (overlays).

### Hangar
3D ship on the turntable is behind the UI (`PreviewShip(id)` when browsing). Top: back, "HANGAR", gem pill.
Bottom glass card: ship name, tagline, page dots, ◀ ▶ (and horizontal swipe) to browse
`ShipCatalog.All`; action button by state: SELECT / ✓ SELECTED / BUY ◆5,000 (requirement met or none; disabled +
shake if too poor, "REQUIREMENT MET" / "UNLOCK WITH GEMS" status + gem progress) / LOCKED "Score 50,000 in one
run" / "Fly the Daily Run on 7 days" / "Reach pilot rank 10" with progress (`UnlockProgress`, "31,200 / 50,000",
"3 / 7", "RANK 6 / 10") and the price that follows as the button's sub-label ("THEN 20,000 GEMS"). Results show a
"NEW SHIP AVAILABLE" card when a run (or Daily Run) meets a requirement (`RunSummary.ShipsAvailable`). "GEM STORE" button opens the store panel:
IAP packs (title, gems, localized `Iap.GetPrice`), REMOVE ADS (hidden once owned), RESTORE PURCHASES
(iOS/when `SupportsRestore`), "Store unavailable" state when `!Iap.Available`.

### Missions (overlay)
Header: "PILOT RANK 3", "+8 % score", "Complete all three to rank up (+300 ◆)". Three mission cards:
description (`MissionSystem.Describe`), progress bar (`Progress01`, "34 / 40"), reward ◆, completed check.

### Records (overlay, tabs)
STATS (best score/distance, runs, play time, totals…; a DAILY RUN card under PERSONAL BESTS: today's best score
and distance, current / best streak, best daily score, days flown, daily runs, attempts today, next-field
countdown), LEADERBOARD (top 10: #, score, distance, ship, date;
empty state), ACHIEVEMENTS (title, description, progress bar, reward, unlocked glow; "12 / 25" header).
While signed in to Google Play Games / Game Center (and the ids are configured), the LEADERBOARD tab starts with a
card whose LEADERBOARDS button opens the platform's leaderboards (`ShowOnlineLeaderboards`) and the ACHIEVEMENTS tab
with an ACHIEVEMENTS button (`ShowOnlineAchievements`) — `RecordsScreen.Online.cs`.

### Settings (overlay; also reachable from Pause)
LANGUAGE (first card; hidden during runs, `SettingsScreen.Language.cs`): the current language's native name as a
button (+ "Automatic: follows your device language." when no choice is stored) → picker dialog: AUTOMATIC, then every
language in its own script and font, two per row (plus the pseudo-localization `[PŠÉÚÐÖ]` in debug builds) →
`LocalizationService.SetChoice` (per device) → UI rebuild.
AUDIO: music, SFX sliders. CONTROLS: DRAG | TILT | STICK, sensitivity, invert up / down, Overdrive button side,
auto-Overdrive. DISPLAY: graphics LOW | MED | HIGH, camera shake, reduce flashing, show FPS, Daily Run ghost (on by default;
`PlayerSettings.ShowGhost`). Haptics toggle.
REMINDERS (devices only): "Daily reminders" toggle (`SetRemindersEnabled`; asks the OS permission when turned on,
offers the system settings if notifications are blocked). PRIVACY: personalised ads toggle, "Send crash reports" toggle
(only when crash reporting is configured; `SetCrashReportsEnabled`), privacy policy link. PURCHASES: restore.
Online card (titled GOOGLE PLAY GAMES / GAME CENTER; hidden when nothing online is configured —
`SettingsScreen.Online.cs`): sign-in state ("Signed in as …") with SIGN IN when the platform allows it
(`SignInOnline`; iOS points to the Settings app), CLOUD SAVE status (last synced / syncing / offline / other
account / update needed) with SYNC NOW (`SyncCloudNow`; hidden during a run; after an account conflict it asks
"COMBINE PROGRESS?" first).
SUPPORT: email, RATE BEAMRUNNER (explicit store link, hidden without one). Credits
(fonts OFL: Audiowide, Rajdhani, Russo One, Tektur, Noto Sans JP/KR/SC; Space Shuttle model; "everything else generated
in code"). RESET PROGRESS (confirm). Version.
Every change calls `GameController.ApplySettings()`.

### Daily reward (popup)
Seven day tiles (◆ amounts, today highlighted, past days checked), CLAIM → `ClaimDailyReward()` → gem burst
+ count-up; "Come back tomorrow for ◆X".

### Consent (dialog, first launch)
Friendly explanation, two equal buttons (Personalised / Non-personalised), privacy policy link. When crash reporting
is configured: a "Send crash reports" switch, pre-set off in the EEA/UK/CH or unknown regions (on elsewhere),
recorded with the answer.

### Reminder offer (dialog, once)
After the first daily reward claim, on the idle menu: DAILY REMINDERS — "REMIND ME" (then the OS notification
permission) / "NO THANKS". Never repeated; Settings › Reminders afterwards.

### HUD
Top: pause (left), score (centre, big) with "LEVEL 4 · 1,234 M" under it and a thin level-progress bar
(`LevelProgress01`), gems (right). Multiplier badge under the score with 4 chain pips and a thin decay timer
(`ChainProgress`, `ChainTimer01`; the chain counts useful builds, full trails and clutch moves). Power-up chips on the
right (shield; magnet, Overload and instant build with radial timers). Bottom: hull hearts (one per point; a lost one
punches and greys) above the fuel gauge (glass track, green fill, "FUEL" + "12/20"; "LOW FUEL" pulsing amber below the
price of a bridge, "FREE BUILDS" in cyan during instant build, a red flash and shake on an empty build). No buttons but
pause: taps and swipes anywhere build and steer.
Popups: "NEAR MISS +50" (clutch moves) near the ship, "+3 FUEL", "NO FUEL", "HULL HIT", "OVERLOAD!", "SMASH +60",
"FULL TRAIL +80" (gold, punches the score) when every gem of a
trail is in, the level banner at every `LevelStarted` ("LEVEL 4" over the
level name, e.g. "EYE OF THE NEEDLE"), "LEVEL 3 CLEAR" with the bonus at every `LevelCleared`, shield broken flash.
Tutorial hint cards (icon + one line) for `TutorialHint` events (Move — per control mode/keys/gamepad —, Bridge,
Ramps, Fuel, Edges, Obstacles, Complete; Move/Bridge/Ramps dismiss early once the player does it).
Daily Run with a ghost: a small chip under the pause button (`HudScreen.Ghost.cs`, `TryGetGhostGap`), a "GHOST"
caption over "42 M AHEAD" (ahead of where the ghost was at the same run time; green chevron up), "17 M BEHIND"
(magenta chevron down), "EVEN", or "PASSED" (check; beyond the point where the ghost's run ended). Hidden otherwise.

### Countdown
Giant 3 · 2 · 1 · GO! pops with glow, colour cycling through the palette.

### Pause
"PAUSED", score and "LEVEL n · distance" so far, the three missions' live progress, RESUME (primary), RESTART, SETTINGS, MENU.

### Checkpoint (popup)
"LEVEL n CLEAR", the run so far ("SCORE · GEMS · M"), "PICK AN UPGRADE" and three perk buttons (icon, name, one-line
effect; `GameController.CheckpointPerks`, `ChooseCheckpointPerk(i)`), and TAKE ALL THREE (rewarded ad, when one is
ready; `TakeAllPerksWithAd`). Back picks the first perk. The run continues on the next level's middle layer.

### Revive offer
"CONTINUE?" with a draining ring (`ReviveOfferRemaining / ReviveOfferSeconds`), WATCH AD (if
`CanReviveWithAd`), ◆250 (enabled if `CanReviveWithGems`), NO THANKS.

### Results
"RUN COMPLETE" or animated "NEW RECORD!"; score count-up; best; distance; stats grid (level reached, gates,
cubes dodged, near misses, best chain, max multiplier, smashes, time); gem breakdown (pickups, missions,
achievements, rank-up = total) with DOUBLE ◆ (rewarded ad, once); completed missions, unlocked achievements,
rank-up and ship-unlock banners; leaderboard placement; RETRY (primary) and MENU →
`ContinueFromResults(retry)`. Daily Runs (`RunSummary.Daily`) add a DAILY RUN card right under the score: "NEW DAILY
BEST!" (gold, punch + reward sound) or "DAILY BEST 12,345", a grid (today's best, attempts today, streak, next
field in), reward rows (first Daily Run today, n-day streak bonus) or the next streak-bonus hint — or, for a run
that did not count, "NOT RECORDED" (device clock behind) / "TOO SHORT TO COUNT" with the reason. The gem
breakdown gets a "Daily run" row. RETRY after a Daily Run starts another Daily Run. (The rating prompt is not here:
it is on the menu, every third launch — see Menu.)

### Daily Run (mode)
One field per UTC day, the same for every player (from level 1) (seed from the UTC date, `DailyRuns`); unlimited attempts; no
tutorial, no revive offer and no pilot-rank score bonus (the score is comparable between players). Entry: the main
menu's DAILY RUN; RETRY on its results and RESTART from its pause menu stay in the Daily Run, PLAY is a normal run.
From the second counted attempt of the day on, the ghost of the day's best run flies along (Settings toggle): a
pale, flickering hologram of the ship it was flown with at the same X/Y — no collision — dimmed while it overlaps the player's ship and fading out where its run ended. Streak, rewards and
the clock rules live in Core (`ProfileService.DailyRun.cs`); the UI only reads them.
