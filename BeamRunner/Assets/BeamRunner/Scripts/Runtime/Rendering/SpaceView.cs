using UnityEngine;
using UnityEngine.Rendering;

namespace BeamRunner.Rendering
{
    /// <summary>
    /// The open space around the track, in world space (it does not bend with the track):
    /// <list type="bullet">
    /// <item>the procedural sky (Sky shader): the haze at the far end of the track, and a nebula, stars and a beacon star
    /// that turn with the world (<see cref="FieldGlobals.SetSkyRotation"/>);</item>
    /// <item><see cref="SpeedLines"/> in a box volume just outside the rails.</item>
    /// </list>
    /// The structure the rails run along and the walls are <see cref="SurfaceView"/>'s (they bend with the track). Two
    /// draw calls.
    /// </summary>
    public sealed class SpaceView : MonoBehaviour
    {
        /// <summary>
        /// Radius of the sky sphere around the camera: beyond everything else (the wall ahead stands up to ~600 m away),
        /// inside the far clip plane.
        /// </summary>
        public const float SkyRadius = 900f;

        private MeshRenderer _sky;
        private SpeedLines _speedLines;
        private bool _visible = true;
        private bool _skyAvailable;
        private int _quality = 2;

        public SpeedLines SpeedLines
        {
            get { return _speedLines; }
        }

        /// <summary>Builds the environment under <paramref name="parent"/> (keep the parent at the world origin).</summary>
        public static SpaceView Create(Transform parent)
        {
            var go = new GameObject("Space");
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            var view = go.AddComponent<SpaceView>();
            view.Build();
            return view;
        }

        /// <summary>
        /// Per-frame update of the speed lines. speed in m/s (0 while the world stands still); speed01 normalised (may
        /// exceed 1 in Overdrive).
        /// </summary>
        public void Tick(float dt, float speed, float speed01, bool overdrive)
        {
            if (_speedLines != null)
            {
                _speedLines.Tick(dt, speed, speed01, overdrive);
            }
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            ApplyVisibility();
            _speedLines.SetVisible(visible);
        }

        /// <summary>Quality tier 0..2: fewer speed lines below 2.</summary>
        public void SetQuality(int level)
        {
            _quality = Mathf.Clamp(level, 0, 2);
            _speedLines.SetDensity(_quality <= 0 ? 0.5f : (_quality == 1 ? 0.8f : 1f));
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            _sky.enabled = _visible && _skyAvailable;
        }

        private void Build()
        {
            // A fallback shader would draw the sphere as a small solid ball: without the real sky the clear colour (the
            // fog colour) is the backdrop instead.
            _skyAvailable = !FieldShaders.IsFallback(FieldShaders.SkyName);
            _sky = CreateRenderer("Sky", transform, MeshFactory.SkySphere(), FieldMaterials.Sky(SkyRadius));

            _speedLines = SpeedLines.Create(transform);
            ApplyVisibility();
            Tick(0f, 0f, 0f, false);
        }

        private static MeshRenderer CreateRenderer(string name, Transform parent, Mesh mesh, Material material)
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
