using System;

namespace Ravenfall.Updater.Core
{
    public interface IGameUpdater
    {
        event EventHandler<GameUpdateChangedEventArgs> StatusChanged;
        event EventHandler<GameUpdateChangedEventArgs> UpdateCompleted;
        void Start();
    }
}