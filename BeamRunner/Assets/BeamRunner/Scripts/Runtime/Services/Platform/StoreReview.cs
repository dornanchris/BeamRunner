using System;
using UnityEngine;

namespace BeamRunner.Services
{
    /// <summary>
    /// The platforms' own in-app review sheets — never a custom "rate us" question (Apple: App Review Guideline 5.6.1;
    /// Google Play: no questions before or while showing the review card). Whether the sheet actually appears is up to
    /// the OS (quotas), and the result never says whether the player rated.
    /// <list type="bullet">
    /// <item><b>Android</b>: Google Play In-App Review — <c>com.google.android.play:review</c> 2.0.2 (declared for the
    /// External Dependency Manager in <c>Scripts/Editor/Android/FieldPlayReviewDependencies.xml</c>), called through JNI:
    /// <c>ReviewManagerFactory.create(Context)</c> → <c>requestReviewFlow()</c> → <c>launchReviewFlow(Activity,
    /// ReviewInfo)</c>, completion through <c>com.google.android.gms.tasks.OnSuccessListener</c> /
    /// <c>OnFailureListener</c> proxies (called on a Java thread, marshalled to the main thread). Without the library
    /// (Android Resolver not run) or without the Play Store it quietly reports false.</item>
    /// <item><b>iOS</b>: <c>UnityEngine.iOS.Device.RequestStoreReview()</c> (StoreKit).</item>
    /// </list>
    /// The explicit, player-initiated "Rate" link is <see cref="PlatformUtil.OpenWriteReviewPage"/>.
    /// </summary>
    public static class StoreReview
    {
        private const float TimeoutSeconds = 60f;

        private static bool s_busy;

