using BeamRunner.Core.Field;
using UnityEngine;
using UnityEngine.Rendering;

namespace BeamRunner.Rendering
{
    /// <summary>
    /// The Daily Run ghost: a translucent hologram of the craft the day's best run was flown with, retracing that run
    /// through the field. Purely visual — it is not part of the simulation, so it never collides. A pale spectral white
    /// (never a level colour), with a strong flicker and fine scanlines, so it is never mistaken for an obstacle or the
    /// player's own craft. It dims while it is close to the player's craft, fades out when it drops behind it (before it
    /// could fill the camera) and where the recorded run ended. Banks and pitches with its own motion like the player's
    /// craft.
    /// </summary>
    public sealed class GhostView : MonoBehaviour
    {
        /// <summary>Pale spectral white: not a level colour, so never mistaken for an obstacle.</summary>
        public static readonly Color GhostColor = new Color(0.80f, 0.86f, 1f, 1f);

        private const float BaseAlpha = 0.85f;
        private const float OverlapAlpha = 0.3f;
        private const float OverlapStart = 3.5f;
        private const float OverlapFull = 1.5f;
        private const float FadeSpeed = 3f;
        private const float BehindCamera = -4f;
        private const float Flicker = 0.32f;
        private const float ReducedFlicker = 0.06f;

        private Transform _model;
        private MeshFilter _hullFilter;
        private MeshRenderer _hull;
        private MeshRenderer _engine;
        private MaterialPropertyBlock _hullBlock;
        private MaterialPropertyBlock _engineBlock;
        private string _design;
        private float _alpha;
        private float _roll;
        private float _pitch;
        private float _yaw;
        private float _lastX;
        private float _lastY;
        private bool _hasLast;
        private float _time;
        private float _worldZ;

        /// <summary>World z of the ghost (0 = alongside the player's craft).</summary>
        public float WorldZ
        {
            get { return _worldZ; }
        }

        /// <summary>True while the ghost is drawn in front of the camera and inside the visible depth of the field.</summary>
        public bool IsVisibleInView
        {
            get
            {
                return gameObject.activeSelf && _alpha > 0.02f && _worldZ > BehindCamera &&
                       _worldZ < FieldMath.VisibleLength;
            }
        }

        /// <summary>Creates the (hidden) ghost under <paramref name="parent"/> (keep the parent at the world origin).</summary>
        public static GhostView Create(Transform parent)
        {
            var go = new GameObject("Ghost");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<GhostView>();
            view.Build();
            go.SetActive(false);
            return view;
        }

        /// <summary>Uses the hull of <paramref name="design"/> (a <see cref="Core.Meta.ShipDefinition.Design"/> id).</summary>
        public void SetDesign(string design)
        {
            if (design == _design && _hullFilter.sharedMesh != null)
            {
                return;
            }

            _design = design;
            _hullFilter.sharedMesh = MeshFactory.Ship(design);
            ShipDesignInfo info = MeshFactory.ShipInfo(design);
            float scale = ShipVisual.HullScaleFor(info);
            _model.localScale = new Vector3(scale, scale, scale);
            Vector3 engine = info.Engines != null && info.Engines.Length > 0 ? info.Engines[0] : new Vector3(0f, 0f, -0.7f);
            _engine.transform.localPosition = new Vector3(0f, engine.y, engine.z - 0.02f);
            float size = info.EngineSize * 1.3f;
            _engine.transform.localScale = new Vector3(size, size, 1f);
        }

        /// <summary>
        /// Places the ghost at (<paramref name="x"/>, <paramref name="y"/>) and world z <paramref name="worldZ"/>.
        /// <paramref name="visible"/> false fades it out (run ended, setting off). It dims while it is within a few metres
        /// of the player's craft at (<paramref name="shipX"/>, <paramref name="shipY"/>, 0).
        /// </summary>
        public void Tick(float x, float y, float worldZ, float shipX, float shipY, bool visible, float dt)
        {
            dt = Mathf.Clamp(dt, 0f, 0.1f);
            if (!gameObject.activeSelf)
            {
                if (!visible)
                {
                    return;
                }

                gameObject.SetActive(true);
                _alpha = 0f;
                _hasLast = false;
            }

            _time += dt;
            _worldZ = worldZ;

            float ox = x - shipX;
            float oy = y - shipY;
            float separation = Mathf.Sqrt(ox * ox + oy * oy + worldZ * worldZ);
            float overlap = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(OverlapStart, OverlapFull, separation));
            float target = visible ? Mathf.Lerp(BaseAlpha, OverlapAlpha, overlap) : 0f;

