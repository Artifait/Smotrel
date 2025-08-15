using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Messaging;
using Smotrel.Controllers;
using Smotrel.Messages;
using Smotrel.ViewModels;
using Smotrel.Services.Interfaces;
using System.Windows.Threading;

using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Smotrel.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;
        private readonly IPlaybackService _playbackService;
        private readonly ICourseRepository _repository;

        private readonly PlayerController _playerController;
        private readonly PlaybackPositionManager _positionManager;
        private readonly PlaylistController _playlistController;
        private readonly OverlayController _overlayController;
        private readonly PipController _pipController;

        private bool _isPiPActive = false;
        private bool _isPlaying = true;
        private long? _pendingResumePositionSeconds = null;
        private Guid? _pendingResumePartId = null;
        private string? _pendingResumeCourseRoot = null;

        // pending state from PiP (applied when PiP closed / main is ready)
        private string? _pendingPiPFilePath = null;
        private long? _pendingPiPPositionSeconds = null;
        private double? _pendingPiPSpeed = null;
        private double? _pendingPiPVolume = null;
        private bool? _pendingPiPIsPlaying = null;
        private Guid? _pendingPiPPartId = null;

        private readonly DispatcherTimer _resumeAutoHideTimer;
        private readonly TimeSpan _resumeAutoHideInterval = TimeSpan.FromSeconds(8);
        private DateTime _resumeAutoHideEnd = DateTime.MinValue;

        public MainWindow(MainViewModel vm, PipController pipController, IPlaybackService playbackService, ICourseRepository repository)
        {
            InitializeComponent();

            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            _playbackService = playbackService ?? throw new ArgumentNullException(nameof(playbackService));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _pipController = pipController ?? throw new ArgumentNullException(nameof(pipController));

            DataContext = _vm;
            _vm.PropertyChanged += Vm_PropertyChanged;

            // controllers
            _playerController = new PlayerController();
            _playerController.Initialize(Player);
            _playerController.MediaOpened += Player_MediaOpened;
            _playerController.MediaEnded += Player_MediaEnded;
            _playerController.PositionChanged += Player_PositionChanged;
            _playerController.PlayingStateChanged += Player_PlayingStateChanged;

            _positionManager = new PlaybackPositionManager(_playbackService);
            _playlistController = new PlaylistController(_vm);
            _overlayController = new OverlayController(ControlOverlay, centerIcon, TimeSpan.FromSeconds(3));

            _resumeAutoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _resumeAutoHideTimer.Tick += ResumeAutoHideTimer_Tick;

            // messaging
            WeakReferenceMessenger.Default.Register<VideoControlMessage>(this, HandleVideoControl);
            WeakReferenceMessenger.Default.Register<ResumeAvailableMessage>(this, (r, msg) => OnResumeAvailable(msg));
            WeakReferenceMessenger.Default.Register<PlaybackSpeedChangedMessage>(this, (r, msg) => _playerController.Speed = msg.Speed);
            WeakReferenceMessenger.Default.Register<PiPStateChangedMessage>(this, (r, msg) =>
            {
                Dispatcher.Invoke(() => OnPiPStateChanged(msg.IsActive));
            });
            WeakReferenceMessenger.Default.Register<PlaybackStateMessage>(this, (r, msg) =>
            {
                Dispatcher.Invoke(() => OnPlaybackStateMessage(msg));
            });

            // initial UI sync
            volumeSlider.Value = _playerController.Volume;
            volumeSlider.ValueChanged += volumeSlider_ValueChanged;
            videoSlider.ValueChanged += videoSlider_ValueChanged;
            _playerController.Volume = volumeSlider.Value;

            UpdatePlayButton();

            this.Closing += MainWindow_Closing;
        }

        private void OnPlaybackStateMessage(PlaybackStateMessage msg)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(msg.FilePath)) return;

                var currentPath = _vm?.SelectedVideo?.FilePath;
                // Если PiP активен — не применять напрямую, а сохранить как pending
                if (_isPiPActive)
                {
                    _pendingPiPFilePath = msg.FilePath;
                    _pendingPiPPositionSeconds = msg.PositionSeconds;
                    _pendingPiPSpeed = msg.Speed;
                    _pendingPiPVolume = msg.Volume;
                    _pendingPiPIsPlaying = msg.IsPlaying;
                    _pendingPiPPartId = msg.PartId;
                    return;
                }

                // Если текущий файл совпадает — применяем сразу
                if (!string.IsNullOrWhiteSpace(currentPath) &&
                    string.Equals(currentPath, msg.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        _playerController.Seek(TimeSpan.FromSeconds(msg.PositionSeconds));
                        _playerController.Speed = msg.Speed;
                        _playerController.Volume = msg.Volume;

                        if (msg.IsPlaying)
                        {
                            _playerController.Play();
                            _isPlaying = true;
                        }
                        else
                        {
                            _playerController.Pause();
                            _isPlaying = false;
                        }
                        UpdatePlayButton();
                    }
                    catch { /* ignore */ }

                    ClearPendingPiPState();
                }
                else
                {
                    // не совпадает — сохраняем pending
                    _pendingPiPFilePath = msg.FilePath;
                    _pendingPiPPositionSeconds = msg.PositionSeconds;
                    _pendingPiPSpeed = msg.Speed;
                    _pendingPiPVolume = msg.Volume;
                    _pendingPiPIsPlaying = msg.IsPlaying;
                    _pendingPiPPartId = msg.PartId;
                }
            }
            catch { /* swallow */ }
        }

        private void ClearPendingPiPState()
        {
            _pendingPiPFilePath = null;
            _pendingPiPPositionSeconds = null;
            _pendingPiPSpeed = null;
            _pendingPiPVolume = null;
            _pendingPiPIsPlaying = null;
            _pendingPiPPartId = null;
        }

        private void Player_PlayingStateChanged(object? sender, bool isPlaying)
        {
            Dispatcher.Invoke(() =>
            {
                _isPlaying = isPlaying;
                UpdatePlayButton();
            });
        }

        private void Vm_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_vm.CurrentVideoPath) && !string.IsNullOrEmpty(_vm.CurrentVideoPath))
            {
                try
                {
                    Player.Source = new Uri(_vm.CurrentVideoPath);
                    _playerController.Speed = 1.0;

                    // Если PiP активен — не стартуем воспроизведение в main
                    if (!_isPiPActive)
                    {
                        _playerController.Play();
                        _isPlaying = true;
                    }
                    else
                    {
                        _playerController.Pause();
                        _isPlaying = false;
                    }

                    UpdatePlayButton();
                    _overlayController.ShowCenterFeedback(_isPlaying);
                }
                catch { }

                UpdateCurrentPartContext();

                // попытка применить pending, если есть (при условии, что PiP не активен)
                TryApplyPendingPiPStateForCurrent();
            }

            if (e.PropertyName == nameof(_vm.SelectedVideo))
            {
                UpdateCurrentPartContext();
                _overlayController.ShowOverlay(isPlaying: _isPlaying);
                UpdateTimeText();

                TryApplyPendingPiPStateForCurrent();
            }
        }

        private void UpdateCurrentPartContext()
        {
            _positionManager.SetContext(_vm.CourseRootPath,
                string.IsNullOrWhiteSpace(_vm.SelectedVideo?.PartId) ? (Guid?)null : Guid.Parse(_vm.SelectedVideo.PartId),
                _vm.SelectedVideo?.FilePath);
        }

        private void Player_MediaOpened(object? sender, EventArgs e)
        {
            try
            {
                if (Player.NaturalDuration.HasTimeSpan)
                {
                    videoSlider.Minimum = 0;
                    videoSlider.Maximum = Player.NaturalDuration.TimeSpan.TotalSeconds;
                }
                else if (_vm.SelectedVideo?.Duration > 0)
                {
                    videoSlider.Minimum = 0;
                    videoSlider.Maximum = (long)_vm.SelectedVideo.Duration;
                }

                AdjustOverlaySize();
                UpdateTimeText();
            }
            catch { }

            // media opened — безопасно применять pending
            TryApplyPendingPiPStateForCurrent();
        }

        private async void Player_MediaEnded(object? sender, EventArgs e)
        {
            try
            {
                // 1) Пометка в UI — делаем это сразу, чтобы пользователь видел обновление
                try
                {
                    if (_vm.SelectedVideo != null)
                    {
                        // Предположим, SelectedVideo имеет свойство Watched (bool) и оповещает UI
                        _vm.SelectedVideo.Watched = true;

                        // Если у вас есть связанная модель (VideoItem) — обновим и её (опционально)
                        // if (_vm.SelectedVideo.Model != null) _vm.SelectedVideo.Model.Watched = true;
                    }
                }
                catch { /* не критично: если viewmodel не поддерживает Watched — пропустим */ }

                // 2) Серверное / репозитарное уведомление (fire-and-forget допустимо, но ждём, чтобы иметь шанс логировать ошибки)
                if (!string.IsNullOrWhiteSpace(_vm.CourseRootPath) && _vm.SelectedVideo != null
                    && Guid.TryParse(_vm.SelectedVideo.PartId, out var pid))
                {
                    try
                    {
                        // не ждём результата UI — выполняем асинхронно, но ждём ошибки внутри
                        await _playbackService.MarkWatchedByPartIdAsync(_vm.CourseRootPath, pid);
                    }
                    catch
                    {
                        // можно логировать, но всё равно UI уже обновлён
                    }
                }

                // 3) Делегируем авто-переход плейлист-контроллеру (он изменит SelectedVideo на следующий)
                _playlistController.OnMediaEnded();
            }
            catch
            {
                // в крайнем случае — просто вызываем OnMediaEnded для корректного поведения
                try { _playlistController.OnMediaEnded(); } catch { }
            }
        }


        private void Player_PositionChanged(object? sender, TimeSpan pos)
        {
            Dispatcher.Invoke(() =>
            {
                if (!videoSlider.IsMouseCaptureWithin)
                {
                    videoSlider.Value = pos.TotalSeconds;
                }
                UpdateTimeText();
            });

            _positionManager.OnPositionChanged(pos);
        }

        private void Slider_PreviewMouseDown(object sender, MouseButtonEventArgs e) => _playerController.Pause();
        private async void Slider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            var posSec = (int)Math.Round(videoSlider.Value);
            _playerController.Seek(TimeSpan.FromSeconds(posSec));
            if (_isPlaying) _playerController.Play();

            await _positionManager.SavePositionImmediateAsync(posSec);
        }

        private void videoSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { }

        private void HandleVideoControl(object recipient, VideoControlMessage msg)
        {
            switch (msg.Action)
            {
                case VideoControlAction.Next:
                    // Если PiP активен — не управляем main player напрямую (чтобы не запустить воспроизведение)
                    if (!_isPiPActive)
                    {
                        _playerController.Stop();
                    }
                    _vm.MoveNext();
                    break;

                case VideoControlAction.Previous:
                    if (!_isPiPActive)
                    {
                        _playerController.Stop();
                    }
                    _vm.MovePrevious();
                    break;

                case VideoControlAction.TogglePlayPause:
                    if (_isPlaying)
                    {
                        _playerController.Pause();
                        _isPlaying = false;
                    }
                    else
                    {
                        _playerController.Play();
                        _isPlaying = true;
                    }

                    UpdatePlayButton();
                    _overlayController.ShowCenterFeedback(_isPlaying);
                    break;

                case VideoControlAction.SpeedUp:
                    _playerController.Speed *= 2;
                    break;
                case VideoControlAction.NormalSpeed:
                    _playerController.Speed = 1.0;
                    break;
                case VideoControlAction.ToggleFullscreen:
                    ToggleFullscreen();
                    break;
            }
        }

        private void ToggleFullscreen()
        {
            if (WindowStyle == WindowStyle.None)
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
            }
            else
            {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
            }
        }

        private void Player_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            WeakReferenceMessenger.Default.Send(new VideoControlMessage(VideoControlAction.TogglePlayPause));
            if (e.ClickCount == 2)
            {
                WeakReferenceMessenger.Default.Send(new VideoControlMessage(VideoControlAction.ToggleFullscreen));
            }
        }

        private void ShowOverlay() => _overlayController.ShowOverlay(isPlaying: _isPlaying);
        private void HideOverlay() => _overlayController.HideOverlay();

        private void PlayerContainer_MouseEnter(object sender, MouseEventArgs e) => ShowOverlay();
        private void PlayerContainer_MouseMove(object sender, MouseEventArgs e) => ShowOverlay();
        private void PlayerContainer_MouseLeave(object sender, MouseEventArgs e) => HideOverlay();
        private void PlayerContainer_SizeChanged(object sender, SizeChangedEventArgs e) => AdjustOverlaySize();

        private void AdjustOverlaySize()
        {
            if (!_playerController.HasNaturalVideoSize) return;

            double containerWidth = PlayerContainer.ActualWidth;
            double containerHeight = PlayerContainer.ActualHeight;
            double videoAspect = (double)_playerController.NaturalVideoWidth / _playerController.NaturalVideoHeight;
            double containerAspect = containerWidth / containerHeight;

            double horizontalMargin = 0;
            double verticalMargin = 0;

            if (videoAspect > containerAspect)
            {
                double actualVideoHeight = containerWidth / videoAspect;
                verticalMargin = (containerHeight - actualVideoHeight) / 2;
            }
            else
            {
                double actualVideoWidth = containerHeight * videoAspect;
                horizontalMargin = (containerWidth - actualVideoWidth) / 2;
            }

            ControlOverlay.Margin = new Thickness(horizontalMargin, verticalMargin, horizontalMargin, verticalMargin);
        }

        private void volumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _playerController.Volume = e.NewValue;
        }

        private void UpdatePlayButton()
        {
            if (btnPausePlay != null)
                btnPausePlay.Content = _isPlaying ? "⏸" : "▶";
        }

        private void UpdateTimeText()
        {
            var curSec = (int)Math.Floor(_playerController.Position.TotalSeconds);
            long totalSec = 0;
            if (Player.NaturalDuration.HasTimeSpan)
                totalSec = (long)Math.Round(Player.NaturalDuration.TimeSpan.TotalSeconds);
            else if (_vm.SelectedVideo?.Duration > 0)
                totalSec = (long)_vm.SelectedVideo.Duration;

            timeText.Text = $"{FormatTime(curSec)} / {FormatTime(totalSec)}";
        }

        private static string FormatTime(long totalSeconds)
        {
            if (totalSeconds <= 0) return "0:00";
            var ts = TimeSpan.FromSeconds(totalSeconds);
            return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
        }

        /// <summary>
        /// Попытка применить pending состояния из PiP к текущему открытому видео.
        /// Применяем только когда PiP **не** активен — иначе main должен оставаться пассивным.
        /// </summary>
        private void TryApplyPendingPiPStateForCurrent()
        {
            try
            {
                if (_isPiPActive) return; // не применяем пока PiP открыт

                if (string.IsNullOrWhiteSpace(_pendingPiPFilePath)) return;
                var currentPath = _vm?.SelectedVideo?.FilePath ?? _vm?.CurrentVideoPath;
                if (string.IsNullOrWhiteSpace(currentPath)) return;

                if (!string.Equals(currentPath, _pendingPiPFilePath, StringComparison.OrdinalIgnoreCase))
                    return;

                try
                {
                    if (_pendingPiPPositionSeconds.HasValue)
                    {
                        _playerController.Seek(TimeSpan.FromSeconds(_pendingPiPPositionSeconds.Value));
                    }

                    if (_pendingPiPSpeed.HasValue) _playerController.Speed = _pendingPiPSpeed.Value;
                    if (_pendingPiPVolume.HasValue) _playerController.Volume = _pendingPiPVolume.Value;

                    if (_pendingPiPIsPlaying.HasValue && _pendingPiPIsPlaying.Value)
                    {
                        _playerController.Play();
                        _isPlaying = true;
                    }
                    else
                    {
                        _playerController.Pause();
                        _isPlaying = false;
                    }

                    UpdatePlayButton();
                }
                catch
                {
                    // если seek не сработал (еще не открыт) — MediaOpened вызовет повторную попытку
                }

                ClearPendingPiPState();
            }
            catch { /* swallow */ }
        }

        private void OnResumeAvailable(ResumeAvailableMessage msg)
        {
            Dispatcher.Invoke(() =>
            {
                if (_vm.SelectedVideo != null && Guid.TryParse(_vm.SelectedVideo.PartId, out var sid) && sid == msg.PartId)
                {
                    _pendingResumePartId = msg.PartId;
                    _pendingResumePositionSeconds = msg.PositionSeconds;
                    _pendingResumeCourseRoot = msg.CourseRootPath;

                    ResumeText.Text = $"Продолжить с {FormatTime(msg.PositionSeconds)}?";
                    ResumeBanner.Visibility = Visibility.Visible;

                    var totalMs = _resumeAutoHideInterval.TotalMilliseconds;
                    ResumeProgressBar.Maximum = totalMs;
                    ResumeProgressBar.Value = totalMs;
                    _resumeAutoHideEnd = DateTime.UtcNow + _resumeAutoHideInterval;
                    _resumeAutoHideTimer.Stop();
                    _resumeAutoHideTimer.Start();
                }
            });
        }

        private async void BtnResume_Click(object sender, RoutedEventArgs e)
        {
            _resumeAutoHideTimer.Stop();
            ResumeProgressBar.Value = 0;

            ResumeBanner.Visibility = Visibility.Collapsed;
            await ContinueFromResumeAsync();
        }

        private async void BtnClearResume_Click(object sender, RoutedEventArgs e)
        {
            _resumeAutoHideTimer.Stop();
            ResumeProgressBar.Value = 0;
            ResumeBanner.Visibility = Visibility.Collapsed;

            _pendingResumePartId = null;
            _pendingResumePositionSeconds = null;
            _pendingResumeCourseRoot = null;

            try
            {
                if (!string.IsNullOrWhiteSpace(_vm.CourseRootPath))
                    await _playbackService.ClearResumeAsync(_vm.CourseRootPath);
            }
            catch { }
        }

        private async Task ContinueFromResumeAsync()
        {
            try
            {
                if (_pendingResumePartId.HasValue && _pendingResumePositionSeconds.HasValue)
                {
                    if (_vm.SelectedVideo != null && Guid.TryParse(_vm.SelectedVideo.PartId, out var selId) && selId == _pendingResumePartId.Value)
                    {
                        var pos = _pendingResumePositionSeconds.Value;
                        if (pos > 0)
                        {
                            _playerController.Seek(TimeSpan.FromSeconds(pos));
                            try { await _playbackService.NotifyPositionAsync(_vm.SelectedVideo.FilePath, pos); } catch { }
                            return;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(_vm.CourseRootPath) || _vm.SelectedVideo == null) return;
                var course = await _repository.LoadAsync(_vm.CourseRootPath);
                if (course == null || !course.LastPlayedPartId.HasValue) return;
                if (!Guid.TryParse(_vm.SelectedVideo.PartId, out var sid)) return;
                if (course.LastPlayedPartId.Value != sid) return;

                var fallbackPos = course.LastPlayedPositionSeconds;
                if (fallbackPos > 0)
                {
                    _playerController.Seek(TimeSpan.FromSeconds(fallbackPos));
                    try { await _playbackService.NotifyPositionAsync(_vm.SelectedVideo.FilePath, fallbackPos); } catch { }
                }
            }
            catch { }
            finally
            {
                _pendingResumePartId = null;
                _pendingResumePositionSeconds = null;
                _pendingResumeCourseRoot = null;

                _resumeAutoHideTimer.Stop();
                ResumeProgressBar.Value = 0;
                ResumeBanner.Visibility = Visibility.Collapsed;
            }
        }

        private async void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            try
            {
                if (_vm.SelectedVideo != null && !string.IsNullOrWhiteSpace(_vm.CourseRootPath) && Guid.TryParse(_vm.SelectedVideo.PartId, out var pid))
                {
                    var sec = (long)Math.Floor(_playerController.Position.TotalSeconds);
                    await _playbackService.SavePositionByPartIdAsync(_vm.CourseRootPath, pid, sec);
                }

                await _positionManager.FlushAsync();
            }
            catch { }
            finally
            {
                try { volumeSlider.ValueChanged -= volumeSlider_ValueChanged; } catch { }
                try { videoSlider.ValueChanged -= videoSlider_ValueChanged; } catch { }

                try { _playerController.Dispose(); } catch { }
                try { _positionManager.Dispose(); } catch { }

                try { _resumeAutoHideTimer.Stop(); } catch { }
            }
        }

        private void ResumeAutoHideTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                var now = DateTime.UtcNow;
                if (_resumeAutoHideEnd <= now)
                {
                    ResumeBanner.Visibility = Visibility.Collapsed;
                    ResumeProgressBar.Value = 0;
                    _resumeAutoHideTimer.Stop();
                    return;
                }

                var remaining = _resumeAutoHideEnd - now;
                var remainingMs = remaining.TotalMilliseconds;
                var totalMs = _resumeAutoHideInterval.TotalMilliseconds;

                ResumeProgressBar.Value = Math.Max(0, Math.Min(totalMs, remainingMs));
            }
            catch { }
        }

        private async void BtnPiP_Click(object sender, RoutedEventArgs e)
        {
            var file = _vm.SelectedVideo?.FilePath;
            if (file == null) return;
            var partId = Guid.TryParse(_vm.SelectedVideo!.PartId, out var pid) ? pid : (Guid?)null;
            var wasPlaying = _isPlaying;

            await _pipController.OpenPiPAsync(_playerController, file, _playerController.Position, _playerController.Speed, _playerController.Volume, wasPlaying, _vm.CourseRootPath, partId);
        }

        private void OnPiPStateChanged(bool isActive)
        {
            _isPiPActive = isActive;
            if (_isPiPActive)
            {
                try { _playerController?.Pause(); } catch { }
                PiPModeOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                PiPModeOverlay.Visibility = Visibility.Collapsed;
                // PiP закрыт — применим pending состояние (если есть)
                TryApplyPendingPiPStateForCurrent();
            }
        }
    }
}
