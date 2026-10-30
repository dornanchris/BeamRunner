using BeamRunner.Services;
using BeamRunner.UI;

namespace BeamRunner.Game
{
    /// <summary>UI creation. The UI observes this controller through its events and read-only state.</summary>
    public sealed partial class GameController
    {
        partial void CreateUI()
        {
            // Strings first: the splash (built right away) and everything after it read the string tables.
            LocalizationService.Initialize();
            GameUI.Create(this);
        }
    }
}
