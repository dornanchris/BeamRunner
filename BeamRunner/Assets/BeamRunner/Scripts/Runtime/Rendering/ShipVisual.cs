using System.Collections.Generic;
using BeamRunner.Core;
using BeamRunner.Core.Meta;
using BeamRunner.Core.Field;
using UnityEngine;
using UnityEngine.Rendering;

namespace BeamRunner.Rendering
{
    /// <summary>
    /// The player's craft: hull, engine glows and flares, a soft glow projected on the floor below it (a depth cue for its
    /// height), shield bubble, and a track-space ribbon trail. The craft never moves in Z (the world streams past), so the
    /// trail is built from a history of (track distance, x, y) samples placed at world z = −(distance now − distance then).
    /// The hull is scaled to a ~1.3 m wingspan (the collision sphere is <see cref="FieldSpec.ShipRadius"/>, a cell 2 m).
    /// Because the hull is wide but thin (about ±0.12 m vertically) while collision uses a 0.42 m sphere, a faint ring of
    /// exactly that radius (facing the chase camera, not rolling with the hull) shows the real hit area; it flares on a
    /// near miss (<see cref="PulseHitRing"/>).
    /// </summary>
    /// <remarks>Per frame: <see cref="SetPose"/> then <see cref="UpdateTrail"/>.</remarks>
    public sealed class ShipVisual : MonoBehaviour
    {
        private const float TrailLength = 26f;
        private const int TrailCapacity = 96;
        private const float TrailSampleSpacing = 0.45f; // 26 m / 0.45 m < 64 ribbon points
        private const float BlinkHz = 12f;
        private const float ReducedBlinkHz = 4f;

        /// <summary>Visual wingspan every hull is scaled toward (m), within <see cref="MinHullScale"/> … <see cref="MaxHullScale"/>.</summary>
        public const float TargetSpan = 1.3f;

        private const float MinHullScale = 0.85f;
        private const float MaxHullScale = 1.15f;
        private const float MaxBank = 34f;
        private const float MaxPitch = 12f;
        private const float MaxYaw = 6f;

        /// <summary>Resting intensity of the collision-radius ring (0 leaves only the near-miss flare).</summary>
        public const float HitRingIntensity = 0.35f;

        /// <summary>The Ring texture's bright circle sits at 0.78 of the quad's half size.</summary>
        private const float RingTextureRadius = 0.78f;

        /// <summary>Accessibility: slower invulnerability blink.</summary>
        public static bool ReducedFlashing { get; set; }

        /// <summary>Height of the surface under the craft (its rail; the floor far below in the void) for the glow pool.</summary>
        public float GroundY { get; set; } = FieldMath.FloorY;

        private ShipDefinition _definition;
        private ShipDesignInfo _info;
        private Transform _model;
        private Transform _underglow;
        private Transform _shield;
        private readonly List<Renderer> _hullRenderers = new List<Renderer>(8);
        private readonly List<Transform> _engineGlows = new List<Transform>(4);
        private readonly List<Transform> _engineFlares = new List<Transform>(4);
        private readonly List<Renderer> _engineRenderers = new List<Renderer>(8);
        private Renderer _underglowRenderer;
        private MaterialPropertyBlock _hullBlock;
        private MaterialPropertyBlock _glowBlock;
        private MaterialPropertyBlock _flareBlock;
        private MaterialPropertyBlock _underBlock;
        private MaterialPropertyBlock _ringBlock;
        private MeshRenderer _hitRing;
        private float _ringPulse;
        private MeshRenderer _muzzle;
        private MaterialPropertyBlock _muzzleBlock;
        private float _muzzlePulse;
        private MaterialPropertyBlock _trailBlock;
        private bool _flashApplied;

        private GameObject _trailObject;
        private MeshRenderer _trailRenderer;
        private RibbonMesh _trail;
        private readonly double[] _historyDistance = new double[TrailCapacity];
        private readonly float[] _historyX = new float[TrailCapacity];
        private readonly float[] _historyY = new float[TrailCapacity];
        private int _historyHead = -1;
        private int _historyCount;

        private Color _bodyColor;
        private Color _glowColor;
        private Color _trailColor;

        private float _x;
        private float _y;
        private float _roll;
        private float _pitch;
        private float _yaw;
        private float _hullScale = 1f;
        private float _time;
        private float _spin;
        private float _boost;
        private float _boostSmooth;
        private float _flash;
        private float _shieldScale;
        private bool _shieldOn;
        private bool _blink;
        private bool _visible = true;
        private bool _showcase;
        private float _lastSpeed;

