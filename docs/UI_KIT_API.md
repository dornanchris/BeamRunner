# UI Kit API (namespace `BeamRunner.UI`, `Scripts/Runtime/UI/Kit/`)

Quick reference for screen builders. Read the source for details; this is the map.

## Conventions
* 1 unit = 1 px on the 1080×1920 reference. The canvas matches width in portrait, height in landscape
  (blends on squarer screens) and re-evaluates on screen changes.
* Spacing 4/8/16/24/32/48/64/96 · radii 12/20/32/48 · type sizes Caption 30, Small 36, Body 42, Lead 48,
  H3 56, H2 72, H1 96, Display 132 · button heights 152 (Primary) / 128 / 96 · icon buttons 132 · setting rows 112.
* Build a screen: subclass `UIScreen`, override `protected Build()` (runs once, before the first show) and put content
  inside `UIFactory.ContentColumn(Rect)`. Create with `UIScreen.Create<T>(root.GetLayer(UILayer.Screens))` (builds now)
  or `UIScreen.CreateDeferred<T>(…)` (builds later — see *Progressive build*), then `root.Stack.SetRoot(screen)` /
  `Push(screen)`. A big screen can override `protected IEnumerator BuildSteps()` instead, with `yield return null`
  between sections, so the build queue can spread it over frames.
* Deferred screens are built while **inactive**: don't rely on a widget's `Awake`/`OnEnable` having run inside `Build`
  (they run at first activation), use `GetComponentInParent<T>(true)`, don't start coroutines there, and let fields
  assigned in a later `BuildSteps` step be null-safe for anything that can run before the build completes (events
  subscribed in `Bind`, e.g. `UIRoot.LayoutChanged`). Public methods that touch widgets before `Show` call
  `EnsureBuilt()` first.
* Neon glows extend ~28 units past panel edges: keep ≥32 units side padding in scroll lists (ScrollList default does).
* The HUD is a `UIScreen` on the HUD layer, shown/hidden directly (outside the stack).
* During runs set `Stack.BackEnabled = false` (else Escape / gamepad B pops screens or fires `BackAtRoot`).
* Zone colours: `UITheme.SetAccent(zone.Primary, zone.Secondary)`.
* Tweens are pooled: never keep a `Tween` reference after it completes — keep its `Id`.
* `Backdrop` adds ~4 full-screen transparent layers — behind menus only, never over gameplay.
* While a dialog is open, the Screens/HUD/Popups layers are non-interactable.
* Tall content (dialogs, pause, revive, daily reward) goes in a wrapper with `UIFitHeight`
  (`Kit/Effects/UIFitHeight.cs`), which uniformly shrinks — never enlarges — the content to fit the available
  height; landscape canvases are only 1080 units tall.
* HUD buttons have keyboard/gamepad navigation disabled (they have direct bindings) so steering keys can't
  move focus onto Pause. Overlays focus their back button on open for gamepad players.
* Focus look: `NeonButton` swells (×1.035) and glows when selected by keyboard/gamepad (not by a pointer); toggles,
  sliders and segments get the same neon frame via `UIFocusRing`. `UITheme.ReducedMotion` drops the swells.
* Text is localized (see `docs/LOCALIZATION.md`): pass `Loc.T("key")` / `Loc.T("key", ("n", count))`
  (`BeamRunner.Core.Localization`), never a literal; numbers via `Loc.N` (per-language grouping, not the device
  culture). The tables hold the final case (upper-case titles): the kit never calls `ToUpper`/`ToLower` on text.
  Brand/debug-only literals carry `// l10n-ok`. `tools/check-strings.py` (CI) enforces all of this.
* Fonts follow the language's script (`UIFonts`), resolved when a widget is built — a language switch rebuilds the
  whole UI (`GameUI`), so widgets never swap fonts or texts themselves.
* Longer translations: single-line labels shrink to fit (`UITextFit`, ≥ 62 % of the design size, English unchanged),
  `NeonTitle` shrinks to ≥ 50 %, buttons grow up to their parent's width, dialogs put long button labels in a column.

