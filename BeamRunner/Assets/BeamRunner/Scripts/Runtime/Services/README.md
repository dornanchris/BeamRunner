# BeamRunner — Services

Platform services for the Unity layer (namespace `BeamRunner.Services`): profile storage, Unity LevelPlay
ads (mediation), iOS App Tracking Transparency, Unity IAP, haptics and platform helpers. Everything degrades
gracefully: with no LevelPlay ids, no store, no network, in the Editor or on unsupported platforms the services
report "unavailable" (or simulate) and never throw into gameplay. All APIs are main-thread only and every
callback/event is raised on the main thread (SDK callbacks are marshalled through a hidden `ServiceRunner`).

| Area | Types |
|---|---|
| Hub | `ServiceHub` (creates/holds everything), `ServiceRunner` (hidden main-thread pump, timers) |
| Config | `MonetizationConfig` (`Resources/BeamRunner/Config/monetization.json`), `IapProductConfig`, `ProductIds` |
| Save | `ISaveStorage`, `FileSaveStorage` (atomic write + `.bak`, signed), `PlayerPrefsSaveStorage` (WebGL), `SaveStorageFactory`, `JsonSanity`, `SaveLoadReport`, `DeviceIdentity`; Core: `SaveEnvelope`, `SaveRecovery`, `SaveIntegrityKey` |
| Online | `IOnlineService` / `ICloudSave`, `PlayGamesOnlineService` (Android), `GameCenterOnlineService` + `GameCenterNative` (iOS, `Assets/Plugins/iOS/FieldGameCenter.m`), `SimulatedOnlineService`, `NullOnlineService`, `OnlineServiceFactory`, `OnlineConfig` (`Config/online.json`); Game: `CloudSync`, `GameController.Online.cs`; Core: `ProfileMerge`, `WalletLedger`, `CloudSyncState`; Editor: `FieldIosOnlineCapabilities`, `FieldOnlineValidation` |
| Ads | `IAdsService`, `LevelPlayAdsService`, `SimulatedAdsService` + `SimulatedAdOverlay`, `NullAdsService`, `AdsServiceFactory`, `AdsServiceBase`; Editor: `FieldMonetizationBuildGuard` (release builds need ad ids + privacy URL) |
| Privacy | `TrackingConsent` (iOS App Tracking Transparency via `com.unity.ads.ios-support`); Editor: `FieldIosTrackingPlist` (Info.plist post-build) |
| IAP | `IIapService`, `UnityIapService`, `NullIapService`, `IapServiceFactory`, `PurchaseResult` / `PurchaseStatus` |
| Platform | `Haptics`, `PlatformUtil`, `StoreReview` (Play In-App Review / StoreKit), `DevicePrefs` (per-device PlayerPrefs) |
| Notifications | `LocalReminders` (`com.unity.mobile.notifications` 2.5.0) |
| Diagnostics | `CrashReporting`, `FieldSentryOptionsConfiguration` (Sentry for Unity 4.10.3, consent-gated) |
| Localization | `LocalizationService` (string tables → `Loc`, language choice per device), `StoreTexts` (product titles/descriptions); Core: `Loc`, `Localizer`, `StringTable`, `PluralRules`, `LocFormat`, `Languages`, `PseudoLocalizer`; Editor: `FieldIosLocalizationPlist` (see *Localization*, `docs/LOCALIZATION.md`) |

## Integration (boot order)

```csharp
using BeamRunner.Services;

// 1. Build services (no network yet). `this` = the bootstrap MonoBehaviour (lives all session).
ServiceHub.Create(this, json => /* true if json parses into a usable profile */ true);

// 2. Load the profile.
string json = ServiceHub.Save.Load();          // null => first launch / unrecoverable => new profile

// 3. Subscribe BEFORE Initialize.
ServiceHub.Iap.EntitlementGranted += productId =>
{
    // THE ONLY PLACE PURCHASES ARE GRANTED. Grant, then save the profile synchronously.
    //   ProductIds.RemoveAds          -> profile.AdsRemoved = true; ServiceHub.Ads.SetAdsRemoved(true)
    //   gem packs                   -> profile.Gems += ServiceHub.Config.FindProduct(productId).Gems
};
ServiceHub.Ads.AdShowingChanged += showing => { /* mute audio, ignore auto-pause & input while true */ };

// 4. After the first-launch consent dialog (pass the stored answer on later launches). On iOS this shows the
//    App Tracking Transparency prompt first (once, only if the player chose personalised ads) and starts
//    LevelPlay after the answer; IAP starts right away.
ServiceHub.Initialize(personalizedAdsConsent, profile.AdsRemoved);
```

* `ServiceHub.Save.Save(json)` returns false on failure (it never throws).
* Rewarded revive: `if (Ads.RewardedReady) Ads.ShowRewarded(ok => { if (ok) Revive(); else GameOver(); });`
  `ok` is true only when the ad was watched to completion. Listen to `Ads.ReadinessChanged` to update the button.
* Interstitials: `Ads.ShowInterstitial(() => ContinueToMenu());` — the callback always fires, also when
  nothing was shown. Pacing (never before run 4, at most every 3rd game over, ≥ 120 s apart) is the game
  flow's job; `Enabled`/`InterstitialReady` are false once Remove Ads is owned.
* Shop: `Iap.GetPrice(id)`, `Iap.Owns(id)`, `Iap.Purchase(id, result => …)` (UI feedback only — never grant
  there), show a "Restore Purchases" button when `Iap.SupportsRestore` (iOS). Refresh on `Iap.AvailabilityChanged`.
* Settings: `ServiceHub.SetAdsConsent(bool)`, `Haptics.Enabled = settings.Haptics`,
  `PlatformUtil.OpenPrivacyPolicy()`, `OpenTerms()`, `ComposeSupportEmail()`, `OpenWriteReviewPage()` (explicit Rate button) /
  `OpenStorePage()` (hide when `!PlatformUtil.CanOpenStorePage`).