        public ShipDefinition Definition
        {
            get { return _definition; }
        }

        public ShipDesignInfo Info
        {
            get { return _info; }
        }

        /// <summary>Current world position of the ship's centre.</summary>
        public Vector3 Position
        {
            get { return transform.position; }
        }

        public bool Visible
        {
            get { return _visible; }
        }

        /// <summary>Uniform scale applied to the hull so its span is about <see cref="TargetSpan"/>.</summary>
        public float HullScale
        {
            get { return _hullScale; }
        }

        /// <summary>Scale that brings a design's span close to <see cref="TargetSpan"/>.</summary>
        public static float HullScaleFor(ShipDesignInfo info)
        {
            if (info == null || info.Span <= 0.01f)
            {
                return 1f;
            }

            return Mathf.Clamp(TargetSpan / info.Span, MinHullScale, MaxHullScale);
        }

        /// <summary>Builds a ship for a catalog definition (null → the default ship).</summary>
        public static ShipVisual Create(Transform parent, ShipDefinition def)
        {
            if (def == null)
            {
                def = ShipCatalog.Get(ShipCatalog.DefaultShipId);
            }

            var go = new GameObject("Ship " + def.Id);
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            var ship = go.AddComponent<ShipVisual>();
            ship.Build(def);
            return ship;
        }

        // ------------------------------------------------------------------ per-frame API

        /// <summary>
        /// Places the craft at (<paramref name="x"/>, <paramref name="y"/>, 0), up = +Y, banking (roll) into sideways
        /// motion, pitching with vertical motion and yawing slightly. Velocities in m/s (full bank at
        /// <see cref="FieldSpec.MaxLateralSpeed"/>). Also animates hover, blink, shield, engines and the floor glow, so call
        /// it every frame (dt = frame delta).
        /// </summary>
        public void SetPose(float x, float y, float vx, float vy, float dt)
        {
            dt = Mathf.Clamp(dt, 0f, 0.1f);
            _time += dt;
            _x = x;
            _y = y;

            float steerX = Mathf.Clamp(vx / FieldSpec.MaxLateralSpeed, -1f, 1f);
            float steerY = Mathf.Clamp(vy / FieldSpec.MaxLateralSpeed, -1f, 1f);
            float follow = 1f - Mathf.Exp(-10f * dt);
            _roll = Mathf.Lerp(_roll, _showcase ? 0f : -steerX * MaxBank, follow);
            _yaw = Mathf.Lerp(_yaw, _showcase ? 0f : steerX * MaxYaw, follow);
            _pitch = Mathf.Lerp(_pitch, _showcase ? 0f : -steerY * MaxPitch, follow);

            float hover = Mathf.Sin(_time * 2.3f) * (_showcase ? 0.06f : 0.022f);
            float pitch = _pitch + (_showcase ? Mathf.Sin(_time * 0.9f) * 2.5f : 0f) - _boostSmooth * 2f;
            if (_showcase)
            {
                _spin += dt * 32f;
            }
            else
            {
                _spin = Mathf.MoveTowards(_spin, Mathf.Round(_spin / 360f) * 360f, dt * 200f);
            }

            transform.SetPositionAndRotation(new Vector3(x, y, 0f), Quaternion.identity);
            _model.localPosition = new Vector3(0f, hover, 0f);
            _model.localRotation = Quaternion.Euler(0f, _spin, 0f) * Quaternion.Euler(pitch, _yaw, _roll);

            AnimateEngines(dt);
            AnimateUnderglow();
            AnimateHitRing(dt);
            AnimateMuzzle(dt);
            AnimateShield(dt);
            AnimateHull(dt);
        }

        /// <summary>A volley left the nose: a short flare at the muzzle (auto-fire calls this several times a second).</summary>
        public void MuzzleFlash()
        {
            _muzzlePulse = 1f;
        }

        /// <summary>Flares the collision-radius ring (near miss); a softer flare with reduced flashing.</summary>
        public void PulseHitRing()
        {
            _ringPulse = 1f;
        }

