using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Smotrel.Services.Implementations
{
    public static class FileHashHelper
    {
        /// <summary>
        /// Deterministic incremental SHA256 on list of files (relativePath|size|ticks) sorted by relative path.
        /// Excludes .smotrel folder.
        /// </summary>
        public static string ComputeFsHash(string rootPath, string[] includeExts = null)
        {
            if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentNullException(nameof(rootPath));
            includeExts ??= new[] { ".mp4", ".mkv", ".avi", ".mov", ".webm", ".flv" };

            var repoPrefix = Path.Combine(rootPath, ".smotrel") + Path.DirectorySeparatorChar;

            var files = Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
                .Where(f => !f.StartsWith(repoPrefix, StringComparison.OrdinalIgnoreCase))
                .Where(f => includeExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Select(f =>
                {
                    var fi = new FileInfo(f);
                    return new
                    {
                        Rel = Path.GetRelativePath(rootPath, fi.FullName).Replace(Path.DirectorySeparatorChar, '/'),
                        Size = fi.Exists ? fi.Length : 0L,
                        Ticks = fi.Exists ? fi.LastWriteTimeUtc.Ticks : 0L
                    };
                })
                .OrderBy(x => x.Rel, StringComparer.Ordinal)
                .ToArray();

            using var sha = SHA256.Create();
            foreach (var e in files)
            {
                var line = $"{e.Rel}|{e.Size}|{e.Ticks}\n";
                var bytes = Encoding.UTF8.GetBytes(line);
                sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