## UIRoot (`Navigation/UIRoot.cs`)
`Create(int sortingOrder = 100, Transform parent = null)`, `Ensure()`, `Instance`, `GetLayer(UILayer)`,
`.Stack`, `.Toasts`, `.Dialogs`, `.Fader`, `.BuildQueue`, `IsLandscape`, `CanvasSize`, `event LayoutChanged`, `ShowFps(bool)`.
`LayoutChanged` fires one frame after a resolution/orientation change, once the new canvas scale has been
applied (so handlers read correct sizes).
Layers (draw order): Background, Screens, HUD, Popups, Dialogs, Toasts, Overlay, Debug. Background, Dialogs and
Overlay are full-screen; the rest sit inside the safe area. Each layer is its own nested canvas. Creates an
EventSystem + `InputSystemUIInputModule` only if none exists.

## UIScreen / UIStack
* `UIScreen`: `static T Create<T>(Transform parent, string name = null)` (builds immediately),
  `static T CreateDeferred<T>(Transform parent, string name = null)` (hidden and empty; built by the build queue or by the
  first `Show`/`EnsureBuilt`); overrides `Build()` or `IEnumerator BuildSteps()`, `OnShow()`, `OnShown()`, `OnHide()`,
  `OnHidden()`, `public virtual bool OnBack()`; `Show(bool instant = false)` (builds what is missing first),
  `Hide(bool instant = false)`, `EnsureBuilt()`, `IsBuilt`, `IsVisible`, `Rect`, `Group`, `Transition` (Zoom, Fade,
  SlideUp, SlideLeft, None), `DefaultSelection` (gamepad focus).
* `UIStack`: `Push`, `Pop`, `Replace`, `SetRoot`, `PopTo`, `Clear` (all take `bool instant = false`), `Top`,
  `Count`, `HandleBack()`, `Action BackAtRoot`, `bool BackEnabled`, `event Action<UIScreen> Changed`,
  `IsNavigating`. Back = Escape (Android back) / gamepad B → open dialog first, then top screen `OnBack`, then
  pop, then `BackAtRoot`.

## Progressive build (`Navigation/UIBuildQueue.cs`, `root.BuildQueue`)
Runs queued work a few ms per frame, in order: `Enqueue(UIScreen screen, bool warm = true)` (one `BuildSteps` step per
slot; `warm` then activates it once, invisibly, in a separate slot so `Awake`/first layout/font glyphs are paid before
its first show), `Enqueue(Action job)` (pool warm-ups such as `Dialogs.Prewarm`), `PrewarmIcons()`
(`UIIcons.BeginPrewarm` + uploads with leftover budget), `Flush()`, `BudgetMs` (default 4), `Paused`, `Pending`,
`IsIdle`, `Progress`. It never runs in the frame it was created in nor the next (a UI created at boot shows its splash
first) and runs at least one step per frame; a single step is never split. Nothing waits for it: showing a screen
builds whatever is missing on the spot, `UIIcons.Get` paints an icon that isn't ready. `GameUI` builds only the splash
at boot and queues everything else, the menu first.

## Overlays
* `Dialogs` (queued, pooled): `Confirm(title, body, confirmLabel, cancelLabel, Action<bool>, bool danger = false,
  IconId icon = Info)` (null labels = `common.ok` / `common.cancel`), `Alert(title, body, okLabel = null (common.ok),
  Action onClose = null, icon)`,
  `Choice(title, body, string[] options, Action<int>, int cancelIndex = -1, icon)`,
  `Custom(title, body, Action<RectTransform> buildBody, options, Action<int>, cancelIndex, icon, Color? accent)`,
  `IsOpen`, `CloseAll()`, `Prewarm()` (builds the window + pooled buttons ahead of the first dialog).
* `Toasts`: `Show(string text, IconId icon, Color accent, float duration = 2.6f)`, `Show(string)`, `Prewarm(int count = 1)`.
  Up to two lines; a longer text shrinks to fit (`UITextFit.FitWrapped`).
