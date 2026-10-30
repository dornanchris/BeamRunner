using BeamRunner.Core.Field;
using UnityEngine;
using UnityEngine.Rendering;

namespace BeamRunner.Rendering
{
    /// <summary>
    /// Light streaks rushing past just outside the play field: to its left and right and above it (never through the
    /// field itself, where they would be mistaken for obstacles, and never below the floor). One dynamic mesh (a quad
    /// per streak, rebuilt on the CPU each frame, no allocations); streaks move faster than the world, grow longer and
    /// denser with speed, and flood in during Overdrive. Each quad faces the field's axis, so it reads from the chase
    /// camera. Coloured from the level palette via the Additive shader.
    /// </summary>
    public sealed class SpeedLines : MonoBehaviour
    {
        private const int MaxLines = 72;
        private const float FarZ = 95f;
        private const float NearZ = -9f;

        /// <summary>Inner edge of the streak volume: a little outside the field.</summary>
        private const float Inner = FieldSpec.HalfExtent + 1f;

        /// <summary>Outer edge of the streak volume at the sides.</summary>
        private const float Outer = FieldSpec.HalfExtent + 8f;

        private struct Line
        {
            public float X;
            public float Y;
            public float SideX;
            public float SideY;
            public float Z;
            public float LengthScale;
            public float SpeedScale;
            public float Width;
            public float Alpha;
        }

        private readonly Line[] _lines = new Line[MaxLines];
        private Vector3[] _positions;
        private Color32[] _colors;
        private Mesh _mesh;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _block;
        private uint _seed = 0x6C8E9CF5u;
        private float _density = 1f;

        /// <summary>Creates the speed-line mesh under <paramref name="parent"/>.</summary>
        public static SpeedLines Create(Transform parent)
        {
            var go = new GameObject("SpeedLines");
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            var lines = go.AddComponent<SpeedLines>();
            lines.Build();
            return lines;
        }

        /// <summary>Quality multiplier on the streak count (0..1).</summary>
        public void SetDensity(float density)
        {
            _density = Mathf.Clamp01(density);
        }

        public void SetVisible(bool visible)
        {
            if (_renderer != null)
            {
                _renderer.enabled = visible;
            }
        }

