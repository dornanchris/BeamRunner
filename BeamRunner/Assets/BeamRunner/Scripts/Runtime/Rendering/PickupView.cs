using BeamRunner.Core.Field;
using UnityEngine;
using UnityEngine.Rendering;

namespace BeamRunner.Rendering
{
    /// <summary>
    /// Renders one <see cref="Pickup"/> at (x, y, z − shipDistance): a spinning, bobbing neon model (gem crystal,
    /// shield torus, magnet, Overload bolt, fuel cell, repair cross, instant-build chevron) with a soft halo facing the chase camera. A pickup the craft has missed
    /// shrinks and its halo fades before the chase camera reaches it (a power-up's 2.4 m halo would otherwise sweep across
    /// the whole screen). Pooled by <see cref="FieldRenderer"/>.
    /// </summary>
    public sealed class PickupView : MonoBehaviour
    {
        /// <summary>
        /// Power-ups are drawn this much larger than their base model (and collected from
        /// <see cref="FieldSpec.PowerUpPickupRadius"/>): they stand alone, and must be easy to spot and grab
        /// wherever the gem trails lead.
        /// </summary>
        public const float PowerUpScale = 1.6f;

        private Transform _model;
        private MeshFilter _modelFilter;
        private MeshRenderer _modelRenderer;
        private Transform _halo;
        private MeshRenderer _haloRenderer;
        private MaterialPropertyBlock _modelBlock;
        private MaterialPropertyBlock _haloBlock;

        private int _pickupId = -1;
        private PickupKind _kind;
        private float _phase;
        private bool _hidden;
        private bool _gone;
        private float _modelScale = 1f;
        private bool _collectFxDone;
        private Vector3 _lastPosition;
        private Pickup _pickup;

        /// <summary>Frame stamp used by <see cref="FieldRenderer"/> to find stale views.</summary>
        internal int Stamp;

        public int PickupId
        {
            get { return _pickupId; }
        }

        /// <summary>Signature colour of a pickup kind (sRGB).</summary>
        public static Color KindColor(PickupKind kind)
        {
            switch (kind)
            {
                case PickupKind.Shield:
                    return new Color(0.3f, 0.95f, 1f);
                case PickupKind.Magnet:
                    return new Color(1f, 0.2f, 0.36f);
                case PickupKind.Overload:
                    return new Color(1f, 0.84f, 0.25f);
                case PickupKind.Fuel:
                    return new Color(0.35f, 1f, 0.45f);
                case PickupKind.Repair:
                    return new Color(1f, 0.42f, 0.72f);
                case PickupKind.InstantBuild:
                    return new Color(0.45f, 0.62f, 1f);
                default:
                    return Color.Lerp(FieldGlobals.AccentColor, Color.white, 0.45f);
            }
        }

        internal static PickupView Create(Transform parent)
        {
            var go = new GameObject("Pickup");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<PickupView>();
            view.Build();
            return view;
        }

        internal void Bind(Pickup pickup)
        {
            _pickup = pickup;
            _pickupId = pickup.Id;
            _kind = pickup.Kind;
            _hidden = pickup.Collected;
            _collectFxDone = pickup.Collected;
            _phase = (pickup.Id * 0.618034f) % 1f * Mathf.PI * 2f;
            gameObject.SetActive(true);
#if UNITY_EDITOR
            gameObject.name = "Pickup " + pickup.Id + " (" + pickup.Kind + ")";
#endif

            Color color = KindColor(_kind);
            _modelBlock.Clear();
            switch (_kind)
            {
                case PickupKind.Shield:
                    _modelFilter.sharedMesh = MeshFactory.Torus();
                    _modelRenderer.sharedMaterial = FieldMaterials.Obstacle();
                    SetSolidColor(color, 3.4f, 0.3f);
                    _modelScale = PowerUpScale;
                    break;
                case PickupKind.Magnet:
                    _modelFilter.sharedMesh = MeshFactory.Magnet();
                    _modelRenderer.sharedMaterial = FieldMaterials.Obstacle();
                    SetSolidColor(Color.white, 3.2f, 0.28f); // vertex colours paint the red body / white poles
                    _modelScale = 1.1f * PowerUpScale;
                    break;
                case PickupKind.Overload:
                    _modelFilter.sharedMesh = MeshFactory.Bolt();
                    _modelRenderer.sharedMaterial = FieldMaterials.Obstacle();
                    SetSolidColor(color, 3.8f, 0.35f);
                    _modelScale = 1.15f * PowerUpScale;
                    break;
                case PickupKind.InstantBuild:
                    _modelFilter.sharedMesh = MeshFactory.Chevron();
                    _modelRenderer.sharedMaterial = FieldMaterials.Obstacle();
                    SetSolidColor(color, 3.8f, 0.35f);
                    _modelScale = 1.2f * PowerUpScale;
                    break;
                case PickupKind.Repair:
                    _modelFilter.sharedMesh = MeshFactory.Plus();
                    _modelRenderer.sharedMaterial = FieldMaterials.Obstacle();
                    SetSolidColor(color, 3.8f, 0.35f);
                    _modelScale = PowerUpScale;
                    break;
                case PickupKind.Fuel:
                    _modelFilter.sharedMesh = MeshFactory.FuelCell();
                    _modelRenderer.sharedMaterial = FieldMaterials.Obstacle();
                    SetSolidColor(color, 3.6f, 0.4f);
                    _modelScale = 1.15f;
                    break;
                default:
                    _modelFilter.sharedMesh = MeshFactory.Octahedron();
                    _modelRenderer.sharedMaterial = FieldMaterials.Gem();
                    _modelScale = 1.3f;
                    break;
            }

            _model.localScale = Vector3.one * _modelScale;
            _gone = false;

            _modelRenderer.SetPropertyBlock(_modelBlock);
            _haloBlock.SetColor(ShaderIds.TintColor, color);
            _haloBlock.SetFloat(ShaderIds.Intensity, 1f);
            _haloRenderer.SetPropertyBlock(_haloBlock);
            _halo.localScale = Vector3.one * (_kind == PickupKind.Gem || _kind == PickupKind.Fuel ? 0.95f : 1.5f * PowerUpScale);
            _modelRenderer.enabled = !_hidden;
            _haloRenderer.enabled = !_hidden;
        }

