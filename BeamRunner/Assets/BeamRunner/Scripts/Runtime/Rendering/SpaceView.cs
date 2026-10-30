using System.Collections.Generic;
using BeamRunner.Core.Field;
using UnityEngine;
using UnityEngine.Rendering;

namespace BeamRunner.Rendering
{
    /// <summary>
    /// The open-space environment around the play field:
    /// <list type="bullet">
    /// <item>a neon grid floor just under the field (Grid shader, opaque): its lane lines sit on the cell boundaries, so
    /// the 9 lanes line up with the gates' cells, and its rails mark the field's sides;</item>
    /// <item>a dimmer see-through grid ceiling above the field (the same shader, additive);</item>
    /// <item>a procedural sky (gradient, horizon band, stars, synthwave sun) whose horizon matches the fogged floor;</item>
    /// <item>a faint additive frame outlining the play area every 20 m (on the floor's major lines) plus rails along its
    /// top corners, scrolling toward the camera with the track so the field boundary always reads;</item>
    /// <item>in levels with deadly edges, the edge fence (<see cref="SetEdgeFence"/>): a hot red ring all round the play
    /// area halfway between the frame's, joined by rails along its four corners, that closes in during the level intro,
    /// throbs while it is deadly and flares as the craft nears it (the frame dims meanwhile, so the edge reads as one
    /// line, not two);</item>
    /// <item><see cref="SpeedLines"/> in a box volume just outside the field.</item>
    /// </list>
    /// Everything is static except the frame and the fence (one transform offset and a vertex-alpha refresh per frame)
    /// and the speed lines; the grid scrolls in its shader from <see cref="FieldGlobals.SetScroll"/>. About five draw
    /// calls (six while a fence stands).
    /// </summary>
    /// <remarks>Per frame, after <see cref="FieldGlobals.SetScroll"/>: <see cref="SetEdgeFence"/>, then <see cref="Tick"/>.</remarks>
    public sealed class SpaceView : MonoBehaviour
    {
        /// <summary>Half width (X) of the floor and ceiling planes: their side edges stay beyond the fog.</summary>
        public const float PlaneHalfWidth = 230f;

        /// <summary>Near (behind the camera) and far ends of the planes (world z).</summary>
        public const float PlaneNear = -40f;

        public const float PlaneFar = 230f;

        /// <summary>Radius of the sky sphere around the camera: beyond everything else, inside the far clip plane.</summary>
        public const float SkyRadius = 265f;

        /// <summary>Spacing of the play-area frame (matches the floor's 20 m major lines; divides 1000).</summary>
        public const float FrameSpacing = 20f;

        private const int FrameStations = 12;
        private const float FrameInset = 0.15f;
        private const float FrameWidth = 0.32f;
        private const float RailWidth = 0.22f;
        private const float FenceWidth = 0.26f;

        /// <summary>
        /// The fence flares from this distance (m) between the craft's collision sphere and the fence: from well before a
        /// craft hugging the floor (safe in the levels before) reaches it.
        /// </summary>
        private const float FenceWarnDistance = 4.5f;

        /// <summary>Hazard colour of the edge fence (sRGB): outside every level palette's blues, pinks and greens.</summary>
        private static readonly Color FenceColor = new Color(1f, 0.2f, 0.12f);

        private MeshRenderer _floor;
        private MeshRenderer _ceiling;
        private MeshRenderer _sky;
        private MeshRenderer _frameRenderer;
        private Transform _frame;
        private Mesh _frameMesh;
        private Color32[] _frameColors;
        private byte[] _frameStation;
        private readonly float[] _stationAlpha = new float[FrameStations];
        private MaterialPropertyBlock _frameBlock;
        private SpeedLines _speedLines;
        private bool _visible = true;
        private bool _skyAvailable;
        private bool _ceilingAvailable;
        private int _quality = 2;
        private float _frameOffsetApplied = float.NaN;

