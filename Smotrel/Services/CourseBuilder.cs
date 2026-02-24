
using System.IO;
using System.Text.RegularExpressions;
using Smotrel.Models;

namespace Smotrel.Services
{
    public class CourseBuilder
    {
        private int _absoluteVideoIndex;
        private readonly HashSet<string> _visitedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _courseCoreName = string.Empty;
        private List<string> _courseBracketTags = new List<string>();

        public CourseModel BuildFromDirectory(string rootPath, IEnumerable<string>? videoExtensions = null, bool returnAbsolutePaths = false, int maxDepth = 20)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
                throw new ArgumentException("rootPath is required", nameof(rootPath));

            var fullRoot = Path.GetFullPath(rootPath);
            if (!Directory.Exists(fullRoot))
                throw new DirectoryNotFoundException(fullRoot);

            var extensions = (videoExtensions ?? DefaultVideoExtensions())
                .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
                .ToHashSet();

            var rootInfo = new DirectoryInfo(fullRoot);

            ExtractCourseTokens(rootInfo.Name);

            var course = new CourseModel
            {
                Label = CleanCourseLabel(rootInfo.Name)
            };

            var main = BuildChapter(rootInfo, fullRoot, extensions, returnAbsolutePaths, 0, maxDepth);
            main.RelativeIndex = 0;
            course.MainChapter = main;

            return course;
        }

        private ChapterCourseModel BuildChapter(DirectoryInfo dir, string rootFullPath, HashSet<string> videoExtensions, bool returnAbsolutePaths, int depth, int maxDepth)
        {
            var dirFull = SafeGetFullPath(dir);
            if (dirFull == null)
                return CreateEmptyChapter(dir, dir.FullName, returnAbsolutePaths);

            if (depth > maxDepth)
                return CreateEmptyChapter(dir, dirFull, returnAbsolutePaths);

            if (!_visitedDirs.Add(dirFull))
                return CreateEmptyChapter(dir, dirFull, returnAbsolutePaths);

            var chapter = new ChapterCourseModel
            {
                Title = CleanTitle(dir.Name),
                Path = GetPathForReturn(dirFull, rootFullPath, returnAbsolutePaths)
            };

            IEnumerable<FileInfo> files;
            try
            {
                files = dir.EnumerateFiles();
            }
            catch
            {
                files = Array.Empty<FileInfo>();
            }

            foreach (var f in files)
            {
                string ext;
                try
                {
                    ext = Path.GetExtension(f.Name).ToLowerInvariant();
                }
                catch
                {
                    continue;
                }

                if (videoExtensions.Contains(ext))
                {
                    var fullFile = SafeGetFullPath(f);
                    if (fullFile == null) continue;
                    var video = new VideoModel
                    {
                        Title = CleanTitle(Path.GetFileNameWithoutExtension(f.Name)),
                        Path = GetPathForReturn(fullFile, rootFullPath, returnAbsolutePaths),
                    };
                    video.RelativeIndex = chapter.Videos.Count;
                    video.AbsoluteIndex = Interlocked.Increment(ref _absoluteVideoIndex) - 1;
                    chapter.Videos.Add(video);
                }
            }

            IEnumerable<DirectoryInfo> subdirs;
            try
            {
                subdirs = dir.EnumerateDirectories();
            }
            catch
            {
                subdirs = Array.Empty<DirectoryInfo>();
            }

            foreach (var sd in subdirs)
            {
                if (IsReparsePoint(sd)) continue;
                ChapterCourseModel child;
                try
                {
                    child = BuildChapter(sd, rootFullPath, videoExtensions, returnAbsolutePaths, depth + 1, maxDepth);
                }
                catch
                {
                    continue;
                }
                if (child != null)
                {
                    child.RelativeIndex = chapter.Chapters.Count;
                    chapter.Chapters.Add(child);
                }
            }

            return chapter;
        }

        private static ChapterCourseModel CreateEmptyChapter(DirectoryInfo dir, string fullPath, bool returnAbsolutePaths)
        {
            return new ChapterCourseModel
            {
                Id = 0,
                Title = dir.Name,
                Path = returnAbsolutePaths ? fullPath : dir.FullName
            };
        }

        private static bool IsReparsePoint(DirectoryInfo d)
        {
            try
            {
                return d.Attributes.HasFlag(FileAttributes.ReparsePoint);
            }
            catch
            {
                return true;
            }
        }

        private static string? SafeGetFullPath(FileSystemInfo fsi)
        {
            try
            {
                return fsi.FullName != null ? Path.GetFullPath(fsi.FullName) : null;
            }
            catch
            {
                return null;
            }
        }

        private static string GetPathForReturn(string fullPath, string rootFullPath, bool absolute)
        {
            if (absolute) return fullPath;
            try
            {
                var rel = Path.GetRelativePath(rootFullPath, fullPath);
                return string.IsNullOrEmpty(rel) ? "." : rel;
            }
            catch
            {
                return fullPath;
            }
        }

        private static IEnumerable<string> DefaultVideoExtensions()
        {
            return new[] { ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".flv", ".webm" };
        }

        private void ExtractCourseTokens(string courseLabel)
        {
            _courseBracketTags.Clear();

            if (string.IsNullOrWhiteSpace(courseLabel))
            {
                _courseCoreName = string.Empty;
                return;
            }

            var bracketMatches = Regex.Matches(courseLabel, @"\[[^\]]+\]");
            foreach (Match m in bracketMatches)
                if (!string.IsNullOrWhiteSpace(m.Value))
                    _courseBracketTags.Add(m.Value);

            _courseCoreName = Regex.Replace(courseLabel, @"\[[^\]]+\]", "");
            _courseCoreName = CollapseSpaces(_courseCoreName);
        }

        private string CleanCourseLabel(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            var s = raw;

            foreach (var tag in _courseBracketTags)
                s = s.Replace(tag, "", StringComparison.OrdinalIgnoreCase);

            s = CollapseSpaces(s);
            s = s.Trim('-', ' ', '.', '\uFEFF', '\u200B');

            return s;
        }

        private string CleanTitle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var s = raw.Trim();

            foreach (var tag in _courseBracketTags)
            {
                if (string.IsNullOrEmpty(tag)) continue;
                s = s.Replace(tag, "", StringComparison.OrdinalIgnoreCase);
            }

            if (!string.IsNullOrWhiteSpace(_courseCoreName))
            {
                var core = _courseCoreName.Trim();
                if (!string.IsNullOrEmpty(core))
                {
                    s = Regex.Replace(s, Regex.Escape(core), "", RegexOptions.IgnoreCase);
                }
            }

            s = Regex.Replace(s, @"^\s*[-–—:.]+\s*", "");
            s = CollapseSpaces(s);
            s = s.Trim('-', ' ', '.', '\uFEFF', '\u200B');
            return s;
        }

        private static string CollapseSpaces(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return Regex.Replace(s, @"\s+", " ").Trim();
        }
    }
}