        /// <summary>Records the trail history and rebuilds the ribbon. distance = track distance, speed in m/s.</summary>
        public void UpdateTrail(double distance, float speed)
        {
            _lastSpeed = speed;
            if (_trail == null)
            {
                return;
            }

            bool show = _visible && !_showcase;
            _trailRenderer.enabled = show;
            if (!show)
            {
                return;
            }

            // A jump backwards (new run / revive) or a large teleport restarts the history.
            if (_historyCount > 0)
            {
                double newest = _historyDistance[_historyHead];
                if (distance < newest - 0.01 || distance - newest > TrailLength * 4.0)
                {
                    _historyCount = 0;
                }
            }

            if (_historyCount == 0 || distance - _historyDistance[_historyHead] >= TrailSampleSpacing)
            {
                _historyHead = (_historyHead + 1) % TrailCapacity;
                _historyDistance[_historyHead] = distance;
                _historyX[_historyHead] = _x;
                _historyY[_historyHead] = _y;
                _historyCount = Mathf.Min(_historyCount + 1, TrailCapacity);
            }

            float speedFade = Mathf.Clamp01(speed / 25f);
            float tailZ = _info.TailZ * _hullScale;
            float[] offsets = _info.TrailOffsets;
            int strips = Mathf.Min(offsets.Length, 2);
            for (int s = 0; s < strips; s++)
            {
                _trail.BeginStrip(s);
                float offset = offsets[s] * _hullScale;
                AddTrailPoint(_x, _y, 0f, tailZ, offset, speedFade);
                for (int k = 0; k < _historyCount && _trail.HasRoom; k++)
                {
                    int idx = (_historyHead - k + TrailCapacity) % TrailCapacity;
                    float age = (float)(distance - _historyDistance[idx]);
                    if (age <= 0.02f)
                    {
                        continue;
                    }

                    if (age >= TrailLength)
                    {
                        AddTrailPoint(_historyX[idx], _historyY[idx], TrailLength, tailZ, offset, speedFade);
                        break;
                    }

                    AddTrailPoint(_historyX[idx], _historyY[idx], age, tailZ, offset, speedFade);
                }

                _trail.EndStrip();
            }

            for (int s = strips; s < 2; s++)
            {
                _trail.ClearStrip(s);
            }

            _trail.Upload();
            _trailBlock.SetFloat(ShaderIds.Intensity, 2f + 1.6f * _boostSmooth);
            _trailRenderer.SetPropertyBlock(_trailBlock);
        }

        private void AddTrailPoint(float x, float y, float age, float tailZ, float offset, float speedFade)
        {
            // A flat, horizontal ribbon: seen from the chase camera above and behind, it reads at any height.
            float t = age / TrailLength;
            float z = tailZ - age;
            Vector3 right = Vector3.right;
            var centre = new Vector3(x + offset, y, z);
            float width = _info.TrailWidth * _hullScale * (1f + 0.6f * _boostSmooth) * (1f - 0.55f * t);
            float fade = 1f - t;
            Color32 c = _trailColor;
            c.a = (byte)Mathf.RoundToInt(Mathf.Clamp01(fade * fade * speedFade) * 255f);
            _trail.AddPoint(centre, right * (width * 0.5f), c, age * 0.1f);
        }

        /// <summary>Engine boost 0..1 (bigger glows, long flares, brighter trail).</summary>
        public void SetBoost(float boost01)
        {
            _boost = Mathf.Clamp01(boost01);
        }

        /// <summary>Shows or hides the shield bubble (pops in / out).</summary>
        public void SetShield(bool on)
        {
            _shieldOn = on;
        }

        /// <summary>Invulnerability blink.</summary>
        public void SetBlink(bool on)
        {
            _blink = on;
        }

        /// <summary>White-hot hit flash on the hull (e.g. shield broken).</summary>
        public void Flash()
        {
            _flash = 1f;
        }

        /// <summary>Blows the ship up: explosion FX at its position, then hides it.</summary>
        public void Explode(FxLibrary fx)
        {
            if (fx != null)
            {
                fx.Explosion(transform.position, _glowColor);
            }

            SetVisible(false);
        }

        /// <summary>Shows / hides everything (hull, glows, trail).</summary>
        public void SetVisible(bool visible)
        {
            _visible = visible;
            _model.gameObject.SetActive(visible);
            _underglow.gameObject.SetActive(visible);
            _hitRing.gameObject.SetActive(visible);
            if (_trailRenderer != null)
            {
                _trailRenderer.enabled = visible && !_showcase;
            }

            if (visible)
            {
                _historyCount = 0;
            }
        }