        private MeshRenderer _fenceRenderer;
        private Transform _fence;
        private Mesh _fenceMesh;
        private Color32[] _fenceColors;
        private byte[] _fenceStation;
        private readonly float[] _fenceAlpha = new float[FrameStations];
        private MaterialPropertyBlock _fenceBlock;
        private float _fenceOffsetApplied = float.NaN;
        private float _fencePosition = FieldSpec.HalfExtent;
        private bool _fenceArmed;
        private float _fenceProximity;
        private float _fenceTime;
        private float _fenceShown;

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
        /// Per-frame update: speed lines and the play-area frame (which follows the track distance last passed to
        /// <see cref="FieldGlobals.SetScroll"/>, so it stays locked to the floor's major lines). speed in m/s (0 while
        /// the world stands still); speed01 normalised (may exceed 1 in Overdrive).
        /// </summary>
        public void Tick(float dt, float speed, float speed01, bool overdrive)
        {
            if (_speedLines != null)
            {
                _speedLines.Tick(dt, speed, speed01, overdrive);
            }

            UpdateFrame(overdrive);
            UpdateFence(dt);
        }

        /// <summary>
        /// The edge fence of the displayed run: <paramref name="fence"/> is its distance (m) from the field's centre
        /// (<c>RunSimulation.EdgeFence</c>; <see cref="FieldSpec.HalfExtent"/> or more hides it), <paramref name="armed"/>
        /// whether it is deadly, (<paramref name="shipX"/>, <paramref name="shipY"/>) the craft, whose approach makes it
        /// flare. Call before <see cref="Tick"/>; outside a run pass <see cref="FieldSpec.HalfExtent"/>.
        /// </summary>
        public void SetEdgeFence(float fence, bool armed, float shipX, float shipY)
        {
            _fencePosition = fence;
            _fenceArmed = armed;
            float margin = fence - FieldSpec.ShipRadius - Mathf.Max(Mathf.Abs(shipX), Mathf.Abs(shipY));
            float near = armed ? 1f - Mathf.Clamp01(margin / FenceWarnDistance) : 0f;
            _fenceProximity = near * near;
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            ApplyVisibility();
            _speedLines.SetVisible(visible);
        }

        /// <summary>Quality tier 0..2: fewer speed lines below 2, and no ceiling grid on tier 0 (one full-screen-ish pass less).</summary>
        public void SetQuality(int level)
        {
            _quality = Mathf.Clamp(level, 0, 2);
            _speedLines.SetDensity(_quality <= 0 ? 0.5f : (_quality == 1 ? 0.8f : 1f));
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            _floor.enabled = _visible;
            _ceiling.enabled = _visible && _ceilingAvailable && _quality > 0;
            _sky.enabled = _visible && _skyAvailable;
            _frameRenderer.enabled = _visible;
            _fenceRenderer.enabled = _visible && _fenceShown > 0f;
        }

        // ------------------------------------------------------------------ frame

