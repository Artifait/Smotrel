using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Smotrel.Controllers;
using CommunityToolkit.Mvvm.Messaging;
using Smotrel.Messages;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Smotrel.Views
{
    public partial class PiPWindow : Window
    {
        // NOTE: Мы делаем PiP гибким: либо AttachController(...) (передаёте существующий PlayerController),
        // либо InitializePlayback(file, position, speed, volume, wasPlaying) — PiP создаст свой PlayerController.
        private PlayerController? _controller;
        private bool _isPlaying = false;
        private bool _wasPlayingBeforeSeek = false;
        private readonly DispatcherTimer _uiTimer;

        private bool _isDragging = false;
        private Point _dragStart;
        private const double DragThreshold = 6.0;

        private long? _pendingResumePositionSeconds = null;
        private Guid? _pendingResumePartId = null;
        private string? _pendingResumeCourseRoot = null;

        public System.Windows.Controls.MediaElement PiPPlayer => PiPPlayerControl;

        public event EventHandler? RestoreRequested;
        public event EventHandler? ClosedByUser;

        public PiPWindow()
        {
            InitializeComponent();

            PiPRateCombo.ItemsSource = new double[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };
            PiPRateCombo.SelectedItem = 1.0;

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _uiTimer.Tick += (s, e) => UpdateTimeAndSliderFromController();

            WeakReferenceMessenger.Default.Register<ResumeAvailableMessage>(this, (_, msg) => OnResumeAvailable(msg));
            WeakReferenceMessenger.Default.Register<PlayVideoMessage>(this, (r, msg) =>
            {
                // IMPORTANT: when switching part while PiP is active, we MUST ensure PiP starts the new file from the beginning.
                Dispatcher.Invoke(() =>
                {
                    if (_controller == null) return;
                    try
                    {
                        // remove previous one-time handler if any (defensive)
                        RoutedEventHandler? one = null;
                        one = async (s2, e2) =>
                        {
                            try
                            {
                                PiPPlayer.MediaOpened -= one;

                                // Ensure start from 0 for new part
                                try { _controller.Seek(TimeSpan.Zero); } catch { }

                                // Start playback
                                _controller.Play();
                                _isPlaying = true;
                                PiPPlayPauseBtn.Content = "⏸";
                            }
                            catch { }
                        };

                        // subscribe one-time handler for MediaOpened
                        PiPPlayer.MediaOpened += one;

                        // set new source (MediaOpened will trigger and the handler will play from 0)
                        PiPPlayer.Source = new Uri(msg.FilePath);

                        // send playback-state message so MainWindow (or PositionManager) can sync its player position for this file
                        WeakReferenceMessenger.Default.Send(new PlaybackStateMessage(
                            msg.FilePath,
                            PartId: null, 
                            PositionSeconds: 0,
                            Speed: _controller?.Speed ?? 1.0,
                            Volume: _controller?.Volume ?? 0.5,
                            IsPlaying: true));
                    }
                    catch { }
                });
            });


            // Inform app that PiP is active (so MainWindow can react)
            WeakReferenceMessenger.Default.Send(new PiPStateChangedMessage(true));

            this.Closed += (s, e) =>
            {
                WeakReferenceMessenger.Default.Send(new PiPStateChangedMessage(false));
                try { _controller?.Dispose(); } catch { }
                try { _uiTimer.Stop(); } catch { }
                ClosedByUser?.Invoke(this, EventArgs.Empty);
            };

            PiPRateCombo.SelectionChanged += PiPRateCombo_SelectionChanged;
        }

        /// <summary>
        /// Attach an existing PlayerController instance (the controller must already be Initialize'd
        /// against a MediaElement — or PipController should reinitialize it to target PiPPlayerControl).
        /// We accept a flag wasPlaying to set initial UI state (backwards-compatible).
        /// </summary>
        public void AttachController(PlayerController controller, bool wasPlaying = true)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            _controller = controller;

            // Subscribe to the controller's events that exist on the current PlayerController.
            _controller.PositionChanged += Controller_PositionChanged;
            _controller.MediaOpened += Controller_MediaOpened;
            _controller.MediaEnded += Controller_MediaEnded;
            _controller.PlayingStateChanged += Controller_PlayingStateChanged;

            // sync UI
            try
            {
                PiPVolumeSlider.Value = _controller.Volume;
                // Speed is present on PlayerController; set combo (if value exists in list)
                var sp = _controller.Speed;
                if (PiPRateCombo.Items.Cast<double?>().Any(d => Math.Abs(d.GetValueOrDefault() - sp) < 0.001))
                    PiPRateCombo.SelectedItem = sp;
            }
            catch { }

            _isPlaying = wasPlaying;
            PiPPlayPauseBtn.Content = _isPlaying ? "⏸" : "▶";

            _uiTimer.Start();
        }

        private void Controller_PlayingStateChanged(object? sender, bool isPlaying)
        {
            Dispatcher.Invoke(() =>
            {
                _isPlaying = isPlaying;
                PiPPlayPauseBtn.Content = _isPlaying ? "⏸" : "▶";
            });
        }

        /// <summary>
        /// Helper: Initialize PiP with a fresh internal controller + start playback.
        /// Useful if caller passes only raw playback params.
        /// </summary>
        public void InitializePlayback(string filePath, TimeSpan position, double speed, double volume, bool startPlaying)
        {
            // Create internal controller bound to PiPPlayerControl
            var localController = new PlayerController();
            localController.Initialize(PiPPlayerControl);
            _controller = localController;

            // subscribe
            _controller.PositionChanged += Controller_PositionChanged;
            _controller.MediaOpened += Controller_MediaOpened;
            _controller.MediaEnded += Controller_MediaEnded;
            _controller.PlayingStateChanged += Controller_PlayingStateChanged;

            // apply initial settings
            _controller.Speed = Math.Max(0.1, Math.Min(4.0, speed));
            _controller.Volume = Math.Max(0.0, Math.Min(1.0, volume));

            try
            {
                PiPRateCombo.SelectedItem = _controller.Speed;
            }
            catch { }

            PiPPlayer.Source = new Uri(filePath);

            if (position.TotalSeconds > 0)
            {
                // seek before play to ensure correct frame
                _controller.Seek(position);
            }

            if (startPlaying)
            {
                _controller.Play();
                _isPlaying = true;
                PiPPlayPauseBtn.Content = "⏸";
            }
            else
            {
                // start paused but render a frame (play briefly)
                _controller.Play();
                Task.Delay(120).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        try { _controller.Pause(); _isPlaying = false; PiPPlayPauseBtn.Content = "▶"; } catch { }
                    });
                });
            }

            _uiTimer.Start();
        }

        private void Controller_MediaOpened(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(async () =>
            {
                if (PiPPlayer.NaturalDuration.HasTimeSpan)
                {
                    PiPVideoSlider.Minimum = 0;
                    PiPVideoSlider.Maximum = PiPPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                }

                // Adjust overlay size to match visible video (like MainWindow)
                AdjustOverlaySize();

                // If resume info present and we are currently paused, ensure visible frame:
                if (_pendingResumePositionSeconds.HasValue && (_controller != null && !_isPlaying))
                {
                    try
                    {
                        _controller.Seek(TimeSpan.FromSeconds(_pendingResumePositionSeconds.Value));
                        _controller.Play();
                        await Task.Delay(150);
                        _controller.Pause();
                    }
                    catch { }
                }
            });
        }

        private void Controller_MediaEnded(object? sender, EventArgs e)
        {
            _isPlaying = false;
            Dispatcher.Invoke(() => PiPPlayPauseBtn.Content = "▶");

            // forward to app: next part
            WeakReferenceMessenger.Default.Send(new VideoControlMessage(VideoControlAction.Next));
        }

        private void Controller_PositionChanged(object? sender, TimeSpan pos)
        {
            Dispatcher.Invoke(() =>
            {
                if (!PiPVideoSlider.IsMouseCaptureWithin)
                    PiPVideoSlider.Value = pos.TotalSeconds;
                UpdateTimeAndSliderFromController();
            });
        }

        private void UpdateTimeAndSliderFromController()
        {
            if (_controller == null) return;
            var cur = (int)Math.Floor(_controller.Position.TotalSeconds);
            long total = 0;
            if (PiPPlayer.NaturalDuration.HasTimeSpan)
                total = (long)Math.Round(PiPPlayer.NaturalDuration.TimeSpan.TotalSeconds);

            PiPTimeText.Text = $"{FormatTime(cur)} / {FormatTime(total)}";
        }

        private static string FormatTime(long totalSeconds)
        {
            if (totalSeconds <= 0) return "0:00";
            var ts = TimeSpan.FromSeconds(totalSeconds);
            return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
        }
        // UI handlers
        private void PiPPlayPauseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_controller == null) return;
            if (_isPlaying)
            {
                _controller.Pause();
                _isPlaying = false;
                PiPPlayPauseBtn.Content = "▶";
            }
            else
            {
                _controller.Play();
                _isPlaying = true;
                PiPPlayPauseBtn.Content = "⏸";
            }
        }

        private void PiPPrevBtn_Click(object sender, RoutedEventArgs e)
            => WeakReferenceMessenger.Default.Send(new VideoControlMessage(VideoControlAction.Previous));

        private void PiPNextBtn_Click(object sender, RoutedEventArgs e)
            => WeakReferenceMessenger.Default.Send(new VideoControlMessage(VideoControlAction.Next));

        private void PiPVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_controller == null) return;
            _controller.Volume = e.NewValue;
        }

        private void PiPRateCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_controller == null) return;
            if (PiPRateCombo.SelectedItem is double d) _controller.Speed = d;
        }

        private void PiPRestoreBtn_Click(object sender, RoutedEventArgs e)
            => RestoreRequested?.Invoke(this, EventArgs.Empty);

        private void PiPBtnResume_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingResumePositionSeconds.HasValue && _pendingResumePartId.HasValue && _controller != null)
            {
                _controller.Seek(TimeSpan.FromSeconds(_pendingResumePositionSeconds.Value));
                _controller.Play();
                _isPlaying = true;
                PiPPlayPauseBtn.Content = "⏸";
            }
            PiPResumeBanner.Visibility = Visibility.Collapsed;
            _pendingResumePositionSeconds = null; _pendingResumePartId = null; _pendingResumeCourseRoot = null;
        }

        private void PiPBtnClearResume_Click(object sender, RoutedEventArgs e)
        {
            PiPResumeBanner.Visibility = Visibility.Collapsed;
            _pendingResumePositionSeconds = null; _pendingResumePartId = null; _pendingResumeCourseRoot = null;
            if (!string.IsNullOrWhiteSpace(_pendingResumeCourseRoot))
                WeakReferenceMessenger.Default.Send(new ClearResumeMessage(_pendingResumeCourseRoot));
        }

        private void PiPSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_controller == null) return;
            _wasPlayingBeforeSeek = _isPlaying;
            if (_isPlaying) _controller.Pause();
        }

        private void PiPSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_controller == null) return;
            var posSec = (int)Math.Round(PiPVideoSlider.Value);
            _controller.Seek(TimeSpan.FromSeconds(posSec));
            if (_wasPlayingBeforeSeek) _controller.Play();

            // notify main/position manager via messenger
            WeakReferenceMessenger.Default.Send(new PlaybackStateMessage(
                PiPPlayer.Source?.ToString() ?? string.Empty,
                _pendingResumePartId,
                posSec,
                _controller.Speed,
                _controller.Volume,
                _isPlaying));
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _uiTimer.Stop();
            ClosedByUser?.Invoke(this, EventArgs.Empty);
        }

        private void OnResumeAvailable(ResumeAvailableMessage msg)
        {
            Dispatcher.Invoke(() =>
            {
                _pendingResumePartId = msg.PartId;
                _pendingResumePositionSeconds = msg.PositionSeconds;
                _pendingResumeCourseRoot = msg.CourseRootPath;

                PiPResumeText.Text = $"Продолжить с {FormatTime(msg.PositionSeconds)}?";
                PiPResumeBanner.Visibility = Visibility.Visible;
            });
        }

        // click video to toggle play/pause
        private void PiPPlayerControl_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_controller == null) return;
            if (_isPlaying) _controller.Pause();
            else _controller.Play();

            _isPlaying = !_isPlaying;
            PiPPlayPauseBtn.Content = _isPlaying ? "⏸" : "▶";
        }

        // adjust overlay margins similar to MainWindow
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => AdjustOverlaySize();

        private void AdjustOverlaySize()
        {
            if (_controller == null || !_controller.HasNaturalVideoSize) return;
            double containerWidth = RootGrid.ActualWidth;
            double containerHeight = RootGrid.ActualHeight;
            double videoAspect = (double)_controller.NaturalVideoWidth / _controller.NaturalVideoHeight;
            double containerAspect = containerWidth / containerHeight;

            double horizontalMargin = 0;
            double verticalMargin = 0;
            double actualVideoWidth, actualVideoHeight;

            if (videoAspect > containerAspect)
            {
                actualVideoWidth = containerWidth;
                actualVideoHeight = containerWidth / videoAspect;
                verticalMargin = (containerHeight - actualVideoHeight) / 2;
            }
            else
            {
                actualVideoHeight = containerHeight;
                actualVideoWidth = containerHeight * videoAspect;
                horizontalMargin = (containerWidth - actualVideoWidth) / 2;
            }

            try
            {
                var rd = RootGrid.RowDefinitions;
                if (rd.Count >= 3)
                {
                    // verticalMargin округлён в пикселях; минимально 0
                    var gap = Math.Max(0, verticalMargin);
                    rd[1].Height = new GridLength(gap, GridUnitType.Pixel);
                }
            }
            catch { }

            // и дополнительно установим небольшие отступы по горизонтали
            PiPBottomControls.Margin = new Thickness(horizontalMargin, 6, horizontalMargin, 8);
            PiPResumeBanner.Margin = new Thickness(0, Math.Max(6, 6 + verticalMargin), 6 + horizontalMargin, 0);
        }

        private void Grid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }
    }

    // minimal ClearResumeMessage record if you don't have one yet
    public record ClearResumeMessage(string CourseRootPath);
}
