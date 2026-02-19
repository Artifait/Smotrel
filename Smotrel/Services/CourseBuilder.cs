using System.IO;
using Smotrel.Models;

namespace Smotrel.Services
{
    public class CourseBuilder
    {
        private int _courseId;
        private int _chapterId;
        private int _videoId;
        private readonly HashSet<string> _visitedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public CourseModel BuildFromDirectory(string rootPath, IEnumerable<string>? videoExtensions = null, bool returnAbsolutePaths = false, int maxDepth = 20)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
                throw new ArgumentException("rootPath is required", nameof(rootPath));

            var fullRoot = Path.GetFullPath(rootPath);
            if (!Directory.Exists(fullRoot))
                throw new DirectoryNotFoundException(fullRoot);

            var extensions = (videoExtensions ?? DefaultVideoExtensions()).Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant()).ToHashSet();

            var rootInfo = new DirectoryInfo(fullRoot);
            var course = new CourseModel
            {
                Id = Interlocked.Increment(ref _courseId),
                Label = rootInfo.Name
            };

            course.MainChapter = BuildChapter(rootInfo, fullRoot, extensions, returnAbsolutePaths, 0, maxDepth);
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
                Id = Interlocked.Increment(ref _chapterId),
                Title = dir.Name,
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
                    chapter.Videos.Add(new VideoModel
                    {
                        Id = Interlocked.Increment(ref _videoId),
                        Title = Path.GetFileNameWithoutExtension(f.Name),
                        Path = GetPathForReturn(fullFile, rootFullPath, returnAbsolutePaths)
                    });
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
                    chapter.Chapters.Add(child);
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
            return [".mp4", ".mkv", ".mov", ".avi", ".wmv", ".flv", ".webm"];
        }
    }
}