
using System.Collections.Concurrent;
using System.IO;
using Microsoft.Extensions.Logging;
using Smotrel.Data.Entities;
using Smotrel.Services.Interfaces;

namespace Smotrel.Services.Implementations
{
    public class PlaybackService : IPlaybackService, IDisposable
    {
        private readonly ICourseRepository _repo;
        private readonly ILogger<PlaybackService>? _logger;
        private readonly TimeSpan _debounceDelay = TimeSpan.FromSeconds(2);

        // pending resume markers per course root
        private readonly ConcurrentDictionary<string, PendingState> _pending = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed = false;

        public PlaybackService(ICourseRepository repo, ILogger<PlaybackService>? logger = null)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _logger = logger;
        }

        public async Task NotifyPositionAsync(string filePath, long seconds)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;

            var courseRoot = FindCourseRootForFile(filePath);
            if (courseRoot == null) return;

            var course = await _repo.LoadAsync(courseRoot);
            if (course == null) return;

            var part = course.Chapters.SelectMany(c => c.Parts)
                                      .FirstOrDefault(p => string.Equals(NormalizePath(p.Path), NormalizePath(filePath), StringComparison.OrdinalIgnoreCase));
            if (part == null) return;

            EnqueueResume(courseRoot, part.Id, seconds);
            ScheduleFlush(courseRoot);
        }

        public async Task SavePositionByPartIdAsync(string courseRootPath, Guid partId, long seconds)
        {
            if (string.IsNullOrWhiteSpace(courseRootPath)) throw new ArgumentNullException(nameof(courseRootPath));

            var course = await _repo.LoadAsync(courseRootPath);
            if (course == null) return;

            // set course-level resume marker
            course.LastPlayedPartId = partId;
            course.LastPlayedPositionSeconds = seconds;

            // optionally update per-part LastPositionSeconds for compatibility (not required)
            var part = course.Chapters.SelectMany(ch => ch.Parts).FirstOrDefault(p => p.Id == partId);
            if (part != null)
            {
                part.LastPositionSeconds = seconds;
                if (part.DurationSeconds.HasValue && seconds >= Math.Round(part.DurationSeconds.Value * 0.95))
                    part.Watched = true;
            }

            RecalculateMetaWatchedSeconds(course);

            await SafeSaveAsync(courseRootPath, course);
        }

        public async Task MarkWatchedByPartIdAsync(string courseRootPath, Guid partId)
        {
            if (string.IsNullOrWhiteSpace(courseRootPath)) throw new ArgumentNullException(nameof(courseRootPath));

            var course = await _repo.LoadAsync(courseRootPath);
            if (course == null) return;

            var part = course.Chapters.SelectMany(ch => ch.Parts).FirstOrDefault(p => p.Id == partId);
            if (part == null) return;

            part.Watched = true;
            if (part.DurationSeconds.HasValue)
                part.LastPositionSeconds = Math.Max(part.LastPositionSeconds, part.DurationSeconds.Value);

            RecalculateMetaWatchedSeconds(course);

            // If this part was the resume marker and was fully watched, clear the resume marker
            if (course.LastPlayedPartId.HasValue && course.LastPlayedPartId.Value == partId)
            {
                course.LastPlayedPartId = null;
                course.LastPlayedPositionSeconds = 0;
            }

            await SafeSaveAsync(courseRootPath, course);
        }

        public async Task ClearResumeAsync(string courseRootPath)
        {
            if (string.IsNullOrWhiteSpace(courseRootPath)) throw new ArgumentNullException(nameof(courseRootPath));

            var course = await _repo.LoadAsync(courseRootPath);
            if (course == null) return;

            course.LastPlayedPartId = null;
            course.LastPlayedPositionSeconds = 0;

            await SafeSaveAsync(courseRootPath, course);
        }

        public async Task FlushAsync()
        {
            var keys = _pending.Keys.ToList();
            foreach (var key in keys)
            {
                try
                {
                    await FlushCoursePendingAsync(key);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error flushing pending resume for {root}", key);
                }
            }
        }

        private void EnqueueResume(string courseRoot, Guid partId, long seconds)
        {
            var state = _pending.GetOrAdd(courseRoot, _ => new PendingState());
            lock (state.Sync)
            {
                state.ResumeMarker = (partId, seconds);
            }
        }

        private void ScheduleFlush(string courseRoot)
        {
            var state = _pending.GetOrAdd(courseRoot, _ => new PendingState());
            lock (state.Sync)
            {
                state.CancelScheduled();
                state.Cts = new CancellationTokenSource();
                var token = state.Cts.Token;
                state.ScheduledTask = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(_debounceDelay, token);
                        await FlushCoursePendingAsync(courseRoot);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Scheduled flush exception for {root}", courseRoot);
                    }
                }, token);
            }
        }

        private async Task FlushCoursePendingAsync(string courseRoot)
        {
            if (!_pending.TryGetValue(courseRoot, out var state)) return;

            (Guid partId, long pos)? marker;
            lock (state.Sync)
            {
                marker = state.ResumeMarker;
                state.ResumeMarker = null;
                state.CancelScheduled();
            }

            if (!marker.HasValue) return;

            var course = await _repo.LoadAsync(courseRoot);
            if (course == null) return;

            course.LastPlayedPartId = marker.Value.partId;
            course.LastPlayedPositionSeconds = marker.Value.pos;

            // optionally update part's LastPositionSeconds for compatibility
            var part = course.Chapters.SelectMany(ch => ch.Parts).FirstOrDefault(p => p.Id == marker.Value.partId);
            if (part != null)
                part.LastPositionSeconds = marker.Value.pos;

            RecalculateMetaWatchedSeconds(course);

            await SafeSaveAsync(courseRoot, course);
        }

        private void RecalculateMetaWatchedSeconds(CourseEntity course)
        {
            long watchedSeconds = 0;
            foreach (var p in course.Chapters.SelectMany(ch => ch.Parts))
            {
                if (p.DurationSeconds.HasValue)
                    watchedSeconds += Math.Min(p.LastPositionSeconds, p.DurationSeconds.Value);
                else
                    watchedSeconds += p.LastPositionSeconds;
            }
            course.WatchedSeconds = watchedSeconds;
        }

        private async Task SafeSaveAsync(string courseRootPath, CourseEntity course)
        {
            try
            {
                await _repo.SaveAsync(course);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save course {root}", courseRootPath);
            }
        }

        private string? FindCourseRootForFile(string filePath)
        {
            try
            {
                var fi = new FileInfo(filePath);
                var dir = fi.Exists ? fi.Directory : new DirectoryInfo(filePath).Parent;
                var current = dir;
                while (current != null)
                {
                    var candidate = Path.Combine(current.FullName, ".smotrel");
                    if (Directory.Exists(candidate))
                        return current.FullName;
                    current = current.Parent;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizePath(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return string.Empty;
            try
            {
                return Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return p.Trim();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var kv in _pending) kv.Value.CancelScheduled();
            _pending.Clear();
        }

        // pending state
        private class PendingState
        {
            public (Guid partId, long pos)? ResumeMarker;
            public CancellationTokenSource? Cts;
            public Task? ScheduledTask;
            public readonly object Sync = new();
            public void CancelScheduled()
            {
                try
                {
                    if (Cts != null && !Cts.IsCancellationRequested)
                    {
                        Cts.Cancel();
                        Cts.Dispose();
                    }
                }
                catch { }
                Cts = null;
                ScheduledTask = null;
            }
        }
    }
}
