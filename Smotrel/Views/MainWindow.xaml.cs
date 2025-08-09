using System;
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

namespace Smotrel.Views
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _overlayTimer;
        private readonly TimeSpan _overlayTimeout = TimeSpan.FromSeconds(1); // из настроек

        private readonly MainViewModel _vm;
        private bool _isSeeking = false;
        private bool _isPlaying = true;
        private bool _isFullscreen = false;

        public MainWindow()
        {
            InitializeComponent();

            var videoSvc = new FileSystemVideoLibraryService();
            _vm = new MainViewModel(videoSvc);
            DataContext = _vm;

            _vm.PropertyChanged += Vm_PropertyChanged;

            WeakReferenceMessenger.Default.Register<VideoControlMessage>(this, HandleVideoControl);

            // Обновлять слайдер каждый кадр
            CompositionTarget.Rendering += UpdateSliderPosition;

            // Когда видео загружено — устанавливаем длительность
            Player.MediaOpened += Player_MediaOpened;

            // инициализируем таймер
            _overlayTimer = new DispatcherTimer
            {
                Interval = _overlayTimeout
            };
            _overlayTimer.Tick += (s, e) => {
                HideOverlay();
            };
        }

        private void Vm_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_vm.CurrentVideoPath)
                && !string.IsNullOrEmpty(_vm.CurrentVideoPath))
            {
                Player.Source = new Uri(_vm.CurrentVideoPath);
                Player.SpeedRatio = 1.0;
                Player.Play();
                _isPlaying = true;
            }
        }

        private void Player_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (Player.NaturalDuration.HasTimeSpan)
            {
                videoSlider.Minimum = 0;
                videoSlider.Maximum = Player.NaturalDuration.TimeSpan.TotalSeconds;
            }
        }

        private void UpdateSliderPosition(object sender, EventArgs e)
        {
            if (!_isSeeking && Player.NaturalDuration.HasTimeSpan)
            {
                videoSlider.Value = Player.Position.TotalSeconds;
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
                    _isPlaying = !_isPlaying;
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
            AnimateOverlayOpacity(1.0); // плавное появление
            _overlayTimer.Stop();
            _overlayTimer.Start();
        }

        private void HideOverlay()
        {
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
    }
}