* Boot: `PlatformUtil.ApplyFrameRate(60 or 120)`; runs: `PlatformUtil.KeepScreenOn(true)`.

### Entitlement rules (IAP)

`EntitlementGranted(productId)` fires from `UnityIapService.HandlePendingOrder` — Unity IAP 5's
`OnPurchasePending` — for **every** paid purchase: new ones, purchases approved later (Ask to Buy, Google
pending payment methods), purchases interrupted before they were confirmed (crash, kill, lost connection —
re-delivered by the store on the next launch) and restored ones. It also fires (idempotently, once per session
and again on restore) for non-consumables the store lists as owned (`ConfirmedOrder`s from `FetchPurchases`).
Therefore:

* Grant **only** there, and persist the profile inside the handler.
* Two-step flow: the order is confirmed (`StoreController.ConfirmPurchase` → Google consumes gem packs /
  acknowledges Remove Ads, Apple finishes the transaction) only **after** every `EntitlementGranted` handler has
  returned. A failed confirmation is retried (store reconnect, back-off, next launch); Google refunds purchases
  left unacknowledged for 3 days, so confirmation must eventually succeed.
* Consumables are delivered once per transaction id (`order.Info.TransactionID`: Google purchase token / Apple
  transaction id): a per-session set plus a bounded PlayerPrefs ledger (`BeamRunner.Iap.GrantedTransactions`,
  last 64) make a re-delivered, already-granted order go straight to confirmation.
* Non-consumable grants must be idempotent (setting a flag is).
* If an order arrives while nothing is subscribed, it is held unconfirmed and granted + confirmed as soon as a
  handler subscribes. Orders for product ids not in the catalogue are ignored and left unconfirmed.
* `PurchaseStatus.Pending` means "awaiting approval/payment" (`OnPurchaseDeferred`); the item arrives later
  through the event, possibly in a later session.
* Receipt validation is **not** implemented. If fraud becomes a concern, validate in
  `UnityIapService.HandlePendingOrder` before granting (Google: `CrossPlatformValidator` with the generated
  `GooglePlayTangle`; Apple/StoreKit 2: send `order.Info.Apple.jwsRepresentation` to your server).

## LevelPlay (ads) configuration

Unity ended monetisation support for the direct Unity Ads SDK (`com.unity.ads`) on 31 Jan 2026, so ads run on
**Unity LevelPlay 9.5.1** (`com.unity.services.levelplay`, "Ads Mediation") with Unity Ads as a mediated network.
Both packages are in `Packages/manifest.json`; `com.unity.ads` is gone.

