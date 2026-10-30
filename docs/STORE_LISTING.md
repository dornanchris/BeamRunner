# BeamRunner — Store Listing Kit

Draft copy for Google Play and the App Store. Character limits are noted; everything here fits them.

**Google Play (as entered in Play Console, 27 Oct 2026):** the exact listing text and the graphics uploaded
(512² icon, 1024×500 feature graphic, six captioned 1080×1920 screenshots made from footage-branch takes) are in
`store/play/` (`listing-en-US.txt`). Play does not render Markdown, so that copy has no `**bold**`.

## Names

| Field | Value | Limit |
|---|---|---|
| App name (Play) | BeamRunner | 30 |
| App name (App Store) | BeamRunner | 30 |
| Subtitle (App Store) | Dodge cubes. Thread the gates. | 30 |
| Short description (Play) | Dodge neon cubes, thread the gates, chain near misses and go Overdrive. | 80 |

## Full description (Play, ≤ 4000 chars)

Find the gap. Then find the next one.

**BeamRunner** is a razor-sharp neon flying game built for one thumb. Your ship rockets through open space — drag
up, down, left and right to dodge a field of floating cubes and thread the open squares of gates made of glowing
blocks, as the speed keeps climbing.

**LEVEL BY LEVEL**
Every level brings something new: tumbling cubes, gates with a single way through, cubes that drift across your path,
walls that slide, and finally a hole that won't sit still. Cube fields and gates alternate, and when you reach the
top, the field stays at full strength for as long as you can survive it. Start any run from the furthest level you've
reached.

**LIVE ON THE EDGE**
Skim a cube or a gate edge by a hair for a **near miss**. Chain near misses and gates to climb all the way to an **x8
multiplier** — then unleash **OVERDRIVE**: double score, more speed, and the power to smash straight through anything
in your way.

**FEATURES**
• One-thumb drag controls (plus a floating thumbstick and tilt, full controller and keyboard support)
• Twelve levels that each add a twist, then a full-strength plateau
• A fair procedural field: every gate always has a way through, and you can always reach it
• A Daily Run: the same field for every pilot, every day, with a ghost of your best attempt
• Shields, magnets and Overdrive cells
• Fourteen ships to earn
• Daily missions, pilot ranks and 29 achievements
• Daily login rewards and a personal top-10 leaderboard
• A pulsing, adaptive synthwave soundtrack that builds with your speed
• Accessibility options: reduced flashing, auto-Overdrive, invert up/down, adjustable camera shake and sensitivity
• Plays offline

How far can you go?

## Promotional text (App Store, ≤ 170 chars)

Dodge neon cubes, thread gates with one way through, and chase a hole that won't sit still. Chain near misses to x8
and smash through everything in Overdrive.

## Keywords (App Store, ≤ 100 chars, comma-separated, no spaces)

`cube,field,neon,synthwave,endless,runner,arcade,dodge,gates,space,retro,flight,reflex,one,thumb`

## Category & rating

* Primary category: **Games › Arcade** (secondary: Racing)
* Content rating: Everyone / 4+ (no violence beyond abstract ship explosions; no user-generated content; no chat)
* Contains ads: **Yes** (Unity LevelPlay mediation; rewarded and occasional interstitials, removable by IAP)
* In-app purchases: **Yes** (Remove Ads, gem packs)

## Screenshot plan (portrait 1242×2688 / 1080×1920)

1. A cube field mid-run, near-miss popup — caption **"DODGE THE CUBES"**
2. A gate with one open square filling the screen — **"THREAD THE GATES"**
3. The level banner ("LEVEL 8 · SLIDING GATES") over a sliding wall — **"EVERY LEVEL A NEW TWIST"**
4. Overdrive smashing through a gate, bloom + chromatic burst — **"GO OVERDRIVE"**
5. Hangar with the Viper on the turntable — **"EARN 14 SHIPS"**
6. Results with a new record and the Daily Run card — **"BEAT THE DAILY FIELD"**

## Data safety / privacy nutrition label (summary)

