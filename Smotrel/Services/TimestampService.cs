
using Smotrel.Models;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Smotrel.Services
{
    public class TimestampService
    {
        private static readonly Regex TimeLineRegex = new Regex(@"^\[(\d{1,2}:\d{2}:\d{2})\]\s*-\s*%(.+)%\s*$", RegexOptions.Compiled);

        public Dictionary<string, List<VideoTimestamp>> LoadTimestampsFromCourse(string courseFolder)
        {
            var map = new Dictionary<string, List<VideoTimestamp>>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(courseFolder)) return map;
            var file = Path.Combine(courseFolder, "SmotrelTimestamp.txt");
            if (!File.Exists(file)) return map;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(file, Encoding.UTF8);
            }
            catch
            {
                return map;
            }

            string? currentRelative = null;
            foreach (var raw in lines)
            {
                var line = raw?.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (!line.StartsWith("[") && !line.StartsWith("#") && !line.StartsWith("//") && !line.StartsWith("["))
                {
                    currentRelative = line;
                    if (!map.ContainsKey(currentRelative)) map[currentRelative] = new List<VideoTimestamp>();
                    continue;
                }
                if (currentRelative == null) continue;
                var m = TimeLineRegex.Match(line);
                if (!m.Success) continue;
                if (!TimeSpan.TryParse(m.Groups[1].Value, out var span)) continue;
                var desc = m.Groups[2].Value;
                var placeholder = new VideoTimestamp(new PlaceholderVideo(currentRelative), span, desc);
                map[currentRelative].Add(placeholder);
            }

            foreach (var k in map.Keys.ToList())
            {
                map[k] = map[k].OrderBy(t => t.Time).ToList();
            }

            return map;
        }

        public bool AppendTimestampToCourse(string courseFolder, string relativeVideoPath, VideoTimestamp timestamp)
        {
            if (string.IsNullOrWhiteSpace(courseFolder)) return false;
            if (string.IsNullOrWhiteSpace(relativeVideoPath)) return false;
            var file = Path.Combine(courseFolder, "SmotrelTimestamp.txt");
            try
            {
                var existing = new List<string>();
                if (File.Exists(file))
                    existing.AddRange(File.ReadAllLines(file, Encoding.UTF8));

                var lines = new List<string>(existing);
                int insertIndex = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].Trim().Equals(relativeVideoPath, StringComparison.OrdinalIgnoreCase))
                    {
                        insertIndex = i;
                        break;
                    }
                }
                if (insertIndex == -1)
                {
                    if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines.Last()))
                        lines.Add(string.Empty);
                    lines.Add(relativeVideoPath);
                    lines.Add($"[{timestamp.Time:hh\\:mm\\:ss}] - %{EscapeForFile(timestamp.Description)}%");
                }
                else
                {
                    int j = insertIndex + 1;
                    while (j < lines.Count && lines[j].StartsWith("["))
                        j++;
                    var block = new List<(TimeSpan time, string line)>();
                    int k = insertIndex + 1;
                    while (k < lines.Count && TimeLineRegex.IsMatch(lines[k]))
                    {
                        var mm = TimeLineRegex.Match(lines[k]);
                        if (TimeSpan.TryParse(mm.Groups[1].Value, out var sp))
                            block.Add((sp, lines[k]));
                        k++;
                    }
                    block.Add((timestamp.Time, $"[{timestamp.Time:hh\\:mm\\:ss}] - %{EscapeForFile(timestamp.Description)}%"));
                    block = block.OrderBy(x => x.time).ToList();
                    lines.RemoveRange(insertIndex + 1, k - (insertIndex + 1));
                    lines.InsertRange(insertIndex + 1, block.Select(x => x.line));
                }

                var tmp = file + ".tmp";
                File.WriteAllLines(tmp, lines, Encoding.UTF8);
                File.Replace(tmp, file, null);
                return true;
            }
            catch
            {
                try
                {
                    var backup = file + ".bak";
                    File.AppendAllLines(file, new[] { relativeVideoPath, $"[{timestamp.Time:hh\\:mm\\:ss}] - %{EscapeForFile(timestamp.Description)}%" }, Encoding.UTF8);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        private string EscapeForFile(string s) => s.Replace("%", "%%");

        private class PlaceholderVideo : IVideo
        {
            public int Id => -1;
            public int RelativeIndex => -1;
            public int AbsoluteIndex => -1;
            public string Title { get; }
            public string Path { get; }
            public TimeSpan Duration => TimeSpan.Zero;

            public PlaceholderVideo(string relativePath)
            {
                Path = relativePath;
                Title = relativePath;
            }
        }
    }
}