        /// <summary>Hangar / menu turntable mode: no trail, idle hover and a slow spin.</summary>
        public void SetShowcase(bool showcase)
        {
            _showcase = showcase;
            if (_trailRenderer != null)
            {
                _trailRenderer.enabled = _visible && !showcase;
            }

            _historyCount = 0;
        }

        // ------------------------------------------------------------------ animation

        private void AnimateEngines(float dt)
        {
            _boostSmooth = Mathf.Lerp(_boostSmooth, _boost, 1f - Mathf.Exp(-6f * dt));
            float flicker = 0.9f + 0.1f * Mathf.PerlinNoise(_time * 18f, 0.37f);
            float idle = _showcase ? 0.65f : 1f;
            float size = _info.EngineSize * (1f + 0.8f * _boostSmooth) * flicker * idle;
            float flareLength = (0.55f + 2.6f * _boostSmooth + 0.5f * Mathf.Clamp01(_lastSpeed / 90f)) * flicker * idle;
            float flareWidth = _info.EngineSize * 0.7f * (1f + 0.3f * _boostSmooth);
            for (int i = 0; i < _engineGlows.Count; i++)
            {
                _engineGlows[i].localScale = new Vector3(size, size, 1f);
                _engineFlares[i].localScale = new Vector3(flareWidth, flareWidth, flareLength);
            }

            _glowBlock.SetFloat(ShaderIds.Intensity, (2.4f + 1.8f * _boostSmooth) * flicker);
            _flareBlock.SetFloat(ShaderIds.Intensity, (1.6f + 1.6f * _boostSmooth) * flicker);
            for (int i = 0; i < _engineRenderers.Count; i++)
            {
                _engineRenderers[i].SetPropertyBlock(i % 2 == 0 ? _glowBlock : _flareBlock);
            }
        }

        /// <summary>
        /// The ground glow: a soft pool of the engine colour on the rail (or the far floor) straight below the craft,
        /// brightest and tightest when it rides low, wide and faint high above.
        /// </summary>
        private void AnimateUnderglow()
        {
            float height = Mathf.Max(0f, _y - GroundY);
            _underglow.localPosition = new Vector3(0f, GroundY + 0.03f - _y, -0.15f);
            float spread = 1f + height * 0.09f;
            _underglow.localScale = new Vector3(_info.Span * _hullScale * 1.6f * spread, _info.Length * _hullScale * 2.1f * spread, 1f);
            float falloff = Mathf.Exp(-height / 5f);
            _underBlock.SetFloat(ShaderIds.Intensity,
                (0.7f + 0.35f * _boostSmooth) * falloff * (0.95f + 0.05f * Mathf.Sin(_time * 7f)));
            _underglowRenderer.SetPropertyBlock(_underBlock);
        }

        private void AnimateShield(float dt)
        {
            float target = _shieldOn && _visible ? 1f : 0f;
            _shieldScale = Mathf.MoveTowards(_shieldScale, target, dt * 5f);
            bool show = _shieldScale > 0.001f;
            if (_shield.gameObject.activeSelf != show)
            {
                _shield.gameObject.SetActive(show);
            }

            if (show)
            {
                float pop = _shieldOn ? 1f + 0.12f * Mathf.Sin(_shieldScale * Mathf.PI) : _shieldScale;
                float breathe = 1f + 0.02f * Mathf.Sin(_time * 3.1f);
                float d = _info.Length * 1.22f * _shieldScale * pop * breathe; // inside the scaled model
                _shield.localScale = new Vector3(d, d * 0.62f, d);
            }
        }

        private void AnimateHitRing(float dt)
        {
            bool show = _visible && !_showcase;
            if (_hitRing.enabled != show)
            {
                _hitRing.enabled = show;
            }

            if (!show)
            {
                _ringPulse = 0f;
                return;
            }

            _ringPulse = Mathf.MoveTowards(_ringPulse, 0f, dt * 2.5f);
            float pulse = _ringPulse * _ringPulse * (ReducedFlashing ? 0.6f : 2f);
            _ringBlock.SetFloat(ShaderIds.Intensity, HitRingIntensity + pulse);
            _hitRing.SetPropertyBlock(_ringBlock);
        }