        /// <summary>True on platforms with an in-app review sheet (Android and iOS devices).</summary>
        public static bool IsSupported
        {
            get
            {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>True while a request is in flight.</summary>
        public static bool IsBusy => s_busy;

        /// <summary>
        /// Asks the platform to show its review sheet. <paramref name="onDone"/> runs once on the main thread (never
        /// synchronously) with true when the flow ran without error — not whether anything was shown or rated.
        /// <paramref name="stillAppropriate"/> (optional) is checked on the main thread right before the sheet is
        /// launched (Android fetches the review info first, which takes a moment): false cancels, e.g. when the player
        /// already started the next run.
        /// </summary>
        public static void Request(Action<bool> onDone, Func<bool> stillAppropriate = null)
        {
            if (!IsSupported || s_busy)
            {
                Post(onDone, false);
                return;
            }

            s_busy = true;
            var once = new Once(onDone);
            int timer = ServiceRunner.Delay(TimeoutSeconds, () =>
            {
                ServiceLog.Warn("StoreReview: no answer after " + TimeoutSeconds + " s.");
                once.Complete(false);
            }, true);
            once.OnComplete = () =>
            {
                ServiceRunner.Cancel(timer);
                s_busy = false;
            };

            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                RequestAndroid(once, stillAppropriate);
#elif UNITY_IOS && !UNITY_EDITOR
                bool ok = UnityEngine.iOS.Device.RequestStoreReview();
                ServiceLog.Info("StoreReview: RequestStoreReview -> " + ok + ".");
                once.CompleteLater(ok);
#else
                once.CompleteLater(false);
#endif
            }
            catch (Exception e)
            {
                ServiceLog.Exception("StoreReview: request failed", e);
                once.CompleteLater(false);
            }
        }

        private static void Post(Action<bool> onDone, bool value)
        {
            if (onDone != null)
            {
                ServiceRunner.Post(() => onDone(value));
            }
        }

        /// <summary>Delivers the result exactly once, on the main thread.</summary>
        private sealed class Once
        {
            private readonly Action<bool> _onDone;
            private bool _done;

            public Once(Action<bool> onDone)
            {
                _onDone = onDone;
            }

            public Action OnComplete;

            /// <summary>True once the result was delivered (e.g. by the time-out).</summary>
            public bool IsDone => _done;

            /// <summary>Main thread only.</summary>
            public void Complete(bool ok)
            {
                if (_done)
                {
                    return;
                }

                _done = true;
                try
                {
                    OnComplete?.Invoke();
                }
                catch (Exception e)
                {
                    ServiceLog.Exception("StoreReview: completion failed", e);
                }

                if (_onDone != null)
                {
                    try
                    {
                        _onDone(ok);
                    }
                    catch (Exception e)
                    {
                        ServiceLog.Exception("StoreReview callback failed", e);
                    }
                }
            }

            /// <summary>Any thread: completes on the main thread during a later frame.</summary>
            public void CompleteLater(bool ok)
            {
                ServiceRunner.Post(() => Complete(ok));
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private const string ReviewManagerFactoryClass = "com.google.android.play.core.review.ReviewManagerFactory";
        private const string OnSuccessListenerInterface = "com.google.android.gms.tasks.OnSuccessListener";
        private const string OnFailureListenerInterface = "com.google.android.gms.tasks.OnFailureListener";

        private static void RequestAndroid(Once once, Func<bool> stillAppropriate)
        {
            AndroidJavaObject activity;
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            }

            AndroidJavaObject manager;
            using (var factory = new AndroidJavaClass(ReviewManagerFactoryClass))
            {
                manager = factory.CallStatic<AndroidJavaObject>("create", activity);
            }

            using (AndroidJavaObject requestTask = manager.Call<AndroidJavaObject>("requestReviewFlow"))
            {
                // Both listeners are registered; exactly one of them fires.
                requestTask.Call<AndroidJavaObject>("addOnSuccessListener", new TaskListener(OnSuccessListenerInterface, reviewInfo =>
                {
                    // Java thread: launch from the main thread - unless the request already timed out (a late sheet
                    // could otherwise pop up in the middle of a run).
                    ServiceRunner.Post(() =>
                    {
                        bool appropriate = true;
                        try
                        {
                            appropriate = stillAppropriate == null || stillAppropriate();
                        }
                        catch (Exception e)
                        {
                            ServiceLog.Exception("StoreReview: stillAppropriate check failed", e);
                            appropriate = false;
                        }

                        if (once.IsDone || !appropriate)
                        {
                            DisposeAll(reviewInfo, manager, activity);
                            once.Complete(false);
                            return;
                        }

                        LaunchAndroid(once, manager, activity, reviewInfo);
                    });
                })).Dispose();
                requestTask.Call<AndroidJavaObject>("addOnFailureListener", new TaskListener(OnFailureListenerInterface, error =>
                {
                    ServiceRunner.Post(() =>
                    {
                        ServiceLog.Warn("StoreReview: requestReviewFlow failed (" + Describe(error) + ").");
                        DisposeAll(error, manager, activity);
                        once.Complete(false);
                    });
                })).Dispose();
            }
        }

        private static void LaunchAndroid(Once once, AndroidJavaObject manager, AndroidJavaObject activity,
            AndroidJavaObject reviewInfo)
        {
            try
            {
                using (AndroidJavaObject launchTask = manager.Call<AndroidJavaObject>("launchReviewFlow", activity, reviewInfo))
                {
                    launchTask.Call<AndroidJavaObject>("addOnSuccessListener", new TaskListener(OnSuccessListenerInterface, result =>
                    {
                        ServiceRunner.Post(() =>
                        {
                            ServiceLog.Info("StoreReview: review flow finished.");
                            DisposeAll(result, reviewInfo, manager, activity);
                            once.Complete(true);
                        });
                    })).Dispose();
                    launchTask.Call<AndroidJavaObject>("addOnFailureListener", new TaskListener(OnFailureListenerInterface, error =>
                    {
                        ServiceRunner.Post(() =>
                        {
                            ServiceLog.Warn("StoreReview: launchReviewFlow failed (" + Describe(error) + ").");
                            DisposeAll(error, reviewInfo, manager, activity);
                            once.Complete(false);
                        });
                    })).Dispose();
                }
            }
            catch (Exception e)
            {
                ServiceLog.Exception("StoreReview: launchReviewFlow threw", e);
                DisposeAll(reviewInfo, manager, activity);
                once.Complete(false);
            }
        }

        private static string Describe(AndroidJavaObject exception)
        {
            if (exception == null)
            {
                return "no details";
            }

            try
            {
                return exception.Call<string>("toString");
            }
            catch (Exception)
            {
                return "unknown error";
            }
        }

        private static void DisposeAll(params AndroidJavaObject[] objects)
        {
            for (int i = 0; i < objects.Length; i++)
            {
                try
                {
                    objects[i]?.Dispose();
                }
                catch (Exception)
                {
                    // best effort
                }
            }
        }

        /// <summary>
        /// Java <c>OnSuccessListener.onSuccess(Object)</c> / <c>OnFailureListener.onFailure(Exception)</c>, invoked by
        /// the Play library on a Java thread: handlers only post to the main thread (disposal and completion happen there).
        /// </summary>
        [UnityEngine.Scripting.Preserve]
        private sealed class TaskListener : AndroidJavaProxy
        {
            private readonly Action<AndroidJavaObject> _handler;

            public TaskListener(string javaInterface, Action<AndroidJavaObject> handler) : base(javaInterface)
            {
                _handler = handler;
            }

            // Method names must match the Java interfaces (AndroidJavaProxy dispatches by name, via reflection: keep them
            // from being stripped).
            [UnityEngine.Scripting.Preserve]
            public void onSuccess(AndroidJavaObject result)
            {
                _handler(result);
            }

            [UnityEngine.Scripting.Preserve]
            public void onFailure(AndroidJavaObject exception)
            {
                _handler(exception);
            }
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_busy = false;
        }
    }
}
