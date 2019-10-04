using Newtonsoft.Json;
using RavenWeave.Core;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Ravenfall.Updater.Core
{
    public class GameUpdater : IGameUpdater
    {
        private const string MSG_PREP_FINDAPPFOLDER = "Looking for app folder...";
        private const string MSG_PREP_FINDUPDATEFOLDER = "Looking for update folder...";
        private const string MSG_UNPACKING_FILE = "Unpacking ";
        private const string MSG_UPDATE = "Updating... Please wait";
        private const string MSG_UPDATE_COMPLETED = "Update complete! Starting Ravenfall";
        private const string MSG_UPDATE_SAMEVERSION = "Ravenfall is already up to date";
        private const string MSG_UPDATE_FAILED = "Update failed. Restart Ravenfall and try again.";
        private const string MSG_UPDATE_FAILED_NOUPDATE = "Update failed. No update has been downloaded. Restart Ravenfall and try again.";
        private const string MSG_WAITING_FOR_RAVENFALL = "Waiting for Ravenfall to exit...";
        private readonly GameUpdateUnpacker unpacker;
        private readonly Dispatcher dispatcher;

        private string metaFile;
        private string updateFile;
        private MetaData metadata;
        private UpdateData update;

        public event EventHandler<GameUpdateChangedEventArgs> StatusChanged;
        public event EventHandler<GameUpdateChangedEventArgs> UpdateCompleted;

        public GameUpdater(GameUpdateUnpacker unpacker, Dispatcher dispatcher)
        {
            this.unpacker = unpacker;
            this.dispatcher = dispatcher;
        }

        public void Start()
        {
            Task.Run(() => StartUpdate());
        }

        private async void StartUpdate()
        {
            var appFolder = await FindAppFolderAsync();

            if (string.IsNullOrEmpty(appFolder))
            {
                await NotifyUpdateProgress("-", "-", MSG_UPDATE_FAILED, 1f);
                return;
            }

            var updateFolder = await FindUpdateFolderAsync();
            if (string.IsNullOrEmpty(updateFolder))
            {
                await NotifyUpdateProgress("-", "-", MSG_UPDATE_FAILED_NOUPDATE, 1f);
                return;
            }

            var currentVersion = GetCurrentVersion();
            var newVersion = GetNewVersion();

            if (newVersion == null || newVersion == currentVersion)
            {
                await NotifyUpdateProgress(newVersion, currentVersion, MSG_UPDATE_SAMEVERSION, 1f);
                return;
            }

            await UnpackUpdateIfNecessaryAsync(appFolder, newVersion);

            if (await ReplaceFilesAsync(newVersion, currentVersion, updateFolder, appFolder))
            {
                await NotifyUpdateProgress(newVersion, currentVersion, MSG_UPDATE_COMPLETED, 1f);
                System.Diagnostics.Process.Start("ravenfall.exe");
                this.dispatcher.BeginInvoke((Action)(() => { try { Application.Current?.Shutdown(); } catch { try { System.Diagnostics.Process.GetCurrentProcess().Kill(); } catch { } } }));
                return;
            }

            await NotifyUpdateProgress(newVersion, currentVersion, MSG_UPDATE_FAILED, 0);
        }

        private async Task UnpackUpdateIfNecessaryAsync(string appFolder, string newVersion)
        {
            var updatePackage = Directory.GetFiles(appFolder, "update.*", SearchOption.AllDirectories)
                .FirstOrDefault(x =>
                    x.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    x.EndsWith(".7z", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(updatePackage))
            {
                return;
            }

            var currentVersion = GetCurrentVersion();
            await UnZipAsync(updatePackage, Path.Combine(appFolder, "update", "unpacked"), async file =>
            {
                await NotifyUpdateProgress(newVersion, currentVersion, MSG_UNPACKING_FILE + file, 1f);
            });
        }

        private Task UnZipAsync(string srcDirPath, string destDirPath, Action<string> onUnzipped)
        {
            return this.unpacker.DecompressArchive(srcDirPath, destDirPath, onUnzipped);
        }

        private async Task<string> FindAppFolderAsync()
        {
            await NotifyUpdateProgress("-", "-", MSG_PREP_FINDAPPFOLDER, 1f);
            var metaFile = GetMetaFile();
            if (!string.IsNullOrEmpty(metaFile))
            {
                return Path.GetDirectoryName(metaFile);
            }
            return metaFile;
        }

        private async Task<string> FindUpdateFolderAsync()
        {
            await NotifyUpdateProgress(GetCurrentVersion(), "-", MSG_PREP_FINDUPDATEFOLDER, 1f);
            var updateFile = GetUpdateFile();
            if (!string.IsNullOrEmpty(updateFile))
            {
                return System.IO.Path.GetDirectoryName(updateFile);
            }
            return updateFile;
        }

        private async Task<bool> ReplaceFilesAsync(string newVersion, string oldVersion, string sourcePath, string destinationPath)
        {
            await CloseRavenfallAsync(newVersion, oldVersion);

            var files = Directory.GetFiles(sourcePath, "*.*", System.IO.SearchOption.AllDirectories);

            for (var i = 0; i < files.Length; ++i)
            {
                try
                {
                    var file = files[i];
                    ReplaceFile(file, destinationPath);
                }
                catch (Exception exc)
                {
                }
                await NotifyUpdateProgress(newVersion, oldVersion, MSG_UPDATE, (float)i / files.Length);
            }
            return true;

        }

        private async Task CloseRavenfallAsync(string newVersion, string oldVersion)
        {
            try
            {
                var ravenfallProcess = System.Diagnostics.Process.GetProcesses().FirstOrDefault(x => x.ProcessName.IndexOf("ravenfall.exe", StringComparison.OrdinalIgnoreCase) >= 0);
                if (ravenfallProcess != null && !ravenfallProcess.HasExited)
                {
                    await NotifyUpdateProgress(newVersion, oldVersion, MSG_WAITING_FOR_RAVENFALL, 0);
                    ravenfallProcess.CloseMainWindow();
                    ravenfallProcess.WaitForExit();
                }
            }
            catch
            {
            }
        }

        private void ReplaceFile(string sourceFile, string destinationPath)
        {
            var additional = sourceFile.Replace(Path.Combine(destinationPath, "update", "unpacked"), "");
            var targetPath = destinationPath + additional;
            var backupPath = destinationPath + "\\backup" + additional;
            //var replacePath = Path.Combine(destinationPath, "update", "unpacked");
            //var backupPath = Path.Combine(destinationPath, "update", "backup");
            //var fileDir = Path.GetDirectoryName(sourceFile);
            //var fileName = Path.GetFileName(sourceFile);
            //var backupFilePath = Path.Combine(sourceFile.Replace(fileDir, backupPath));
            //var targetFilePath = Path.Combine(sourceFile.Replace(fileDir, destinationPath));

            if (sourceFile.Contains("update.json") || sourceFile.EndsWith(".zip") || sourceFile.EndsWith(".7z"))
            {
                return;
            }

            var backupDir = System.IO.Path.GetDirectoryName(backupPath);
            if (!System.IO.Directory.Exists(backupDir))
            {
                System.IO.Directory.CreateDirectory(backupDir);
            }

            var targetDir = System.IO.Path.GetDirectoryName(targetPath);
            if (!System.IO.Directory.Exists(targetDir))
            {
                System.IO.Directory.CreateDirectory(targetDir);
            }

            if (System.IO.File.Exists(sourceFile))
            {
                if (System.IO.File.Exists(targetPath))
                {
                    File.Copy(targetPath, backupPath, true);
                }
                File.Copy(sourceFile, targetPath, true);
            }

            // file: c:\something\ravenfall\update\unpacked\update.json
            // file: c:\something\ravenfall\update\unpacked\data\test
            // destinationPath: c:\something\ravenfall\
        }

        private DispatcherOperation NotifyUpdateProgress(string newVersion, string oldVersion, string messageUpdate, float progress)
        {
            return dispatcher.BeginInvoke((Action)(() =>
            {
                (progress >= 1.0f ? UpdateCompleted : StatusChanged)?
                    .Invoke(this, new GameUpdateChangedEventArgs(
                        messageUpdate,
                        oldVersion,
                        newVersion,
                        progress * 100f));
            }));
        }

        private MetaData GetMetaData()
        {
            if (metadata != null) return metadata;
            var metadataFile = GetMetaFile();
            if (!string.IsNullOrEmpty(metadataFile))
            {
                return metadata = JsonConvert.DeserializeObject<MetaData>(File.ReadAllText(metadataFile));
            }
            return null;
        }

        private UpdateData GetUpdate()
        {
            if (update != null) return update;
            var updateFile = GetUpdateFile();
            if (!string.IsNullOrEmpty(updateFile))
            {
                return update = JsonConvert.DeserializeObject<UpdateData>(File.ReadAllText(updateFile));
            }
            return null;
        }
        private string GetMetaFile()
        {
            if (!string.IsNullOrEmpty(metaFile))
            {
                return metaFile;
            }

            var root = Directory.GetCurrentDirectory();
            metaFile = Directory.GetFiles(root, "metadata.json", System.IO.SearchOption.AllDirectories).OrderBy(x => x.Length).FirstOrDefault();
            return metaFile;
        }

        private string GetUpdateFile()
        {
            if (!string.IsNullOrEmpty(updateFile))
            {
                return updateFile;
            }

            var root = Directory.GetCurrentDirectory();
            metaFile = Directory.GetFiles(root, "update.json", System.IO.SearchOption.AllDirectories).OrderBy(x => x.Length).FirstOrDefault();
            return metaFile;
        }

        private string GetCurrentVersion()
        {
            return GetMetaData().Version;
        }

        private string GetNewVersion()
        {
            return GetUpdate().Version;
        }
    }
}
