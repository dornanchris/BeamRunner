using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BeamRunner.Rendering
{
    /// <summary>Transient post-processing accents.</summary>
    public enum PostFxPulse
    {
        NearMiss,
        Crash,
        Overdrive,

        /// <summary>A new level starts (its palette fades in).</summary>
        LevelStart,

        /// <summary>A level was cleared: a short, softer bloom lift.</summary>
        LevelCleared,

        Hit,
    }

    /// <summary>
    /// Runtime URP post-processing: a global <see cref="Volume"/> built in code with Bloom (HDR-friendly),
    /// Tonemapping (None — keeps the neon fully saturated; line cores are authored with a white-hot centre
    /// instead), Vignette, Chromatic Aberration, Lens Distortion and Color Adjustments, animated by pulses
    /// and the Overdrive state. A harmless no-op when URP is not the active pipeline (the shaders carry an
    /// in-shader glow so the look survives without bloom).
    /// </summary>
    public sealed class PostFx : MonoBehaviour
    {
        private const float VolumePriority = 100f;

        private bool _active;
        private Camera _camera;
        private VolumeProfile _profile;
        private Bloom _bloom;
        private Tonemapping _tonemapping;
        private Vignette _vignette;
        private ChromaticAberration _chromatic;
        private LensDistortion _distortion;
        private ColorAdjustments _color;

        private int _quality = 2;
        private bool _reducedFlashing;
        private bool _overdrive;
        private float _overdrive01;
        private float _nearMiss;
        private float _crash;
        private float _overdriveKick;
        private float _level;
        private float _cleared;
        private float _hit;

        /// <summary>True when URP is active and the volume exists.</summary>
        public bool IsActive
        {
            get { return _active; }
        }

        /// <summary>Creates the component (call <see cref="Initialize"/> next).</summary>
        public static PostFx Create(Transform parent)
        {
            var go = new GameObject("PostFx");
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            return go.AddComponent<PostFx>();
        }

        /// <summary>Builds the volume and enables post-processing on <paramref name="cam"/>. No-op without URP.</summary>
        public void Initialize(Camera cam)
        {
            _camera = cam;
            _active = GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset;
            if (!_active)
            {
                return;
            }

            if (_profile == null)
            {
                _profile = ScriptableObject.CreateInstance<VolumeProfile>();
                _profile.name = "BeamRunner PostFx";
                _profile.hideFlags = HideFlags.DontSave;

                _bloom = _profile.Add<Bloom>(true);
                _tonemapping = _profile.Add<Tonemapping>(true);
                _vignette = _profile.Add<Vignette>(true);
                _chromatic = _profile.Add<ChromaticAberration>(true);
                _distortion = _profile.Add<LensDistortion>(true);
                _color = _profile.Add<ColorAdjustments>(true);

                _tonemapping.mode.value = TonemappingMode.None;
                _bloom.scatter.value = 0.68f;
                _bloom.clamp.value = 65472f;
                _bloom.tint.value = Color.white;
                _vignette.color.value = Color.black;
                _vignette.center.value = new Vector2(0.5f, 0.5f);
                _vignette.smoothness.value = 0.45f;
                _vignette.rounded.value = false;
                _distortion.xMultiplier.value = 1f;
                _distortion.yMultiplier.value = 1f;
                _distortion.center.value = new Vector2(0.5f, 0.55f);
                _color.colorFilter.value = Color.white;
                _color.hueShift.value = 0f;

                var volume = gameObject.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = VolumePriority;
                volume.weight = 1f;
                volume.sharedProfile = _profile;
            }

            if (_camera != null)
            {
                UniversalAdditionalCameraData data = _camera.GetUniversalAdditionalCameraData();
                if (data != null)
                {
                    data.renderPostProcessing = true;
                    data.stopNaN = false;
                    data.dithering = true;
                }
            }

            SetQuality(_quality);
            Apply();
        }

        /// <summary>
        /// Quality tier 0..2. 0: light bloom, no chromatic aberration / distortion, no FXAA; 1: + FXAA and
        /// accents; 2: full. On device builds also sets the URP render scale (0.75 / 0.9 / 1.0); the asset is
        /// never modified in the Editor.
        /// </summary>
        public void SetQuality(int level)
        {
            _quality = Mathf.Clamp(level, 0, 2);
            if (!_active)
            {
                return;
            }

            _bloom.highQualityFiltering.value = _quality >= 2;
            _chromatic.active = _quality >= 1;
            _distortion.active = _quality >= 1;

            if (_camera != null)
            {
                UniversalAdditionalCameraData data = _camera.GetUniversalAdditionalCameraData();
                if (data != null)
                {
                    data.antialiasing = _quality >= 1 ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.None;
                }
            }

            if (!Application.isEditor)
            {
                var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (asset != null)
                {
                    asset.renderScale = _quality == 0 ? 0.75f : (_quality == 1 ? 0.9f : 1f);
                }
            }

            Apply();
        }

        /// <summary>Fires a transient accent.</summary>
        public void Pulse(PostFxPulse kind)
        {
            switch (kind)
            {
                case PostFxPulse.NearMiss:
                    _nearMiss = 1f;
                    break;
                case PostFxPulse.Crash:
                    _crash = 1f;
                    break;
                case PostFxPulse.Overdrive:
                    _overdriveKick = 1f;
                    break;
                case PostFxPulse.LevelStart:
                    _level = 1f;
                    break;
                case PostFxPulse.LevelCleared:
                    _cleared = 1f;
                    break;
                case PostFxPulse.Hit:
                    _hit = 1f;
                    break;
            }
        }

        /// <summary>Sustained Overdrive look (stronger bloom, lens push, saturation). Eases in and out.</summary>
        public void SetOverdrive(bool on)
        {
            _overdrive = on;
        }

        /// <summary>Accessibility: removes bright flashes and strong chromatic spikes.</summary>
        public void SetReducedFlashing(bool reduced)
        {
            _reducedFlashing = reduced;
        }

        /// <summary>Clears every pulse and the Overdrive state (e.g. when returning to the menu).</summary>
        public void ResetPulses()
        {
            _nearMiss = _crash = _overdriveKick = _level = _cleared = _hit = 0f;
            _overdrive = false;
            _overdrive01 = 0f;
            Apply();
        }

        /// <summary>Animates pulses. Call once per frame with unscaled delta time.</summary>
        public void Tick(float unscaledDt)
        {
            if (!_active)
            {
                return;
            }

            float dt = Mathf.Clamp(unscaledDt, 0f, 0.1f);
            _overdrive01 = Mathf.MoveTowards(_overdrive01, _overdrive ? 1f : 0f, dt * 2.5f);
            _nearMiss *= Mathf.Exp(-7f * dt);
            _overdriveKick *= Mathf.Exp(-3f * dt);
            _level *= Mathf.Exp(-2.2f * dt);
            _cleared *= Mathf.Exp(-3.5f * dt);
            _hit *= Mathf.Exp(-4.5f * dt);
            _crash *= Mathf.Exp(-1.2f * dt);
            Apply();
        }

        private void Apply()
        {
            if (!_active || _profile == null)
            {
                return;
            }

            float flash = _reducedFlashing ? 0.2f : 1f;
            float od = Smooth(_overdrive01);

            // Bloom: HDR line cores are 2–4, the dark floor and bodies well under 0.1, so a threshold near 1 isolates the neon.
            float bloomBase = _quality == 0 ? 0.55f : 0.85f;
            _bloom.threshold.value = 0.92f - 0.12f * od;
            _bloom.intensity.value = bloomBase + 0.45f * od
                                     + (0.9f * _level + 0.5f * _cleared + 0.6f * _overdriveKick + 1.2f * _crash + 0.35f * _nearMiss) * flash;

            _vignette.intensity.value = Mathf.Clamp01(0.3f + 0.08f * od + 0.22f * _hit + 0.18f * _crash);
            _vignette.color.value = Color.Lerp(Color.black, new Color(0.55f, 0f, 0.08f), Mathf.Clamp01(_hit * 1.4f + _crash * 0.8f));

            float chroma = 0.06f + 0.22f * od + (0.45f * _nearMiss + 0.5f * _hit + 0.7f * _crash + 0.4f * _overdriveKick) * (_reducedFlashing ? 0.4f : 1f);
            _chromatic.intensity.value = Mathf.Clamp01(chroma);

            // Negative intensity = barrel "fish-eye" push that sells speed; scale hides the stretched border.
            float push = 0.16f * od + 0.22f * _overdriveKick + 0.3f * _crash + 0.06f * _nearMiss;
            _distortion.intensity.value = Mathf.Clamp(-push, -1f, 1f);
            _distortion.scale.value = 1f + push * 0.35f;

            _color.postExposure.value = (0.35f * _level + 0.15f * _cleared + 0.25f * _overdriveKick + 0.6f * _crash) * flash;
            _color.contrast.value = 8f + 6f * od;
            _color.saturation.value = 12f + 14f * od - 45f * _crash;
        }

        private static float Smooth(float t)
        {
            return t * t * (3f - 2f * t);
        }

        private void OnDestroy()
        {
            if (_profile != null)
            {
                Destroy(_profile);
                _profile = null;
            }
        }
    }
}
