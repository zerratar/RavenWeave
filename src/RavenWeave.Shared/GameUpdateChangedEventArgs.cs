using System;

namespace Ravenfall.Updater.Core
{
    public class GameUpdateChangedEventArgs : EventArgs
    {
        public string Message { get; }
        public string OldVersion { get; }
        public string NewVersion { get; }
        public float Progress { get; }

        public GameUpdateChangedEventArgs(string message, string oldVersion, string newVersion, float v)
        {
            this.Message = message;
            this.OldVersion = oldVersion;
            this.NewVersion = newVersion;
            this.Progress = v;
        }
    }
}