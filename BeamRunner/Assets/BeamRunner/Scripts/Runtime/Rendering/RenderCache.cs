using System.Collections.Generic;
using UnityEngine;

namespace BeamRunner.Rendering
{
    /// <summary>
    /// Ownership list for runtime-created, never-saved rendering assets (materials, meshes, textures).
    /// The factories cache these for the lifetime of the app; in the Editor they are destroyed when play
    /// mode ends so nothing leaks between sessions. Every factory re-creates an asset whose cached
    /// reference has become null, so this also works with domain reload disabled.
    /// </summary>
    internal static class RenderCache
    {
        private static readonly List<Object> Owned = new List<Object>(128);
        private static bool _hooked;

        /// <summary>Marks an object as runtime-only and registers it for editor cleanup.</summary>
        public static T Track<T>(T obj) where T : Object
        {
            if (obj == null)
            {
                return null;
            }

            obj.hideFlags = HideFlags.DontSave;
            if (!_hooked)
            {
                _hooked = true;
                Application.quitting += OnQuitting;
            }

            Owned.Add(obj);
            return obj;
        }

        /// <summary>
        /// Destroys a tracked object whose owner is going away (e.g. a ship's trail mesh on every hangar swap) and
        /// drops it from the list, so per-instance assets don't accumulate for the lifetime of the app.
        /// </summary>
        public static void Release(Object obj)
        {
            if (obj == null)
            {
                return;
            }

            Owned.Remove(obj);
            Object.Destroy(obj);
        }

        private static void OnQuitting()
        {
            Application.quitting -= OnQuitting;
            _hooked = false;
            if (Application.isEditor)
            {
                for (int i = 0; i < Owned.Count; i++)
                {
                    if (Owned[i] != null)
                    {
                        Object.DestroyImmediate(Owned[i]);
                    }
                }
            }

            Owned.Clear();
        }
    }
}