            // Falling behind the player's craft it would pass right in front of the camera (~7 m back): fade it out first.
            target *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(BehindCamera + 0.5f, -1.5f, worldZ));
            _alpha = Mathf.MoveTowards(_alpha, target, dt * FadeSpeed);
            if (!visible && _alpha <= 0f)
            {
                gameObject.SetActive(false);
                return;
            }

            // Bank, pitch and yaw with its own motion like the player's craft (velocity from frame to frame).
            float vx = 0f;
            float vy = 0f;
            if (_hasLast && dt > 1e-4f)
            {
                vx = (x - _lastX) / dt;
                vy = (y - _lastY) / dt;
            }

            _lastX = x;
            _lastY = y;
            _hasLast = true;
            float steerX = Mathf.Clamp(vx / FieldSpec.MaxLateralSpeed, -1f, 1f);
            float steerY = Mathf.Clamp(vy / FieldSpec.MaxLateralSpeed, -1f, 1f);
            float follow = 1f - Mathf.Exp(-10f * dt);
            _roll = Mathf.Lerp(_roll, -steerX * 34f, follow);
            _yaw = Mathf.Lerp(_yaw, steerX * 6f, follow);
            _pitch = Mathf.Lerp(_pitch, -steerY * 12f, follow);

            transform.SetPositionAndRotation(new Vector3(x, y, worldZ), Quaternion.identity);
            _model.localPosition = new Vector3(0f, 0.03f + Mathf.Sin(_time * 2.1f) * 0.025f, 0f);
            _model.localRotation = Quaternion.Euler(_pitch, _yaw, _roll);

            Color c = GhostColor;
            c.a = _alpha;
            _hullBlock.SetColor(ShaderIds.Color, c);
            _hullBlock.SetFloat(ShaderIds.Flicker, ShipVisual.ReducedFlashing ? ReducedFlicker : Flicker);
            _hullBlock.SetFloat(ShaderIds.Intensity, 1.25f);
            _hull.SetPropertyBlock(_hullBlock);
            _engineBlock.SetFloat(ShaderIds.Intensity, 1.4f * _alpha * (0.85f + 0.15f * Mathf.Sin(_time * 23f)));
            _engine.SetPropertyBlock(_engineBlock);
        }

        /// <summary>Hides the ghost immediately.</summary>
        public void Hide()
        {
            _alpha = 0f;
            _hasLast = false;
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }

        private void Build()
        {
            _hullBlock = new MaterialPropertyBlock();
            _engineBlock = new MaterialPropertyBlock();
            _model = new GameObject("Model").transform;
            _model.SetParent(transform, false);

            var hull = new GameObject("Hologram");
            hull.transform.SetParent(_model, false);
            _hullFilter = hull.AddComponent<MeshFilter>();
            _hull = hull.AddComponent<MeshRenderer>();
            _hull.sharedMaterial = FieldMaterials.Hologram(GhostColor, CullMode.Back);
            FieldMaterials.GuardHologram(_hull);
            Configure(_hull);
            _hullBlock.SetFloat(ShaderIds.Flicker, Flicker);
            _hullBlock.SetFloat(ShaderIds.ScanDensity, 16f);
            _hullBlock.SetFloat(ShaderIds.ScanSpeed, 2.2f);

            var engine = new GameObject("Engine");
            engine.transform.SetParent(_model, false);
            engine.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Quad();
            _engine = engine.AddComponent<MeshRenderer>();
            _engine.sharedMaterial = FieldMaterials.Glow();
            FieldMaterials.GuardAdditive(_engine);
            Configure(_engine);
            _engineBlock.SetColor(ShaderIds.TintColor, GhostColor);

            SetDesign("arrow");
        }

        private static void Configure(MeshRenderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }
}
