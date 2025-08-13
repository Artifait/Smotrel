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

        private bool _isPlaying = true;
        private long? _pendingResumePositionSeconds = null;
        private Guid? _pendingResumePartId = null;
        private string? _pendingResumeCourseRoot = null;

        private readonly DispatcherTimer _resumeAutoHideTimer;
        private readonly TimeSpan _resumeAutoHideInterval = TimeSpan.FromSeconds(8); // тот же интервал что и раньше
        private DateTime _resumeAutoHideEnd = DateTime.MinValue;

        public MainWindow(MainViewModel vm, IPlaybackService playbackService, ICourseRepository repository)
        {
            InitializeComponent();

            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            _playbackService = playbackService ?? throw new ArgumentNullException(nameof(playbackService));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));

            DataContext = _vm;
            _vm.PropertyChanged += Vm_PropertyChanged;

            // controllers (создаём и инициализируем)
            _playerController = new PlayerController();
            _playerController.Initialize(Player);
            _playerController.MediaOpened += Player_MediaOpened;
            _playerController.MediaEnded += Player_MediaEnded;
            _playerController.PositionChanged += Player_PositionChanged;

            _positionManager = new PlaybackPositionManager(_playbackService);
            _playlistController = new PlaylistController(_vm);
            _overlayController = new OverlayController(ControlOverlay, centerIcon, TimeSpan.FromSeconds(3));

            _resumeAutoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _resumeAutoHideTimer.Tick += ResumeAutoHideTimer_Tick;

            // messaging
            WeakReferenceMessenger.Default.Register<VideoControlMessage>(this, HandleVideoControl);
            WeakReferenceMessenger.Default.Register<ResumeAvailableMessage>(this, (r, msg) => OnResumeAvailable(msg));
            WeakReferenceMessenger.Default.Register<PlaybackSpeedChangedMessage>(this, (r, msg) => _playerController.Speed = msg.Speed);

            // Программно устанавливаем начальные значения и подписываемся на обработчики
            // Устанавливаем начальную громкость (без преждевременного вызова обработчика)
            volumeSlider.Value = _playerController.Volume;

            // Подписываемся на ValueChanged **после** инициализации контроллеров
            volumeSlider.ValueChanged += volumeSlider_ValueChanged;
            videoSlider.ValueChanged += videoSlider_ValueChanged;

            // синхронизируем контроллер с UI (на случай, если ещё требуется)
            _playerController.Volume = volumeSlider.Value;

            UpdatePlayButton();

            this.Closing += MainWindow_Closing;
        }


        // VM property changes
        private void Vm_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_vm.CurrentVideoPath) && !string.IsNullOrEmpty(_vm.CurrentVideoPath))
            {
                try
                {
                    Player.Source = new Uri(_vm.CurrentVideoPath);
                    _playerController.Speed = 1.0;
                    _playerController.Play();
                    _isPlaying = true;
                    UpdatePlayButton();
                    _overlayController.ShowCenterFeedback(_isPlaying);
                }
                catch { }

                UpdateCurrentPartContext();
            }

            if (e.PropertyName == nameof(_vm.SelectedVideo))
            {
                UpdateCurrentPartContext();
                _overlayController.ShowOverlay(isPlaying: _isPlaying);
                UpdateTimeText();
            }
        }

        private void UpdateCurrentPartContext()
        {
            _positionManager.SetContext(_vm.CourseRootPath,
                string.IsNullOrWhiteSpace(_vm.SelectedVideo?.PartId) ? (Guid?)null : Guid.Parse(_vm.SelectedVideo.PartId),
                _vm.SelectedVideo?.FilePath);
        }

        // Player controller handlers
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
        }

        private void Player_MediaEnded(object? sender, EventArgs e)
        {
            // mark watched
            if (!string.IsNullOrWhiteSpace(_vm.CourseRootPath) && _vm.SelectedVideo != null && Guid.TryParse(_vm.SelectedVideo.PartId, out var pid))
            {
                _ = _playbackService.MarkWatchedByPartIdAsync(_vm.CourseRootPath, pid);
            }

            // delegate auto-advance to playlist controller
            _playlistController.OnMediaEnded();
        }

        private void Player_PositionChanged(object? sender, TimeSpan pos)
        {
            // update slider & time (on UI thread)
            Dispatcher.Invoke(() =>
            {
                if (!videoSlider.IsMouseCaptureWithin)
                {
                    videoSlider.Value = pos.TotalSeconds;
                }
                UpdateTimeText();
            });

            // inform position manager (debounced write)
            _positionManager.OnPositionChanged(pos);
        }

        private void Slider_PreviewMouseDown(object sender, MouseButtonEventArgs e) => _playerController.Pause();
        private async void Slider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            var posSec = (int)Math.Round(videoSlider.Value);
            _playerController.Seek(TimeSpan.FromSeconds(posSec));
            if (_isPlaying) _playerController.Play();

            // immediate save
            await _positionManager.SavePositionImmediateAsync(posSec);
        }

        private void videoSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { /* optional tooltip */ }

        // Controls
        private void HandleVideoControl(object recipient, VideoControlMessage msg)
        {
            switch (msg.Action)
            {
                case VideoControlAction.Next:
                    _playerController.Stop();
                    _vm.MoveNext();
                    break;
                case VideoControlAction.Previous:
                    _playerController.Stop();
                    _vm.MovePrevious();
                    break;
                case VideoControlAction.TogglePlayPause:
                    if (_isPlaying) _playerController.Pause();
                    else _playerController.Play();

                    _isPlaying = !_isPlaying;
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
                WeakReferenceMessenger.Default.Send(
                                       new VideoControlMessage(VideoControlAction.ToggleFullscreen));
            }
        }

        // overlay handlers
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

            ControlOverlay.Margin = new Thickness(horizontalMargin, 0, horizontalMargin, verticalMargin);
        }

        private void volumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _playerController.Volume = e.NewValue;
        }

        // UI helpers
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

        // Resume banner handling (kept in Window for now)
        private void OnResumeAvailable(ResumeAvailableMessage msg)
        {
            Dispatcher.Invoke(() =>
            {
                // Только если выбран тот же кусок — показываем
                if (_vm.SelectedVideo != null && Guid.TryParse(_vm.SelectedVideo.PartId, out var sid) && sid == msg.PartId)
                {
                    // кешируем данные (используем данные из message — они актуальны на момент скана/проверки)
                    _pendingResumePartId = msg.PartId;
                    _pendingResumePositionSeconds = msg.PositionSeconds;
                    _pendingResumeCourseRoot = msg.CourseRootPath; // если в message есть

                    ResumeText.Text = $"Продолжить с {FormatTime(msg.PositionSeconds)}?";
                    ResumeBanner.Visibility = Visibility.Visible;

                    // Настройка прогресс-бара (если он есть)
                    var totalMs = _resumeAutoHideInterval.TotalMilliseconds;
                    ResumeProgressBar.Maximum = totalMs;
                    ResumeProgressBar.Value = totalMs;
                    _resumeAutoHideEnd = DateTime.UtcNow + _resumeAutoHideInterval;
                    _resumeAutoHideTimer.Stop();
                    _resumeAutoHideTimer.Start();
                }
                else
                {
                    // не для текущего селекта — игнорируем
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
            // Остановим таймер и скроем UI
            _resumeAutoHideTimer.Stop();
            ResumeProgressBar.Value = 0;
            ResumeBanner.Visibility = Visibility.Collapsed;

            // Очистим локальный кэш
            _pendingResumePartId = null;
            _pendingResumePositionSeconds = null;
            _pendingResumeCourseRoot = null;

            // И попытаемся очистить в репозитории
            try
            {
                if (!string.IsNullOrWhiteSpace(_vm.CourseRootPath))
                    await _playbackService.ClearResumeAsync(_vm.CourseRootPath);
            }
            catch
            {
                // ignore errors
            }
        }


        private async Task ContinueFromResumeAsync()
        {
            try
            {
                // prefer cached resume values (they were set in OnResumeAvailable)
                if (_pendingResumePartId.HasValue && _pendingResumePositionSeconds.HasValue)
                {
                    // validate selected video matches cached part
                    if (_vm.SelectedVideo != null && Guid.TryParse(_vm.SelectedVideo.PartId, out var selId) && selId == _pendingResumePartId.Value)
                    {
                        var pos = _pendingResumePositionSeconds.Value;
                        if (pos > 0)
                        {
                            // seek and notify
                            _playerController.Seek(TimeSpan.FromSeconds(pos));
                            try
                            {
                                await _playbackService.NotifyPositionAsync(_vm.SelectedVideo.FilePath, pos);
                            }
                            catch { }
                            // optionally clear cached resume? keep it if you want
                            return;
                        }
                    }
                }

                // Fallback: try to load from repository (older behavior)
                if (string.IsNullOrWhiteSpace(_vm.CourseRootPath) || _vm.SelectedVideo == null) return;
                var course = await _repository.LoadAsync(_vm.CourseRootPath);
                if (course == null || !course.LastPlayedPartId.HasValue) return;
                if (!Guid.TryParse(_vm.SelectedVideo.PartId, out var sid)) return;
                if (course.LastPlayedPartId.Value != sid) return;

                var fallbackPos = course.LastPlayedPositionSeconds;
                if (fallbackPos > 0)
                {
                    _playerController.Seek(TimeSpan.FromSeconds(fallbackPos));
                    try
                    {
                        await _playbackService.NotifyPositionAsync(_vm.SelectedVideo.FilePath, fallbackPos);
                    }
                    catch { }
                }
            }
            catch
            {
                // ignore
            }
            finally
            {
                // cleanup cached info after trying to resume to avoid reusing stale values
                _pendingResumePartId = null;
                _pendingResumePositionSeconds = null;
                _pendingResumeCourseRoot = null;

                // stop timer and hide banner just in case
                _resumeAutoHideTimer.Stop();
                ResumeProgressBar.Value = 0;
                ResumeBanner.Visibility = Visibility.Collapsed;
            }
        }


        // Closing / flush
        private async void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            try
            {
                // final save
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
                // Отписываемся от программных событий
                try
                {
                    volumeSlider.ValueChanged -= volumeSlider_ValueChanged;
                    videoSlider.ValueChanged -= videoSlider_ValueChanged;
                }
                catch { }

                // Dispose controllers
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
                    // время вышло — скрываем баннер и останавливаем таймер
                    ResumeBanner.Visibility = Visibility.Collapsed;
                    ResumeProgressBar.Value = 0;
                    _resumeAutoHideTimer.Stop();
                    return;
                }

                var remaining = _resumeAutoHideEnd - now;
                var remainingMs = remaining.TotalMilliseconds;
                var totalMs = _resumeAutoHideInterval.TotalMilliseconds;

                // progress = remainingMs (we set Maximum = totalMs when starting)
                ResumeProgressBar.Value = Math.Max(0, Math.Min(totalMs, remainingMs));
            }
            catch
            {
                // игнорируем ошибки UI-таймера
            }
        }
    }
}
