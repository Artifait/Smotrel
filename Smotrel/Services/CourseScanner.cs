
using Smotrel.Data.Entities;
using System.IO;
using System.Diagnostics;
using System.Text.Json;

namespace Smotrel.Services
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
                Title = Path.GetFileName(rootPath)
            };

            // compute fs hash (async)
            var fsHashTask = FileHashHelper.ComputeFsHashAsync(rootPath, DefaultVideoExts);

            // walk dir and build chapters
            var chapters = new List<ChapterEntity>();

            // Strategy: each directory that has video files becomes chapter; root can be chapter too.
            async Task ProcessDir(string dir)
            {
                ct.ThrowIfCancellationRequested();

                var files = Directory.GetFiles(dir)
                                     .Where(f => DefaultVideoExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                     .OrderBy(f => f, StringComparer.Ordinal)
                                     .ToList();

                if (files.Count > 0)
                {
                    var rel = Path.GetRelativePath(rootPath, dir).Replace(Path.DirectorySeparatorChar, '/');
                    if (string.IsNullOrEmpty(rel) || rel == ".") rel = ".";
                    var ch = new ChapterEntity
                    {
                        Title = Path.GetFileName(dir),
                        RelPath = rel
                    };

                    int idx = 1;
                    foreach (var f in files)
                    {
                        ct.ThrowIfCancellationRequested();
                        var fi = new FileInfo(f);
                        var part = new PartEntity
                        {
                            FileName = Path.GetFileName(f),
                            Path = fi.FullName,
                            Index = idx++,
                            Title = Path.GetFileNameWithoutExtension(f),
                            FileSizeBytes = fi.Exists ? fi.Length : 0,
                            // DurationSeconds can be filled by ffprobe if requested
                            DurationSeconds = null,
                            LastPositionSeconds = 0,
                            Watched = false
                        };

                        if (tryGetDurations)
                        {
                            // optional: call ffprobe (platform dependent), fill DurationSeconds.
                            // I'll leave a placeholder method TryGetDurationSecondsAsync for extension.
                            part.DurationSeconds = await TryGetDurationSecondsAsync(f);
                        }

                        ch.Parts.Add(part);
                    }

                    chapters.Add(ch);
                }

                // recurse
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    ct.ThrowIfCancellationRequested();
                    await ProcessDir(sub);
                }
            }

            await ProcessDir(rootPath);

            course.Chapters = chapters;
            course.LastScannedAt = DateTime.UtcNow;
            course.FsHash = await fsHashTask;

            // compute total duration if available
            long totalDur = 0;
            bool anyDur = false;
            foreach (var ch in chapters)
            {
                foreach (var p in ch.Parts)
                {
                    if (p.DurationSeconds.HasValue) { totalDur += p.DurationSeconds.Value; anyDur = true; }
                }
            }
            course.TotalDurationSeconds = anyDur ? totalDur : null;

            return course;
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
                var errorTask = process.StandardError.ReadToEndAsync();

                var waitTask = process.WaitForExitAsync();
                var finished = await Task.WhenAny(waitTask, Task.Delay(timeoutMs));

                if (finished != waitTask)
                {
                    // timeout
                    try { process.Kill(true); } catch { /*ignore*/ }
                    return null;
                }

                // ensure we have output
                var output = await outputTask;
                if (string.IsNullOrWhiteSpace(output))
                    return null;

                using var doc = JsonDocument.Parse(output);
                if (doc.RootElement.TryGetProperty("format", out var formatElem) &&
                    formatElem.TryGetProperty("duration", out var durationElem))
                {
                    var durationStr = durationElem.GetString();
                    if (double.TryParse(durationStr, System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture,
                                        out var secs))
                    {
                        return (long)Math.Round(secs);
                    }
                }
            }
            catch
            {
                // ignore and return null for robustness
            }

            return null;
        }
    }
}
