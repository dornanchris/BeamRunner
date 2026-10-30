using BeamRunner.Core.Field;
using UnityEngine;

namespace BeamRunner.Rendering
{
    /// <summary>Camera behaviours.</summary>
    public enum CameraMode
    {
        /// <summary>Chase camera behind and a little above the craft, following its X/Y with a slight lag.</summary>
        Run,

        /// <summary>Slow attract-mode orbit drifting around the craft flying the menu's autopilot run.</summary>
        Menu,

        /// <summary>Close three-quarter showcase of the craft on its turntable.</summary>
        Hangar,

        /// <summary>Freezes at the crash and slowly pulls back and up.</summary>
        Crash,
    }

    /// <summary>
    /// Owns the main camera and frames the craft for each <see cref="CameraMode"/>, blending smoothly between modes.
    /// Adapts to the aspect ratio: in portrait the vertical FOV opens up so the field keeps enough horizontal coverage
    /// (about 45–52°); in landscape the camera pulls back a little.
    /// </summary>
    /// <remarks>
    /// <para><b>Run framing.</b> The focus follows <see cref="RunFollow"/> of the craft's offset from the field's axis
    /// (with lag), so the craft visibly moves on screen and stays in view at the field's edges, while the camera looks a
    /// little back toward the field's centre <see cref="RunLookAhead"/> m ahead, so the field ahead stays in view too.
    /// Roll follows sideways velocity, a small pitch follows vertical velocity; FOV kicks with speed and Overdrive.</para>
    /// <para>Up is always +Y (gravity-free, but the floor is down).</para>
    /// </remarks>
    public sealed class CameraRig : MonoBehaviour
    {
        /// <summary>Fraction of the craft's offset from the field's axis that the chase camera follows.</summary>
        public const float RunFollow = 0.5f;

        /// <summary>Fraction of the focus offset the look-at point keeps (below 1: the camera looks toward the centre).</summary>
        public const float RunLookFollow = 0.35f;

        /// <summary>Distance ahead of the craft the chase camera looks at (m).</summary>
        public const float RunLookAhead = 26f;

        private const float BlendTime = 0.9f;

        /// <summary>Cap on the accumulated FOV punch (degrees): bursts of kicks (a revive smashing a dozen cubes) stay sane.</summary>
        public const float MaxKick = 12f;

        /// <summary>
        /// Near clip plane (m). Nothing is drawn closer than about 1 m in any mode (passed obstacles dissolve first), and
        /// 0.3 instead of 0.1 triples the far depth precision of the 24-bit, non-reversed depth buffer on GLES3.
        /// </summary>
        public const float NearClip = 0.3f;
        private const float MaxRoll = 6f;
        private const float MaxPitch = 2.5f;

        private Camera _camera;
        private CameraMode _mode = CameraMode.Run;

        private Vector2 _focus;
        private bool _hasFocus;
        private float _roll;
        private float _pitch;
        private float _time;
        private float _modeTime;

        private float _trauma;
        private float _kick;
        private float _speedFov;

        // Blend from the pose captured when the mode changed.
        private bool _blending;
        private float _blendT;
        private Vector3 _fromPos;
        private Quaternion _fromRot;
        private float _fromFov;

        // Crash anchor.
        private Vector3 _crashPos;
        private Quaternion _crashRot;
        private Vector3 _crashTarget;
        private float _crashFov = 70f;

        /// <summary>The main camera (tagged MainCamera, with the AudioListener).</summary>
        public Camera Camera
        {
            get { return _camera; }
        }

        public CameraMode Mode
        {
            get { return _mode; }
        }

        /// <summary>Creates the rig and its camera.</summary>
        public static CameraRig Create(Transform parent)
        {
            var go = new GameObject("CameraRig");
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            var rig = go.AddComponent<CameraRig>();
            rig.Build();
            return rig;
        }

        private void Build()
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(transform, false);
            _camera = camGo.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = FieldGlobals.FogColor;
            _camera.nearClipPlane = NearClip;
            _camera.farClipPlane = 300f; // beyond SpaceView.SkyRadius plus the camera's offset from the origin
            _camera.allowHDR = true;
            _camera.allowMSAA = false;
            _camera.fieldOfView = 70f;
            camGo.AddComponent<AudioListener>();

            camGo.transform.SetPositionAndRotation(new Vector3(0f, 1.5f, -7.5f), Quaternion.identity);
        }

