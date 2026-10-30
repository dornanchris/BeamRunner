using System.Collections.Generic;
using BeamRunner.Core;
using BeamRunner.Core.Field;
using UnityEngine;

namespace BeamRunner.Rendering
{
    /// <summary>
    /// Binds the simulation's live pickups to pooled views (the rails, obstacles and walls are drawn by
    /// <see cref="TrackRenderer"/>). Each frame <see cref="Sync"/> diffs the ids against the active views: a new id rents a
    /// view (or creates one) and binds it, every live view is stamped with the frame number, and views left unstamped
    /// return to their pool. Zero allocations in steady state.
    /// </summary>
    /// <remarks>
    /// Simulation events are dispatched <b>before</b> <see cref="Sync"/> each frame. <see cref="OnPickupCollected"/> is
    /// idempotent and order-independent: views also react to the <c>Collected</c> flag during <see cref="Sync"/> and to
    /// pickups the simulation removed in the same step, so the FX play exactly once either way.
    /// </remarks>
    public sealed class FieldRenderer : MonoBehaviour
    {
        private readonly Dictionary<int, PickupView> _pickups = new Dictionary<int, PickupView>(64);
        private readonly Stack<PickupView> _pickupPool = new Stack<PickupView>(64);
        private readonly List<int> _stale = new List<int>(64);

        private Transform _pickupRoot;
        private FxLibrary _fx;
        private ZoneTheme _theme;
        private int _frame;
        private Transform _camera;
        private double _clock;

        /// <summary>Camera z assumed when no camera is set: the chase camera's distance behind the craft.</summary>
        private const float DefaultCameraZ = -7.5f;

        public int ActivePickupCount
        {
            get { return _pickups.Count; }
        }

        /// <summary>Views created so far (live + pooled), for debugging the pools.</summary>
        public int CreatedViewCount { get; private set; }

        /// <summary>Current level theme (for anything coloured on the CPU).</summary>
        public ZoneTheme Theme
        {
            get { return _theme; }
        }

        /// <summary>Creates the renderer under <paramref name="parent"/> (keep the parent at the world origin).</summary>
        public static FieldRenderer Create(Transform parent, FxLibrary fx)
        {
            var go = new GameObject("Field");
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            var field = go.AddComponent<FieldRenderer>();
            field._fx = fx;
            field._pickupRoot = CreateChild(go.transform, "Pickups");
            field._theme = ZoneThemes.ForLevel(1);
            return field;
        }

        /// <summary>The camera whose position drives the fade-out of passed pickups (set once; null = a default chase pose).</summary>
        public void SetCamera(Camera camera)
        {
            _camera = camera != null ? camera.transform : null;
        }

        /// <summary>Synchronises the pickups with the simulation.</summary>
        public void Sync(IReadOnlyList<Pickup> pickups, double shipDistance)
        {
            _frame++;
            _clock += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            Vector3 camera = _camera != null ? _camera.position : new Vector3(0f, 1.5f, DefaultCameraZ);
            if (pickups != null)
            {
                for (int i = 0; i < pickups.Count; i++)
                {
                    Pickup p = pickups[i];
                    if (p == null)
                    {
                        continue;
                    }

                    PickupView view;
                    if (!_pickups.TryGetValue(p.Id, out view))
                    {
                        view = _pickupPool.Count > 0 ? _pickupPool.Pop() : NewPickupView();
                        view.Bind(p);
                        _pickups.Add(p.Id, view);
                    }

                    view.Stamp = _frame;
                    view.Tick(p, shipDistance, _clock, _fx, camera.z);
                }
            }

            _stale.Clear();
            foreach (KeyValuePair<int, PickupView> kv in _pickups)
            {
                if (kv.Value.Stamp != _frame)
                {
                    _stale.Add(kv.Key);
                }
            }

            for (int i = 0; i < _stale.Count; i++)
            {
                PickupView view = _pickups[_stale[i]];
                _pickups.Remove(_stale[i]);
                view.Release(_fx, shipDistance);
                _pickupPool.Push(view);
            }
        }

        /// <summary>
        /// Collection FX for a pickup, then hides it (call on <c>RunEventType.PickupCollected</c>). Idempotent and
        /// order-independent.
        /// </summary>
        public void OnPickupCollected(int pickupId)
        {
            PickupView view;
            if (_pickups.TryGetValue(pickupId, out view))
            {
                view.Collect(_fx);
            }
        }

        /// <summary>Returns every view to its pool (new run / back to menu). No FX.</summary>
        public void Clear()
        {
            foreach (KeyValuePair<int, PickupView> kv in _pickups)
            {
                kv.Value.Release(null, 0.0);
                _pickupPool.Push(kv.Value);
            }

            _pickups.Clear();
        }

        /// <summary>Sets the level theme used for CPU-side colours (FX).</summary>
        public void SetTheme(ZoneTheme theme)
        {
            if (theme != null)
            {
                _theme = theme;
            }
        }

        private PickupView NewPickupView()
        {
            CreatedViewCount++;
            return PickupView.Create(_pickupRoot);
        }

        private static Transform CreateChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }
    }
}
