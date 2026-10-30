# BeamRunner — Rendering layer (`BeamRunner.Rendering`)

## The look

Open neon space. The craft flies along +Z over a near-black floor carrying a razor-sharp grid: lane lines on the cell
boundaries (so the field's 9 lanes line up with the gates' cells), bright rails along the field's sides with light pulses
racing away, cross lines that scroll toward you, a beat ring flying off every other bar, and speed streaks that thicken
with speed. The far floor melts into the level's fog colour with a horizon glow that the sky continues: a gradient up to a
deep zenith, a thin horizon band, twinkling stars and a low synthwave sun off to the left (kept away from the vanishing
point, where far gates would sit on it as dark squares). A dim see-through grid ceiling
hangs above the field, a faint square frame of light outlines the play area every 20 m (on the floor's major lines), and
light streaks rush past just outside the field. In levels with deadly edges a hot red **edge fence** (rings halfway
between the frame's, rails along its four corners) closes in during the level intro, throbs while it is deadly and flares
as the craft nears it; the white frame dims meanwhile so the edge reads as one line.

Obstacles are dark solids outlined in HDR neon edges: gate walls are 9 × 9 (or wider, for sliding walls) blocks with a
small gap between neighbouring blocks (the sides of an opening are flush with the true cell boundary, so an opening is
drawn exactly as wide as it collides), with an additive frame around every opening so the way through reads from far
away; free cubes tumble, spin and drift. Far obstacles fade toward whatever is behind them (the fogged floor, the sky),
so nothing pops in as a silhouette; obstacles the craft has passed dissolve before the chase camera reaches them. Everything follows the level palette (`ZoneThemes.ForLevel`), cross-faded on the GPU. Everything
is procedural: meshes, textures, materials and particles are built at runtime. Only the shaders are assets
(`Resources/BeamRunner/Shaders`).

With URP active, a runtime Volume adds bloom, vignette, chromatic aberration, lens distortion and colour grading.
Tonemapping is **None** to keep the neon saturated; line cores get their white-hot centre in the shader instead. Without
URP (Built-in), every shader draws its own soft glow around lines and edges, so the neon still reads.

There is **no curved-world bend** (the tunnel game had one): obstacles are drawn exactly where the collision is, because
the player aims at gate openings from far away.

## Coordinates

Craft at world z ≈ 0, moving freely in X (right) / Y (up) inside the field: 9 × 9 cells of 2 m, X and Y in −9 … +9
(`FieldSpec`). An object at track position `z` is drawn at world `z − s`, `s` = `sim.Distance`, subtracted in double
precision (`FieldMath.WorldZ`). Obstacles are posed on the **obstacle clock** (`sim.GateClock`), exactly like collision:
gate slide offset `Gate.OffsetAt`, gliding hole `Gate.HolePositionAt` / `Gate.BlockedPart`, cube `Cube.PositionAt` / `Cube.RotationAt`
(`new Quaternion(q.X, q.Y, q.Z, q.W)` is the same rotation). Floor at `FieldMath.FloorY` (just under the bottom row, so
wall blocks stand on it), ceiling at `FieldMath.CeilingY` (3 m above the field).

## Shaders (unlit `CGPROGRAM`, no `LightMode` tag → URP `SRPDefaultUnlit` + Built-in)

| Shader | Used for | Key properties |
|---|---|---|
| `BeamRunner/Grid` | floor (opaque) and ceiling (additive) | `_GridIntensity`, `_RailIntensity`, `_PulseIntensity`, `_StreakIntensity`, `_BaseAmount` (1 floor / 0 ceiling), `_CellSize`, `_HalfExtent`, `_SrcBlend`/`_DstBlend`/`_ZWrite` |
| `BeamRunner/Sky` | sky sphere (re-centred on the camera in the vertex stage; queue Geometry+450, ZWrite Off: drawn after the opaques so covered pixels are rejected) | `_Radius`, `_StarDensity`, `_StarIntensity`, `_SunIntensity`, `_SunSize` (band / haze strength is the global `_FieldFogParams.w`) |
| `BeamRunner/NeonSolid` | gate walls, cubes, pickups, procedural ships | `_BaseColor`, `_EdgeColor`, `_RimColor`, `_Palette`, `_EdgeWidth` (m, mesh space), `_EdgeIntensity`, `_BodyGlow`, `_Glow`, `_Flash`, `_StripeAmount`, `_Fade` (`FIELD_DITHER` variant only) |
| `BeamRunner/Additive` | particles, trail, glows, speed lines, play-area frame, gate-opening highlights | `_MainTex` (+ `_MainTex_ST`), `_TintColor`, `_Palette`, `_Intensity`, `_FogAmount` |
| `BeamRunner/Hologram` | shield bubble, Daily Run ghost | `_Color`, `_Palette`, `_Intensity`, `_FresnelPower`, `_ScanDensity`, `_Flicker`, `_Cull` |

* `FieldCommon.cginc` declares the ARCHITECTURE §4 globals and provides `FieldObjectToClip`, the radial fog
  (`FieldFogAt(worldPos)` = `FieldFogFactor(distance from the camera)`), `FieldEndGlow` (horizon glow), `FieldSkyColor(h)`
  (the sky without stars and sun: gradient, band and haze, which start exactly at the horizon), `FieldFogOpaque` (opaque
  things fade toward the fogged floor below the horizon and toward `FieldSkyColor` above it, so at full fog they match
  their background exactly), `FieldFogFloor`, `FieldKeyLight` (the fake key light `_FieldLightDir`), `FieldDitherNoise`,
  AA line helpers and hashes. `_FieldScrollDelta` (metres per frame, set by `FieldGlobals.SetScroll`) motion-blurs the grid's cross lines so
  they never strobe backwards at high speed.
* **`_Palette` = (weight A, weight B, weight C, weight custom)** mixes `_FieldColorA/B/C` with the material's own colour.
  Obstacles use palette weights, so a level cross-fade (`FieldGlobals.LerpTheme`) recolours them with no CPU work.
* **NeonSolid edges** read metric edge distances from UV1 (float4, written by `MeshBuilder`), so edge width is uniform in
  metres on every face — in **mesh** space: a scaled renderer scales its glow too. Cubes therefore use a cached mesh per
  quantised size (`MeshFactory.Cube`, 2 cm steps, prewarmed for 0.3–2.2 m half sizes) with a residual scale within 1 %.
  Meshes without UV1 fall back to per-face UV0 borders (0/1), using `_UVEdgeWidth`.
* NeonSolid's fake lighting: faces toward `_FieldLightDir` (`FieldGlobals.SetLightDirection`, default above-right-front)
  and toward −Z (the camera) read brighter.
* NeonSolid `FIELD_DITHER` (`multi_compile_local`, material `FieldMaterials.ObstacleDithered`): a screen-space dithered
  clip by `_Fade` and by the distance to the camera (dissolving below ~3.5 m, gone at 0.8 m). Gate walls and cubes swap to
  it only while they fade out after the craft has passed them (`FieldMath.PassedFade`) or come within ~4 m of the camera,
  and stop drawing once gone; the plain variant (no discard, full early-Z on mobile GPUs) is the default.
* Tier 0 quality enables the global keyword `FIELD_LITE` (`multi_compile`): no lane pulses or speed streaks on the grid.
* Colours: all authored colours are sRGB and LDR; shaders apply HDR intensity. `Shader.SetGlobalColor` does not convert
  sRGB to linear (6000.0 forwards the raw vector), so `FieldGlobals` converts with `Color.linear` in linear projects.
  `Material` and `MaterialPropertyBlock` `SetColor` do convert, so they receive sRGB. Vertex colours are raw: convert by
  hand (`FieldGlobals.ToShaderSpace`).

## Building the scene (once)

```csharp
var world = new GameObject("World").transform;            // keep at the origin, unscaled
var fx     = FxLibrary.Create(world);
var space  = SpaceView.Create(world);                     // floor, ceiling, sky, play-area frame, edge fence, speed lines
var field  = FieldRenderer.Create(world, fx);             // gates, cubes, pickups (pooled)
var rig    = CameraRig.Create(world);                     // creates the MainCamera + AudioListener
var post   = PostFx.Create(world); post.Initialize(rig.Camera);   // no-op without URP
field.SetCamera(rig.Camera);                              // passed obstacles fade out before this camera
var ship   = ShipVisual.Create(world, ShipCatalog.Get(profile.SelectedShip));
// quality 0..2: FieldGlobals.SetQuality(q); post.SetQuality(q); space.SetQuality(q);
// accessibility: FieldGlobals.SetReducedFlashing(b); post.SetReducedFlashing(b); fx.SetReducedFlashing(b);
//                ShipVisual.ReducedFlashing = b;
```

Swap ships by destroying the `ShipVisual` GameObject and calling `Create` again. Its trail object is cleaned up
automatically.

## Per-frame order (every rendered frame, after `RunSimulation.Step` and the event dispatch)

Before / while dispatching the frame's events: `field.SetCraftPosition(sim.ShipX, sim.ShipY)` (smash bursts and near-miss
sparks go off around the craft's current position).

```csharp
float dt = Time.deltaTime;
bool frozen = sim.IsCrashed || countdown;              // the world stands still
// 1. globals + environment
FieldGlobals.SetScroll(sim.Distance);                 // also feeds SpaceView's frame
FieldGlobals.SetSpeed01(sim.Speed01);
FieldGlobals.SetBeat(audio.BeatPulse, audio.BeatTime); // grid throbs every beat, ring every BeatRingEvery beats
FieldGlobals.SetFlash(flash01);                       // decay your own flash envelope
fx.SetTrackSpeed(frozen ? 0f : sim.Speed);
space.SetEdgeFence(sim.EdgeFence, sim.EdgeFenceArmed, sim.ShipX, sim.ShipY); // hangar: FieldSpec.HalfExtent, false
space.Tick(dt, frozen ? 0f : sim.Speed, sim.Speed01, sim.OverdriveActive);
// 2. craft
ship.SetBoost(sim.OverdriveActive ? 1f : 0f);
ship.SetShield(sim.ShieldActive);
ship.SetBlink(sim.Invulnerable && !sim.ShieldActive);
ship.SetPose(sim.ShipX, sim.ShipY, sim.ShipVelocityX, sim.ShipVelocityY, dt);
ship.UpdateTrail(sim.Distance, sim.Speed);
// 3. field (pass the OBSTACLE CLOCK: all obstacle motion is defined on it)
field.SetCraftPosition(sim.ShipX, sim.ShipY);
field.Sync(sim.Gates, sim.Cubes, sim.Pickups, sim.Distance, sim.GateClock);
// 4. camera, 5. post
rig.Tick(dt, sim.ShipX, sim.ShipY, sim.ShipVelocityX, sim.ShipVelocityY, sim.Speed01, sim.OverdriveActive);
post.SetOverdrive(sim.OverdriveActive);
post.Tick(Time.unscaledDeltaTime);
```

### Events → rendering (dispatched before `field.Sync`)

| RunEvent | Call |
|---|---|
| `Smashed` (gate or cube, shared id space) | `field.OnSmashed(e.Id)` for each (bursts at the wall blocks nearest the craft / at the cube); `rig.Shake(0.35 gate / 0.2 cube)`, `rig.Kick(4)` and the haptic **once per frame** (a revive smashes 15–20 obstacles in one step; the kick is also capped at `CameraRig.MaxKick`) |
| `PickupCollected` | `field.OnPickupCollected(e.Id)` (sparkle / power-up burst) |
| `TrailCompleted` | `fx.PowerUpBurst(ship.Position, FieldGlobals.AccentColor)`, `ship.PulseHitRing()`, `post.Pulse(PostFxPulse.NearMiss)`, `rig.Kick(3)` |
| `NearMiss` | `fx.NearMissSpark(field.NearMissSparkPoint(e.Id, e.X, e.Y, 0.55f), FieldGlobals.PaletteColor(slot))`, `field.OnNearMiss(e.Id)` (flash), `ship.PulseHitRing()`, `post.Pulse(PostFxPulse.NearMiss)`, `rig.Shake(0.1f)` |
| `OverdriveStarted` | `post.Pulse(PostFxPulse.Overdrive)`, `rig.Kick(9)`, `fx.PowerUpBurst(ship.Position, FieldGlobals.PrimaryColor)` |
| `ShieldBroken` | `ship.Flash()`, `post.Pulse(PostFxPulse.Hit)`, `rig.Shake(0.5f)` |
| `LevelStarted` | cross-fade `FieldGlobals.LerpTheme(prev, ZoneThemes.ForLevel(e.Count), t)` over ~2 s, `field.SetTheme(next)`, `post.Pulse(PostFxPulse.LevelStart)` |
| `LevelCleared` | `post.Pulse(PostFxPulse.LevelCleared)`, a small world flash (scaled down by reduced flashing) |
| `Crashed` | `ship.Explode(fx)`, `rig.SetMode(CameraMode.Crash, false)`, `rig.Shake(1)`, `post.Pulse(PostFxPulse.Crash)` |
| `Revived` | `ship.SetVisible(true)`, `rig.SetMode(CameraMode.Run, false)` |
| new run / menu | `field.Clear()`, `fx.Clear()`, `post.ResetPulses()` |

`OnSmashed` / `OnPickupCollected` are idempotent: views also react to the `Smashed` / `Collected` flags during `Sync` and
to obstacles removed in the same step, so the FX play exactly once. Near-miss flashes decay on unscaled frame time, so
they also fade while the run is frozen (crash, revive offer).

Camera modes: `Run` during play (chase camera: follows 85 % of the craft's offset with lag, 7.5 m behind and 1.5 m above,
looking 27 m ahead and a little toward the field's centre; roll from sideways velocity, small pitch from vertical
velocity, FOV kick with speed / Overdrive; portrait keeps ~45–52° horizontal FOV); `Menu` for the attract loop (slow orbit
drifting after the autopilot craft); `Hangar` with `ship.SetShowcase(true)` and the craft at (0, 0); `Crash` after a crash.
Near plane 0.3 m (nothing is drawn closer; it triples the far depth precision of GLES3's 24-bit depth buffer).

The craft carries a faint ring of exactly the 0.42 m collision radius (facing the camera, not rolling with the hull;
`ShipVisual.HitRingIntensity`): the hull is ±0.12 m thick, so without it a vertical graze would crash with visible air.

## Files

`FieldMath` (world helpers), `ShaderIds`, `FieldShaders`, `FieldMaterials`, `FieldGlobals`, `RenderCache` (runtime-asset
ownership), `ProceduralTextures`, `MeshBuilder`, `MeshFactory` (+ `.Ships`: `ShipDesignInfo`), `RibbonMesh`, `FxLibrary`,
`SpeedLines`, `SpaceView`, `TrackRenderer` (rails, ramps, ghost outline, ceiling, obstacles, walls), `FieldRenderer`
(pickups), `PickupView`, `ShipVisual`, `GhostView`, `CameraRig`, `PostFx`.

## Notes and limits

* **Draw calls ≈ visible renderers** (no instancing, no SRP Batcher help: runtime materials + property blocks). Budget:
  environment 5, each gate wall 2, each cube 1, each pickup 2, craft ~7; passed obstacles stop
  drawing once dissolved. Debris-storm levels can
  show 50+ cubes; `Graphics.RenderMeshInstanced` for cubes is the next step if that proves too many on low-end phones
  (needs instancing pragmas in NeonSolid and "Keep All" instancing variants — see the FIRST_RUN_CHECKLIST).
* GPU instancing is off on purpose. The materials are runtime-created, so the default "Strip Unused" instancing setting
  would strip the instanced variants from builds.
* Zero allocations per frame in steady state: pooled views (id diff), owned dynamic meshes rebuilt only on bind and, for a
  gliding hole, on frames it moves, property blocks re-sent past a change threshold, particles via `Emit`, cube meshes prewarmed.
* Graceful fallback: a missing Grid shader hides the ceiling (the floor shows a flat fallback colour); a missing Sky shader
  hides the sky (the camera clears to the fog colour). The transparent shaders (Additive, Hologram) fall back only to
  `Sprites/Default` (soft white sprites / a faint see-through hologram); if that is missing too, `FieldMaterials`'
  guards set `forceRenderingOff` on those renderers instead of letting an opaque shader draw rectangles. If building the
  presentation throws at boot, `GameController.Presentation` stays inert (audio still runs) instead of throwing each frame.
* `PostFx.SetQuality` changes URP `renderScale` only in players, never in the Editor, so the asset is not dirtied.