        /// <summary>Advances the streaks. speed in m/s; speed01 normalised (may exceed 1 in Overdrive).</summary>
        public void Tick(float dt, float speed, float speed01, bool overdrive)
        {
            if (_mesh == null)
            {
                return;
            }

            float s01 = Mathf.Clamp01(speed01);
            float coverage = Mathf.Clamp01((s01 - 0.12f) / 0.88f) * 0.75f + (overdrive ? 0.25f : 0f);
            int wanted = Mathf.RoundToInt(MaxLines * coverage * _density);
            float baseLength = Mathf.Lerp(1.5f, 9f, s01) * (overdrive ? 1.6f : 1f);
            float fade = 1f - Mathf.Exp(-6f * dt);

            for (int i = 0; i < MaxLines; i++)
            {
                Line l = _lines[i];
                l.Z -= Mathf.Max(speed, 0f) * l.SpeedScale * dt;
                float length = baseLength * l.LengthScale;

                if (l.Z + length < NearZ)
                {
                    // Recycle behind the camera: respawn far ahead (hidden in the fog) and fade in.
                    Respawn(ref l, FarZ + Next01() * 25f);
                    l.Alpha = 0f;
                }

                // Slots beyond the wanted count fade out; the count follows speed smoothly.
                float target = i < wanted ? 1f : 0f;
                l.Alpha += (target - l.Alpha) * fade;
                _lines[i] = l;
                WriteQuad(i, ref l, length);
            }

            _mesh.SetVertices(_positions, 0, _positions.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            _mesh.SetColors(_colors, 0, _colors.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

            _block.SetFloat(ShaderIds.Intensity, 1.2f + 1.3f * s01 + (overdrive ? 1.2f : 0f));
            _renderer.SetPropertyBlock(_block);
        }

        private void WriteQuad(int i, ref Line l, float length)
        {
            int v = i * 4;
            var head = new Vector3(l.X, l.Y, l.Z);
            var tail = new Vector3(l.X, l.Y, l.Z + length);
            var side = new Vector3(l.SideX, l.SideY, 0f) * (l.Width * 0.5f);
            _positions[v] = tail - side;
            _positions[v + 1] = tail + side;
            _positions[v + 2] = head - side;
            _positions[v + 3] = head + side;
            byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(l.Alpha) * 255f);
            var c = new Color32(255, 255, 255, a);
            _colors[v] = c;
            _colors[v + 1] = c;
            _colors[v + 2] = c;
            _colors[v + 3] = c;
        }

        private void Respawn(ref Line l, float z)
        {
            // Two thirds at the sides (from just above the floor to just under the ceiling), one third above the field.
            float pick = Next01();
            if (pick < 0.66f)
            {
                float sign = pick < 0.33f ? -1f : 1f;
                l.X = sign * Mathf.Lerp(Inner, Outer, Next01());
                l.Y = Mathf.Lerp(FieldSpec.RailY(0) - 3f, FieldMath.CeilingY - 0.5f, Next01());
            }
            else
            {
                l.X = Mathf.Lerp(-Outer, Outer, Next01());
                l.Y = Mathf.Lerp(Inner, FieldMath.CeilingY - 0.4f, Next01());
            }

            // The quad's width runs around the field's axis, so its face points at the axis (and at the chase camera).
            float len = Mathf.Sqrt(l.X * l.X + l.Y * l.Y);
            l.SideX = len > 1e-3f ? -l.Y / len : 1f;
            l.SideY = len > 1e-3f ? l.X / len : 0f;
            l.Z = z;
            l.LengthScale = Mathf.Lerp(0.55f, 1.25f, Next01());
            l.SpeedScale = Mathf.Lerp(1.2f, 1.9f, Next01());
            l.Width = Mathf.Lerp(0.03f, 0.08f, Next01());
        }

        private float Next01()
        {
            _seed ^= _seed << 13;
            _seed ^= _seed >> 17;
            _seed ^= _seed << 5;
            return (_seed & 0xFFFFFF) / 16777216f;
        }

        private void Build()
        {
            int vertexCount = MaxLines * 4;
            _positions = new Vector3[vertexCount];
            _colors = new Color32[vertexCount];
            var uvs = new Vector2[vertexCount];
            var indices = new int[MaxLines * 6];
            for (int i = 0; i < MaxLines; i++)
            {
                int v = i * 4;
                // u along the streak (0 tail → 1 head, matching the Streak texture), v across.
                uvs[v] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(0f, 1f);
                uvs[v + 2] = new Vector2(1f, 0f);
                uvs[v + 3] = new Vector2(1f, 1f);
                int t = i * 6;
                indices[t] = v;
                indices[t + 1] = v + 2;
                indices[t + 2] = v + 1;
                indices[t + 3] = v + 1;
                indices[t + 4] = v + 2;
                indices[t + 5] = v + 3;

                Line l = default(Line);
                Respawn(ref l, Mathf.Lerp(NearZ, FarZ, Next01()));
                _lines[i] = l;
            }

            _mesh = new Mesh();
            _mesh.name = "BeamRunner SpeedLines";
            _mesh.MarkDynamic();
            _mesh.vertices = _positions;
            _mesh.colors32 = _colors;
            _mesh.uv = uvs;
            _mesh.triangles = indices;
            float extent = Outer + 2f;
            _mesh.bounds = new Bounds(new Vector3(0f, 0f, (FarZ + NearZ) * 0.5f + 12f), new Vector3(extent * 2f, extent * 2f, FarZ - NearZ + 60f));
            RenderCache.Track(_mesh);

            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = FieldMaterials.Additive(ProceduralTextures.Streak());
            FieldMaterials.GuardAdditive(_renderer);
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _block = new MaterialPropertyBlock();
            _block.SetVector(ShaderIds.Palette, new Vector4(0.45f, 0.35f, 0f, 0.4f));
            _block.SetColor(ShaderIds.TintColor, Color.white);
            _renderer.SetPropertyBlock(_block);
        }
    }
}
