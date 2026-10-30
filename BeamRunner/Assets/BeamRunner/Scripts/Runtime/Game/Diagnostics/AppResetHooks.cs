#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Reflection;
using BeamRunner.Core.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BeamRunner.Game
{
    /// <summary>
    /// Restart emulation for the PlayMode smoke tests (Editor and development builds only): destroys the game's
    /// DontDestroyOnLoad helpers and re-runs every static reset of the game's assembly — the
    /// <c>[RuntimeInitializeOnLoadMethod]</c> methods Unity calls when Play Mode starts (SubsystemRegistration,
    /// AfterAssembliesLoaded, BeforeSplashScreen, BeforeSceneLoad; never AfterSceneLoad, whose bootstrap safety net
    /// would boot a second game). The game already supports "Enter Play Mode Options" without domain reload through
    /// these methods, so the next load of the main scene boots like a fresh launch.
    /// </summary>
    internal sealed class AppResetHooks : IAppResetHooks
    {
        public static readonly AppResetHooks Instance = new AppResetHooks();

        private static readonly RuntimeInitializeLoadType[] ResetOrder =
        {
            RuntimeInitializeLoadType.SubsystemRegistration,
            RuntimeInitializeLoadType.AfterAssembliesLoaded,
            RuntimeInitializeLoadType.BeforeSplashScreen,
            RuntimeInitializeLoadType.BeforeSceneLoad,
        };

        private AppResetHooks()
        {
        }

        public void DestroyPersistentObjects()
        {
            // The DontDestroyOnLoad scene can only be reached through an object in it.
            var marker = new GameObject("[BeamRunner.ProbeMarker]");
            Object.DontDestroyOnLoad(marker);
            Scene persistent = marker.scene;
            Assembly game = typeof(GameController).Assembly;
            GameObject[] roots = persistent.IsValid() && persistent.isLoaded ? persistent.GetRootGameObjects() : new GameObject[0];
            for (int i = 0; i < roots.Length; i++)
            {
                GameObject go = roots[i];
                if (go != null && go != marker && OwnedByGame(go, game))
                {
                    Object.Destroy(go);
                }
            }

            Object.Destroy(marker);
        }

        public void ResetStatics()
        {
            Assembly game = typeof(GameController).Assembly;
            Type[] types;
            try
            {
                types = game.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types;
            }

            var byStage = new List<MethodInfo>[ResetOrder.Length];
            for (int s = 0; s < byStage.Length; s++)
            {
                byStage[s] = new List<MethodInfo>();
            }

            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (int t = 0; t < types.Length; t++)
            {
                Type type = types[t];
                if (type == null || type.ContainsGenericParameters)
                {
                    continue;
                }

                MethodInfo[] methods = type.GetMethods(flags);
                for (int m = 0; m < methods.Length; m++)
                {
                    MethodInfo method = methods[m];
                    if (method.GetParameters().Length != 0)
                    {
                        continue;
                    }

                    var attribute = (RuntimeInitializeOnLoadMethodAttribute)Attribute.GetCustomAttribute(method,
                        typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attribute == null)
                    {
                        continue;
                    }

                    int stage = Array.IndexOf(ResetOrder, attribute.loadType);
                    if (stage >= 0)
                    {
                        byStage[stage].Add(method);
                    }
                }
            }

            for (int s = 0; s < byStage.Length; s++)
            {
                for (int i = 0; i < byStage[s].Count; i++)
                {
                    MethodInfo method = byStage[s][i];
                    try
                    {
                        method.Invoke(null, null);
                    }
                    catch (TargetInvocationException e)
                    {
                        FieldLog.Exception("Static reset " + method.DeclaringType + "." + method.Name, e.InnerException ?? e);
                    }
                }
            }
        }

        private static bool OwnedByGame(GameObject go, Assembly game)
        {
            string name = go.name;
            if (name.StartsWith("[BeamRunner", StringComparison.Ordinal) || name.StartsWith("[FieldUI", StringComparison.Ordinal))
            {
                return true;
            }

            MonoBehaviour[] behaviours = go.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] != null && behaviours[i].GetType().Assembly == game)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
#endif