**Dashboard** ([LevelPlay platform](https://platform.ironsrc.com/))

1. Create one LevelPlay app per store listing (Android bundle id, iOS bundle id) and note each **app key**.
2. Create a **Rewarded** and an **Interstitial** ad unit per app and note their **ad unit ids**.
3. **Unity Ads is required.** The built-in ironSource Ads network stopped serving direct demand on 30 Apr 2026
   (only the ironSource Exchange remains), so without Unity Ads the ad units earn very little. In the Unity Ads
   (Monetization) dashboard create the project for both platforms and pick mediation partner **Unity LevelPlay**;
   then LevelPlay ▸ Setup ▸ Networks → **Unity Ads ▸ Setup** (Unity's guide offers an auto-setup there; the
   manual route uses the Monetization Stats API key + organization core id, then a Unity Ads instance per ad unit
   with the **game id** and the bidding placement the Unity Ads project created for LevelPlay:
   `BP_Rewarded_Android` / `BP_Interstitial_Android` (Android game id), `BP_Rewarded_iOS` / `BP_Interstitial_iOS`
   (iOS game id)). Each ad unit then lists 2 networks with Unity Ads **Active**. If the API management page never
   shows the Monetization Stats API key after *Create*, it is in the response of that `POST` request (browser
   developer tools ▸ Network); the newest key is the valid one.
   Register your phones as **test devices** in the dashboard — test ads are configured there, not in code.
4. `Resources/BeamRunner/Config/monetization.json`: `levelPlayAppKeyAndroid`, `levelPlayAppKeyIos`,
   `rewardedAdUnitAndroid/Ios`, `interstitialAdUnitAndroid/Ios`. Empty ids (the default) ⇒ no ads in release
   builds, simulated ads in the Editor / development builds. An empty ad unit id disables just that format.
   Old Unity Ads keys (`androidGameId`, `iosGameId`, `*Placement*`) are ignored. `testMode: true` makes
   **development builds** enable and open the LevelPlay **Test Suite** after init; release builds ignore it.

**Unity Editor, once per machine / after package updates**

1. *Ads Mediation ▸ Network Manager*: Unity Ads ships **bundled** in the "LevelPlay SDK" section (with LevelPlay
   Mediation and the ironSource Exchange; committed here: native LevelPlay 9.7.0 / Unity Ads adapter 5.15.0 on package 9.5.1):
   use its **Update** buttons for the latest compatible native versions. Don't install other networks unless you
   also set them up in the dashboard. Commit what it writes under `Assets/LevelPlay`. In the same window enable
   **"Add networks SKAdNetwork IDs"** (LevelPlay then writes every installed network's SKAdNetwork ids into
   Info.plist; we do not ship our own list).
2. *Ads Mediation* settings (`Assets/LevelPlay/Resources/LevelPlayMediationSettings.asset`): keep
   **auto-initialisation (`EnableIronsourceSDKInitAPI`) off** — the game calls `LevelPlay.Init` itself, after
   consent/ATT; keep `EnableAdapterDebug` / `EnableIntegrationHelper` off for release; turn
   **`DeclareAD_IDPermission` on** (Android 13+/API 33+ needs `com.google.android.gms.permission.AD_ID`; the target
   API is 36) unless the resolved `play-services-ads-identifier` AAR already merges it.
3. Native dependencies need the **Mobile Dependency Resolver / External Dependency Manager (EDM4U)** (accept its
   install prompt when LevelPlay asks). Android: *Assets ▸ External Dependency Manager ▸ Android Resolver ▸
   Resolve*; iOS: *iOS Resolver ▸ Install Cocoapods* and open the generated `.xcworkspace`, not the
   `.xcodeproj`. Delete any legacy `Assets/IronSource` folder.
4. Android already uses IL2CPP + ARM64 (*Setup Project*); ads also need the INTERNET permission — the SDK's
   merged manifest declares it; setting *Player Settings ▸ Android ▸ Internet Access = Require* is harmless.
5. `BeamRunner ▸ Validate Project` checks the ids, the LevelPlay settings above and that Unity Ads is among
   LevelPlay's native dependencies (an EDM4U `*Dependencies.xml` under `Assets/LevelPlay` or the LevelPlay package
   naming `unity-ads` / `UnityAds`).

**Release build guard** (`FieldMonetizationBuildGuard`, `IPreprocessBuildWithReport`): Android/iOS builds that are
not development builds fail while `monetization.json` has no LevelPlay app key, or neither ad unit id, for the target
platform (a release build would silently run `NullAdsService` — no ads, no revenue) or `privacyPolicyUrl` is still a
placeholder. It covers every build path (the BeamRunner build menu checks the same rule up front and names the
problem in a dialog, File ▸ Build, CI). `"allowIncompleteReleaseBuild": true` turns it into a warning — for a
test-track upload made before ads are set up (Google Play only lets you create products once a build with billing is
on a track); `Validate Project` warns while it is on. Development builds are never blocked.

**Getting paid and getting fill** (dashboards, no code)

* **Payout**: in the LevelPlay dashboard's account / payment settings enter the payment method and the tax form
  (W-9 / W-8BEN). Revenue accrues without it but is not paid out. If you also monetise Unity Ads directly in the
  Unity Cloud dashboard, it has its own payout settings.
* **app-ads.txt**: ad buyers check that the seller is authorised in an `app-ads.txt` file at the root of the
  **developer website** listed in each store listing (Google Play: *Store settings ▸ Store listing contact details ▸
  Website*; App Store Connect: *Marketing URL*/*Support URL* domain). Without it many buyers skip the inventory and
  fill and eCPM drop. Copy the lines LevelPlay shows for your account (and, for Unity Ads, Unity Cloud ▸
  Monetization ▸ Settings ▸ Organization ▸ App-ads.txt) into `https://<your domain>/app-ads.txt`, enter the same
  root URL as the developer website in the ad dashboards, and re-check the list monthly. The privacy policy can be
  hosted on the same site.

**Runtime behaviour** (`LevelPlayAdsService`)

* Privacy flags go through `LevelPlayPrivacySettings` **before** `LevelPlay.Init` and again on every change:
  `SetGDPRConsent(personalised)`, `SetCCPA(!personalised)` (true = "do not sell"), `SetCOPPA(false)` (the game
  targets 13+).
* After `OnInitSuccess` one `LevelPlayRewardedAd` and one `LevelPlayInterstitialAd` are created and loaded, and
  reloaded after each show or display failure. Load failures retry with capped exponential back-off
  (4 s → 120 s), init failures after 10 s → 300 s; loaded ads are re-checked with `IsAdReady()` every 60 s.
* Rewarded: the callback gets true only when `OnAdRewarded` fired — before `OnAdClosed` or up to 4 s after it.
* Every show request calls back exactly once: 30 s for the ad to appear, 240 s to close (active app time,
  paused while Android suspends the app for the ad), 8 s after Android regains focus without a close callback.
* `LevelPlay.SetPauseGame` is not used (not confirmed for 9.x): on iOS the game keeps running under the ad, muted
  via `AdShowingChanged`, and the game flow waits while `Ads.IsShowing`.

### Consent (GDPR / CCPA / COPPA / ATT)

The first-launch dialog (UI) stores the player's choice; `ServiceHub.Initialize` / `ServiceHub.SetAdsConsent(bool)`
apply it. The value handed to the ad network is `ServiceHub.EffectiveAdsConsent` = player's choice **and**, on
iOS, an *authorized* App Tracking Transparency status:

* **ATT** (`TrackingConsent`, `com.unity.ads.ios-support` 1.2.0 `ATTrackingStatusBinding`): requested once,
  after the dialog was answered and before `LevelPlay.Init`, only when the player chose personalised ads and the
  status is still *not determined* (switching to personalised later in Settings asks then). Any other status ⇒
  non-personalised. No answer within 60 s ⇒ LevelPlay starts non-personalised and the late answer is applied
  when it arrives; a change in the iOS Settings app is picked up when the game regains focus.
* The prompt text (`NSUserTrackingUsageDescription` = "Your data will be used to show you more relevant ads. Ads
  keep BeamRunner free.") is added to the Xcode project's Info.plist by the `FieldIosTrackingPlist`
  post-build step (an existing value is kept). Without it iOS never shows the prompt. Its translations (string key
  `ios.tracking_usage`) go into `<lang>.lproj/InfoPlist.strings` via `FieldIosLocalizationPlist` (see *Localization*).
* Not covered: a certified CMP for the EEA — check current store/ad-network policy before release. Declare ads
  and purchases in the Google Play **Data safety** form and the App Store privacy labels (LevelPlay collects
  device identifiers).
* iOS privacy manifest: `Assets/Plugins/iOS/PrivacyInfo.xcprivacy` declares the game's own required-reason APIs —
  UserDefaults `CA92.1` (PlayerPrefs: IAP grant ledger, WebGL save fallback), FileTimestamp `C617.1` (save files
  in the app container) and SystemBootTime `35F9.1` (Stopwatch for elapsed-time budgets) — and no tracking or
  collected data (nothing leaves the device). Unity merges it into `UnityFramework/PrivacyInfo.xcprivacy`;
  the engine, IAP, Services Core and the LevelPlay/ad-network pods ship their own manifests.
  `FieldIosPrivacyManifest` (iOS post-build) verifies the declarations reached the Xcode project. Don't use
  `Environment.TickCount` (boot-time clock) for seeds; use `DateTime.UtcNow.Ticks`.

## IAP configuration

Product ids are identical on every store (see `ProductIds` and `monetization.json`):

| Id | Type | Grants | Fallback price |
|---|---|---|---|
| `beamrunner.removeads` | Non-consumable | no interstitials | $2.99 |
| `beamrunner.gems.small` | Consumable | 1,000 gems | $0.99 |
| `beamrunner.gems.medium` | Consumable | 6,000 gems | $4.99 |
| `beamrunner.gems.large` | Consumable | 15,000 gems | $9.99 |

**Package.** Unity IAP **5.4.3** (`com.unity.purchasing`), which bundles **Google Play Billing Library 9.0.0**
(Google Play rejects new apps/updates built with Billing < 8; IAP 4.12.2 shipped 6.2.1) and uses **StoreKit 2**
on iOS 15+ (StoreKit 1 below). The code uses only the IAP 5 API — `UnityIAPServices.StoreController()` and its
events; none of the `[Obsolete]` v4 types (`UnityPurchasing`, `IStoreListener`, `ConfigurationBuilder`,
`IExtensionProvider`, `Product.receipt`/`hasReceipt`/`transactionID`). Products are declared in code from
`monetization.json` (no IAP Catalog / codeless IAP, no Remote Catalog, no Unity Authentication).

**Flow** (`UnityIapService`): `Initialize()` (from `ServiceHub.Initialize`) → subscribe to every
`StoreController` event → `Connect()` → `OnStoreConnected` → `FetchProducts(catalogue)` → `OnProductsFetched`
(`Available` = true) → `FetchPurchases()` (unconfirmed orders come back through `OnPurchasePending`, owned
Remove Ads as a `ConfirmedOrder`). `Purchase` → `PurchaseProduct` → `OnPurchasePending` (grant, confirm, report
Success) / `OnPurchaseDeferred` (Pending) / `OnPurchaseFailed` (Cancelled / Failed / Unavailable / Pending).
`Restore` → `RestoreTransactions` (Apple: `AppStore.sync`) → IAP re-fetches the purchases → owned items are
re-announced through `EntitlementGranted` → callback(true). Every callback fires exactly once, on a later frame:
180 s purchase and 90 s restore time-outs (active app time; a late store answer still grants). Connection /
catalogue failures retry with back-off (5 s → 300 s, real time), immediately on app resume or on a Buy/Restore
tap after a 10 s cool-down; IAP itself retries Google connects 3× and each product fetch 3×.
**Unity Gaming Services are not initialized:** IAP 5 has its own initialization path (`Connect`) and does not
need `UnityServices.InitializeAsync` (the package docs; it only enables automatic transaction events when
Unity Analytics is installed, which this project does not use).

* **Google Play Console**: set up a **payments profile** (Play Console ▸ Setup ▸ Payments profile: merchant account,
  bank account, tax info) — products cannot be priced/activated or paid out without it. Then upload a build containing IAP to a testing track first (the
  `com.android.vending.BILLING` permission is merged from the IAP package), then *Monetize with Play → Products
  → One-time products* (formerly "In-app products"): create the four ids with a buy option, set prices,
  **activate**. Google does not distinguish consumable/non-consumable — the code consumes gem packs and
  acknowledges Remove Ads. Add **license testers** (Settings → License testing) for test purchases, including the
  "slow test card" (pending payment → `Pending`, delivered later). Remove Ads is restored automatically when
  the store connects (reinstall / new device); Android has no Restore button.
* **App Store Connect**: sign the Paid Apps agreement (tax + banking), then create In-App Purchases:
  `beamrunner.removeads` as **Non-Consumable**, the gem packs as **Consumable**, each with a review
  screenshot, and attach them to the version for review. The App ID needs the In-App Purchase capability
  (default). Test with **Sandbox** tester accounts (Users and Access → Sandbox). App Review requires the
  **Restore Purchases** button (`Iap.SupportsRestore`).
* Prices shown come from the store (`GetPrice`, localized). Before the catalogue arrives, and always in the
  Editor (whose fake store reports $0.01), the `fallbackPrice` from the config is shown. Ids missing from a
  store are logged ("products missing from the store") and shown as unavailable; the rest still sell.
* Desktop and WebGL builds use `NullIapService` on purpose — Unity IAP would otherwise fall back to its fake
  store there and hand out free purchases.

## What is simulated in the Editor

* **Ads**: the Editor always uses `SimulatedAdsService` when `simulateAdsInEditor: true` (LevelPlay's own Editor
  mock ads do not reliably raise their init/reward callbacks): a full-screen "SIMULATED AD" overlay (uGUI,
  sorting order 32000) with a 3 s countdown, SKIP (no reward) and CLOSE (reward) buttons; works at
  `timeScale = 0`; Esc/Back or gamepad East = skip, Enter/Space or gamepad South = close. Development builds
  without LevelPlay ids for the platform get the same. Release builds without ids get `NullAdsService` (never
  ready). ATT is not applicable in the Editor (personalisation follows the player's choice).
* **IAP**: Unity IAP 5's fake store (`UIFakeStore`): every purchase opens a small **Buy / Cancel** dialog
  (add the scripting define `IAP_FAKE_STORE_DEFAULT` to make purchases succeed instantly without it); Remove Ads
  stays owned for the play session only; restore re-reads that list and succeeds; prices = config fallbacks.
* **Haptics**: no-op (gamepad rumble works).
* **Online services**: off by default (`NullOnlineService`, all online UI hidden). With `simulateInEditor: true` in
  `online.json` the Editor and development builds use `SimulatedOnlineService`: signed in as "Test Pilot" after
  0.6 s, scores/achievements/native UIs are log lines, and the cloud save is the file
  `persistentDataPath/beamrunner_cloud_sim.json` (edit it or copy another profile's cloud document there to try
  merges; delete it for an empty cloud). Automated tests (`TestLaunchOptions.OfflineServices`) always get the null
  service.

## Reminders (local notifications)

`LocalReminders` wraps **Mobile Notifications 2.5.0** (`AndroidNotificationCenter` / `iOSNotificationCenter`; the
package requires Unity 6.0+). Rules (`GameController.Engagement.cs`, planning in Core `ReminderPlanner`, tested):

* **At most one pending reminder.** When the app goes to the background (`OnApplicationPause(true)`) or quits, the
  reminder is re-planned: 19:00 local time on the first day the next daily reward can be claimed (today if it is
  still unclaimed and 19:00 is ≥ 3 h away, else tomorrow); "keep your N-day streak" texts when a streak is running;
  a Daily Run streak at risk comes first (before the UTC reset, latest whole hour ≥ 1 h before it). Always inside
  09:00–21:00, never at night. Returning to the game cancels it and clears delivered ones. A lapsed player gets one.
* **Permission only after an in-game explanation**: after the first daily reward claim the menu shows "DAILY
  REMINDERS — REMIND ME / NO THANKS" once; only "REMIND ME" triggers the iOS authorization / Android 13+
  `POST_NOTIFICATIONS` prompt. Denial is respected (never asked again automatically). Settings ▸ Reminders turns it
  on/off (on after permission; if the OS blocks notifications it offers the system settings). Opt-in state is per
  device (`DevicePrefs`, not the profile/cloud save).
* **Package settings** are committed in `ProjectSettings/NotificationsSettings.asset` (the package's own JSON
  format): iOS *Request Authorization on App Launch* **off** (a build guard fails iOS builds if it is on), no push
  capability, no time-sensitive entitlement; Android: no exact alarms, no reschedule-on-reboot (no extra
  permissions besides `POST_NOTIFICATIONS`), small icon **`field_notification_small`** =
  `Art/Notification/NotificationSmall.png` (white ship on transparency, Read/Write on; regenerate with
  `Art/Source~/make_notification_icon.py`). Channel `field_reminders` ("Reminders", default importance) is created at
  runtime. `Validate Project` checks all of this.

## Rating prompt

Every third launch of the app (the 3rd, 6th, 9th …, counted per device in `DevicePrefs`; `ReviewPromptPolicy`, Core,
tested) the menu asks for a rating once it is idle (2 s after it appears, after the daily reward and the reminder
offer, never during a run or an ad; a launch after an app crash — Sentry's last-run state — is skipped). How it asks
depends on the store (`GameController.ReviewPromptStyle`):

* **Android (and the Editor)**: the game's own dialog — RATE BEAMRUNNER / RATE NOW · NOT NOW · DON'T ASK AGAIN.
  RATE NOW opens the Play Store page (`PlatformUtil.OpenWriteReviewPage`) and ends the prompts, as does DON'T ASK
  AGAIN; NOT NOW (also back / tapping outside) asks again three launches later. If no store link is configured RATE
  NOW shows a toast and counts as NOT NOW. Google Play allows such a dialog; it does not use Google's In-App Review
  card (whose guidelines forbid any question before it).
* **iOS**: no dialog of the game's own — App Store Review Guideline 5.6.1 disallows custom review prompts. Every
  third launch requests Apple's review sheet (`StoreReview.Request` → StoreKit `RequestStoreReview`), which has its
  own "Not Now"; iOS shows it at most three times a year and players can switch it off in Settings ▸ App Store, so
  there is no game-side "don't ask again".

`StoreReview` still implements Google Play In-App Review (`com.google.android.play:review` 2.0.2, declared for EDM4U
in `Scripts/Editor/Android/FieldPlayReviewDependencies.xml`, called through JNI: `ReviewManagerFactory.create` →
`requestReviewFlow` → `launchReviewFlow`, listeners marshalled to the main thread), but the game no longer calls it on
Android. The explicit **Settings ▸ Support ▸ RATE BEAMRUNNER** button
opens the store (`PlatformUtil.OpenWriteReviewPage`: App Store "write a review" page, needs `iosAppId`; Play Store
page) and is hidden when no store link is known. Without the resolved Play library (Android Resolver not run) the
Android request just reports false.

## Crash reporting (Sentry)

**Sentry for Unity 4.10.3** (`io.sentry.unity`, UPM git URL `https://github.com/getsentry/unity.git#4.10.3`):
C# exceptions + native crashes (Android NDK / iOS) + app hangs, IL2CPP line numbers, automatic debug-symbol upload.
Consent-gated — nothing is captured unless **all** hold: `crashReportingDsn` is set in `monetization.json` (empty =
off; the game runs the same), the player switched **Send crash reports** on (first-launch privacy dialog, pre-set
off in the EEA/UK/CH or when the region is unknown — `PrivacyRegion`; Settings ▸ Privacy), and it is not the Editor
(unless `crashReportingInEditor: true`). Choice stored per device (`DevicePrefs`, key
`BeamRunner.Privacy.CrashReports`); unanswered = off.

* Startup: Sentry self-initialises from `Assets/Resources/Sentry/SentryOptions.asset`; that asset's *Options
  Configuration* is `FieldSentryOptionsConfiguration` (`Resources/Sentry/FieldSentryOptionsConfiguration.asset`), which
  injects the DSN and disables the SDK without consent. Create/repair both with **BeamRunner ▸ Crash Reporting ▸
  Set Up Sentry Options**. A build guard fails Android/iOS builds if the options asset is enabled but not linked to
  it (the SDK would start before consent); at boot `CrashReporting.Apply()` also closes an SDK that started anyway.
* Privacy options: `SendDefaultPii` off, no screenshots / view hierarchy, no tracing, native SDKs initialised at
  runtime (after consent), release `beamrunner@<version>`, environment development/production.
* Without the options asset only managed exceptions are reported (the native SDKs are added to the Gradle/Xcode
  project by Sentry's build step only when the asset is valid at build time).
* Symbols: Tools ▸ Sentry ▸ Debug Symbols (auth token, org, project) → `Assets/Plugins/Sentry/SentryCliOptions.asset`
  (git-ignored: it holds the token).

## Haptics

`Haptics.Selection/Tick/Light/Medium/Heavy/Success/Failure`, gated by `Haptics.Enabled`, rate-limited to one
pulse per 40 ms unless the new pulse is stronger. Android uses the system Vibrator through JNI
(`VibrationEffect.createOneShot` with amplitude on API 26+). The `android.permission.VIBRATE` permission is
added automatically by Unity because `Haptics` references `Handheld.Vibrate` (also its fallback) — keep that
reference; the alternative would be a custom Android manifest. iOS has no native haptics plugin, so only
Heavy/Failure buzz (`Handheld.Vibrate`). Connected gamepads get a short rumble.

## Save storage

`FileSaveStorage` writes `persistentDataPath/beamrunner_profile.json` via `*.tmp` (flushed to disk) →
current file becomes `*.bak` (only if loadable) → rename `*.tmp` over the main file. `Save` refuses profiles that
fail the validator (`ProfileStore.IsUsableSave`: JsonUtility turns it into a profile). WebGL uses PlayerPrefs with a
backup key and the same rules.

**Integrity (signed saves).** Every document is a `SaveEnvelope` (Core):
`{"fieldSave":1,"iid":"<install id>","sig":"<HMAC-SHA256 hex>","data":<profile JSON>}` — still readable JSON, but any
edit of the data no longer matches the signature. Key: HMAC-SHA256(app key, "beamrunner/save/v1/" + install id);
the app key (`SaveIntegrityKey`, assembled at run time) is compiled into the game and pinned by a golden test — never
change it. **This is deterrence against casual editing, not security**: anyone who extracts the key from the binary
can sign anything. The install id travels inside the file (so a restore without PlayerPrefs still verifies).

Load policy (`SaveRecovery`, Core, tested; copies tried newest first: main → complete `*.tmp` → `*.bak`):

1. The first correctly **signed** copy wins — an edited main file silently falls back to the signed backup.
2. A **legacy** (pre-signing, plain JSON) copy is accepted once: while no signed save was ever written here (marker
   file `*.signed` next to the save; WebGL: key `BeamRunner.Profile.signed`). The next save signs it. After that
   an unsigned copy is ignored — except when the evidence points at corruption (a signed-looking copy is unreadable
   and no copy has a mismatching signature), so corruption never wipes progress.
3. Otherwise nothing loads and a fresh profile starts. **Nothing is deleted**: the unusable main file is moved to
   `*.rejected` on the next save (the backup is kept). Torn/garbage files are *damaged*, never *tampered*; a
   signature mismatch on an intact document is treated as an edit.

`FileSaveStorage.LastLoad` (`SaveLoadReport`) says what was found. The save also carries the gem ledger's device
id (`DeviceIdentity`): mirrored in `temporaryCachePath/beamrunner_device.id`, which backups and device transfers do
not copy, so a restored/copied save gets a fresh device key (see cloud save).

## Online services (Google Play Games, Game Center, cloud save)

Everything is driven by `Resources/BeamRunner/Config/online.json`; **every id is empty by default and an empty id
hides that feature** (the shipped file turns everything online off — the game runs exactly as before).
`OnlineServiceFactory` picks: Android + something configured + the Play Games plugin set up →
`PlayGamesOnlineService`; iOS + something configured → `GameCenterOnlineService`; Editor/development +
`simulateInEditor` → `SimulatedOnlineService`; otherwise `NullOnlineService`. Services start only after the
first-launch consent flow (`GameController.StartServices` → `StartOnline`).

| Key | Meaning |
|---|---|
| `googlePlayGames`, `gameCenter` | master switches (default on; nothing happens without ids) |
| `leaderboardBestScoreAndroid/Ios` | all-time best score leaderboard (every run's score is submitted; platforms keep the best) |
| `leaderboardDailyRunAndroid/Ios` | Daily Run leaderboard (counted runs via `ProfileService.DailyRunCompleted`; the UTC day number goes in the score tag / GameKit context) |
| `achievements[]` | `{ "id": <AchievementCatalog id>, "android": <Play id>, "ios": <App Store Connect id> }` — all 25 listed, ids empty |
| `cloudSaveAndroid` | Play Games **Saved Games** (enable Saved Games in the Play Console first). Off by default |
| `cloudSaveIos` | **iCloud key-value store**; the iOS build then adds the iCloud (key-value storage) entitlement. Off by default |
| `simulateInEditor` | Editor/development simulation (above) |

**Android — Google Play Games plugin for Unity 2.2.1** (github.com/playgameservices/play-games-plugin-for-unity,
release 2026-08-28, PGS v2 SDK `play-services-games-v2:22.0.0`; its bridge AAR requires **Android API 24**, so the project's minimum is now 24). It is only
distributed as a `.unitypackage`, so it is **vendored** under `Assets/GooglePlayGames/com.google.play.games`
(runtime + editor setup, with the package's own `.meta` GUIDs so a later official import updates it in place, plus
`LICENSE` — Apache 2.0) and `Assets/Plugins/Android/GooglePlayGamesManifest.androidlib`. **Not** vendored: the
package's bundled External Dependency Manager 1.2.182 (LevelPlay's Mobile Dependency Resolver resolves
`GooglePlayGamesPluginDependencies.xml`; two resolvers conflict), a stray nested 2.2.0 `.unitypackage`, and the
generated manifest — the repo ships a placeholder `AndroidManifest.xml` without an app id so unconfigured builds work
(the plugin's setup overwrites it). The game calls `PlayGamesPlatform.Instance` directly (never `Activate()` /
`UnityEngine.Social`, obsolete in Unity 6.3) and only when `PlayGamesSettings` has an app id. Sign-in: PGS v2 signs in
automatically at start; `Authenticate` reads the result; if it failed Settings shows **SIGN IN**
(`ManuallyAuthenticate`). The compile check builds `Google.Play.Games` from the v2.2.1 tag's sources.

**iOS — GameKit + iCloud key-value store through the game's own native bridge** (`Assets/Plugins/iOS/FieldGameCenter.m`,
~300 lines, ARC, framework dependency GameKit; C# `GameCenterNative`, polled — no callbacks into managed code).
Why not the alternatives: Unity's `UnityEngine.SocialPlatforms`/`Social` Game Center API is `[Obsolete("… will be
removed …")]` in UnityCsReference 6000.3.25f1 and has no cloud save; Apple's official **Apple.GameKit** plug-in
(github.com/apple/unityplugins, release 27.0 = GameKit 4.1.0 / Core 3.3.0, 2026-09-16) is the recommended wrapper,
but it is source-only — its native framework must be built with `build.py` on a Mac with Xcode before the project
could even resolve the package — and it does not cover the key-value store; the four GameKit calls the game needs are
small. The bridge uses only non-deprecated iOS 14/15+ APIs (`authenticateHandler`, `GKLeaderboard submitScore:…`,
`GKAchievement reportAchievements:`, `GKAccessPoint triggerAccessPointWithState:` — `GKGameCenterViewController` is
deprecated in iOS 26) and was compiled with clang against the iOS 27.0 SDK headers (`-Wall -Wextra -Wdeprecated
-Wunguarded-availability`, deployment target 15.0): no warnings. Sign-in: installing GameKit's handler signs in
silently; if GameKit offers its sign-in screen it is kept until the player taps **SIGN IN**; once dismissed, iOS only
allows signing in from the Settings app (the UI says so). Leaderboards/achievements open through the access point.
`FieldIosOnlineCapabilities` (iOS post-build) links GameKit and writes `com.apple.developer.game-center` (Game Center
ids configured) and `com.apple.developer.ubiquity-kvstore-identifier` = `$(TeamIdentifierPrefix)$(CFBundleIdentifier)`
(`cloudSaveIos`) into the main target's entitlements. Why the key-value store and not GameKit saved games: the profile
is a few KB (limit 1 MB), it needs neither iCloud Drive nor a Game Center sign-in, it syncs in the background and
notifies other devices; GameKit saved games need iCloud Drive and are known to be slow and conflict-prone.

**Scores & achievements** (`GameController.Online.cs`): after each run the score goes to the best-score leaderboard;
counted Daily Runs to the Daily Run leaderboard; achievements are reported when the profile's list grows (runs, ship
purchases, cloud merges). After every sign-in the bests and all unlocked achievements are re-reported quietly (no
banners) — so progress made offline arrives — unless the signed-in account differs from the account this device's
cloud save is bound to. Records shows **LEADERBOARDS** / **ACHIEVEMENTS** (native UIs) while signed in.

**Cloud save** (`CloudSync`, merge rules in Core `ProfileMerge`, tested): read → merge into the live profile → write
the merged, signed profile back in one pass (Saved Games: open with automatic conflict resolution "longest play
time" → read → commit; iCloud: get → set, synchronous). Triggers: sign-in / boot (after consent), app pause and
resume, back at the menu after a run and external iCloud changes (rate-limited to one per 45 s), Settings ›
**SYNC NOW**. Offline-safe: a failed read changes nothing; a failed write keeps the (monotone) local merge and the next
sync uploads it. Guards: a cloud copy not correctly signed by the game is replaced, never merged; a copy written by a
newer game version is merged into the device but never overwritten ("update to sync"); a copy that belongs to another
account (Play Games: hashed player id; iCloud: the save's cloud id) is not merged until the player confirms
**COMBINE** in Settings. Never during a run.

Merge rules (`ProfileMerge.RulesVersion` 1): purchases OR; ships and achievements union (their gem rewards were
paid on the device that unlocked them); numeric stats max (bests and totals); rank max; missions from the snapshot
with more completed sets; local top 10 union; login calendar and Daily Run: the later day wins (same day: the better
best); settings, ads consent, ad pacing, selected ship (while owned) stay per device; unknown future fields come from
the snapshot with more play time. **Gems** use a per-device ledger (`WalletLedger`, a PN-counter): each device only
grows its own earned/spent counters, a merge takes the larger value per device and the balance is Σ earned − Σ
spent — spending on one device can never be undone by the other device's older balance, and nothing is duplicated or
lost (IAP gems included). **Reset progress** raises the profile's epoch: older snapshots cannot bring the progress
back (purchases and the ledger still merge; the wiped balance is booked as spent). Known, bounded limits without a
server: the same daily reward / mission / achievement reward earned on two devices while both are offline is paid on
each; buying the same ship on two offline devices spends twice.

## Localization

Twelve languages (English source + es, pt-BR, fr, de, it, ru, tr, pl, ja, ko, zh-Hans; pseudo-localization
`qps-ploc` in the Editor/development builds). Full guide, glossary and workflow: `docs/LOCALIZATION.md`.

* `LocalizationService.Initialize()` (called by `GameController.CreateUI` before the UI is built) loads
  `Resources/BeamRunner/Localization/en.json` plus the current language's table into `Core.Localization.Loc`.
  Language = the player's choice (Settings ▸ LANGUAGE, per device: `DevicePrefs` key `BeamRunner.Language`,
  empty = automatic) or else `Application.systemLanguage` (every Chinese variant → Simplified Chinese, Portuguese →
  pt-BR, anything unsupported → English). `LocalizationService.SetChoice(lang or null)` switches at runtime and
  raises `Loc.LanguageChanged`; `GameUI` then rebuilds the UI behind a fade (menu/hangar only).
* Missing key → English → the key itself; each miss is logged once (Editor/development builds). A broken table
  logs an error and falls back to English — nothing throws.
* Numbers shown to players go through `Loc.N` (per-language grouping, never the device culture); saves, JSON and
  prefs stay culture-invariant (`JsonUtility`, `SaveEnvelope`, `InvariantCulture` dates).
* Texts from services: store toasts (`store.*`), online toasts (`online.*`), reminder channel/texts
  (`notifications.*`, `reminder.*` via Core `ReminderPlanner`), support e-mail (`support.*`) and product
  titles/descriptions (`StoreTexts`, our own names — store metadata is not shown). The reminder is planned when the
  app goes to the background, so it uses the language shown then; the Android channel name follows the language
  from the next launch (re-registering updates it).
* iOS: `FieldIosLocalizationPlist` (post-build) sets `CFBundleDevelopmentRegion` = en, `CFBundleLocalizations` = the
  twelve languages, and writes `<lang>.lproj/InfoPlist.strings` with the translated ATT prompt as folder references
  of the app target. Android needs nothing (the game picks its language itself; notification texts are ours).
* Tests: `TestLaunchOptions.Language` stands in for the device language (the smoke tests use "en").

## Before release (owner checklist)

* **Translations are machine-made** (`_meta.machineTranslated` in every table): have a native speaker review each
  language before shipping it — the consent/privacy texts (`consent.*`, `settings.personalised_hint`,
  `settings.crash_hint`, `ios.tracking_usage`) and store texts first — then set `machineTranslated` to false and
  name the reviewer in `_meta.translator`. After any edit: `python3 tools/build-fonts.py` (ru/ja/ko/zh-Hans) and
  `python3 tools/check-strings.py`. On a device per script (Latin, Cyrillic, Japanese, Korean, Chinese): switch the
  language in Settings, open every screen, a run with the tutorial, a revive, results, the store (prices use the
  store's currency sign; missing signs come from the OS font) and a reminder notification.

* `monetization.json`: LevelPlay app keys + rewarded/interstitial ad unit ids for both platforms, real
  `privacyPolicyUrl` (release builds fail without these, see *Release build guard*; keep
  `allowIncompleteReleaseBuild` false), `termsUrl`,
  `supportEmail` (currently `example.com` placeholders), `iosAppId` (numeric Apple ID),
  optionally `androidPackage` (defaults to the bundle identifier).
* Player Settings: the bundle id is `com.cddevs.beamrunner` on every platform (set by *Setup Project*) —
  confirm it before the first upload (it must match Google Play / App Store Connect and can never change
  afterwards), plus company name and version.
* Money in: Google Play payments profile, App Store Paid Apps agreement (tax + banking), LevelPlay payout and tax
  form, `app-ads.txt` on the developer website (see *Getting paid and getting fill*).
* Create the four products in both stores; set up the LevelPlay apps/ad units with active Unity Ads instances,
  the Network Manager adapter + SKAdNetwork option, and the resolved native dependencies (see above); fill in
  Data safety / privacy labels.
* Crash reporting (optional): create a Sentry project (platform Unity), put its DSN in `crashReportingDsn`, run
  *BeamRunner ▸ Crash Reporting ▸ Set Up Sentry Options*, enter the debug-symbol auth token/org/project in
  Tools ▸ Sentry, turn on *Prevent Storing of IP Addresses* in the Sentry project, declare crash data (see
  `docs/STORE_LISTING.md`). On devices: consent off ⇒ nothing in Sentry; on ⇒ a test exception arrives with line
  numbers; a native crash arrives on the next launch.
* Online (optional, see *Online services*): Play Console → Play Games Services: create the game project, link the
  app, add the **SHA-1** of the upload key **and** of the Play App Signing key (Setup and management ▸
  Configuration ▸ credentials), create the leaderboards (best score; Daily Run) and the achievements, enable **Saved
  Games** if `cloudSaveAndroid`, publish the PGS configuration and add testers; then in Unity *Window ▸ Google Play
  Games ▸ Setup ▸ Android Setup* with the resources XML and commit the generated files. App Store Connect: enable
  Game Center for the app, create the leaderboards (Daily Run as a *recurring* leaderboard, 1 day, starting 00:00
  UTC) and achievements; for `cloudSaveIos` enable iCloud for the App ID. Put every id into `online.json`
  (`Validate Project` lists what is missing). On devices: automatic sign-in, SIGN IN after declining, scores and
  achievements appear, cloud save between two devices (buy a ship on one, earn gems on the other, sync both),
  airplane mode, account switch (COMBINE prompt).
* Reminders / rating: run the Android Resolver (Play review library); on devices check the reminder offer after the
  first daily claim, the OS prompt only after "REMIND ME", the 19:00 notification (small white ship icon on
  Android), and the rating prompt at the 3rd and 6th launch (Android: the game's dialog, RATE NOW opens the Play
  Store; iOS: Apple's sheet, shows in development builds, never in TestFlight).
* On devices (development build, dashboard test devices): run the LevelPlay Test Suite; watch a rewarded ad to
  the end (reward) and skip one (no reward); interstitial after the 4th run; airplane mode (buttons hide, game
  never blocks); iOS: ATT prompt appears once after choosing personalised ads, and "Ask App Not to Track" ⇒
  non-personalised.
* IAP on devices (Play license tester / App Store sandbox account, development build to see `[BeamRunner]
  IAP:` logs): localized prices appear; buy a gem pack (gems once, log "confirmed", pack buyable again);
  buy Remove Ads (interstitials stop; buying again says "Already owned"); cancel a purchase (no error toast);
  Android "slow test card" ⇒ "Purchase pending", gems arrive minutes later (also after an app restart);
  kill the app right after paying ⇒ the item arrives on the next launch, exactly once; reinstall ⇒ Remove Ads
  comes back once the store connects (iOS: also via Restore Purchases); airplane mode ⇒ "Store unavailable", recovers
  when back online. In Play Console → Order management, test orders must show as acknowledged/consumed, not
  refunded after 3 days.
