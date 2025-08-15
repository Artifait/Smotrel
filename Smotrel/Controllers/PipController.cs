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

        /// <summary>
        /// Open PiP by transferring playback from main controller.
        /// mainPlayer: the existing main PlayerController (will be paused)
        /// filePath: path to file to play
        /// position: current position to resume at
        /// </summary>
        public async Task OpenPiPAsync(PlayerController mainPlayer, string filePath, TimeSpan position, double speed, double volume, bool isPlaying, string? courseRoot = null, Guid? partId = null)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath));
            if (mainPlayer == null) throw new ArgumentNullException(nameof(mainPlayer));

            // If already open — bring to front
            if (_pipWindow != null)
            {
                try
                {
                    _pipWindow.Topmost = true;
                    _pipWindow.Activate();
                }
                catch { }
                return;
            }

            _mainPlayerController = mainPlayer;
            _currentFilePath = filePath;
            _currentCourseRoot = courseRoot;
            _currentPartId = partId;

            // Pause main playback
            try { _mainPlayerController.Pause(); } catch { }

            // Create PiP window
            _pipWindow = new PiPWindow();
            // Optional: remember previous PiP location from settings; left to user

            // Create pip player controller and attach
            _pipPlayerController = new PlayerController();

            // set source on media element BEFORE initialize to have MediaElement try to open
            try
            {
                _pipWindow.PiPPlayer.Source = new Uri(filePath);
            }
            catch { }

            _pipPlayerController.Initialize(_pipWindow.PiPPlayer);

            // Hook pip events
            _pipPlayerController.MediaOpened += (s, e) =>
            {
                // Seek to requested position when media opened
                try
                {
                    if (position > TimeSpan.Zero)
                        _pipPlayerController.Seek(position);
                }
                catch { }
            };

            // position changed — persist / notify (best-effort)
            _pipPlayerController.PositionChanged += async (s, pos) =>
            {
                try
                {
                    // notify playback service (non-blocking)
                    if (!string.IsNullOrWhiteSpace(_currentFilePath))
                    {
                        var sec = (long)Math.Floor(pos.TotalSeconds);
                        _ = _playbackService.NotifyPositionAsync(_currentFilePath, sec);
                        // If you want to use SavePositionByPartIdAsync instead (to update CourseEntity.LastPlayedPosition), uncomment:
                        if (!string.IsNullOrWhiteSpace(_currentCourseRoot) && _currentPartId.HasValue)
                        {
                            _ = _playbackService.SavePositionByPartIdAsync(_currentCourseRoot, _currentPartId.Value, (long)Math.Floor(pos.TotalSeconds));
                        }
                    }
                }
                catch { }
            };

            // show window and attach controller so UI is wired
            _pipWindow.AttachController(_pipPlayerController);

            // Wire restore/close events
            _pipWindow.RestoreRequested += async (_, __) => await ClosePiPAndRestoreToMainAsync();
            _pipWindow.ClosedByUser += async (_, __) => await ClosePiPAndRestoreToMainAsync();
            _pipWindow.Closed += async (_, __) => await ClosePiPAndRestoreToMainAsync();

            // Show PiP and start playback (play only if original was playing)
            _pipWindow.Show();

            _pipPlayerController.Volume = volume;
            _pipPlayerController.Speed = speed;

            // If main was playing -> start pip playback
            try
            {
                if (isPlaying)
                {
                    _pipPlayerController.Play();
                }
            }
            catch { }
        }

        /// <summary>
        /// Restore playback to main controller: take current pip position and state and apply to main player.
        /// </summary>
        public async Task ClosePiPAndRestoreToMainAsync()
        {
            if (_isRestoring) return;
            _isRestoring = true;

            try
            {
                if (_pipPlayerController == null || _mainPlayerController == null)
                {
                    // just close window if any
                    try { _pipWindow?.Close(); } catch { }
                    Cleanup();
                    return;
                }

                // capture pip state
                var pos = _pipPlayerController.Position;
                var speed = _pipPlayerController.Speed;
                var vol = _pipPlayerController.Volume;
                bool wasPlaying = false;
                try
                {
                    // we don't have IsPlaying property; rely on MediaEnded or assume if PositionChanged recently, treat as playing
                    // Best-effort: if controller had been playing right before restore, try to Play afterwards.
                    wasPlaying = true;
                }
                catch { wasPlaying = true; }

                // stop and dispose pip
                try { _pipPlayerController.Pause(); } catch { }
                try { _pipPlayerController.Dispose(); } catch { }
                _pipPlayerController = null;

                // close window (if not already)
                try { _pipWindow?.Close(); } catch { }
                _pipWindow = null;

                // restore to main player
                try
                {
                    if (_mainPlayerController != null)
                    {
                        _mainPlayerController.Seek(pos);
                        _mainPlayerController.Speed = speed;
                        _mainPlayerController.Volume = vol;
                        if (wasPlaying) _mainPlayerController.Play();
                    }
                }
                catch { }

                // final notify to playback service (best-effort)
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
