using System;

namespace Ravenfall.Updater.Core
{
    public enum UpdatePhase
    {
        Preparing,
        WaitingForGame,
        Unpacking,
        Installing,
        Finishing,
        Done,
        Failed,
    }

    public class GameUpdateChangedEventArgs : EventArgs
    {
        public string Message { get; }
        public string OldVersion { get; }
        public string NewVersion { get; }
        /// <summary>0 to 100.</summary>
        public float Progress { get; }
        public UpdatePhase Phase { get; }
        /// <summary>The file being worked on, or a second line about the failure.</summary>
        public string Detail { get; }
        public long BytesDone { get; }
        public long BytesTotal { get; }

        public GameUpdateChangedEventArgs(string message, string oldVersion, string newVersion, float progress,
            UpdatePhase phase = UpdatePhase.Preparing, string detail = null, long bytesDone = 0, long bytesTotal = 0)
        {
            Message = message;
            OldVersion = oldVersion;
            NewVersion = newVersion;
            Progress = progress;
            Phase = phase;
            Detail = detail;
            BytesDone = bytesDone;
            BytesTotal = bytesTotal;
        }
    }
}