        /// <summary>Switches mode; blends over ~0.9 s unless <paramref name="instant"/>.</summary>
        public void SetMode(CameraMode mode, bool instant)
        {
            if (mode == _mode && !instant)
            {
                return;
            }

            Transform t = _camera.transform;
            if (mode == CameraMode.Crash)
            {
                _crashPos = t.position;
                _crashRot = t.rotation;
                _crashTarget = t.position + t.forward * 7f;
                _crashFov = _camera.fieldOfView;
            }

            _mode = mode;
            _modeTime = 0f;
            if (instant)
            {
                _blending = false;
                _hasFocus = false;
            }
            else
            {
                _blending = true;
                _blendT = 0f;
                _fromPos = t.position;
                _fromRot = t.rotation;
                _fromFov = _camera.fieldOfView;
            }
        }

        /// <summary>Adds camera shake (0..1, accumulates, decays).</summary>
        public void Shake(float amount)
        {
            _trauma = Mathf.Clamp01(_trauma + Mathf.Max(0f, amount));
        }

        /// <summary>Momentary FOV punch in degrees (decays; the total is capped at <see cref="MaxKick"/>).</summary>
        public void Kick(float fovDelta)
        {
            _kick = Mathf.Min(_kick + fovDelta, MaxKick);
        }

        /// <summary>
        /// Per-frame update. (shipX, shipY) is the craft's position, (velX, velY) its velocity in m/s; speed01 may
        /// exceed 1 in Overdrive.
        /// </summary>
        public void Tick(float dt, float shipX, float shipY, float velX, float velY, float speed01, bool overdrive)
        {
            if (_camera == null)
            {
                return;
            }

            dt = Mathf.Clamp(dt, 0f, 0.1f);
            _time += dt;
            _modeTime += dt;

            float aspect = Mathf.Max(0.2f, _camera.aspect);
            float landscape = Mathf.InverseLerp(0.6f, 1.7f, aspect);
            var ship = new Vector2(shipX, shipY);

            Vector3 pos;
            Quaternion rot;
            float fov;
            switch (_mode)
            {
                case CameraMode.Menu:
                    MenuPose(dt, ship, landscape, out pos, out rot, out fov);
                    break;
                case CameraMode.Hangar:
                    HangarPose(ship, landscape, out pos, out rot, out fov);
                    break;
                case CameraMode.Crash:
                    CrashPose(out pos, out rot, out fov);
                    break;
                default:
                    RunPose(dt, ship, velX, velY, speed01, overdrive, aspect, landscape, out pos, out rot, out fov);
                    break;
            }

            if (_blending)
            {
                _blendT += dt / BlendTime;
                float k = _blendT >= 1f ? 1f : _blendT * _blendT * (3f - 2f * _blendT);
                pos = Vector3.Lerp(_fromPos, pos, k);
                rot = Quaternion.Slerp(_fromRot, rot, k);
                fov = Mathf.Lerp(_fromFov, fov, k);
                if (_blendT >= 1f)
                {
                    _blending = false;
                }
            }

            // Shake: trauma² scaled Perlin jitter in position and roll.
            _trauma = Mathf.Max(0f, _trauma - dt * 1.6f);
            float shake = _trauma * _trauma;
            if (shake > 0f)
            {
                float t = _time * 23f;
                Vector3 jitter = new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, 0f) * (0.7f * shake);
                pos += rot * jitter;
                rot *= Quaternion.Euler(0f, 0f, (Mathf.PerlinNoise(t, 5.1f) - 0.5f) * 9f * shake);
            }

            _kick *= Mathf.Exp(-5f * dt);
            fov = Mathf.Clamp(fov + _kick, 30f, 120f);

