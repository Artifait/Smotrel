using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Smotrel.Services
{
    public static class FileHashHelper
    {
        /// <summary>
        /// Детерминированно обходит файловую систему (включая поддиректории),
        /// берет только видео/важные файлы (если нужно — можно расширить список),
        /// формирует строки "relativePath|size|lastWriteUtcTicks" отсортированные по relativePath,
        /// и возвращает SHA256 hex.
        /// </summary>
        public static async Task<string> ComputeFsHashAsync(string rootPath, string[] includeExts = null)
        {
            if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentNullException(nameof(rootPath));
            includeExts ??= new[] { ".mp4", ".mkv", ".avi", ".mov", ".webm", ".flv" };

            var files = Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
                                 .Where(f => includeExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                 .Select(f => new FileInfo(f))
                                 .Where(fi => fi.Exists)
                                 .Select(fi => new
                                 {
                                     Rel = Path.GetRelativePath(rootPath, fi.FullName).Replace(Path.DirectorySeparatorChar, '/'),
                                     Size = fi.Length,
                                     Ticks = fi.LastWriteTimeUtc.Ticks
                                 })
                                 .OrderBy(x => x.Rel, StringComparer.Ordinal);

            // build canonical string
            var sb = new StringBuilder();
            foreach (var e in files)
            {
                sb.Append(e.Rel);
                sb.Append('|');
                sb.Append(e.Size);
                sb.Append('|');
                sb.Append(e.Ticks);
                sb.Append('\n');
            }

            var raw = Encoding.UTF8.GetBytes(sb.ToString());

            using var sha = SHA256.Create();
            var hash = await Task.Run(() => sha.ComputeHash(raw));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