        private void AnimateHull(float dt)
        {
            bool hidden = false;
            if (_blink && _visible)
            {
                float hz = ReducedFlashing ? ReducedBlinkHz : BlinkHz;
                hidden = Mathf.Repeat(_time * hz, 1f) > 0.55f;
            }

            for (int i = 0; i < _hullRenderers.Count; i++)
            {
                _hullRenderers[i].enabled = !hidden;
            }

            if (_flash > 0f || _flashApplied)
            {
                _flash = Mathf.MoveTowards(_flash, 0f, dt * 3.5f);
                _hullBlock.SetFloat(ShaderIds.Flash, _flash * (ReducedFlashing ? 0.35f : 1f));
                for (int i = 0; i < _hullRenderers.Count; i++)
                {
                    _hullRenderers[i].SetPropertyBlock(_hullBlock);
                }

                _flashApplied = _flash > 0f;
            }
        }

        // ------------------------------------------------------------------ construction

        private void Build(ShipDefinition def)
        {
            _definition = def;
            _bodyColor = FieldGlobals.ToColor(def.Body);
            _glowColor = FieldGlobals.ToColor(def.Glow);
            // The trail colour travels as a raw vertex colour, which Unity never converts sRGB → linear (only
            // Material / MaterialPropertyBlock SetColor do), so convert it here or it renders washed out.
            _trailColor = FieldGlobals.ToShaderSpace(FieldGlobals.ToColor(def.Trail));
            _info = MeshFactory.ShipInfo(def.Design);
            _hullScale = HullScaleFor(_info);

            _hullBlock = new MaterialPropertyBlock();
            _glowBlock = new MaterialPropertyBlock();
            _flareBlock = new MaterialPropertyBlock();
            _underBlock = new MaterialPropertyBlock();
            _trailBlock = new MaterialPropertyBlock();

            _model = new GameObject("Model").transform;
            _model.SetParent(transform, false);
            _model.localScale = new Vector3(_hullScale, _hullScale, _hullScale);

            var hull = CreatePart("Hull", _model, MeshFactory.Ship(def.Design), FieldMaterials.Ship(_bodyColor, _glowColor));
            _hullRenderers.Add(hull);

            BuildEngines(_info.Engines);
            BuildUnderglow();
            BuildShield();
            BuildHitRing();
            BuildMuzzle();
            BuildTrail();
        }

        private void BuildEngines(Vector3[] engines)
        {
            Color hot = Color.Lerp(_glowColor, Color.white, 0.35f);
            _glowBlock.SetColor(ShaderIds.TintColor, hot);
            _flareBlock.SetColor(ShaderIds.TintColor, _glowColor);
            for (int i = 0; i < engines.Length; i++)
            {
                MeshRenderer glow = CreatePart("EngineGlow", _model, MeshFactory.Quad(), FieldMaterials.Glow());
                FieldMaterials.GuardAdditive(glow);
                glow.transform.localPosition = engines[i] + new Vector3(0f, 0f, -0.02f);
                _engineGlows.Add(glow.transform);
                _engineRenderers.Add(glow);

                MeshRenderer flare = CreatePart("EngineFlare", _model, MeshFactory.FlareCross(), FieldMaterials.Additive(ProceduralTextures.Streak()));
                FieldMaterials.GuardAdditive(flare);
                flare.transform.localPosition = engines[i];
                _engineFlares.Add(flare.transform);
                _engineRenderers.Add(flare);
            }
        }

        private void BuildUnderglow()
        {
            // Lies flat on the floor (the quad faces −Z; rotated to face up), repositioned under the craft every frame.
            MeshRenderer under = CreatePart("FloorGlow", transform, MeshFactory.Quad(), FieldMaterials.Glow());
            FieldMaterials.GuardAdditive(under);
            _underglow = under.transform;
            _underglow.localPosition = new Vector3(0f, FieldMath.FloorY + 0.03f, -0.15f);
            _underglow.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _underglow.localScale = new Vector3(_info.Span * _hullScale * 1.6f, _info.Length * _hullScale * 2.1f, 1f);
            _underglowRenderer = under;
            _underBlock.SetColor(ShaderIds.TintColor, _glowColor);
        }

