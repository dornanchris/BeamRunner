using System;

namespace BeamRunner.Core.Meta
{
    /// <summary>Bookkeeping for the rating prompt (kept per device by the Unity layer).</summary>
    [Serializable]
    public sealed class ReviewPromptState
    {
        /// <summary>App launches (cold starts) counted on this device so far.</summary>
        public int Launches;

        /// <summary>The player chose "Don't ask again" or went to the store from the prompt: never ask again.</summary>
        public bool OptedOut;

        /// <summary>UTC ticks of the last prompt shown (0 = never).</summary>
        public long LastRequestUtcTicks;

        /// <summary>How many prompts were shown so far.</summary>
        public int Requests;
    }

    /// <summary>How the rating prompt is shown on a platform.</summary>
    public enum ReviewPromptStyle
    {
        /// <summary>
        /// Google Play (and the Editor): the game's own dialog — RATE NOW / NOT NOW / DON'T ASK AGAIN; RATE NOW opens the
        /// store page. (Not Google's In-App Review card, whose guidelines forbid asking anything before it.)
        /// </summary>
        GameDialog = 0,

        /// <summary>
        /// iOS: Apple's own review sheet only — App Store Review Guideline 5.6.1 disallows custom review prompts. The sheet
        /// carries its own "Not Now"; iOS shows it at most three times a year and players can turn it off in Settings.
        /// </summary>
        SystemSheet,
    }

    /// <summary>The player's answer to the game's rating dialog.</summary>
    public enum ReviewAnswer
    {
        /// <summary>RATE NOW: the store page opened. Never asked again.</summary>
        Rate = 0,

        /// <summary>NOT NOW (also back / tapping outside): asked again at the next due launch.</summary>
        Later,

        /// <summary>DON'T ASK AGAIN.</summary>
        Never,
    }

    /// <summary>
    /// When to ask for a rating: at every <see cref="LaunchInterval"/>-th launch of the app (the 3rd, 6th, 9th …), once
    /// the main menu is idle, until the player opts out — except in a session that follows an app crash (that launch is
    /// skipped). Shared by Android and iOS; <see cref="ReviewPromptStyle"/> decides what the prompt looks like.
    /// </summary>
    public static class ReviewPromptPolicy
    {
        /// <summary>The prompt is due at every this-many-th launch.</summary>
        public const int LaunchInterval = 3;

        /// <summary>Counts a launch. Returns true when the prompt is due at it (see <see cref="IsDue"/>).</summary>
        public static bool RecordLaunch(ReviewPromptState state, bool appCrashedLastSession)
        {
            if (state == null)
            {
                return false;
            }

            if (state.Launches < 0)
            {
                state.Launches = 0; // a corrupt value: start counting again
            }

            if (state.Launches < int.MaxValue)
            {
                state.Launches++;
            }

            return IsDue(state, appCrashedLastSession);
        }

        /// <summary>
        /// True when the launch just counted is a prompt launch: every <see cref="LaunchInterval"/>-th, not opted out,
        /// not after an app crash. Pure.
        /// </summary>
        public static bool IsDue(ReviewPromptState state, bool appCrashedLastSession)
        {
            return state != null && !state.OptedOut && !appCrashedLastSession && state.Launches > 0 &&
                   state.Launches % LaunchInterval == 0;
        }

        /// <summary>Records a prompt shown at <paramref name="utcNow"/>.</summary>
        public static void MarkRequested(ReviewPromptState state, DateTime utcNow)
        {
            if (state == null)
            {
                return;
            }

            state.LastRequestUtcTicks = utcNow.Ticks;
            state.Requests++;
        }

        /// <summary>
        /// Records the player's answer to the game's dialog: <see cref="ReviewAnswer.Rate"/> and
        /// <see cref="ReviewAnswer.Never"/> end the prompts for good, <see cref="ReviewAnswer.Later"/> waits for the next
        /// due launch.
        /// </summary>
        public static void Answer(ReviewPromptState state, ReviewAnswer answer)
        {
            if (state != null && answer != ReviewAnswer.Later)
            {
                state.OptedOut = true;
            }
        }
    }
}
