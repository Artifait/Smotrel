using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Smotrel.Messages;
using Smotrel.Services;
using Smotrel.ViewModels;
using System.Windows.Media.Animation;

using Point = System.Windows.Point;
using System.Windows.Media.Effects;

namespace Smotrel.Views
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _overlayTimer;
        private readonly TimeSpan _overlayTimeout = TimeSpan.FromSeconds(1);

        private readonly MainViewModel _vm;
        private readonly IPlaybackService _playbackService;

        private bool _isSeeking = false;
        private bool _isPlaying = true;
        private bool _isFullscreen = false;

        private readonly DispatcherTimer _centerIconTimer;
        private bool _keepOverlayVisibleWhenPaused = true;
        private bool _forceShowOverlayWhenPausing = true;

        // для сохранения прогресса
        private Guid? _currentPartId = null;
        private int _lastNotifiedSecond = -1;

        public MainWindow(MainViewModel vm, IPlaybackService playbackService)
        {
            InitializeComponent();

            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            _playbackService = playbackService ?? throw new ArgumentNullException(nameof(playbackService));

            DataContext = _vm;

            _vm.PropertyChanged += Vm_PropertyChanged;

            WeakReferenceMessenger.Default.Register<VideoControlMessage>(this, HandleVideoControl);

            CompositionTarget.Rendering += UpdateSliderPosition;

            // MediaOpened подписка (уже была)
            Player.MediaOpened += Player_MediaOpened;
            Player.MediaEnded += Player_MediaEnded; // добавляем обработчик окончания

            _overlayTimer = new DispatcherTimer { Interval = _overlayTimeout };
            _overlayTimer.Tick += (s, e) => HideOverlay();

            _centerIconTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _centerIconTimer.Tick += (s, e) =>
            {
                _centerIconTimer.Stop();
                var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(200));
                fadeOut.Completed += (_, __) => centerIcon.Visibility = Visibility.Collapsed;
                centerIcon.BeginAnimation(OpacityProperty, fadeOut);
            };

            volumeSlider.Value = 0.5;
            Player.Volume = 0.5;
            UpdatePlayButton();

            this.Closing += MainWindow_Closing;
        }

        // при смене свойств ViewModel — отслеживаем SelectedVideo и CurrentVideoPath
        private void Vm_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_vm.CurrentVideoPath)
                && !string.IsNullOrEmpty(_vm.CurrentVideoPath))
            {
                Player.Source = new Uri(_vm.CurrentVideoPath);
                Player.SpeedRatio = 1.0;
                Player.Play();
                _isPlaying = true;
                UpdatePlayButton();
                ShowCenterFeedback(_isPlaying);
            }

            if (e.PropertyName == nameof(_vm.SelectedVideo))
            {
                // получили новую выбранную VideoItem — установим current part id и попытаемся восстановить позицию
                var selected = _vm.SelectedVideo;
                if (selected != null)
                {
                    // предполагается, что VideoItem содержит PartId и Duration (из Course)
                    if (Guid.TryParse(selected.PartId ?? "", out var gid))
                        _currentPartId = gid;
                    else
                        _currentPartId = null;

                    // Если есть сохранённая позиция — запомним и поставим плеер в это место, когда MediaOpened произойдёт.
                    // Здесь можно сразу установить, если MediaElement уже загружен и имеет длительность.
                    if (selected.LastPosition > 0)
                    {
                        try
                        {
                            // Отложим положение, если медиа не открыто — при MediaOpened позиция установится ещё раз.
                            Player.Position = TimeSpan.FromSeconds(selected.LastPosition);
                        }
                        catch { /* ignore */ }
                    }

                    // обновим timeText на основе known duration
                    UpdateTimeText();
                }
                else
                {
                    _currentPartId = null;
                }
            }
        }

        private void Player_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (Player.NaturalDuration.HasTimeSpan)
            {
                videoSlider.Minimum = 0;
                videoSlider.Maximum = Player.NaturalDuration.TimeSpan.TotalSeconds;
            }
            else
            {
                // если NaturalDuration не доступна — попытаться взять длительность из SelectedVideo (metadata)
                if (_vm.SelectedVideo?.Duration is long dur && dur > 0)
                {
                    videoSlider.Minimum = 0;
                    videoSlider.Maximum = dur;
                }
            }

            // Если SelectedVideo имеет LastPosition (resume) — сразу встанем туда
            if (_vm.SelectedVideo?.LastPosition > 0)
            {
                var pos = TimeSpan.FromSeconds(_vm.SelectedVideo.LastPosition);
                // защитимся от выхода за пределы (если Slider.Maximum меньше)
                if (playerPositionWithinBounds(pos))
                    Player.Position = pos;
            }

            AdjustOverlaySize();
            UpdateTimeText();
        }

        private void Player_MediaEnded(object sender, RoutedEventArgs e)
        {
            // при достижении конца — отмечаем watched и сохраняем
            if (_currentPartId.HasValue && _vm.SelectedVideo != null)
            {
                // На всякий случай: пометим watched в background
                _ = _playbackService.MarkWatchedByPartIdAsync(_vm.CourseRootPath!, _currentPartId.Value);
            }
        }

        private bool playerPositionWithinBounds(TimeSpan pos)
        {
            if (Player.NaturalDuration.HasTimeSpan)
            {
                return pos <= Player.NaturalDuration.TimeSpan;
            }
            if (_vm.SelectedVideo?.Duration is long d && d > 0)
            {
                return pos.TotalSeconds <= d;
            }
            return true;
        }

        private void UpdateSliderPosition(object sender, EventArgs e)
        {
            if (!_isSeeking)
            {
                if (Player.NaturalDuration.HasTimeSpan)
                {
                    videoSlider.Value = Player.Position.TotalSeconds;
                }
                else
                {
                    // если NaturalDuration не доступна, но slider.Maximum установлен из metadata
                    videoSlider.Value = Player.Position.TotalSeconds;
                }

                // Обновлять текст времени
                UpdateTimeText();

                // и отправлять позицию в playbackService по целым секундам (чтобы не спамить)
                var currentSec = (int)Math.Floor(Player.Position.TotalSeconds);
                if (currentSec != _lastNotifiedSecond)
                {
                    _lastNotifiedSecond = currentSec;
                    // вызываем без await — service дебаунсит и буферизует
                    if (!string.IsNullOrWhiteSpace(_vm.SelectedVideo?.FilePath))
                    {
                        _ = _playbackService.NotifyPositionAsync(_vm.SelectedVideo.FilePath, currentSec);
                    }
                }
            }
        }

        private void UpdateTimeText()
        {
            // current
            var curSec = (int)Math.Floor(Player.Position.TotalSeconds);

            // total: try MediaElement.NaturalDuration first, then metadata
            long totalSec = 0;
            if (Player.NaturalDuration.HasTimeSpan)
            {
                totalSec = (long)Math.Round(Player.NaturalDuration.TimeSpan.TotalSeconds);
            }
            else if (_vm.SelectedVideo?.Duration is long dur && dur > 0)
            {
                totalSec = dur;
            }
            else
            {
                totalSec = 0;
            }

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

        private async void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            try
            {
                // Перед закрытием — сохранить финальную позицию для текущей части (если есть)
                if (_currentPartId.HasValue && _vm.SelectedVideo != null)
                {
                    var sec = (long)Math.Floor(Player.Position.TotalSeconds);
                    await _playbackService.SavePositionByPartIdAsync(_vm.CourseRootPath, _currentPartId.Value, sec);
                }

                // и сбросить pending (Flush)
                await _playbackService.FlushAsync();
            }
            catch
            {
                // не мешаем закрытию из-за ошибок записи
            }
        }

        private void Slider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Пользователь начал тянуть — останавливаем авто-обновление
            _isSeeking = true;
        }

        private void Slider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            // Пользователь отпустил ползунок — прыгаем плееру и возобновляем авто-обновление
            var pos = TimeSpan.FromSeconds(videoSlider.Value);
            Player.Position = pos;
            _isSeeking = false;
        }

        private void videoSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Можно добавить, например, подсказку с текущим временем:
            // tooltip.Text = TimeSpan.FromSeconds(e.NewValue).ToString(@"hh\:mm\:ss");
        }

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

                    // переключаем состояние и обновляем UI
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


        // показываем оверлей и (пере)запускаем таймер
        private void ShowOverlay()
        {
            // Плавное появление (как раньше)
            AnimateOverlayOpacity(1.0);

            // Останавливаем таймер и запускаем его только если:
            // - либо мы не специально держим overlay при паузе,
            // - либо сейчас играет (то есть можно авто-скрывать)
            _overlayTimer.Stop();
            if (!(_keepOverlayVisibleWhenPaused && !_isPlaying))
            {
                _overlayTimer.Start();
            }
        }

        private void HideOverlay()
        {
            // Если настроено держать overlay при паузе — не прячем его
            if (_keepOverlayVisibleWhenPaused && !_isPlaying)
            {
                // оставляем его видимым — просто останавливаем таймер
                _overlayTimer.Stop();
                return;
            }

            AnimateOverlayOpacity(0.0); // плавное исчезновение
            _overlayTimer.Stop();
        }

        private void AnimateOverlayOpacity(double toOpacity)
        {
            var animation = new DoubleAnimation
            {
                To = toOpacity,
                Duration = TimeSpan.FromMilliseconds(300),
            };
            ControlOverlay.BeginAnimation(OpacityProperty, animation);
        }
        private void PlayerContainer_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            ShowOverlay();
        }

        private void PlayerContainer_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            ShowOverlay();
        }

        private void PlayerContainer_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            HideOverlay();
        }
        private void PlayerContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            AdjustOverlaySize();
        }

        private void AdjustOverlaySize()
        {
            if (Player.NaturalVideoWidth == 0 || Player.NaturalVideoHeight == 0)
                return;

            double containerWidth = PlayerContainer.ActualWidth;
            double containerHeight = PlayerContainer.ActualHeight;

            double videoAspect = (double)Player.NaturalVideoWidth / Player.NaturalVideoHeight;
            double containerAspect = containerWidth / containerHeight;

            double horizontalMargin = 0;
            double verticalMargin = 0;

            if (videoAspect > containerAspect)
            {
                // Видео шире, по бокам нет отступов, сверху/снизу есть
                double actualVideoHeight = containerWidth / videoAspect;
                verticalMargin = (containerHeight - actualVideoHeight) / 2;
            }
            else
            {
                // Видео выше, сверху/снизу нет отступов, по бокам есть
                double actualVideoWidth = containerHeight * videoAspect;
                horizontalMargin = (containerWidth - actualVideoWidth) / 2;
            }

            ControlOverlay.Margin = new Thickness(horizontalMargin, 0, horizontalMargin, verticalMargin);
        }

        private void volumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Player.Volume = e.NewValue; // 0.0 — тишина, 1.0 — максимум
        }

        // ---------------- new helpers ----------------

        // Обновляет иконку нижней кнопки в зависимости от текущего состояния
        private void UpdatePlayButton()
        {
            // Если сейчас играет — показываем иконку "пауза" на кнопке (т.е. действие — поставить на паузу)
            // Если сейчас на паузе — показываем иконку "play" (т.е. действие — воспроизвести)
            if (btnPausePlay != null)
            {
                btnPausePlay.Content = _isPlaying ? "⏸" : "▶";
            }
        }

        // Показывает в центре крупную подсказку (Play / Pause) с анимацией
        private void ShowCenterFeedback(bool isPlaying)
        {
            if (centerIcon == null) return;

            // Если до показа подсказки видео проигрывалось и мы сейчас переходим в паузу:
            // сначала мгновенно показать overlay полностью (без анимации), затем запускать анимацию подсказки.
            bool wasPlayingBefore = !isPlaying;
            if (wasPlayingBefore && _forceShowOverlayWhenPausing)
            {
                // Прервём любые анимации и установим overlay видимым и полностью непрозрачным.
                ControlOverlay.BeginAnimation(OpacityProperty, null);
                ControlOverlay.Opacity = 1.0;
                ControlOverlay.Visibility = Visibility.Visible;

                // Остановим авто-таймер — если настроено держать overlay при паузе, он не будет скрываться.
                _overlayTimer.Stop();
            }

            // Текст и видимость центра
            centerIcon.Text = isPlaying ? "▶" : "⏸";
            centerIcon.Visibility = Visibility.Visible;

            // Подготовим трансформ (если не установлен)
            if (!(centerIcon.RenderTransform is ScaleTransform scale))
            {
                scale = new ScaleTransform(1, 1);
                centerIcon.RenderTransform = scale;
                centerIcon.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            // Сброс анимаций
            centerIcon.BeginAnimation(OpacityProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

            // Плавное появление и "прыжок"
            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(120));
            centerIcon.BeginAnimation(OpacityProperty, fadeIn);

            var scaleAnim = new DoubleAnimation(0.8, 1.15, TimeSpan.FromMilliseconds(220))
            {
                AutoReverse = true,
                EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut }
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);

            // Перезапускаем таймер скрытия индикатора центра (как раньше)
            _centerIconTimer.Stop();
            _centerIconTimer.Start();
        }
    }
}