        private void BuildHitRing()
        {
            // On the craft's transform (not the model): the collision sphere neither rolls nor scales with the hull. Just
            // behind the craft's centre, so the hull hides its middle and only the rim above and below shows.
            _ringBlock = new MaterialPropertyBlock();
            _hitRing = CreatePart("HitRing", transform, MeshFactory.Quad(), FieldMaterials.Additive(ProceduralTextures.Ring()));
            FieldMaterials.GuardAdditive(_hitRing);
            float size = FieldSpec.ShipRadius / (RingTextureRadius * 0.5f);
            _hitRing.transform.localPosition = new Vector3(0f, 0f, 0.08f);
            _hitRing.transform.localScale = new Vector3(size, size, 1f);
            _ringBlock.SetColor(ShaderIds.TintColor, Color.Lerp(_glowColor, Color.white, 0.5f));
            _ringBlock.SetFloat(ShaderIds.Intensity, HitRingIntensity);
            _ringBlock.SetFloat(ShaderIds.FogAmount, 0f);
            _hitRing.SetPropertyBlock(_ringBlock);
        }

        private void BuildMuzzle()
        {
            // On the craft's transform, just ahead of the nose, pointing forward (the flare's head is at its origin).
            _muzzleBlock = new MaterialPropertyBlock();
            _muzzle = CreatePart("Muzzle", transform, MeshFactory.FlareCross(), FieldMaterials.Additive(ProceduralTextures.Streak()));
            FieldMaterials.GuardAdditive(_muzzle);
            _muzzle.transform.localPosition = new Vector3(0f, 0f, _info.Length * _hullScale * 0.5f + 0.55f);
            _muzzle.transform.localScale = new Vector3(0.55f, 0.55f, 0.9f);
            _muzzleBlock.SetColor(ShaderIds.TintColor, Color.Lerp(_glowColor, Color.white, 0.6f));
            _muzzleBlock.SetFloat(ShaderIds.Intensity, 0f);
            _muzzle.SetPropertyBlock(_muzzleBlock);
            _muzzle.enabled = false;
        }

        private void AnimateMuzzle(float dt)
        {
            bool show = _visible && !_showcase && _muzzlePulse > 0.01f;
            if (_muzzle.enabled != show)
            {
                _muzzle.enabled = show;
            }

            if (!show)
            {
                return;
            }

            _muzzlePulse = Mathf.MoveTowards(_muzzlePulse, 0f, dt * 9f);
            _muzzleBlock.SetFloat(ShaderIds.Intensity, _muzzlePulse * (ReducedFlashing ? 1f : 2.4f));
            _muzzle.SetPropertyBlock(_muzzleBlock);
        }

        private void BuildShield()
        {
            Color shieldColor = new Color(0.45f, 0.95f, 1f);
            MeshRenderer shield = CreatePart("Shield", _model, MeshFactory.Sphere(), FieldMaterials.Hologram(shieldColor));
            FieldMaterials.GuardHologram(shield);
            _shield = shield.transform;
            _shield.localPosition = new Vector3(0f, 0.04f, 0f);
            _shield.gameObject.SetActive(false);
        }

        private void BuildTrail()
        {
            _trailObject = new GameObject("Ship Trail");
            _trailObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            float extent = FieldSpec.HalfExtent + 2f;
            var bounds = new Bounds(new Vector3(0f, 0f, -TrailLength * 0.5f), new Vector3(extent * 2f, extent * 2f, TrailLength + 6f));
            _trail = new RibbonMesh("BeamRunner Craft Trail", 2, 64, bounds);
            _trailObject.AddComponent<MeshFilter>().sharedMesh = _trail.Mesh;
            _trailRenderer = _trailObject.AddComponent<MeshRenderer>();
            _trailRenderer.sharedMaterial = FieldMaterials.Additive(ProceduralTextures.Trail());
            FieldMaterials.GuardAdditive(_trailRenderer);
            _trailRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _trailRenderer.receiveShadows = false;
            _trailBlock.SetColor(ShaderIds.TintColor, Color.white);
            _trailBlock.SetFloat(ShaderIds.Intensity, 2f);
            _trailRenderer.SetPropertyBlock(_trailBlock);
            _trail.ClearStrip(0);
            _trail.ClearStrip(1);
            _trail.Upload();
        }

        private void OnDestroy()
        {
            if (_trailObject != null)
            {
                Destroy(_trailObject);
            }

            // The trail mesh is created per ship and tracked by RenderCache, which only frees assets in the Editor.
            if (_trail != null)
            {
                RenderCache.Release(_trail.Mesh);
            }
        }

        private void OnDisable()
        {
            if (_trailRenderer != null)
            {
                _trailRenderer.enabled = false;
            }
        }

        private static MeshRenderer CreatePart(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }
    }
}