* `FloatingText.Spawn(RectTransform parent, string text, Color color, Vector2 anchoredPos, float scale = 1f)` → `Text`.
  The text is kept inside the parent (shrunk when wider, pulled in from the edge).
* `ScreenFader` (`.Current` or `root.Fader`): `FadeOut(dur, Color?, Action)`, `FadeIn(dur, Action)`,
  `FadeThrough(Action mid, dur, Color?, Action)`, `SetInstant(alpha)`, `Flash(color, peak, dur)`, `IsBusy`.
* `FpsCounter` via `UIRoot.ShowFps(bool)`.

## UIFactory (`UIFactory.cs`, `UIFactory.Widgets.cs`; parent is always the first argument)
* Text & shapes: `Label(text, size, Color?, TextAnchor, UIFont, TextGlow)` (wraps), `LabelLine(…, bool fit = true)`
  (single line + `UITextFit`), `AddGlow`,
  `Icon(IconId, size, Color?)`, `SetIcon`, `Panel`, `GlassPanel(Color? accent, radius, padding, scanlines, shadow,
  frame)` (children go in `.Content`), `NeonTitle(text, size, align, Color?, Color?)`.
* Buttons: `Button(label, ButtonStyle, Action, IconId, subLabel, height, minWidth)`,
  `IconButton(IconId, Action, size = 132, style)` → `NeonButton`. Styles: Primary, Secondary, Ghost, Danger.
  The label is shown as given (no upper-casing); a free-standing button grows with its label up to its parent's width
  minus 2 × 32, then the label shrinks.
* Controls: `Slider(value, Action<float>, min, max, wholeNumbers, width)`, `Toggle(bool, Action<bool>)`,
  `ToggleRow`, `SliderRow`, `SettingRow`, `SegmentedControl(string[], selected, Action<int>)`,
  `ProgressBar(value, height, Color? from, Color? to)`, `RingProgress(size, value, thickness, Color?)`,
  `Badge(count)` (−1 dot, 0 hidden), `CurrencyPill(long, IconId = Gem, Color?, Action onPlus)`.
* Containers: `ScrollList(spacing, RectOffset, fadeEdge)` (`.Content`, `ScrollTo`, `ScrollIntoView` — least scroll that
  shows the item; follows keyboard/gamepad focus automatically; right stick / Page Up / Page Down scroll it),
  `Card(title, IconId, Color?)`
  (`.Body`, `.HeaderRight`), `StatRow(label, value, IconId, divider)`, `KeyValueGrid(columns)` → `.Add(key, value,
  Color?)`, `Divider`, `Spacer`, `VStack`, `HStack`, `FitContent`, `SetLayout`, `IgnoreLayout`,
  `ContentColumn(maxWidth)`, `Backdrop(opacity, scanlines)`.
* Layout: `Stretch(rt, l, t, r, b)`, `Anchor(rt, AnchorPreset, offset)`, `SetSize`, `Pad`.
  Motion: `StaggerIn(container, delay, step)`, `PopIn`, `Breathe`. Hooks: `static Action OnClickSound`,
  `static Action OnHaptic`.

## Widget handles
`NeonButton.OnClick(Action)`, `SetLabel`, `SetSubLabel`, `SetIcon`, `SetInteractable`, `SetAttention`,
`SetColors`; `ProgressBar` / `RingProgress.SetValue(v, animate)`; `NeonToggle.IsOn`; `NeonSlider.Value`;
`SegmentedControl.Select(i)`; `Badge.SetCount`; `CurrencyPill.SetAmount(long, animate)`;
`NumberTicker.Attach(Text, long).SetValue(long, animate)` (grouped with `Loc.N`); `NeonTitle.SetText`, `SetFont(Font)`
(the brand logo keeps Audiowide in every language); `UITextFit.Attach(Text, minScale)`, `SetBaseSize` (instead of
setting `fontSize`), `IsShrunk`, `DesignWidth`, static `FitWrapped(Text, designSize, minScale)` for wrapping text in a
fixed rect.

