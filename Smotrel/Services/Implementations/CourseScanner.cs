
using Smotrel.Data.Entities;
using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Smotrel.Services.Interfaces;

namespace Smotrel.Services.Implementations
{
    public class CourseScanner : ICourseScanner
    {
        private static readonly string[] DefaultVideoExts = { ".mp4", ".mkv", ".avi", ".mov", ".webm", ".flv" };

        public async Task<CourseEntity> ScanAsync(string rootPath, bool tryGetDurations = false, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentNullException(nameof(rootPath));
            if (!Directory.Exists(rootPath)) throw new DirectoryNotFoundException(rootPath);

            var course = new CourseEntity
            {
                RootPath = Path.GetFullPath(rootPath),
                Title = Path.GetFileName(rootPath),
                CreatedAt = DateTime.UtcNow
            };

            // compute fs hash synchronously (fast). If you want async, wrap in Task.Run.
            var fsHash = await Task.Run(() => FileHashHelper.ComputeFsHash(rootPath, DefaultVideoExts));
            course.FsHash = fsHash;
            course.LastScannedAt = DateTime.UtcNow;

            var chapters = new List<ChapterEntity>();

            // walk directories depth-first
            async Task ProcessDir(string dir)
            {
                ct.ThrowIfCancellationRequested();

                var files = Directory.EnumerateFiles(dir)
                    .Where(f => DefaultVideoExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => f, StringComparer.Ordinal)
                    .ToList();

                var parts = new List<PartEntity>();

                int sequentialIndex = 1;
                foreach (var f in files)
                {
                    ct.ThrowIfCancellationRequested();
                    var fi = new FileInfo(f);
                    var parsedIndex = ExtractIndexFromFileName(fi.Name);
                    var index = parsedIndex ?? int.MaxValue; // temp
                    var part = new PartEntity
                    {
                        Id = Guid.NewGuid(),
                        FileName = fi.Name,
                        Path = fi.FullName,
                        Index = parsedIndex,
                        Title = Path.GetFileNameWithoutExtension(fi.Name),
                        FileSizeBytes = fi.Exists ? fi.Length : 0L,
                        DurationSeconds = null,
                        LastPositionSeconds = 0,
                        Watched = false
                    };

                    // Try get duration via ffprobe if requested
                    if (tryGetDurations)
                    {
                        try
                        {
                            var d = await TryGetDurationSecondsAsync(fi.FullName);
                            if (d.HasValue) part.DurationSeconds = d.Value;
                        }
                        catch { /* ignore */ }
                    }

                    parts.Add(part);
                }

                if (parts.Count > 0)
                {
                    // assign stable indices: if any part has no index (null) or index==int.Max => assign sequential in file order
                    int seq = 1;
                    foreach (var p in parts)
                    {
                        if (!p.Index.HasValue || p.Index.Value == int.MaxValue)
                        {
                            p.Index = seq++;
                        }
                    }

                    // sort parts by Index then filename
                    parts.Sort((a, b) =>
                    {
                        var na = a.Index ?? int.MaxValue;
                        var nb = b.Index ?? int.MaxValue;
                        if (na != nb) return na - nb;
                        return string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);
                    });

                    // determine chapter meta
                    var rel = Path.GetRelativePath(rootPath, dir).Replace(Path.DirectorySeparatorChar, '/');
                    if (string.IsNullOrEmpty(rel)) rel = ".";
                    var chapterTitle = rel == "." ? Path.GetFileName(rootPath) : Path.GetFileName(dir);

                    // extract order from folder name or fallback to min(part.index)
                    int? order = ExtractOrderFromName(chapterTitle);
                    if (!order.HasValue)
                    {
                        var numericIndices = parts.Select(p => p.Index).Where(i => i.HasValue).Select(i => i!.Value).ToList();
                        if (numericIndices.Count > 0) order = numericIndices.Min();
                    }

                    var chapter = new ChapterEntity
                    {
                        Id = Guid.NewGuid(),
                        Title = chapterTitle,
                        RelPath = rel,
                        Order = order
                    };
                    chapter.Parts = parts;
                    chapters.Add(chapter);
                }

                // recurse into subdirs
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    ct.ThrowIfCancellationRequested();
                    await ProcessDir(sub);
                }
            }

            await ProcessDir(course.RootPath);

            // sort chapters by order then by title natural-ish
            course.Chapters = chapters
                .OrderBy(ch => ch.Order ?? int.MaxValue)
                .ThenBy(ch => ch.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // compute totals
            long totalParts = 0;
            long totalDuration = 0;
            bool anyDuration = false;
            foreach (var ch in course.Chapters)
            {
                totalParts += ch.Parts.Count;
                foreach (var p in ch.Parts)
                {
                    if (p.DurationSeconds.HasValue)
                    {
                        anyDuration = true;
                        totalDuration += p.DurationSeconds.Value;
                    }
                }
            }
            course.TotalDurationSeconds = anyDuration ? totalDuration : null;

            return course;
        }

        private static int? ExtractIndexFromFileName(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename)) return null;
            // Common patterns: [Platform] 01. Title.ext  OR 01. Title.ext  OR 1 - Title.ext
            // Try several regexes in order
            var m = Regex.Match(filename, @"^\s*\[[^\]]+\]\s*(\d{1,4})\b");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var v)) return v;

            m = Regex.Match(filename, @"^\s*(\d{1,4})\b");
            if (m.Success && int.TryParse(m.Groups[1].Value, out v)) return v;

            // try first number inside name
            m = Regex.Match(filename, @"(\d{1,4})");
            if (m.Success && int.TryParse(m.Groups[1].Value, out v)) return v;

            return null;
        }

        private static int? ExtractOrderFromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            // Try [Platform] 01. or 01. or "01 Title" variants
            var m = Regex.Match(name, @"^\s*(?:\[[^\]]+\]\s*)?(\d{1,4})\s*(?:[.\uFF0E\uFF61\-\s_:])?");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var v)) return v;
            m = Regex.Match(name, @"^\s*(\d{1,4})\b");
            if (m.Success && int.TryParse(m.Groups[1].Value, out v)) return v;
            return null;
        }

        private async Task<long?> TryGetDurationSecondsAsync(string filePath, int timeoutMs = 7000)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return null;

            try
            {
                var ffprobePath = "ffprobe";
                var args = $"-v quiet -print_format json -show_format \"{filePath}\"";

                var startInfo = new ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var waitTask = process.WaitForExitAsync();
                var finished = await Task.WhenAny(waitTask, Task.Delay(timeoutMs));
                if (finished != waitTask)
                {
                    try { process.Kill(true); } catch { }
                    return null;
                }

                var output = await outputTask;
                if (string.IsNullOrWhiteSpace(output)) return null;

                using var doc = JsonDocument.Parse(output);
                if (doc.RootElement.TryGetProperty("format", out var formatElem) &&
                    formatElem.TryGetProperty("duration", out var durationElem))
                {
                    var s = durationElem.GetString();
                    if (double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var secs))
                        return (long)Math.Round(secs);
                }
            }
            catch
            {
                // ignore and return null
            }

            return null;
        }
    }
}
