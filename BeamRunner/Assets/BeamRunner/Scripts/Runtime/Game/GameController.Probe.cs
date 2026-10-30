#if UNITY_EDITOR || DEVELOPMENT_BUILD
using BeamRunner.Core.Sim;

namespace BeamRunner.Game
{
    /// <summary>
    /// Test probe wiring (Editor and development builds only; in release builds the partial hooks in
    /// <c>GameController.cs</c> have no implementation and compile to nothing). Publishes a <see cref="GameProbe"/>
    /// through <c>BeamRunner.Core.Diagnostics.GameProbeRegistry</c> for the PlayMode smoke tests and lets its
    /// autopilot replace the player's input during runs.
    /// </summary>
    public sealed partial class GameController
    {
        private GameProbe _probe;

        partial void ProbeAttach()
        {
            _probe = GameProbe.Attach(this);
        }

        partial void ProbeDetach()
        {
            if (_probe != null)
            {
                _probe.Detach();
                _probe = null;
            }
        }

        partial void ProbeOverrideInput(ref RunInput input)
        {
            if (_probe != null)
            {
                _probe.OverrideInput(Run, ref input);
            }
        }
    }
}
#endif
