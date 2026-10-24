using SharpCompress.Archives;
using SharpCompress.Common;
using System;
using System.IO;

namespace RavenWeave.Core
{
    /// <summary>
    /// Unpacks the update archive (7z or zip) into a folder, reporting the bytes written so far against the total, so
    /// progress follows the data rather than the number of files (a few huge data files at the end made the old
    /// progress sit at the top for minutes).
    /// </summary>
    public class GameUpdateUnpacker
    {
        private const int BufferSize = 1 << 20;

        /// <param name="progress">The entry being written, bytes written so far, bytes in all.</param>
        public void Extract(string archivePath, string destination, Action<string, long, long> progress)
        {
            var root = Path.GetFullPath(destination);
            Directory.CreateDirectory(root);

            using (var archive = ArchiveFactory.Open(archivePath))
            {
                var total = archive.TotalUncompressSize;
                long done = 0;
                var buffer = new byte[BufferSize];

                // one pass over the whole archive: a solid 7z can only be read front to back
                using (var reader = archive.ExtractAllEntries())
                {
                    while (reader.MoveToNextEntry())
                    {
                        var entry = reader.Entry;
                        var key = entry.Key.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                        var target = Path.GetFullPath(Path.Combine(root, key));

                        // an entry naming a place outside the folder ("..\") is never written
                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (entry.IsDirectory)
                        {
                            Directory.CreateDirectory(target);
                            continue;
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        progress?.Invoke(key, done, total);
                        using (var input = reader.OpenEntryStream())
                        using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
                        {
                            int read;
                            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                output.Write(buffer, 0, read);
                                done += read;
                                progress?.Invoke(key, done, total);
                            }
                        }
                    }
                }

                progress?.Invoke(null, total, total);
            }
        }
    }
}