## Fonts (`UIFonts.cs`)
`UIFonts.Title` / `Body` / `Bold` / `Get(UIFont)` return the current language's set (`Loc.Language.Script`);
`For(LocScript, UIFont)` a given script's (the language picker draws each name in its own script); `FileName(script,
role)`. Sets: Latin = Audiowide / Rajdhani SemiBold / Rajdhani Bold; Cyrillic = Neo Title Cyrillic (Russo One subset)
/ Tektur condensed Medium / SemiBold; Japanese, Korean, Simplified Chinese = Noto Sans JP/KR/SC Bold (titles and bold)
and Medium (body). Non-Latin fonts are subsets generated by `tools/build-fonts.py`; a missing font falls back to
Latin, then to Unity's LegacyRuntime.

## Input behaviour of the controls
* `NeonSlider` wraps a `ScrollAwareSlider` (`.Slider` is still typed `Slider`). Inside a ScrollRect a drag is arbitrated
  once it passes the EventSystem drag threshold: mostly vertical → forwarded to the list (initialize/begin/drag/end, so it
  scrolls and flings), mostly horizontal → moves the value; a tap sets the value at the tapped point on release (a tap on
  the knob changes nothing); a non-interactable slider forwards every drag. Outside a ScrollRect it is a plain Slider.
  Keyboard/gamepad left/right step it by 10 % (uGUI).
* Buttons, toggles and segments have no drag handler, so a drag that starts on them already scrolls the list and cancels
  their click. Toggles toggle on Submit; segments are separate navigable buttons (left/right between them, Submit selects).
* `UIFocusRing.Attach(Selectable, RectTransform host, radius, outset = 6, RectTransform swell = null)`: accent neon frame
  (Secondary button frame) shown while the control is selected by navigation, `IsFocused`, `event FocusChanged`; used by
  `NeonToggle` (frame + swell), `NeonSlider` (frame + lit knob) and each `SegmentedControl` segment (frame on the thumb
  outline).

## UITween (unscaled time, pooled)
`Fade`, `Scale`, `Move`, `Color`, `Alpha`, `RendererAlpha`, `Fill`, `Size`, `Rotate`, `Punch`, `Shake`,
`Value(from, to, dur, Action<float>)`, `Delay(sec, Action)`; chain `.SetEase(UIEase).SetDelay().SetLoops(n,
yoyo).OnComplete()`, read `.Id`; `Kill(target, complete)`, `KillId`, `IsAlive`. A new tween replaces a running one
of the same kind on the same target; tweens stop silently when their target is destroyed.

## Theme, sprites, icons
* `UITheme`: colour tokens, `Accent` / `AccentAlt`, `SetAccent(ColorRGB, ColorRGB, bool animate = true)`,
  `event AccentChanged`, spacing/radius/type scales, `ReducedMotion` (also disables focus/hover swells), helpers `Hex`,
  `Alpha`, `Lighten`.
* `UISprites`: RoundedRect, RoundedOutline, NeonFrame, SoftRect, Circle, Ring, Glow, RadialGradient, Vignette,
  GradientVertical/Horizontal, Hazard, Scanlines, Noise, Chevrons, FadeLine, plus `Apply*(Image, …)` helpers.
* `UIIcons.Get(IconId)`, `Prewarm(params IconId[])` / `PrewarmAll()` (synchronous), `BeginPrewarm()` (paints every
  icon on a worker thread — pure C# — main-thread-only on WebGL), `PumpPrewarm(budgetMs)` (uploads finished ones),
  `IsPrewarmed`. An icon requested before the worker reached it is painted on the spot.
  42 icons: Play, Pause, Settings, Back, Close, Home, Retry, Trophy, Hangar,
  Missions, Stats, Gem, Lock, Check, Star, SoundOn, SoundOff, Music, Vibration, Gift, Ad, Cart, Shield,
  Magnet, Bolt, Info, Crown, ChevronRight, ChevronLeft, Plus, Restore, Speed, Heart, Calendar, Gamepad, Hand,
  PhoneTilt, Warning, Minus, ChevronUp, ChevronDown, Globe (check `Pure/IconId.cs` for exact names).
