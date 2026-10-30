using UnityEngine;
using UnityEngine.Rendering;

namespace BeamRunner.Rendering
{
    /// <summary>
    /// Burst effects built from a fixed set of runtime-created, always-playing, zero-emission particle
    /// systems that every effect call emits into (<see cref="ParticleSystem.Emit(ParticleSystem.EmitParams, int)"/>):
    /// no per-effect GameObjects, no allocations after setup.
    /// </summary>
    /// <remarks>
    /// Particles simulate in world space near the craft, but the world streams toward the craft, so each
    /// system drifts its particles toward −Z by a fraction of the track speed (<see cref="SetTrackSpeed"/>,
    /// applied through the Velocity over Lifetime module). Debris and sparks drift at 35–40 % so a smashed wall's or
    /// cube's gems visibly tumble past the camera instead of vanishing in one frame; glows attached to the craft
    /// (power-up rings) barely drift. At a crash the speed is zero, so nothing drifts. Emission shapes are small spheres
    /// around the given point (nothing depends on the old tunnel's cylinder).
    /// </remarks>
    public sealed class FxLibrary : MonoBehaviour
    {
        private sealed class Emitter
        {
            public ParticleSystem System;
            public ParticleSystemRenderer Renderer;
            public MaterialPropertyBlock Block;
            public float Drift;
            public float BaseIntensity;
            public float AppliedVelocity = float.NaN;
        }

        private Emitter _sparks;
        private Emitter _debris;
        private Emitter _rings;
        private Emitter _flash;
        private Emitter _glints;
        private Emitter[] _all;
        private bool _reducedFlashing;

        /// <summary>Creates the library (call once; keep the reference).</summary>
        public static FxLibrary Create(Transform parent)
        {
            var go = new GameObject("FxLibrary");
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            var fx = go.AddComponent<FxLibrary>();
            fx.Build();
            return fx;
        }

        /// <summary>Current track speed (m/s). Call every frame; particles drift toward the ship accordingly.</summary>
        public void SetTrackSpeed(float metresPerSecond)
        {
            if (_all == null)
            {
                return;
            }

            for (int i = 0; i < _all.Length; i++)
            {
                Emitter e = _all[i];
                if (e.Drift <= 0f)
                {
                    continue;
                }

                float vz = -Mathf.Max(0f, metresPerSecond) * e.Drift;
                if (Mathf.Abs(vz - e.AppliedVelocity) > 0.01f || float.IsNaN(e.AppliedVelocity))
                {
                    var vol = e.System.velocityOverLifetime;
                    vol.z = new ParticleSystem.MinMaxCurve(vz);
                    e.AppliedVelocity = vz;
                }
            }
        }

        /// <summary>Accessibility: dims full-screen-ish flashes.</summary>
        public void SetReducedFlashing(bool reduced)
        {
            _reducedFlashing = reduced;
            if (_flash != null)
            {
                SetIntensity(_flash, _flash.BaseIntensity * (reduced ? 0.25f : 1f));
            }
        }

        /// <summary>Removes every live particle (e.g. on run restart).</summary>
        public void Clear()
        {
            if (_all == null)
            {
                return;
            }

            for (int i = 0; i < _all.Length; i++)
            {
                _all[i].System.Clear(true);
            }
        }

        // ------------------------------------------------------------------ effects

        /// <summary>Big burst: flash, double shockwave ring, sparks and tumbling gems.</summary>
        public void Explosion(Vector3 position, Color color)
        {
            Color hot = Color.Lerp(color, Color.white, 0.55f);
            EmitSized(_flash, position, hot, 1, 8f);
            EmitSized(_rings, position, color, 1, 10f);
            EmitSized(_rings, position, hot, 1, 5.5f);
            Emit(_sparks, position, color, 80);
            Emit(_sparks, position, hot, 36);
            Emit(_debris, position, color, 28);
            Emit(_glints, position, hot, 10);
        }

        /// <summary>Gem pickup: a few glints, a pinch of sparks and a small ring.</summary>
        public void Sparkle(Vector3 position, Color color)
        {
            Emit(_glints, position, Color.Lerp(color, Color.white, 0.4f), 5);
            Emit(_sparks, position, color, 10);
            EmitSized(_rings, position, color, 1, 1.4f);
        }

        /// <summary>Gate block or cube smashed in Overdrive / by the shield / on revive.</summary>
        public void SmashBurst(Vector3 position, Color color)
        {
            Color hot = Color.Lerp(color, Color.white, 0.5f);
            EmitSized(_flash, position, hot, 1, 4.5f);
            EmitSized(_rings, position, color, 1, 6.5f);
            Emit(_sparks, position, color, 40);
            Emit(_sparks, position, hot, 16);
            Emit(_debris, position, color, 20);
        }

        /// <summary>Power-up collected (shield, magnet, overdrive cell) — rings and glints around the ship.</summary>
        public void PowerUpBurst(Vector3 position, Color color)
        {
            Color hot = Color.Lerp(color, Color.white, 0.5f);
            EmitSized(_flash, position, hot, 1, 2.6f);
            EmitSized(_rings, position, color, 1, 3.8f);
            EmitSized(_rings, position, hot, 1, 2.2f);
            Emit(_glints, position, hot, 10);
            Emit(_sparks, position, color, 24);
        }

        /// <summary>Near miss: a grinding spray of sparks at the craft's flank facing the obstacle.</summary>
        public void NearMissSpark(Vector3 position, Color color)
        {
            Emit(_sparks, position, Color.Lerp(color, Color.white, 0.6f), 10);
            Emit(_sparks, position, color, 8);
            Emit(_glints, position, Color.white, 1);
        }

