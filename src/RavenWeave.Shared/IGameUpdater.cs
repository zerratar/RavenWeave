using System;

namespace Ravenfall.Updater.Core
{
    public interface IGameUpdater
    {
        event EventHandler<GameUpdateChangedEventArgs> StatusChanged;
        event EventHandler<GameUpdateChangedEventArgs> UpdateCompleted;
        void Start();
        /// <summary>After a failure: tries the files that are left again.</summary>
        void Retry();
        /// <summary>Starts the game and closes the updater.</summary>
        void StartGameAndExit();
    }
}
