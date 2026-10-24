using Newtonsoft.Json;
using RavenWeave.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Ravenfall.Updater.Core
{
    public interface IActionDispatcher
    {
        Task BeginInvoke(Action action);
    }

    public static class OperatingSystem
    {
        public static bool IsWindows() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public static bool IsMacOS() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        public static bool IsLinux() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    }

    /// <summary>
    /// Installs the update the game downloaded into update\ and then starts the game again. The game writes
    /// metadata.json (its version) and update\update.json (the new one), downloads update\update.7z, starts this and
    /// closes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What changed from the first updater, which streamers reported "stuck at 98%" with nothing to go on:
    /// </para>
    /// <list type="bullet">
    /// <item>Progress follows the bytes, unpacking then installing, and shows the file being worked on. It counted
    /// files, and the last few were the biggest data files, copied twice each.</item>
    /// <item>Installing moves files instead of copying them: the old file into backup\, the new one into place, both on
    /// the same drive, so each is a rename. Locked files (the game still closing, a virus scanner) are tried again for a
    /// few seconds, and a file that still cannot be replaced is put back and reported, with Retry.</item>
    /// <item>update\unpacked is emptied first, so files left from an earlier update are never installed again.</item>
    /// <item>Waiting for the game has a limit: asked to close, then ended. It used to wait forever.</item>
    /// <item>Everything goes to update\ravenweave.log.</item>
    /// <item>When it is done, the downloaded archive, the unpacked copy and the backup are removed (over 4 GB).</item>
    /// </list>
    /// <para>
    /// The updater's own files never come with an update (they are in use while it runs): a new updater comes in
    /// updater\, and the game moves it into place the next time it starts.
    /// </para>
    /// </remarks>
    public class GameUpdater : IGameUpdater
    {
        private static readonly TimeSpan WaitForGame = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan WaitAfterClose = TimeSpan.FromSeconds(15);
        private const int FileAttempts = 10;
        private const int FileRetryDelayMs = 500;

        // how the whole bar is split between the steps
        private const float UnpackShare = 55f;
        private const float InstallShare = 42f;

        private readonly GameUpdateUnpacker unpacker;
        private readonly IActionDispatcher dispatcher;

        private string appFolder;
        private string updateFolder;
        private string unpackedFolder;
        private string backupFolder;
        private string oldVersion = "-";
        private string newVersion = "-";
        private DateTime lastReport;
        private int running;
        // files the last install could not replace, for Retry: who holds them and what to try again
        private readonly List<string> lockedPaths = new List<string>();

        public event EventHandler<GameUpdateChangedEventArgs> StatusChanged;
        public event EventHandler<GameUpdateChangedEventArgs> UpdateCompleted;

        public GameUpdater(GameUpdateUnpacker unpacker, IActionDispatcher dispatcher)
        {
            this.unpacker = unpacker;
            this.dispatcher = dispatcher;
        }

        public void Start()
        {
            Run(full: true);
        }

        public void Retry()
        {
            Run(full: false);
        }

        private void Run(bool full)
        {
            if (Interlocked.Exchange(ref running, 1) == 1) return;
            Task.Run(() =>
            {
                try
                {
                    if (full) Update();
                    else
                    {
                        CloseRavenfallPrograms();
                        Install();
                    }
                }
                catch (Exception exc)
                {
                    UpdateLog.Write("Failed: " + exc);
                    Fail("The update failed.", exc.Message);
                }
                finally
                {
                    Interlocked.Exchange(ref running, 0);
                }
            });
        }

        private void Update()
        {
            appFolder = FindAppFolder();
            updateFolder = Path.Combine(appFolder, "update");
            unpackedFolder = Path.Combine(updateFolder, "unpacked");
            backupFolder = Path.Combine(appFolder, "backup");
            UpdateLog.Open(updateFolder);
            UpdateLog.Write("RavenWeave " + typeof(GameUpdater).Assembly.GetName().Version + " in " + appFolder);

            oldVersion = ReadVersion(Path.Combine(appFolder, "metadata.json")) ?? "-";
            newVersion = ReadVersion(Path.Combine(updateFolder, "update.json")) ?? "-";
            UpdateLog.Write("Installed " + oldVersion + ", update " + newVersion);

            var package = FindPackage();
            if (package == null)
            {
                UpdateLog.Write("No update archive in " + updateFolder);
                Fail("No update has been downloaded.", "Start Ravenfall again and it will download the update.");
                return;
            }

            UpdateLog.Write("Archive " + package + " (" + new FileInfo(package).Length / (1024 * 1024) + " MB)");

            WaitForRavenfallToClose();

            Report("Unpacking the update...", 0f, UpdatePhase.Unpacking, null, force: true);
            if (Directory.Exists(unpackedFolder))
            {
                Directory.Delete(unpackedFolder, true);
            }

            var watch = Stopwatch.StartNew();
            unpacker.Extract(package, unpackedFolder, (file, done, total) =>
            {
                var share = total > 0 ? (float)done / total : 0f;
                Report("Unpacking the update...", share * UnpackShare, UpdatePhase.Unpacking, file, done: done, total: total);
            });
            UpdateLog.Write("Unpacked in " + watch.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s");

            // a fresh backup: the files this update replaces, kept until it is done
            if (Directory.Exists(backupFolder))
            {
                TryDeleteFolder(backupFolder);
            }

            Install();
        }

        private void Install()
        {
            var files = Directory.Exists(unpackedFolder)
                ? Directory.GetFiles(unpackedFolder, "*", SearchOption.AllDirectories).Where(IsInstallable).ToList()
                : new List<string>();
            var total = files.Sum(x => SafeLength(x));
            long done = 0;
            var failed = new List<string>();
            lockedPaths.Clear();
            var watch = Stopwatch.StartNew();
            UpdateLog.Write("Installing " + files.Count + " files (" + total / (1024 * 1024) + " MB)");

            foreach (var source in files)
            {
                var relative = source.Substring(unpackedFolder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var length = SafeLength(source);
                Report("Installing...", UnpackShare + (total > 0 ? (float)done / total : 1f) * InstallShare, UpdatePhase.Installing, relative, done: done, total: total);

                if (!InstallFile(source, relative))
                {
                    failed.Add(relative);
                    lockedPaths.Add(source);
                    lockedPaths.Add(Path.Combine(appFolder, relative));
                    lockedPaths.Add(Path.Combine(backupFolder, relative));
                }

                done += length;
            }

            UpdateLog.Write("Installed in " + watch.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s, " + failed.Count + " files could not be replaced");

            if (failed.Count > 0)
            {
                foreach (var f in failed.Take(50)) UpdateLog.Write("  not replaced: " + f);
                Fail(failed.Count == 1 ? "1 file could not be replaced." : failed.Count + " files could not be replaced.", DescribeLocks(failed[0]));
                return;
            }

            Report("Cleaning up...", 98f, UpdatePhase.Finishing, null, force: true);
            WriteVersion(Path.Combine(appFolder, "metadata.json"), newVersion);
            TryDeleteFolder(unpackedFolder);
            TryDeleteFolder(backupFolder);
            foreach (var archive in Directory.GetFiles(updateFolder, "update*.*").Where(IsArchive))
            {
                TryDelete(archive);
            }

            UpdateLog.Write("Done");
            Complete("Ravenfall is up to date! Starting the game...");
            Thread.Sleep(1200);
            StartGameAndExit();
        }

        /// <summary>
        /// Who holds the files that could not be replaced, for the failure message. Programs from the Ravenfall folder
        /// (a game or bot that did not close) are closed by Retry; anything else the streamer closes, since ending an
        /// unknown program (Explorer, a virus scanner) could do harm.
        /// </summary>
        private string DescribeLocks(string firstFailed)
        {
            var lockers = FileLocks.WhoIsLocking(lockedPaths.Where(File.Exists));
            if (lockers.Count == 0)
            {
                return "Something is still using " + firstFailed + ". Wait a moment, or close what is using it, and press Retry.";
            }

            var ours = lockers.Where(IsRavenfallProgram).Select(Describe).Distinct().ToList();
            var others = lockers.Where(x => !IsRavenfallProgram(x)).Select(Describe).Distinct().ToList();
            foreach (var p in lockers) UpdateLog.Write("  in use by " + Describe(p) + " (" + (FileLocks.ExecutableOf(p) ?? "?") + ")");

            var text = "In use by " + string.Join(", ", ours.Concat(others)) + ".";
            if (ours.Count > 0 && others.Count == 0) return text + " Retry closes " + (ours.Count == 1 ? "it" : "them") + ".";
            if (ours.Count > 0) return text + " Retry closes " + string.Join(", ", ours) + "; close " + string.Join(", ", others) + " yourself, then press Retry.";
            return text + " Close " + (others.Count == 1 ? "it" : "them") + ", then press Retry.";
        }

        /// <summary>Retry: closes the programs from the Ravenfall folder that hold files the update needs.</summary>
        private void CloseRavenfallPrograms()
        {
            var lockers = FileLocks.WhoIsLocking(lockedPaths.Where(File.Exists));
            // sorted before closing anything: a closed program can no longer say where it runs from
            var ours = lockers.Where(IsRavenfallProgram).ToList();
            var others = lockers.Except(ours).ToList();
            foreach (var process in ours)
            {
                UpdateLog.Write("Closing " + Describe(process) + ", which holds files the update needs");
                Report("Closing " + Describe(process) + "...", 0f, UpdatePhase.WaitingForGame, null, force: true);
                Try(() =>
                {
                    if (!process.CloseMainWindow() || !process.WaitForExit(5000))
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                });
            }

            foreach (var process in others)
            {
                UpdateLog.Write("Left " + Describe(process) + " running (not a Ravenfall program)");
            }
        }

        /// <summary>A program that runs from the game folder (the game, the bot), or the game by name.</summary>
        private bool IsRavenfallProgram(Process process)
        {
            var exe = FileLocks.ExecutableOf(process);
            if (exe != null && appFolder != null && exe.StartsWith(appFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var name = SafeName(process);
            return name.Equals("Ravenfall", StringComparison.OrdinalIgnoreCase) || name.Equals("Ravenfall Legacy", StringComparison.OrdinalIgnoreCase);
        }

        private static string Describe(Process process) => SafeName(process) + ".exe";

        private static string SafeName(Process process)
        {
            try { return process.ProcessName; } catch { return "a program"; }
        }

        /// <summary>The old file into backup\, the new one into place, each tried again while locked.</summary>
        private bool InstallFile(string source, string relative)
        {
            var target = Path.Combine(appFolder, relative);
            var backup = Path.Combine(backupFolder, relative);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                var hadOld = File.Exists(target);
                if (hadOld)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backup));
                    if (!Attempt(() =>
                    {
                        if (File.Exists(backup)) { File.SetAttributes(backup, FileAttributes.Normal); File.Delete(backup); }
                        File.SetAttributes(target, FileAttributes.Normal);
                        File.Move(target, backup);
                    }, "move " + relative + " to the backup"))
                    {
                        return false;
                    }
                }

                if (Attempt(() => MoveOrCopy(source, target), "install " + relative))
                {
                    return true;
                }

                // not installed: put the old one back so the game is still whole
                if (hadOld)
                {
                    Attempt(() => File.Move(backup, target), "restore " + relative);
                }

                return false;
            }
            catch (Exception exc)
            {
                UpdateLog.Write("Could not install " + relative + ": " + exc.Message);
                return false;
            }
        }

        private static void MoveOrCopy(string source, string target)
        {
            try
            {
                File.Move(source, target);
            }
            catch (IOException) when (!File.Exists(target))
            {
                // another drive (a moved update folder): copy instead
                File.Copy(source, target, true);
                File.Delete(source);
            }
        }

        private static bool Attempt(Action action, string what)
        {
            for (var i = 1; i <= FileAttempts; ++i)
            {
                try
                {
                    action();
                    return true;
                }
                catch (Exception exc) when (exc is IOException || exc is UnauthorizedAccessException)
                {
                    if (i == FileAttempts)
                    {
                        UpdateLog.Write("Could not " + what + " after " + FileAttempts + " tries: " + exc.Message);
                        return false;
                    }

                    Thread.Sleep(FileRetryDelayMs);
                }
            }

            return false;
        }

        private static bool IsInstallable(string file)
        {
            var name = Path.GetFileName(file);
            return !name.Equals("update.json", StringComparison.OrdinalIgnoreCase) && !IsArchive(file);
        }

        private static bool IsArchive(string file)
        {
            var extension = Path.GetExtension(file).ToLowerInvariant();
            return extension == ".7z" || extension == ".zip" || extension == ".gz" || extension == ".tar";
        }

        /// <summary>This program's own folder, where the game is; the old search for metadata.json as a fallback.</summary>
        private static string FindAppFolder()
        {
            var own = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            if (File.Exists(Path.Combine(own, "Ravenfall.exe")) || File.Exists(Path.Combine(own, "Ravenfall.x86_64")) || File.Exists(Path.Combine(own, "metadata.json")))
            {
                return own;
            }

            var current = Directory.GetCurrentDirectory();
            var meta = Directory.GetFiles(current, "metadata.json", SearchOption.AllDirectories).OrderBy(x => x.Length).FirstOrDefault();
            return meta != null ? Path.GetDirectoryName(meta) : own;
        }

        private string FindPackage()
        {
            if (!Directory.Exists(updateFolder)) return null;
            foreach (var name in new[] { "update.7z", "update-linux.7z", "update.zip" })
            {
                var path = Path.Combine(updateFolder, name);
                if (File.Exists(path)) return path;
            }

            return Directory.GetFiles(updateFolder, "update*.*", SearchOption.TopDirectoryOnly).FirstOrDefault(IsArchive);
        }

        private void WaitForRavenfallToClose()
        {
            var processes = FindRavenfall();
            if (processes.Count == 0) return;

            Report("Waiting for Ravenfall to close...", 0f, UpdatePhase.WaitingForGame, null, force: true);
            UpdateLog.Write("Waiting for " + processes.Count + " Ravenfall process(es) to close");
            if (WaitForExit(processes, WaitForGame)) return;

            foreach (var p in processes) Try(() => p.CloseMainWindow());
            if (WaitForExit(processes, WaitAfterClose)) return;

            UpdateLog.Write("Ravenfall did not close; ending it");
            foreach (var p in processes) Try(() => p.Kill());
            WaitForExit(processes, TimeSpan.FromSeconds(5));
        }

        /// <summary>The game, by name: "Ravenfall" (or the older "Ravenfall Legacy"), not this updater or the bot.</summary>
        private static List<Process> FindRavenfall()
        {
            var self = Process.GetCurrentProcess().Id;
            return Process.GetProcesses()
                .Where(p => p.Id != self && (p.ProcessName.Equals("Ravenfall", StringComparison.OrdinalIgnoreCase)
                    || p.ProcessName.Equals("Ravenfall Legacy", StringComparison.OrdinalIgnoreCase)
                    || p.ProcessName.Equals("Ravenfall.x86_64", StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        private static bool WaitForExit(List<Process> processes, TimeSpan limit)
        {
            var until = DateTime.UtcNow + limit;
            foreach (var p in processes)
            {
                var left = until - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) return processes.All(HasExited);
                Try(() => p.WaitForExit((int)left.TotalMilliseconds));
            }

            return processes.All(HasExited);
        }

        private static bool HasExited(Process p)
        {
            try { return p.HasExited; } catch { return true; }
        }

        public void StartGameAndExit()
        {
            try
            {
                var folder = appFolder ?? FindAppFolder();
                var exe = OperatingSystem.IsLinux() ? Path.Combine(folder, "Ravenfall.x86_64") : Path.Combine(folder, "Ravenfall.exe");
                UpdateLog.Write("Starting " + exe);
                Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = folder, UseShellExecute = true });
            }
            catch (Exception exc)
            {
                UpdateLog.Write("Could not start the game: " + exc.Message);
            }

            dispatcher.BeginInvoke(() =>
            {
                try { Environment.Exit(0); }
                catch { try { Process.GetCurrentProcess().Kill(); } catch { } }
            });
        }

        private static string ReadVersion(string file)
        {
            try
            {
                if (!File.Exists(file)) return null;
                var json = JsonConvert.DeserializeObject<UpdateData>(File.ReadAllText(file));
                return string.IsNullOrWhiteSpace(json?.Version) ? null : json.Version;
            }
            catch (Exception exc)
            {
                UpdateLog.Write("Could not read " + file + ": " + exc.Message);
                return null;
            }
        }

        private static void WriteVersion(string file, string version)
        {
            if (string.IsNullOrEmpty(version) || version == "-") return;
            Try(() => File.WriteAllText(file, JsonConvert.SerializeObject(new MetaData { Version = version })));
        }

        private static long SafeLength(string file)
        {
            try { return new FileInfo(file).Length; } catch { return 0; }
        }

        private static void TryDelete(string file)
        {
            Try(() => { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); });
        }

        private static void TryDeleteFolder(string folder)
        {
            if (!Directory.Exists(folder)) return;
            if (!Attempt(() => Directory.Delete(folder, true), "remove " + folder))
            {
                UpdateLog.Write("Left " + folder + " behind");
            }
        }

        private static void Try(Action action)
        {
            try { action(); } catch { }
        }

        private void Report(string message, float progress, UpdatePhase phase, string detail, bool force = false, long done = 0, long total = 0)
        {
            // the window needs ten updates a second, not one per megabyte
            var now = DateTime.UtcNow;
            if (!force && (now - lastReport).TotalMilliseconds < 100) return;
            lastReport = now;
            var args = new GameUpdateChangedEventArgs(message, oldVersion, newVersion, Math.Min(100f, progress), phase, detail, done, total);
            dispatcher.BeginInvoke(() => StatusChanged?.Invoke(this, args));
        }

        private void Fail(string message, string detail)
        {
            var args = new GameUpdateChangedEventArgs(message, oldVersion, newVersion, 0f, UpdatePhase.Failed, detail);
            dispatcher.BeginInvoke(() => StatusChanged?.Invoke(this, args));
        }

        private void Complete(string message)
        {
            var args = new GameUpdateChangedEventArgs(message, oldVersion, newVersion, 100f, UpdatePhase.Done);
            dispatcher.BeginInvoke(() => UpdateCompleted?.Invoke(this, args));
        }
    }
}
