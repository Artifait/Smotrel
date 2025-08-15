using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Smotrel.Controllers;
using CommunityToolkit.Mvvm.Messaging;
using Smotrel.Messages;

namespace Smotrel.Views
{
    public partial class PiPWindow : Window
    {
        private PlayerController? _controller;
        private bool _isPlaying = false;
        private bool _wasPlayingBeforeSeek = false;
        private readonly DispatcherTimer _uiTimer;

        // expose MediaElement to controllers
        public System.Windows.Controls.MediaElement PiPPlayer => PiPPlayerControl;

        // events for orchestration
        public event EventHandler? RestoreRequested;
        public event EventHandler? ClosedByUser;

        // cached resume info (set via OnResumeAvailable through message)
        private long? _pendingResumePositionSeconds = null;
        private Guid? _pendingResumePartId = null;
        private string? _pendingResumeCourseRoot = null;

        public PiPWindow()
        {
            InitializeComponent();

            // populate rates
            PiPRateCombo.ItemsSource = new double[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };
            PiPRateCombo.SelectedItem = 1.0;

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _uiTimer.Tick += (s, e) => UpdateTimeAndSliderFromController();

            // Listen for resume messages (same message type as main window)
            WeakReferenceMessenger.Default.Register<ResumeAvailableMessage>(this, (_, msg) => OnResumeAvailable(msg));
        }

        public void AttachController(PlayerController controller)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            _controller = controller;

            // update UI from controller events
            _controller.PositionChanged += Controller_PositionChanged;
            _controller.MediaOpened += Controller_MediaOpened;
            _controller.MediaEnded += Controller_MediaEnded;

            // make sure UI sliders reflect controller values
            try
            {
                PiPVolumeSlider.Value = _controller.Volume;
                PiPRateCombo.SelectedItem = _controller.Speed;
            }
            catch { }

            _uiTimer.Start();
        }

        private void Controller_MediaEnded(object? sender, EventArgs e)
        {
            _isPlaying = false;
            Dispatcher.Invoke(() => PiPPlayPauseBtn.Content = "▶");
        }

        private void Controller_MediaOpened(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // update slider maximum if media has natural duration
                if (PiPPlayer.NaturalDuration.HasTimeSpan)
                {
                    PiPVideoSlider.Minimum = 0;
                    PiPVideoSlider.Maximum = PiPPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                }
            });
        }

        private void Controller_PositionChanged(object? sender, TimeSpan pos)
        {
            // invoked on timer thread in PlayerController; marshal to UI via dispatcher if needed
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
            else
                total = 0;

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
        {
            WeakReferenceMessenger.Default.Send(new VideoControlMessage(VideoControlAction.Previous));
        }

        private void PiPNextBtn_Click(object sender, RoutedEventArgs e)
        {
            WeakReferenceMessenger.Default.Send(new VideoControlMessage(VideoControlAction.Next));
        }

        private void PiPVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_controller == null) return;
            _controller.Volume = e.NewValue;
        }

        private void PiPRestoreBtn_Click(object sender, RoutedEventArgs e)
        {
            // request restore (PiP closed and state returned to main)
            RestoreRequested?.Invoke(this, EventArgs.Empty);
        }

        private void PiPBtnResume_Click(object sender, RoutedEventArgs e)
        {
            // use cached resume info if available
            if (_pendingResumePositionSeconds.HasValue && _pendingResumePartId.HasValue)
            {
                var pos = TimeSpan.FromSeconds(_pendingResumePositionSeconds.Value);
                if (_controller != null)
                {
                    _controller.Seek(pos);
                    _controller.Play();
                    _isPlaying = true;
                    PiPPlayPauseBtn.Content = "⏸";
                }
            }
            // hide banner after applying
            PiPResumeBanner.Visibility = Visibility.Collapsed;
            _pendingResumePositionSeconds = null;
            _pendingResumePartId = null;
            _pendingResumeCourseRoot = null;
        }

        private void PiPBtnClearResume_Click(object sender, RoutedEventArgs e)
        {
            PiPResumeBanner.Visibility = Visibility.Collapsed;
            _pendingResumePositionSeconds = null;
            _pendingResumePartId = null;
            _pendingResumeCourseRoot = null;

            // optionally notify main to clear persisted resume (use messenger)
            if (!string.IsNullOrWhiteSpace(_pendingResumeCourseRoot))
            {
                // best-effort: send clear request (main or playback service should handle)
                WeakReferenceMessenger.Default.Send(new ClearResumeMessage(_pendingResumeCourseRoot));
            }
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

            // notify via messenger so position can be saved (main or PipController can listen)
            if (_controller != null)
            {
                WeakReferenceMessenger.Default.Send(new PlaybackStateMessage(
                    PiPPlayer.Source?.ToString() ?? string.Empty,
                    _pendingResumePartId, // may be null
                    posSec,
                    _controller.Speed,
                    _controller.Volume,
                    _isPlaying));
            }
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
                // Only show banner if matching selected part (some callers include PartId)
                // We don't have SelectedVideo information here, so accept message if part matches current pending or if file path equals
                _pendingResumePartId = msg.PartId;
                _pendingResumePositionSeconds = msg.PositionSeconds;
                _pendingResumeCourseRoot = msg.CourseRootPath;

                PiPResumeText.Text = $"Продолжить с {FormatTime(msg.PositionSeconds)}?";
                PiPResumeBanner.Visibility = Visibility.Visible;
            });
        }
    }

    // Minimal ClearResumeMessage definition if not present in your project — remove if already exists.
    public record ClearResumeMessage(string CourseRootPath);
}
