using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Smotrel.Data.Entities;

namespace Smotrel.Services
{
    /// <summary>
    /// PlaybackService:
    /// - буферизует обновления позиции по частям (debounce)
    /// - при Flush сохраняет все pending изменения в репозиторий
    /// - можно сразу сохранять по partId / помечать watched
    /// </summary>
    public class PlaybackService : IPlaybackService, IDisposable
    {
        private readonly ICourseRepository _repo;
        private readonly ILogger<PlaybackService>? _logger;
        private readonly TimeSpan _debounceDelay = TimeSpan.FromSeconds(2);

        // pending per courseRoot -> state
        private readonly ConcurrentDictionary<string, PendingState> _pending = new(StringComparer.OrdinalIgnoreCase);

        // for disposing
        private bool _disposed;

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

            // try to find part id quickly by loading course (could be optimized by caching)
            var course = await _repo.LoadAsync(courseRoot);
            if (course == null) return;

            var part = course.Chapters.SelectMany(ch => ch.Parts)
                                      .FirstOrDefault(p => string.Equals(NormalizePath(p.Path), NormalizePath(filePath), StringComparison.OrdinalIgnoreCase));
            if (part == null) return;

            EnqueuePendingPosition(courseRoot, part.Id, seconds);

            // schedule a delayed flush for this course
            ScheduleFlush(courseRoot);
        }

        public async Task SavePositionByPartIdAsync(string courseRootPath, Guid partId, long seconds)
        {
            if (string.IsNullOrWhiteSpace(courseRootPath)) throw new ArgumentNullException(nameof(courseRootPath));

            var course = await _repo.LoadAsync(courseRootPath);
            if (course == null) return;

            var part = course.Chapters.SelectMany(ch => ch.Parts).FirstOrDefault(p => p.Id == partId);
            if (part == null) return;

            part.LastPositionSeconds = seconds;
            if (part.DurationSeconds.HasValue && seconds >= Math.Round(part.DurationSeconds.Value * 0.95))
                part.Watched = true;

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
            else
                part.LastPositionSeconds = part.LastPositionSeconds; // leave as is

            RecalculateMetaWatchedSeconds(course);

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
                    _logger?.LogError(ex, "Error flushing pending playback for {root}", key);
                }
            }
        }

        // ---------------- helpers ----------------

        private void EnqueuePendingPosition(string courseRoot, Guid partId, long seconds)
        {
            var state = _pending.GetOrAdd(courseRoot, _ => new PendingState());

            lock (state.Sync)
            {
                state.Positions[partId] = seconds;
                _logger?.LogDebug("Enqueued position {s} for part {p} (course {c})", seconds, partId, courseRoot);
            }
        }

        private void ScheduleFlush(string courseRoot)
        {
            var state = _pending.GetOrAdd(courseRoot, _ => new PendingState());

            lock (state.Sync)
            {
                // cancel previous scheduled task (if any)
                state.CancelScheduled();

                state.Cts = new CancellationTokenSource();
                var token = state.Cts.Token;

                // schedule a task that waits debounce then flushes
                state.ScheduledTask = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(_debounceDelay, token);
                        await FlushCoursePendingAsync(courseRoot);
                    }
                    catch (OperationCanceledException)
                    {
                        // expected on reschedule
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error during scheduled flush for {root}", courseRoot);
                    }
                }, token);
            }
        }

        private async Task FlushCoursePendingAsync(string courseRoot)
        {
            if (!_pending.TryGetValue(courseRoot, out var state)) return;

            KeyValuePair<Guid, long>[] items;
            lock (state.Sync)
            {
                if (state.Positions.Count == 0) return;
                items = state.Positions.ToArray();
                state.Positions.Clear();
                state.CancelScheduled(); // cancel scheduled as we're executing now
            }

            // safe save: load course, apply updates, save
            var course = await _repo.LoadAsync(courseRoot);
            if (course == null) return;

            var parts = course.Chapters.SelectMany(ch => ch.Parts).ToDictionary(p => p.Id, p => p);
            bool changed = false;

            foreach (var kv in items)
            {
                if (parts.TryGetValue(kv.Key, out var part))
                {
                    var seconds = kv.Value;
                    if (seconds != part.LastPositionSeconds)
                    {
                        part.LastPositionSeconds = seconds;
                        changed = true;
                    }

                    if (part.DurationSeconds.HasValue && seconds >= Math.Round(part.DurationSeconds.Value * 0.95))
                        part.Watched = true;
                }
                else
                {
                    _logger?.LogWarning("Pending part {p} not found in course {c} during flush", kv.Key, courseRoot);
                }
            }

            if (changed)
            {
                RecalculateMetaWatchedSeconds(course);
                await SafeSaveAsync(courseRoot, course);
            }
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
                // Optional: make backup before saving if you want; repo may do it.
                await _repo.SaveAsync(course);
                _logger?.LogDebug("Saved playback positions for course {root}", courseRootPath);
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
                var dir = fi.Exists ? fi.Directory : new FileInfo(filePath).Directory;
                if (dir == null) return null;

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

            foreach (var kv in _pending)
                kv.Value.CancelScheduled();

            _pending.Clear();
        }

        // ---------------- internal types ----------------
        private class PendingState
        {
            public readonly Dictionary<Guid, long> Positions = new();
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
                catch { /*ignore*/ }
                Cts = null;
                ScheduledTask = null;
            }
        }
    }
}