        // ------------------------------------------------------------------ emission

        private static void Emit(Emitter e, Vector3 position, Color color, int count)
        {
            if (e == null || count <= 0)
            {
                return;
            }

            var p = new ParticleSystem.EmitParams();
            p.position = position;
            p.applyShapeToPosition = true;
            p.startColor = color;
            e.System.Emit(p, count);
        }

        private static void EmitSized(Emitter e, Vector3 position, Color color, int count, float size)
        {
            if (e == null || count <= 0)
            {
                return;
            }

            var p = new ParticleSystem.EmitParams();
            p.position = position;
            p.applyShapeToPosition = true;
            p.startColor = color;
            p.startSize = size;
            e.System.Emit(p, count);
        }

        // ------------------------------------------------------------------ construction

        private void Build()
        {
            _sparks = CreateEmitter("Sparks", ProceduralTextures.SoftDot(), ParticleSystemRenderMode.Stretch, 3.2f, 0.35f, 900);
            ConfigureSparks(_sparks.System, _sparks.Renderer);

            _debris = CreateEmitter("Debris", Texture2D.whiteTexture, ParticleSystemRenderMode.Mesh, 1.8f, 0.4f, 300);
            ConfigureDebris(_debris.System, _debris.Renderer);

            _rings = CreateEmitter("Rings", ProceduralTextures.Ring(), ParticleSystemRenderMode.Billboard, 2.4f, 0.25f, 48);
            ConfigureRings(_rings.System, _rings.Renderer);

            _flash = CreateEmitter("Flash", ProceduralTextures.SoftDot(), ParticleSystemRenderMode.Billboard, 2.6f, 0f, 16);
            ConfigureFlash(_flash.System, _flash.Renderer);

            _glints = CreateEmitter("Glints", ProceduralTextures.Sparkle(), ParticleSystemRenderMode.Billboard, 3f, 0.15f, 160);
            ConfigureGlints(_glints.System, _glints.Renderer);

            _all = new[] { _sparks, _debris, _rings, _flash, _glints };
            for (int i = 0; i < _all.Length; i++)
            {
                _all[i].System.gameObject.SetActive(true);
                _all[i].System.Play(false);
            }

            SetReducedFlashing(_reducedFlashing);
        }

        private Emitter CreateEmitter(string name, Texture texture, ParticleSystemRenderMode mode, float intensity, float drift, int maxParticles)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(transform, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var renderer = go.GetComponent<ParticleSystemRenderer>();

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0f);
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white);
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.stopAction = ParticleSystemStopAction.None;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;
            shape.radiusThickness = 1f;

            var vol = ps.velocityOverLifetime;
            vol.enabled = drift > 0f;
            vol.space = ParticleSystemSimulationSpace.World;
            vol.x = new ParticleSystem.MinMaxCurve(0f);
            vol.y = new ParticleSystem.MinMaxCurve(0f);
            vol.z = new ParticleSystem.MinMaxCurve(0f);

            renderer.renderMode = mode;
            renderer.sharedMaterial = FieldMaterials.Additive(texture);
            FieldMaterials.GuardAdditive(renderer);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingFudge = 0f;

            var e = new Emitter
            {
                System = ps,
                Renderer = renderer,
                Block = new MaterialPropertyBlock(),
                Drift = drift,
                BaseIntensity = intensity,
            };
            SetIntensity(e, intensity);
            return e;
        }

        private static void SetIntensity(Emitter e, float intensity)
        {
            e.Block.SetFloat(ShaderIds.Intensity, intensity);
            e.Renderer.SetPropertyBlock(e.Block);
        }

        private static Gradient FadeGradient(float holdUntil)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, holdUntil), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        private static void ConfigureSparks(ParticleSystem ps, ParticleSystemRenderer r)
        {
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 15f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(FadeGradient(0.4f));

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.25f)));

            r.velocityScale = 0.03f;
            r.lengthScale = 1.4f;
            r.maxParticleSize = 0.5f;
        }

        private static void ConfigureDebris(ParticleSystem ps, ParticleSystemRenderer r)
        {
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 10f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-7f, 7f);
            rot.y = new ParticleSystem.MinMaxCurve(-7f, 7f);
            rot.z = new ParticleSystem.MinMaxCurve(-7f, 7f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(FadeGradient(0.6f));

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 0.8f), new Keyframe(1f, 0f)));

            r.mesh = MeshFactory.Octahedron();
            r.alignment = ParticleSystemRenderSpace.World;
        }

        private static void ConfigureRings(ParticleSystem ps, ParticleSystemRenderer r)
        {
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f);
            main.startSize = new ParticleSystem.MinMaxCurve(4f);

            var shape = ps.shape;
            shape.enabled = false;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(FadeGradient(0.3f));

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.12f, 0f, 3.5f), new Keyframe(1f, 1f, 0.2f, 0f)));

            r.maxParticleSize = 3f;
        }

        private static void ConfigureFlash(ParticleSystem ps, ParticleSystemRenderer r)
        {
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f);
            main.startSize = new ParticleSystem.MinMaxCurve(4f);

            var shape = ps.shape;
            shape.enabled = false;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.35f, 0.35f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.2f)));

            r.maxParticleSize = 3f;
        }

        private static void ConfigureGlints(ParticleSystem ps, ParticleSystemRenderer r)
        {
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(FadeGradient(0.5f));

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0f)));

            r.maxParticleSize = 0.5f;
        }
    }
}