            _camera.transform.SetPositionAndRotation(pos, rot);
            _camera.fieldOfView = fov;
            _camera.backgroundColor = FieldGlobals.FogColor;
        }

        // ------------------------------------------------------------------ poses

        /// <summary>Base vertical FOV for the aspect: wide in portrait (keeps ~45–52° horizontally), 58° in landscape.</summary>
        private static float BaseFov(float aspect)
        {
            // 42° (CubeField used 52° for its 18 m field): the 9 m lane field fills the screen and blocks read early.
            const float targetHorizontal = 42f;
            float halfH = targetHorizontal * 0.5f * Mathf.Deg2Rad;
            float vertical = 2f * Mathf.Atan(Mathf.Tan(halfH) / aspect) * Mathf.Rad2Deg;
            return Mathf.Clamp(vertical, 50f, 76f);
        }

        private void Follow(Vector2 target, float rate, float dt)
        {
            if (!_hasFocus)
            {
                _focus = target;
                _hasFocus = true;
                return;
            }

            _focus += (target - _focus) * (1f - Mathf.Exp(-rate * dt));
        }

        private void RunPose(float dt, Vector2 ship, float velX, float velY, float speed01, bool overdrive, float aspect,
            float landscape, out Vector3 pos, out Quaternion rot, out float fov)
        {
            // Lagged follow of most of the craft's offset: moving visibly shifts the craft on screen, and at the field's
            // edge it stays in view while the rest of the field ahead does too.
            // (Lanes: half the lane offset, so all three lanes ahead stay in frame. Layers: the craft's height with more
            // lag, so a drop to the layer below reads as a fall before the camera catches up.)
            if (!_hasFocus)
            {
                Follow(new Vector2(ship.x * RunFollow, ship.y), 9f, dt);
            }
            else
            {
                float kx = 1f - Mathf.Exp(-9f * dt);
                float ky = 1f - Mathf.Exp(-4.5f * dt);
                _focus = new Vector2(_focus.x + (ship.x * RunFollow - _focus.x) * kx, _focus.y + (ship.y - _focus.y) * ky);
            }

            float back = Mathf.Lerp(6.6f, 7.2f, landscape);
            float height = Mathf.Lerp(2.6f, 2.2f, landscape);

            // Never above the ceiling: from the top layer the hazard line stays overhead.
            float camY = Mathf.Min(_focus.y + height, FieldSpec.CeilingY - 0.35f);
            pos = new Vector3(_focus.x, camY, -back);

            Vector3 target = new Vector3(_focus.x * RunLookFollow, _focus.y - 0.2f, RunLookAhead);

            float targetRoll = Mathf.Clamp(-velX / FieldSpec.MaxLateralSpeed, -1f, 1f) * MaxRoll;
            float targetPitch = Mathf.Clamp(-velY / FieldSpec.MaxLateralSpeed, -1f, 1f) * MaxPitch;
            float k = 1f - Mathf.Exp(-5f * dt);
            _roll = Mathf.Lerp(_roll, targetRoll, k);
            _pitch = Mathf.Lerp(_pitch, targetPitch, k);
            rot = Quaternion.LookRotation(target - pos, Vector3.up) * Quaternion.Euler(_pitch, 0f, _roll);

            float s01 = Mathf.Clamp01(speed01);
            _speedFov = Mathf.Lerp(_speedFov, s01 * 9f + (overdrive ? 8f : 0f), 1f - Mathf.Exp(-3f * dt));
            fov = BaseFov(aspect) + _speedFov;
        }

        private void MenuPose(float dt, Vector2 ship, float landscape, out Vector3 pos, out Quaternion rot, out float fov)
        {
            // The attract run steers around the field; drift after it gently rather than snapping.
            Follow(ship, 3f, dt);
            var craft = new Vector3(_focus.x, _focus.y, 0f);

            // Slow orbit behind the craft, swinging from side to side and bobbing up and down.
            float azimuth = Mathf.Sin(_time * 0.13f) * 0.85f;
            float elevation = 0.3f + 0.1f * Mathf.Sin(_time * 0.21f);
            float distance = Mathf.Lerp(4.6f, 5.6f, landscape);
            Vector3 local = new Vector3(
                Mathf.Sin(azimuth) * Mathf.Cos(elevation),
                Mathf.Sin(elevation),
                -Mathf.Cos(azimuth) * Mathf.Cos(elevation)) * distance;

            pos = craft + local;
            Vector3 lookAt = craft + new Vector3(0f, 0.55f, 2.2f);
            rot = Quaternion.LookRotation(lookAt - pos, Vector3.up);
            fov = Mathf.Lerp(62f, 50f, landscape);
        }

        private void HangarPose(Vector2 ship, float landscape, out Vector3 pos, out Quaternion rot, out float fov)
        {
            _focus = ship;
            _hasFocus = true;
            var craft = new Vector3(ship.x, ship.y, 0f);

            // Three-quarter view from behind-right and above, looking into the glowing depth of the field.
            float bob = Mathf.Sin(_time * 0.4f) * 0.08f;
            Vector3 local = new Vector3(1.55f, 0.95f + bob, -2.35f) * Mathf.Lerp(1f, 1.15f, landscape);
            pos = craft + local;
            Vector3 lookAt = craft + new Vector3(-0.15f, 0.12f, 0.35f);
            rot = Quaternion.LookRotation(lookAt - pos, Vector3.up);
            fov = Mathf.Lerp(58f, 44f, landscape);
        }

        private void CrashPose(out Vector3 pos, out Quaternion rot, out float fov)
        {
            // Hold, then ease back and up while keeping the wreck in frame.
            float t = Mathf.Clamp01((_modeTime - 0.15f) / 3.5f);
            float k = t * t * (3f - 2f * t);
            pos = _crashPos + (Vector3.back * 4.5f + Vector3.up * 1.8f) * k;
            Vector3 dir = _crashTarget - pos;
            rot = dir.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(dir, Vector3.up) : _crashRot;
            fov = _crashFov - 4f * k;
        }
    }
}
