
using System;
using System.Threading;
using System.Threading.Tasks;
using Smotrel.Services.Interfaces;

namespace Smotrel.Controllers
{
    public class PlaybackPositionManager : IDisposable
    {
        private readonly IPlaybackService _playbackService;
        private readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(1200);

        private CancellationTokenSource? _cts;
        private int _lastNotifiedSecond = -1;
        private string? _currentFilePath;
        private string? _currentCourseRoot;
        private Guid? _currentPartId;
        private readonly object _lock = new();

        public PlaybackPositionManager(IPlaybackService playbackService)
        {
            _playbackService = playbackService ?? throw new ArgumentNullException(nameof(playbackService));
        }

        public void SetContext(string? courseRootPath, Guid? partId, string? filePath)
        {
            lock (_lock)
            {
                _currentCourseRoot = courseRootPath;
                _currentPartId = partId;
                _currentFilePath = filePath;
                _lastNotifiedSecond = -1;
            }
        }

        public void OnPositionChanged(TimeSpan position)
        {
            var sec = (int)Math.Floor(position.TotalSeconds);

            lock (_lock)
            {
                if (_currentFilePath == null) return;
                if (sec == _lastNotifiedSecond) return;
                _lastNotifiedSecond = sec;

                // Debounce previous
                _cts?.Cancel();
                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(_debounce, token);
                        if (token.IsCancellationRequested) return;
                        await _playbackService.NotifyPositionAsync(_currentFilePath, sec);
                    }
                    catch (OperationCanceledException) { }
                    catch { /* ignore*/ }
                }, token);
            }
        }

        public async Task SavePositionImmediateAsync(long seconds)
        {
            Guid? pid;
            string? root;
            lock (_lock) { pid = _currentPartId; root = _currentCourseRoot; }

            if (pid.HasValue && !string.IsNullOrWhiteSpace(root))
            {
                try
                {
                    await _playbackService.SavePositionByPartIdAsync(root!, pid.Value, seconds);
                }
                catch { }
            }
        }

        public async Task FlushAsync()
        {
            try
            {
                _cts?.Cancel();
                await _playbackService.FlushAsync();
            }
            catch { }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}