        /// <summary>
        /// Positions and animates the pickup (collection FX fire here if the flag is seen first); <paramref name="cameraZ"/>
        /// drives the fade-out of a missed pickup.
        /// </summary>
        internal void Tick(Pickup pickup, double shipDistance, double time, FxLibrary fx, float cameraZ)
        {
            _pickup = pickup;
            float z = FieldMath.WorldZ(pickup.Z, shipDistance);
            if (pickup.Collected && !_collectFxDone)
            {
                _lastPosition = new Vector3(pickup.X, pickup.Y, z);
                Collect(fx);
            }

            if (_hidden)
            {
                return;
            }

            float fade = FieldMath.PassedFade(z + 0.4f, z - 0.4f, cameraZ);
            bool gone = fade <= 0.001f;
            if (gone != _gone)
            {
                _gone = gone;
                _modelRenderer.enabled = !gone;
                _haloRenderer.enabled = !gone;
            }

            if (gone)
            {
                return;
            }

            float t = (float)(time % 3600.0);
            float bob = Mathf.Sin(t * 3.1f + _phase) * 0.07f;
            var position = new Vector3(pickup.X, pickup.Y + bob, z);
            transform.localPosition = position;
            _lastPosition = position;

            if (fade < 1f || _model.localScale.x != _modelScale)
            {
                _model.localScale = Vector3.one * (_modelScale * fade);
            }

            float spinRate = pickup.Attracted ? 9f : (_kind == PickupKind.Gem ? 2.6f : 1.6f);
            _model.localRotation = Quaternion.Euler(0f, (t * spinRate + _phase) * Mathf.Rad2Deg, _kind == PickupKind.Gem ? 0f : Mathf.Sin(t * 1.3f + _phase) * 12f);

            float pulse = 0.85f + 0.15f * Mathf.Sin(t * 5.2f + _phase);
            _haloBlock.SetFloat(ShaderIds.Intensity, (_kind == PickupKind.Gem ? 0.9f : 1.3f) * pulse * fade);
            _haloRenderer.SetPropertyBlock(_haloBlock);
        }

        /// <summary>Collection feedback once: sparkle or power-up burst, then hide.</summary>
        internal void Collect(FxLibrary fx)
        {
            if (_collectFxDone)
            {
                return;
            }

            _collectFxDone = true;
            if (fx != null)
            {
                Color color = KindColor(_kind);
                if (_kind == PickupKind.Gem || _kind == PickupKind.Fuel)
                {
                    fx.Sparkle(_lastPosition, color);
                }
                else
                {
                    fx.PowerUpBurst(_lastPosition, color);
                }
            }

            Hide();
        }

        /// <summary>
        /// Returns the view to the pool. The simulation removes a pickup in the same step it is collected, so if
        /// the collection has not been shown yet it is shown now (null <paramref name="fx"/> suppresses it).
        /// </summary>
        internal void Release(FxLibrary fx, double shipDistance)
        {
            if (fx != null && _pickup != null && _pickup.Collected && !_collectFxDone)
            {
                _lastPosition = new Vector3(_pickup.X, _pickup.Y, FieldMath.WorldZ(_pickup.Z, shipDistance));
                Collect(fx);
            }

            _pickup = null;
            _pickupId = -1;
            gameObject.SetActive(false);
        }

        private void Hide()
        {
            _hidden = true;
            _modelRenderer.enabled = false;
            _haloRenderer.enabled = false;
        }

        private void SetSolidColor(Color color, float edgeIntensity, float bodyGlow)
        {
            FieldMaterials.SetCustomColor(_modelBlock, color);
            _modelBlock.SetColor(ShaderIds.BaseColor, Color.Lerp(new Color(0.03f, 0.03f, 0.05f), color, 0.12f));
            _modelBlock.SetFloat(ShaderIds.EdgeIntensity, edgeIntensity);
            _modelBlock.SetFloat(ShaderIds.EdgeWidth, 0.018f);
            _modelBlock.SetFloat(ShaderIds.BodyGlow, bodyGlow);
            _modelBlock.SetFloat(ShaderIds.RimIntensity, 1.2f);
        }

        private void Build()
        {
            _modelBlock = new MaterialPropertyBlock();
            _haloBlock = new MaterialPropertyBlock();

            var model = new GameObject("Model");
            model.transform.SetParent(transform, false);
            _model = model.transform;
            _modelFilter = model.AddComponent<MeshFilter>();
            _modelRenderer = model.AddComponent<MeshRenderer>();
            Configure(_modelRenderer);

            var halo = new GameObject("Halo");
            halo.transform.SetParent(transform, false);
            halo.transform.localPosition = new Vector3(0f, 0f, 0.05f);
            _halo = halo.transform;
            halo.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Quad();
            _haloRenderer = halo.AddComponent<MeshRenderer>();
            _haloRenderer.sharedMaterial = FieldMaterials.Glow();
            FieldMaterials.GuardAdditive(_haloRenderer);
            Configure(_haloRenderer);
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