* Data collected by the game itself: none leaves the device (save data, reminder/crash-report choices and the
  review-prompt bookkeeping are local), **except crash reports when the player allows them** (below).
* **Crash reports (Sentry, `io.sentry.unity` 4.10.3)** — only when `crashReportingDsn` is set in `monetization.json`
  AND the player switched "Send crash reports" on (first-launch privacy dialog / Settings › Privacy; off by default
  in the EEA/UK/CH). Declare only if the DSN is set:
  * **Google Play Data safety**: *App info and performance* › **Crash logs** and **Diagnostics** — collected,
    **not shared** (Sentry is a service provider), **optional** (the player can turn it off), purpose **App
    functionality / Analytics** (stability). *Device or other IDs*: **yes** — Sentry's random per-install id (not the
    advertising ID); same answers (not shared, optional, App functionality). Data encrypted in transit: yes. Users can
    request deletion: yes (support e-mail).
  * **App Store privacy label**: **Diagnostics › Crash Data** and **Performance Data** (+ **Other Diagnostic Data**),
    purpose **App Functionality**, **not linked** to the user, **not used for tracking** — matching the privacy manifest
    Sentry's iOS framework ships (`SentryObjC.framework/PrivacyInfo.xcprivacy`, merged into the app's privacy report
    by Xcode; the game's own `Assets/Plugins/iOS/PrivacyInfo.xcprivacy` needs no change).
  * In Sentry › Project Settings › Security & Privacy turn on **Prevent Storing of IP Addresses** and keep the
    data scrubbers on (the policy promises no IP addresses).
* **Reminders (Mobile Notifications 2.5.0)**: local notifications only — no data collected, no push service
  (nothing to declare). Android: the `POST_NOTIFICATIONS` permission (Android 13+) is added by the package and
  requested in game, after an explanation; iOS asks for notification permission at the same moment, never at launch.
* **Ratings**: the Google Play In-App Review / StoreKit review sheets collect nothing for the app (nothing to
  declare).
* **Google Play Games / Game Center / cloud save** — only when ids / `cloudSave*` are set in `online.json`
  (all empty by default):
  * Scores, achievements and the gamer profile are handled by **Google Play Games Services** / **Game Center**
    under Google's / Apple's policies; the game itself sends nothing to the developer. Google Play Data safety:
    Google's guidance treats Play Games Services data as collected by Google for the platform, not by the app
    (re-check the current Play Games Services Data safety guidance for `play-services-games-v2` 22.x); the game uses
    no other identifiers (it only hashes the player id on the device to tell accounts apart).
  * **Cloud save** stores the save file (game progress + a random per-device id for the gem ledger) in the
    player's own Saved Games (Google Drive app data) / iCloud key-value store; the developer has no access.
    Play Data safety: *App activity › Other actions* / *In-app progress* style data stays in the user's account —
    declare **Game progress** if Play's current form counts user-account cloud storage as collection; App Store:
    iCloud storage in the user's account is not "collected" by the developer (Apple: data not transmitted off device
    to the developer or third parties is not collected).
  * Listing copy (optional feature bullet, only when configured): "• Leaderboards, achievements and cloud save with
    Google Play Games" (Play) / "• Game Center leaderboards and achievements, iCloud save" (App Store).
* Unity LevelPlay (and the ad networks it mediates) collects device identifiers (Android advertising ID; iOS IDFA
  only after App Tracking Transparency permission), approximate location (IP-derived) and ad interaction data
  for advertising and fraud prevention; personalised ads only with consent. On iOS the app declares tracking in
  the App Privacy label if ATT is used; on Android declare the advertising-ID permission and "Advertising or
  marketing" data use in Data safety.
* Unity IAP / the platform stores process purchases.
* Declare these in Play Console "Data safety" and App Store "App Privacy" according to Unity's current
  guidance for the SDK versions in `Packages/manifest.json` (LevelPlay 9.5.1, IAP 5.4.3, Sentry for Unity 4.10.3,
  Mobile Notifications 2.5.0).
