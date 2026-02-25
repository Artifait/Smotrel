
using Smotrel.Models;
using System.IO;
using System.Text.Json;

namespace Smotrel.Services
{
    public class ProgressService
    {
        private readonly JsonSerializerOptions _opts = new JsonSerializerOptions { WriteIndented = true };

        public record VideoProgressRecord(string RelativePath, long LastPositionTicks, bool IsWatched);

        public async Task SaveProgressAsync(string courseFolder, IEnumerable<VideoModel> videos)
        {
            if (string.IsNullOrWhiteSpace(courseFolder)) return;
            var file = Path.Combine(courseFolder, "SmotrelProgress.json");
            var list = new List<VideoProgressRecord>();
            foreach (var v in videos)
            {
                var rel = MakeRelativePath(courseFolder, v.Path) ?? v.Path;
                list.Add(new VideoProgressRecord(rel, v.LastPosition.Ticks, v.IsWatched));
            }
            var tmp = file + ".tmp";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(list, _opts);
            await File.WriteAllBytesAsync(tmp, bytes);
            try
            {
                if (File.Exists(file))
                    File.Replace(tmp, file, file + ".old");
                else
                    File.Move(tmp, file);
            }
            catch
            {
                try { File.Move(tmp, file, true); } catch { }
            }
        }

        public List<VideoProgressRecord> LoadProgress(string courseFolder)
        {
            var res = new List<VideoProgressRecord>();
            if (string.IsNullOrWhiteSpace(courseFolder)) return res;
            var file = Path.Combine(courseFolder, "SmotrelProgress.json");
            if (!File.Exists(file)) return res;
            try
            {
                var json = File.ReadAllText(file);
                res = JsonSerializer.Deserialize<List<VideoProgressRecord>>(json) ?? new List<VideoProgressRecord>();
            }
            catch
            {
                return new List<VideoProgressRecord>();
            }
            return res;
        }

        private static string? MakeRelativePath(string baseDir, string filePath)
        {
            try
            {
                var b = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var f = Path.GetFullPath(filePath);
                if (!f.StartsWith(b, StringComparison.OrdinalIgnoreCase)) return null;
                return f.Substring(b.Length);
            }
            catch
            {
                return null;
            }
        }
    }
}