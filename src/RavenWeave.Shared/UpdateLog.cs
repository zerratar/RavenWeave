using System;
using System.IO;

namespace Ravenfall.Updater.Core
{
    /// <summary>
    /// update\ravenweave.log in the game folder: what the updater did, so a streamer whose update fails can send it.
    /// Before there was nothing to go on ("stuck at 98%", no log).
    /// </summary>
    public static class UpdateLog
    {
        private static readonly object sync = new object();
        private static string path;

        public static string Path => path;

        public static void Open(string updateFolder)
        {
            try
            {
                Directory.CreateDirectory(updateFolder);
                path = System.IO.Path.Combine(updateFolder, "ravenweave.log");
                // one run per file: the last update is the one worth reading
                File.WriteAllText(path, "");
            }
            catch
            {
                path = null;
            }
        }

        public static void Write(string line)
        {
            if (path == null) return;
            lock (sync)
            {
                try
                {
                    File.AppendAllText(path, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + line + Environment.NewLine);
                }
                catch
                {
                }
            }
        }
    }
}
