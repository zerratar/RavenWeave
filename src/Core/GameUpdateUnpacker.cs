using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace RavenWeave.Core
{
    public class GameUpdateUnpacker
    {
        private static readonly HashSet<string> FileCompressionExtensions = new HashSet<string>
        {
            ".zip", ".rar", ".gzip", ".gz", ".7z", ".tar", ".tar.gz"
        };

        public Task<bool> DecompressArchive(
            string srcFilePath,
            string destDirPath,
            Action<string> onUnzipped)
        {
            switch (Path.GetExtension(srcFilePath)?.ToLower())
            {
                case ".rar": return DecompressRarAsync(srcFilePath, destDirPath, onUnzipped);
                case ".zip": return DecompressZipAsync(srcFilePath, destDirPath, onUnzipped);
                case ".7z": return Decompress7ZipAsync(srcFilePath, destDirPath, onUnzipped);
                case ".tar": return DecompressTarAsync(srcFilePath, destDirPath, onUnzipped);
                case ".gz":
                case ".gzip":
                    return DecompressGZipAsync(srcFilePath, destDirPath, onUnzipped);

                default:
                    throw new NotImplementedException($"The archive extension: {Path.GetExtension(srcFilePath)?.ToLower()} has not yet been implemented.");
            }
        }
        
        private async Task<bool> DecompressArchive(
            string srcFilePath,
            string destDirPath,
            Action<string> onUnzipped,
            Func<Stream, IArchive> archiveOpener)
        {
            var file = new FileInfo(srcFilePath);
            using (var fileReader = file.OpenRead())
            using (var reader = archiveOpener(fileReader))
            {
                if (reader.IsSolid)
                {
                    var entryReader = reader.ExtractAllEntries();
                    while (entryReader.MoveToNextEntry())
                    {
                        await UnpackSubtitleEntryAsync(entryReader, entryReader.Entry, destDirPath, onUnzipped);
                    }
                }
                else
                {
                    foreach (var entry in reader.Entries)
                    {
                        await UnpackEntryAsync(entry, destDirPath, onUnzipped);
                    }
                }
            }
            return true;
        }

        private async Task<EntryUnpackResult> UnpackSubtitleEntryAsync(IReader reader, IEntry entry, string directory, Action<string> onUnzipped)
        {
            return await UnpackEntryAsync(reader, entry, directory, onUnzipped);
        }

        private Task<EntryUnpackResult> UnpackEntryAsync(IReader reader, IEntry entry, string directory, Action<string> onUnzipped)
        {
            return this.UnpackEntryAsync(reader.OpenEntryStream, entry, directory, onUnzipped);
        }

        private Task<EntryUnpackResult> UnpackEntryAsync(IArchiveEntry entry, string directory, Action<string> onUnzipped)
        {
            return this.UnpackEntryAsync(entry.OpenEntryStream, entry, directory, onUnzipped);
        }

        private async Task<EntryUnpackResult> UnpackEntryAsync(
            Func<Stream> entryStreamProvider,
            IEntry entry,
            string directory,
            Action<string> onUnzipped)
        {
            var ext = Path.GetExtension(entry.Key);
            var targetFile = Path.Combine(directory, Path.ChangeExtension(entry.Key.Replace("?", ""), ext));
            var dir = new FileInfo(targetFile).Directory;
            if (dir != null && !dir.Exists)
            {
                dir.Create();
            }

            var targetFileInfo = new FileInfo(targetFile);
            using (var entryStream = entryStreamProvider())
            using (var sw = targetFileInfo.Create())
            {
                var read = 0;
                var buffer = new byte[4096];
                while ((read = await entryStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                {
                    await sw.WriteAsync(buffer, 0, read);
                }

                onUnzipped?.Invoke(entry.Key);

                return new EntryUnpackResult(filename: targetFile, entry: entry.Key);
            }
        }

        private Task<bool> DecompressRarAsync(string filename, string destDirPath, Action<string> onUnzipped) =>
            DecompressArchive(filename, destDirPath, onUnzipped, x => SharpCompress.Archives.Rar.RarArchive.Open(x));
        private Task<bool> DecompressZipAsync(string filename, string destDirPath, Action<string> onUnzipped) =>
            DecompressArchive(filename, destDirPath, onUnzipped, x => SharpCompress.Archives.Zip.ZipArchive.Open(x));
        private Task<bool> DecompressGZipAsync(string filename, string destDirPath, Action<string> onUnzipped) =>
            DecompressArchive(filename, destDirPath, onUnzipped, x => SharpCompress.Archives.GZip.GZipArchive.Open(x));
        private Task<bool> Decompress7ZipAsync(string filename, string destDirPath, Action<string> onUnzipped) =>
            DecompressArchive(filename, destDirPath, onUnzipped, x => SharpCompress.Archives.SevenZip.SevenZipArchive.Open(x));
        private Task<bool> DecompressTarAsync(string filename, string destDirPath, Action<string> onUnzipped) =>
            DecompressArchive(filename, destDirPath, onUnzipped, x => SharpCompress.Archives.Tar.TarArchive.Open(x));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsCompressed(string extension) => FileCompressionExtensions.Contains(extension.ToLower());
        private struct EntryUnpackResult
        {
            public readonly string Filename;
            public readonly string Entry;

            public EntryUnpackResult(string filename, string entry)
            {
                Filename = filename;
                Entry = entry;
            }
        }
    }

}

