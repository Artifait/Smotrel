using System;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using Smotrel.Messages;
using Smotrel.Services.Interfaces;
using Smotrel.Views;

namespace Smotrel.Controllers
{
    public class PipController : IDisposable
    {
        private readonly IPlaybackService _playbackService;
        private PiPWindow? _pipWindow;
        private PlayerController? _pipPlayerController;
        private PlayerController? _mainPlayerController;
        private string? _currentFilePath;
        private Guid? _currentPartId;
        private string? _currentCourseRoot;
        private bool _isRestoring = false;

        public PipController(IPlaybackService playbackService)
        {
            _playbackService = playbackService ?? throw new ArgumentNullException(nameof(playbackService));
        }

        public async Task OpenPiPAsync(PlayerController mainPlayer, string filePath, TimeSpan position, double speed, double volume, bool isPlaying, string? courseRoot = null, Guid? partId = null)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath));
            if (mainPlayer == null) throw new ArgumentNullException(nameof(mainPlayer));

            if (_pipWindow != null)
            {
                try { _pipWindow.Topmost = true; _pipWindow.Activate(); } catch { }
                return;
            }

            _mainPlayerController = mainPlayer;
            _currentFilePath = filePath;
            _currentCourseRoot = courseRoot;
            _currentPartId = partId;

            // Pause main
            try { _mainPlayerController.Pause(); } catch { }

            _pipWindow = new PiPWindow();
            _pipPlayerController = new PlayerController();

            try { _pipWindow.PiPPlayer.Source = new Uri(filePath); } catch { }

            _pipPlayerController.Initialize(_pipWindow.PiPPlayer);

            // On opened -> seek and render frame/resume if necessary
            _pipPlayerController.MediaOpened += (s, e) =>
            {
                try
                {
                    if (position > TimeSpan.Zero)
                        _pipPlayerController.Seek(position);
                }
                catch { }
            };

            _pipPlayerController.PositionChanged += async (s, pos) =>
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(_currentFilePath))
                    {
                        var sec = (long)Math.Floor(pos.TotalSeconds);
                        _ = _playbackService.NotifyPositionAsync(_currentFilePath, sec);
                        if (!string.IsNullOrWhiteSpace(_currentCourseRoot) && _currentPartId.HasValue)
                        {
                            _ = _playbackService.SavePositionByPartIdAsync(_currentCourseRoot, _currentPartId.Value, sec);
                        }
                    }
                }
                catch { }
            };

            _pipWindow.AttachController(_pipPlayerController);

            _pipWindow.RestoreRequested += async (_, __) => await ClosePiPAndRestoreToMainAsync();
            _pipWindow.ClosedByUser += async (_, __) => await ClosePiPAndRestoreToMainAsync();
            _pipWindow.Closed += async (_, __) => await ClosePiPAndRestoreToMainAsync();

            WeakReferenceMessenger.Default.Send(new PiPStateChangedMessage(true));
            _pipWindow.Show();

            // apply settings
            _pipPlayerController.Volume = volume;
            _pipPlayerController.Speed = speed;

            // start playback in pip if original was playing
            try
            {
                if (isPlaying)
                {
                    _pipPlayerController.Play();
                }
                else
                {
                    // If original was paused — we still want to render a frame:
                    // play briefly and pause after small delay so MediaElement paints a frame.
                    _pipPlayerController.Play();
                    await Task.Delay(120);
                    _pipPlayerController.Pause();
                }
            }
            catch { }
        }

        public async Task ClosePiPAndRestoreToMainAsync()
        {
            WeakReferenceMessenger.Default.Send(new PiPStateChangedMessage(false));

            if (_isRestoring) return;
            _isRestoring = true;

            try
            {
                if (_pipPlayerController == null || _mainPlayerController == null)
                {
                    try { _pipWindow?.Close(); } catch { }
                    Cleanup();
                    return;
                }

                var pos = _pipPlayerController.Position;
                var speed = _pipPlayerController.Speed;
                var vol = _pipPlayerController.Volume;
                bool pipWasPlaying = _pipPlayerController.IsPlaying;

                // --- NEW: отправляем состояние плеера через messenger, чтобы MainWindow мог подхватить позицию
                try
                {
                    WeakReferenceMessenger.Default.Send(new PlaybackStateMessage(
                        _currentFilePath ?? string.Empty,
                        _currentPartId,
                        (long)Math.Floor(pos.TotalSeconds),
                        speed,
                        vol,
                        pipWasPlaying
                    ));
                }
                catch { /* swallow */ }

                try { _pipPlayerController.Pause(); } catch { }
                try { _pipPlayerController.Dispose(); } catch { }
                _pipPlayerController = null;

                try { _pipWindow?.Close(); } catch { }
                _pipWindow = null;

                try
                {
                    if (_mainPlayerController != null)
                    {
                        // main may already handle the PlaybackStateMessage (seek/play). But keep a best-effort sync:
                        _mainPlayerController.Seek(pos);
                        _mainPlayerController.Speed = speed;
                        _mainPlayerController.Volume = vol;
                        if (pipWasPlaying) _mainPlayerController.Play();
                        else _mainPlayerController.Pause();
                    }
                }
                catch { }

                try
                {
                    if (!string.IsNullOrWhiteSpace(_currentFilePath))
                    {
                        var sec = (long)Math.Floor(pos.TotalSeconds);
                        _ = _playbackService.NotifyPositionAsync(_currentFilePath, sec);
                        if (!string.IsNullOrWhiteSpace(_currentCourseRoot) && _currentPartId.HasValue)
                        {
                            _ = _playbackService.SavePositionByPartIdAsync(_currentCourseRoot, _currentPartId.Value, sec);
                        }
                    }
                }
                catch { }
            }
            finally
            {
                Cleanup();
                _isRestoring = false;
            }
        }


        private void Cleanup()
        {
            _pipWindow = null;
            _pipPlayerController = null;
            _mainPlayerController = null;
            _currentFilePath = null;
            _currentPartId = null;
            _currentCourseRoot = null;
        }

        public void Dispose()
        {
            try { _pipPlayerController?.Dispose(); } catch { }
            try { _pipWindow?.Close(); } catch { }
        }
    }
}
