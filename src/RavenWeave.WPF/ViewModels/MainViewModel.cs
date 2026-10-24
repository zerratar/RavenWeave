using Ravenfall.Updater.Core;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Input;

namespace Ravenfall.Updater.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IGameUpdater updater;

        private string message = "Getting ready...";
        private string detail;
        private float updateProgress;
        private string progressText;
        private string versionText;
        private bool isFailed;
        private bool isDone;

        public MainViewModel(IGameUpdater updater, IKernel kernel)
        {
            this.updater = updater;
            RetryCommand = new RelayCommand(() =>
            {
                IsFailed = false;
                Message = "Trying again...";
                updater.Retry();
            });
            StartGameCommand = new RelayCommand(() => updater.StartGameAndExit());
            OpenLogCommand = new RelayCommand(OpenLog);
            CloseCommand = new RelayCommand(() => System.Windows.Application.Current.Shutdown());

            if (updater == null) return; // the designer
            updater.StatusChanged += (s, e) => UpdateView(e);
            updater.UpdateCompleted += (s, e) => UpdateView(e);
            updater.Start();
        }

        public ICommand RetryCommand { get; }
        public ICommand StartGameCommand { get; }
        public ICommand OpenLogCommand { get; }
        public ICommand CloseCommand { get; }

        private void UpdateView(GameUpdateChangedEventArgs e)
        {
            Message = e.Message;
            Detail = e.Detail;
            UpdateProgress = e.Progress;
            IsFailed = e.Phase == UpdatePhase.Failed;
            IsDone = e.Phase == UpdatePhase.Done;
            VersionText = Versions(e.OldVersion, e.NewVersion);

            if (IsFailed)
            {
                ProgressText = null;
            }
            else if (e.BytesTotal > 0)
            {
                ProgressText = string.Format(CultureInfo.InvariantCulture, "{0:0}%   {1} of {2}", e.Progress, Size(e.BytesDone), Size(e.BytesTotal));
            }
            else
            {
                ProgressText = string.Format(CultureInfo.InvariantCulture, "{0:0}%", e.Progress);
            }
        }

        private static string Versions(string oldVersion, string newVersion)
        {
            var hasOld = !string.IsNullOrEmpty(oldVersion) && oldVersion != "-";
            var hasNew = !string.IsNullOrEmpty(newVersion) && newVersion != "-";
            if (hasOld && hasNew) return "v" + oldVersion + "  →  v" + newVersion;
            if (hasNew) return "Updating to v" + newVersion;
            return null;
        }

        private static string Size(long bytes)
        {
            if (bytes >= 1L << 30) return (bytes / (double)(1L << 30)).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
            return (bytes / (double)(1L << 20)).ToString("0", CultureInfo.InvariantCulture) + " MB";
        }

        private static void OpenLog()
        {
            try
            {
                var path = UpdateLog.Path;
                if (path != null && File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
                }
            }
            catch
            {
            }
        }

        #region Properties

        public string Message
        {
            get => message;
            set => Set(ref message, value);
        }

        public string Detail
        {
            get => detail;
            set => Set(ref detail, value);
        }

        public float UpdateProgress
        {
            get => updateProgress;
            set => Set(ref updateProgress, value);
        }

        public string ProgressText
        {
            get => progressText;
            set => Set(ref progressText, value);
        }

        public string VersionText
        {
            get => versionText;
            set => Set(ref versionText, value);
        }

        public bool IsFailed
        {
            get => isFailed;
            set => Set(ref isFailed, value);
        }

        public bool IsDone
        {
            get => isDone;
            set => Set(ref isDone, value);
        }

        #endregion

        public static MainViewModel DesignInstance => new MainViewModel(null, null)
        {
            Message = "Installing...",
            Detail = @"Ravenfall_Data\sharedassets1.assets.resS",
            UpdateProgress = 62f,
            ProgressText = "62%   1.36 GB of 2.20 GB",
            VersionText = "v0.9.5.0a  →  v0.9.5.1a"
        };
    }

    /// <summary>A command that runs an action.</summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action action;

        public RelayCommand(Action action)
        {
            this.action = action;
        }

        public event EventHandler CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter) => action?.Invoke();
    }
}