        private void UpdateFrame(bool overdrive)
        {
            double scroll = FieldGlobals.ScrollDistance;
            double phase = scroll % FrameSpacing;
            if (phase < 0.0)
            {
                phase += FrameSpacing;
            }

            float offset = -(float)phase;

            // The frame steps back while an edge fence stands: the fence is the edge then.
            _frameBlock.SetFloat(ShaderIds.Intensity, (overdrive ? 1.6f : 1.15f) * (1f - 0.75f * _fenceShown));
            _frameRenderer.SetPropertyBlock(_frameBlock);
            if (offset == _frameOffsetApplied)
            {
                return;
            }

            _frameOffsetApplied = offset;
            _frame.localPosition = new Vector3(0f, 0f, offset);

            // Each station fades out as it nears the camera (it passes right around it) and is fogged in the distance.
            for (int k = 0; k < FrameStations; k++)
            {
                float z = k * FrameSpacing + offset;
                float t = Mathf.InverseLerp(3f, 18f, z);
                _stationAlpha[k] = t * t * (3f - 2f * t);
            }

            for (int i = 0; i < _frameColors.Length; i++)
            {
                _frameColors[i].a = (byte)Mathf.RoundToInt(_stationAlpha[_frameStation[i]] * 255f);
            }

            _frameMesh.SetColors(_frameColors, 0, _frameColors.Length,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
        }

        // ------------------------------------------------------------------ edge fence

        private void UpdateFence(float dt)
        {
            // Fully shown once it has closed in half a metre; gone at the field's side.
            _fenceShown = Mathf.Clamp01((FieldSpec.HalfExtent - _fencePosition) / 0.5f);
            bool show = _visible && _fenceShown > 0f;
            if (_fenceRenderer.enabled != show)
            {
                _fenceRenderer.enabled = show;
            }

            if (!show)
            {
                return;
            }

            _fenceTime += dt;
            _fence.localScale = new Vector3(_fencePosition, _fencePosition, 1f);

            // A slow throb while it is deadly (steady with reduced flashing), a flare as the craft nears it.
            float throb = _fenceArmed && !FieldGlobals.ReducedFlashing
                ? 0.82f + 0.18f * Mathf.Sin(_fenceTime * Mathf.PI * 2f * 1.6f)
                : 1f;
            float intensity = (_fenceArmed ? 1.5f : 0.8f) * throb + 2.6f * _fenceProximity;
            _fenceBlock.SetFloat(ShaderIds.Intensity, intensity * _fenceShown);
            _fenceRenderer.SetPropertyBlock(_fenceBlock);

            // Rings halfway between the frame's.
            double phase = (FieldGlobals.ScrollDistance + FrameSpacing * 0.5) % FrameSpacing;
            if (phase < 0.0)
            {
                phase += FrameSpacing;
            }

            float offset = -(float)phase;
            if (offset == _fenceOffsetApplied)
            {
                return;
            }

            _fenceOffsetApplied = offset;
            _fence.localPosition = new Vector3(0f, 0f, offset);
            for (int k = 0; k < FrameStations; k++)
            {
                float z = k * FrameSpacing + offset;
                float t = Mathf.InverseLerp(2f, 14f, z);
                _fenceAlpha[k] = t * t * (3f - 2f * t);
            }

            for (int i = 0; i < _fenceColors.Length; i++)
            {
                _fenceColors[i].a = (byte)Mathf.RoundToInt(_fenceAlpha[_fenceStation[i]] * 255f);
            }

            _fenceMesh.SetColors(_fenceColors, 0, _fenceColors.Length,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
        }

        // ------------------------------------------------------------------ construction

        private void Build()
        {
            Mesh plane = MeshFactory.GridPlane(-PlaneHalfWidth, PlaneHalfWidth, PlaneNear, PlaneFar, 8, 12);
            _floor = CreateRenderer("Floor", transform, plane, FieldMaterials.Floor());
            _floor.transform.localPosition = new Vector3(0f, FieldMath.FloorY, 0f);

            // No ceiling grid: TrackRenderer draws the ceiling as a hazard line just over the top layer.
            _ceilingAvailable = false;
            _ceiling = CreateRenderer("Ceiling", transform, plane, FieldMaterials.Ceiling());
            _ceiling.transform.localPosition = new Vector3(0f, FieldMath.CeilingY, 0f);

            // A fallback shader would draw the sphere as a small solid ball: without the real sky the clear colour (the
            // fog colour) is the backdrop instead.
            _skyAvailable = !FieldShaders.IsFallback(FieldShaders.SkyName);
            _sky = CreateRenderer("Sky", transform, MeshFactory.SkySphere(), FieldMaterials.Sky(SkyRadius));

            BuildFrame();
            BuildFence();
            _speedLines = SpeedLines.Create(transform);
            ApplyVisibility();
            Tick(0f, 0f, 0f, false);
        }

        private void BuildFrame()
        {
            var positions = new List<Vector3>(384);
            var uvs = new List<Vector2>(384);
            var stations = new List<byte>(384);
            var indices = new List<int>(576);

            float h = FieldSpec.HalfExtent + FrameInset;
            float floor = FieldSpec.RailY(0) - 1.5f; // the posts frame the three layers, not the void below
            float w = FrameWidth * 0.5f;
            float r = RailWidth * 0.5f;
            for (int k = 0; k < FrameStations; k++)
            {
                float z = k * FrameSpacing;
                byte s = (byte)k;

                // Arch over the field: left and right posts rising from the floor (whose major line closes the square)
                // and the top bar. v runs across each strip (the Beam texture's profile).
                AddQuad(positions, uvs, stations, indices,
                    new Vector3(-h - w, floor, z), new Vector3(-h - w, h + w, z), new Vector3(-h + w, h + w, z), new Vector3(-h + w, floor, z), s, s);
                AddQuad(positions, uvs, stations, indices,
                    new Vector3(h - w, floor, z), new Vector3(h - w, h + w, z), new Vector3(h + w, h + w, z), new Vector3(h + w, floor, z), s, s);
                AddQuad(positions, uvs, stations, indices,
                    new Vector3(-h - w, h - w, z), new Vector3(h + w, h - w, z), new Vector3(h + w, h + w, z), new Vector3(-h - w, h + w, z), s, s);

                if (k == FrameStations - 1)
                {
                    continue;
                }

                // Rails along the field's top corners, as crossed quads so they read from any angle.
                float z1 = z + FrameSpacing;
                byte s1 = (byte)(k + 1);
                for (int side = -1; side <= 1; side += 2)
                {
                    float x = side * h;
                    AddQuad(positions, uvs, stations, indices,
                        new Vector3(x - r, h, z), new Vector3(x - r, h, z1), new Vector3(x + r, h, z1), new Vector3(x + r, h, z), s, s1);
                    AddQuad(positions, uvs, stations, indices,
                        new Vector3(x, h - r, z), new Vector3(x, h - r, z1), new Vector3(x, h + r, z1), new Vector3(x, h + r, z), s, s1);
                }
            }

            _frameColors = new Color32[positions.Count];
            for (int i = 0; i < _frameColors.Length; i++)
            {
                _frameColors[i] = new Color32(255, 255, 255, 255);
            }

            _frameStation = stations.ToArray();
            _frameMesh = new Mesh();
            _frameMesh.name = "BeamRunner Play Area Frame";
            _frameMesh.MarkDynamic();
            _frameMesh.SetVertices(positions);
            _frameMesh.SetUVs(0, uvs);
            _frameMesh.colors32 = _frameColors;
            _frameMesh.SetTriangles(indices, 0, false);
            _frameMesh.bounds = new Bounds(
                new Vector3(0f, (floor + h) * 0.5f, FrameSpacing * (FrameStations - 1) * 0.5f),
                new Vector3(h * 2f + 2f, h - floor + 2f, FrameSpacing * FrameStations + 4f));
            RenderCache.Track(_frameMesh);

            _frameRenderer = CreateRenderer("PlayAreaFrame", transform, _frameMesh, FieldMaterials.Beam());
            FieldMaterials.GuardAdditive(_frameRenderer);
            _frame = _frameRenderer.transform;
            _frameBlock = new MaterialPropertyBlock();
            _frameBlock.SetVector(ShaderIds.Palette, new Vector4(0.3f, 0.6f, 0f, 0.15f));
            _frameBlock.SetColor(ShaderIds.TintColor, Color.white);
            _frameBlock.SetFloat(ShaderIds.Intensity, 1.15f);
            _frameBlock.SetFloat(ShaderIds.FogAmount, 1f);
            _frameRenderer.SetPropertyBlock(_frameBlock);
        }

        /// <summary>
        /// The edge fence, built for a fence 1 m from the centre and scaled on X/Y to the real one: a full ring at every
        /// station (it stands above the floor, so no floor line closes it) and crossed rails along its four corners.
        /// </summary>
        private void BuildFence()
        {
            var positions = new List<Vector3>(512);
            var uvs = new List<Vector2>(512);
            var stations = new List<byte>(512);
            var indices = new List<int>(768);

            // Widths in the unit mesh, for the fence's usual distance (8–9 m: within a few per cent everywhere).
            float scale = FieldSpec.HalfExtent - 1f;
            float w = FenceWidth * 0.5f / scale;
            float r = RailWidth * 0.5f / scale;
            for (int k = 0; k < FrameStations; k++)
            {
                float z = k * FrameSpacing;
                byte s = (byte)k;
                AddQuad(positions, uvs, stations, indices,
                    new Vector3(-1f - w, -1f - w, z), new Vector3(-1f - w, 1f + w, z), new Vector3(-1f + w, 1f + w, z), new Vector3(-1f + w, -1f - w, z), s, s);
                AddQuad(positions, uvs, stations, indices,
                    new Vector3(1f - w, -1f - w, z), new Vector3(1f - w, 1f + w, z), new Vector3(1f + w, 1f + w, z), new Vector3(1f + w, -1f - w, z), s, s);
                AddQuad(positions, uvs, stations, indices,
                    new Vector3(-1f - w, 1f - w, z), new Vector3(1f + w, 1f - w, z), new Vector3(1f + w, 1f + w, z), new Vector3(-1f - w, 1f + w, z), s, s);
                AddQuad(positions, uvs, stations, indices,
                    new Vector3(-1f - w, -1f - w, z), new Vector3(1f + w, -1f - w, z), new Vector3(1f + w, -1f + w, z), new Vector3(-1f - w, -1f + w, z), s, s);

                if (k == FrameStations - 1)
                {
                    continue;
                }

                float z1 = z + FrameSpacing;
                byte s1 = (byte)(k + 1);
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    for (int sy = -1; sy <= 1; sy += 2)
                    {
                        AddQuad(positions, uvs, stations, indices,
                            new Vector3(sx - r, sy, z), new Vector3(sx - r, sy, z1), new Vector3(sx + r, sy, z1), new Vector3(sx + r, sy, z), s, s1);
                        AddQuad(positions, uvs, stations, indices,
                            new Vector3(sx, sy - r, z), new Vector3(sx, sy - r, z1), new Vector3(sx, sy + r, z1), new Vector3(sx, sy + r, z), s, s1);
                    }
                }
            }

            _fenceColors = new Color32[positions.Count];
            for (int i = 0; i < _fenceColors.Length; i++)
            {
                _fenceColors[i] = new Color32(255, 255, 255, 255);
            }

            _fenceStation = stations.ToArray();
            _fenceMesh = new Mesh();
            _fenceMesh.name = "BeamRunner Edge Fence";
            _fenceMesh.MarkDynamic();
            _fenceMesh.SetVertices(positions);
            _fenceMesh.SetUVs(0, uvs);
            _fenceMesh.colors32 = _fenceColors;
            _fenceMesh.SetTriangles(indices, 0, false);
            _fenceMesh.bounds = new Bounds(
                new Vector3(0f, 0f, FrameSpacing * (FrameStations - 1) * 0.5f),
                new Vector3(2.4f, 2.4f, FrameSpacing * FrameStations + 4f));
            RenderCache.Track(_fenceMesh);

            _fenceRenderer = CreateRenderer("EdgeFence", transform, _fenceMesh, FieldMaterials.Beam());
            FieldMaterials.GuardAdditive(_fenceRenderer);
            _fence = _fenceRenderer.transform;
            _fenceBlock = new MaterialPropertyBlock();
            _fenceBlock.SetVector(ShaderIds.Palette, ShaderIds.PaletteCustom);
            _fenceBlock.SetColor(ShaderIds.TintColor, FenceColor);
            _fenceBlock.SetFloat(ShaderIds.Intensity, 0f);
            _fenceBlock.SetFloat(ShaderIds.FogAmount, 1f);
            _fenceRenderer.SetPropertyBlock(_fenceBlock);
            _fenceRenderer.enabled = false;
        }

        // p0 → p1 along the strip, p0 → p3 across it; stationA tags p0/p3, stationB tags p1/p2.
        private static void AddQuad(List<Vector3> positions, List<Vector2> uvs, List<byte> stations, List<int> indices,
            Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, byte stationA, byte stationB)
        {
            int i0 = positions.Count;
            positions.Add(p0);
            positions.Add(p1);
            positions.Add(p2);
            positions.Add(p3);
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(0f, 1f));
            stations.Add(stationA);
            stations.Add(stationB);
            stations.Add(stationB);
            stations.Add(stationA);
            indices.Add(i0);
            indices.Add(i0 + 1);
            indices.Add(i0 + 2);
            indices.Add(i0);
            indices.Add(i0 + 2);
            indices.Add(i0 + 3);
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

        private void OnDestroy()
        {
            if (_frameMesh != null)
            {
                RenderCache.Release(_frameMesh);
            }

            if (_fenceMesh != null)
            {
                RenderCache.Release(_fenceMesh);
            }
        }
    }
}
