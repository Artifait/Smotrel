using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Smotrel.Messages;
using Smotrel.ViewModels;

using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Smotrel.Services.Interfaces;

namespace Smotrel.Views
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _overlayTimer;
        private readonly TimeSpan _overlayTimeout = TimeSpan.FromSeconds(3);

        private readonly MainViewModel _vm;
        private readonly IPlaybackService _playbackService;
        private readonly ICourseRepository _repository;

        private bool _isSeeking = false;
        private bool _isPlaying = true;
        private bool _isFullscreen = false;

        private readonly DispatcherTimer _centerIconTimer;

        private bool _keepOverlayVisibleWhenPaused = true;
        private bool _forceShowOverlayWhenPausing = true;

        // resume / save helpers
        private Guid? _currentPartId = null;
        private int _lastNotifiedSecond = -1;

        // to avoid spamming playback service when slider dragging
        private DateTime _lastNotifyTime = DateTime.MinValue;

        public MainWindow(MainViewModel vm, IPlaybackService playbackService, ICourseRepository repository)
        {
            InitializeComponent();

            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            _playbackService = playbackService ?? throw new ArgumentNullException(nameof(playbackService));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));

            DataContext = _vm;

            _vm.PropertyChanged += Vm_PropertyChanged;

            // Messenger handlers
            WeakReferenceMessenger.Default.Register<VideoControlMessage>(this, HandleVideoControl);
            WeakReferenceMessenger.Default.Register<ResumeAvailableMessage>(this, (r, msg) => OnResumeAvailable(msg));

            // CompositionTarget.Rendering для обновления слайдера и времени
            CompositionTarget.Rendering += UpdateSliderPosition;

            // Media events
            Player.MediaOpened += Player_MediaOpened;
            Player.MediaEnded += Player_MediaEnded;

            // Overlay timer
            _overlayTimer = new DispatcherTimer { Interval = _overlayTimeout };
            _overlayTimer.Tick += (s, e) => HideOverlay();

            // Center icon timer
            _centerIconTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _centerIconTimer.Tick += (s, e) =>
            {
                _centerIconTimer.Stop();
                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(200));
                fadeOut.Completed += (_, __) => centerIcon.Visibility = Visibility.Collapsed;
                centerIcon.BeginAnimation(OpacityProperty, fadeOut);
            };

            // Initial audio
            volumeSlider.Value = 0.5;
            Player.Volume = 0.5;

            UpdatePlayButton();

            // Closing handler to flush
            this.Closing += MainWindow_Closing;
        }

        // ---------------- VM property changes ----------------
        private void Vm_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // When CurrentVideoPath changes (via SelectedVideo) — load & play
            if (e.PropertyName == nameof(_vm.CurrentVideoPath) && !string.IsNullOrEmpty(_vm.CurrentVideoPath))
            {
                try
                {
                    Player.Source = new Uri(_vm.CurrentVideoPath);
                    Player.SpeedRatio = 1.0;
                    Player.Play();
                    _isPlaying = true;
                    UpdatePlayButton();
                    ShowCenterFeedback(_isPlaying);
                }
                catch
                {
                    // ignore invalid URIs / playback errors here; could log
                }

                // update currentPartId from SelectedVideo
                UpdateCurrentPartIdFromSelected();
            }

            // If SelectedVideo changed we already send PlayVideoMessage in VM; here we also ensure part id updated
            if (e.PropertyName == nameof(_vm.SelectedVideo))
            {
                UpdateCurrentPartIdFromSelected();
                ShowOverlay();
                // Update time text immediately (may be updated later in MediaOpened)
                UpdateTimeText();
            }
        }

        private void UpdateCurrentPartIdFromSelected()
        {
            _currentPartId = null;
            _lastNotifiedSecond = -1;

            if (_vm.SelectedVideo != null && !string.IsNullOrWhiteSpace(_vm.SelectedVideo.PartId))
            {
                if (Guid.TryParse(_vm.SelectedVideo.PartId, out var gid))
                    _currentPartId = gid;
            }
        }

        // ---------------- Media events and slider ----------------
        private void Player_MediaOpened(object sender, RoutedEventArgs e)
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

                // NOTE:
                // В старой реализации здесь автоматически ставился Player.Position
                // на значение SelectedVideo.LastPosition (если >0). Это приводило
                // к тому, что плеер уже перематывал видео, а баннер "Продолжить"
                // всё ещё предлагал перемотать — двойная логика и путаница.
                //
                // Мы **удаляем** автоматическое применение сохранённой позиции.
                // Поведение:
                //  - при открытии видео плеер стартует с начала;
                //  - если есть сохранённая позиция — VM отправит ResumeAvailableMessage,
                //    MainWindow покажет баннер; пользователь может нажать "Продолжить",
                //    что вызовет ContinueFromResumeAsync() и вручную установит Player.Position.
                //
                // Если нужно вернуть поведение "автоматически применять resume", можно
                // добавить настройку (например AppSettings.AutoApplyResume) и применить
                // позицию только когда флаг включён.

                AdjustOverlaySize();
                UpdateTimeText();
            }
            catch
            {
                // ignore
            }
        }


        private void Player_MediaEnded(object sender, RoutedEventArgs e)
        {
            // mark watched and clear resume if appropriate
            if (_currentPartId.HasValue && !string.IsNullOrWhiteSpace(_vm.CourseRootPath))
            {
                // Fire and forget
                _ = _playbackService.MarkWatchedByPartIdAsync(_vm.CourseRootPath, _currentPartId.Value);
            }

            // Auto-advance logic: if current is last in chapter -> first of next chapter; else next in playlist
            try
            {
                var current = _vm.SelectedVideo;
                if (current == null) return;

                // build list and indexes
                var list = _vm.Playlist.ToList();
                var currentIndex = list.FindIndex(v => v.PartId == current.PartId);

                // find index within chapter
                if (!string.IsNullOrWhiteSpace(current.ChapterId))
                {
                    var chapterParts = list.Where(p => p.ChapterId == current.ChapterId).ToList();
                    var idxInChapter = chapterParts.FindIndex(p => p.PartId == current.PartId);
                    if (idxInChapter == chapterParts.Count - 1)
                    {
                        // last in chapter -> jump to first of next chapter (if any)
                        var chapterOrder = list.Select(p => p.ChapterId).Distinct().ToList();
                        var curChapterIndex = chapterOrder.IndexOf(current.ChapterId);
                        if (curChapterIndex >= 0 && curChapterIndex < chapterOrder.Count - 1)
                        {
                            var nextChapterId = chapterOrder[curChapterIndex + 1];
                            var nextPart = list.FirstOrDefault(p => p.ChapterId == nextChapterId);
                            if (nextPart != null)
                            {
                                var nextGlobalIndex = list.IndexOf(nextPart);
                                _vm.SetCurrentIndex(nextGlobalIndex);
                                return;
                            }
                        }
                    }
                }

                // default: next part
                if (_vm.Playlist.Count > 0 && _vm.Playlist.IndexOf(current) < _vm.Playlist.Count - 1)
                {
                    _vm.MoveNext();
                }
            }
            catch
            {
                // ignore auto-advance errors
            }
        }

        private bool playerPositionWithinBounds(TimeSpan pos)
        {
            if (Player.NaturalDuration.HasTimeSpan)
                return pos <= Player.NaturalDuration.TimeSpan;

            if (_vm.SelectedVideo?.Duration > 0)
                return pos.TotalSeconds <= _vm.SelectedVideo.Duration;

            return true;
        }

        private void UpdateSliderPosition(object sender, EventArgs e)
        {
            if (_isSeeking) return;

            if (Player.NaturalDuration.HasTimeSpan || _vm.SelectedVideo?.Duration > 0)
            {
                try
                {
                    videoSlider.Value = Player.Position.TotalSeconds;
                }
                catch
                {
                    // ignore binding exceptions
                }
            }

            UpdateTimeText();

            // Debounced send to PlaybackService: only if second changed
            var currentSec = (int)Math.Floor(Player.Position.TotalSeconds);
            if (currentSec != _lastNotifiedSecond)
            {
                _lastNotifiedSecond = currentSec;
                // Avoid spamming: at least 200ms between notifications (but PlaybackService itself debounces)
                if ((DateTime.UtcNow - _lastNotifyTime).TotalMilliseconds > 200)
                {
                    _lastNotifyTime = DateTime.UtcNow;
                    if (_vm.SelectedVideo != null && !string.IsNullOrWhiteSpace(_vm.SelectedVideo.FilePath))
                    {
                        _ = _playbackService.NotifyPositionAsync(_vm.SelectedVideo.FilePath, currentSec);
                    }
                }
            }
        }

        private void Slider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isSeeking = true;
        }

        private void Slider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            var posSec = (int)Math.Round(videoSlider.Value);
            Player.Position = TimeSpan.FromSeconds(posSec);
            _isSeeking = false;

            // Immediately inform playback service about new position (user action)
            if (_vm.SelectedVideo != null && _currentPartId.HasValue && !string.IsNullOrWhiteSpace(_vm.CourseRootPath))
            {
                _ = _playbackService.SavePositionByPartIdAsync(_vm.CourseRootPath, _currentPartId.Value, posSec);
            }
        }

        private void videoSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Could show hover tooltip here
        }

        // ---------------- Player controls / UI overlay ----------------
        private void HandleVideoControl(object recipient, VideoControlMessage msg)
        {
            switch (msg.Action)
            {
                case VideoControlAction.Next:
                    Player.Stop();
                    _vm.MoveNext();
                    break;
                case VideoControlAction.Previous:
                    Player.Stop();
                    _vm.MovePrevious();
                    break;
                case VideoControlAction.TogglePlayPause:
                    if (_isPlaying) Player.Pause();
                    else Player.Play();

                    _isPlaying = !_isPlaying;
                    UpdatePlayButton();
                    ShowCenterFeedback(_isPlaying);
                    break;
                case VideoControlAction.SpeedUp:
                    Player.SpeedRatio *= 2;
                    break;
                case VideoControlAction.NormalSpeed:
                    Player.SpeedRatio = 1.0;
                    break;
                case VideoControlAction.ToggleFullscreen:
                    ToggleFullscreen();
                    break;
            }
        }

        private void ToggleFullscreen()
        {
            if (_isFullscreen)
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
            }
            else
            {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
            }
            _isFullscreen = !_isFullscreen;
        }

        private void Player_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            WeakReferenceMessenger.Default.Send(new VideoControlMessage(VideoControlAction.TogglePlayPause));
        }

        // overlay show/hide
        private void ShowOverlay()
        {
            AnimateOverlayOpacity(1.0);
            _overlayTimer.Stop();
            if (!(_keepOverlayVisibleWhenPaused && !_isPlaying))
                _overlayTimer.Start();
        }

        private void HideOverlay()
        {
            if (_keepOverlayVisibleWhenPaused && !_isPlaying)
            {
                _overlayTimer.Stop();
                return;
            }
            AnimateOverlayOpacity(0.0);
            _overlayTimer.Stop();
        }

        private void AnimateOverlayOpacity(double toOpacity)
        {
            var animation = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = toOpacity,
                Duration = TimeSpan.FromMilliseconds(300),
            };
            ControlOverlay.BeginAnimation(OpacityProperty, animation);
        }

        private void PlayerContainer_MouseEnter(object sender, MouseEventArgs e) => ShowOverlay();
        private void PlayerContainer_MouseMove(object sender, MouseEventArgs e) => ShowOverlay();
        private void PlayerContainer_MouseLeave(object sender, MouseEventArgs e) => HideOverlay();
        private void PlayerContainer_SizeChanged(object sender, SizeChangedEventArgs e) => AdjustOverlaySize();

        private void AdjustOverlaySize()
        {
            if (Player.NaturalVideoWidth == 0 || Player.NaturalVideoHeight == 0) return;

            double containerWidth = PlayerContainer.ActualWidth;
            double containerHeight = PlayerContainer.ActualHeight;

            double videoAspect = (double)Player.NaturalVideoWidth / Player.NaturalVideoHeight;
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
            Player.Volume = e.NewValue;
        }

        // ---------------- UI helpers ----------------
        private void UpdatePlayButton()
        {
            if (btnPausePlay != null)
                btnPausePlay.Content = _isPlaying ? "⏸" : "▶";
        }

        private void ShowCenterFeedback(bool isPlaying)
        {
            if (centerIcon == null) return;

            bool wasPlayingBefore = !isPlaying;
            if (wasPlayingBefore && _forceShowOverlayWhenPausing)
            {
                ControlOverlay.BeginAnimation(OpacityProperty, null);
                ControlOverlay.Opacity = 1.0;
                ControlOverlay.Visibility = Visibility.Visible;
                _overlayTimer.Stop();
            }

            centerIcon.Text = isPlaying ? "▶" : "⏸";
            centerIcon.Visibility = Visibility.Visible;

            if (!(centerIcon.RenderTransform is ScaleTransform scale))
            {
                scale = new ScaleTransform(1, 1);
                centerIcon.RenderTransform = scale;
                centerIcon.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            centerIcon.BeginAnimation(OpacityProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(120));
            centerIcon.BeginAnimation(OpacityProperty, fadeIn);

            var scaleAnim = new System.Windows.Media.Animation.DoubleAnimation(0.8, 1.15, TimeSpan.FromMilliseconds(220))
            {
                AutoReverse = true,
                EasingFunction = new System.Windows.Media.Animation.BackEase { Amplitude = 0.3, EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);

            _centerIconTimer.Stop();
            _centerIconTimer.Start();
        }

        private void UpdateTimeText()
        {
            var curSec = (int)Math.Floor(Player.Position.TotalSeconds);

            long totalSec = 0;
            if (Player.NaturalDuration.HasTimeSpan)
                totalSec = (long)Math.Round(Player.NaturalDuration.TimeSpan.TotalSeconds);
            else if (_vm.SelectedVideo?.Duration > 0)
                totalSec = (long)_vm.SelectedVideo.Duration;
            else
                totalSec = 0;

            timeText.Text = $"{FormatTime(curSec)} / {FormatTime(totalSec)}";
        }

        private static string FormatTime(long totalSeconds)
        {
            if (totalSeconds <= 0) return "0:00";
            var ts = TimeSpan.FromSeconds(totalSeconds);
            if (ts.TotalHours >= 1)
                return ts.ToString(@"h\:mm\:ss");
            return ts.ToString(@"m\:ss");
        }

        // ---------------- Resume banner handling ----------------
        private void OnResumeAvailable(ResumeAvailableMessage msg)
        {
            // Ensure UI thread
            Dispatcher.Invoke(() =>
            {
                // Only show if selected video matches
                if (_vm.SelectedVideo != null && Guid.TryParse(_vm.SelectedVideo.PartId, out var sid) && sid == msg.PartId)
                {
                    ResumeText.Text = $"Продолжить с {FormatTime(msg.PositionSeconds)}?";
                    ResumeBanner.Visibility = Visibility.Visible;

                    // auto-hide after 8 seconds
                    var auto = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
                    auto.Tick += (s, e) =>
                    {
                        auto.Stop();
                        ResumeBanner.Visibility = Visibility.Collapsed;
                    };
                    auto.Start();
                }
            });
        }

        private async void BtnResume_Click(object sender, RoutedEventArgs e)
        {
            ResumeBanner.Visibility = Visibility.Collapsed;
            await ContinueFromResumeAsync();
        }

        private async void BtnClearResume_Click(object sender, RoutedEventArgs e)
        {
            ResumeBanner.Visibility = Visibility.Collapsed;
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
                if (string.IsNullOrWhiteSpace(_vm.CourseRootPath) || _vm.SelectedVideo == null) return;
                var course = await _repository.LoadAsync(_vm.CourseRootPath);
                if (course == null || !course.LastPlayedPartId.HasValue) return;
                if (!Guid.TryParse(_vm.SelectedVideo.PartId, out var sid)) return;
                if (course.LastPlayedPartId.Value != sid) return;

                var pos = course.LastPlayedPositionSeconds;
                if (pos > 0)
                {
                    Player.Position = TimeSpan.FromSeconds(pos);
                    await _playbackService.NotifyPositionAsync(_vm.SelectedVideo.FilePath, pos);
                }
            }
            catch
            {
                // ignore
            }
        }

        // ---------------- Closing / Flush ----------------
        private async void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            try
            {
                // final save of current position into course (synchronous awaited)
                if (_currentPartId.HasValue && !string.IsNullOrWhiteSpace(_vm.CourseRootPath))
                {
                    var sec = (long)Math.Floor(Player.Position.TotalSeconds);
                    await _playbackService.SavePositionByPartIdAsync(_vm.CourseRootPath, _currentPartId.Value, sec);
                }

                // flush pending
                await _playbackService.FlushAsync();
            }
            catch
            {
                // ignore errors on close
            }
            finally
            {
                // unsubscribe rendering to avoid leaks
                CompositionTarget.Rendering -= UpdateSliderPosition;
            }
        }
    }
}
